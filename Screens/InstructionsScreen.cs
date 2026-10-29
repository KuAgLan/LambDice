using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LambDice.Ui;

namespace LambDice.Screens
{
    public sealed class InstructionsScreen : Screen
    {
        private float t;

        private static readonly RectangleF Back = new RectangleF(600, 690, 400, 84);

        public InstructionsScreen(App app) : base(app) { }

        public override void OnEnter()
        {
            InitLeaves(7);
            t = 0;
        }

        public override void Update(float dt)
        {
            t += dt;
            UpdateLeaves(dt);
            foreach (var k in App.Pressed)
            {
                if (k == Keys.Escape || k == Keys.Enter || k == Keys.Space) { App.ShowTitle(); return; }
            }
        }

        public override void Render(Graphics g)
        {
            DrawBackground(g);

            // 标题
            Theme.TextOutlined(g, "玩法说明", Theme.Font(46f, true), Theme.Parchment, Color.FromArgb(70, 0, 0, 0),
                new RectangleF(0, 52, Theme.Width, 62), 4f);
            FlourishPair(g, Theme.Width / 2f, 84, 1.05f, Theme.CreamDim, 0.5f);

            Theme.Text(g, "计算分数的方式是加总所有骰子点数，同点数放在一列会成倍放大。", Theme.Font(20f),
                Color.FromArgb(220, 0xD8, 0xCF, 0xB8), new RectangleF(0, 126, Theme.Width, 30));

            // ---------------- 左：配对
            DrawPairDemo(g, new RectangleF(270, 200, 420, 330));
            Theme.Text(g, "配对股子", Theme.Font(30f, true), Theme.Blood,
                new RectangleF(270, 548, 420, 44));
            Theme.Text(g, "同点数的骰子放进同一列，得分按颗数的平方翻倍。", Theme.Font(17f),
                Color.FromArgb(210, 0xD8, 0xCF, 0xB8), new RectangleF(230, 594, 500, 30));

            // ---------------- 右：摧毁
            DrawDestroyDemo(g, new RectangleF(910, 200, 420, 330));
            Theme.Text(g, "摧毁对手的骰子", Theme.Font(30f, true), Theme.Blood,
                new RectangleF(910, 548, 420, 44));
            Theme.Text(g, "放下的点数与对手同列相同时，对手那些骰子会碎掉。", Theme.Font(17f),
                Color.FromArgb(210, 0xD8, 0xCF, 0xB8), new RectangleF(870, 594, 500, 30));

            // 中间小徽记
            Art.BoneMark(g, new PointF(Theme.Width / 2f, 360), 26f, Theme.Blood, 0.85f);

            RibbonButton(g, Back, "了解", 30f, () => App.ShowTitle());
            KeyHint(g, "Esc / 回车  返回", 800);
        }

        private void DrawPairDemo(Graphics g, RectangleF area)
        {
            float bob = (float)Math.Sin(t * 1.4f) * 3f;

            var board = new RectangleF(area.X + 72, area.Y + 20, 300, 300);
            DrawDisplayBoard(g, board, 2, 2);

            float cell = (board.Width - 28 - 12) / 2f;
            float inset = cell * 0.09f;
            var d1 = new RectangleF(board.X + 14 + inset, board.Y + 14 + inset + bob, cell - inset * 2, cell - inset * 2);
            var d2 = new RectangleF(board.X + 14 + inset, board.Y + 14 + cell + 12 + inset - bob, cell - inset * 2, cell - inset * 2);

            // 光晕
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(d1.X - 20, d1.Y - 20, d1.Width + 40, d1.Height + 40);
                using (var brush = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(90, 0xE8, 0xD2, 0x6A),
                    SurroundColors = new[] { Color.FromArgb(0, 0xE8, 0xD2, 0x6A) }
                })
                    g.FillPath(brush, path);
            }

