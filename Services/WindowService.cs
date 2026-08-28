using System;
using System.Linq;
using System.Text;
using CaptionScribe.Core.Interop;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>Locates the Teams meeting window and builds the capture-diagnostics report.</summary>
    internal sealed class WindowService
    {
        // Best-match Teams window from the last scan; revalidated cheaply before a full re-scan.
        private IntPtr _cachedTeamsWindow;

        /// <summary>Returns the best-matching Teams window handle, or IntPtr.Zero if none found.</summary>
        public IntPtr FindTeamsWindow(string? titleHint)
        {
            // Fast path: if the cached window is still a valid Teams window, skip enumerating all windows.
            if (_cachedTeamsWindow != IntPtr.Zero)
            {
                var cached = Win32.GetWindowInfo(_cachedTeamsWindow);
                if (cached is not null && Score(cached, titleHint) > 0)
                    return _cachedTeamsWindow;
                _cachedTeamsWindow = IntPtr.Zero;
            }

            IntPtr best = IntPtr.Zero;
            int bestScore = 0;
            foreach (var w in Win32.EnumerateTopLevelWindows())
            {
                int score = Score(w, titleHint);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = w.Handle;
                }
            }

            _cachedTeamsWindow = best;
            return best;
        }

        /// <summary>True when the top-level window shown at the given screen point belongs to Teams.</summary>
        public bool IsTeamsAtPoint(int x, int y)
        {
            string process = Win32.GetProcessNameAt(x, y).ToLowerInvariant();
            return process is "ms-teams" or "teams";
        }

        public string BuildCaptureDiagnostics(string? titleHint, CaptureRegion? region)
        {
            var sb = new StringBuilder();

            IntPtr foreground = Win32.GetForegroundWindowHandle();
            sb.AppendLine("Current foreground window:");
            sb.AppendLine($"  {Describe(foreground)}");
            sb.AppendLine();

            AppendCaptureRegionReport(sb, region);
            sb.AppendLine();

            var candidates = Win32.EnumerateTopLevelWindows()
                .Select(w => (window: w, score: Score(w, titleHint)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ToList();

            IntPtr resolved = candidates.Count > 0 ? candidates[0].window.Handle : IntPtr.Zero;
            sb.AppendLine("Resolved Teams window (would be brought to front):");
            if (resolved != IntPtr.Zero)
            {
                string flag = Win32.IsForeground(resolved) ? "  [already foreground]" : string.Empty;
                sb.AppendLine($"  {Describe(resolved)}{flag}");
            }
            else
            {
                sb.AppendLine("  (none found)");
            }
            sb.AppendLine();

            sb.AppendLine($"Teams candidates ({candidates.Count}):");
            if (candidates.Count == 0)
                sb.AppendLine("  (none)");
            foreach (var (window, score) in candidates)
            {
                string min = window.IsMinimized ? " [min]" : string.Empty;
                sb.AppendLine($"  [{score,3}] \"{window.Title}\"  ({window.ProcessName}, pid {window.ProcessId}){min}");
            }

            return sb.ToString();
        }

        private static string Describe(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return "(none)";

            string title = Win32.GetWindowTitle(hWnd);
            uint pid = Win32.GetWindowProcessId(hWnd);
            Win32.TryGetWindowRect(hWnd, out var rect);
            return $"\"{title}\"  (pid {pid}, hwnd 0x{hWnd.ToInt64():X}, rect {rect})";
        }

        // Reports the window under the capture region and whether it blocks screen capture — the symptom
        // when Teams live captions are popped out into their own (capture-excluded) window.
        private static void AppendCaptureRegionReport(StringBuilder sb, CaptureRegion? region)
        {
            sb.AppendLine("Capture region:");
            if (region is null)
            {
                sb.AppendLine("  (not set)");
                return;
            }

            sb.AppendLine($"  {region.Width}×{region.Height} @ ({region.X}, {region.Y})");

            int cx = region.X + region.Width / 2;
            int cy = region.Y + region.Height / 2;
            IntPtr under = Win32.RootWindowAt(cx, cy);
            sb.AppendLine($"  Window at centre: {Describe(under)}");

            if (Win32.IsCaptureProtected(under))
            {
                sb.AppendLine("  Screen capture: BLOCKED — this window opts out of screen capture and records as");
                sb.AppendLine("    blank. If these are Teams live captions in a pop-out window, dock them back into");
                sb.AppendLine("    the meeting window (Teams marks the pop-out as capture-protected).");
            }
            else
            {
                sb.AppendLine("  Screen capture: OK (window is capturable)");
            }
        }

        // Ranking weights: a Teams-owned process dominates; title cues ("teams", "meeting", the user's hint)
        // are additive tie-breakers so the actual meeting window outranks chats and other Teams windows.
        private const int BaseScore = 1;
        private const int TeamsProcessWeight = 100;
        private const int TeamsTitleWeight = 20;
        private const int MeetingKeywordWeight = 40;
        private const int TitleHintWeight = 30;

        internal static int Score(WindowInfo w, string? titleHint)
        {
            string title = w.Title.ToLowerInvariant();
            string process = (w.ProcessName ?? string.Empty).ToLowerInvariant();
            bool teamsProcess = process is "ms-teams" or "teams";
            bool teamsTitle = title.Contains("teams");

            if (!teamsProcess && !teamsTitle)
                return 0;

            int score = BaseScore;
            if (teamsProcess) score += TeamsProcessWeight;
            if (teamsTitle) score += TeamsTitleWeight;
            if (title.Contains("meeting")) score += MeetingKeywordWeight;
            if (!string.IsNullOrWhiteSpace(titleHint) && title.Contains(titleHint!.ToLowerInvariant())) score += TitleHintWeight;
            return score;
        }
    }
}
