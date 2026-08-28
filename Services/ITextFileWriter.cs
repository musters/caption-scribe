namespace CaptionScribe.Services
{
    /// <summary>Writes text files. Abstracted so transcript-save logic is unit-testable.</summary>
    public interface ITextFileWriter
    {
        void Write(string path, string content);
    }
}
