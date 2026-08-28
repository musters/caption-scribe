using System;

namespace CaptionScribe.Core.Logging
{
    public enum LogLevel { Debug, Info, Warning, Error }

    /// <summary>Minimal logging abstraction (dependency-free). Debug is off unless enabled.</summary>
    public interface ILog
    {
        bool IsDebugEnabled { get; }
        void Debug(string message);
        void Info(string message);
        void Warning(string message);
        void Error(string message, Exception? exception = null);
    }
}
