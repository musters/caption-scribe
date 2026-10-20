using CaptionScribe.Core.Logging;
using CaptionScribe.Models;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class TeamsPresenceGateTests
    {
        private static CaptureSettings Settings(bool requireTeams, bool focusSwitch)
            => new(new CaptureRegion(0, 0, 10, 10), 1000, 1, false, focusSwitch, 0, "", requireTeams);

        [Fact]
        public void Allow_WhenNotRequired_IsTrue()
        {
            var gate = new TeamsPresenceGate(new WindowService(), NullLog.Instance);
            Assert.True(gate.Allow(Settings(requireTeams: false, focusSwitch: false), _ => { }));
        }

        [Fact]
        public void Allow_WhenFocusSwitchEnabled_IsTrue()
        {
            var gate = new TeamsPresenceGate(new WindowService(), NullLog.Instance);
            Assert.True(gate.Allow(Settings(requireTeams: true, focusSwitch: true), _ => { }));
        }
    }
}
