using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CaptionScribe.Models;
using CaptionScribe.Core.Logging;
using CaptionScribe.Core.Mvvm;
using CaptionScribe.Services;

namespace CaptionScribe.ViewModels
{
    /// <summary>Editable view of the settings with live range validation.</summary>
    public sealed class SettingsViewModel : ObservableObject, INotifyDataErrorInfo
    {
        private readonly AppSettings _settings;
        private readonly IDialogService _dialogs;
        private readonly Func<string?> _detectTeamsTitle;
        private readonly IStartupLaunchService _startup;
        private readonly ILog _log;
        private readonly Dictionary<string, string> _errors = new();
        private readonly bool _startupEnabledAtOpen;

        private string _captureInterval;
        private string _autoSaveInterval;
        private string _autoSaveDir;
        private string _defaultSaveDir;
        private string _upscale;
        private string _threshold;
        private string _hint;
        private string _settle;
        private bool _focusSwitch;
        private bool _requireTeams;
        private bool _enhanceForOcr;
        private bool _showAllOutput;
        private bool _showTimestamps;
        private bool _timestampsPerTurn;
        private bool _enableDebugLogging;
        private string _autoSavePromptThreshold;
        private bool _autoDeleteOldAutoSaves;
        private bool _enableParticipantCapture;
        private bool _runOnStartup;

        /// <summary>Raised with the dialog result when the user saves; the view closes accordingly.</summary>
        public event EventHandler<bool>? CloseRequested;

        public SettingsViewModel(AppSettings settings, IDialogService dialogs, Func<string?> detectTeamsTitle,
            IStartupLaunchService startup, ILog log)
        {
            _settings = settings;
            _dialogs = dialogs;
            _detectTeamsTitle = detectTeamsTitle;
            _startup = startup;
            _log = log;

            _captureInterval = Str(settings.CaptureIntervalMs);
            _autoSaveInterval = Str(settings.AutoSaveIntervalMinutes);
            _autoSaveDir = settings.AutoSaveDirectory;
            _defaultSaveDir = settings.DefaultSaveDirectory;
            _upscale = Str(settings.UpscaleFactor);
            _threshold = settings.SimilarityThreshold.ToString(CultureInfo.InvariantCulture);
            _hint = settings.TeamsWindowTitleHint;
            _settle = Str(settings.FocusSettleMs);
            _focusSwitch = settings.FocusSwitchEnabled;
            _requireTeams = settings.RequireTeamsWindow;
            _enhanceForOcr = settings.EnhanceForOcr;
            _showAllOutput = settings.ShowAllOutput;
            _showTimestamps = settings.ShowTimestamps;
            _timestampsPerTurn = settings.TimestampsPerTurn;
            _enableDebugLogging = settings.EnableDebugLogging;
            _autoSavePromptThreshold = Str(settings.AutoSavePromptThreshold);
            _autoDeleteOldAutoSaves = settings.AutoDeleteOldAutoSaves;
            _enableParticipantCapture = settings.EnableParticipantCapture;
            _startupEnabledAtOpen = startup.IsEnabled();
            _runOnStartup = _startupEnabledAtOpen;

            BrowseAutoSaveCommand = new RelayCommand(BrowseAutoSave);
            BrowseDefaultSaveCommand = new RelayCommand(BrowseDefaultSave);
            ClearAutoSaveCommand = new RelayCommand(ClearAutoSave);
            DetectTeamsTitleCommand = new RelayCommand(DetectTeamsTitle);
            SaveCommand = new RelayCommand(Save, () => IsDirty && !HasErrors);

            ValidateAll();
        }

        public ICommand BrowseAutoSaveCommand { get; }
        public ICommand BrowseDefaultSaveCommand { get; }
        public ICommand ClearAutoSaveCommand { get; }
        public ICommand DetectTeamsTitleCommand { get; }
        public ICommand SaveCommand { get; }

