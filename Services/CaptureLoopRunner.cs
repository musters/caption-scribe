using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CaptionScribe.Core.Logging;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Periodic capture loop: interval/backpressure, consecutive-failure policy, and auto-pause.
    /// The host supplies each cycle and owns bitmap/OCR lifetime.
    /// </summary>
    internal sealed class CaptureLoopRunner
    {
        internal const int MaxConsecutiveCaptureFailures = 5;

        private readonly CaptureFailurePolicy _failurePolicy = new(MaxConsecutiveCaptureFailures);
        private readonly ILog _log;
        private readonly Action<string> _onError;

        public CaptureLoopRunner(ILog log, Action<string> onError)
        {
            _log = log;
            _onError = onError;
        }

        public void Reset() => _failurePolicy.RecordSuccess();

        public async Task RunAsync(
            CancellationTokenSource cts,
            Func<int> intervalMs,
            Func<CancellationToken, Task> cycle,
            Func<CancellationTokenSource, bool> isCurrent,
            Action releaseBuffers,
            Action onStoppedIfCurrent,
            Action autoPause)
        {
            var token = cts.Token;
            bool autoPaused = false;
            bool captureImmediately = true;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int ms = intervalMs();
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ms));
                    if (!captureImmediately && !await SafeWaitAsync(timer, token))
                        break;
                    captureImmediately = false;
                    var cycleWatch = Stopwatch.StartNew();
                    do
                    {
                        if (token.IsCancellationRequested)
                            break;

                        cycleWatch.Restart();
                        try
                        {
                            await cycle(token);
                            NoteSuccess();
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            if (NoteFailure(ex))
                            {
                                autoPaused = true;
                                break;
                            }
                        }

                        if (intervalMs() != ms)
                            break;
                        if (cycleWatch.ElapsedMilliseconds >= ms)
                        {
                            captureImmediately = false;
                            break;
                        }
                    }
                    while (!autoPaused && await SafeWaitAsync(timer, token));

                    if (autoPaused)
                        break;
                }
            }
            finally
            {
                if (isCurrent(cts))
                {
                    if (!autoPaused)
                        onStoppedIfCurrent();
                    releaseBuffers();
                }
                cts.Dispose();
            }

            if (autoPaused)
                autoPause();
        }

        private void NoteSuccess()
        {
            int cleared = _failurePolicy.RecordSuccess();
            if (cleared > 0)
                _log.Info($"Capture recovered after {cleared} failed cycle(s).");
        }

        private bool NoteFailure(Exception ex)
        {
            switch (_failurePolicy.RecordFailure())
            {
                case CaptureFailureAction.Notify:
                    _log.Error("Capture cycle failed.", ex);
                    _onError(ex.Message);
                    return false;
                case CaptureFailureAction.Pause:
                    _log.Error("Capture cycle failed; pausing after repeated errors.", ex);
                    return true;
                default:
                    _log.Warning("Capture cycle failed again: " + ex.Message);
                    return false;
            }
        }

        private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken token)
        {
            try { return await timer.WaitForNextTickAsync(token); }
            catch (OperationCanceledException) { return false; }
        }
    }
}
