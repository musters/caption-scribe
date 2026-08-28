using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using CaptionScribe.Models;
using CaptionScribe.Core.Logging;
using CaptionScribe.Core.Interop;

namespace CaptionScribe.Services
{
    public sealed class CaptureController : ICaptureController, IDisposable
    {
        private readonly ScreenCaptureService _capture = new();
        private readonly OcrService _ocr = new();
        private readonly WindowService _windows = new();
        private readonly TranscriptAggregator _aggregator;
        private readonly TranscriptAutoSaver _autoSaver;
        private readonly AppSettings _settings;
        private readonly ILog _log;
        private readonly IFrameObserver? _frameObserver;

        // Consistent view of capture-relevant settings; swapped atomically when they change.
        private volatile CaptureSettings _snapshot;

        private volatile CancellationTokenSource? _cts;
        private Task? _loop;
        private volatile bool _running;

        // Set once when capture pauses because Teams isn't over the region; reset when it returns.
        private bool _teamsMissingNotified;

        // Notify once at the onset of a failure streak, then auto-pause after this many in a row.
        private const int MaxConsecutiveCaptureFailures = 5;
        private readonly CaptureFailurePolicy _failurePolicy = new(MaxConsecutiveCaptureFailures);

        // Fingerprint of the last grabbed frame; identical frames skip OCR to cut work and allocations.
        private long _lastFrameHash;

        // The single capture loop feeds the frame observer (participants) on this cadence, reusing one grab + engine.
        private static readonly TimeSpan ParticipantSampleInterval = TimeSpan.FromSeconds(4);
        private DateTime _lastParticipantSampleUtc;

        // When capture first started for the current transcript; used to name saved files.
        private DateTime? _sessionStartedAt;

        public event EventHandler? TranscriptUpdated;
        public event EventHandler<string>? CaptureError;

        // Raised when the controller changes running state on its own (e.g. auto-pause after failures).
        public event EventHandler? CaptureStateChanged;

        public bool IsRunning => _running;
        public bool OcrAvailable => _ocr.IsAvailable;
        public string AutoSavePath => _autoSaver.CurrentPath;
        public DateTime? SessionStartedAt => _sessionStartedAt;

        public CaptureController(AppSettings settings, ILog log, IFrameObserver? frameObserver = null)
        {
            _settings = settings;
            _log = log;
            _frameObserver = frameObserver;
            _snapshot = CaptureSettings.From(settings);
            _aggregator = new TranscriptAggregator(settings.SimilarityThreshold);
            _autoSaver = new TranscriptAutoSaver(
                () => _aggregator.GetLines(),
                () => _settings.AutoSaveDirectory,
                log);
        }

        public void UpdateRegion(CaptureRegion region)
        {
            _settings.Region = region;
            _snapshot = CaptureSettings.From(_settings);
        }

        /// <summary>Republishes the capture snapshot after settings change, so the loop applies them atomically.</summary>
        public void ApplySettings()
        {
            _snapshot = CaptureSettings.From(_settings);
            _aggregator.SimilarityThreshold = _settings.SimilarityThreshold;
        }

        public void Start()
        {
            if (_running) return;
            if (_snapshot.Region is null) return;
            if (!_ocr.IsAvailable)
            {
                _log.Warning("Start aborted: no OCR language pack is available.");
                CaptureError?.Invoke(this, "No OCR language pack is available on this system.");
                return;
            }

            _teamsMissingNotified = false;
            _failurePolicy.RecordSuccess();
            _lastFrameHash = 0;
            _lastParticipantSampleUtc = DateTime.MinValue;
            _sessionStartedAt ??= DateTime.Now;
            var cts = new CancellationTokenSource();
            _cts = cts;
            _running = true;
            _loop = Task.Run(() => CaptureLoopAsync(cts));

            var period = TimeSpan.FromMinutes(Math.Max(1, _settings.AutoSaveIntervalMinutes));
            _autoSaver.Start(period);
            _log.Info("Capture started.");
        }

        public void Stop()
        {
            bool wasRunning = _running;
            _running = false;
            try { _cts?.Cancel(); } catch { /* already disposed */ }

            _autoSaver.Stop();
            if (wasRunning)
                _log.Info("Capture stopped.");
        }

