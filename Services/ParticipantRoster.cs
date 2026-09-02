using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>
    /// Collects unique meeting participants (name + avatar) from OCR'd conversation frames.
    /// Names are fuzzy-deduplicated and majority-voted across frames; the avatar is cropped from
    /// the square immediately left of each name line.
    /// </summary>
    public sealed class ParticipantRoster
    {
        private const double NameMatchThreshold = 0.72;

        // Avatar crop geometry, calibrated against a real Teams frame: the circular avatar sits just left of
        // the name, is ~4x the name-text height, and is centred ~1.35x that height below the name's top.
        // Deliberately generous (extra whitespace is fine) so small OCR height variations still capture it.
        private const double AvatarSizeFactor = 4.0;
        private const double AvatarRightGapFactor = 0.25;
        private const double AvatarCenterYFactor = 1.35;

        // Extra margin (× name height, on each side) kept around the avatar when saving, so the circular mask
        // in the participants image leaves a little space instead of clipping a marginally off-centre avatar.
        private const double AvatarPadFactor = 0.3;

        // A real avatar (photo or coloured initials) varies; blank padding beside a non-name line is near-uniform.
        private const double AvatarContentStdDevThreshold = 10.0;

        // A name must be seen in at least this many samples to count, filtering one-frame OCR flukes.
        private const int MinVotesToInclude = 2;

        private readonly object _gate = new();
        private readonly List<Entry> _entries = new();

        private sealed class Entry
        {
            public byte[] Avatar = Array.Empty<byte>();
            public readonly Dictionary<string, int> NameVotes = new(StringComparer.OrdinalIgnoreCase);
            public string BestName => NameVotes.OrderByDescending(kv => kv.Value).First().Key;
            public int TotalVotes => NameVotes.Values.Sum();
        }

        public int Count { get { lock (_gate) return _entries.Count(e => e.TotalVotes >= MinVotesToInclude); } }

        public void Clear() { lock (_gate) _entries.Clear(); }

        /// <summary>Scans a color frame's OCR lines, adding/updating participants for name-shaped lines.</summary>
        public void Observe(Bitmap frame, IReadOnlyList<RecognizedLine> lines)
        {
            lock (_gate)
            {
                foreach (var line in lines)
                {
                    if (!SpeakerHeuristics.LooksLikeName(line.Text) || !IsLikelyPersonName(line.Text))
                        continue;
                    var existing = Find(line.Text);
                    if (existing is not null)
                    {
                        existing.NameVotes[line.Text] = existing.NameVotes.GetValueOrDefault(line.Text) + 1;
                        if (existing.Avatar.Length == 0)
                        {
                            var filled = CropAvatar(frame, line);
                            if (filled.Length > 0)
                                existing.Avatar = filled;
                        }
                        continue;
                    }
                    var avatar = CropAvatar(frame, line);
                    if (avatar.Length == 0)
                        continue;
                    var entry = new Entry { Avatar = avatar };
                    entry.NameVotes[line.Text] = 1;
                    _entries.Add(entry);
                }
            }
        }

        public IReadOnlyList<Participant> Snapshot()
        {
            lock (_gate)
                return _entries
                    .Where(e => e.TotalVotes >= MinVotesToInclude)
                    .Select(e => new Participant { Name = e.BestName, AvatarPng = e.Avatar })
                    .ToList();
        }

        private Entry? Find(string name)
        {
            foreach (var e in _entries)
                if (TextSimilarity.Meets(e.BestName, name, NameMatchThreshold))
                    return e;
            return null;
        }

        // Stricter than LooksLikeName: every word is a real 2+ letter, not-all-caps token (letters/'/-/. only).
        // Rejects OCR garbage that happens to be Title-Case, e.g. "Lil<e WI", "Tirusn I", "But I".
        private static bool IsLikelyPersonName(string text)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || words.Length > 4)
                return false;

            foreach (var w in words)
            {
                if (w.Length < 2 || !char.IsUpper(w[0]))
                    return false;

                int letters = 0, upper = 0;
                foreach (char c in w)
                {
                    if (char.IsLetter(c)) { letters++; if (char.IsUpper(c)) upper++; }
                    else if (c is not ('\'' or '-' or '.')) return false;   // digit or symbol like '<'
                }
                if (letters < 2 || upper == letters)   // too few letters, or an all-caps token like "WI"
                    return false;
            }
            return true;
        }

        private static byte[] CropAvatar(Bitmap frame, RecognizedLine line)
        {
            // The avatar is the circular image immediately to the LEFT of the speaker name.
            double h = line.Height;
            double diameter = h * AvatarSizeFactor;
            double centerX = line.X - h * AvatarRightGapFactor - diameter / 2.0;
            double centerY = line.Y + h * AvatarCenterYFactor;

            // Presence is judged on a tight box around the avatar (the std-dev threshold is calibrated for it).
            var tight = Clamp(SquareAround(centerX, centerY, diameter), frame.Width, frame.Height);
            if (tight.Width <= 0 || tight.Height <= 0)
                return Array.Empty<byte>();
            using (var probe = CropRegion(frame, tight))
                if (!HasAvatarContent(probe))
                    return Array.Empty<byte>();

            // Save a slightly larger box so the round mask in the image leaves a margin instead of clipping the avatar.
            var rect = Clamp(SquareAround(centerX, centerY, diameter + 2 * h * AvatarPadFactor), frame.Width, frame.Height);
            using var crop = CropRegion(frame, rect);
            using var ms = new MemoryStream();
            crop.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        private static Rectangle SquareAround(double centerX, double centerY, double side)
        {
            int s = (int)Math.Round(side);
            int x = (int)Math.Round(centerX - side / 2.0);
            int y = (int)Math.Round(centerY - side / 2.0);
            return new Rectangle(x, y, s, s);
        }

        private static Bitmap CropRegion(Bitmap frame, Rectangle rect)
        {
            var crop = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(crop);
            g.DrawImage(frame, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
            return crop;
        }

        // Rejects blank/near-uniform crops: a name-shaped line with no avatar beside it (e.g. a shared-item title).
        private static unsafe bool HasAvatarContent(Bitmap crop)
        {
            int step = Math.Max(1, Math.Min(crop.Width, crop.Height) / 24);
            var rect = new Rectangle(0, 0, crop.Width, crop.Height);
            var data = crop.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                double sum = 0, sumSq = 0;
                int n = 0;
                byte* scan0 = (byte*)data.Scan0;
                for (int y = 0; y < crop.Height; y += step)
                {
                    byte* row = scan0 + y * data.Stride;
                    for (int x = 0; x < crop.Width; x += step)
                    {
                        byte* px = row + x * 4;   // BGRA in memory
                        double lum = 0.114 * px[0] + 0.587 * px[1] + 0.299 * px[2];
                        sum += lum;
                        sumSq += lum * lum;
                        n++;
                    }
                }
                if (n == 0)
                    return false;
                double mean = sum / n;
                double variance = (sumSq / n) - (mean * mean);
                return variance > 0 && Math.Sqrt(variance) >= AvatarContentStdDevThreshold;
            }
            finally
            {
                crop.UnlockBits(data);
            }
        }

        private static Rectangle Clamp(Rectangle r, int width, int height)
        {
            int x = Math.Max(0, r.X);
            int y = Math.Max(0, r.Y);
            int right = Math.Min(width, r.Right);
            int bottom = Math.Min(height, r.Bottom);
            return new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }
    }
}
