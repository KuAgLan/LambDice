using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LambDice.Game;
using LambDice.Ui;

namespace LambDice.Screens
{
    public sealed class OpponentScreen : Screen
    {
        private float t;
        private int selected;
        private float[] pop = new float[3];

        private static readonly float CardW = 370f, CardH = 430f, Gap = 105f, Top = 180f;

        public OpponentScreen(App app) : base(app) { }

        public override void OnEnter()
        {
            InitLeaves(8);
            t = 0;
            for (int i = 0; i < pop.Length; i++) pop[i] = 0;
        }

        private static RectangleF CardRect(int i)
        {
            float total = CardW * 3 + Gap * 2;
            float x0 = (Theme.Width - total) / 2f;
            return new RectangleF(x0 + i * (CardW + Gap), Top, CardW, CardH);
        }

        private static RectangleF StartButton => new RectangleF(Theme.Width / 2f - 190, 668, 380, 82);
        private static RectangleF BackButton => new RectangleF(Theme.Width / 2f - 120, 782, 240, 40);

        public override void Update(float dt)
        {
            t += dt;
            UpdateLeaves(dt);
            for (int i = 0; i < pop.Length; i++)
                pop[i] = Math.Max(0f, pop[i] - dt * 4f);

            for (int i = 0; i < OpponentInfo.All.Length; i++)
            {
                if (Click(CardRect(i))) { Select(i); }
            }

            foreach (var k in App.Pressed)
            {
                if (k == Keys.D1 || k == Keys.NumPad1) Select(0);
                else if (k == Keys.D2 || k == Keys.NumPad2) Select(1);
                else if (k == Keys.D3 || k == Keys.NumPad3) Select(2);
                else if (k == Keys.Enter || k == Keys.Space) { App.Chosen = OpponentInfo.All[selected]; App.StartMatch(); return; }
                else if (k == Keys.Escape) { App.ShowTitle(); return; }
            }
        }

        private void Select(int i)
        {
            selected = i;
            pop[i] = 1f;
            App.Chosen = OpponentInfo.All[i];
        }

        public override void Render(Graphics g)
        {
            DrawBackground(g);

            Theme.TextOutlined(g, "选择对手", Theme.Font(46f, true), Theme.Parchment, Color.FromArgb(70, 0, 0, 0),
                new RectangleF(0, 48, Theme.Width, 62), 4f);
            FlourishPair(g, Theme.Width / 2f, 80, 1.0f, Theme.CreamDim, 0.45f);

            for (int i = 0; i < OpponentInfo.All.Length; i++)
                DrawCard(g, i, CardRect(i));

            RibbonButton(g, StartButton, "开始对局", 30f,
                () => { App.Chosen = OpponentInfo.All[selected]; App.StartMatch(); });

            TextButton(g, BackButton, "返回", 22f, () => App.ShowTitle(), Color.FromArgb(150, 0xC9, 0xBE, 0xA2));

            KeyHint(g, "1 / 2 / 3  选择     回车 开始     Esc 返回", 838);
        }

        private void DrawCard(Graphics g, int index, RectangleF r)
        {
            var info = OpponentInfo.All[index];
            bool chosen = index == selected;
            float lift = chosen ? 10f + pop[index] * 4f : 0f;
            var rect = new RectangleF(r.X, r.Y - lift, r.Width, r.Height);

            // 光晕
            if (chosen)
            {
                using (var path = Theme.RoundRect(new RectangleF(rect.X - 8, rect.Y - 8, rect.Width + 16, rect.Height + 16), 18))
                using (var brush = new SolidBrush(Color.FromArgb(26, Theme.Gold)))
                    g.FillPath(brush, path);
            }

            Art.Shadow(g, rect, 16, chosen ? 130 : 90, 8);

            using (var path = Theme.RoundRect(rect, 14))
            {
                using (var brush = new LinearGradientBrush(rect,
                    Color.FromArgb(chosen ? 255 : 235, 0x24, 0x1F, 0x19),
                    Color.FromArgb(chosen ? 255 : 235, 0x14, 0x11, 0x0E), 90f))
                    g.FillPath(brush, path);

                using (var pen = new Pen(chosen ? Theme.Gold : Color.FromArgb(90, 0x6E, 0x5F, 0x48), chosen ? 3f : 1.6f))
                    g.DrawPath(pen, path);
            }

            // 头像
            var portrait = new RectangleF(rect.X + rect.Width / 2f - 78, rect.Y + 26, 156, 156);
            using (var path = new EllipsePath(portrait))
            {
                using (var brush = new SolidBrush(Color.FromArgb(60, info.Accent)))
                    g.FillPath(brush, path.Path);
            }
            Art.Portrait(g, new RectangleF(portrait.X + 14, portrait.Y + 16, portrait.Width - 28, portrait.Height - 30), info.Portrait);

            // 名字
            Theme.Text(g, info.Chinese, Theme.Font(32f, true), chosen ? Theme.Parchment : Theme.CreamDim,
                new RectangleF(rect.X, rect.Y + 196, rect.Width, 44));

            // 难度
            var chip = new RectangleF(rect.X + rect.Width / 2f - 56, rect.Y + 246, 112, 32);
            using (var path = Theme.RoundRect(chip, 16))
            {
                Color accent = info.Difficulty == Difficulty.Hard ? Theme.Blood
                    : info.Difficulty == Difficulty.Normal ? Color.FromArgb(0x8F, 0xA8, 0x6B) : Theme.Gold;
                using (var brush = new SolidBrush(Color.FromArgb(60, accent)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(180, accent), 1.6f))
                    g.DrawPath(pen, path);
                Theme.Text(g, info.DifficultyText, Theme.Font(15f, true), Color.FromArgb(230, accent),
                    chip, StringAlignment.Center, StringAlignment.Center);
            }

            // 战绩
            string record = Stats.Line(info.Name);
            if (!string.IsNullOrEmpty(record))
            {
                Theme.Text(g, record, Theme.Font(16f), Color.FromArgb(170, 0xC9, 0xBE, 0xA2),
                    new RectangleF(rect.X, rect.Y + 350, rect.Width, 24));
            }

            // 简介
            using (var brush = new SolidBrush(Color.FromArgb(chosen ? 225 : 185, 0xC9, 0xBE, 0xA2)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
            {
                var box = new RectangleF(rect.X + 30, rect.Y + 286, rect.Width - 60, 86);
                g.DrawString(info.Blurb, Theme.Font(17f), brush, box, format);
            }

            if (chosen)
            {
                Theme.Text(g, "◆", Theme.Font(16f), Theme.Gold, new RectangleF(rect.X, rect.Y + 12, rect.Width, 20));
            }
        }

        private struct EllipsePath : IDisposable
        {
            public GraphicsPath Path;
            public EllipsePath(RectangleF r) { Path = new GraphicsPath(); Path.AddEllipse(r); }
            public void Dispose() { Path?.Dispose(); }
        }
    }
}
