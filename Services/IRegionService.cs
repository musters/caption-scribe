using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>Region picking and the on-screen highlight overlay (view concerns).</summary>
    public interface IRegionService
    {
        /// <summary>Shows the region selector; returns the chosen region, or null if cancelled.</summary>
        CaptureRegion? SelectRegion();

        /// <summary>Briefly frames the region on screen.</summary>
        void HighlightRegion(CaptureRegion region);
    }
}
