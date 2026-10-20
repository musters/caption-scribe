using System;
using System.Threading;
using System.Threading.Tasks;
using CaptionScribe.Core.Logging;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Device-loopback recording into a crash-safe WAV. Pause keeps the file; Play inserts a short silence.
    /// All WASAPI and disk work runs on a worker chain so the UI is not blocked.
    /// </summary>
    internal sealed class LoopbackAudioSession : IDisposable
    {
        internal static readonly TimeSpan PauseSilence = TimeSpan.FromMilliseconds(750);
        internal static readonly TimeSpan DefaultSilentAfter = TimeSpan.FromMinutes(3);
        private const int PacketPollMs = 10;
        private const int MixBufferBytes = 192_000;

        private readonly ILog _log;
        private readonly ILoopbackCaptureFactory _factory;
        private readonly string _label;
        private readonly long _silentAfterMs;
        private readonly object _gate = new();
        private Task _chain = Task.CompletedTask;
        private PcmWavWriter? _writer;
        private CancellationTokenSource? _cts;
        private Task? _pump;
        private int _epoch;
        private volatile bool _capturing;
        private volatile bool _failed;
        private volatile float _level;
        private volatile bool _apparentlySilent;
        private bool _heardSound;
        private long _lastSoundMs;
        private bool _loggedSizeCap;
        private string? _wavPath;
        private bool _disposed;

        public LoopbackAudioSession(ILog log, ILoopbackCaptureFactory? factory = null, string label = "Meeting audio",
            TimeSpan? silentAfter = null)
        {
            _log = log;
            _factory = factory ?? new WasapiLoopbackFactory();
            _label = label;
            _silentAfterMs = (long)(silentAfter ?? DefaultSilentAfter).TotalMilliseconds;
        }

        public bool IsCapturing => _capturing;
        public bool Failed => _failed;
        public bool ApparentlySilent => _apparentlySilent;
        public float Level => _level;

        public string? SnapshotWavPath()
        {
            FlushForSnapshot();
            lock (_gate)
                return _wavPath;
        }

        public void FlushForSnapshot()
        {
            lock (_gate)
            {
                try { _writer?.FlushHeader(); }
                catch { /* snapshot remains best-effort */ }
            }
        }

        public void EnqueueStart(string wavPath) => Enqueue(() => StartOrResume(wavPath));
        public void EnqueuePause() => Enqueue(PauseCore);
        public void EnqueueClose() => Enqueue(CloseCore);

        public void WaitForIdle()
        {
            Task chain;
            lock (_gate) chain = _chain;
            chain.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Enqueue(CloseCore);
            WaitForIdle();
            lock (_gate)
                _disposed = true;
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
                    catch (Exception ex) { _log.Warning("Audio session operation failed: " + ex.Message); }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }

        private void StartOrResume(string wavPath)
        {
            _failed = false;
            _apparentlySilent = false;
            _heardSound = false;
            _lastSoundMs = Environment.TickCount64;
            PauseCore();
            lock (_gate)
            {
                _wavPath = wavPath;
                if (_writer is not null)
                    PcmConvert.WriteSilence(_writer, PauseSilence);
                var cts = new CancellationTokenSource();
                _cts = cts;
                int epoch = _epoch;
                _pump = Task.Run(() => Pump(wavPath, epoch, cts.Token));
            }
        }

        private void PauseCore()
        {
            CancellationTokenSource? cts;
            Task? pump;
            lock (_gate)
            {
                cts = _cts;
                pump = _pump;
                _cts = null;
                _pump = null;
                _capturing = false;
                _level = 0;
            }
            try { cts?.Cancel(); } catch { /* disposed */ }
            if (pump is not null)
            {
                try { pump.Wait(); }
                catch { /* cancelled or failed */ }
            }
            cts?.Dispose();
            lock (_gate)
            {
                try { _writer?.FlushToDisk(); }
                catch { /* best-effort */ }
            }
        }

        private void CloseCore()
        {
            lock (_gate)
                _epoch++;
            PauseCore();
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
                _wavPath = null;
                _failed = false;
            }
        }

        private void Pump(string wavPath, int epoch, CancellationToken token)
        {
            ILoopbackCapture? capture = null;
            var mix = new byte[MixBufferBytes];
            try
            {
                capture = _factory.Start();
                lock (_gate)
                {
                    if (epoch != _epoch)
                        return;
                    _writer ??= new PcmWavWriter(wavPath, capture.SampleRate, capture.Channels);
                    _capturing = true;
                    _lastSoundMs = Environment.TickCount64;
                    _heardSound = false;
                    _apparentlySilent = false;
                }

                int mixCap = Math.Max(MixBufferBytes, capture.MaxPacketBytes);
                if (mix.Length < mixCap)
                    mix = new byte[mixCap];
                var pcm = new byte[mixCap];
                while (!token.IsCancellationRequested)
                {
                    int bytes = capture.Read(mix, out int frames);
                    if (bytes <= 0 || frames <= 0)
                    {
                        _level *= 0.85f;
                        if (!_heardSound && _silentAfterMs > 0
                            && Environment.TickCount64 - _lastSoundMs >= _silentAfterMs)
                            _apparentlySilent = true;
                        Thread.Sleep(PacketPollMs);
                        continue;
                    }
                    int written = PcmConvert.MixToPcm16(mix.AsSpan(0, bytes), frames, capture.Channels, capture.IsFloat,
                        capture.BitsPerSample, pcm);
                    lock (_gate)
                    {
                        if (epoch != _epoch || _writer is null)
                            return;
                        if (!_writer.Write(pcm.AsSpan(0, written)))
                        {
                            if (!_loggedSizeCap)
                            {
                                _loggedSizeCap = true;
                                _log.Warning(_label + " recording stopped: WAV size cap reached.");
                            }
                            return;
                        }
                    }
                    float peak = PcmConvert.PeakPcm16(pcm.AsSpan(0, written));
                    float cur = _level;
                    _level = peak > cur * 0.7f ? peak : cur * 0.7f;
                    long now = Environment.TickCount64;
                    if (peak > 0.02f)
                    {
                        _heardSound = true;
                        _lastSoundMs = now;
                        _apparentlySilent = false;
                    }
                    else if (!_heardSound && _silentAfterMs > 0 && now - _lastSoundMs >= _silentAfterMs)
                        _apparentlySilent = true;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _failed = true;
                _log.Warning(_label + " capture failed: " + ex.Message);
            }
            finally
            {
                _capturing = false;
                capture?.Dispose();
            }
        }
    }
}
