using System.IO;

namespace CaptionScribe.Services
{
    public sealed class TextFileWriter : ITextFileWriter
    {
        public void Write(string path, string content) => File.WriteAllText(path, content);
    }
}
