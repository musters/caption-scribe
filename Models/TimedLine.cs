using System;

namespace CaptionScribe.Models
{
    /// <summary>A transcript line paired with the time it was first captured.</summary>
    public readonly record struct TimedLine(string Text, DateTime Time);
}
