using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LambDice.Ui;

namespace LambDice.Screens
{
    public sealed class SettingsScreen : Screen
    {
        private static readonly RectangleF Panel = new RectangleF(400, 100, 800, 620);
        private static readonly RectangleF NameField = new RectangleF(760, 148, 330, 44);
        private static readonly RectangleF VolumeTrack = new RectangleF(470, 272, 600, 16);
        private static readonly RectangleF MuteToggle = new RectangleF(1016, 328, 100, 44);
        private static readonly RectangleF FullToggle = new RectangleF(1016, 390, 100, 44);
        private static readonly RectangleF BackButton = new RectangleF(680, 768, 240, 44);

        private static readonly RectangleF ComboButton = new RectangleF(470, 516, 320, 46);
        private static readonly RectangleF ComboList = new RectangleF(470, 566, 320, 44 * 3);

        private readonly TextField nameField = new TextField("", "玩家") { MaxLength = 8 };


        private static readonly int[,] Resolutions =
        {
            { 1280, 720 },
            { 1600, 900 },
            { 1920, 1080 }
        };

        private float t;
        private bool dragging;
        private bool comboOpen;

        public SettingsScreen(App app) : base(app) { }

        public override void OnEnter()
        {
            InitLeaves(7);
            t = 0;
            dragging = false;
            comboOpen = false;
            nameField.Text = Config.GetString("player.name", "玩家");
        }

        private static float Volume
        {
            get { return Ui.Sfx.Volume / 100f; }
            set
            {
                int v = (int)Math.Round(Math.Max(0f, Math.Min(1f, value)) * 100f);
                Ui.Sfx.Volume = v;
                Config.SetInt("volume", v);
            }
        }

        private static RectangleF ComboItem(int index)
        {
            return new RectangleF(ComboList.X, ComboList.Y + index * 44, ComboList.Width, 44);
        }

        private string CurrentLabel()
        {
            Size size = App.ClientSize;
            for (int i = 0; i < Resolutions.GetLength(0); i++)
                if (size.Width == Resolutions[i, 0] && size.Height == Resolutions[i, 1])
                    return Resolutions[i, 0] + " × " + Resolutions[i, 1];
            return size.Width + " × " + size.Height;
        }

        public override void Update(float dt)
        {
            t += dt;
            UpdateLeaves(dt);

            // 下拉展开时，只处理列表本身
            if (comboOpen)
            {
                for (int i = 0; i < Resolutions.GetLength(0); i++)
                {
                    if (Click(ComboItem(i)))
                    {
                        comboOpen = false;
                        Ui.Sfx.Play("click");
                        App.SetClientResolution(Resolutions[i, 0], Resolutions[i, 1]);
                        return;
                    }
                }

                bool inside = ComboList.Contains(App.Mouse) || ComboButton.Contains(App.Mouse);
                if (App.MouseClicked && !inside) comboOpen = false;
                foreach (var k in App.Pressed)
                    if (k == Keys.Escape) comboOpen = false;
                return;
            }

            if (nameField.Update(App, NameField))
            {
                string name = string.IsNullOrEmpty(nameField.Text.Trim()) ? "玩家" : nameField.Text.Trim();
                Config.SetString("player.name", name);
                Ui.Sfx.Play("click");
            }

            var hitZone = new RectangleF(VolumeTrack.X - 14, VolumeTrack.Y - 16, VolumeTrack.Width + 28, VolumeTrack.Height + 32);
            if (App.MouseClicked && hitZone.Contains(App.Mouse)) dragging = true;
            if (!App.MouseHeld) dragging = false;
            if (dragging) Volume = (App.Mouse.X - VolumeTrack.X) / VolumeTrack.Width;

            if (Click(ComboButton))
            {
                comboOpen = true;
                Ui.Sfx.Play("click");
            }

            if (Click(MuteToggle))
            {
                Ui.Sfx.Muted = !Ui.Sfx.Muted;
                Config.SetBool("muted", Ui.Sfx.Muted);
                if (!Ui.Sfx.Muted) Ui.Sfx.Play("click");
            }

            if (Click(FullToggle))
            {
                App.ToggleFullscreen();
                Ui.Sfx.Play("click");
            }

            if (Click(BackButton)) { Config.Save(); App.ShowTitle(); return; }
            foreach (var k in App.Pressed)
                if (k == Keys.Escape) { Config.Save(); App.ShowTitle(); return; }
        }

