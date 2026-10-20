using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using CaptionScribe.Models;
using CaptionScribe.Core.Mvvm;
using CaptionScribe.Services;

namespace CaptionScribe.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private const int MinUiRefreshMs = 250;
        private static readonly TimeSpan MeterTick = TimeSpan.FromMilliseconds(50);

        private readonly ICaptureController _controller;
        private readonly AppSettings _settings;
        private readonly ISettingsService _settingsService;
        private readonly IDialogService _dialogs;
        private readonly IRegionService _regions;
        private readonly INotificationService _notifications;
        private readonly IParticipantCollector _participants;
        private readonly TranscriptExportWorkflow _export;
        private readonly Dispatcher _dispatcher;

        private string _transcriptText = "";
        private string _statusText = "";
        private string _regionText = "";
        private bool _autoScroll = true;
        private int _refreshQueued;
        private bool _lastCapturing;
        private bool _lastHasContent;
        private bool _refreshInitialized;
        private bool _isBusy;
        private string _busyMessage = "";
        private Task _busyWork = Task.CompletedTask;
        private long _lastUiRefreshMs;
        private DispatcherTimer? _meterTimer;
        private float _audioLevel;

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
            _participants = participants;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _export = new TranscriptExportWorkflow(controller, dialogs, fileWriter, participants, settings, SetBusy, Refresh);

            NewScribeCommand = new RelayCommand(NewScribe, () => !IsCapturing && !IsBusy);
            SaveCommand = new RelayCommand(Save, () => HasContent && !IsBusy);
            ClearCommand = new RelayCommand(Clear, () => HasContent && !IsBusy);
            CopyCommand = new RelayCommand(Copy, () => HasContent && !IsBusy);
            PlayCommand = new RelayCommand(Play, () => !IsBusy && !_lastCapturing);
            PauseCommand = new RelayCommand(Pause, () => !IsBusy && _lastCapturing);
            PlayPauseCommand = new RelayCommand(ToggleActive, () => !IsBusy);
            StopCommand = new RelayCommand(StopAndSave, () => IsCapturing && !IsBusy);
            HighlightCommand = new RelayCommand(Highlight, () => !IsBusy);
            SelectRegionCommand = new RelayCommand(SelectRegion, () => !IsBusy);
            CaptureDiagnosticsCommand = new RelayCommand(ShowCaptureDiagnostics, () => !IsBusy);
            SettingsCommand = new RelayCommand(OpenSettings, () => !IsBusy);
            HelpCommand = new RelayCommand(() => _dialogs.ShowHelp(), () => !IsBusy);
            AboutCommand = new RelayCommand(() => _dialogs.ShowAbout(_controller.AutoSavePath), () => !IsBusy);
            ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);

            _controller.TranscriptUpdated += OnTranscriptUpdated;
            _controller.CaptureStateChanged += OnCaptureStateChanged;
            Refresh();
        }

        // ---- commands ----
        public ICommand NewScribeCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand PlayCommand { get; }
        public ICommand PauseCommand { get; }
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
        public bool IsAudioMeterVisible => IsCapturing && _settings.EnableCaptureAudio;
        public bool IsAudioMeterLive => IsCapturing && _controller.IsAudioCapturing;
        public float AudioLevel { get => _audioLevel; private set => SetProperty(ref _audioLevel, value); }
        public bool HasContent => _controller.TranscriptLineCount > 0;
        public bool ShowIdleOverlay => !IsCapturing && !HasContent && !IsBusy;
        public string PlayPauseToolTip => IsCapturing ? "Pause capturing" : "Start capturing";
        public bool IsBusy => _isBusy;
        public string BusyMessage => _busyMessage;
        public bool IsUiEnabled => !_isBusy;
        internal Task BusyWork => _busyWork;

        /// <summary>Two-way for the checkable "Active" menu item; toggling starts/stops capture.</summary>
        public bool IsActive
        {
            get => IsCapturing;
            set
            {
                if (_isBusy || value == IsCapturing)
                    return;
                if (value) Play();
                else Pause();
            }
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

        public bool EnableCaptureAudio
        {
            get => _settings.EnableCaptureAudio;
            set
            {
                if (value == _settings.EnableCaptureAudio) return;
                _settings.EnableCaptureAudio = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
                _controller.ApplySettings();
                OnPropertyChanged(nameof(IsAudioMeterVisible));
                Refresh();
            }
        }

        // ---- capture control ----
        public void ToggleActive()
        {
            if (_isBusy)
                return;
            if (_lastCapturing)
                Pause();
            else
                Play();
        }

        private void Play()
        {
            if (_isBusy)
                return;
            if (_settings.Region is null)
            {
                SelectRegion();
                if (_settings.Region is null) { Refresh(); return; }
            }
            _controller.Start();
            if (_settings.EnableParticipantCapture)
                _participants.Start();
            Refresh();
        }

        private void Pause()
        {
            if (_isBusy)
                return;
            _controller.Stop();
            _participants.Stop();
            Refresh();
        }

        private void StopAndSave()
        {
            if (_isBusy)
                return;
            _busyWork = StopAndSaveAsync();
        }

        private async Task StopAndSaveAsync()
        {
            if (_isBusy)
                return;
            if (IsCapturing)
            {
                _controller.Stop();
                _participants.Stop();
            }
            Refresh();
            if (!HasContent)
                return;
            if (await SaveAsync() && !_controller.IsRunning)
            {
                _controller.ClearTranscript();
                _participants.Reset();
                Refresh();
            }
        }

        private void NewScribe()
        {
            if (_isBusy)
                return;
            ResetTranscript(
                _dialogs.ConfirmOkCancel("New Scribe",
                    "Start a new scribe? The current transcript will be cleared.\n" +
                    "(The previous transcript remains in the autosave folder.)"));
        }

        private void Clear()
        {
            if (_isBusy)
                return;
            ResetTranscript(
                _dialogs.ConfirmYesNo("Clear",
                    "Are you sure you want to clear the current transcript?\n" +
                    "(The previous transcript remains in the autosave folder.)"));
        }

        private void ResetTranscript(bool confirmed)
        {
            if (!confirmed)
                return;
            _controller.ClearTranscript();
            _participants.Reset();
        }

        private void Copy()
        {
            if (_isBusy)
                return;
            _busyWork = _export.CopyAsync();
        }

        private void Save()
        {
            if (_isBusy)
                return;
            _busyWork = SaveAsync();
        }

        internal Task<bool> SaveAsync() => _export.SaveAsync();

        private void SetBusy(bool busy, string message = "")
        {
            _isBusy = busy;
            _busyMessage = busy ? message : "";
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(BusyMessage));
            OnPropertyChanged(nameof(IsUiEnabled));
            OnPropertyChanged(nameof(ShowIdleOverlay));
            if (busy)
                StatusText = message;
            CommandManager.InvalidateRequerySuggested();
        }

        private void Highlight()
        {
            if (_isBusy)
                return;
            if (_settings.Region is CaptureRegion region)
                _regions.HighlightRegion(region);
            else
                _notifications.Info("No capture region is set yet — pick one first.");
        }

        private void SelectRegion()
        {
            if (_isBusy)
                return;
            if (_regions.SelectRegion() is not CaptureRegion region)
                return;

            _settings.Region = region;
            _settingsService.Save(_settings);
            _controller.UpdateRegion(region);
            _notifications.Info($"Region set ({region.Width}x{region.Height}). Toggle 'Active' to begin.");
            Refresh();
        }

        private void ShowCaptureDiagnostics()
        {
            if (_isBusy)
                return;
            _dialogs.Info("Caption Scribe — Capture Diagnostics", _controller.GetCaptureDiagnostics());
        }

        private void OpenSettings()
        {
            if (_isBusy)
                return;
            if (!_dialogs.ShowSettings(_settings))
                return;

            if (!_settingsService.Save(_settings))
                _dialogs.Info("Settings", "Settings were applied but could not be saved to disk.");
            _controller.ApplySettings();
            SyncParticipantCapture();
            OnPropertyChanged(nameof(ShowAllOutput));
            OnPropertyChanged(nameof(ShowTimestamps));
            OnPropertyChanged(nameof(EnableParticipantCapture));
            OnPropertyChanged(nameof(EnableCaptureAudio));
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

        // Marshal off the capture thread; skip during shutdown. Coalesce bursts onto one UI pass.
        private void RefreshOnUi()
        {
            if (_dispatcher.HasShutdownStarted)
                return;
            if (Interlocked.Exchange(ref _refreshQueued, 1) != 0)
                return;
            _dispatcher.InvokeAsync(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (_controller.IsRunning)
                {
                    long now = Environment.TickCount64;
                    if (now - _lastUiRefreshMs < MinUiRefreshMs)
                        return;
                    _lastUiRefreshMs = now;
                }
                Refresh();
            });
        }

        private void Refresh()
        {
            if (_isBusy)
                return;
            TranscriptText = BuildDisplayText();
            StatusText = BuildStatusText();
            RegionText = _settings.Region is CaptureRegion r
                ? $"Region: {r.Width}×{r.Height} @ ({r.X}, {r.Y})"
                : "Region: not set";
            EnsureMeterTimer();
            OnPropertyChanged(nameof(IsAudioMeterVisible));
            OnPropertyChanged(nameof(IsAudioMeterLive));

            bool capturing = _controller.IsRunning;
            bool hasContent = _controller.TranscriptLineCount > 0;
            bool stateChanged = capturing != _lastCapturing || hasContent != _lastHasContent;
            _lastCapturing = capturing;
            _lastHasContent = hasContent;
            if (!stateChanged && _refreshInitialized)
                return;
            _refreshInitialized = true;

            OnPropertyChanged(nameof(IsCapturing));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(ShowIdleOverlay));
            OnPropertyChanged(nameof(PlayPauseToolTip));
            CommandManager.InvalidateRequerySuggested();
        }

        private string BuildStatusText()
        {
            if (!_controller.IsRunning)
            {
                return _settings.Region is null
                    ? "No capture region — Settings ▸ Select Capture Region"
                    : "Idle";
            }
            if (_controller.IsAudioCapturing)
                return "Capturing captions and audio… Press Spacebar to Pause.";
            if (_settings.EnableCaptureAudio && _controller.AudioFailed)
                return "Capturing… Audio unavailable.";
            return "Capturing… Press Spacebar to Pause.";
        }

        private void EnsureMeterTimer()
        {
            bool want = IsCapturing && _settings.EnableCaptureAudio;
            if (want)
            {
                if (_meterTimer is null)
                {
                    _meterTimer = new DispatcherTimer { Interval = MeterTick };
                    _meterTimer.Tick += OnMeterTick;
                }
                if (!_meterTimer.IsEnabled)
                    _meterTimer.Start();
                return;
            }
            _meterTimer?.Stop();
            if (_audioLevel != 0)
                AudioLevel = 0;
        }

        private void OnMeterTick(object? sender, EventArgs e)
        {
            bool live = _controller.IsRunning && _controller.IsAudioCapturing;
            float level = live ? _controller.AudioLevel : 0;
            if (Math.Abs(level - _audioLevel) > 0.02f)
                AudioLevel = level;
            string status = BuildStatusText();
            if (status != StatusText)
                StatusText = status;
            OnPropertyChanged(nameof(IsAudioMeterLive));
        }

        private string BuildDisplayText()
        {
            bool capturing = _controller.IsRunning;
            int cap = TranscriptDisplayText.LineCap(capturing, _settings.ShowAllOutput);
            return TranscriptDisplayText.Build(
                capturing,
                _settings.ShowAllOutput,
                _settings.ShowTimestamps,
                _settings.TimestampsPerTurn,
                _controller.TranscriptLineCount,
                _controller.GetTranscriptTailText(cap),
                _controller.GetTimedTail(cap));
        }

    }
}
