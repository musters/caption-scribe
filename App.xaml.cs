using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CaptionScribe.Core.Interop;
using CaptionScribe.Core.Logging;
using CaptionScribe.Models;
using CaptionScribe.Services;
using CaptionScribe.Core.Shell;
using CaptionScribe.ViewModels;
using CaptionScribe.Views;
using WinForms = System.Windows.Forms;

namespace CaptionScribe
{
    public partial class App : Application, INotificationService
    {
        private CaptureController _controller = null!;
        private SettingsService _settingsService = null!;
        private AppSettings _settings = null!;
        private MainWindow _mainWindow = null!;
        private MainViewModel _viewModel = null!;
        private RegionService _regionService = null!;
        private WpfDialogService _dialogs = null!;
        private ParticipantCollector _participants = null!;
        private TrayIcon _tray = null!;
        private ILog _log = null!;
        private Mutex? _instanceMutex;
        private EventWaitHandle? _showWindowEvent;
        private RegisteredWaitHandle? _showWindowWait;
        private volatile bool _exiting;
        private int _handlesReleased;

        private const string InstanceMutexName = @"Local\CaptionScribe";
        private const string ShowWindowEventName = @"Local\CaptionScribe.ShowWindow";
        private const int UnregisterWaitMs = 500;

        // Set when Windows is logging off/shutting down, so exit never blocks the OS.
        private bool _systemShutdown;

        private bool DebugLoggingEnabled() => _settings is { EnableDebugLogging: true };

        private void ExitApp()
        {
            // Warn before discarding an in-progress capture — but never block an OS-driven shutdown.
            if (!_systemShutdown && _controller.IsRunning &&
                !_dialogs.ConfirmYesNo("Exit Caption Scribe",
                    "Capture is still active. Exiting won't save the transcript to a file — it will remain " +
                    "only in the autosave folder:\n\n" +
                    TranscriptAutoSaver.DirectoryFor(_settings.AutoSaveDirectory) +
                    "\n\nExit anyway?"))
                return;

            _controller.Stop();
            _mainWindow.AllowClose = true;
            Shutdown();
        }

        private static bool HasStartupArgument(string[] args)
            => Array.Exists(args, a =>
                string.Equals(a, StartupLaunchService.StartupArgument, StringComparison.OrdinalIgnoreCase));

        // ---- INotificationService (log + tray balloon) ----
        public void Info(string message)
        {
            _log.Info(message);
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.InvokeAsync(() => _tray.ShowBalloon(message, WinForms.ToolTipIcon.Info));
        }

        // While capturing, the top-most window must not sit over the capture region (it would be OCR'd in
        // place of the captions). If it overlaps, move it to the nearest clear working-area corner.
        private void KeepMainWindowClearOfRegion()
        {
            if (!_controller.IsRunning || _settings.Region is not CaptureRegion region)
                return;

            IntPtr hwnd = new WindowInteropHelper(_mainWindow).Handle;
            if (hwnd == IntPtr.Zero || !Win32.TryGetWindowRect(hwnd, out var win))
                return;

            var regionRect = new Win32.RECT
            {
                Left = region.X,
                Top = region.Y,
                Right = region.X + region.Width,
                Bottom = region.Y + region.Height,
            };
            if (!Overlaps(win, regionRect))
                return;

            if (TryFindClearCorner(win, regionRect, out int x, out int y))
                Win32.MoveWindowTo(hwnd, x, y, win.Width, win.Height);
        }

