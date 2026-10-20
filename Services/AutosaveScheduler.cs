using System;
using System.Threading;
using System.Threading.Tasks;
using CaptionScribe.Core.Logging;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Runs autosave Start/Stop/Dispose on one worker chain so Pause cannot dispose a timer
    /// that Play just created, and so those waits never sit on the UI thread.
    /// </summary>
    internal sealed class AutosaveScheduler : IDisposable
    {
        private readonly TranscriptAutoSaver _saver;
        private readonly ILog _log;
        private readonly object _gate = new();
        private Task _chain = Task.CompletedTask;
        private bool _disposed;

        public AutosaveScheduler(TranscriptAutoSaver saver, ILog log)
        {
            _saver = saver;
            _log = log;
        }

        public string CurrentPath => _saver.CurrentPath;

        public void EnqueueStart(TimeSpan interval, Func<bool> stillWanted)
            => Enqueue(() =>
            {
                if (!stillWanted())
                    return;
                _saver.Start(interval);
            });

        public void EnqueueStop() => Enqueue(() => _saver.Stop());

        public void Flush(bool final) => _saver.Flush(final);

        public void Roll() => _saver.Roll();

        public void Dispose()
        {
            Task chain;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _chain = _chain.ContinueWith(_ =>
                {
                    try { _saver.Dispose(); }
                    catch (Exception ex) { _log.Warning("Autosave dispose failed: " + ex.Message); }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                chain = _chain;
            }
            chain.GetAwaiter().GetResult();
        }

        private void Enqueue(Action action)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _chain = _chain.ContinueWith(_ =>
                {
                    try { action(); }
                    catch (Exception ex) { _log.Warning("Autosave operation failed: " + ex.Message); }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
    }
}
