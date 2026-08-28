using System;
using CaptionScribe.Models;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class TranscriptCleanerTests
    {
        private static readonly string NL = Environment.NewLine;
        private static readonly DateTime T = new(2026, 1, 1, 9, 0, 0);
        private static TimedLine Line(string text) => new(text, T);

        [Fact]
        public void FixesBulletGlyphToApostrophe()
        {
            var result = TranscriptCleaner.Clean(new[] { Line("I don\u2022t know") }, withTimestamps: false);
            Assert.Equal("I don't know", result);
        }

        [Fact]
        public void FixesDigitLookAlikeInMixedWord()
        {
            var result = TranscriptCleaner.Clean(new[] { Line("0K then") }, withTimestamps: false);
            Assert.Equal("OK then", result);
        }

        [Fact]
        public void LeavesPureNumbersAlone()
        {
            var result = TranscriptCleaner.Clean(new[] { Line("meet at 500") }, withTimestamps: false);
            Assert.Equal("meet at 500", result);
        }

        [Fact]
        public void FixesCommonOcrNonWords()
        {
            var result = TranscriptCleaner.Clean(new[] { Line("tne new teatures") }, withTimestamps: false);
            Assert.Equal("the new features", result);
        }

        [Fact]
        public void FixesFToTMisreads()
        {
            var result = TranscriptCleaner.Clean(
                new[] { Line("that's so Tunny, it ran trom home to Tor refuge") },
                withTimestamps: false);
            Assert.Equal("that's so funny, it ran from home to for refuge", result);
        }

        [Fact]
        public void CollapsesRepeatedSpeakerNames()
        {
            var lines = new[]
            {
                Line("Testy McTestface"),
                Line("hello."),
                Line("Testy McTestface"),
                Line("Testy McTestface"),
                Line("world."),
                Line("Testy McTestface"),
                Line("bye."),
            };
            var result = TranscriptCleaner.Clean(lines, withTimestamps: false);
            Assert.Equal(string.Join(NL, "Testy McTestface", "hello.", "world.", "bye."), result);
        }

        [Fact]
        public void DoesNotCollapseNamesSeenFewerThanThreeTimes()
        {
            var lines = new[]
            {
                Line("Testy McTestface"),
                Line("hello."),
                Line("Testy McTestface"),   // only twice total -> not treated as a recurring speaker header
                Line("world."),
            };
            var result = TranscriptCleaner.Clean(lines, withTimestamps: false);
            Assert.Equal(string.Join(NL, "Testy McTestface", "hello.", "Testy McTestface", "world."), result);
        }
    }
}
