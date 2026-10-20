using System;
using System.IO;
using System.Text;

namespace CaptionScribe.Services
{
    /// <summary>Autosave-folder helpers for crash-safe <c>scribe-*.wav</c> files. Directory matches text autosave.</summary>
    internal static class AudioAutoSave
    {
        public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
        private const string WavPattern = "scribe-*.wav";

        public static string DirectoryFor(string? configuredDir) => TranscriptAutoSaver.DirectoryFor(configuredDir);

        public static string WavPathForTranscript(string transcriptPath)
            => Path.ChangeExtension(transcriptPath, ".wav");

        public static string MicWavPathForTranscript(string transcriptPath)
        {
            string dir = Path.GetDirectoryName(transcriptPath) ?? "";
            string stem = Path.GetFileNameWithoutExtension(transcriptPath);
            return Path.Combine(dir, stem + "-mic.wav");
        }

        public static int CountFiles(string? configuredDir)
        {
            var dir = DirectoryFor(configuredDir);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, WavPattern).Length : 0;
        }

        public static long TotalBytes(string? configuredDir)
        {
            var dir = DirectoryFor(configuredDir);
            if (!Directory.Exists(dir))
                return 0;
            long total = 0;
            foreach (var file in Directory.GetFiles(dir, WavPattern))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* skip an unreadable file */ }
            }
            return total;
        }

        public static int ClearDirectory(string? configuredDir)
        {
            var dir = DirectoryFor(configuredDir);
            if (!Directory.Exists(dir))
                return 0;
            int deleted = 0;
            foreach (var file in Directory.GetFiles(dir, WavPattern))
            {
                try { File.Delete(file); deleted++; }
                catch { /* skip a locked file */ }
            }
            return deleted;
        }

        public static int DeleteOlderThan(string? configuredDir, TimeSpan age)
        {
            var dir = DirectoryFor(configuredDir);
            if (!Directory.Exists(dir))
                return 0;
            var cutoff = DateTime.UtcNow - age;
            int deleted = 0;
            foreach (var file in Directory.GetFiles(dir, WavPattern))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
                catch { /* skip a locked file */ }
            }
            return deleted;
        }

        public static int RepairHeaders(string? configuredDir)
        {
            var dir = DirectoryFor(configuredDir);
            if (!Directory.Exists(dir))
                return 0;
            int repaired = 0;
            foreach (var file in Directory.GetFiles(dir, WavPattern))
            {
                try
                {
                    if (TryRepairHeader(file))
                        repaired++;
                }
                catch { /* skip unreadable files */ }
            }
            return repaired;
        }

        public static bool TryRepairHeader(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (fs.Length < PcmWavWriter.HeaderSize)
                return false;

            using var reader = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
            if (!ReadFour(reader, "RIFF") || fs.Length < 12)
                return false;
            reader.ReadUInt32();
            if (!ReadFour(reader, "WAVE") || !ReadFour(reader, "fmt "))
                return false;
            if (reader.ReadInt32() != 16)
                return false;
            if (reader.ReadUInt16() != 1)
                return false;
            ushort channels = reader.ReadUInt16();
            int sampleRate = reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadUInt16();
            if (reader.ReadUInt16() != PcmWavWriter.BitsPerSample)
                return false;
            if (!ReadFour(reader, "data"))
                return false;
            uint oldData = reader.ReadUInt32();
            ulong raw = fs.Length > PcmWavWriter.HeaderSize ? (ulong)(fs.Length - PcmWavWriter.HeaderSize) : 0;
            uint dataBytes = raw > PcmWavWriter.MaxDataBytes ? PcmWavWriter.MaxDataBytes : (uint)raw;
            if (oldData == dataBytes)
                return false;
            if (channels is not (1 or 2) || sampleRate <= 0)
                return false;

            int blockAlign = channels * (PcmWavWriter.BitsPerSample / 8);
            int byteRate = sampleRate * blockAlign;
            uint riffSize = dataBytes + PcmWavWriter.HeaderSize - 8;
            fs.Position = 0;
            using var w = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(riffSize);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((ushort)1);
            w.Write(channels);
            w.Write(sampleRate);
            w.Write(byteRate);
            w.Write((ushort)blockAlign);
            w.Write((ushort)PcmWavWriter.BitsPerSample);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);
            w.Flush();
            fs.Flush(flushToDisk: true);
            return true;
        }

        private static bool ReadFour(BinaryReader reader, string expected)
        {
            var bytes = reader.ReadBytes(4);
            return bytes.Length == 4 && Encoding.ASCII.GetString(bytes) == expected;
        }
    }
}
