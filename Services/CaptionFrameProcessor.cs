using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using CaptionScribe.Core.Logging;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Upscale/OCR a captured frame, apply it to the transcript under the epoch lock, and
    /// optionally notify a frame observer. Pooled bitmaps stay owned by <see cref="ScreenCaptureService"/>.
    /// </summary>
    internal sealed class CaptionFrameProcessor : IDisposable
    {
        private static readonly TimeSpan ParticipantSampleInterval = TimeSpan.FromSeconds(4);
        private const double UnityScaleEpsilon = 1.0001;

        private readonly ScreenCaptureService _capture;
        private readonly OcrService _ocr = new();
        private readonly TranscriptAggregator _aggregator;
        private readonly IFrameObserver? _frameObserver;
        private readonly ILog _log;
        private readonly object _gate = new();
        private int _epoch;
        private string[]? _lastOcrTexts;
        private DateTime _lastParticipantSampleUtc;

        public event EventHandler? TranscriptUpdated;

        public CaptionFrameProcessor(ScreenCaptureService capture, TranscriptAggregator aggregator, ILog log,
            IFrameObserver? frameObserver)
        {
            _capture = capture;
            _aggregator = aggregator;
            _log = log;
            _frameObserver = frameObserver;
        }

        public bool OcrAvailable => _ocr.IsAvailable;
        public int LineCount => _aggregator.Count;

        public void ApplySimilarity(double threshold) => _aggregator.SimilarityThreshold = threshold;

        public void ResetParticipantClock() => _lastParticipantSampleUtc = DateTime.MinValue;

        public string GetText() => _aggregator.GetText();
        public string GetTailText(int maxLines) => _aggregator.GetTailText(maxLines);
        public IReadOnlyList<TimedLine> GetTimedLines() => _aggregator.GetTimedLines();
        public IReadOnlyList<TimedLine> GetTimedTail(int maxLines) => _aggregator.GetTimedTail(maxLines);
        public (IReadOnlyList<TimedLine> Timed, string Plain) GetExportSnapshot() => _aggregator.GetExportSnapshot();

        public void Clear()
        {
            lock (_gate)
            {
                _epoch++;
                _lastOcrTexts = null;
                _aggregator.Clear();
            }
            TranscriptUpdated?.Invoke(this, EventArgs.Empty);
        }

        public async Task RecognizeAsync(Bitmap raw, CaptureSettings s, CancellationToken token)
        {
            int scale = Math.Max(1, s.UpscaleFactor);
            long rawPixels = (long)raw.Width * raw.Height;
            bool overCap = rawPixels * scale * scale > ScreenCaptureService.MaxProcessedPixels;
            Bitmap ocrImage = (scale > 1 || s.EnhanceForOcr || overCap)
                ? _capture.Process(raw, scale, s.EnhanceForOcr)
                : raw;
            if (!ReferenceEquals(ocrImage, raw) && !_capture.HasMeaningfulProcessedChange(ocrImage))
                return;

            int epoch = Volatile.Read(ref _epoch);
            var layout = await _ocr.RecognizeLayoutAsync(ocrImage,
                includeBoxes: _frameObserver is { WantsFrames: true }, token);
            token.ThrowIfCancellationRequested();

            List<string>? texts = null;
            if (layout.Count > 0)
            {
                texts = new List<string>(layout.Count);
                foreach (var line in layout)
                    texts.Add(line.Text);
            }

            double layoutScale = (double)ocrImage.Width / Math.Max(1, raw.Width);
            lock (_gate)
            {
                if (epoch != _epoch)
                    return;
                if (_log.IsDebugEnabled)
                    _log.Debug($"OCR produced {layout.Count} line(s).");
                if (texts is not null && !SameTexts(texts, _lastOcrTexts))
                {
                    _lastOcrTexts = texts.ToArray();
                    _aggregator.AddSnapshot(texts);
                    TranscriptUpdated?.Invoke(this, EventArgs.Empty);
                }
                NotifyParticipants(raw, layout, layoutScale, token);
            }
        }

        public void Dispose() => _ocr.Dispose();

        internal static bool SameTexts(List<string> texts, string[]? previous)
        {
            if (previous is null || previous.Length != texts.Count)
                return false;
            for (int i = 0; i < texts.Count; i++)
            {
                if (!string.Equals(texts[i], previous[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        internal static IReadOnlyList<RecognizedLine> ScaleLayout(IReadOnlyList<RecognizedLine> layout, double scale)
        {
            var scaled = new List<RecognizedLine>(layout.Count);
            foreach (var line in layout)
                scaled.Add(new RecognizedLine(line.Text, line.X / scale, line.Y / scale, line.Width / scale, line.Height / scale));
            return scaled;
        }

        private void NotifyParticipants(Bitmap raw, IReadOnlyList<RecognizedLine> layout, double scale,
            CancellationToken token)
        {
            if (_frameObserver is not { WantsFrames: true })
                return;

            var now = DateTime.UtcNow;
            if (now - _lastParticipantSampleUtc < ParticipantSampleInterval)
                return;
            token.ThrowIfCancellationRequested();
            _lastParticipantSampleUtc = now;
            _frameObserver.OnFrame(raw, scale <= UnityScaleEpsilon ? layout : ScaleLayout(layout, scale));
        }
    }
}
