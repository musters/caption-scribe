using System;
using CaptionScribe.Models;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class TranscriptExportTests
    {
        [Fact]
        public void SuggestedFileName_UsesStampAndSanitizedTitle()
        {
            var started = new DateTime(2026, 8, 26, 9, 30, 0);
            Assert.Equal("2026-08-26-Meeting-09-30-Weekly Sync.txt",
                TranscriptExport.SuggestedFileName(started, "Weekly Sync"));
        }

        [Fact]
        public void SuggestedFileName_BlankTitle_IsStampOnly()
        {
            var started = new DateTime(2026, 8, 26, 9, 30, 0);
            Assert.Equal("2026-08-26-Meeting-09-30.txt",
                TranscriptExport.SuggestedFileName(started, "   "));
        }

        [Fact]
        public void SuggestedFileName_ReplacesInvalidFileNameChars()
        {
            var started = new DateTime(2026, 1, 1, 12, 0, 0);
            var name = TranscriptExport.SuggestedFileName(started, "a:b*c");
            Assert.DoesNotContain(":", name);
            Assert.DoesNotContain("*", name);
            Assert.EndsWith(".txt", name);
        }

        [Fact]
        public void Build_AsIsWithoutTimestamps_ReturnsPlain()
        {
            var timed = new[] { new TimedLine("hello", DateTime.Now) };
            Assert.Equal("plain", TranscriptExport.Build(SaveCleanup.AsIs, timestamps: false, perTurn: true, timed, "plain"));
        }

        [Fact]
        public void Build_AsIsWithTimestamps_FormatsTimedLines()
        {
            var t = new DateTime(2026, 1, 1, 9, 5, 23);
            var timed = new[] { new TimedLine("hello", t) };
            var text = TranscriptExport.Build(SaveCleanup.AsIs, timestamps: true, perTurn: false, timed, "ignored");
            Assert.Contains("[09:05:23]", text);
            Assert.Contains("hello", text);
        }

        [Fact]
        public void Build_Clean_FixesOcrGlyphs()
        {
            var timed = new[] { new TimedLine("I don\u2022t know", new DateTime(2026, 1, 1, 9, 0, 0)) };
            Assert.Equal("I don't know",
                TranscriptExport.Build(SaveCleanup.Clean, timestamps: false, perTurn: false, timed, "ignored"));
        }
    }
}
