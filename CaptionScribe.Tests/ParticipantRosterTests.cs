using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class ParticipantRosterTests
    {
        // A frame whose left region varies (gradient) => an avatar is present beside each name.
        private static Bitmap Frame()
        {
            var bmp = new Bitmap(200, 300, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            using var brush = new LinearGradientBrush(new Rectangle(0, 0, 200, 300), Color.Black, Color.White, 45f);
            g.FillRectangle(brush, 0, 0, 200, 300);
            return bmp;
        }

        // A uniformly white frame => a name-shaped line with no avatar beside it (like a shared-item title).
        private static Bitmap BlankFrame()
        {
            var bmp = new Bitmap(200, 300, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            return bmp;
        }

        // A name-shaped line whose avatar crop (left of the text) lands inside the frame.
        private static RecognizedLine NameLine(string text, double y = 100) =>
            new(text, X: 60, Y: y, Width: 90, Height: 20);

        [Fact]
        public void Observe_AddsOneParticipant_PerDistinctName()
        {
            var roster = new ParticipantRoster();
            using var frame = Frame();

            var lines = new List<RecognizedLine>
            {
                NameLine("Testy McTestface", 100),
                NameLine("Zippy Zapp", 160),
            };

            roster.Observe(frame, lines);
            roster.Observe(frame, lines);   // a name must be seen at least twice to count

            Assert.Equal(2, roster.Count);
        }

        [Fact]
        public void Observe_IgnoresNonNameLines()
        {
            var roster = new ParticipantRoster();
            using var frame = Frame();

            roster.Observe(frame, new List<RecognizedLine>
            {
                NameLine("Okay.", 100),                      // trailing punctuation -> not a name
                NameLine("Accessibility scanner was", 160),  // mixed case -> not a name
            });

            Assert.Equal(0, roster.Count);
        }

        [Fact]
        public void Observe_SkipsAValidName_WhenNoAvatarIsPresent()
        {
            var roster = new ParticipantRoster();
            using var frame = BlankFrame();   // valid name shape, but blank (no avatar) to its left

            roster.Observe(frame, new List<RecognizedLine> { NameLine("Ada Placeholder") });

            Assert.Equal(0, roster.Count);
        }

        [Fact]
        public void Observe_DeduplicatesTheSameNameAcrossFrames()
        {
            var roster = new ParticipantRoster();
            using var f1 = Frame();
            using var f2 = Frame();

            roster.Observe(f1, new List<RecognizedLine> { NameLine("Zippy Zapp") });
            roster.Observe(f2, new List<RecognizedLine> { NameLine("Zippy Zapp") });

            Assert.Equal(1, roster.Count);
        }

        [Fact]
        public void Observe_FuzzyMatches_AndMajorityVotesTheBestName()
        {
            var roster = new ParticipantRoster();
            using var frame = Frame();

            roster.Observe(frame, new List<RecognizedLine> { NameLine("Testy McTestfoce") }); // 1 vote (OCR slip)
            roster.Observe(frame, new List<RecognizedLine> { NameLine("Testy McTestface") }); // 2 votes
            roster.Observe(frame, new List<RecognizedLine> { NameLine("Testy McTestface") });

            Assert.Equal(1, roster.Count);
            Assert.Equal("Testy McTestface", roster.Snapshot()[0].Name);
        }

        [Fact]
        public void Snapshot_CarriesANonEmptyAvatarCrop()
        {
            var roster = new ParticipantRoster();
            using var frame = Frame();
            var lines = new List<RecognizedLine> { NameLine("Zippy Zapp") };

            roster.Observe(frame, lines);
            roster.Observe(frame, lines);

            Assert.NotEmpty(roster.Snapshot()[0].AvatarPng);
        }

        [Fact]
        public void Observe_RequiresAtLeastTwoSightings()
        {
            var roster = new ParticipantRoster();
            using var frame = Frame();
            var lines = new List<RecognizedLine> { NameLine("Zippy Zapp") };

            roster.Observe(frame, lines);
            Assert.Equal(0, roster.Count);   // seen once -> not yet counted

            roster.Observe(frame, lines);
            Assert.Equal(1, roster.Count);   // seen twice -> counted
        }

        [Theory]
        [InlineData("Lil<e WI")]
        [InlineData("Tirusn I")]
        [InlineData("But I")]
        public void Observe_RejectsOcrGarbageNames(string garbage)
        {
            var roster = new ParticipantRoster();
            using var frame = Frame();
            var lines = new List<RecognizedLine> { NameLine(garbage) };

            roster.Observe(frame, lines);
            roster.Observe(frame, lines);

            Assert.Equal(0, roster.Count);
        }

        [Fact]
        public void Clear_EmptiesTheRoster()
        {
            var roster = new ParticipantRoster();
            using var frame = Frame();
            roster.Observe(frame, new List<RecognizedLine> { NameLine("Zippy Zapp") });

            roster.Clear();

            Assert.Equal(0, roster.Count);
            Assert.Empty(roster.Snapshot());
        }
    }
}
