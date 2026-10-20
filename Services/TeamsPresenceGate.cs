using System;
using CaptionScribe.Core.Logging;

namespace CaptionScribe.Services
{
    /// <summary>
    /// When "require Teams window" is on (and we're not force-focusing Teams), skip OCR unless a
    /// Teams window is in front of the region. Prevents trailing text after a meeting window closes.
    /// </summary>
    internal sealed class TeamsPresenceGate
    {
        private readonly WindowService _windows;
        private readonly ILog _log;
        private bool _missingNotified;

        public TeamsPresenceGate(WindowService windows, ILog log)
        {
            _windows = windows;
            _log = log;
        }

        public void Reset() => _missingNotified = false;

        public bool Allow(CaptureSettings s, Action<string> onError)
        {
            if (!s.RequireTeamsWindow || s.FocusSwitchEnabled)
                return true;

            var region = s.Region!.Value;
            int cx = region.X + region.Width / 2;
            int cy = region.Y + region.Height / 2;
            bool present = _windows.IsTeamsAtPoint(cx, cy);

            if (!present)
            {
                if (!_missingNotified)
                {
                    _missingNotified = true;
                    _log.Info("Capture paused: Teams is not in front of the region.");
                    onError("Teams isn't in front of the capture region — pausing capture until it returns.");
                }
            }
            else if (_missingNotified)
            {
                _missingNotified = false;
                _log.Info("Capture resumed: Teams is in front of the region.");
            }
            return present;
        }
    }
}
