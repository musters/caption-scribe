using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using CaptionScribe.Models;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class ParticipantImageWriterTests
    {
        private static byte[] SmallPng()
        {
            using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.CornflowerBlue);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        [Fact]
        public void Write_ProducesADecodablePng()
        {
            var writer = new ParticipantImageWriter();
            var people = new List<Participant>
            {
                new() { Name = "Testy McTestface", AvatarPng = SmallPng() },
                new() { Name = "Zippy Zapp", AvatarPng = System.Array.Empty<byte>() }, // placeholder path
            };
            var path = Path.Combine(Path.GetTempPath(), $"cs-participants-{System.Guid.NewGuid():N}.png");

            try
            {
                writer.Write(path, "2 participants\nWeekly Sync \u2014 2026-08-26 \u2014 09:30", people);

                Assert.True(File.Exists(path));
                using var img = Image.FromFile(path);
                Assert.True(img.Width > 0 && img.Height > 0);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Write_ImageGrowsTallerWithMoreParticipants()
        {
            var writer = new ParticipantImageWriter();
            Assert.True(HeightOf(writer, 5) > HeightOf(writer, 1),
                "a 5-row participants image should be taller than a 1-row one");
        }

        [Fact]
        public void Write_WithNoParticipants_StillProducesAValidPng()
        {
            var writer = new ParticipantImageWriter();
            var path = Path.Combine(Path.GetTempPath(), $"cs-piw-{System.Guid.NewGuid():N}.png");
            try
            {
                writer.Write(path, "Empty meeting", new List<Participant>());
                Assert.True(File.Exists(path));
                using var img = Image.FromFile(path);
                Assert.True(img.Height > 0);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static int HeightOf(ParticipantImageWriter writer, int count)
        {
            var people = new List<Participant>();
            for (int i = 0; i < count; i++)
                people.Add(new Participant { Name = $"Person {i}", AvatarPng = System.Array.Empty<byte>() });

            var path = Path.Combine(Path.GetTempPath(), $"cs-piw-{System.Guid.NewGuid():N}.png");
            try
            {
                writer.Write(path, "Meeting", people);
                using var img = Image.FromFile(path);
                return img.Height;
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
