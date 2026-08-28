using System;
using System.IO;
using System.Linq;

namespace CaptionScribe.Core.Logging
{
    /// <summary>
    /// Thread-safe rolling file log under %APPDATA%\CaptionScribe\logs. Info/Warning/Error are always
    /// written; Debug is written only while <paramref name="debugEnabled"/> returns true, so it can be
    /// toggled at runtime. A new file is created per launch and old ones are pruned.
    /// </summary>
    public sealed class FileLog : ILog
    {
        private const int MaxLogFiles = 10;

        private readonly object _gate = new();
        private readonly Func<bool> _debugEnabled;
        private readonly string _path;

        public FileLog(Func<bool> debugEnabled, string? directory = null)
        {
            _debugEnabled = debugEnabled;
            var dir = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CaptionScribe", "logs");
            Directory.CreateDirectory(dir);
            Prune(dir);
            _path = Path.Combine(dir, $"log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        }

        /// <summary>Full path of the file this instance writes to.</summary>
        public string FilePath => _path;

        public bool IsDebugEnabled => _debugEnabled();

        public void Debug(string message)
        {
            if (_debugEnabled())
                Write(LogLevel.Debug, message, null);
        }

        public void Info(string message) => Write(LogLevel.Info, message, null);
        public void Warning(string message) => Write(LogLevel.Warning, message, null);
        public void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);

        private void Write(LogLevel level, string message, Exception? exception)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Tag(level)}] {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;

            lock (_gate)
            {
                try { File.AppendAllText(_path, line + Environment.NewLine); }
                catch { /* logging must never throw */ }
            }
        }

        private static string Tag(LogLevel level) => level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            _ => "ERR",
        };

        private static void Prune(string dir)
        {
            try
            {
                var stale = new DirectoryInfo(dir)
                    .GetFiles("log-*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(MaxLogFiles - 1);
                foreach (var file in stale)
                {
                    try { file.Delete(); } catch { /* ignore a locked/removed file */ }
                }
            }
            catch { /* best-effort housekeeping */ }
        }
    }
}
