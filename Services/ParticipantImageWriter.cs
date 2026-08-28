using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using CaptionScribe.Models;

namespace CaptionScribe.Services
{
    /// <summary>Composes a PNG of the meeting participants: a header, then one avatar/name row each.</summary>
    public sealed class ParticipantImageWriter
    {
        public void Write(string path, string title, IReadOnlyList<Participant> participants)
        {
            const int width = 460, pad = 16, avatar = 44, rowGap = 12;
            int rowH = avatar + rowGap;

            using var headerFont = new Font("Segoe UI", 12f, FontStyle.Bold);
            using var nameFont = new Font("Segoe UI", 11f);

            int headerH;
            using (var probe = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(probe))
                headerH = (int)Math.Ceiling(g.MeasureString(title, headerFont, width - 2 * pad).Height) + pad;

            int height = pad + headerH + Math.Max(1, participants.Count) * rowH + pad;

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);
                using var text = new SolidBrush(Color.FromArgb(0x22, 0x22, 0x22));

                g.DrawString(title, headerFont, text, new RectangleF(pad, pad, width - 2 * pad, headerH));

                int y = pad + headerH;
                foreach (var p in participants)
                {
                    DrawAvatar(g, p.AvatarPng, pad, y, avatar);
                    g.DrawString(p.Name, nameFont, text, pad + avatar + 12, y + (avatar - nameFont.Height) / 2f);
                    y += rowH;
                }
            }

            bmp.Save(path, ImageFormat.Png);
        }

        private static void DrawAvatar(Graphics g, byte[] png, int x, int y, int size)
        {
            using var circle = new GraphicsPath();
            circle.AddEllipse(x, y, size, size);

            if (png.Length == 0)
            {
                using var placeholder = new SolidBrush(Color.FromArgb(0xDD, 0xDD, 0xDD));
                g.FillPath(placeholder, circle);
                return;
            }

            try
            {
                using var ms = new MemoryStream(png);
                using var img = Image.FromStream(ms);
                var saved = g.Clip;
                g.SetClip(circle);
                g.DrawImage(img, new Rectangle(x, y, size, size));
                g.Clip = saved;
            }
            catch { /* skip a bad avatar crop */ }
        }
    }
}
