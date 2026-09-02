using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinRT;

namespace CaptionScribe.Services
{
    /// <summary>Wraps the built-in offline Windows OCR engine (Windows.Media.Ocr).</summary>
    public sealed class OcrService
    {
        private readonly OcrEngine? _engine;

        public OcrService()
        {
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();
        }

        public bool IsAvailable => _engine is not null;

        public async Task<List<string>> RecognizeLinesAsync(Bitmap bitmap)
        {
            var layout = await RecognizeLayoutAsync(bitmap);
            var texts = new List<string>(layout.Count);
            foreach (var line in layout)
                texts.Add(line.Text);
            return texts;
        }

        /// <summary>Recognizes lines together with their bounding boxes (in the bitmap's pixels).</summary>
        public async Task<IReadOnlyList<RecognizedLine>> RecognizeLayoutAsync(Bitmap bitmap,
            bool includeBoxes = true, CancellationToken token = default)
        {
            if (_engine is null)
                return Array.Empty<RecognizedLine>();

            token.ThrowIfCancellationRequested();
            using var softwareBitmap = ToSoftwareBitmap(bitmap);
            var operation = _engine.RecognizeAsync(softwareBitmap);
            OcrResult result;
            using (token.Register(() => { try { operation.Cancel(); } catch { /* already completed */ } }))
                result = await operation.AsTask(token);

            var lines = new List<RecognizedLine>(result.Lines.Count);
            foreach (var line in result.Lines)
            {
                var text = line.Text.Trim();
                if (text.Length == 0)
                    continue;
                if (!includeBoxes)
                {
                    lines.Add(new RecognizedLine(text, 0, 0, 0, 0));
                    continue;
                }

                double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
                foreach (var word in line.Words)
                {
                    var r = word.BoundingRect;
                    left = Math.Min(left, r.X);
                    top = Math.Min(top, r.Y);
                    right = Math.Max(right, r.X + r.Width);
                    bottom = Math.Max(bottom, r.Y + r.Height);
                }
                if (left > right || top > bottom)
                    continue;

                lines.Add(new RecognizedLine(text, left, top, right - left, bottom - top));
            }
            return lines;
        }

        // Copies the GDI bitmap's BGRA pixels straight into a WinRT SoftwareBitmap, skipping the
        // encode/decode round-trip (GDI Format32bppArgb is byte-identical to WinRT Bgra8).
        internal static unsafe SoftwareBitmap ToSoftwareBitmap(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            SoftwareBitmap? software = null;
            try
            {
                software = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
                var data = bitmap.LockBits(
                    new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    using var buffer = software.LockBuffer(BitmapBufferAccessMode.Write);
                    using var reference = buffer.CreateReference();
                    reference.As<IMemoryBufferByteAccess>().GetBuffer(out byte* dest, out uint capacity);

                    var plane = buffer.GetPlaneDescription(0);
                    int rowBytes = width * 4;
                    var src = (byte*)data.Scan0;
                    for (int y = 0; y < height; y++)
                    {
                        long destOffset = plane.StartIndex + (long)y * plane.Stride;
                        Buffer.MemoryCopy(
                            src + (long)y * data.Stride,
                            dest + destOffset,
                            capacity - destOffset,
                            rowBytes);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
                return software;
            }
            catch
            {
                software?.Dispose();
                throw;
            }
        }

        [ComImport]
        [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private unsafe interface IMemoryBufferByteAccess
        {
            void GetBuffer(out byte* buffer, out uint capacity);
        }
    }
}
