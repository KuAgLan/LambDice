using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LambDice.Ui
{
    /// <summary>原作配色与字体（文字带位图缓存，避免每帧 DrawString）。</summary>
    public static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(0x15, 0x12, 0x0F);
        public static readonly Color Cream = Color.FromArgb(0xF1, 0xE7, 0xC9);
        public static readonly Color CreamDim = Color.FromArgb(0xC9, 0xBE, 0xA2);
        public static readonly Color Parchment = Color.FromArgb(0xEF, 0xE6, 0xD2);
        public static readonly Color Gold = Color.FromArgb(0xD8, 0xB0, 0x4A);
        public static readonly Color Blood = Color.FromArgb(0xC1, 0x12, 0x1F);
        public static readonly Color BloodDark = Color.FromArgb(0x7A, 0x0C, 0x12);
        public static readonly Color Tan = Color.FromArgb(0xB9, 0xA2, 0x81);
        public static readonly Color TanDark = Color.FromArgb(0x8C, 0x7A, 0x5E);
        public static readonly Color TrayFrame = Color.FromArgb(0x2A, 0x2F, 0x26);
        public static readonly Color TrayInner = Color.FromArgb(0x14, 0x18, 0x12);
        public static readonly Color Grey = Color.FromArgb(0x8A, 0x8A, 0x86);

        public const int Width = 1600;
        public const int Height = 900;

        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

        public static Font Font(float size, bool bold = false)
        {
            string key = size + (bold ? "b" : "n");
            Font f;
            if (Fonts.TryGetValue(key, out f)) return f;
            try { f = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { f = new Font(FontFamily.GenericSansSerif, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
            Fonts[key] = f;
            return f;
        }

        // ------------------------------------------------------------------ 文字位图缓存

        /// <summary>当前窗口的缩放系数（文字/骰子位图按它渲染，保证 1:1 不重采样）。</summary>
        public static float DeviceScale = 1f;

        private static readonly Dictionary<string, Bitmap> TextCache = new Dictionary<string, Bitmap>();

        private static void TrimCache()
        {
            if (TextCache.Count <= 500) return;
            foreach (var b in TextCache.Values) b.Dispose();
            TextCache.Clear();
        }

        private static Bitmap Cached(string key, int w, int h, Action<Graphics> draw)
        {
            Bitmap bmp;
            if (TextCache.TryGetValue(key, out bmp)) return bmp;

            float k = Math.Max(0.5f, DeviceScale);
            bmp = new Bitmap(Math.Max(1, (int)Math.Ceiling(w * k)), Math.Max(1, (int)Math.Ceiling(h * k)),
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.ScaleTransform(k, k);
                draw(g);
            }

            TrimCache();
            TextCache[key] = bmp;
            return bmp;
        }

        public static void Text(Graphics g, string text, Font font, Color color, RectangleF rect,
            StringAlignment align = StringAlignment.Center, StringAlignment line = StringAlignment.Center, bool shadow = false)
        {
            if (string.IsNullOrEmpty(text)) return;

            int w = (int)Math.Ceiling(rect.Width);
            int h = (int)Math.Ceiling(rect.Height);
            string key = "t|" + text + "|" + font.Size + (font.Bold ? "b" : "n") + "|" + color.ToArgb()
                         + "|" + (int)align + "|" + (int)line + "|" + (shadow ? 1 : 0) + "|" + w + "x" + h + "|" + DeviceScale;

            Bitmap bmp = Cached(key, w, h, sg =>
            {
                using (var brush = new SolidBrush(color))
                using (var format = new StringFormat
                {
                    Alignment = align,
                    LineAlignment = line,
                    FormatFlags = StringFormatFlags.NoWrap,
                    Trimming = StringTrimming.None
                })
                {
                    if (shadow)
                    {
                        using (var dark = new SolidBrush(Color.FromArgb(color.A * 3 / 5, 0, 0, 0)))
                            sg.DrawString(text, font, dark, new RectangleF(3f, 3f, rect.Width, rect.Height), format);
                    }
                    sg.DrawString(text, font, brush, new RectangleF(0, 0, rect.Width, rect.Height), format);
                }
            });

            Blit.Image(g, bmp, rect);
        }

        public static void TextOutlined(Graphics g, string text, Font font, Color color, Color outline, RectangleF rect, float width = 4f,
            StringAlignment align = StringAlignment.Center)
        {
            if (string.IsNullOrEmpty(text)) return;

            int w = (int)Math.Ceiling(rect.Width);
            int h = (int)Math.Ceiling(rect.Height);
            string key = "o|" + text + "|" + font.Size + (font.Bold ? "b" : "n") + "|" + color.ToArgb()
                         + "|" + outline.ToArgb() + "|" + width + "|" + (int)align + "|" + w + "x" + h + "|" + DeviceScale;

            Bitmap bmp = Cached(key, w, h, sg =>
            {
                float pad = width * 2f;
                using (var path = new GraphicsPath())
                {
                    using (var format = new StringFormat
                    {
                        Alignment = align,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.NoWrap
                    })
                        path.AddString(text, font.FontFamily, (int)font.Style, font.Size,
                            new RectangleF(pad, pad, rect.Width - pad * 2, rect.Height - pad * 2), format);

                    using (var pen = new Pen(outline, width * 2f) { LineJoin = LineJoin.Round })
                        sg.DrawPath(pen, path);
                    using (var brush = new SolidBrush(color))
                        sg.FillPath(brush, path);
                }
            });

            Blit.Image(g, bmp, rect);
        }

        public static Color Alpha(Color c, float a)
        {
            return Color.FromArgb((int)Math.Max(0, Math.Min(255, c.A * a)), c.R, c.G, c.B);
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
