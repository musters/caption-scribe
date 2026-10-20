using System;
using System.IO;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class AudioAutoSaveTests : IDisposable
    {
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "CaptionScribeAudioAuto", Guid.NewGuid().ToString("N"));

        public AudioAutoSaveTests() => Directory.CreateDirectory(_dir);

        [Fact]
        public void WavPathForTranscript_UsesSameStem()
        {
            Assert.Equal(@"C:\a\scribe-1.wav", AudioAutoSave.WavPathForTranscript(@"C:\a\scribe-1.txt"));
            Assert.Equal(@"C:\a\scribe-1-mic.wav", AudioAutoSave.MicWavPathForTranscript(@"C:\a\scribe-1.txt"));
        }

        [Fact]
        public void TryRepairHeader_RewritesStaleDataSize()
        {
            string path = Path.Combine(_dir, "scribe-broken.wav");
            byte[] pcm = new byte[200];
            using (var writer = new PcmWavWriter(path, 8000, 1))
                writer.Write(pcm);

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Position = 4;
                fs.Write(BitConverter.GetBytes(36u));
                fs.Position = 40;
                fs.Write(BitConverter.GetBytes(0u));
            }

            Assert.True(AudioAutoSave.TryRepairHeader(path));
            using var read = File.OpenRead(path);
            using var r = new BinaryReader(read);
            Assert.Equal("RIFF", new string(r.ReadChars(4)));
            Assert.Equal((uint)(read.Length - 8), r.ReadUInt32());
            read.Position = 40;
            Assert.Equal((uint)pcm.Length, r.ReadUInt32());
        }

        [Fact]
        public void TryRepairHeader_AlreadyValid_ReturnsFalse()
        {
            string path = Path.Combine(_dir, "scribe-ok.wav");
            using (var writer = new PcmWavWriter(path, 8000, 1))
                writer.Write(new byte[40]);
            Assert.False(AudioAutoSave.TryRepairHeader(path));
        }

        [Fact]
        public void ClearDirectory_DeletesScribeWav_KeepsOthers()
        {
            File.WriteAllBytes(Path.Combine(_dir, "scribe-1.wav"), new byte[8]);
            File.WriteAllBytes(Path.Combine(_dir, "scribe-1-mic.wav"), new byte[8]);
            File.WriteAllText(Path.Combine(_dir, "notes.wav"), "keep");

            int removed = AudioAutoSave.ClearDirectory(_dir);

            Assert.Equal(2, removed);
            Assert.True(File.Exists(Path.Combine(_dir, "notes.wav")));
        }

        [Fact]
        public void CountFiles_CountsOnlyScribeWav()
        {
            File.WriteAllBytes(Path.Combine(_dir, "scribe-1.wav"), new byte[4]);
            File.WriteAllText(Path.Combine(_dir, "notes.wav"), "x");
            Assert.Equal(1, AudioAutoSave.CountFiles(_dir));
        }

        [Fact]
        public void TotalBytes_CountsOnlyScribeWav()
        {
            File.WriteAllBytes(Path.Combine(_dir, "scribe-1.wav"), new byte[4]);
            File.WriteAllBytes(Path.Combine(_dir, "scribe-1-mic.wav"), new byte[3]);
            File.WriteAllText(Path.Combine(_dir, "notes.wav"), "unrelated");
            Assert.Equal(7, AudioAutoSave.TotalBytes(_dir));
        }

        [Fact]
        public void DeleteOlderThan_RemovesOld_KeepsRecent()
        {
            var old = Path.Combine(_dir, "scribe-old.wav");
            var recent = Path.Combine(_dir, "scribe-new.wav");
            File.WriteAllBytes(old, new byte[4]);
            File.WriteAllBytes(recent, new byte[4]);
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));

            int removed = AudioAutoSave.DeleteOlderThan(_dir, AudioAutoSave.Retention);

            Assert.Equal(1, removed);
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }

        [Fact]
        public void RepairHeaders_RepairsMatchingFiles()
        {
            string path = Path.Combine(_dir, "scribe-fix.wav");
            using (var writer = new PcmWavWriter(path, 8000, 1))
                writer.Write(new byte[60]);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Position = 40;
                fs.Write(BitConverter.GetBytes(0u));
            }

            Assert.Equal(1, AudioAutoSave.RepairHeaders(_dir));
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