        public string CaptureInterval
        {
            get => _captureInterval;
            set { if (Set(ref _captureInterval, value)) ValidateInt(value, 200, 60000, nameof(CaptureInterval)); }
        }
        public string AutoSaveInterval
        {
            get => _autoSaveInterval;
            set { if (Set(ref _autoSaveInterval, value)) ValidateInt(value, 1, 60, nameof(AutoSaveInterval)); }
        }
        public string Upscale
        {
            get => _upscale;
            set { if (Set(ref _upscale, value)) ValidateInt(value, 1, 4, nameof(Upscale)); }
        }
        public string Threshold
        {
            get => _threshold;
            set { if (Set(ref _threshold, value)) ValidateDouble(value, 0.0, 1.0, nameof(Threshold)); }
        }
        public string Settle
        {
            get => _settle;
            set { if (Set(ref _settle, value)) ValidateInt(value, 0, 5000, nameof(Settle)); }
        }
        public string Hint { get => _hint; set => Set(ref _hint, value); }
        public string AutoSaveDir
        {
            get => _autoSaveDir;
            set { if (Set(ref _autoSaveDir, value)) OnPropertyChanged(nameof(AutoSaveLocation)); }
        }
        public string DefaultSaveDir { get => _defaultSaveDir; set => Set(ref _defaultSaveDir, value); }

        /// <summary>The folder autosaves are actually written to (configured folder, or the default).</summary>
        public string AutoSaveLocation => TranscriptAutoSaver.DirectoryFor(AutoSaveDir);

        public bool FocusSwitch { get => _focusSwitch; set => Set(ref _focusSwitch, value); }
        public bool RequireTeams { get => _requireTeams; set => Set(ref _requireTeams, value); }
        public bool EnhanceForOcr { get => _enhanceForOcr; set => Set(ref _enhanceForOcr, value); }
        public bool ShowAllOutput { get => _showAllOutput; set => Set(ref _showAllOutput, value); }
        public bool ShowTimestamps { get => _showTimestamps; set => Set(ref _showTimestamps, value); }
        public bool TimestampsPerTurn { get => _timestampsPerTurn; set => Set(ref _timestampsPerTurn, value); }
        public bool EnableDebugLogging { get => _enableDebugLogging; set => Set(ref _enableDebugLogging, value); }
        public string AutoSavePromptThreshold
        {
            get => _autoSavePromptThreshold;
            set { if (Set(ref _autoSavePromptThreshold, value)) ValidateInt(value, 1, 1000000, nameof(AutoSavePromptThreshold)); }
        }
        public bool AutoDeleteOldAutoSaves { get => _autoDeleteOldAutoSaves; set => Set(ref _autoDeleteOldAutoSaves, value); }
        public bool EnableParticipantCapture { get => _enableParticipantCapture; set => Set(ref _enableParticipantCapture, value); }
        public bool RunOnStartup { get => _runOnStartup; set => Set(ref _runOnStartup, value); }

        // True when any field differs from the saved settings (Save stays disabled until then).
        private bool IsDirty =>
            _captureInterval != Str(_settings.CaptureIntervalMs)
            || _autoSaveInterval != Str(_settings.AutoSaveIntervalMinutes)
            || _autoSaveDir != _settings.AutoSaveDirectory
            || _defaultSaveDir != _settings.DefaultSaveDirectory
            || _upscale != Str(_settings.UpscaleFactor)
            || _threshold != _settings.SimilarityThreshold.ToString(CultureInfo.InvariantCulture)
            || _hint != _settings.TeamsWindowTitleHint
            || _settle != Str(_settings.FocusSettleMs)
            || _focusSwitch != _settings.FocusSwitchEnabled
            || _requireTeams != _settings.RequireTeamsWindow
            || _enhanceForOcr != _settings.EnhanceForOcr
            || _showAllOutput != _settings.ShowAllOutput
            || _showTimestamps != _settings.ShowTimestamps
            || _timestampsPerTurn != _settings.TimestampsPerTurn
            || _enableDebugLogging != _settings.EnableDebugLogging
            || _autoSavePromptThreshold != Str(_settings.AutoSavePromptThreshold)
            || _autoDeleteOldAutoSaves != _settings.AutoDeleteOldAutoSaves
            || _enableParticipantCapture != _settings.EnableParticipantCapture
            || _runOnStartup != _startupEnabledAtOpen;

        // ---- INotifyDataErrorInfo ----
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
        public bool HasErrors => _errors.Count > 0;

        private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private void BrowseAutoSave()
        {
            if (_dialogs.PickFolder(Blank(AutoSaveDir)) is { } picked)
                AutoSaveDir = picked;
        }

        private void BrowseDefaultSave()
        {
            if (_dialogs.PickFolder(Blank(DefaultSaveDir)) is { } picked)
                DefaultSaveDir = picked;
        }

