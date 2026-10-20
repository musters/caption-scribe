using System;
using System.IO;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class AudioExportTests
    {
        [Fact]
        public void HasUsefulWav_FalseForMissingOrHeaderOnly()
        {
            Assert.False(AudioExport.HasUsefulWav(null));
            Assert.False(AudioExport.HasUsefulWav(@"C:\no-such-scribe.wav"));
            string path = Path.Combine(Path.GetTempPath(), "CaptionScribeHdr-" + Path.GetRandomFileName() + ".wav");
            try
            {
                using (new PcmWavWriter(path, 8000, 1)) { }
                Assert.False(AudioExport.HasUsefulWav(path));
            }
            finally { try { File.Delete(path); } catch { /* ignore */ } }
        }

        [Fact]
        public void HasUsefulWav_TrueWhenPcmPresent()
        {
            string path = Path.Combine(Path.GetTempPath(), "CaptionScribePcm-" + Path.GetRandomFileName() + ".wav");
            try
            {
                using (var writer = new PcmWavWriter(path, 8000, 1))
                    writer.Write(new byte[80]);
                Assert.True(AudioExport.HasUsefulWav(path));
            }
            finally { try { File.Delete(path); } catch { /* ignore */ } }
        }

        [Fact]
        public void MixPcm16_AddsMicIntoEveryLoopbackChannel_ClipSafe()
        {
            var loop = new byte[8];
            BitConverter.GetBytes((short)20000).CopyTo(loop, 0);
            BitConverter.GetBytes((short)20000).CopyTo(loop, 2);
            BitConverter.GetBytes((short)100).CopyTo(loop, 4);
            BitConverter.GetBytes((short)100).CopyTo(loop, 6);
            var mic = new byte[4];
            BitConverter.GetBytes((short)20000).CopyTo(mic, 0);
            BitConverter.GetBytes((short)50).CopyTo(mic, 2);

            byte[] mixed = AudioExport.MixPcm16(loop, loopCh: 2, loopRate: 8000, mic, micCh: 1, micRate: 8000);

            Assert.Equal(32767, BitConverter.ToInt16(mixed, 0));
            Assert.Equal(32767, BitConverter.ToInt16(mixed, 2));
            Assert.Equal(150, BitConverter.ToInt16(mixed, 4));
            Assert.Equal(150, BitConverter.ToInt16(mixed, 6));
        }

        [Fact]
        public void TryMixWav_StreamsWithoutRequiringWholeFileInCaller()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CaptionScribeMix-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string loop = Path.Combine(dir, "loop.wav");
                string mic = Path.Combine(dir, "mic.wav");
                string dest = Path.Combine(dir, "mix.wav");
                using (var w = new PcmWavWriter(loop, 8000, 1))
                    w.Write(new byte[] { 0x10, 0x00, 0x20, 0x00 });
                using (var w = new PcmWavWriter(mic, 8000, 1))
                    w.Write(new byte[] { 0x05, 0x00, 0x01, 0x00 });
                Assert.True(AudioExport.TryMixWav(loop, mic, dest));
                Assert.True(new FileInfo(dest).Length > PcmWavWriter.HeaderSize);
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ } }
        }

        [Fact]
        public void TryCreateStableSnapshot_CapturesFixedPrefixOfOpenWriter()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CaptionScribeSnapshot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string source = Path.Combine(dir, "live.wav");
                string snapshot = Path.Combine(dir, "snapshot.wav");
                using (var writer = new PcmWavWriter(source, 8000, 1))
                {
                    Assert.True(writer.Write(new byte[] { 0x10, 0x00, 0x20, 0x00 }));
                    writer.FlushHeader();
                    Assert.Equal(48, new FileInfo(source).Length);
                    Assert.True(AudioExport.TryCreateStableSnapshot(source, snapshot));
                    Assert.True(writer.Write(new byte[] { 0x30, 0x00, 0x40, 0x00 }));
                }

                using var fs = File.OpenRead(snapshot);
                using var reader = new BinaryReader(fs);
                Assert.Equal("RIFF", new string(reader.ReadChars(4)));
                reader.ReadUInt32();
                Assert.Equal("WAVE", new string(reader.ReadChars(4)));
                fs.Position = 40;
                Assert.Equal(4u, reader.ReadUInt32());
                Assert.Equal(48, fs.Length);
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ } }
        }
    }
}
