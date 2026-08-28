using System;
using System.Collections.Generic;
using CaptionScribe.Models;
using CaptionScribe.Services;

namespace CaptionScribe.Tests
{
    /// <summary>In-memory <see cref="ICaptureController"/> that records calls for view-model tests.</summary>
    internal sealed class FakeCaptureController : ICaptureController
    {
        public event EventHandler? TranscriptUpdated;
        public event EventHandler? CaptureStateChanged;

        public bool IsRunning { get; set; }
        public int TranscriptLineCount { get; set; }
        public string AutoSavePath { get; set; } = @"C:\autosave\scribe.txt";
        public DateTime? SessionStartedAt { get; set; }

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int ClearCalls { get; private set; }
        public int ApplySettingsCalls { get; private set; }
        public CaptureRegion? UpdatedRegion { get; private set; }

        public string TranscriptText { get; set; } = "";
        public IReadOnlyList<TimedLine> TimedLines { get; set; } = Array.Empty<TimedLine>();
        public string CaptureDiagnostics { get; set; } = "diagnostics report";

        public void Start() { StartCalls++; IsRunning = true; }
        public void Stop() { StopCalls++; IsRunning = false; }
        public void ClearTranscript() { ClearCalls++; TranscriptLineCount = 0; }
        public void UpdateRegion(CaptureRegion region) => UpdatedRegion = region;
        public void ApplySettings() => ApplySettingsCalls++;

        public string GetTranscriptText() => TranscriptText;
        public string GetTranscriptTailText(int maxLines) => TranscriptText;
        public IReadOnlyList<TimedLine> GetTimedTranscript() => TimedLines;
        public IReadOnlyList<TimedLine> GetTimedTail(int maxLines) => TimedLines;
        public string GetCaptureDiagnostics() => CaptureDiagnostics;

        public void RaiseTranscriptUpdated() => TranscriptUpdated?.Invoke(this, EventArgs.Empty);
        public void RaiseCaptureStateChanged() => CaptureStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records saves without touching disk.</summary>
    internal sealed class FakeSettingsService : ISettingsService
    {
        public int SaveCalls { get; private set; }
        public AppSettings? LastSaved { get; private set; }

        public AppSettings Load() => new();
        public void Save(AppSettings settings) { SaveCalls++; LastSaved = settings; }
    }

    internal sealed class FakeRegionService : IRegionService
    {
        public CaptureRegion? RegionToReturn { get; set; }
        public int SelectCalls { get; private set; }
        public CaptureRegion? Highlighted { get; private set; }

        public CaptureRegion? SelectRegion() { SelectCalls++; return RegionToReturn; }
        public void HighlightRegion(CaptureRegion region) => Highlighted = region;
    }

    internal sealed class FakeNotificationService : INotificationService
    {
        public List<string> Infos { get; } = new();

        public void Info(string message) => Infos.Add(message);
    }

    internal sealed class FakeTextFileWriter : ITextFileWriter
    {
        public string? LastPath { get; private set; }
        public string? LastContent { get; private set; }
        public int Writes { get; private set; }

        public void Write(string path, string content)
        {
            LastPath = path;
            LastContent = content;
            Writes++;
        }
    }
}
