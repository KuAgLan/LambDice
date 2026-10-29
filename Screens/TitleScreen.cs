using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LambDice.Ui;

namespace LambDice.Screens
{
    public sealed class TitleScreen : Screen
    {
        private float t;

        // 从上到下：骰子徽记 → 标题 → 骨节 → 副标题 → 主按钮 → 分隔线 → 次要入口 → 退出
        private static readonly RectangleF Ribbon = new RectangleF(600, 506, 400, 84);
        private static readonly RectangleF OnlineButton = new RectangleF(400, 632, 240, 44);
        private static readonly RectangleF HelpButton = new RectangleF(680, 632, 240, 44);
        private static readonly RectangleF SettingsButton = new RectangleF(960, 632, 240, 44);
        private static readonly RectangleF QuitButton = new RectangleF(700, 706, 200, 34);

        private static Bitmap glow;
        private static float glowScale = -1f;

        private static Bitmap BuildGlow(float k)
        {
            int w = Math.Max(1, (int)Math.Round(1000 * k));
            int h = Math.Max(1, (int)Math.Round(640 * k));
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            glowScale = k;
            using (var bg = Graphics.FromImage(bmp))
            using (var path = new GraphicsPath())
            {
                bg.SmoothingMode = SmoothingMode.AntiAlias;
                bg.ScaleTransform(k, k);
                path.AddEllipse(0, 0, 1000, 640);
                using (var brush = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(48, 0x6A, 0x54, 0x3A),
                    SurroundColors = new[] { Color.FromArgb(0, 0x6A, 0x54, 0x3A) },
                    FocusScales = new PointF(0.35f, 0.35f)
                })
                    bg.FillPath(brush, path);
            }
            return bmp;
        }

        public TitleScreen(App app) : base(app) { }

        public override void OnEnter()
        {
            InitLeaves(9);
            t = 0;
        }

        public override void Update(float dt)
        {
            t += dt;
            UpdateLeaves(dt);

            foreach (var k in App.Pressed)
            {
                if (k == Keys.Enter || k == Keys.Space) { App.ShowOpponents(); return; }
                if (k == Keys.Escape) { App.Close(); return; }
            }
        }

        public override void Render(Graphics g)
        {
            DrawBackground(g);

            // 中央柔光：只渲染一次，之后直接贴图
            float gk = Math.Max(0.5f, Theme.DeviceScale);
            if (glow == null || Math.Abs(gk - glowScale) > 0.01f) { glow?.Dispose(); glow = BuildGlow(gk); }
            Blit.Image(g, glow, new RectangleF(300, 100, 1000, 640));

            // 三颗骰子 + 两侧卷花
            float bob = (float)Math.Sin(t * 1.6f) * 3f;
            for (int i = 0; i < 3; i++)
            {
                float size = 64;
                float x = Theme.Width / 2f + (i - 1) * 80f - size / 2f;
                float y = 138 + bob * (i == 1 ? 1f : 0.55f);
                Art.DrawDie(g, new RectangleF(x, y, size, size), 6, DieTint.Normal, 1f, (i - 1) * 2.5f);
            }
            Art.Flourish(g, new PointF(Theme.Width / 2f - 236, 174), 1.25f, false, Theme.CreamDim, 0.5f);
            Art.Flourish(g, new PointF(Theme.Width / 2f + 236, 174), 1.25f, true, Theme.CreamDim, 0.5f);

            // 标题
            Theme.TextOutlined(g, "距骨骰", Theme.Font(116f, true), Theme.Parchment, Color.FromArgb(80, 0, 0, 0),
                new RectangleF(0, 230, Theme.Width, 142), 6f);

            // 骨节徽记
            Art.BoneMark(g, new PointF(Theme.Width / 2f, 406), 33f, Theme.Parchment, 0.95f);

            // 副标题
            Theme.Text(g, "风险与奖励并存的骰子游戏", Theme.Font(26f), Color.FromArgb(232, 0xD8, 0xCF, 0xB8),
                new RectangleF(0, 444, Theme.Width, 38));

            // 主按钮
            RibbonButton(g, Ribbon, "开始游戏", 32f, () => App.ShowOpponents());

            // 分隔线
            using (var pen = new Pen(Color.FromArgb(46, Theme.Cream), 1.6f))
                g.DrawLine(pen, 470, 606, 1130, 606);
            Art.Dot(g, Theme.Width / 2f, 606.5f, 4.5f, Color.FromArgb(90, Theme.Cream));

            // 次要入口
            TextButton(g, OnlineButton, "联机对战", 25f, () => App.ShowLobby(), Color.FromArgb(210, 0xE0, 0xC0, 0x76));
            TextButton(g, HelpButton, "玩法说明", 25f, () => App.ShowHelp(), Color.FromArgb(175, 0xC9, 0xBE, 0xA2));
            TextButton(g, SettingsButton, "设置", 25f, () => App.ShowSettings(), Color.FromArgb(175, 0xC9, 0xBE, 0xA2));

            // 退出
            TextButton(g, QuitButton, "结束游戏", 21f, () => App.Close(), Color.FromArgb(150, 0xC9, 0xBE, 0xA2));

            // 断线等提示
            if (!string.IsNullOrEmpty(App.Notice) && App.NoticeAge < 4f)
            {
                float a = Math.Min(1f, (4f - App.NoticeAge) / 0.8f);
                Theme.Text(g, App.Notice, Theme.Font(20f), Color.FromArgb((int)(a * 235), 0xE0, 0x8A, 0x72),
                    new RectangleF(0, 470, Theme.Width, 30));
            }

            // 底部
            string total = Stats.Total();
            if (!string.IsNullOrEmpty(total))
                Theme.Text(g, total, Theme.Font(15f), Color.FromArgb(120, 0xC9, 0xBE, 0xA2),
                    new RectangleF(0, 784, Theme.Width, 22));

            KeyHint(g, "回车 / 空格  开始      鼠标点击  选择      Esc  退出", 836);
        }
    }
}