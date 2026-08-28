using System.Collections.Generic;
using System.Drawing;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Collects meeting participants (name + avatar) from frames supplied by the capture loop and
    /// renders them to an image on demand. It performs no capture or OCR of its own — the single
    /// caption pipeline feeds it via <see cref="IFrameObserver"/> while capture is active.
    /// </summary>
    public sealed class ParticipantCollector : IParticipantCollector, IFrameObserver
    {
        private readonly ParticipantRoster _roster = new();
        private readonly ParticipantImageWriter _writer = new();
        private volatile bool _enabled;

        public int Count => _roster.Count;

        public bool WantsFrames => _enabled;

        public void Start() => _enabled = true;

        public void Stop() => _enabled = false;

        public void Reset() => _roster.Clear();

        public void OnFrame(Bitmap frame, IReadOnlyList<RecognizedLine> lines) => _roster.Observe(frame, lines);

        public void WriteImage(string path, string title) => _writer.Write(path, title, _roster.Snapshot());
    }
}
