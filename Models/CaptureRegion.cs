namespace CaptionScribe.Models
{
    /// <summary>Screen rectangle to capture, expressed in physical pixels.</summary>
    public sealed class CaptureRegion
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
