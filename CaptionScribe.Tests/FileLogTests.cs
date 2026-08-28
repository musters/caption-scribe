using System;
using System.IO;
using CaptionScribe.Core.Logging;
using Xunit;

namespace CaptionScribe.Tests
{
    public class FileLogTests : IDisposable
    {
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "CaptionScribeTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void Info_IsAlwaysWritten()
        {
            var log = new FileLog(() => false, _dir);
            log.Info("hello");
            Assert.Contains("[INF] hello", File.ReadAllText(log.FilePath));
        }

        [Fact]
        public void Debug_IsSuppressed_WhenDisabled()
        {
            var log = new FileLog(() => false, _dir);
            log.Info("kept");
            log.Debug("secret");

            Assert.False(log.IsDebugEnabled);
            var text = File.ReadAllText(log.FilePath);   // the Info write guarantees the file exists
            Assert.Contains("[INF] kept", text);
            Assert.DoesNotContain("secret", text);
        }

        [Fact]
        public void Debug_IsWritten_WhenEnabled()
        {
            var log = new FileLog(() => true, _dir);
            log.Debug("verbose");

            Assert.True(log.IsDebugEnabled);
            Assert.Contains("[DBG] verbose", File.ReadAllText(log.FilePath));
        }

        [Fact]
        public void NullLog_WritesNothing_AndNeverThrows()
        {
            ILog log = NullLog.Instance;
            Assert.False(log.IsDebugEnabled);
            log.Info("a");
            log.Debug("b");
            log.Warning("c");
            log.Error("d", new InvalidOperationException());
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
