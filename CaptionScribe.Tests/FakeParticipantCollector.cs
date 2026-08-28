using CaptionScribe.Services;

namespace CaptionScribe.Tests
{
    /// <summary>In-memory <see cref="IParticipantCollector"/> that records calls for view-model tests.</summary>
    internal sealed class FakeParticipantCollector : IParticipantCollector
    {
        public int Count { get; set; }

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastTitle { get; private set; }

        public void Start() => StartCalls++;
        public void Stop() => StopCalls++;
        public void Reset() => ResetCalls++;

        public void WriteImage(string path, string title)
        {
            WriteCalls++;
            LastPath = path;
            LastTitle = title;
        }
    }
}
