using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    public sealed class ScreenCaptureService : IDisposable
    {
        // Reused frame buffers (the capture loop is single-threaded); avoids per-cycle LOH churn.
        private Bitmap? _rawPool;
        private Bitmap? _processedPool;
        private Graphics? _rawGraphics;
        private Graphics? _processedGraphics;

        /// <summary>
        /// Grabs the screen region at native resolution and color into a reused buffer. The returned bitmap is
        /// owned by this service — do not dispose it; it is overwritten on the next call.
        /// </summary>
        public Bitmap CaptureRaw(CaptureRegion region)
        {
            var raw = EnsurePool(ref _rawPool, ref _rawGraphics, region.Width, region.Height);
            _rawGraphics!.CopyFromScreen(region.X, region.Y, 0, 0,
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
            var processed = EnsurePool(ref _processedPool, ref _processedGraphics, raw.Width * scale, raw.Height * scale);
            var g = _processedGraphics!;
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
            return processed;
        }

        // Reuses the pooled bitmap when the size matches, otherwise (re)allocates it.
        private static Bitmap EnsurePool(ref Bitmap? pool, ref Graphics? graphics, int width, int height)
        {
            if (pool is null || pool.Width != width || pool.Height != height)
            {
                graphics?.Dispose();
                graphics = null;
                pool?.Dispose();
                pool = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                graphics = Graphics.FromImage(pool);
            }
            return pool;
        }

        /// <summary>Frees the pooled frame buffers (called when capture stops so idle memory stays low).</summary>
        public void ReleaseBuffers()
        {
            _rawGraphics?.Dispose();
            _processedGraphics?.Dispose();
            _rawGraphics = null;
            _processedGraphics = null;
            _rawPool?.Dispose();
            _processedPool?.Dispose();
            _rawPool = null;
            _processedPool = null;
            ResetChangeDetection();
        }

        public void Dispose() => ReleaseBuffers();

        private readonly object _sampleGate = new();
        private byte[]? _prevSample;
        private byte[]? _currSample;

        /// <summary>True when the frame differs enough from the last accepted one to warrant OCR.</summary>
        public unsafe bool HasMeaningfulChange(Bitmap bitmap)
        {
            const int step = 4;
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                lock (_sampleGate)
                {
                    int w = bitmap.Width, h = bitmap.Height;
                    int nx = (w + step - 1) / step;
                    int ny = (h + step - 1) / step;
                    int n = nx * ny;
                    if (_currSample is null || _currSample.Length != n)
                        _currSample = new byte[n];

                    byte* src = (byte*)data.Scan0;
                    int i = 0;
                    for (int y = 0; y < h; y += step)
                    {
                        byte* row = src + (long)y * data.Stride;
                        for (int x = 0; x < w; x += step)
                            _currSample[i++] = row[x * 4 + 1];
                    }

                    if (_prevSample is null || _prevSample.Length != n)
                    {
                        _prevSample = (byte[])_currSample.Clone();
                        return true;
                    }

                    int threshold = Math.Max(1, n / 200);
                    int changed = 0;
                    for (i = 0; i < n; i++)
                    {
                        if (_prevSample[i] != _currSample[i] && ++changed >= threshold)
                            break;
                    }
                    if (changed < threshold)
                        return false;

                    Buffer.BlockCopy(_currSample, 0, _prevSample, 0, n);
                    return true;
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public void ResetChangeDetection()
        {
            lock (_sampleGate)
            {
                _prevSample = null;
                _currSample = null;
            }
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
