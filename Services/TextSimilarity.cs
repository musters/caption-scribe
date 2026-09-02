using System;

namespace CaptionScribe.Services
{
    /// <summary>Levenshtein-based similarity ratio (0..1), used to fuzzy-match OCR'd names.</summary>
    internal static class TextSimilarity
    {
        public static bool Meets(string a, string b, double threshold)
        {
            if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
                return true;
            int max = Math.Max(a.Length, b.Length);
            if (max == 0)
                return true;
            if ((max - Math.Min(a.Length, b.Length)) / (double)max > 1.0 - threshold)
                return false;
            return Ratio(a, b) >= threshold;
        }

        public static double Ratio(string a, string b)
        {
            int n = a.Length, m = b.Length;
            int max = Math.Max(n, m);
            if (max == 0)
                return 1.0;
            int distance = LevenshteinOrdinalIgnoreCase(a, b);
            return 1.0 - (double)distance / max;
        }

        private static int LevenshteinOrdinalIgnoreCase(string a, string b)
        {
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            var prev = new int[m + 1];
            var curr = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[m];
        }
    }
}