        private void ManageAutoSaveCacheOnStartup(WpfDialogService dialogs, bool prompt)
        {
            try
            {
                if (_settings.AutoDeleteOldAutoSaves)
                {
                    int removed = TranscriptAutoSaver.DeleteSavesOlderThan(_settings.AutoSaveDirectory, TranscriptAutoSaver.DefaultRetention);
                    if (removed > 0)
                        _log.Info($"Deleted {removed} autosave file(s) older than a month.");
                }

                if (!prompt)
                    return;

                int count = TranscriptAutoSaver.CountSaves(_settings.AutoSaveDirectory);
                if (count > _settings.AutoSavePromptThreshold &&
                    dialogs.ConfirmYesNo("Autosave cache",
                        $"The autosave folder holds {count} files.\n\n" +
                        $"{TranscriptAutoSaver.DirectoryFor(_settings.AutoSaveDirectory)}\n\n" +
                        "Clear them now?"))
                {
                    int cleared = TranscriptAutoSaver.ClearDirectory(_settings.AutoSaveDirectory);
                    _log.Info($"Cleared {cleared} autosave file(s) at startup.");
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Autosave cache check failed: " + ex.Message);
            }
        }

        private void OnCaptureError(object? sender, string message)
        {
            // Non-blocking and shutdown-safe.
            if (Dispatcher.HasShutdownStarted)
                return;
            Dispatcher.InvokeAsync(() =>
                _tray.ShowBalloon(message, WinForms.ToolTipIcon.Warning, "Caption Scribe - capture error"));
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _log?.Error("Unhandled UI exception.", e.Exception);
            e.Handled = true;
            try { _controller?.Stop(); } catch { /* shutdown path */ }
            if (!Dispatcher.HasShutdownStarted)
                _tray?.ShowBalloon("An unexpected error occurred; capture was paused and it was written to the log.",
                    WinForms.ToolTipIcon.Warning);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Last chance to record a fatal error (the process is usually terminating here).
            _log?.Error($"Unhandled exception (terminating={e.IsTerminating}).", e.ExceptionObject as Exception);
        }

        private void OnExit(object sender, ExitEventArgs e)
        {
            _log?.Info("CaptionScribe exiting.");
            ReleaseStartupHandles();
        }

        private void OnMainWindowStateChanged(object? sender, EventArgs e)
        {
            // Restoring from the taskbar (not only via the tray) must also clear the capture region.
            if (_mainWindow.WindowState == WindowState.Normal)
                KeepMainWindowClearOfRegion();
        }

        // OS shutdown/logoff must never be blocked; exit gracefully so autosave is flushed.
        private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            _log?.Info($"Session ending ({e.ReasonSessionEnding}); exiting without prompt.");
            _systemShutdown = true;
            ExitApp();
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            bool launchedAtStartup = HasStartupArgument(e.Args);

            _instanceMutex = new Mutex(true, InstanceMutexName, out bool createdNew);
            _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
            if (!createdNew)
            {
                if (!launchedAtStartup)
                    _showWindowEvent.Set();
                _showWindowEvent.Dispose();
                _showWindowEvent = null;
                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown();
                return;
            }

            bool ready = false;
            try
            {
                _log = new FileLog(DebugLoggingEnabled);
                _log.Info(launchedAtStartup ? "CaptionScribe starting (startup)." : "CaptionScribe starting.");

                WireGlobalExceptionHandlers();

                _settingsService = new SettingsService(_log);
                _settings = _settingsService.Load();
                if (_settings.EnableDebugLogging)
                    _log.Debug("Debug logging is enabled.");

                _participants = new ParticipantCollector();
                _controller = new CaptureController(_settings, _log, _participants);
                _controller.CaptureError += OnCaptureError;

                _dialogs = new WpfDialogService(new StartupLaunchService(_log), _log);
                _regionService = new RegionService();
                _viewModel = new MainViewModel(_controller, _settings, _settingsService, _dialogs, _regionService, this, new TextFileWriter(), _participants);
                _viewModel.ExitRequested += (_, _) => ExitApp();
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;

                _mainWindow = new MainWindow { DataContext = _viewModel };
                _mainWindow.StateChanged += OnMainWindowStateChanged;
                _dialogs.Owner = _mainWindow;

                _tray = new TrayIcon(
                    onOpen: ShowMainWindow,
                    onNewScribe: () => _viewModel.NewScribeCommand.Execute(null),
                    onPlay: () => _viewModel.PlayPauseCommand.Execute(null),
                    onPause: () => _viewModel.PlayPauseCommand.Execute(null),
                    onStop: () => _viewModel.StopCommand.Execute(null),
                    onShowRegion: () => _viewModel.HighlightCommand.Execute(null),
                    onSetRegion: () => _viewModel.SelectRegionCommand.Execute(null),
                    onSettings: () => _viewModel.SettingsCommand.Execute(null),
                    onExit: ExitApp);
                _tray.SetActive(_viewModel.IsCapturing);

                StartShowWindowListener();

                if (!launchedAtStartup)
                    _mainWindow.Show();

                WarnIfOcrUnavailable(showBalloon: !launchedAtStartup);

                ManageAutoSaveCacheOnStartup(_dialogs, prompt: !launchedAtStartup);
                ready = true;
            }
            finally
            {
                if (!ready)
                    ReleaseStartupHandles();
            }
        }