        public override void Render(Graphics g)
        {
            DrawBackground(g);

            Theme.TextOutlined(g, "设 置", Theme.Font(46f, true), Theme.Parchment, Color.FromArgb(70, 0, 0, 0),
                new RectangleF(0, 46, Theme.Width, 62), 4f);

            Art.Shadow(g, Panel, 16, 110, 8);
            using (var path = Theme.RoundRect(Panel, 16))
            {
                using (var brush = new LinearGradientBrush(Panel, Color.FromArgb(240, 0x25, 0x20, 0x1A), Color.FromArgb(240, 0x15, 0x12, 0x0E), 90f))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(90, Theme.Gold), 1.8f))
                    g.DrawPath(pen, path);
            }

            // 我的名字
            Theme.Text(g, "我的名字", Theme.Font(26f, true), Theme.Parchment,
                new RectangleF(Panel.X + 70, Panel.Y + 56, 300, 36), StringAlignment.Near);
            nameField.Draw(g, NameField);

            // 音量
            Theme.Text(g, "音效音量", Theme.Font(26f, true), Theme.Parchment,
                new RectangleF(Panel.X + 70, Panel.Y + 116, 300, 36), StringAlignment.Near);
            Theme.Text(g, Ui.Sfx.Volume + "%", Theme.Font(26f, true), Theme.Gold,
                new RectangleF(Panel.Right - 220, Panel.Y + 116, 150, 36), StringAlignment.Far);
            DrawSlider(g);

            // 分辨率（下拉）
            Theme.Text(g, "分辨率", Theme.Font(26f, true), Theme.Parchment,
                new RectangleF(Panel.X + 70, Panel.Y + 380, 300, 36), StringAlignment.Near);
            DrawCombo(g);

            // 静音 / 全屏
            Theme.Text(g, "静音", Theme.Font(26f, true), Theme.Parchment,
                new RectangleF(Panel.X + 70, Panel.Y + 228, 300, 44), StringAlignment.Near);
            DrawToggle(g, MuteToggle, Ui.Sfx.Muted);

            Theme.Text(g, "全屏", Theme.Font(26f, true), Theme.Parchment,
                new RectangleF(Panel.X + 70, Panel.Y + 290, 300, 44), StringAlignment.Near);
            DrawToggle(g, FullToggle, App.Fullscreen);

            Theme.Text(g, "F11 / Alt+Enter 也可切全屏", Theme.Font(14f),
                Color.FromArgb(150, 0xC9, 0xBE, 0xA2),
                new RectangleF(Panel.X + 70, Panel.Y + 344, 520, 24), StringAlignment.Near);

            string total = Stats.Total();
            if (!string.IsNullOrEmpty(total))
                Theme.Text(g, total, Theme.Font(18f), Color.FromArgb(170, 0xC9, 0xBE, 0xA2),
                    new RectangleF(0, 736, Theme.Width, 26));

            TextButton(g, BackButton, "返回", 24f, () => { Config.Save(); App.ShowTitle(); }, Color.FromArgb(160, 0xC9, 0xBE, 0xA2));
            KeyHint(g, "输入名字后回车保存      拖动滑杆调音量      点分辨率下拉选择      Esc 返回", 848);

