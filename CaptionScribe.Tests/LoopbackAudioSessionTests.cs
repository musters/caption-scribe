using System;
using System.IO;
using System.Threading;
using CaptionScribe.Core.Logging;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class LoopbackAudioSessionTests : IDisposable
    {
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "CaptionScribeLoopback", Guid.NewGuid().ToString("N"));

        public LoopbackAudioSessionTests() => Directory.CreateDirectory(_dir);

        [Fact]
        public void DefaultSilenceDetectionWindow_IsThreeMinutes()
        {
            Assert.Equal(TimeSpan.FromMinutes(3), LoopbackAudioSession.DefaultSilentAfter);
        }

        [Fact]
        public void Start_CreatesWav_PauseKeepsFile_ResumeAddsSilence()
        {
            string path = Path.Combine(_dir, "scribe-1.wav");
            using var session = new LoopbackAudioSession(NullLog.Instance, new SilentLoopbackFactory());
            session.EnqueueStart(path);
            session.WaitForIdle();
            WaitForFile(path);

            long afterStart = new FileInfo(path).Length;
            Assert.True(afterStart >= PcmWavWriter.HeaderSize);

            session.EnqueuePause();
            session.WaitForIdle();
            long afterPause = new FileInfo(path).Length;

            session.EnqueueStart(path);
            session.WaitForIdle();
            Thread.Sleep(50);

            Assert.True(new FileInfo(path).Length >= afterPause + 12_000);
            Assert.Equal(afterStart, afterPause);
        }

        [Fact]
        public void Close_ThenStart_UsesAFreshWriter()
        {
            string first = Path.Combine(_dir, "scribe-a.wav");
            string second = Path.Combine(_dir, "scribe-b.wav");
            using var session = new LoopbackAudioSession(NullLog.Instance, new SilentLoopbackFactory());
            session.EnqueueStart(first);
            session.WaitForIdle();
            WaitForFile(first);

            session.EnqueueClose();
            session.WaitForIdle();
            session.EnqueueStart(second);
            session.WaitForIdle();
            WaitForFile(second);
            Assert.True(File.Exists(second));
        }

        [Fact]
        public void SilentCapture_MarksApparentlySilent()
        {
            string path = Path.Combine(_dir, "scribe-silent.wav");
            using var session = new LoopbackAudioSession(NullLog.Instance, new SilentLoopbackFactory(),
                silentAfter: TimeSpan.FromMilliseconds(80));
            session.EnqueueStart(path);
            session.WaitForIdle();
            WaitForFile(path);
            Thread.Sleep(150);
            Assert.True(session.ApparentlySilent);
        }

        [Fact]
        public void HeardSound_DoesNotMarkSilentDuringQuiet()
        {
            string path = Path.Combine(_dir, "scribe-heard.wav");
            using var session = new LoopbackAudioSession(NullLog.Instance, new OneShotLoudFactory(),
                silentAfter: TimeSpan.FromMilliseconds(80));
            session.EnqueueStart(path);
            session.WaitForIdle();
            WaitForFile(path);
            Thread.Sleep(150);
            Assert.False(session.ApparentlySilent);
        }

        [Fact]
        public void Start_WhenFactoryThrows_FailsSoft()
        {
            using var session = new LoopbackAudioSession(NullLog.Instance, new ThrowingLoopbackFactory());
            session.EnqueueStart(Path.Combine(_dir, "scribe-x.wav"));
            session.WaitForIdle();
            Thread.Sleep(50);
            Assert.True(session.Failed);
            Assert.False(session.IsCapturing);
        }

        private static void WaitForFile(string path)
        {
            for (int i = 0; i < 50; i++)
            {
                if (File.Exists(path) && new FileInfo(path).Length >= PcmWavWriter.HeaderSize)
                    return;
                Thread.Sleep(20);
            }
            Assert.Fail("WAV was not created.");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
        }

        private sealed class SilentLoopbackFactory : ILoopbackCaptureFactory
        {
            public ILoopbackCapture Start() => new SilentLoopback();
        }

        private sealed class OneShotLoudFactory : ILoopbackCaptureFactory
        {
            public ILoopbackCapture Start() => new OneShotLoud();
        }

        private sealed class OneShotLoud : ILoopbackCapture
        {
            private int _reads;
            public int SampleRate => 8000;
            public int Channels => 1;
            public bool IsFloat => false;
            public int BitsPerSample => 16;
            public int MaxPacketBytes => 4096;

            public int Read(byte[] buffer, out int frames)
            {
                if (_reads++ == 0)
                {
                    frames = 1;
                    BitConverter.GetBytes((short)20000).CopyTo(buffer, 0);
                    return 2;
                }
                frames = 0;
                Thread.Sleep(15);
                return 0;
            }

            public void Dispose() { }
        }

        private sealed class ThrowingLoopbackFactory : ILoopbackCaptureFactory
        {
            public ILoopbackCapture Start() => throw new InvalidOperationException("no device");
        }

        private sealed class SilentLoopback : ILoopbackCapture
        {
            public int SampleRate => 8000;
            public int Channels => 1;
            public bool IsFloat => false;
            public int BitsPerSample => 16;
            public int MaxPacketBytes => 4096;

            public int Read(byte[] buffer, out int frames)
            {
                frames = 0;
                Thread.Sleep(15);
                return 0;
            }

            public void Dispose() { }
        }
    }
}
