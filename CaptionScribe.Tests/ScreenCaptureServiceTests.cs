using System.Drawing;
using System.Drawing.Imaging;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class ScreenCaptureServiceTests
    {
        [Fact]
        public void Fingerprint_IsEqualForIdenticalPixels_AndDiffersWhenTheyChange()
        {
            var svc = new ScreenCaptureService();
            using var a = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using var b = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(a)) g.Clear(Color.White);
            using (var g = Graphics.FromImage(b)) g.Clear(Color.White);

            Assert.Equal(svc.Fingerprint(a), svc.Fingerprint(b));

            using (var g = Graphics.FromImage(b)) g.Clear(Color.Black);
            Assert.NotEqual(svc.Fingerprint(a), svc.Fingerprint(b));
        }

        [Fact]
        public void Process_UpscalesByTheFactor()
        {
            using var svc = new ScreenCaptureService();
            using var raw = new Bitmap(10, 6, PixelFormat.Format32bppArgb);

            var processed = svc.Process(raw, upscaleFactor: 3, enhance: false);

            Assert.Equal(30, processed.Width);
            Assert.Equal(18, processed.Height);
        }

        [Fact]
        public void Process_Enhance_ProducesGrayscalePixels()
        {
            using var svc = new ScreenCaptureService();
            using var raw = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(raw)) g.Clear(Color.FromArgb(255, 200, 50, 10));

            var processed = svc.Process(raw, upscaleFactor: 1, enhance: true);

            var p = processed.GetPixel(2, 2);
            Assert.Equal(p.R, p.G);   // grayscale => R == G == B
            Assert.Equal(p.G, p.B);
        }

        [Fact]
        public void Process_ReusesThePooledBitmap_ForTheSameSize()
        {
            using var svc = new ScreenCaptureService();
            using var raw = new Bitmap(8, 8, PixelFormat.Format32bppArgb);

            var first = svc.Process(raw, 2, false);
            var second = svc.Process(raw, 2, false);

            Assert.Same(first, second);
        }

        [Fact]
        public void Process_ReallocatesOnSizeChange_AndReleaseBuffersResetsThePool()
        {
            using var svc = new ScreenCaptureService();
            using var small = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
            using var big = new Bitmap(16, 8, PixelFormat.Format32bppArgb);

            var a = svc.Process(small, 1, false);
            var b = svc.Process(big, 1, false);     // different size -> new bitmap
            Assert.NotSame(a, b);

            var c = svc.Process(big, 1, false);     // same size -> reused
            Assert.Same(b, c);

            svc.ReleaseBuffers();
            var d = svc.Process(big, 1, false);     // pool freed -> new bitmap
            Assert.NotSame(c, d);
        }
    }
}
