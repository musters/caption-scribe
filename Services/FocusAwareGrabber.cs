using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using CaptionScribe.Core.Interop;

namespace CaptionScribe.Services
{
    /// <summary>Grabs a screen region, optionally activating Teams first so the caption window is on top.</summary>
    internal sealed class FocusAwareGrabber
    {
        private readonly ScreenCaptureService _capture;
        private readonly WindowService _windows;

        public FocusAwareGrabber(ScreenCaptureService capture, WindowService windows)
        {
            _capture = capture;
            _windows = windows;
        }

        public async Task<Bitmap> GrabAsync(CaptureSettings s, CancellationToken token)
        {
            var region = s.Region!.Value;
            if (!s.FocusSwitchEnabled)
                return _capture.CaptureRaw(region);

            IntPtr teams = _windows.FindTeamsWindow(s.TeamsWindowTitleHint);
            if (teams == IntPtr.Zero || Win32.IsForeground(teams))
                return _capture.CaptureRaw(region);

            IntPtr previous = Win32.GetForegroundWindowHandle();
            bool activated = Win32.TryActivate(teams);
            try
            {
                if (activated && s.FocusSettleMs > 0)
                    await Task.Delay(s.FocusSettleMs, token);
                token.ThrowIfCancellationRequested();
                return _capture.CaptureRaw(region);
            }
            finally
            {
                if (activated && previous != IntPtr.Zero)
                    Win32.TryActivate(previous);
            }
        }
    }
}
