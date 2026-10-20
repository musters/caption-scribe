using System;
using System.IO;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class PcmWavWriterTests : IDisposable
    {
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "CaptionScribeWav", Guid.NewGuid().ToString("N"));

        public PcmWavWriterTests() => Directory.CreateDirectory(_dir);

        [Fact]
        public void Dispose_WritesHeaderMatchingFileLength()
        {
            string path = Path.Combine(_dir, "scribe-1.wav");
            byte[] pcm = new byte[400];
            using (var writer = new PcmWavWriter(path, sampleRate: 8000, channels: 1))
                writer.Write(pcm);

            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            Assert.Equal("RIFF", new string(r.ReadChars(4)));
            Assert.Equal(fs.Length - 8, r.ReadUInt32());
            Assert.Equal("WAVE", new string(r.ReadChars(4)));
            fs.Position = 40;
            Assert.Equal((uint)pcm.Length, r.ReadUInt32());
            Assert.Equal(PcmWavWriter.HeaderSize + pcm.Length, fs.Length);
        }

        [Fact]
        public void FlushHeader_MakesPartialFilePlayable()
        {
            string path = Path.Combine(_dir, "scribe-2.wav");
            using var writer = new PcmWavWriter(path, 8000, 1);
            writer.Write(new byte[80]);
            writer.FlushHeader();

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new BinaryReader(fs);
            fs.Position = 40;
            Assert.Equal(80u, r.ReadUInt32());
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
