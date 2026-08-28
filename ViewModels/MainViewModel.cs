using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using CaptionScribe.Models;
using CaptionScribe.Core.Mvvm;
using CaptionScribe.Services;

namespace CaptionScribe.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private const int TailLineCount = 200;

        // Show All Output cap (~10k lines ≈ 5h dense / ~8h at the measured rate). Full transcript is still saved/copied.
        private const int AllOutputLineCount = 10_000;

        private readonly ICaptureController _controller;
        private readonly AppSettings _settings;
        private readonly ISettingsService _settingsService;
        private readonly IDialogService _dialogs;
        private readonly IRegionService _regions;
        private readonly INotificationService _notifications;
        private readonly ITextFileWriter _fileWriter;
        private readonly IParticipantCollector _participants;
        private readonly Dispatcher _dispatcher;

        private string _transcriptText = "";
        private string _statusText = "";
        private string _regionText = "";
        private bool _autoScroll = true;

        /// <summary>Raised when the user asks to exit; the shell (App) performs the shutdown.</summary>
        public event EventHandler? ExitRequested;

        public MainViewModel(ICaptureController controller, AppSettings settings, ISettingsService settingsService,
            IDialogService dialogs, IRegionService regions, INotificationService notifications,
            ITextFileWriter fileWriter, IParticipantCollector participants)
        {
            _controller = controller;
            _settings = settings;
            _settingsService = settingsService;
            _dialogs = dialogs;
            _regions = regions;
            _notifications = notifications;
            _fileWriter = fileWriter;
            _participants = participants;
            _dispatcher = Dispatcher.CurrentDispatcher;

            NewScribeCommand = new RelayCommand(NewScribe, () => !IsCapturing);
            SaveCommand = new RelayCommand(() => Save(), () => HasContent);
            ClearCommand = new RelayCommand(Clear, () => HasContent);
            CopyCommand = new RelayCommand(Copy, () => HasContent);
            PlayPauseCommand = new RelayCommand(ToggleActive);
            StopCommand = new RelayCommand(StopAndSave, () => IsCapturing);
            HighlightCommand = new RelayCommand(Highlight);
            SelectRegionCommand = new RelayCommand(SelectRegion);
            CaptureDiagnosticsCommand = new RelayCommand(ShowCaptureDiagnostics);
            SettingsCommand = new RelayCommand(OpenSettings);
            HelpCommand = new RelayCommand(() => _dialogs.ShowHelp());
            AboutCommand = new RelayCommand(() => _dialogs.ShowAbout(_controller.AutoSavePath));
            ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));

            _controller.TranscriptUpdated += OnTranscriptUpdated;
            _controller.CaptureStateChanged += OnCaptureStateChanged;
            Refresh();
        }

        // ---- commands ----
        public ICommand NewScribeCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand HighlightCommand { get; }
        public ICommand SelectRegionCommand { get; }
        public ICommand CaptureDiagnosticsCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand HelpCommand { get; }
        public ICommand AboutCommand { get; }
        public ICommand ExitCommand { get; }

        // ---- observable state ----
        public string TranscriptText { get => _transcriptText; private set => SetProperty(ref _transcriptText, value); }
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
        public string RegionText { get => _regionText; private set => SetProperty(ref _regionText, value); }
        public bool AutoScroll { get => _autoScroll; set => SetProperty(ref _autoScroll, value); }

        public bool IsCapturing => _controller.IsRunning;
        public bool HasContent => _controller.TranscriptLineCount > 0;
        public bool ShowIdleOverlay => !IsCapturing && !HasContent;
        public string PlayPauseToolTip => IsCapturing ? "Pause capturing" : "Start capturing";

        /// <summary>Two-way for the checkable "Active" menu item; toggling starts/stops capture.</summary>
        public bool IsActive
        {
            get => IsCapturing;
            set { if (value != IsCapturing) ToggleActive(); }
        }

        public bool ShowAllOutput
        {
            get => _settings.ShowAllOutput;
            set
            {
                if (value == _settings.ShowAllOutput) return;
                _settings.ShowAllOutput = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
                Refresh();
            }
        }

        public bool ShowTimestamps
        {
            get => _settings.ShowTimestamps;
            set
            {
                if (value == _settings.ShowTimestamps) return;
                _settings.ShowTimestamps = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
                Refresh();
            }
        }

        public bool EnableParticipantCapture
        {
            get => _settings.EnableParticipantCapture;
            set
            {
                if (value == _settings.EnableParticipantCapture) return;
                _settings.EnableParticipantCapture = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
                SyncParticipantCapture();
            }
        }

        // ---- capture control ----
        public void ToggleActive()
        {
            if (!IsCapturing)
            {
                if (_settings.Region is null)
                {
                    SelectRegion();
                    if (_settings.Region is null) { Refresh(); return; }
                }
                _controller.Start();
                if (_settings.EnableParticipantCapture)
                    _participants.Start();
            }
            else
            {
                _controller.Stop();
                _participants.Stop();
            }
            Refresh();
        }

        private void StopAndSave()
        {
            if (IsCapturing)
            {
                _controller.Stop();
                _participants.Stop();
            }
            Refresh();
            // Stop = finish this scribe: after a successful save, clear the window for the next one.
            if (HasContent && Save())
            {
                _controller.ClearTranscript();
                _participants.Reset();
                Refresh();
            }
        }

        private void NewScribe()
        {
            if (_dialogs.ConfirmOkCancel("New Scribe",
                "Start a new scribe? The current transcript will be cleared.\n" +
                "(The previous transcript remains in the autosave folder.)"))
            {
                _controller.ClearTranscript();
                _participants.Reset();
            }
        }

        private void Clear()
        {
            if (_dialogs.ConfirmYesNo("Clear",
                "Are you sure you want to clear the current transcript?\n" +
                "(The previous transcript remains in the autosave folder.)"))
            {
                _controller.ClearTranscript();
                _participants.Reset();
            }
        }

        private void Copy()
        {
            var text = _controller.GetTranscriptText();
            if (!string.IsNullOrEmpty(text))
                _dialogs.CopyToClipboard(text);
        }

        private bool Save()
        {
            var cleanup = _dialogs.AskSaveCleanup();
            if (cleanup is null)
                return false;

            var title = _dialogs.PromptMeetingTitle();
            if (title is null)
                return false;

            // Meeting start time; ':' is illegal in file names, so HH-mm is used.
            var started = _controller.SessionStartedAt ?? DateTime.Now;
            var stamp = started.ToString("yyyy-MM-dd-'Meeting'-HH-mm");
            var safe = SanitizeFileName(title.Trim());
            var suggested = string.IsNullOrWhiteSpace(safe) ? $"{stamp}.txt" : $"{stamp}-{safe}.txt";

            var path = _dialogs.PickSaveFile(suggested, _settings.DefaultSaveDirectory);
            if (path is null)
                return false;

            var text = BuildTranscriptToSave(cleanup.Value);
            try
            {
                _fileWriter.Write(path, text);
            }
            catch (Exception ex)
            {
                _dialogs.Info("Save failed", ex.Message);
                return false;
            }

            TryWriteParticipantsImage(path, title.Trim(), started);
            return true;
        }

        // Renders the transcript for saving: cleaned-up, timestamped, or plain per the current settings.
        private string BuildTranscriptToSave(SaveCleanup cleanup)
        {
            if (cleanup == SaveCleanup.Clean)
                return TranscriptCleaner.Clean(_controller.GetTimedTranscript(), _settings.ShowTimestamps, _settings.TimestampsPerTurn);
            if (_settings.ShowTimestamps)
                return TranscriptFormatter.Format(_controller.GetTimedTranscript(), true, _settings.TimestampsPerTurn);
            return _controller.GetTranscriptText();
        }

        // POC: emit a participants PNG (avatars + names) next to the saved transcript.
        private void TryWriteParticipantsImage(string transcriptPath, string meetingTitle, DateTime started)
        {
            if (!_settings.EnableParticipantCapture || _participants.Count == 0)
                return;
            try
            {
                int count = _participants.Count;
                var pngPath = Path.ChangeExtension(transcriptPath, null) + "-Participants.png";
                var header = $"{meetingTitle} — {started:yyyy-MM-dd} — {started:HH:mm}\n" +
                             $"{count} participant{(count == 1 ? "" : "s")}";
                _participants.WriteImage(pngPath, header);
            }
            catch (Exception ex)
            {
                _dialogs.Info("Participants image", "Could not write the participants image: " + ex.Message);
            }
        }

        private void Highlight()
        {
            if (_settings.Region is CaptureRegion region)
                _regions.HighlightRegion(region);
            else
                _notifications.Info("No capture region is set yet — pick one first.");
        }

        private void SelectRegion()
        {
            if (_regions.SelectRegion() is not CaptureRegion region)
                return;

            _settings.Region = region;
            _settingsService.Save(_settings);
            _controller.UpdateRegion(region);
            _notifications.Info($"Region set ({region.Width}x{region.Height}). Toggle 'Active' to begin.");
            Refresh();
        }

        private void ShowCaptureDiagnostics()
            => _dialogs.Info("Caption Scribe — Capture Diagnostics", _controller.GetCaptureDiagnostics());

        private void OpenSettings()
        {
            if (!_dialogs.ShowSettings(_settings))
                return;

            _settingsService.Save(_settings);
            _controller.ApplySettings();
            SyncParticipantCapture();
            OnPropertyChanged(nameof(ShowAllOutput));
            OnPropertyChanged(nameof(ShowTimestamps));
            OnPropertyChanged(nameof(EnableParticipantCapture));
            Refresh();
        }

        // Reflect the participant-capture setting immediately, even mid-capture.
        private void SyncParticipantCapture()
        {
            if (!IsCapturing)
                return;
            if (_settings.EnableParticipantCapture)
                _participants.Start();
            else
                _participants.Stop();
        }

        // ---- refresh ----
        private void OnTranscriptUpdated(object? sender, EventArgs e) => RefreshOnUi();

        private void OnCaptureStateChanged(object? sender, EventArgs e) => RefreshOnUi();

        // Marshal off the capture thread; skip during shutdown.
        private void RefreshOnUi()
        {
            if (!_dispatcher.HasShutdownStarted)
                _dispatcher.InvokeAsync(Refresh);
        }

        private void Refresh()
        {
            TranscriptText = BuildDisplayText();
            StatusText = _controller.IsRunning
                ? "Capturing… Press Spacebar to Pause."
                : _settings.Region is null
                    ? "No capture region — Settings ▸ Select Capture Region"
                    : "Idle";
            RegionText = _settings.Region is CaptureRegion r
                ? $"Region: {r.Width}×{r.Height} @ ({r.X}, {r.Y})"
                : "Region: not set";

            // Computed from controller state, so raise them explicitly and re-query command availability.
            OnPropertyChanged(nameof(IsCapturing));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(ShowIdleOverlay));
            OnPropertyChanged(nameof(PlayPauseToolTip));
            CommandManager.InvalidateRequerySuggested();
        }

        private string BuildDisplayText()
        {
            // Show All Output raises the on-screen cap; the full transcript is always saved/copied in full.
            int cap = _settings.ShowAllOutput ? AllOutputLineCount : TailLineCount;
            bool timestamps = _settings.ShowTimestamps;

            string tail = timestamps
                ? TranscriptFormatter.Format(_controller.GetTimedTail(cap), true, _settings.TimestampsPerTurn)
                : _controller.GetTranscriptTailText(cap);

            if (_controller.TranscriptLineCount <= cap)
                return tail;

            return $"… showing the last {cap} lines (the full transcript is captured and will be saved) …"
                   + Environment.NewLine + tail;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return name.Trim();
        }
    }
}
