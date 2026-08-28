using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CaptionScribe.Core.Interop
{
    /// <summary>
    /// Thin Win32 interop layer. P/Invoke declarations live in the "Import Functions" region;
    /// friendlier wrappers live in the "Methods" region.
    /// </summary>
    internal static class Win32
    {
        #region Structs

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;

            public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        private const int SW_RESTORE = 9;
        private const uint GA_ROOT = 2;

        // SetWindowDisplayAffinity values that opt a window out of screen capture.
        private const uint WDA_MONITOR = 0x01;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

        #endregion

        #region Import Functions

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("User32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("User32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("User32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("User32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, [Out] StringBuilder lpString, int nMaxCount);

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("User32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("User32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("User32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("User32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("User32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("User32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("User32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("User32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("User32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("User32.dll")]
        private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint dwAffinity);

        [DllImport("Kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        #endregion

        #region Methods

        public static IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

        public static bool IsForeground(IntPtr hWnd) => hWnd != IntPtr.Zero && GetForegroundWindow() == hWnd;

        public static bool TryGetWindowRect(IntPtr hWnd, out RECT rect) => GetWindowRect(hWnd, out rect);

        /// <summary>Moves and resizes a window using physical-pixel coordinates.</summary>
        public static void MoveWindowTo(IntPtr hWnd, int x, int y, int width, int height)
            => MoveWindow(hWnd, x, y, width, height, true);

        public static string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length <= 0)
                return string.Empty;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static uint GetWindowProcessId(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            return pid;
        }

        /// <summary>Top-level window shown at a screen point (physical px), or IntPtr.Zero.</summary>
        public static IntPtr RootWindowAt(int x, int y)
        {
            IntPtr hWnd = WindowFromPoint(new POINT(x, y));
            if (hWnd == IntPtr.Zero)
                return IntPtr.Zero;
            IntPtr root = GetAncestor(hWnd, GA_ROOT);
            return root != IntPtr.Zero ? root : hWnd;
        }

        /// <summary>Process name of the top-level window shown at a screen point (physical px), or "".</summary>
        public static string GetProcessNameAt(int x, int y)
        {
            IntPtr root = RootWindowAt(x, y);
            return root == IntPtr.Zero ? string.Empty : SafeGetProcessName(GetWindowProcessId(root));
        }

        /// <summary>True when a window opts out of screen capture via SetWindowDisplayAffinity (reads as blank).</summary>
        public static bool IsCaptureProtected(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !GetWindowDisplayAffinity(hWnd, out uint affinity))
                return false;
            return affinity is WDA_EXCLUDEFROMCAPTURE or WDA_MONITOR;
        }

        /// <summary>
        /// Best-effort activation. Uses the AttachThreadInput dance to work around the Windows
        /// foreground lock. Returns whether SetForegroundWindow reported success.
        /// </summary>
        public static bool TryActivate(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return false;

            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);

            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
            uint targetThread = GetWindowThreadProcessId(hWnd, out _);
            uint currentThread = GetCurrentThreadId();

            bool attachedForeground = false;
            bool attachedTarget = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
                if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
                    attachedTarget = AttachThreadInput(currentThread, targetThread, true);

                BringWindowToTop(hWnd);
                return SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
                if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        public static IReadOnlyList<WindowInfo> EnumerateTopLevelWindows()
        {
            var results = new List<WindowInfo>();

            EnumWindows((hWnd, _) =>
            {
                var info = GetWindowInfo(hWnd);
                if (info is not null)
                    results.Add(info);
                return true;
            }, IntPtr.Zero);

            return results;
        }

        // Builds info for a single window, or null if it is not a visible, titled window.
        public static WindowInfo? GetWindowInfo(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd))
                return null;

            string title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return null;

            uint pid = GetWindowProcessId(hWnd);
            return new WindowInfo(hWnd, title, SafeGetProcessName(pid), pid, IsIconic(hWnd));
        }

        private static string SafeGetProcessName(uint pid)
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                return p.ProcessName;
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion
    }
}
