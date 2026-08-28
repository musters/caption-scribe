using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class ParticipantCollectorTests
    {
        // A frame whose left region varies (gradient) => an avatar is present beside each name.
        private static Bitmap AvatarFrame()
        {
            var bmp = new Bitmap(200, 300, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            using var brush = new LinearGradientBrush(new Rectangle(0, 0, 200, 300), Color.Black, Color.White, 45f);
            g.FillRectangle(brush, 0, 0, 200, 300);
            return bmp;
        }

        private static RecognizedLine NameLine(string text) => new(text, X: 60, Y: 100, Width: 90, Height: 20);

        [Fact]
        public void Start_And_Stop_ToggleWantsFrames()
        {
            var pc = new ParticipantCollector();
            Assert.False(pc.WantsFrames);

            pc.Start();
            Assert.True(pc.WantsFrames);

            pc.Stop();
            Assert.False(pc.WantsFrames);
        }

        [Fact]
        public void OnFrame_CollectsParticipantsFromNameRows()
        {
            var pc = new ParticipantCollector();
            using var frame = AvatarFrame();
            var lines = new List<RecognizedLine> { NameLine("Zippy Zapp") };

            pc.OnFrame(frame, lines);
            pc.OnFrame(frame, lines);   // must be seen at least twice to count

            Assert.Equal(1, pc.Count);
        }

        [Fact]
        public void Reset_ClearsCollectedParticipants()
        {
            var pc = new ParticipantCollector();
            using var frame = AvatarFrame();
            var lines = new List<RecognizedLine> { NameLine("Zippy Zapp") };
            pc.OnFrame(frame, lines);
            pc.OnFrame(frame, lines);
            Assert.Equal(1, pc.Count);

            pc.Reset();

            Assert.Equal(0, pc.Count);
        }

        [Fact]
        public void WriteImage_WritesAPngFile()
        {
            var pc = new ParticipantCollector();
            using (var frame = AvatarFrame())
            {
                var lines = new List<RecognizedLine> { NameLine("Zippy Zapp") };
                pc.OnFrame(frame, lines);
                pc.OnFrame(frame, lines);
            }

            var path = Path.Combine(Path.GetTempPath(), $"cs-pc-{Guid.NewGuid():N}.png");
            try
            {
                pc.WriteImage(path, "Test \u2014 2026-08-26 \u2014 10:00\n1 participants");

                Assert.True(File.Exists(path));
                using var img = Image.FromFile(path);
                Assert.True(img.Width > 0 && img.Height > 0);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
