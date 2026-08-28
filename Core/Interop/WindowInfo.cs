using System;

namespace CaptionScribe.Core.Interop
{
    internal sealed record WindowInfo(
        IntPtr Handle,
        string Title,
        string ProcessName,
        uint ProcessId,
        bool IsMinimized);
}
