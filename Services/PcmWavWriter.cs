using System;
using System.IO;
using System.Text;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Appends PCM 16-bit samples to a RIFF WAVE file and keeps the header sizes truthful
    /// so a crash still leaves a playable file.
    /// </summary>
    internal sealed class PcmWavWriter : IDisposable
    {
        internal const int HeaderSize = 44;
        internal const int BitsPerSample = 16;
        internal static readonly TimeSpan HeaderFlushInterval = TimeSpan.FromSeconds(3);
        /// <summary>Classic RIFF data chunk is uint; stay under 4 GB minus header.</summary>
        internal const uint MaxDataBytes = 4_000_000_000;

        private readonly object _gate = new();
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly int _blockAlign;
        private FileStream? _stream;
        private long _dataBytes;
        private long _lastHeaderFlushMs;
        private bool _disposed;

        public PcmWavWriter(string path, int sampleRate, int channels)
        {
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels is not (1 or 2))
                throw new ArgumentOutOfRangeException(nameof(channels));

            _sampleRate = sampleRate;
            _channels = channels;
            _blockAlign = channels * (BitsPerSample / 8);
            FilePath = path;
            string? dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            WriteHeaderUnlocked(0);
            _lastHeaderFlushMs = Environment.TickCount64;
        }

        public string FilePath { get; }
        public int SampleRate => _sampleRate;
        public int Channels => _channels;
        public int BlockAlign => _blockAlign;

        public long DataBytes
        {
            get { lock (_gate) return _dataBytes; }
        }

        public bool Write(ReadOnlySpan<byte> pcm)
        {
            if (pcm.Length == 0)
                return true;
            if (pcm.Length % _blockAlign != 0)
                throw new ArgumentException("PCM length must be a multiple of the block align.", nameof(pcm));

            lock (_gate)
            {
                EnsureOpen();
                if ((ulong)_dataBytes + (uint)pcm.Length > MaxDataBytes)
                    return false;
                _stream!.Write(pcm);
                _dataBytes += pcm.Length;
                long now = Environment.TickCount64;
                if (now - _lastHeaderFlushMs >= HeaderFlushInterval.TotalMilliseconds)
                    FlushHeaderUnlocked(toDisk: false);
                return true;
            }
        }

        public void FlushHeader() => FlushHeader(toDisk: false);

        public void FlushToDisk() => FlushHeader(toDisk: true);

        private void FlushHeader(bool toDisk)
        {
            lock (_gate)
            {
                if (_stream is null)
                    return;
                FlushHeaderUnlocked(toDisk);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                if (_stream is not null)
                {
                    try { FlushHeaderUnlocked(toDisk: true); }
                    catch { /* best-effort final header */ }
                    _stream.Dispose();
                    _stream = null;
                }
            }
        }

        private void EnsureOpen()
        {
            if (_disposed || _stream is null)
                throw new ObjectDisposedException(nameof(PcmWavWriter));
        }

        private void FlushHeaderUnlocked(bool toDisk)
        {
            uint data = _dataBytes > MaxDataBytes ? MaxDataBytes : (uint)_dataBytes;
            WriteHeaderUnlocked(data);
            _stream!.Flush(flushToDisk: toDisk);
            _lastHeaderFlushMs = Environment.TickCount64;
        }

        private void WriteHeaderUnlocked(uint dataBytes)
        {
            int blockAlign = _blockAlign;
            int byteRate = _sampleRate * blockAlign;
            uint riffSize = dataBytes + HeaderSize - 8;
            _stream!.Position = 0;
            using (var w = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(riffSize);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((ushort)1);
                w.Write((ushort)_channels);
                w.Write(_sampleRate);
                w.Write(byteRate);
                w.Write((ushort)blockAlign);
                w.Write((ushort)BitsPerSample);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
            }
            _stream.Position = HeaderSize + dataBytes;
        }
    }
}
