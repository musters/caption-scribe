namespace CaptionScribe.Models
{
    /// <summary>Screen rectangle to capture, expressed in physical pixels.</summary>
    public readonly record struct CaptureRegion(int X, int Y, int Width, int Height);
}
