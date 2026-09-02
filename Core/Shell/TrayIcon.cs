using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace CaptionScribe.Core.Shell
{
    /// <summary>Owns the tray NotifyIcon, its menu, and the vector active/inactive icons.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        // Shared shape (a leaf/comma outline plus a diagonal stroke); colours differ per state.
        private const string CommaData = "M14,80 C18,47 37,20 94,4 C91,50 70,76 38,84 C29,86 21,84 14,80 Z";
        private const string SlashData = "M5,96 L95,5";

        private readonly WinForms.NotifyIcon _notifyIcon;
        private readonly WinForms.ContextMenuStrip _menu;
        private readonly Drawing.Font _openFont;
        private readonly WinForms.ToolStripMenuItem _activeItem;
        private readonly WinForms.ToolStripMenuItem _newScribeItem;
        private readonly Drawing.Icon _activeIcon;
        private readonly Drawing.Icon _inactiveIcon;
        private const int BalloonDurationMs = 3000;

        public TrayIcon(Action onOpen, Action onNewScribe, Action onToggleActive, Action onShowRegion,
            Action onSetRegion, Action onSettings, Action onExit)
        {
            _activeIcon = RenderIcon(active: true);
            _inactiveIcon = RenderIcon(active: false);

            _menu = new WinForms.ContextMenuStrip();
            _openFont = new Drawing.Font(_menu.Font, Drawing.FontStyle.Bold);
            var openItem = new WinForms.ToolStripMenuItem("Open", null, (_, _) => onOpen())
            {
                Font = _openFont,
            };
            _newScribeItem = new WinForms.ToolStripMenuItem("New Scribe", null, (_, _) => onNewScribe());
            _activeItem = new WinForms.ToolStripMenuItem("Active", null, (_, _) => onToggleActive())
            {
                CheckOnClick = false,
            };
            var showRegionItem = new WinForms.ToolStripMenuItem("Show Capture Region", null, (_, _) => onShowRegion());
            var setRegionItem = new WinForms.ToolStripMenuItem("Set Capture Region…", null, (_, _) => onSetRegion());
            var settingsItem = new WinForms.ToolStripMenuItem("Settings…", null, (_, _) => onSettings());
            var exitItem = new WinForms.ToolStripMenuItem("Exit", null, (_, _) => onExit());

            _menu.Items.Add(openItem);
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            _menu.Items.Add(_newScribeItem);
            _menu.Items.Add(_activeItem);
            _menu.Items.Add(showRegionItem);
            _menu.Items.Add(setRegionItem);
            _menu.Items.Add(settingsItem);
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            _menu.Items.Add(exitItem);

            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = _inactiveIcon,
                Visible = true,
                Text = "Caption Scribe - idle",
                ContextMenuStrip = _menu,
            };
            _notifyIcon.DoubleClick += (_, _) => onOpen();
        }

        public void SetActive(bool active)
        {
            _activeItem.Checked = active;
            _newScribeItem.Enabled = !active;
            _notifyIcon.Icon = active ? _activeIcon : _inactiveIcon;
            _notifyIcon.Text = active ? "Caption Scribe - capturing" : "Caption Scribe - idle";
        }

        public void ShowBalloon(string message, WinForms.ToolTipIcon icon, string title = "Caption Scribe")
            => _notifyIcon.ShowBalloonTip(BalloonDurationMs, title, message, icon);

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip = null;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _openFont.Dispose();
            _activeIcon.Dispose();
            _inactiveIcon.Dispose();
        }

        // ---- vector icon rendering ----

        private static Drawing.Icon RenderIcon(bool active)
        {
            var commaColor = active ? Color.FromRgb(0x5B, 0x2A, 0x91) : Colors.Black;
            var slashColor = active ? Color.FromRgb(0xE6, 0x00, 0x3C) : Colors.Black;

            var comma = MakeStroke(CommaData, commaColor, 7);
            var slash = MakeStroke(SlashData, slashColor, 8);
            return Rasterize(32, comma, slash);
        }

        private static (Geometry geo, Pen pen) MakeStroke(string data, Color color, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(color), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();
            return (Geometry.Parse(data), pen);
        }

        private static Drawing.Icon Rasterize(int size, params (Geometry geo, Pen pen)[] strokes)
        {
            Rect bounds = Rect.Empty;
            double maxThickness = 0;
            foreach (var (geo, pen) in strokes)
            {
                bounds.Union(geo.Bounds);
                maxThickness = Math.Max(maxThickness, pen.Thickness);
            }

            // Pad so the stroke isn't clipped, then fit the drawing centred into the square.
            double pad = maxThickness / 2.0 + 1;
            bounds = new Rect(bounds.X - pad, bounds.Y - pad, bounds.Width + 2 * pad, bounds.Height + 2 * pad);
            double scale = size / Math.Max(bounds.Width, bounds.Height);
            double offX = (size - bounds.Width * scale) / 2;
            double offY = (size - bounds.Height * scale) / 2;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(offX, offY));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                foreach (var (geo, pen) in strokes)
                    dc.DrawGeometry(null, pen, geo);
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            encoder.Save(stream);
            stream.Position = 0;

            using var bitmap = new Drawing.Bitmap(stream);
            IntPtr hicon = bitmap.GetHicon();
            try { return (Drawing.Icon)Drawing.Icon.FromHandle(hicon).Clone(); }
            finally { DestroyIcon(hicon); }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