            if (comboOpen) DrawComboList(g);
        }

        // ------------------------------------------------------------------ 控件

        private void DrawCombo(Graphics g)
        {
            bool hover = Hover(ComboButton);
            bool open = comboOpen;

            using (var path = Theme.RoundRect(ComboButton, 10))
            {
                using (var brush = new SolidBrush(Color.FromArgb(255, open ? 0x2E : 0x20, open ? 0x26 : 0x1A, 0x1E)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(open ? 235 : hover ? 200 : 120, Theme.Gold), open ? 2.2f : 1.6f))
                    g.DrawPath(pen, path);
            }

            Theme.Text(g, CurrentLabel(), Theme.Font(20f), Theme.Parchment,
                new RectangleF(ComboButton.X + 18, ComboButton.Y, ComboButton.Width - 60, ComboButton.Height),
                StringAlignment.Near, StringAlignment.Center);

            float cx = ComboButton.Right - 26;
            float cy = ComboButton.Y + ComboButton.Height / 2f + (open ? 1f : -1f);
            using (var brush = new SolidBrush(Color.FromArgb(210, Theme.Parchment)))
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[]
                {
                    new PointF(cx - 8, cy - 4),
                    new PointF(cx + 8, cy - 4),
                    new PointF(cx, cy + 6)
                });
                g.FillPath(brush, path);
            }
        }

        private void DrawComboList(Graphics g)
        {
            Art.Shadow(g, ComboList, 10, 140, 6);
            using (var path = Theme.RoundRect(ComboList, 10))
            {
                using (var brush = new LinearGradientBrush(ComboList, Color.FromArgb(252, 0x24, 0x1E, 0x18), Color.FromArgb(252, 0x18, 0x14, 0x10), 90f))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(180, Theme.Gold), 1.6f))
                    g.DrawPath(pen, path);
            }

            for (int i = 0; i < Resolutions.GetLength(0); i++)
            {
                var r = ComboItem(i);
                bool hover = Hover(r);
                bool active = App.ClientSize.Width == Resolutions[i, 0] && App.ClientSize.Height == Resolutions[i, 1];

                if (hover)
                {
                    using (var path = Theme.RoundRect(new RectangleF(r.X + 4, r.Y + 3, r.Width - 8, r.Height - 6), 6))
                    using (var brush = new SolidBrush(Color.FromArgb(60, Theme.Gold)))
                        g.FillPath(brush, path);
                }

                if (active)
                {
                    Theme.Text(g, "◆", Theme.Font(13f), Theme.Gold,
                        new RectangleF(r.X + 12, r.Y, 24, r.Height), StringAlignment.Center, StringAlignment.Center);
                }

                Theme.Text(g, Resolutions[i, 0] + " × " + Resolutions[i, 1], Theme.Font(19f, active),
                    active ? Theme.Parchment : Color.FromArgb(hover ? 235 : 205, 0xC9, 0xBE, 0xA2),
                    new RectangleF(r.X, r.Y, r.Width, r.Height), StringAlignment.Center, StringAlignment.Center);
            }
        }

        private void DrawSlider(Graphics g)
        {
            float v = Volume;
            var track = VolumeTrack;

            using (var path = Theme.RoundRect(track, track.Height / 2f))
            using (var brush = new SolidBrush(Color.FromArgb(255, 0x14, 0x11, 0x0D)))
                g.FillPath(brush, path);

            var fill = new RectangleF(track.X, track.Y, Math.Max(6f, track.Width * v), track.Height);
            using (var path = Theme.RoundRect(fill, track.Height / 2f))
            using (var brush = new LinearGradientBrush(fill, Color.FromArgb(0xE0, 0x3A, 0x30), Color.FromArgb(0xA8, 0x12, 0x18), 90f))
                g.FillPath(brush, path);

            float kx = track.X + track.Width * v;
            float ky = track.Y + track.Height / 2f;
            float r = 15f + (dragging ? 2f : 0f);
            using (var brush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillEllipse(brush, kx - r + 2, ky - r + 3, r * 2, r * 2);
            using (var brush = new SolidBrush(Theme.Parchment))
                g.FillEllipse(brush, kx - r, ky - r, r * 2, r * 2);
            using (var pen = new Pen(Color.FromArgb(200, 0x4A, 0x33, 0x22), 2f))
                g.DrawEllipse(pen, kx - r, ky - r, r * 2, r * 2);
        }

        private void DrawToggle(Graphics g, RectangleF r, bool on)
        {
            bool hover = Hover(r);
            using (var path = Theme.RoundRect(r, r.Height / 2f))
            {
                using (var brush = new SolidBrush(on ? Color.FromArgb(0xC1, 0x2A, 0x1E) : Color.FromArgb(0x2A, 0x24, 0x1C)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(hover ? 230 : 120, on ? Theme.Gold : Color.FromArgb(0x7A, 0x6A, 0x52)), 2f))
                    g.DrawPath(pen, path);
            }

            float knob = r.Height - 10f;
            float x = on ? r.Right - knob - 5f : r.X + 5f;
            using (var brush = new SolidBrush(on ? Theme.Parchment : Color.FromArgb(0x8A, 0x7C, 0x62)))
                g.FillEllipse(brush, x, r.Y + 5f, knob, knob);

            Theme.Text(g, on ? "开" : "关", Theme.Font(15f, true), on ? Theme.Parchment : Color.FromArgb(0xB0, 0x9C, 0x7E),
                new RectangleF(on ? r.X + 6 : r.Right - 34, r.Y, 28, r.Height), StringAlignment.Center, StringAlignment.Center);
        }
    }
}
