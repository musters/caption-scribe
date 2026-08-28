using System;

namespace CaptionScribe.Services
{
    /// <summary>Shared heuristic for recognizing a Teams caption speaker-name (turn header) line.</summary>
    internal static class SpeakerHeuristics
    {
        /// <summary>True when a line is shaped like a speaker header: 2–4 Title-Case words, no ending punctuation.</summary>
        public static bool LooksLikeName(string line)
        {
            if (line.Length is 0 or > 40)
                return false;

            char last = line[^1];
            if (last is '.' or ',' or '?' or '!' or ':' or ';')
                return false;

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is < 2 or > 4)
                return false;

            foreach (var word in words)
            {
                if (!char.IsUpper(word[0]))
                    return false;
            }
            return true;
        }
    }
}
