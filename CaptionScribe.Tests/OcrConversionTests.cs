using System.Drawing;
using System.Drawing.Imaging;
using CaptionScribe.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Xunit;

namespace CaptionScribe.Tests
{
    public class OcrConversionTests
    {
        [Fact]
        public void ToSoftwareBitmap_CopiesDimensionsFormatAndBgraPixels()
        {
            using var bmp = new Bitmap(4, 3, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.FromArgb(255, 10, 20, 30));   // A=255, R=10, G=20, B=30

            using var sb = OcrService.ToSoftwareBitmap(bmp);

            Assert.Equal(4, sb.PixelWidth);
            Assert.Equal(3, sb.PixelHeight);
            Assert.Equal(BitmapPixelFormat.Bgra8, sb.BitmapPixelFormat);

            var px = ReadPixels(sb);
            // First pixel, Bgra8 byte order: B, G, R, A.
            Assert.Equal(30, px[0]);
            Assert.Equal(20, px[1]);
            Assert.Equal(10, px[2]);
            Assert.Equal(255, px[3]);
        }

        private static byte[] ReadPixels(SoftwareBitmap sb)
        {
            uint length = (uint)(sb.PixelWidth * sb.PixelHeight * 4);
            var buffer = new Windows.Storage.Streams.Buffer(length);
            sb.CopyToBuffer(buffer);
            var bytes = new byte[length];
            DataReader.FromBuffer(buffer).ReadBytes(bytes);
            return bytes;
        }
    }
}
