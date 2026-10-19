using System;
using CaptionScribe.Core.Logging;
using Xunit;

namespace CaptionScribe.Tests
{
    public class NullLogTests
    {
        [Fact]
        public void WritesDoNotThrow()
        {
            var log = NullLog.Instance;
            log.Debug("d");
            log.Info("i");
            log.Warning("w");
            log.Error("e", new InvalidOperationException("x"));
            Assert.False(log.IsDebugEnabled);
        }
    }
}
