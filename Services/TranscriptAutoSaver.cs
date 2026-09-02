using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CaptionScribe.Core.Logging;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Owns the rolling autosave file for a transcript: resolves the target folder, appends
    /// newly-finalized lines on a timer, prunes old files, and rolls to a fresh file on demand.
    /// Lines near the growing end are held back until they are unlikely to be revised.
    /// </summary>
    public sealed class TranscriptAutoSaver : IDisposable
    {
        // Lines within this many of the end may still be revised by later frames, so hold them back.
        private const int StableMargin = 20;
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);

        private readonly Func<IReadOnlyList<string>> _lines;
        private readonly Func<string?> _configuredDir;
        private readonly ILog _log;
        private readonly object _gate = new();

        private string _path;
        private int _appendedCount;
        private Timer? _timer;
        private bool _disposed;

        public TranscriptAutoSaver(Func<IReadOnlyList<string>> lines, Func<string?> configuredDir, ILog log)
        {
            _lines = lines;
            _configuredDir = configuredDir;
            _log = log;
            _path = NewPath();
        }

        /// <summary>Path of the file currently being appended to.</summary>
        public string CurrentPath
        {
            get { lock (_gate) return _path; }
        }

        /// <summary>Starts (or restarts) the periodic append timer.</summary>
        public void Start(TimeSpan interval)
        {
            WaitAndDropTimer();
            lock (_gate)
            {
                if (_disposed)
                    return;
                _timer = new Timer(_ => Flush(final: false), null, interval, interval);
            }
        }

        /// <summary>Stops the timer and flushes what is safely finalized.</summary>
        public void Stop()
        {
            WaitAndDropTimer();
            Flush(final: false);
        }

        /// <summary>Rolls to a fresh file (the previous one stays on disk) and resets the append cursor.</summary>
        public void Roll()
        {
            lock (_gate)
            {
                _path = NewPath();
                _appendedCount = 0;
            }
        }

        /// <summary>
        /// Appends newly-finalized lines to the current file. Lines within <see cref="StableMargin"/>
        /// of the end are held back unless <paramref name="final"/> is set.
        /// </summary>
        public void Flush(bool final)
        {
            IReadOnlyList<string> lines;
            int start;
            int safe;
            string path;
            lock (_gate)
            {
                lines = _lines();
                start = _appendedCount;
                safe = Math.Max(0, lines.Count - (final ? 0 : StableMargin));
                if (safe <= start)
                    return;
                path = _path;
            }

            var newLines = new List<string>(safe - start);
            for (int i = start; i < safe; i++)
                newLines.Add(lines[i]);

            try
            {
                File.AppendAllLines(path, newLines);
            }
            catch (Exception ex)
            {
                _log.Warning("Autosave append failed: " + ex.Message);
                return;
            }

            lock (_gate)
            {
                if (_path == path && _appendedCount == start)
                    _appendedCount = safe;
            }
            if (_log.IsDebugEnabled)
                _log.Debug($"Autosaved {newLines.Count} line(s).");
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }
            WaitAndDropTimer();
            Flush(final: true);
        }

        private void WaitAndDropTimer()
        {
            Timer? timer;
            lock (_gate)
            {
                timer = _timer;
                _timer = null;
            }
            if (timer is null)
                return;
            using var done = new ManualResetEvent(false);
            timer.Dispose(done);
            if (!done.WaitOne(TimeSpan.FromSeconds(5)))
                _log.Warning("Timed out waiting for the autosave timer to finish.");
        }

        private string NewPath()
        {
            var dir = ResolveDir();

            var stamp = $"scribe-{DateTime.Now:yyyyMMdd-HHmmss}";
            var path = Path.Combine(dir, stamp + ".txt");
            // Guard against two rolls within the same second landing on one file.
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(dir, $"{stamp}-{n}.txt");
            return path;
        }

        private string ResolveDir()
        {
            var dir = _configuredDir();
            if (!string.IsNullOrWhiteSpace(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    return dir;
                }
                catch (Exception ex)
                {
                    _log.Warning($"Autosave folder '{dir}' is unusable ({ex.Message}); using the default.");
                }
            }

            var fallback = DirectoryFor(null);
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        /// <summary>The folder autosaves go to for a configured setting (pure path; no side effects).</summary>
        public static string DirectoryFor(string? configuredDir)
        {
            if (string.IsNullOrWhiteSpace(configuredDir))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CaptionScribe", "autosave");
            }
            return configuredDir.Trim();
        }

        /// <summary>Deletes autosaved transcripts in the folder; returns how many were removed.</summary>
        public static int ClearDirectory(string? configuredDir)
        {
            var dir = DirectoryFor(configuredDir);
            if (!Directory.Exists(dir))
                return 0;

            int deleted = 0;
            foreach (var file in Directory.GetFiles(dir, "scribe-*.txt"))
            {
                try { File.Delete(file); deleted++; } catch { /* skip a locked file */ }
            }
            return deleted;
        }

        /// <summary>Number of autosaved transcripts currently in the folder.</summary>
        public static int CountSaves(string? configuredDir)
        {
            var dir = DirectoryFor(configuredDir);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "scribe-*.txt").Length : 0;
        }

        /// <summary>Deletes autosaved transcripts older than the given age; returns how many were removed.</summary>
        public static int DeleteSavesOlderThan(string? configuredDir, TimeSpan age)
        {
            var dir = DirectoryFor(configuredDir);
            if (!Directory.Exists(dir))
                return 0;

            var cutoff = DateTime.UtcNow - age;
            int deleted = 0;
            foreach (var file in Directory.GetFiles(dir, "scribe-*.txt"))
            {
                try { if (File.GetLastWriteTimeUtc(file) < cutoff) { File.Delete(file); deleted++; } }
                catch { /* skip a locked file */ }
            }
            return deleted;
        }
    }
}
