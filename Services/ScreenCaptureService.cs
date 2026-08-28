using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    public sealed class ScreenCaptureService : IDisposable
    {
        // Reused frame buffers (the capture loop is single-threaded); avoids per-cycle LOH churn.
        private Bitmap? _rawPool;
        private Bitmap? _processedPool;

        /// <summary>
        /// Grabs the screen region at native resolution and color into a reused buffer. The returned bitmap is
        /// owned by this service — do not dispose it; it is overwritten on the next call.
        /// </summary>
        public Bitmap CaptureRaw(CaptureRegion region)
        {
            var raw = EnsurePool(ref _rawPool, region.Width, region.Height);
            using var g = Graphics.FromImage(raw);
            g.CopyFromScreen(region.X, region.Y, 0, 0,
                new Size(region.Width, region.Height), CopyPixelOperation.SourceCopy);
            return raw;
        }

        /// <summary>
        /// Upscales and optionally contrast-enhances a raw frame for OCR into a reused buffer. The returned
        /// bitmap is owned by this service — do not dispose it; it is overwritten on the next call.
        /// </summary>
        public Bitmap Process(Bitmap raw, int upscaleFactor, bool enhance)
        {
            int scale = Math.Max(1, upscaleFactor);
            var processed = EnsurePool(ref _processedPool, raw.Width * scale, raw.Height * scale);
            using (var g = Graphics.FromImage(processed))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                if (enhance)
                {
                    using var attributes = new ImageAttributes();
                    attributes.SetColorMatrix(GrayscaleContrast);
                    var dest = new Rectangle(0, 0, processed.Width, processed.Height);
                    g.DrawImage(raw, dest, 0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, attributes);
                }
                else
                {
                    g.DrawImage(raw, 0, 0, processed.Width, processed.Height);
                }
            }
            return processed;
        }

        // Reuses the pooled bitmap when the size matches, otherwise (re)allocates it.
        private static Bitmap EnsurePool(ref Bitmap? pool, int width, int height)
        {
            if (pool is null || pool.Width != width || pool.Height != height)
            {
                pool?.Dispose();
                pool = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            }
            return pool;
        }

        /// <summary>Frees the pooled frame buffers (called when capture stops so idle memory stays low).</summary>
        public void ReleaseBuffers()
        {
            _rawPool?.Dispose();
            _processedPool?.Dispose();
            _rawPool = null;
            _processedPool = null;
        }

        public void Dispose() => ReleaseBuffers();

        private byte[]? _hashBuffer;

        /// <summary>Fast content fingerprint of a bitmap; identical fingerprints mean identical pixels.</summary>
        public long Fingerprint(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int length = data.Stride * data.Height;
                if (_hashBuffer is null || _hashBuffer.Length < length)
                    _hashBuffer = new byte[length];
                Marshal.Copy(data.Scan0, _hashBuffer, 0, length);
                return Hash(_hashBuffer, length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        // FNV-1a over 8-byte words (with a byte tail); fast, and any pixel change flips it.
        private static long Hash(byte[] data, int length)
        {
            const ulong prime = 1099511628211UL;
            ulong hash = 14695981039346656037UL;
            int i = 0;
            int words = length - (length % 8);
            for (; i < words; i += 8)
                hash = (hash ^ BitConverter.ToUInt64(data, i)) * prime;
            for (; i < length; i++)
                hash = (hash ^ data[i]) * prime;
            return unchecked((long)hash);
        }

        // Grayscale (luminance) with a mild contrast boost centred on mid-grey, to sharpen thin text.
        private static readonly ColorMatrix GrayscaleContrast = BuildGrayscaleContrast(1.4f);

        private static ColorMatrix BuildGrayscaleContrast(float contrast)
        {
            float t = 0.5f * (1f - contrast);
            float r = 0.299f * contrast;
            float g = 0.587f * contrast;
            float b = 0.114f * contrast;
            return new ColorMatrix(new[]
            {
                new[] { r, r, r, 0f, 0f },
                new[] { g, g, g, 0f, 0f },
                new[] { b, b, b, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { t, t, t, 0f, 1f },
            });
        }
    }
}
