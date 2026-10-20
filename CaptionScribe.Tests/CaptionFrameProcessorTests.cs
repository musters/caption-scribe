using System.Collections.Generic;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class CaptionFrameProcessorTests
    {
        [Fact]
        public void SameTexts_NullPrevious_IsNotEqual()
        {
            Assert.False(CaptionFrameProcessor.SameTexts(new List<string> { "a" }, null));
        }

        [Fact]
        public void SameTexts_MatchingLists_AreEqual()
        {
            Assert.True(CaptionFrameProcessor.SameTexts(new List<string> { "a", "b" }, new[] { "a", "b" }));
        }

        [Fact]
        public void SameTexts_DifferentLengthOrContent_AreNotEqual()
        {
            Assert.False(CaptionFrameProcessor.SameTexts(new List<string> { "a" }, new[] { "a", "b" }));
            Assert.False(CaptionFrameProcessor.SameTexts(new List<string> { "a" }, new[] { "b" }));
        }

        [Fact]
        public void ScaleLayout_DividesBoxesByScale()
        {
            var scaled = CaptionFrameProcessor.ScaleLayout(
                new[] { new RecognizedLine("hi", 10, 20, 30, 40) }, 2);
            Assert.Single(scaled);
            Assert.Equal("hi", scaled[0].Text);
            Assert.Equal(5, scaled[0].X);
            Assert.Equal(10, scaled[0].Y);
            Assert.Equal(15, scaled[0].Width);
            Assert.Equal(20, scaled[0].Height);
        }
    }
}
