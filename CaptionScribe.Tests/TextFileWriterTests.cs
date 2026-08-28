using System;
using System.IO;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class TextFileWriterTests
    {
        [Fact]
        public void Write_PersistsContentExactly()
        {
            var writer = new TextFileWriter();
            var path = Path.Combine(Path.GetTempPath(), $"cs-tfw-{Guid.NewGuid():N}.txt");
            try
            {
                writer.Write(path, "line one\nline two");
                Assert.Equal("line one\nline two", File.ReadAllText(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