        private void ClearAutoSave()
        {
            var dir = TranscriptAutoSaver.DirectoryFor(AutoSaveDir);
            if (!_dialogs.ConfirmYesNo("Clear autosave folder",
                    $"Delete all autosaved transcripts in:\n{dir}\n\nThis cannot be undone."))
                return;

            int removed = TranscriptAutoSaver.ClearDirectory(AutoSaveDir);
            _dialogs.Info("Autosave folder",
                removed == 0
                    ? "There were no autosaved transcripts to remove."
                    : $"Removed {removed} autosaved transcript{(removed == 1 ? "" : "s")}.");
        }

        // Fills the Teams title hint from the current Teams window; isolated, not gated by the focus-switch flag.
        private void DetectTeamsTitle()
        {
            var title = _detectTeamsTitle();
            if (string.IsNullOrWhiteSpace(title))
            {
                _dialogs.Info("Detect Teams window",
                    "No Teams window found. Start or join the meeting first, then try Detect again.");
                return;
            }
            Hint = title;
        }

        private static void EnsureDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try { Directory.CreateDirectory(dir); } catch { /* the app falls back if the folder is missing */ }
        }

        public IEnumerable GetErrors(string? propertyName)
            => propertyName is not null && _errors.TryGetValue(propertyName, out var msg)
                ? new[] { msg }
                : Array.Empty<string>();

        private static int Int(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

        private void Save()
        {
            try
            {
                if (RunOnStartup != _startupEnabledAtOpen)
                    _startup.SetEnabled(RunOnStartup);
            }
            catch (Exception ex)
            {
                _log.Error("Could not update the Windows startup setting.", ex);
                _dialogs.Info("Run on startup",
                    "Could not update the Windows startup setting. " + ex.Message);
                return;
            }

            _settings.CaptureIntervalMs = Int(CaptureInterval);
            _settings.AutoSaveIntervalMinutes = Int(AutoSaveInterval);
            _settings.UpscaleFactor = Int(Upscale);
            _settings.SimilarityThreshold = double.Parse(Threshold, NumberStyles.Float, CultureInfo.InvariantCulture);
            _settings.FocusSettleMs = Int(Settle);
            _settings.AutoSaveDirectory = AutoSaveDir.Trim();
            _settings.DefaultSaveDirectory = DefaultSaveDir.Trim();
            _settings.TeamsWindowTitleHint = Hint.Trim();
            _settings.FocusSwitchEnabled = FocusSwitch;
            _settings.RequireTeamsWindow = RequireTeams;
            _settings.EnhanceForOcr = EnhanceForOcr;
            _settings.ShowAllOutput = ShowAllOutput;
            _settings.ShowTimestamps = ShowTimestamps;
            _settings.TimestampsPerTurn = TimestampsPerTurn;
            _settings.EnableDebugLogging = EnableDebugLogging;
            _settings.AutoSavePromptThreshold = Int(AutoSavePromptThreshold);
            _settings.AutoDeleteOldAutoSaves = AutoDeleteOldAutoSaves;
            _settings.EnableParticipantCapture = EnableParticipantCapture;

            EnsureDir(_settings.AutoSaveDirectory);
            EnsureDir(_settings.DefaultSaveDirectory);

            CloseRequested?.Invoke(this, true);
        }

        // Set + re-evaluate command availability (Save also depends on IsDirty).
        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (!SetProperty(ref field, value, name))
                return false;
            CommandManager.InvalidateRequerySuggested();
            return true;
        }

        private void SetError(string propertyName, string? message)
        {
            if (message is null)
            {
                if (!_errors.Remove(propertyName)) return;
            }
            else
            {
                _errors[propertyName] = message;
            }

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
            CommandManager.InvalidateRequerySuggested();
        }

        private static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);

        private void ValidateAll()
        {
            ValidateInt(CaptureInterval, 200, 60000, nameof(CaptureInterval));
            ValidateInt(AutoSaveInterval, 1, 60, nameof(AutoSaveInterval));
            ValidateInt(Upscale, 1, 4, nameof(Upscale));
            ValidateDouble(Threshold, 0.0, 1.0, nameof(Threshold));
            ValidateInt(Settle, 0, 5000, nameof(Settle));
            ValidateInt(AutoSavePromptThreshold, 1, 1000000, nameof(AutoSavePromptThreshold));
        }

        private void ValidateDouble(string text, double min, double max, string propertyName)
        {
            bool ok = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= min && v <= max;
            SetError(propertyName, ok ? null : $"Enter a number between {min} and {max}.");
        }

        private void ValidateInt(string text, int min, int max, string propertyName)
        {
            bool ok = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= min && v <= max;
            SetError(propertyName, ok ? null : $"Enter a whole number between {min} and {max}.");
        }
    }
}