        private async Task CaptureLoopAsync(CancellationTokenSource cts)
        {
            var token = cts.Token;
            var interval = TimeSpan.FromMilliseconds(Math.Max(400, _snapshot.CaptureIntervalMs));
            using var timer = new PeriodicTimer(interval);
            bool autoPaused = false;
            try
            {
                do
                {
                    if (token.IsCancellationRequested)
                        break;

                    try
                    {
                        var s = _snapshot;   // one consistent view for this cycle
                        if (s.Region is not null && TeamsPresentForCapture(s))
                        {
                            var raw = await GrabRawAsync(s, token);   // pooled buffer owned by _capture
                            long frame = _capture.Fingerprint(raw);
                            if (frame != _lastFrameHash)
                            {
                                // Only OCR when the region's pixels actually changed since the last grab.
                                _lastFrameHash = frame;
                                await RecognizeCaptionsAsync(raw, s);
                            }
                            await MaybeSampleParticipantsAsync(raw, token);
                        }
                        NoteCaptureSuccess();
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancellation is a normal stop, not a capture failure.
                    }
                    catch (Exception ex)
                    {
                        if (NoteCaptureFailure(ex))
                        {
                            autoPaused = true;
                            break;
                        }
                    }
                }
                while (await SafeWaitAsync(timer, token));
            }
            finally
            {
                // Only clear state if a newer Start() has not replaced this loop.
                if (ReferenceEquals(_cts, cts))
                {
                    _running = false;
                    _capture.ReleaseBuffers();   // free pooled frames between captures
                }
                cts.Dispose();
            }

            if (autoPaused)
                AutoPause();
        }

        private void NoteCaptureSuccess()
        {
            int cleared = _failurePolicy.RecordSuccess();
            if (cleared > 0)
                _log.Info($"Capture recovered after {cleared} failed cycle(s).");
        }

        // Logs the failure, notifies once at the onset, and returns true when the loop should auto-pause.
        private bool NoteCaptureFailure(Exception ex)
        {
            switch (_failurePolicy.RecordFailure())
            {
                case CaptureFailureAction.Notify:
                    _log.Error("Capture cycle failed.", ex);
                    CaptureError?.Invoke(this, ex.Message);
                    return false;
                case CaptureFailureAction.Pause:
                    _log.Error("Capture cycle failed; pausing after repeated errors.", ex);
                    return true;
                default:
                    _log.Warning("Capture cycle failed again: " + ex.Message);
                    return false;
            }
        }

        // Runs after the loop exits from repeated failures: stop autosave and let the UI reflect the pause.
        private void AutoPause()
        {
            _autoSaver.Stop();
            _log.Warning($"Capture auto-paused after {MaxConsecutiveCaptureFailures} consecutive failures.");
            CaptureError?.Invoke(this,
                "Capture paused after repeated errors. Check the capture region, then press Play to resume.");
            CaptureStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken token)
        {
            try { return await timer.WaitForNextTickAsync(token); }
            catch (OperationCanceledException) { return false; }
        }

        /// <summary>
        /// Grabs the region at native resolution/color. When focus-switching is enabled and the Teams
        /// window is not already in front, briefly activates it for the screenshot then restores focus.
        /// </summary>
        private async Task<Bitmap> GrabRawAsync(CaptureSettings s, CancellationToken token)
        {
            var region = s.Region!;
            if (!s.FocusSwitchEnabled)
                return _capture.CaptureRaw(region);

            IntPtr teams = _windows.FindTeamsWindow(s.TeamsWindowTitleHint);
            if (teams == IntPtr.Zero || Win32.IsForeground(teams))
                return _capture.CaptureRaw(region);

            IntPtr previous = Win32.GetForegroundWindowHandle();
            bool activated = Win32.TryActivate(teams);
            if (activated && s.FocusSettleMs > 0)
            {
                try { await Task.Delay(s.FocusSettleMs, token); }
                catch (OperationCanceledException) { }
            }

            try
            {
                return _capture.CaptureRaw(region);
            }
            finally
            {
                if (activated && previous != IntPtr.Zero)
                    Win32.TryActivate(previous);
            }
        }

