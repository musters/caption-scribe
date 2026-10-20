using System;
using System.Collections.Generic;
using System.IO;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>Builds save file names and transcript text for export (cleanup / timestamps / plain).</summary>
    public readonly struct TranscriptSnapshot
    {
        public TranscriptSnapshot(IReadOnlyList<TimedLine> timed, string plain, DateTime startedAt)
        {
            Timed = timed;
            Plain = plain;
            StartedAt = startedAt;
        }

        public IReadOnlyList<TimedLine> Timed { get; }
        public string Plain { get; }
        public DateTime StartedAt { get; }
    }

    public static class TranscriptExport
    {
        public static string SuggestedFileName(DateTime started, string title)
        {
            var stamp = started.ToString("yyyy-MM-dd-'Meeting'-HH-mm");
            var safe = SanitizeFileName(title.Trim());
            return string.IsNullOrWhiteSpace(safe) ? $"{stamp}.txt" : $"{stamp}-{safe}.txt";
        }

        public static string Build(SaveCleanup cleanup, bool timestamps, bool perTurn,
            IReadOnlyList<TimedLine> timed, string plain)
        {
            if (cleanup == SaveCleanup.Clean)
                return TranscriptCleaner.Clean(timed, timestamps, perTurn);
            if (timestamps)
                return TranscriptFormatter.Format(timed, true, perTurn);
            return plain;
        }

        public static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return name.Trim();
        }
    }
}