            Art.DrawDie(g, d1, 6, DieTint.Pair, 1f, -4f);
            Art.DrawDie(g, d2, 6, DieTint.Pair, 1f, 3.5f);

            using (var pen = new Pen(Color.FromArgb(230, 0xC1, 0x12, 0x1F), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                Ray(g, pen, d1, -1, -1, 34f);
                Ray(g, pen, d1, 1, -1, 26f);
                Ray(g, pen, d2, -1, 1, 30f);
                Ray(g, pen, d2, 1, 1, 26f);
            }

        }

        private static void DrawDisplayBoard(Graphics g, RectangleF board, int cols, int rows)
        {
            using (var path = Theme.RoundRect(board, 6))
            {
                using (var brush = new SolidBrush(Color.FromArgb(235, 0x3F, 0x3E, 0x39)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0), 2.4f))
                    g.DrawPath(pen, path);
            }

            float pad = 14f;
            float gap = 12f;
            float cw = (board.Width - pad * 2 - gap * (cols - 1)) / cols;
            float ch = (board.Height - pad * 2 - gap * (rows - 1)) / rows;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var cell = new RectangleF(board.X + pad + c * (cw + gap), board.Y + pad + r * (ch + gap), cw, ch);
                    using (var path = Theme.RoundRect(cell, 3))
                    {
                        using (var brush = new SolidBrush(Color.FromArgb(255, 0x13, 0x10, 0x0C)))
                            g.FillPath(brush, path);
                        using (var pen = new Pen(Color.FromArgb(60, 0x8A, 0x84, 0x74), 1.4f))
                            g.DrawPath(pen, path);
                    }
                }
            }
        }

        private static void Ray(Graphics g, Pen pen, RectangleF die, int sx, int sy, float len)
        {
            float cx = die.X + die.Width / 2f + sx * die.Width * 0.44f;
            float cy = die.Y + die.Height / 2f + sy * die.Height * 0.44f;
            g.DrawLine(pen, cx, cy, cx + sx * len * 0.55f, cy + sy * len);
        }

        private void DrawDestroyDemo(Graphics g, RectangleF area)
        {
            float bob = (float)Math.Sin(t * 1.4f + 1f) * 2f;

            var board = new RectangleF(area.X + 110, area.Y + 20, 200, 300);
            DrawDisplayBoard(g, board, 1, 3);

            float cellW = board.Width - 28;
            float cellH = (board.Height - 28 - 24) / 3f;
            float inset = cellW * 0.085f;
            var d1 = new RectangleF(board.X + 14 + inset, board.Y + 14 + inset + bob, cellW - inset * 2, cellH - inset * 2);
            var d2 = new RectangleF(board.X + 14 + inset, board.Y + 14 + cellH + 12 + inset - bob, cellW - inset * 2, cellH - inset * 2);

            Art.DrawDie(g, d1, 6, DieTint.Destroyed, 1f, -3f);
            Art.DrawDie(g, d2, 6, DieTint.Destroyed, 1f, 2f);

            using (var pen = new Pen(Theme.Blood, 10f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (var d in new[] { d1, d2 })
                {
                    float m = d.Width * 0.24f;
                    g.DrawLine(pen, d.X + m, d.Y + m * 1.3f, d.Right - m, d.Bottom - m * 1.3f);
                    g.DrawLine(pen, d.Right - m, d.Y + m * 1.3f, d.X + m, d.Bottom - m * 1.3f);
                }
            }

            using (var pen = new Pen(Color.FromArgb(160, 0xC9, 0xBE, 0xA2), 5f))
            {
                DrawX(g, pen, board.X - 22, board.Y + board.Height / 2f, 15);
                DrawX(g, pen, board.Right + 22, board.Y + board.Height / 2f, 15);
            }
        }

        private static void DrawX(Graphics g, Pen pen, float x, float y, float s)
        {
            g.DrawLine(pen, x - s, y - s, x + s, y + s);
            g.DrawLine(pen, x - s, y + s, x + s, y - s);
        }
    }
}
