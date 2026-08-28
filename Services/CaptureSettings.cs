using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Immutable snapshot of the settings a single capture cycle reads. The loop reads it once per
    /// cycle so it never observes a half-applied change while the UI thread edits settings; a fresh
    /// snapshot is published atomically whenever settings change.
    /// </summary>
    internal sealed record CaptureSettings(
        CaptureRegion? Region,
        int CaptureIntervalMs,
        int UpscaleFactor,
        bool EnhanceForOcr,
        bool FocusSwitchEnabled,
        int FocusSettleMs,
        string TeamsWindowTitleHint,
        bool RequireTeamsWindow)
    {
        public static CaptureSettings From(AppSettings s) => new(
            s.Region,
            s.CaptureIntervalMs,
            s.UpscaleFactor,
            s.EnhanceForOcr,
            s.FocusSwitchEnabled,
            s.FocusSettleMs,
            s.TeamsWindowTitleHint,
            s.RequireTeamsWindow);
    }
}
