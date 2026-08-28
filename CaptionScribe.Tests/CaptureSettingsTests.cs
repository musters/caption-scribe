using CaptionScribe.Models;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class CaptureSettingsTests
    {
        // Guards against adding a capture-relevant setting and forgetting to carry it into the snapshot.
        [Fact]
        public void From_CopiesEveryCaptureField()
        {
            var settings = new AppSettings
            {
                Region = new CaptureRegion { X = 1, Y = 2, Width = 3, Height = 4 },
                CaptureIntervalMs = 1234,
                UpscaleFactor = 3,
                EnhanceForOcr = false,
                FocusSwitchEnabled = true,
                FocusSettleMs = 250,
                TeamsWindowTitleHint = "Meet",
                RequireTeamsWindow = true,
            };

            var snap = CaptureSettings.From(settings);

            Assert.Same(settings.Region, snap.Region);
            Assert.Equal(1234, snap.CaptureIntervalMs);
            Assert.Equal(3, snap.UpscaleFactor);
            Assert.False(snap.EnhanceForOcr);
            Assert.True(snap.FocusSwitchEnabled);
            Assert.Equal(250, snap.FocusSettleMs);
            Assert.Equal("Meet", snap.TeamsWindowTitleHint);
            Assert.True(snap.RequireTeamsWindow);
        }
    }
}
