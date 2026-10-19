using System;
using System.Threading;
using System.Windows.Threading;

namespace CaptionScribe.Core.Shell
{
    /// <summary>Per-user mutex + named event so a second launch activates the running instance.</summary>
    internal sealed class SingleInstanceHost : IDisposable
    {
        private const string MutexName = @"Local\CaptionScribe";
        private const string ShowWindowEventName = @"Local\CaptionScribe.ShowWindow";
        private const int UnregisterWaitMs = 500;

        private readonly string _mutexName;
        private readonly string _eventName;
        private Mutex? _mutex;
        private EventWaitHandle? _showEvent;
        private RegisteredWaitHandle? _wait;
        private volatile bool _exiting;
        private bool _disposed;

        public SingleInstanceHost() : this(MutexName, ShowWindowEventName) { }

        internal SingleInstanceHost(string mutexName, string eventName)
        {
            _mutexName = mutexName;
            _eventName = eventName;
        }

        public bool TryClaim(bool notifyExisting)
        {
            _mutex = new Mutex(true, _mutexName, out bool createdNew);
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
            if (createdNew)
                return true;

            if (notifyExisting)
                _showEvent.Set();
            Dispose();
            return false;
        }

        public void Listen(Action onShow, Dispatcher dispatcher)
        {
            if (_showEvent is null)
                return;
            _wait = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (_, _) =>
                {
                    if (_exiting)
                        return;
                    try { dispatcher.BeginInvoke(onShow); }
                    catch (InvalidOperationException) { /* dispatcher gone */ }
                },
                state: null,
                millisecondsTimeOutInterval: Timeout.Infinite,
                executeOnlyOnce: false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _exiting = true;
            if (_wait is not null)
            {
                using var done = new ManualResetEvent(false);
                if (!_wait.Unregister(done))
                    done.Set();
                done.WaitOne(UnregisterWaitMs);
                _wait = null;
            }
            _showEvent?.Dispose();
            _showEvent = null;
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}
