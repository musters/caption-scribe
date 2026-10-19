using CaptionScribe.ViewModels;
using Xunit;

namespace CaptionScribe.Tests
{
    public class AboutViewModelTests
    {
        [Fact]
        public void FormatsVersionAndAutosavePath()
        {
            var vm = new AboutViewModel(@"C:\autosave");
            Assert.StartsWith("Version ", vm.Version);
            Assert.Contains(@"C:\autosave", vm.AutoSavePath);
        }
    }
}
