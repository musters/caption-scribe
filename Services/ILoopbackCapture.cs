using System;

namespace CaptionScribe.Services
{
    internal interface ILoopbackCapture : IDisposable
    {
        int SampleRate { get; }
        int Channels { get; }
        bool IsFloat { get; }
        int BitsPerSample { get; }
        int MaxPacketBytes { get; }

        /// <summary>Copy one mix-format packet into <paramref name="buffer"/>. Returns bytes copied; 0 if none.</summary>
        int Read(byte[] buffer, out int frames);
    }

    internal interface ILoopbackCaptureFactory
    {
        ILoopbackCapture Start();
    }
}
