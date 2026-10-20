using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CaptionScribe.Models;
using CaptionScribe.Core.Logging;

namespace CaptionScribe.Services
{
    public sealed class CaptureController : ICaptureController, IDisposable
    {
        private readonly ScreenCaptureService _capture = new();
        private readonly WindowService _windows = new();
        private readonly FocusAwareGrabber _grabber;
        private readonly CaptionFrameProcessor _frames;
        private readonly CaptureLoopRunner _loopRunner;
        private readonly TeamsPresenceGate _teams;
        private readonly AppSettings _settings;
        private readonly ILog _log;

        private volatile CaptureSettings _snapshot;

        private readonly object _lifecycle = new();
        private readonly AutosaveScheduler _autosave;
        private volatile CancellationTokenSource? _cts;
        private Task? _loop;
        private volatile bool _running;
        private volatile bool _startQueued;
        private int _lifecycleEpoch;

        private DateTime? _sessionStartedAt;

        public event EventHandler? TranscriptUpdated
        {
            add => _frames.TranscriptUpdated += value;
            remove => _frames.TranscriptUpdated -= value;
        }

        public event EventHandler<string>? CaptureError;

        public event EventHandler? CaptureStateChanged;

        public bool IsRunning => _running;
        public bool OcrAvailable => _frames.OcrAvailable;
        public string AutoSavePath => _autosave.CurrentPath;
        public DateTime? SessionStartedAt => _sessionStartedAt;

        public CaptureController(AppSettings settings, ILog log, IFrameObserver? frameObserver = null)
        {
            _settings = settings;
            _log = log;
            _snapshot = CaptureSettings.From(settings);
            _grabber = new FocusAwareGrabber(_capture, _windows);
            var aggregator = new TranscriptAggregator(settings.SimilarityThreshold);
            _frames = new CaptionFrameProcessor(_capture, aggregator, log, frameObserver);
            _loopRunner = new CaptureLoopRunner(log, msg => CaptureError?.Invoke(this, msg));
            _teams = new TeamsPresenceGate(_windows, log);
            _autosave = new AutosaveScheduler(
                new TranscriptAutoSaver(aggregator.GetLinesFrom, () => _settings.AutoSaveDirectory, log),
                log);
        }

        public void UpdateRegion(CaptureRegion region)
        {
            _settings.Region = region;
            _snapshot = CaptureSettings.From(_settings);
        }

        public void ApplySettings()
        {
            _snapshot = CaptureSettings.From(_settings);
            _frames.ApplySimilarity(_settings.SimilarityThreshold);
        }

