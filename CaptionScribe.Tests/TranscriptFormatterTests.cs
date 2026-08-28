using System;
using System.Linq;
using CaptionScribe.Models;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class TranscriptFormatterTests
    {
        private static readonly string NL = Environment.NewLine;
        private static readonly string Indent = new(' ', 11);   // width of "[HH:mm:ss] "
        private static DateTime At(int second) => new(2026, 1, 1, 9, 5, second);

        [Fact]
        public void NoTimestamps_JoinsLines()
        {
            var lines = new[] { new TimedLine("hello", At(1)), new TimedLine("world", At(2)) };
            Assert.Equal("hello" + NL + "world", TranscriptFormatter.Format(lines, withTimestamps: false));
        }

        [Fact]
        public void Timestamps_ShownOnChangeOfSecond()
        {
            var lines = new[]
            {
                new TimedLine("aaa", At(23)),
                new TimedLine("bbb", At(23)),
                new TimedLine("ccc", At(24)),
            };
            var expected = string.Join(NL,
                "[09:05:23] aaa",
                Indent + "bbb",
                "[09:05:24] ccc");
            Assert.Equal(expected, TranscriptFormatter.Format(lines, withTimestamps: true));
        }

        [Fact]
        public void PerTurn_StampsOnlyFirstLineAndSpeakerHeaders()
        {
            var t = At(23);
            var lines = new[]
            {
                new TimedLine("Testy McTestface", t),
                new TimedLine("hello there.", t),
                new TimedLine("Zippy Zapp", t),
                new TimedLine("hi.", t),
            };
            var expected = string.Join(NL,
                "[09:05:23] Testy McTestface",
                Indent + "hello there.",
                "[09:05:23] Zippy Zapp",
                Indent + "hi.");
            Assert.Equal(expected, TranscriptFormatter.Format(lines, withTimestamps: true, perTurn: true));
        }

        [Fact]
        public void EmptyList_ReturnsEmptyString()
        {
            var none = Array.Empty<TimedLine>();
            Assert.Equal("", TranscriptFormatter.Format(none, withTimestamps: false));
            Assert.Equal("", TranscriptFormatter.Format(none, withTimestamps: true));
            Assert.Equal("", TranscriptFormatter.Format(none, withTimestamps: true, perTurn: true));
        }

        [Fact]
        public void SingleLine_WithTimestamps_IsStamped()
        {
            var lines = new[] { new TimedLine("only", At(7)) };
            Assert.Equal("[09:05:07] only", TranscriptFormatter.Format(lines, withTimestamps: true));
        }
    }
}
