using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>Optional, user-invoked cleanup applied when saving a transcript.</summary>
    public static class TranscriptCleaner
    {
        public static string Clean(IReadOnlyList<TimedLine> lines, bool withTimestamps, bool perTurn = false)
        {
            var fixedLines = lines.Select(l => new TimedLine(FixLine(l.Text), l.Time)).ToList();
            var speakers = FindSpeakerNames(fixedLines.Select(l => l.Text).ToList());

            var result = new List<TimedLine>(fixedLines.Count);
            string? lastSpeaker = null;
            foreach (var line in fixedLines)
            {
                if (speakers.Contains(line.Text))
                {
                    // A speaker's name is printed only when the speaker changes.
                    if (string.Equals(line.Text, lastSpeaker, StringComparison.OrdinalIgnoreCase))
                        continue;
                    lastSpeaker = line.Text;
                }
                result.Add(line);
            }

            return TranscriptFormatter.Format(result, withTimestamps, perTurn);
        }

        private static string FixLine(string line)
        {
            line = FixGlyphs(line);
            line = ApplyWordFixes(line);
            line = FixDigitsInWords(line);
            return line;
        }

        private static string FixGlyphs(string line)
            => line.Replace('\u2022', '\'')   // bullet -> apostrophe (OCR renders ' as •)
                   .Replace('\u00B4', '\'')   // acute accent -> apostrophe
                   .Replace('`', '\'');

        // Whole-word OCR corrections: apostrophe insertions the look-alike swap can't make on its own, plus
        // systematic f->t / h->n mis-reads that land on non-words (so they are safe to auto-correct).
        private static readonly Dictionary<string, string> WordFixes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["11m"] = "I'm",
            ["Weill"] = "We'll",
            ["tne"] = "the",
            ["yean"] = "yeah",
            // f -> t mis-reads: each key is a non-word; the value is the intended common f-word.
            ["trom"] = "from",
            ["tor"] = "for",
            ["tunny"] = "funny",
            ["tirst"] = "first",
            ["tind"] = "find",
            ["tound"] = "found",
            ["tocus"] = "focus",
            ["tocused"] = "focused",
            ["tunction"] = "function",
            ["tunctions"] = "functions",
            ["tuture"] = "future",
            ["tinal"] = "final",
            ["tinally"] = "finally",
            ["tollow"] = "follow",
            ["tollowing"] = "following",
            ["torward"] = "forward",
            ["tigure"] = "figure",
            ["tield"] = "field",
            ["triend"] = "friend",
            ["triends"] = "friends",
            ["tamily"] = "family",
            ["tull"] = "full",
            ["teel"] = "feel",
            ["teeling"] = "feeling",
            ["teedback"] = "feedback",
            ["teatures"] = "features",
            ["teature"] = "feature",
            ["tilters"] = "filters",
        };

        // One pass over the line: match any fixable whole word, then map it to its correction.
        private static readonly Regex WordFixPattern = new(
            @"\b(" + string.Join("|", WordFixes.Keys.Select(Regex.Escape)) + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string ApplyWordFixes(string line) =>
            WordFixPattern.Replace(line, m => WordFixes[m.Value]);

        private static readonly Dictionary<char, (char Upper, char Lower)> DigitLookAlike = new()
        {
            ['0'] = ('O', 'o'),
            ['1'] = ('I', 'l'),
            ['5'] = ('S', 's'),
            ['8'] = ('B', 'b'),
        };

        // Fixes digits that are really letters (e.g. "0K" -> "OK"), but only inside a mixed
        // letter/digit word. Standalone numbers and number-like tokens are left as numbers.
        private static string FixDigitsInWords(string line)
            => Regex.Replace(line, "[A-Za-z0-9]+", m => ConvertToken(m.Value));

        private static string ConvertToken(string token)
        {
            int letters = token.Count(char.IsLetter);
            int digits = token.Count(char.IsDigit);
            if (letters == 0 || digits == 0)
                return token;                                   // pure word or pure number
            if (letters < digits)
                return token;                                   // number-like ("10x", "24h")
            if (Regex.IsMatch(token, @"^\d+(st|nd|rd|th)$", RegexOptions.IgnoreCase))
                return token;                                   // ordinal ("1st", "2nd")
            if (token.Any(c => char.IsDigit(c) && !DigitLookAlike.ContainsKey(c)))
                return token;                                   // a digit with no look-alike

            bool upper = token.Where(char.IsLetter).All(char.IsUpper);
            var chars = token.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (DigitLookAlike.TryGetValue(chars[i], out var forms))
                    chars[i] = upper ? forms.Upper : forms.Lower;
            }
            return new string(chars);
        }

        private static HashSet<string> FindSpeakerNames(List<string> lines)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                if (SpeakerHeuristics.LooksLikeName(line))
                    counts[line] = counts.GetValueOrDefault(line) + 1;
            }

            // Recurring, name-shaped lines are the speaker headers.
            return counts.Where(kv => kv.Value >= 3)
                         .Select(kv => kv.Key)
                         .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
