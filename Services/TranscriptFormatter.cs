using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>Renders transcript lines, optionally prefixing each with its capture time.</summary>
    public static class TranscriptFormatter
    {
        // Width of "[HH:mm:ss] " so continuation lines stay aligned in the monospace view.
        private const string Indent = "           ";

        public static string Format(IReadOnlyList<TimedLine> lines, bool withTimestamps, bool perTurn = false)
        {
            if (!withTimestamps)
                return string.Join(Environment.NewLine, lines.Select(l => l.Text));

            var sb = new StringBuilder();
            string? previousStamp = null;
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    sb.Append(Environment.NewLine);

                string stamp = lines[i].Time.ToString("HH:mm:ss");

                // Per-turn: stamp only the first line and each speaker header. Otherwise: stamp on every
                // change of second, blank-aligning the lines that repeat within the same second.
                bool show = perTurn
                    ? (i == 0 || SpeakerHeuristics.LooksLikeName(lines[i].Text))
                    : stamp != previousStamp;

                if (show)
                {
                    sb.Append('[').Append(stamp).Append("] ");
                    previousStamp = stamp;
                }
                else
                {
                    sb.Append(Indent);
                }
                sb.Append(lines[i].Text);
            }
            return sb.ToString();
        }
    }
}
