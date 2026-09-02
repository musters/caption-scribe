using System;
using System.Drawing;
using System.Windows.Forms;
using CaptionScribe.Models;

namespace CaptionScribe.Views
{
    /// <summary>
    /// Full virtual-screen overlay for picking a capture region. The selection is clamped to the
    /// monitor where the drag starts, so a region never spans two screens. Uses raw pixels
    /// (AutoScaleMode.None) so it is correct across monitors with different DPI.
    /// </summary>
    internal sealed class RegionSelectorForm : Form
    {
        private const int MinRegionSize = 5;
        private readonly Font _hintFont = new("Segoe UI", 12f);
        private readonly SolidBrush _hintBrush = new(Color.White);
        private Point _start;
        private Rectangle _selection;
        private Rectangle _startScreenClient;
        private bool _dragging;

        public CaptureRegion? SelectedRegion { get; private set; }

        public RegionSelectorForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Color.Black;
            Opacity = 0.45;
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Select capture region";
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            _dragging = true;
            _start = e.Location;
            _startScreenClient = ToClient(Screen.FromPoint(PointToScreen(e.Location)).Bounds);
            _selection = Rectangle.Empty;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
                return;

            var p = ClampToStartScreen(e.Location);
            _selection = Rectangle.FromLTRB(
                Math.Min(_start.X, p.X), Math.Min(_start.Y, p.Y),
                Math.Max(_start.X, p.X), Math.Max(_start.Y, p.Y));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
                return;

            _dragging = false;

            if (_selection.Width < MinRegionSize || _selection.Height < MinRegionSize)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            var origin = PointToScreen(_selection.Location);
            SelectedRegion = new CaptureRegion
            {
                X = origin.X,
                Y = origin.Y,
                Width = _selection.Width,
                Height = _selection.Height,
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private Point ClampToStartScreen(Point clientPoint)
        {
            int x = Math.Clamp(clientPoint.X, _startScreenClient.Left, _startScreenClient.Right);
            int y = Math.Clamp(clientPoint.Y, _startScreenClient.Top, _startScreenClient.Bottom);
            return new Point(x, y);
        }

        private Rectangle ToClient(Rectangle screenRect)
            => new(PointToClient(screenRect.Location), screenRect.Size);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            g.DrawString(
                "Drag over the captions on the meeting's monitor.  Release to confirm · Esc to cancel",
                _hintFont, _hintBrush, 24, 24);

            if (_selection.Width > 0 && _selection.Height > 0)
            {
                using var fill = new SolidBrush(Color.FromArgb(64, 59, 130, 246));
                using var pen = new Pen(Color.FromArgb(255, 59, 130, 246), 2f);
                g.FillRectangle(fill, _selection);
                g.DrawRectangle(pen, _selection);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hintFont.Dispose();
                _hintBrush.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
