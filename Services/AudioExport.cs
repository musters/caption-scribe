using System;
using System.Buffers.Binary;
using System.IO;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace CaptionScribe.Services
{
    /// <summary>Copies or transcodes an autosave WAV to a listening file next to the transcript.</summary>
    internal static class AudioExport
    {
        public static bool HasUsefulWav(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;
            return new FileInfo(path).Length > PcmWavWriter.HeaderSize;
        }

        public static string? TrySaveListeningFile(string? wavPath, string destBaseWithoutExtension, string? micWavPath = null)
        {
            bool hasLoop = HasUsefulWav(wavPath);
            bool hasMic = HasUsefulWav(micWavPath);
            if (!hasLoop && !hasMic)
                return null;

            string? loopSnapshot = null;
            string? micSnapshot = null;
            string? mixed = null;
            try
            {
                if (hasLoop)
                {
                    string path = CreateTemporaryWavPath(destBaseWithoutExtension);
                    if (TryCreateStableSnapshot(wavPath!, path))
                        loopSnapshot = path;
                    else
                        try { File.Delete(path); } catch { /* ignore */ }
                }
                if (hasMic)
                {
                    string path = CreateTemporaryWavPath(destBaseWithoutExtension);
                    if (TryCreateStableSnapshot(micWavPath!, path))
                        micSnapshot = path;
                    else
                        try { File.Delete(path); } catch { /* ignore */ }
                }
                if (loopSnapshot is null && micSnapshot is null)
                    return null;

                string source = loopSnapshot ?? micSnapshot!;
                if (loopSnapshot is not null && micSnapshot is not null)
                {
                    mixed = CreateTemporaryWavPath(destBaseWithoutExtension);
                    if (TryMixWav(loopSnapshot, micSnapshot, mixed))
                        source = mixed;
                }

                if (TryTranscode(source, destBaseWithoutExtension + ".mp3",
                        () => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.Auto)))
                    return destBaseWithoutExtension + ".mp3";
                if (TryTranscode(source, destBaseWithoutExtension + ".m4a",
                        () => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.Auto)))
                    return destBaseWithoutExtension + ".m4a";

                string wavDest = destBaseWithoutExtension + ".wav";
                File.Copy(source, wavDest, overwrite: true);
                return wavDest;
            }
            finally
            {
                if (mixed is not null)
                    try { File.Delete(mixed); } catch { /* ignore */ }
                if (loopSnapshot is not null)
                    try { File.Delete(loopSnapshot); } catch { /* ignore */ }
                if (micSnapshot is not null)
                    try { File.Delete(micSnapshot); } catch { /* ignore */ }
            }
        }

        internal static bool TryCreateStableSnapshot(string sourcePath, string snapshotPath)
        {
            if (!TryOpenPcm16(sourcePath, FileShare.ReadWrite, out var source))
                return false;
            using (source)
            using (var writer = new PcmWavWriter(snapshotPath, source.SampleRate, source.Channels))
            {
                const int chunkBytes = 64 * 1024;
                int alignedChunkBytes = chunkBytes - chunkBytes % source.BlockAlign;
                var buffer = new byte[alignedChunkBytes];
                long remaining = source.DataBytes;
                while (remaining > 0)
                {
                    int requested = (int)Math.Min(buffer.Length, remaining);
                    int read = ReadFully(source.Stream, buffer, requested);
                    if (read != requested || !writer.Write(buffer.AsSpan(0, read)))
                        return false;
                    remaining -= read;
                }
                return true;
            }
        }

        private static string CreateTemporaryWavPath(string destBaseWithoutExtension)
            => destBaseWithoutExtension + "." + Guid.NewGuid().ToString("N") + ".tmp.wav";

        public static bool TryMixWav(string loopPath, string micPath, string destPath)
        {
            if (!TryOpenPcm16(loopPath, FileShare.Read, out var loop))
                return false;
            using (loop)
            {
                if (!TryOpenPcm16(micPath, FileShare.Read, out var mic))
                    return false;
                using (mic)
                {
                    using var writer = new PcmWavWriter(destPath, loop.SampleRate, loop.Channels);
                    const int chunkFrames = 4096;
                    int loopStep = loop.BlockAlign;
                    int micStep = mic.BlockAlign;
                    long loopFrames = loop.DataBytes / loopStep;
                    var loopBuf = new byte[chunkFrames * loopStep];
                    var destBuf = new byte[chunkFrames * loopStep];
                    var micBuf = new byte[Math.Max(micStep, chunkFrames * 4 * micStep)];
                    long frame = 0;
                    while (frame < loopFrames)
                    {
                        int n = (int)Math.Min(chunkFrames, loopFrames - frame);
                        int loopBytes = n * loopStep;
                        if (ReadFully(loop.Stream, loopBuf, loopBytes) != loopBytes)
                            break;
                        long micStart = loop.SampleRate <= 0 ? 0 : frame * mic.SampleRate / loop.SampleRate;
                        long micEnd = loop.SampleRate <= 0 ? 0 : (frame + n) * mic.SampleRate / loop.SampleRate;
                        int micCount = (int)Math.Max(0, micEnd - micStart + 1);
                        int micBytes = 0;
                        long micFramesTotal = micStep == 0 ? 0 : mic.DataBytes / micStep;
                        if (micCount > 0 && micStart < micFramesTotal)
                        {
                            mic.Stream.Position = mic.DataStart + micStart * micStep;
                            long remaining = mic.DataStart + mic.DataBytes - mic.Stream.Position;
                            int want = (int)Math.Min((long)micCount * micStep, remaining);
                            if (want > 0)
                            {
                                if (want > micBuf.Length)
                                    micBuf = new byte[want];
                                micBytes = ReadFully(mic.Stream, micBuf, want);
                            }
                        }
                        MixPcm16(loopBuf.AsSpan(0, loopBytes), loop.Channels, loop.SampleRate,
                            micBuf.AsSpan(0, micBytes), mic.Channels, mic.SampleRate,
                            destBuf.AsSpan(0, loopBytes), frame, micStart);
                        if (!writer.Write(destBuf.AsSpan(0, loopBytes)))
                            break;
                        frame += n;
                    }
                    return frame > 0;
                }
            }
        }

        internal static byte[] MixPcm16(byte[] loopPcm, int loopCh, int loopRate, byte[] micPcm, int micCh, int micRate)
        {
            var dest = new byte[loopPcm.Length];
            MixPcm16(loopPcm, loopCh, loopRate, micPcm, micCh, micRate, dest, 0, 0);
            return dest;
        }

        internal static void MixPcm16(ReadOnlySpan<byte> loopPcm, int loopCh, int loopRate,
            ReadOnlySpan<byte> micPcm, int micCh, int micRate, Span<byte> dest,
            long loopFrameOrigin, long micFrameOrigin)
        {
            int loopStep = 2 * Math.Max(1, loopCh);
            int micStep = 2 * Math.Max(1, micCh);
            int loopFrames = loopPcm.Length / loopStep;
            int micFrames = micStep == 0 ? 0 : micPcm.Length / micStep;
            for (int i = 0; i < loopFrames; i++)
            {
                long micI = loopRate <= 0 ? 0 : (loopFrameOrigin + i) * micRate / loopRate - micFrameOrigin;
                short micS = 0;
                if (micI >= 0 && micI < micFrames)
                {
                    int off = (int)micI * micStep;
                    if (micCh <= 1)
                        micS = BinaryPrimitives.ReadInt16LittleEndian(micPcm.Slice(off, 2));
                    else
                    {
                        int l = BinaryPrimitives.ReadInt16LittleEndian(micPcm.Slice(off, 2));
                        int r = BinaryPrimitives.ReadInt16LittleEndian(micPcm.Slice(off + 2, 2));
                        micS = (short)((l + r) / 2);
                    }
                }
                for (int ch = 0; ch < loopCh; ch++)
                {
                    int loopS = BinaryPrimitives.ReadInt16LittleEndian(loopPcm.Slice(i * loopStep + ch * 2, 2));
                    int sum = loopS + micS;
                    if (sum > 32767) sum = 32767;
                    if (sum < -32768) sum = -32768;
                    BinaryPrimitives.WriteInt16LittleEndian(dest.Slice(i * loopStep + ch * 2, 2), (short)sum);
                }
            }
        }

        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = stream.Read(buffer, total, count - total);
                if (n <= 0)
                    break;
                total += n;
            }
            return total;
        }

        private static bool TryOpenPcm16(string path, FileShare share, out Pcm16File file)
        {
            file = null!;
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, share);
            try
            {
                if (fs.Length < PcmWavWriter.HeaderSize)
                {
                    fs.Dispose();
                    return false;
                }
                using var r = new BinaryReader(fs, System.Text.Encoding.ASCII, leaveOpen: true);
                if (new string(r.ReadChars(4)) != "RIFF")
                {
                    fs.Dispose();
                    return false;
                }
                r.ReadInt32();
                if (new string(r.ReadChars(4)) != "WAVE" || new string(r.ReadChars(4)) != "fmt ")
                {
                    fs.Dispose();
                    return false;
                }
                int fmtSize = r.ReadInt32();
                short format = r.ReadInt16();
                short channels = r.ReadInt16();
                int sampleRate = r.ReadInt32();
                r.ReadInt32();
                r.ReadInt16();
                short bits = r.ReadInt16();
                if (fmtSize > 16)
                    r.ReadBytes(fmtSize - 16);
                if (format != 1 || bits != 16 || channels < 1)
                {
                    fs.Dispose();
                    return false;
                }
                if (new string(r.ReadChars(4)) != "data")
                {
                    fs.Dispose();
                    return false;
                }
                r.ReadUInt32();
                if (sampleRate <= 0)
                {
                    fs.Dispose();
                    return false;
                }
                long dataStart = fs.Position;
                long dataBytes = Math.Min(fs.Length - dataStart, PcmWavWriter.MaxDataBytes);
                int blockAlign = channels * 2;
                dataBytes -= dataBytes % blockAlign;
                if (dataBytes <= 0)
                {
                    fs.Dispose();
                    return false;
                }
                file = new Pcm16File(fs, sampleRate, channels, dataStart, dataBytes);
                return true;
            }
            catch
            {
                fs.Dispose();
                return false;
            }
        }

        private sealed class Pcm16File : IDisposable
        {
            public Pcm16File(FileStream stream, int sampleRate, int channels, long dataStart, long dataBytes)
            {
                Stream = stream;
                SampleRate = sampleRate;
                Channels = channels;
                BlockAlign = channels * 2;
                DataStart = dataStart;
                DataBytes = dataBytes;
            }

            public FileStream Stream { get; }
            public int SampleRate { get; }
            public int Channels { get; }
            public int BlockAlign { get; }
            public long DataStart { get; }
            public long DataBytes { get; }

            public void Dispose() => Stream.Dispose();
        }

        private static bool TryTranscode(string sourcePath, string destPath, Func<MediaEncodingProfile> profile)
        {
            try
            {
                var encoding = profile();
                string? dir = Path.GetDirectoryName(destPath);
                if (string.IsNullOrEmpty(dir))
                    return false;
                Directory.CreateDirectory(dir);
                var folder = StorageFolder.GetFolderFromPathAsync(dir).AsTask().GetAwaiter().GetResult();
                var destFile = folder.CreateFileAsync(Path.GetFileName(destPath), CreationCollisionOption.ReplaceExisting)
                    .AsTask().GetAwaiter().GetResult();
                var sourceFile = StorageFile.GetFileFromPathAsync(sourcePath).AsTask().GetAwaiter().GetResult();
                var transcoder = new MediaTranscoder();
                var prepared = transcoder.PrepareFileTranscodeAsync(sourceFile, destFile, encoding)
                    .AsTask().GetAwaiter().GetResult();
                if (!prepared.CanTranscode)
                {
                    try { destFile.DeleteAsync().AsTask().GetAwaiter().GetResult(); } catch { /* ignore */ }
                    return false;
                }
                prepared.TranscodeAsync().AsTask().GetAwaiter().GetResult();
                return File.Exists(destPath) && new FileInfo(destPath).Length > 0;
            }
            catch
            {
                try { File.Delete(destPath); } catch { /* ignore */ }
                return false;
            }
        }
    }
}
