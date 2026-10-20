using System.Drawing;
using System.Drawing.Imaging;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class ScreenCaptureServiceTests
    {
        [Fact]
        public void HasMeaningfulProcessedChange_IsIndependentOfRawSampler()
        {
            using var svc = new ScreenCaptureService();
            using var raw = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(raw)) g.Clear(Color.White);

            Assert.True(svc.HasMeaningfulChange(raw));
            var processed = svc.Process(raw, 1, enhance: true);
            Assert.True(svc.HasMeaningfulProcessedChange(processed));
            Assert.False(svc.HasMeaningfulProcessedChange(processed));
            Assert.False(svc.HasMeaningfulChange(raw));
        }

        [Fact]
        public void HasMeaningfulChange_FirstFrameAndRealChange_NotTinyNoise()
        {
            using var svc = new ScreenCaptureService();
            using var frame = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(frame)) g.Clear(Color.White);

            Assert.True(svc.HasMeaningfulChange(frame));
            Assert.False(svc.HasMeaningfulChange(frame));

            frame.SetPixel(1, 1, Color.Red);
            Assert.False(svc.HasMeaningfulChange(frame));

            using (var g = Graphics.FromImage(frame)) g.Clear(Color.Black);
            Assert.True(svc.HasMeaningfulChange(frame));

            svc.ResetChangeDetection();
            Assert.True(svc.HasMeaningfulChange(frame));
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
        public void ClampProcessedSize_LeavesSmallFramesUnchanged()
        {
            ScreenCaptureService.ClampProcessedSize(10, 6, 3, out int w, out int h);
            Assert.Equal(30, w);
            Assert.Equal(18, h);
        }

        [Fact]
        public void ClampProcessedSize_CapsHugeFrames()
        {
            ScreenCaptureService.ClampProcessedSize(3840, 2160, 4, out int w, out int h);
            Assert.True((long)w * h <= ScreenCaptureService.MaxProcessedPixels);
            Assert.True(w < 3840 * 4);
            Assert.True(h < 2160 * 4);
        }

        [Fact]
        public void Process_HugeUpscale_StaysWithinPixelCap()
        {
            using var svc = new ScreenCaptureService();
            using var raw = new Bitmap(800, 600, PixelFormat.Format32bppArgb);

            var processed = svc.Process(raw, upscaleFactor: 4, enhance: false);

            Assert.True((long)processed.Width * processed.Height <= ScreenCaptureService.MaxProcessedPixels);
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
