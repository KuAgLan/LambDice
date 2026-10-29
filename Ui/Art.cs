using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LambDice.Ui
{
    public enum DieTint { Normal, Pair, Triple, Ghost, Destroyed }

    /// <summary>所有美术资源都用代码画：血迹、棋盘、骰盆、骰子、缎带、花纹、头像。</summary>
    public static class Art
    {
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        // ------------------------------------------------------------------ 工具

        /// <summary>手绘感的圆角矩形（轻微抖动）。</summary>
        public static GraphicsPath WobblyRect(RectangleF r, float radius, float wobble, int seed)
        {
            const int steps = 64;
            var pts = new List<PointF>(steps);
            float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f;
            float hw = r.Width * 0.5f, hh = r.Height * 0.5f;
            float rad = Math.Min(radius, Math.Min(hw, hh));

            for (int i = 0; i < steps; i++)
            {
                double a = i * Math.PI * 2 / steps;
                float ux = (float)Math.Cos(a), uy = (float)Math.Sin(a);
                // 超椭圆逼近圆角矩形
                float k = 5.5f;
                float sx = (float)(Math.Sign(ux) * Math.Pow(Math.Abs(ux), 2f / k));
                float sy = (float)(Math.Sign(uy) * Math.Pow(Math.Abs(uy), 2f / k));
                float x = cx + sx * hw, y = cy + sy * hh;

                // 让四个角更圆
                float px = Math.Abs(x - cx) / hw, py = Math.Abs(y - cy) / hh;
                float cornerT = Math.Min(1f, Math.Max(0f, (Math.Min(px, py) - 0.62f) / 0.38f));
                float pull = 1f - cornerT * (rad / Math.Min(hw, hh)) * 0.30f;

                x = cx + (x - cx) * pull + Noise(seed, i) * wobble;
                y = cy + (y - cy) * pull + Noise(seed + 91, i) * wobble;
                pts.Add(new PointF(x, y));
            }

            var path = new GraphicsPath();
            path.AddClosedCurve(pts.ToArray(), 0.4f);
            return path;
        }

        private static float Noise(int seed, int i)
        {
            double v = Math.Sin(seed * 12.9898 + i * 78.233) * 43758.5453;
            return (float)((v - Math.Floor(v)) * 2 - 1);
        }

        public static void Shadow(Graphics g, RectangleF r, int radius, int alpha, int offset)
        {
            using (var path = Theme.RoundRect(new RectangleF(r.X - 3, r.Y + offset, r.Width + 6, r.Height), radius))
            using (var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                g.FillPath(brush, path);
        }

        public static void Dot(Graphics g, float x, float y, float r, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        }

        // ------------------------------------------------------------------ 背景

        public static Bitmap Background()
        {
            const string key = "bg";
            if (Cache.TryGetValue(key, out Bitmap cached)) return cached;

            int w = Theme.Width, h = Theme.Height;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(255, 6, 5, 5));
                var rnd = new Random(20261027);

                // 四角的干血块
                BloodMass(g, rnd, new PointF(60, 40), 330, 190);
                BloodMass(g, rnd, new PointF(1545, 20), 330, 210);
                BloodMass(g, rnd, new PointF(20, 860), 340, 210);
                BloodMass(g, rnd, new PointF(1570, 880), 350, 220);
                BloodMass(g, rnd, new PointF(520, -70), 300, 150);
                BloodMass(g, rnd, new PointF(1080, 950), 320, 150);

                // 边缘压暗
                using (var v = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(110, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90f))
                    g.FillRectangle(v, 0, 0, w, h);
                using (var v = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0), 90f))
                    g.FillRectangle(v, 0, 0, w, h);

                // 暗角
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(-w * 0.16f, -h * 0.22f, w * 1.32f, h * 1.44f);
                    using (var brush = new PathGradientBrush(path)
                    {
                        CenterColor = Color.FromArgb(0, 0, 0, 0),
                        SurroundColors = new[] { Color.FromArgb(230, 0, 0, 0) },
                        FocusScales = new PointF(0.58f, 0.58f)
                    })
                        g.FillRectangle(brush, 0, 0, w, h);
                }

                // 细颗粒
                for (int i = 0; i < 1100; i++)
                {
                    int x = rnd.Next(w), y = rnd.Next(h);
                    int v = rnd.Next(5, 14);
                    bmp.SetPixel(x, y, Color.FromArgb(255, v, v, v));
                }
            }

            Cache[key] = bmp;
            return bmp;
        }

        /// <summary>不规则干血块：一个主团 + 卫星团，褐色填充、红色描边。</summary>
        private static void BloodMass(Graphics g, Random rnd, PointF c, float rx, float ry)
        {
            var main = BlobPath(c, rx, ry, 18, 0.42f, rnd);
            PaintBlob(g, main, rnd);

            int satellites = rnd.Next(3, 6);
            for (int i = 0; i < satellites; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2;
                float d = (float)(0.55 + rnd.NextDouble() * 0.5);
                var p = new PointF(c.X + (float)Math.Cos(a) * rx * d, c.Y + (float)Math.Sin(a) * ry * d);
                var blob = BlobPath(p, rx * 0.35f, ry * 0.32f, 12, 0.5f, rnd);
                PaintBlob(g, blob, rnd);
            }

            // 飞溅小点
            for (int i = 0; i < 14; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2;
                float d = (float)(0.8 + rnd.NextDouble() * 0.6);
                Dot(g, c.X + (float)Math.Cos(a) * rx * d, c.Y + (float)Math.Sin(a) * ry * d,
                    (float)(rnd.NextDouble() * 5 + 1.5), Color.FromArgb(210, 0xB5, 0x12, 0x1B));
            }
        }

        private static GraphicsPath BlobPath(PointF c, float rx, float ry, int points, float wobble, Random rnd)
        {
            var pts = new PointF[points];
            float phase = (float)(rnd.NextDouble() * 6.28);
            for (int i = 0; i < points; i++)
            {
                double a = i * Math.PI * 2 / points;
                float k = 1f + (float)Math.Sin(a * 3 + phase) * wobble * 0.5f
                             + (float)Math.Sin(a * 5 + phase * 2) * wobble * 0.35f
                             + (float)(rnd.NextDouble() - 0.5) * wobble * 0.4f;
                pts[i] = new PointF(c.X + (float)Math.Cos(a) * rx * k, c.Y + (float)Math.Sin(a) * ry * k);
            }
            var path = new GraphicsPath();
            path.AddClosedCurve(pts, 0.55f);
            return path;
        }

        private static void PaintBlob(Graphics g, GraphicsPath path, Random rnd)
        {
            using (var brush = new SolidBrush(Color.FromArgb(250, 0x6D, 0x4F, 0x3B)))
                g.FillPath(brush, path);
            using (var pen = new Pen(Color.FromArgb(240, 0xB8, 0x10, 0x1C), 6.5f) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, path);

            var bounds = path.GetBounds();
            for (int i = 0; i < 3; i++)
            {
                float x = bounds.X + (float)rnd.NextDouble() * bounds.Width;
                float y = bounds.Y + (float)rnd.NextDouble() * bounds.Height;
                float len = (float)(rnd.NextDouble() * 40 + 14);
                using (var pen = new Pen(Color.FromArgb(rnd.Next(80, 170), 0x9E, 0x10, 0x18), (float)(rnd.NextDouble() * 2.4 + 1)))
                    g.DrawArc(pen, x, y, len, len * 0.75f, rnd.Next(0, 360), rnd.Next(70, 210));
            }
        }

        public static void RuneLeaf(Graphics g, PointF c, float size, float angle, float alpha)
        {
            var state = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(angle);
            float w = Math.Max(1.4f, size * 0.045f);
            using (var pen = new Pen(Color.FromArgb((int)(alpha * 110), 0x74, 0x74, 0x6C), w))
            {
                g.DrawEllipse(pen, -size * 0.5f, -size * 0.3f, size, size * 0.6f);
                g.DrawEllipse(pen, -size * 0.3f, -size * 0.3f, size * 0.6f, size * 0.6f);
                g.DrawLine(pen, -size * 0.5f, 0, size * 0.5f, 0);
            }
            g.Restore(state);
        }

        // ------------------------------------------------------------------ 棋盘

        public static Bitmap Board(int w, int h, int seed, bool opponent)
        {
            string key = "board" + w + "x" + h + (opponent ? "o" : "p");
            if (Cache.TryGetValue(key, out Bitmap cached)) return cached;

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rnd = new Random(seed);
                var rect = new RectangleF(3, 3, w - 6, h - 6);
                float radius = Math.Min(w, h) * 0.045f;

                using (var path = Theme.RoundRect(rect, radius))
                {
                    using (var brush = new LinearGradientBrush(rect, Color.FromArgb(0xAC, 0x96, 0x78), Color.FromArgb(0x8B, 0x76, 0x5A), 90f))
                        g.FillPath(brush, path);

                    var clip = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    for (int i = 0; i < 700; i++)
                    {
                        float x = rnd.Next(w), y = rnd.Next(h);
                        float r = rnd.Next(5, 46);
                        int a = rnd.Next(6, 22);
                        bool dark = rnd.Next(2) == 0;
                        Color blot = dark ? Color.FromArgb(0x4A, 0x38, 0x26) : Color.FromArgb(0xE2, 0xCF, 0xA8);
                        using (var b = new SolidBrush(Color.FromArgb(a, blot)))
                            g.FillEllipse(b, x, y, r, r * 0.75f);
                    }
                    for (int i = 0; i < 26; i++)
                    {
                        float x = rnd.Next(w), y = rnd.Next(h);
                        using (var pen = new Pen(Color.FromArgb(rnd.Next(8, 22), 0x4E, 0x3C, 0x28), rnd.Next(1, 3)))
                            g.DrawLine(pen, x, y, x + rnd.Next(-40, 40), y + rnd.Next(-16, 16));
                    }
                    g.Clip = clip;

                    using (var pen = new Pen(Color.FromArgb(190, 0x53, 0x42, 0x30), 3.4f) { LineJoin = LineJoin.Round })
                        g.DrawPath(pen, path);
                    using (var pen = new Pen(Color.FromArgb(60, 0xFF, 0xF0, 0xD2), 2f))
                    {
                        var inner = Theme.RoundRect(new RectangleF(rect.X + 5, rect.Y + 5, rect.Width - 10, rect.Height - 10), radius * 0.85f);
                        g.DrawPath(pen, inner);
                        inner.Dispose();
                    }
                }

                for (int c = 0; c < 3; c++)
                {
                    for (int r = 0; r < 3; r++)
                    {
                        RectangleF cell = CellRect(rect, c, r);
                        using (var path = Theme.RoundRect(cell, cell.Width * 0.06f))
                        {
                            using (var brush = new LinearGradientBrush(cell, Color.FromArgb(0xFF, 0x16, 0x12, 0x0D), Color.FromArgb(0xFF, 0x0C, 0x0A, 0x08), 90f))
                                g.FillPath(brush, path);
                            using (var pen = new Pen(Color.FromArgb(160, 0x00, 0x00, 0x00), 2f))
                                g.DrawPath(pen, path);
                            using (var pen = new Pen(Color.FromArgb(26, 0xFF, 0xF0, 0xCC), 1.4f))
                            {
                                var inner = Theme.RoundRect(new RectangleF(cell.X + 3, cell.Y + 3, cell.Width - 6, cell.Height - 6), cell.Width * 0.04f);
                                g.DrawPath(pen, inner);
                                inner.Dispose();
                            }
                        }
                    }
                }
            }

            Cache[key] = bmp;
            return bmp;
        }

        public static RectangleF CellRect(RectangleF board, int column, int row)
        {
            float padX = board.Width * 0.038f;
            float padY = board.Height * 0.045f;
            float gapX = board.Width * 0.021f;
            float gapY = board.Height * 0.026f;
            float cw = (board.Width - padX * 2 - gapX * 2) / 3f;
            float ch = (board.Height - padY * 2 - gapY * 2) / 3f;
            return new RectangleF(
                board.X + padX + column * (cw + gapX),
                board.Y + padY + row * (ch + gapY),
                cw, ch);
        }

        // ------------------------------------------------------------------ 骰盆

        public static Bitmap Tray(int w, int h)
        {
            string key = "tray" + w + "x" + h;
            if (Cache.TryGetValue(key, out Bitmap cached)) return cached;

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rnd = new Random(4242);
                var rect = new RectangleF(3, 3, w - 6, h - 6);
                float radius = Math.Min(w, h) * 0.05f;

                using (var path = Theme.RoundRect(rect, radius))
                {
                    using (var brush = new LinearGradientBrush(rect, Color.FromArgb(0x46, 0x4E, 0x3C), Color.FromArgb(0x20, 0x25, 0x1B), 90f))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(235, 0x0A, 0x0C, 0x08), 3f))
                        g.DrawPath(pen, path);
                }

                var inner = new RectangleF(rect.X + w * 0.055f, rect.Y + h * 0.11f, rect.Width - w * 0.11f, rect.Height - h * 0.22f);

                // 内嵌黑面
                using (var path = Theme.RoundRect(inner, 4))
                {
                    using (var brush = new LinearGradientBrush(inner, Color.FromArgb(0xFF, 0x11, 0x15, 0x0E), Color.FromArgb(0xFF, 0x0A, 0x0D, 0x08), 90f))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(120, 0x00, 0x00, 0x00), 2f))
                        g.DrawPath(pen, path);
                }

                // 刻蚀边线
                using (var pen = new Pen(Color.FromArgb(185, 0x93, 0xA0, 0x80), 2f))
                    g.DrawRectangle(pen, inner.X, inner.Y, inner.Width, inner.Height);
                using (var pen = new Pen(Color.FromArgb(120, 0x65, 0x71, 0x58), 1.4f))
                    g.DrawRectangle(pen, inner.X + 5, inner.Y + 5, inner.Width - 10, inner.Height - 10);

                float cx = rect.X + rect.Width * 0.5f, cy = rect.Y + rect.Height * 0.52f;
                float rw = inner.Width * 0.30f, rh = inner.Height * 0.26f;

                // 符文菱形
                using (var pen = new Pen(Color.FromArgb(120, 0xA8, 0xB6, 0x94), 2f))
                {
                    g.DrawPolygon(pen, new[]
                    {
                        new PointF(cx, cy - rh), new PointF(cx + rw, cy), new PointF(cx, cy + rh), new PointF(cx - rw, cy)
                    });
                    g.DrawPolygon(pen, new[]
                    {
                        new PointF(cx, cy - rh * 0.5f), new PointF(cx + rw * 0.5f, cy), new PointF(cx, cy + rh * 0.5f), new PointF(cx - rw * 0.5f, cy)
                    });
                    g.DrawLine(pen, cx - rw * 0.7f, cy - rh * 0.55f, cx + rw * 0.7f, cy + rh * 0.55f);
                    g.DrawLine(pen, cx + rw * 0.7f, cy - rh * 0.55f, cx - rw * 0.7f, cy + rh * 0.55f);
                }

                using (var brush = new SolidBrush(Color.FromArgb(130, 0xCF, 0xDA, 0xB6)))
                {
                    g.FillEllipse(brush, cx - 5, cy - rh * 0.32f, 10, 10);
                    g.FillEllipse(brush, cx + rw * 0.40f, cy + 1, 9, 9);
                    g.FillEllipse(brush, cx - rw * 0.34f, cy + rh * 0.20f, 8, 8);
                    g.FillEllipse(brush, cx + rw * 0.08f, cy - rh * 0.12f, 7, 7);
                    g.FillEllipse(brush, cx - rw * 0.52f, cy - rh * 0.10f, 7, 7);
                }

                // 四角 X
                using (var pen = new Pen(Color.FromArgb(130, 0x93, 0x9F, 0x80), 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float s = 9;
                    foreach (var p in new[]
                    {
                        new PointF(inner.X, inner.Y), new PointF(inner.Right, inner.Y),
                        new PointF(inner.X, inner.Bottom), new PointF(inner.Right, inner.Bottom)
                    })
                    {
                        g.DrawLine(pen, p.X - s, p.Y - s, p.X + s, p.Y + s);
                        g.DrawLine(pen, p.X - s, p.Y + s, p.X + s, p.Y - s);
                    }
                }

                // 上下卷草
                using (var pen = new Pen(Color.FromArgb(95, 0x93, 0x9F, 0x80), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float x = inner.X + inner.Width * (0.22f + i * 0.28f);
                        g.DrawArc(pen, x - 13, inner.Y - 9, 26, 18, 195, 150);
                        g.DrawArc(pen, x - 13, inner.Bottom - 9, 26, 18, 15, 150);
                    }
                }

                for (int i = 0; i < 6; i++)
                {
                    float x = rnd.Next((int)inner.X + 10, (int)inner.Right - 10);
                    float y = rnd.Next((int)inner.Y + 10, (int)inner.Bottom - 10);
                    using (var pen = new Pen(Color.FromArgb(45, 0, 0, 0), 1.4f))
                        g.DrawLine(pen, x, y, x + rnd.Next(-18, 18), y + rnd.Next(-10, 10));
                }
            }

            Cache[key] = bmp;
            return bmp;
        }

        // ------------------------------------------------------------------ 骰子

        /// <summary>骰子贴图：先按 256px 画母版（细节比例正确），再一次性高质量缩放到目标尺寸并缓存。</summary>
        public static Bitmap Die(int value, DieTint tint, int size = 256)
        {
            size = Math.Max(8, size);
            string key = "die" + value + tint + "@" + size;
            if (Cache.TryGetValue(key, out Bitmap cached)) return cached;

            if (size != 256)
            {
                Bitmap master = Die(value, tint, 256);
                var scaled = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                using (var sg = Graphics.FromImage(scaled))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    sg.DrawImage(master, new Rectangle(0, 0, size, size));
                }
                Cache[key] = scaled;
                return scaled;
            }

            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Color top, bottom, pip;
                switch (tint)
                {
                    case DieTint.Pair:
                        top = Color.FromArgb(0xEF, 0xE0, 0x83); bottom = Color.FromArgb(0xCF, 0xBB, 0x59); pip = Color.FromArgb(0x39, 0x2F, 0x14);
                        break;
                    case DieTint.Triple:
                        top = Color.FromArgb(0xAF, 0xD6, 0xEA); bottom = Color.FromArgb(0x84, 0xB4, 0xCF); pip = Color.FromArgb(0x1B, 0x2C, 0x39);
                        break;
                    case DieTint.Destroyed:
                        top = Color.FromArgb(0xE9, 0xA9, 0x9D); bottom = Color.FromArgb(0xC7, 0x7F, 0x73); pip = Color.FromArgb(0x3A, 0x14, 0x12);
                        break;
                    case DieTint.Ghost:
                        top = Color.FromArgb(0xF3, 0xEA, 0xD0); bottom = Color.FromArgb(0xCF, 0xC2, 0xA2); pip = Color.FromArgb(0x3A, 0x31, 0x22);
                        break;
                    default:
                        top = Color.FromArgb(0xF7, 0xEF, 0xD6); bottom = Color.FromArgb(0xDA, 0xCD, 0xAA); pip = Color.FromArgb(0x2B, 0x22, 0x19);
                        break;
                }

                var rect = new RectangleF(16, 16, size - 32, size - 32);
                using (var path = WobblyRect(rect, size * 0.15f, 1.1f, value * 31 + (int)tint * 7 + 5))
                {
                    using (var brush = new LinearGradientBrush(rect, top, bottom, 90f))
                        g.FillPath(brush, path);

                    var old = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 9f))
                        g.DrawArc(pen, rect.X + 12, rect.Y + 10, rect.Width - 24, rect.Height * 0.55f, 195, 150);
                    using (var sh = new LinearGradientBrush(
                        new RectangleF(rect.X, rect.Bottom - rect.Height * 0.24f, rect.Width, rect.Height * 0.24f),
                        Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 0, 0, 0), 90f))
                    {
                        var r2 = new RectangleF(rect.X - 30, rect.Bottom - rect.Height * 0.24f, rect.Width + 60, rect.Height * 0.3f);
                        g.FillRectangle(sh, r2);
                    }
                    g.Clip = old;

                    using (var pen = new Pen(Color.FromArgb(165, 0x3D, 0x30, 0x1F), 4.6f) { LineJoin = LineJoin.Round })
                        g.DrawPath(pen, path);
                }

                foreach (var p in PipLayout(value))
                {
                    float px = rect.X + p.X * rect.Width;
                    float py = rect.Y + p.Y * rect.Height;
                    float pr = size * 0.072f;
                    using (var shadow = new SolidBrush(Color.FromArgb(46, 0, 0, 0)))
                        g.FillEllipse(shadow, px - pr, py - pr + 3, pr * 2, pr * 2);
                    using (var brush = new SolidBrush(pip))
                        g.FillEllipse(brush, px - pr, py - pr, pr * 2, pr * 2);
                    using (var hi = new SolidBrush(Color.FromArgb(85, 255, 255, 255)))
                        g.FillEllipse(hi, px - pr * 0.42f, py - pr * 0.62f, pr * 0.62f, pr * 0.5f);
                }

                // 符文刻痕
                using (var pen = new Pen(Color.FromArgb(58, 0x8A, 0x74, 0x50), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float s = size * 0.14f;
                    g.DrawArc(pen, size * 0.17f, size * 0.17f, s, s, 30, 240);
                    g.DrawArc(pen, size * 0.69f, size * 0.65f, s * 1.2f, s, 200, 210);
                    g.DrawLine(pen, size * 0.19f, size * 0.73f, size * 0.27f, size * 0.79f);
                }
            }

            Cache[key] = bmp;
            return bmp;
        }

        private static PointF[] PipLayout(int value)
        {
            switch (value)
            {
                case 1: return new[] { new PointF(0.5f, 0.5f) };
                case 2: return new[] { new PointF(0.31f, 0.31f), new PointF(0.69f, 0.69f) };
                case 3: return new[] { new PointF(0.28f, 0.28f), new PointF(0.5f, 0.5f), new PointF(0.72f, 0.72f) };
                case 4: return new[] { new PointF(0.31f, 0.31f), new PointF(0.69f, 0.31f), new PointF(0.31f, 0.69f), new PointF(0.69f, 0.69f) };
                case 5: return new[] { new PointF(0.30f, 0.30f), new PointF(0.70f, 0.30f), new PointF(0.5f, 0.5f), new PointF(0.30f, 0.70f), new PointF(0.70f, 0.70f) };
                default: return new[]
                {
                    new PointF(0.30f, 0.25f), new PointF(0.30f, 0.5f), new PointF(0.30f, 0.75f),
                    new PointF(0.70f, 0.25f), new PointF(0.70f, 0.5f), new PointF(0.70f, 0.75f)
                };
            }
        }

        private static Bitmap ShadowSprite(int w, int h)
        {
            string key = "dieshadow" + w + "x" + h;
            if (Cache.TryGetValue(key, out Bitmap cached)) return cached;

            var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = Theme.RoundRect(new RectangleF(0, 0, w, h), w * 0.2f))
                using (var brush = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
                    g.FillPath(brush, path);
            }
            Cache[key] = bmp;
            return bmp;
        }

        public static void DrawDie(Graphics g, RectangleF rect, int value, DieTint tint, float alpha, float angle, bool shadow = true)
        {
            int size = Math.Max(8, (int)Math.Round(rect.Width * Theme.DeviceScale));
            Bitmap bmp = Die(value, tint, size);
            PointF c = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);

            if (shadow && alpha > 0.5f)
            {
                int shW = Math.Max(4, (int)Math.Round(size * 0.84f));
                int shH = Math.Max(3, (int)Math.Round(size * 0.78f));
                Bitmap sh = ShadowSprite(shW, shH);
                Blit.Image(g, sh, new RectangleF(rect.X + rect.Width * 0.08f, rect.Y + rect.Height * 0.13f,
                    rect.Width * 0.84f, rect.Height * 0.78f));
            }

            bool flat = Math.Abs(angle) < 0.01f;
            var state = g.Save();
            if (!flat)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TranslateTransform(c.X, c.Y);
                g.RotateTransform(angle);
            }

            if (alpha < 0.999f)
            {
                var dest = flat
                    ? new Rectangle((int)rect.X, (int)rect.Y, size, size)
                    : new Rectangle(-size / 2, -size / 2, size, size);
                using (var attrs = new ImageAttributes())
                {
                    attrs.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Max(0.02f, alpha) },
                        ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
                }
            }
            else
            {
                if (flat) Blit.Image(g, bmp, rect);
                else g.DrawImage(bmp, new RectangleF(-rect.Width / 2f, -rect.Height / 2f, rect.Width, rect.Height));
            }

            g.Restore(state);
        }

        // ------------------------------------------------------------------ 缎带

        public static void Ribbon(Graphics g, RectangleF r, float hover, float press)
        {
            float dy = press * 3f;
            r = new RectangleF(r.X, r.Y + dy, r.Width, r.Height);
            float notch = r.Height * 0.34f;

            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[]
                {
                    new PointF(r.X + notch, r.Y),
                    new PointF(r.Right - notch, r.Y),
                    new PointF(r.Right, r.Y + r.Height / 2f),
                    new PointF(r.Right - notch, r.Bottom),
                    new PointF(r.X + notch, r.Bottom),
                    new PointF(r.X, r.Y + r.Height / 2f)
                });

                using (var shadow = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
                {
                    var s = g.Save();
                    g.TranslateTransform(0, 7);
                    g.FillPath(shadow, path);
                    g.Restore(s);
                }

                Color top = Theme.Lerp(Color.FromArgb(0xDC, 0x17, 0x1E), Color.FromArgb(0xF5, 0x35, 0x3A), hover);
                Color bottom = Theme.Lerp(Color.FromArgb(0x96, 0x0B, 0x12), Color.FromArgb(0xB5, 0x12, 0x19), hover);
                using (var brush = new LinearGradientBrush(r, top, bottom, 90f))
                    g.FillPath(brush, path);

                using (var pen = new Pen(Color.FromArgb(210, 0x46, 0x04, 0x09), 3f) { LineJoin = LineJoin.Round })
                    g.DrawPath(pen, path);

                using (var pen = new Pen(Color.FromArgb(75, 255, 255, 255), 2.2f))
                    g.DrawLine(pen, r.X + notch + 10, r.Y + 5.5f, r.Right - notch - 10, r.Y + 5.5f);

                using (var brush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                    g.FillRectangle(brush, r.X + notch * 0.5f, r.Bottom - r.Height * 0.18f, r.Width - notch, r.Height * 0.18f);

                if (hover > 0.01f)
                {
                    using (var glow = new Pen(Color.FromArgb((int)(80 * hover), 0xFF, 0x8A, 0x80), 4f))
                        g.DrawPath(glow, path);
                }
            }
        }

        // ------------------------------------------------------------------ 花纹

        public static void Flourish(Graphics g, PointF c, float scale, bool flip, Color color, float alpha)
        {
            var state = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.ScaleTransform(flip ? -scale : scale, scale);
            Color ink = Color.FromArgb((int)(alpha * 255), color);
            using (var pen = new Pen(ink, 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                // 长尾
                using (var p = new GraphicsPath())
                {
                    p.AddBezier(-2, 4, -22, 0, -38, -12, -52, -26);
                    g.DrawPath(pen, p);
                }

                // 螺旋（向内收）
                var pts = new List<PointF>();
                int n = 46;
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    double a = -0.4 + t * Math.PI * 3.4;
                    float r = 26f * (1f - 0.88f * t);
                    pts.Add(new PointF((float)(-16 + Math.Cos(a) * r), (float)(-18 + Math.Sin(a) * r)));
                }
                g.DrawCurve(pen, pts.ToArray(), 0.5f);

                // 叶片
                using (var p = new GraphicsPath())
                {
                    p.AddBezier(4, 6, 16, 4, 26, -6, 34, -20);
                    p.AddBezier(34, -20, 22, -12, 12, -2, 4, 6);
                    g.DrawPath(pen, p);
                }
                g.DrawLine(pen, 18, -8, 24, -16);
            }
            g.Restore(state);
        }

        public static void BoneMark(Graphics g, PointF c, float s, Color color, float alpha)
        {
            Color ink = Color.FromArgb((int)(alpha * 255), color);
            using (var brush = new SolidBrush(ink))
            {
                // 四瓣 + 中心，合并成骨节 / 三叶草的形状
                float d = s * 0.34f;
                float r = s * 0.30f;
                var lobes = new[]
                {
                    new PointF(c.X, c.Y - d),
                    new PointF(c.X + d, c.Y),
                    new PointF(c.X, c.Y + d),
                    new PointF(c.X - d, c.Y)
                };
                foreach (var p in lobes) g.FillEllipse(brush, p.X - r, p.Y - r, r * 2, r * 2);
                g.FillEllipse(brush, c.X - r * 1.05f, c.Y - r * 1.05f, r * 2.1f, r * 2.1f);
            }
        }

        // ------------------------------------------------------------------ 头像

        public static void Sheep(Graphics g, RectangleF r)
        {
            float s = Math.Min(r.Width, r.Height);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;

            using (var brush = new SolidBrush(Color.FromArgb(0xF4, 0xED, 0xDA)))
            {
                var puffs = new[]
                {
                    new PointF(cx - 0.28f * s, cy - 0.18f * s), new PointF(cx + 0.28f * s, cy - 0.18f * s),
                    new PointF(cx - 0.34f * s, cy + 0.10f * s), new PointF(cx + 0.34f * s, cy + 0.10f * s),
                    new PointF(cx - 0.20f * s, cy + 0.30f * s), new PointF(cx + 0.20f * s, cy + 0.30f * s),
                    new PointF(cx, cy - 0.32f * s)
                };
                foreach (var p in puffs)
                    g.FillEllipse(brush, p.X - 0.205f * s, p.Y - 0.205f * s, 0.41f * s, 0.41f * s);
            }

            using (var pen = new Pen(Color.FromArgb(0xD9, 0xB8, 0x84), 0.075f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, cx - 0.42f * s, cy - 0.44f * s, 0.30f * s, 0.28f * s, 140, 200);
                g.DrawArc(pen, cx + 0.12f * s, cy - 0.44f * s, 0.30f * s, 0.28f * s, 200, 200);
            }

            var face = new RectangleF(cx - 0.23f * s, cy - 0.20f * s, 0.46f * s, 0.44f * s);
            using (var brush = new SolidBrush(Color.FromArgb(0x3A, 0x39, 0x37)))
                g.FillEllipse(brush, face);

            using (var pen = new Pen(Color.FromArgb(0xF2, 0xEC, 0xDC), 0.05f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, face.X + 0.06f * s, face.Y + 0.13f * s, 0.12f * s, 0.09f * s, 200, 140);
                g.DrawArc(pen, face.X + 0.26f * s, face.Y + 0.13f * s, 0.12f * s, 0.09f * s, 200, 140);
            }

            Dot(g, cx, face.Y + 0.29f * s, 0.035f * s, Color.FromArgb(0x1A, 0x18, 0x16));
            Dot(g, face.X + 0.08f * s, face.Y + 0.31f * s, 0.035f * s, Color.FromArgb(160, 0xC1, 0x5A, 0x50));
            Dot(g, face.Right - 0.08f * s, face.Y + 0.31f * s, 0.035f * s, Color.FromArgb(160, 0xC1, 0x5A, 0x50));

            Dot(g, cx, face.Y + 0.05f * s, 0.03f * s, Color.FromArgb(0xC1, 0x12, 0x1F));
        }

        public static void Portrait(Graphics g, RectangleF r, int kind)
        {
            float s = Math.Min(r.Width, r.Height);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;

            if (kind == 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(0x6E, 0x7A, 0x84)))
                {
                    g.FillEllipse(brush, cx - 0.20f * s, cy - 0.10f * s, 0.40f * s, 0.42f * s);
                    g.FillEllipse(brush, cx - 0.33f * s, cy - 0.32f * s, 0.26f * s, 0.26f * s);
                    g.FillEllipse(brush, cx + 0.07f * s, cy - 0.32f * s, 0.26f * s, 0.26f * s);
                }
                using (var brush = new SolidBrush(Color.FromArgb(0x9A, 0xA6, 0xAE)))
                {
                    g.FillEllipse(brush, cx - 0.25f * s, cy - 0.25f * s, 0.15f * s, 0.15f * s);
                    g.FillEllipse(brush, cx + 0.10f * s, cy - 0.25f * s, 0.15f * s, 0.15f * s);
                }
                Dot(g, cx - 0.08f * s, cy + 0.02f * s, 0.032f * s, Color.FromArgb(0x22, 0x1E, 0x1A));
                Dot(g, cx + 0.08f * s, cy + 0.02f * s, 0.032f * s, Color.FromArgb(0x22, 0x1E, 0x1A));
                Dot(g, cx, cy + 0.14f * s, 0.028f * s, Color.FromArgb(0x6E, 0x3A, 0x38));
            }
            else if (kind == 1)
            {
                foreach (var off in new[] { -0.17f, 0.17f })
                {
                    using (var brush = new SolidBrush(Color.FromArgb(0x8F, 0xA8, 0x6B)))
                        g.FillEllipse(brush, cx + off * s - 0.15f * s, cy - 0.08f * s, 0.30f * s, 0.34f * s);
                    using (var brush = new SolidBrush(Color.FromArgb(0x2E, 0x3A, 0x22)))
                        g.FillEllipse(brush, cx + off * s - 0.19f * s, cy - 0.26f * s, 0.38f * s, 0.15f * s);
                    Dot(g, cx + off * s - 0.05f * s, cy + 0.02f * s, 0.024f * s, Color.FromArgb(0x1E, 0x1A, 0x16));
                    Dot(g, cx + off * s + 0.05f * s, cy + 0.02f * s, 0.024f * s, Color.FromArgb(0x1E, 0x1A, 0x16));
                }
            }
            else
            {
                using (var brush = new SolidBrush(Color.FromArgb(0x5F, 0x8C, 0x86)))
                    g.FillEllipse(brush, cx - 0.17f * s, cy - 0.04f * s, 0.34f * s, 0.40f * s);
                using (var brush = new SolidBrush(Color.FromArgb(0x2A, 0x2E, 0x28)))
                {
                    g.FillEllipse(brush, cx - 0.25f * s, cy - 0.38f * s, 0.50f * s, 0.20f * s);
                    g.FillRectangle(brush, cx - 0.15f * s, cy - 0.50f * s, 0.30f * s, 0.16f * s);
                }
                using (var pen = new Pen(Color.FromArgb(0x9B, 0x7A, 0xC8), 0.045f * s))
                    g.DrawLine(pen, cx - 0.20f * s, cy - 0.30f * s, cx + 0.20f * s, cy - 0.30f * s);
                using (var pen = new Pen(Color.FromArgb(0xE8, 0xE2, 0xD0), 0.018f * s))
                {
                    g.DrawEllipse(pen, cx - 0.13f * s, cy - 0.04f * s, 0.11f * s, 0.11f * s);
                    g.DrawEllipse(pen, cx + 0.02f * s, cy - 0.04f * s, 0.11f * s, 0.11f * s);
                }
                Dot(g, cx - 0.075f * s, cy + 0.015f * s, 0.022f * s, Color.FromArgb(0x1E, 0x1A, 0x16));
                Dot(g, cx + 0.075f * s, cy + 0.015f * s, 0.022f * s, Color.FromArgb(0x1E, 0x1A, 0x16));
                Dot(g, cx, cy + 0.16f * s, 0.05f * s, Color.FromArgb(0x1E, 0x1A, 0x16));
            }
        }
    }
}