        private void ReleaseStartupHandles()
        {
            if (Interlocked.Exchange(ref _handlesReleased, 1) != 0)
                return;
            _exiting = true;
            if (_showWindowWait is not null)
            {
                using var done = new ManualResetEvent(false);
                if (!_showWindowWait.Unregister(done))
                    done.Set();
                done.WaitOne(UnregisterWaitMs);
                _showWindowWait = null;
            }
            _showWindowEvent?.Dispose();
            _showWindowEvent = null;
            _instanceMutex?.Dispose();
            _instanceMutex = null;
            _regionService?.Dispose();
            _controller?.Dispose();
            _tray?.Dispose();
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _log?.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainViewModel.IsCapturing) or nameof(MainViewModel.IsActive))
                _tray.SetActive(_viewModel.IsCapturing);
        }

        private static bool Overlaps(Win32.RECT a, Win32.RECT b)
            => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

        private void ShowMainWindow()
        {
            if (_exiting || Dispatcher.HasShutdownStarted)
                return;
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            KeepMainWindowClearOfRegion();
        }

        private void StartShowWindowListener()
        {
            if (_showWindowEvent is null)
                return;
            _showWindowWait = ThreadPool.RegisterWaitForSingleObject(
                _showWindowEvent,
                (_, _) =>
                {
                    if (_exiting)
                        return;
                    try { Dispatcher.BeginInvoke(ShowMainWindow); }
                    catch (InvalidOperationException) { /* dispatcher gone */ }
                },
                state: null,
                millisecondsTimeOutInterval: Timeout.Infinite,
                executeOnlyOnce: false);
        }

        // Nearest working-area corner (on the region's monitor) at which the window clears the region.
        private static bool TryFindClearCorner(Win32.RECT win, Win32.RECT region, out int x, out int y)
        {
            var work = WinForms.Screen.FromRectangle(
                System.Drawing.Rectangle.FromLTRB(region.Left, region.Top, region.Right, region.Bottom)).WorkingArea;
            int w = win.Width, h = win.Height;
            (int X, int Y)[] corners =
            {
                (work.Left, work.Top),
                (work.Right - w, work.Top),
                (work.Left, work.Bottom - h),
                (work.Right - w, work.Bottom - h),
            };

            x = win.Left;
            y = win.Top;
            long best = long.MaxValue;
            bool found = false;
            foreach (var (cx, cy) in corners)
            {
                var candidate = new Win32.RECT { Left = cx, Top = cy, Right = cx + w, Bottom = cy + h };
                if (Overlaps(candidate, region))
                    continue;
                long dist = (long)(cx - win.Left) * (cx - win.Left) + (long)(cy - win.Top) * (cy - win.Top);
                if (dist < best) { best = dist; x = cx; y = cy; found = true; }
            }
            return found;
        }

        private void WarnIfOcrUnavailable(bool showBalloon)
        {
            if (_controller.OcrAvailable)
                return;
            _log.Warning("No Windows OCR language pack found.");
            if (!showBalloon)
                return;
            _tray.ShowBalloon(
                "No Windows OCR language pack was found. Add one under Settings > Time & language > Language & region.",
                WinForms.ToolTipIcon.Warning);
        }

        // Log everything and keep the app alive where we safely can (handlers are defined below).
        private void WireGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }
    }
}
