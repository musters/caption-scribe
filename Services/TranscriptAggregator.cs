using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Stitches successive OCR snapshots of a scrolling caption box into one growing transcript.
    /// Teams shows only the last few caption lines and the bottom line keeps growing as someone
    /// speaks, so each snapshot overlaps the previous one. We find that overlap and append only
    /// the genuinely new content, tolerating small OCR differences via fuzzy matching.
    /// </summary>
    public sealed class TranscriptAggregator
    {
        private readonly List<string> _lines = new();
        private readonly List<DateTime> _times = new();
        private double _similarityThreshold;
        private readonly object _gate = new();

        // Committed lines beyond the snapshot's length that we still scan for overlap, so a line the
        // committed side dropped earlier doesn't hide the true overlap.
        private const int OverlapSearchSlack = 4;

        // Recent committed lines checked on append, to drop a re-rendered duplicate the alignment missed.
        private const int DedupWindow = 4;

        // Short lines repeat too naturally to dedup safely; only check lines with at least this many words.
        private const int MinDedupWordCount = 3;

        public TranscriptAggregator(double similarityThreshold)
        {
            _similarityThreshold = similarityThreshold;
        }

        /// <summary>Fuzzy-match threshold; can be updated live when the setting changes.</summary>
        public double SimilarityThreshold
        {
            get { lock (_gate) return _similarityThreshold; }
            set { lock (_gate) _similarityThreshold = value; }
        }

        public void AddSnapshot(IReadOnlyList<string> rawLines)
        {
            var snapshot = rawLines
                .Select(NormalizeWhitespace)
                .Where(s => s.Length > 0)
                .ToList();
            if (snapshot.Count == 0)
                return;

            lock (_gate)
            {
                var now = DateTime.Now;
                try
                {
                    if (_lines.Count == 0)
                    {
                        _lines.AddRange(snapshot);
                        return;
                    }

                    Merge(snapshot);
                }
                finally
                {
                    // Newly appended lines get this snapshot's time; overwritten lines keep theirs.
                    while (_times.Count < _lines.Count)
                        _times.Add(now);
                }
            }
        }

        // Merges a snapshot into the transcript by finding the overlap between the committed tail and
        // the snapshot as a fuzzy longest-common-subsequence, then appending only what is genuinely new.
        // Using a subsequence (rather than a positional suffix match) tolerates a line OCR dropped in the
        // middle of the overlap, which would otherwise cause a block of lines to be duplicated.
        private void Merge(List<string> snapshot)
        {
            int n = _lines.Count;
            int t0 = Math.Max(0, n - snapshot.Count - OverlapSearchSlack);
            var pairs = LcsPairs(t0, snapshot);

            if (pairs.Count == 0)
            {
                // No overlap: this is unrelated, genuinely new content.
                AppendFrom(snapshot, 0);
                return;
            }

            // Refresh the overlapped lines with the newer OCR text (absorbs corrections and growth).
            foreach (var (ci, sj) in pairs)
                _lines[ci] = snapshot[sj];

            var (lastCommitted, lastSnapshot) = pairs[^1];
            int afterCommitted = lastCommitted + 1;
            int afterSnapshot = lastSnapshot + 1;

            if (afterCommitted == n)
            {
                // Overlap runs to the end of the transcript — append whatever follows it.
                AppendFrom(snapshot, afterSnapshot);
            }
            else if (afterCommitted == n - 1 && afterSnapshot < snapshot.Count)
            {
                // One trailing (live) line remains and the snapshot continues past the anchor: the live
                // line is being revised, so overwrite it in place, then append the rest.
                _lines[n - 1] = snapshot[afterSnapshot];
                AppendFrom(snapshot, afterSnapshot + 1);
            }
            else
            {
                // The snapshot dropped one or more committed trailing lines — keep them (a later frame
                // usually restores context) and append only the snapshot's new tail.
                AppendFrom(snapshot, afterSnapshot);
            }
        }

        private void AppendFrom(List<string> snapshot, int start)
        {
            for (int j = start; j < snapshot.Count; j++)
            {
                var line = snapshot[j];
                if (!IsRecentDuplicate(line))
                    _lines.Add(line);
            }
        }

        // A multi-word line that near-matches one of the last few committed lines is a re-render of
        // already-captured speech, not new content, so it must not be appended again.
        private bool IsRecentDuplicate(string line)
        {
            if (WordCount(line) < MinDedupWordCount)
                return false;
            int from = Math.Max(0, _lines.Count - DedupWindow);
            for (int i = _lines.Count - 1; i >= from; i--)
                if (LinesMatch(_lines[i], line))
                    return true;
            return false;
        }

        private static int WordCount(string line)
        {
            int words = line.Length == 0 ? 0 : 1;
            foreach (var c in line)
                if (c == ' ') words++;
            return words;
        }

        // Fuzzy longest common subsequence of committed[t0..] and the snapshot. Returns the matched
        // (committedIndex, snapshotIndex) pairs in increasing order.
        private List<(int Committed, int Snapshot)> LcsPairs(int t0, List<string> snapshot)
        {
            int a = _lines.Count - t0;
            int b = snapshot.Count;
            var dp = new int[a + 1, b + 1];
            for (int i = 1; i <= a; i++)
                for (int j = 1; j <= b; j++)
                    dp[i, j] = LinesRelated(_lines[t0 + i - 1], snapshot[j - 1])
                        ? dp[i - 1, j - 1] + 1
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);

            var pairs = new List<(int, int)>();
            for (int i = a, j = b; i > 0 && j > 0;)
            {
                if (LinesRelated(_lines[t0 + i - 1], snapshot[j - 1]))
                {
                    pairs.Add((t0 + i - 1, j - 1));
                    i--;
                    j--;
                }
                else if (dp[i - 1, j] >= dp[i, j - 1])
                {
                    i--;
                }
                else
                {
                    j--;
                }
            }
            pairs.Reverse();
            return pairs;
        }

        // Two lines are "the same" for overlap purposes when they fuzzy-match or the incoming line is the
        // committed one still growing (a prefix extension of the live line).
        private bool LinesRelated(string committed, string incoming)
            => LinesMatch(committed, incoming) || LooksLikeExtension(committed, incoming);

        private bool LinesMatch(string a, string b)
        {
            if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
                return true;
            return Similarity(a, b) >= _similarityThreshold;
        }

        private static bool LooksLikeExtension(string existing, string incoming)
        {
            if (existing.Length == 0)
                return false;
            var e = existing.ToLowerInvariant();
            var n = incoming.ToLowerInvariant();
            return n.Length >= e.Length && n.StartsWith(e, StringComparison.Ordinal);
        }

        public IReadOnlyList<string> GetLines()
        {
            lock (_gate)
                return _lines.ToList();
        }

        public IReadOnlyList<TimedLine> GetTimedLines()
        {
            lock (_gate)
                return BuildTimed(0);
        }

        public IReadOnlyList<TimedLine> GetTimedTail(int maxLines)
        {
            lock (_gate)
                return BuildTimed(Math.Max(0, _lines.Count - maxLines));
        }

        private List<TimedLine> BuildTimed(int start)
        {
            var result = new List<TimedLine>(_lines.Count - start);
            for (int i = start; i < _lines.Count; i++)
                result.Add(new TimedLine(_lines[i], _times[i]));
            return result;
        }

        public string GetText()
        {
            lock (_gate)
                return string.Join(Environment.NewLine, _lines);
        }

        public int Count
        {
            get { lock (_gate) return _lines.Count; }
        }

        // Returns only the last maxLines joined, so display cost is independent of transcript length.
        public string GetTailText(int maxLines)
        {
            lock (_gate)
            {
                int start = Math.Max(0, _lines.Count - maxLines);
                if (start == 0)
                    return string.Join(Environment.NewLine, _lines);

                var sb = new StringBuilder();
                for (int i = start; i < _lines.Count; i++)
                {
                    if (i > start)
                        sb.Append(Environment.NewLine);
                    sb.Append(_lines[i]);
                }
                return sb.ToString();
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _lines.Clear();
                _times.Clear();
            }
        }

        private static string NormalizeWhitespace(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;

            var sb = new StringBuilder(s.Length);
            bool prevSpace = false;
            foreach (var ch in s.Trim())
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!prevSpace)
                        sb.Append(' ');
                    prevSpace = true;
                }
                else
                {
                    sb.Append(ch);
                    prevSpace = false;
                }
            }
            return sb.ToString();
        }

        private static double Similarity(string a, string b)
        {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            int distance = Levenshtein(a, b);
            int max = Math.Max(a.Length, b.Length);
            return max == 0 ? 1.0 : 1.0 - (double)distance / max;
        }

        private static int Levenshtein(string a, string b)
        {
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            var prev = new int[m + 1];
            var curr = new int[m + 1];
            for (int j = 0; j <= m; j++)
                prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[m];
        }
    }
}
