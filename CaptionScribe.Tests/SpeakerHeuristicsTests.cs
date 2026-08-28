using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class SpeakerHeuristicsTests
    {
        [Theory]
        [InlineData("Testy McTestface")]
        [InlineData("John Q Public")]
        [InlineData("Ann Marie De Vries")]
        public void LooksLikeName_TrueForNameShapedLines(string line)
            => Assert.True(SpeakerHeuristics.LooksLikeName(line));

        [Theory]
        [InlineData("")]
        [InlineData("Testy")]                    // one word
        [InlineData("hello there")]              // lowercase
        [InlineData("Testy McTestface.")]        // ends with punctuation
        [InlineData("One Two Three Four Five")]  // five words
        [InlineData("Testy mctestface")]         // second word lowercase
        public void LooksLikeName_FalseForOtherLines(string line)
            => Assert.False(SpeakerHeuristics.LooksLikeName(line));
    }
}