        public void Start()
        {
            Task? loop;
            int epoch;
            lock (_lifecycle)
            {
                if (_running || _startQueued)
                    return;
                try { _cts?.Cancel(); } catch { /* already disposed */ }
                loop = _loop;
                epoch = _lifecycleEpoch;
                _startQueued = true;
            }

            void AfterJoin()
            {
                try
                {
                    ObserveLoop(loop);
                    lock (_lifecycle)
                    {
                        if (ReferenceEquals(_loop, loop))
                        {
                            _loop = null;
                            _cts = null;
                        }
                        if (epoch != _lifecycleEpoch)
                            return;
                    }
                    BeginCapture(epoch);
                }
                catch (Exception ex)
                {
                    _log.Warning("Capture start after join failed: " + ex.Message);
                }
                finally
                {
                    lock (_lifecycle)
                        _startQueued = false;
                    CaptureStateChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            if (loop is null || loop.IsCompleted)
            {
                AfterJoin();
                return;
            }

            loop.ContinueWith(_ => AfterJoin(), TaskScheduler.Default);
        }

        private void BeginCapture(int epoch)
        {
            bool started = false;
            lock (_lifecycle)
            {
                if (_running || epoch != _lifecycleEpoch)
                    return;
                if (_snapshot.Region is null)
                    return;
                if (!_frames.OcrAvailable)
                {
                    _log.Warning("Start aborted: no OCR language pack is available.");
                    CaptureError?.Invoke(this, "No OCR language pack is available on this system.");
                    return;
                }

                _teams.Reset();
                _loopRunner.Reset();
                _capture.ResetChangeDetection();
                _frames.ResetParticipantClock();
                _sessionStartedAt ??= DateTime.Now;
                var cts = new CancellationTokenSource();
                _cts = cts;
                _running = true;
                _loop = Task.Run(() => _loopRunner.RunAsync(
                    cts,
                    () => Math.Max(AppSettings.MinCaptureIntervalMs, _snapshot.CaptureIntervalMs),
                    RunCycleAsync,
                    c => ReferenceEquals(_cts, c),
                    _capture.ReleaseBuffers,
                    () => _running = false,
                    AutoPause));
                started = true;
            }

            if (!started)
                return;
            var interval = TimeSpan.FromMinutes(Math.Max(AppSettings.MinAutoSaveIntervalMinutes, _settings.AutoSaveIntervalMinutes));
            _autosave.EnqueueStart(interval, () => Volatile.Read(ref _lifecycleEpoch) == epoch && _running);
            _log.Info("Capture started.");
        }

        public void Stop()
        {
            bool wasActive;
            lock (_lifecycle)
            {
                wasActive = _running || _startQueued;
                _lifecycleEpoch++;
                _running = false;
                _startQueued = false;
                try { _cts?.Cancel(); } catch { /* already disposed */ }
            }
            if (wasActive)
            {
                _log.Info("Capture stopped.");
                _autosave.EnqueueStop();
            }
        }

        private void ObserveLoop(Task? loop)
        {
            if (loop is null)
                return;
            try { loop.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { /* normal stop */ }
            catch (Exception ex) { _log.Warning("Capture loop ended with an error: " + ex.Message); }
        }

        private void JoinPending()
        {
            Task? loop;
            lock (_lifecycle)
            {
                _running = false;
                try { _cts?.Cancel(); } catch { /* already disposed */ }
                loop = _loop;
                _loop = null;
            }
            ObserveLoop(loop);
            lock (_lifecycle)
            {
                if (_loop is null)
                    _cts = null;
            }
        }

        private async Task RunCycleAsync(CancellationToken token)
        {
            var s = _snapshot;
            if (s.Region is not null && _teams.Allow(s, msg => CaptureError?.Invoke(this, msg)))
            {
                var raw = await _grabber.GrabAsync(s, token);
                if (_capture.HasMeaningfulChange(raw))
                    await _frames.RecognizeAsync(raw, s, token);
            }
        }

        private void AutoPause()
        {
            Stop();
            _log.Warning($"Capture auto-paused after {CaptureLoopRunner.MaxConsecutiveCaptureFailures} consecutive failures.");
            CaptureError?.Invoke(this,
                "Capture paused after repeated errors. Check the capture region, then press Play to resume.");
            CaptureStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetCaptureDiagnostics() => _windows.BuildCaptureDiagnostics(_snapshot.TeamsWindowTitleHint, _snapshot.Region);

        public string GetTranscriptText() => _frames.GetText();
        public int TranscriptLineCount => _frames.LineCount;
        public string GetTranscriptTailText(int maxLines) => _frames.GetTailText(maxLines);
        public IReadOnlyList<TimedLine> GetTimedTranscript() => _frames.GetTimedLines();
        public IReadOnlyList<TimedLine> GetTimedTail(int maxLines) => _frames.GetTimedTail(maxLines);

        public TranscriptSnapshot GetExportSnapshot()
        {
            var (timed, plain) = _frames.GetExportSnapshot();
            return new TranscriptSnapshot(timed, plain, _sessionStartedAt ?? DateTime.Now);
        }

        public void ClearTranscript()
        {
            _autosave.Flush(final: true);
            _frames.Clear();
            _autosave.Roll();
            _capture.ResetChangeDetection();
            _sessionStartedAt = _running ? DateTime.Now : null;
            _log.Info("Transcript cleared.");
        }

        public void Dispose()
        {
            lock (_lifecycle)
            {
                _lifecycleEpoch++;
                _running = false;
                _startQueued = false;
                try { _cts?.Cancel(); } catch { /* already disposed */ }
            }
            JoinPending();
            _autosave.Dispose();
            _frames.Dispose();
            _capture.Dispose();
        }
    }
}
