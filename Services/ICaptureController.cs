using System;
using System.Collections.Generic;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>
    /// The capture controller as its view-model consumes it. Abstracting it lets the view-model be
    /// unit-tested without the real screen/OCR capture pipeline.
    /// </summary>
    public interface ICaptureController
    {
        event EventHandler? TranscriptUpdated;
        event EventHandler? CaptureStateChanged;

        bool IsRunning { get; }
        int TranscriptLineCount { get; }
        string AutoSavePath { get; }
        DateTime? SessionStartedAt { get; }

        void Start();
        void Stop();
        void ClearTranscript();
        void UpdateRegion(CaptureRegion region);
        void ApplySettings();

        string GetTranscriptText();
        string GetTranscriptTailText(int maxLines);
        IReadOnlyList<TimedLine> GetTimedTranscript();
        IReadOnlyList<TimedLine> GetTimedTail(int maxLines);
        string GetCaptureDiagnostics();
    }
}
