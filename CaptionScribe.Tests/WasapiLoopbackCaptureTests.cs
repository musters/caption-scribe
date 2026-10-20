using System;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class WasapiLoopbackCaptureTests
    {
        [Fact]
        public void PreferProcess_UsesProcessWhenPidKnown()
        {
            var used = WasapiLoopbackCapture.PreferProcess(42, pid => new Marker(pid), () => new Marker(0));
            Assert.Equal(42u, ((Marker)used).Pid);
        }

        [Fact]
        public void PreferProcess_FallsBackToDeviceWhenProcessThrows()
        {
            var used = WasapiLoopbackCapture.PreferProcess(9, _ => throw new InvalidOperationException("no"),
                () => new Marker(0));
            Assert.Equal(0u, ((Marker)used).Pid);
        }

        [Fact]
        public void PreferProcess_UsesDeviceWhenPidIsZero()
        {
            var used = WasapiLoopbackCapture.PreferProcess(0, pid => new Marker(pid), () => new Marker(7));
            Assert.Equal(7u, ((Marker)used).Pid);
        }

        private sealed class Marker : ILoopbackCapture
        {
            public Marker(uint pid) => Pid = pid;
            public uint Pid { get; }
            public int SampleRate => 8000;
            public int Channels => 1;
            public bool IsFloat => false;
            public int BitsPerSample => 16;
            public int MaxPacketBytes => 4096;
            public int Read(byte[] buffer, out int frames) { frames = 0; return 0; }
            public void Dispose() { }
        }
    }
}
