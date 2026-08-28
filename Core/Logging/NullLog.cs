using System;

namespace CaptionScribe.Core.Logging
{
    /// <summary>A logger that discards everything (tests, or when logging is unavailable).</summary>
    public sealed class NullLog : ILog
    {
        public static readonly NullLog Instance = new();

        public bool IsDebugEnabled => false;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
