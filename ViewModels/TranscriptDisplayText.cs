using System;
using System.Collections.Generic;
using CaptionScribe.Models;
using CaptionScribe.Services;

namespace CaptionScribe.ViewModels
{
    /// <summary>Live tail vs Show-All cap and the overflow banner for the transcript text box.</summary>
    internal static class TranscriptDisplayText
    {
        internal const int TailLineCount = 200;
        internal const int AllOutputLineCount = 10_000;

        internal static int LineCap(bool capturing, bool showAllOutput)
            => (!capturing && showAllOutput) ? AllOutputLineCount : TailLineCount;

        internal static string Build(bool capturing, bool showAllOutput, bool timestamps, bool perTurn,
            int lineCount, string plainTail, IReadOnlyList<TimedLine> timedTail)
        {
            int cap = LineCap(capturing, showAllOutput);
            string tail = timestamps
                ? TranscriptFormatter.Format(timedTail, true, perTurn)
                : plainTail;
            if (lineCount <= cap)
                return tail;
            return $"… showing the last {cap} lines (the full transcript is captured and will be saved) …"
                   + Environment.NewLine + tail;
        }
    }
}
