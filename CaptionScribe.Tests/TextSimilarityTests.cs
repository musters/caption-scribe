using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class TextSimilarityTests
    {
        [Fact]
        public void IdenticalStrings_ScoreOne() =>
            Assert.Equal(1.0, TextSimilarity.Ratio("Testy McTestface", "Testy McTestface"), 3);

        [Fact]
        public void CaseInsensitive() =>
            Assert.Equal(1.0, TextSimilarity.Ratio("Zippy Zapp", "zippy zapp"), 3);

        [Fact]
        public void SmallOcrDrift_ScoresHigh()
        {
            // one-character OCR slip should stay well above the roster's 0.72 match threshold
            Assert.True(TextSimilarity.Ratio("Testy McTestface", "Testy McTestfoce") > 0.85);
        }

        [Fact]
        public void DifferentNames_ScoreLow()
        {
            Assert.True(TextSimilarity.Ratio("Zippy Zapp", "Testy McTestface") < 0.5);
        }

        [Fact]
        public void BothEmpty_ScoreOne() =>
            Assert.Equal(1.0, TextSimilarity.Ratio("", ""), 3);

        [Fact]
        public void OneEmpty_ScoreZero() =>
            Assert.Equal(0.0, TextSimilarity.Ratio("", "abc"), 3);
    }
}
