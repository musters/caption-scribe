namespace CaptionScribe.Services
{
    /// <summary>An OCR line with its bounding box, in the source bitmap's pixel coordinates.</summary>
    public readonly record struct RecognizedLine(string Text, double X, double Y, double Width, double Height);
}
