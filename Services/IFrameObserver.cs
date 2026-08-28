using System.Collections.Generic;
using System.Drawing;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Receives native-color frames plus their OCR layout from the capture loop, so a single
    /// capture+OCR pipeline can feed secondary consumers (e.g. participant collection) without
    /// grabbing or recognizing the screen a second time.
    /// </summary>
    public interface IFrameObserver
    {
        /// <summary>When false, the loop skips the extra layout OCR and does not call <see cref="OnFrame"/>.</summary>
        bool WantsFrames { get; }

        void OnFrame(Bitmap frame, IReadOnlyList<RecognizedLine> lines);
    }
}
