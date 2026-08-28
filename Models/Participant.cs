using System;

namespace CaptionScribe.Models
{
    /// <summary>A meeting participant: display name plus a PNG-encoded avatar crop.</summary>
    public sealed class Participant
    {
        public string Name { get; init; } = "";
        public byte[] AvatarPng { get; init; } = Array.Empty<byte>();
    }
}
