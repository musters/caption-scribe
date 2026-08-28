namespace CaptionScribe.Services
{
    /// <summary>Collects meeting participants (name + avatar) and writes a participants image on demand.</summary>
    public interface IParticipantCollector
    {
        int Count { get; }
        void Start();
        void Stop();
        void Reset();
        void WriteImage(string path, string title);
    }
}