        // Captions OCR the upscaled/enhanced image so text accuracy is unchanged; the raw frame stays native.
        private async Task RecognizeCaptionsAsync(Bitmap raw, CaptureSettings s)
        {
            // Both raw and the processed image are pooled buffers owned by _capture; do not dispose them.
            Bitmap ocrImage = (s.UpscaleFactor > 1 || s.EnhanceForOcr)
                ? _capture.Process(raw, s.UpscaleFactor, s.EnhanceForOcr)
                : raw;

            var lines = await _ocr.RecognizeLinesAsync(ocrImage);
            if (_log.IsDebugEnabled)
                _log.Debug($"OCR produced {lines.Count} line(s).");
            if (lines.Count > 0)
            {
                _aggregator.AddSnapshot(lines);
                TranscriptUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        // On a slow cadence, hand the same native frame + its layout to the observer (participant collection),
        // reusing this loop's single grab and OCR engine instead of capturing/recognizing the screen again.
        private async Task MaybeSampleParticipantsAsync(Bitmap raw, CancellationToken token)
        {
            if (_frameObserver is not { WantsFrames: true })
                return;

            var now = DateTime.UtcNow;
            if (now - _lastParticipantSampleUtc < ParticipantSampleInterval)
                return;
            _lastParticipantSampleUtc = now;

            token.ThrowIfCancellationRequested();
            var layout = await _ocr.RecognizeLayoutAsync(raw);
            _frameObserver.OnFrame(raw, layout);
        }

        // When "require Teams window" is on (and we're not force-focusing Teams), skip OCR unless a Teams
        // window is actually in front of the region. Prevents trailing text after a meeting window closes.
        private bool TeamsPresentForCapture(CaptureSettings s)
        {
            if (!s.RequireTeamsWindow || s.FocusSwitchEnabled)
                return true;

            var region = s.Region!;
            int cx = region.X + region.Width / 2;
            int cy = region.Y + region.Height / 2;
            bool present = _windows.IsTeamsAtPoint(cx, cy);

            if (!present)
            {
                if (!_teamsMissingNotified)
                {
                    _teamsMissingNotified = true;
                    _log.Info("Capture paused: Teams is not in front of the region.");
                    CaptureError?.Invoke(this,
                        "Teams isn't in front of the capture region — pausing capture until it returns.");
                }
            }
            else if (_teamsMissingNotified)
            {
                _teamsMissingNotified = false;
                _log.Info("Capture resumed: Teams is in front of the region.");
            }
            return present;
        }

        /// <summary>Diagnostic used by the "Capture Diagnostics" button.</summary>
        public string GetCaptureDiagnostics() => _windows.BuildCaptureDiagnostics(_snapshot.TeamsWindowTitleHint, _snapshot.Region);

        public string GetTranscriptText() => _aggregator.GetText();

        public int TranscriptLineCount => _aggregator.Count;

        public string GetTranscriptTailText(int maxLines) => _aggregator.GetTailText(maxLines);

        public IReadOnlyList<TimedLine> GetTimedTranscript() => _aggregator.GetTimedLines();

        public IReadOnlyList<TimedLine> GetTimedTail(int maxLines) => _aggregator.GetTimedTail(maxLines);

        public void ClearTranscript()
        {
            // Finalize the outgoing file before clearing, so the previous transcript is complete on disk.
            _autoSaver.Flush(final: true);
            _aggregator.Clear();
            _autoSaver.Roll();
            _lastFrameHash = 0;
            _sessionStartedAt = _running ? DateTime.Now : null;
            _log.Info("Transcript cleared.");
            TranscriptUpdated?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Stop();
            try { _loop?.Wait(1000); } catch { /* ignore shutdown races */ }
            _autoSaver.Dispose();
            // Safe after Wait: the loop reads pooled frames only synchronously (OCR copies them out before awaiting).
            _capture.Dispose();
            // The capture loop disposes its own CancellationTokenSource when it exits.
        }
    }
}
