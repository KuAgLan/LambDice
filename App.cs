using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using LambDice.Game;
using LambDice.Net;
using LambDice.Screens;
using LambDice.Ui;
using GameScreen = LambDice.Screens.Screen;

namespace LambDice
{
    public enum ScreenKind { Title, Instructions, Opponents, Lobby, Settings, Match }

    public sealed class App : Form
    {
        // 界面按 1600×900 设计；内部画布按屏幕缩放（上限 1.6×），再大就用合成放大，保证帧率
        private Bitmap canvas;
        private Bitmap bgCache;
        private int bgW, bgH;
        private float canvasScale = 1f;

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Timer timer;
        private float lastTime;

        private float scale = 1f;
        private Point offset;
        private bool fullscreen;
        private Rectangle windowedBounds;
        private bool layoutDirty = true;
        private float fade = 1f;
        private Size normalClient = new Size(1600, 900);

        public ScreenKind Kind { get; private set; }
        public TitleScreen Title { get; private set; }
        public InstructionsScreen Help { get; private set; }
        public OpponentScreen Opponents { get; private set; }
        public LobbyScreen Lobby { get; private set; }
        public SettingsScreen Settings { get; private set; }
        public bool Fullscreen { get { return fullscreen; } }

        /// <summary>标题页上的一次性提示（断线等）。</summary>
        public string Notice { get; private set; }
        public float NoticeAge { get; private set; }

        public void ShowNotice(string text)
        {
            Notice = text;
            NoticeAge = 0f;
        }

        /// <summary>把窗口客户区设成指定分辨率（居中，退出全屏）。</summary>
        public void SetClientResolution(int width, int height)
        {
            // 关键：最大化 / 无边框状态下直接改客户区会把窗口状态和后备缓冲搞乱
            if (fullscreen) ToggleFullscreen();
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;

            SuspendLayout();
            Rectangle work = System.Windows.Forms.Screen.FromControl(this).WorkingArea;
            int w = Math.Min(width, work.Width - 40);
            int h = Math.Min(height, work.Height - 40);

            ClientSize = new Size(w, h);
            Location = new Point(work.X + (work.Width - Width) / 2, work.Y + (work.Height - Height) / 2);
            ResumeLayout(true);

            normalClient = new Size(w, h);
            windowedBounds = new Rectangle(Location, new Size(Width, Height));
            layoutDirty = true;
            Invalidate();
            SaveWindow();
        }
        public NetSession Net { get; private set; }
        public MatchScreen Match { get; private set; }
        public GameScreen Current { get; private set; }

        public OpponentInfo Chosen { get; set; } = OpponentInfo.All[0];

        public Point Mouse { get; private set; }

        /// <summary>命中就吃掉这次点击（同一帧只触发一个按钮）。</summary>
        public bool TakeClick(RectangleF rect)
        {
            if (!MouseClicked || !rect.Contains(Mouse)) return false;
            MouseClicked = false;
            return true;
        }

        /// <summary>自测用：模拟一次左键点击（设计坐标）。</summary>
        public void SimulateClick(Point design)
        {
            Mouse = design;
            MouseHeld = true;
            MouseClicked = true;
        }

        /// <summary>自测用：模拟按住拖动。</summary>
        public void SimulateMove(Point design)
        {
            Mouse = design;
        }

        /// <summary>自测用：松开左键。</summary>
        public void SimulateRelease()
        {
            MouseHeld = false;
        }
        public bool MouseHeld { get; private set; }
        public bool MouseClicked { get; private set; }
        public readonly List<Keys> Pressed = new List<Keys>();
        public readonly List<char> Typed = new List<char>();
        public float Time { get; private set; }
        public int Seed { get; set; } = Environment.TickCount;
        public float Fps { get; private set; }

        private float fpsTimer;
        private int fpsCount;

        /// <summary>窗口在设计坐标里覆盖的区域（通栏横带、全屏遮罩用）。</summary>
        public RectangleF ScreenRect
        {
            get
            {
                if (scale <= 0.0001f) return new RectangleF(0, 0, Theme.Width, Theme.Height);
                return new RectangleF(-offset.X / scale, -offset.Y / scale,
                    ClientSize.Width / scale, ClientSize.Height / scale);
            }
        }

        public App()
        {
            Text = "距骨骰 · LambDice";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            RestoreWindow();

            Title = new TitleScreen(this);
            Help = new InstructionsScreen(this);
            Opponents = new OpponentScreen(this);
            Lobby = new LobbyScreen(this);
            Settings = new SettingsScreen(this);
            Current = Title;
            Kind = ScreenKind.Title;

            timer = new Timer { Interval = 15 };
            timer.Tick += (s, e) => Tick();
            timer.Start();
        }

        // ------------------------------------------------------------------ 窗口

        private void RestoreWindow()
        {
            Config.Load();
            Ui.Sfx.Volume = Config.GetInt("volume", 80);
            Ui.Sfx.Muted = Config.GetBool("muted", false);

            Rectangle work = System.Windows.Forms.Screen.FromPoint(Cursor.Position).WorkingArea;
            int w = Math.Min(1600, (int)(work.Width * 0.92f));
            int h = (int)(w * 9f / 16f);
            if (h > work.Height * 0.92f) { h = (int)(work.Height * 0.92f); w = (int)(h * 16f / 9f); }
            int x = work.X + (work.Width - w) / 2;
            int y = work.Y + (work.Height - h) / 2;

            Rectangle saved = Config.GetRect("window", Rectangle.Empty);
            if (saved.Width > 400 && saved.Height > 300)
            {
                w = saved.Width;
                h = saved.Height;
                x = saved.X;
                y = saved.Y;
            }
            fullscreen = Config.GetBool("fullscreen", false);

            if (x < work.X - 60 || y < work.Y - 60 || x > work.Right - 240 || y > work.Bottom - 160)
            {
                x = work.X + (work.Width - w) / 2;
                y = work.Y + (work.Height - h) / 2;
            }

            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(960, 540);
            windowedBounds = new Rectangle(x, y, w, h);
            ClientSize = new Size(w, h);          // 客户区精确等于设定分辨率
            Location = new Point(x, y);
            if (fullscreen) ToggleFullscreen();
            layoutDirty = true;
        }

        private void SaveWindow()
        {
            Rectangle r;
            if (fullscreen) r = new Rectangle(Location, normalClient);
            else if (WindowState == FormWindowState.Maximized) r = new Rectangle(Location, normalClient);
            else r = new Rectangle(Location, ClientSize);
            Config.SetRect("window", r);
            Config.SetBool("fullscreen", fullscreen);
        }

        public void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                windowedBounds = Bounds;
                fullscreen = true;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal; // 先恢复再最大化，避免边框残留
                WindowState = FormWindowState.Maximized;
                Bounds = System.Windows.Forms.Screen.FromControl(this).Bounds;
            }
            else
            {
                fullscreen = false;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = windowedBounds;
            }
            layoutDirty = true;
            SaveWindow();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            layoutDirty = true;
            if (WindowState == FormWindowState.Normal && !fullscreen && ClientSize.Width > 200)
                normalClient = ClientSize;
        }

        // ------------------------------------------------------------------ 界面切换

        public void ShowTitle()
        {
            // 离开对局：关掉连接并销毁对局，对面会收到断线
            CloseNet();
            if (Match != null) { Match.Dispose(); Match = null; }
            Switch(Title, ScreenKind.Title);
        }
        public void ShowHelp() { Switch(Help, ScreenKind.Instructions); }
        public void ShowOpponents() { Switch(Opponents, ScreenKind.Opponents); }

        public void ShowLobby()
        {
            CloseNet();
            Switch(Lobby, ScreenKind.Lobby);
        }

        public void ShowSettings() { Switch(Settings, ScreenKind.Settings); }

        private static OpponentInfo NetOpponent()
        {
            return new OpponentInfo
            {
                Name = "PLAYER",
                Chinese = "对手",
                Difficulty = Difficulty.Hard,
                Blurb = "",
                Portrait = 1,
                Accent = Color.FromArgb(0xC0, 0x39, 0x2B)
            };
        }

        public void StartHostMatch(NetSession session)
        {
            Net = session;
            session.LocalName = Config.GetString("player.name", "玩家");
            session.OnStart -= HandleRemoteRestart;
            Chosen = NetOpponent();
            if (Match != null) Match.Dispose();
            Match = new MatchScreen(this, Chosen, session, true, -1);
            Switch(Match, ScreenKind.Match);
        }

        public void StartGuestMatch(NetSession session, int firstTurn)
        {
            Net = session;
            session.LocalName = Config.GetString("player.name", "玩家");
            session.SendHello();          // 名字可能在连接之后才读出来，补发一次
            session.OnStart -= HandleRemoteRestart;
            session.OnStart += HandleRemoteRestart;
            Chosen = NetOpponent();
            if (Match != null) { Match.Dispose(); Match = null; }
            Match = new MatchScreen(this, Chosen, session, false, firstTurn);
            Switch(Match, ScreenKind.Match);
        }

        /// <summary>房主点「再来一局」：直接用同一条连接开新局并通知客人。</summary>
        public void StartHostRematch()
        {
            if (Net == null || !Net.Connected) { ShowTitle(); return; }
            if (Match != null) { Match.Dispose(); Match = null; }
            Match = new MatchScreen(this, NetOpponent(), Net, true, -1);
            Switch(Match, ScreenKind.Match);
        }

        private void HandleRemoteRestart(int firstTurn)
        {
            if (Net == null) return;
            if (Kind == ScreenKind.Match) StartGuestMatch(Net, firstTurn);
        }

        public void OnNetworkLost()
        {
            CloseNet();
            if (Match != null) { Match.Dispose(); Match = null; }
            Switch(Title, ScreenKind.Title);
            ShowNotice("与对手的连接已断开");
        }

        private void CloseNet()
        {
            if (Net != null) { Net.Close(); Net = null; }
        }

        public void StartMatch()
        {
            if (Match != null) Match.Dispose();
            Match = new MatchScreen(this, Chosen);
            Switch(Match, ScreenKind.Match);
        }

        private void Switch(GameScreen screen, ScreenKind kind)
        {
            Current = screen;
            Kind = kind;
            screen.OnEnter();
            fade = 0f;   // 界面切换时从黑场淡入
        }

        // ------------------------------------------------------------------ 主循环

        private void Tick()
        {
            float now = (float)clock.Elapsed.TotalSeconds;
            float dt = Math.Min(0.05f, now - lastTime);
            lastTime = now;
            Time = now;

            fpsTimer += dt;
            fpsCount++;
            if (fpsTimer > 0.5f) { Fps = fpsCount / fpsTimer; fpsTimer = 0; fpsCount = 0; }

            if (Net != null) Net.Poll();
            if (Current != null) Current.Update(dt);
            if (fade < 1f) fade = Math.Min(1f, fade + dt * 5f);
            NoticeAge += dt;
            Pressed.Clear();
            Typed.Clear();

            Invalidate();
        }

        // ------------------------------------------------------------------ 输入

        private Point ToDesign(Point p)
        {
            if (scale <= 0.001f) return p;
            return new Point((int)((p.X - offset.X) / scale), (int)((p.Y - offset.Y) / scale));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Mouse = ToDesign(e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Mouse = ToDesign(e.Location);
            MouseHeld = true;
            MouseClicked = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            MouseHeld = false;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (Typed.Count < 16) Typed.Add(e.KeyChar);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F11 || (e.Alt && e.KeyCode == Keys.Enter))
            {
                ToggleFullscreen();
                return;
            }
            if (Pressed.Count < 8) Pressed.Add(e.KeyCode);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveWindow();
            CloseNet();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------ 布局

        private void ComputeLayout(int width, int height)
        {
            float fit = Math.Min(width / (float)Theme.Width, height / (float)Theme.Height);
            if (fit <= 0.0001f) fit = 0.0001f;
            scale = fit;
            offset = new Point((int)Math.Round((width - Theme.Width * fit) / 2f),
                               (int)Math.Round((height - Theme.Height * fit) / 2f));

            Theme.DeviceScale = fit;

            int cw = Math.Max(1, width);
            int ch = Math.Max(1, height);
            if (canvas == null || canvas.Width != cw || canvas.Height != ch)
            {
                canvas?.Dispose();
                canvas = new Bitmap(cw, ch, PixelFormat.Format32bppPArgb);
            }
            layoutDirty = false;
        }

        private void EnsureBackground(int width, int height)
        {
            if (bgCache != null && bgW == width && bgH == height) return;

            bgCache?.Dispose();
            bgCache = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var bg = Graphics.FromImage(bgCache))
            {
                bg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var dest = CoverRect(width, height);
                bg.DrawImage(Art.Background(), dest);
            }
            bgW = width;
            bgH = height;
        }

        /// <summary>背景按 cover 铺满窗口（等比放大并裁切），任何比例都不留黑边。</summary>
        private static Rectangle CoverRect(int width, int height)
        {
            float s = Math.Max(width / (float)Theme.Width, height / (float)Theme.Height);
            int w = (int)Math.Round(Theme.Width * s);
            int h = (int)Math.Round(Theme.Height * s);
            return new Rectangle((width - w) / 2, (height - h) / 2, w, h);
        }

        /// <summary>把一帧画到任意尺寸的 Graphics（窗口和截图共用）。</summary>
        public void DrawScene(Graphics g, int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            ComputeLayout(width, height);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            EnsureBackground(width, height);

            // 画布和窗口同尺寸：背景、通栏层、界面、覆盖层依次画进去，最后一次 1:1 贴到窗口
            using (var cg = Graphics.FromImage(canvas))
            {
                cg.SmoothingMode = SmoothingMode.AntiAlias;
                cg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                cg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                cg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                cg.CompositingMode = CompositingMode.SourceCopy;   // 背景不透明，直接覆盖（比混合快）
                cg.DrawImageUnscaled(bgCache, 0, 0);
                cg.CompositingMode = CompositingMode.SourceOver;

                if (Current != null)
                {
                    RectangleF screen = new RectangleF(-offset.X / scale, -offset.Y / scale, width / scale, height / scale);
                    var state = cg.Save();
                    cg.TranslateTransform(offset.X, offset.Y);
                    cg.ScaleTransform(scale, scale);
                    Current.RenderBehind(cg, screen);
                    Current.Render(cg);
                    Current.RenderOver(cg, screen);
                    cg.Restore(state);
                }
            }

            g.CompositingMode = CompositingMode.SourceCopy;        // 画布已是完整不透明画面
            g.DrawImageUnscaled(canvas, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;

            if (fade < 1f)
            {
                int alpha = (int)((1f - fade) * 255f);
                using (var brush = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), 0, 0, 0)))
                    g.FillRectangle(brush, 0, 0, width, height);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 布局永远按客户区大小算；窗口拖到屏幕外时绘制区域会被系统裁切，
            // 但场景本身必须保持完整，交给 GDI 自动裁切即可。
            int w = Math.Max(1, ClientSize.Width);
            int h = Math.Max(1, ClientSize.Height);
            if (layoutDirty || canvas == null) ComputeLayout(w, h);
            DrawScene(e.Graphics, w, h);
            MouseClicked = false;   // 点击只消费一帧；按住状态由 OnMouseUp 结束
        }

        // ------------------------------------------------------------------ 截图

        /// <summary>自测用：推进一帧（不重绘、不依赖 Timer）。</summary>
        public void StepForTest(float dt)
        {
            if (Net != null) Net.Poll();
            if (Current != null) Current.Update(dt);
            if (fade < 1f) fade = Math.Min(1f, fade + dt * 5f);
            NoticeAge += dt;
            Pressed.Clear();
            Typed.Clear();
        }

        /// <summary>自测用：直接切到某个界面（不保存截图）。</summary>
        public void SetScreenForTest(ScreenKind kind)
        {
            switch (kind)
            {
                case ScreenKind.Title: Current = Title; break;
                case ScreenKind.Instructions: Current = Help; break;
                case ScreenKind.Opponents: Current = Opponents; break;
                case ScreenKind.Lobby: Current = Lobby; break;
                case ScreenKind.Settings: Current = Settings; break;
                case ScreenKind.Match:
                    if (Match != null) Match.Dispose();
                    Match = new MatchScreen(this, Chosen, 1);
                    Current = Match;
                    break;
            }
            Kind = kind;
            Current.OnEnter();
        }

        public Bitmap RenderFrame(int width, int height)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) DrawScene(g, width, height);
            return bmp;
        }

        public void RenderTo(string path, ScreenKind kind, int moves = 0, int seed = 12345, int opponentIndex = 2,
            bool forceResult = false, int width = 1600, int height = 900)
        {
            Chosen = OpponentInfo.All[Math.Max(0, Math.Min(OpponentInfo.All.Length - 1, opponentIndex))];
            if (Match != null) { Match.Dispose(); Match = null; }
            switch (kind)
            {
                case ScreenKind.Title: Current = Title; break;
                case ScreenKind.Instructions: Current = Help; break;
                case ScreenKind.Opponents: Current = Opponents; break;
                case ScreenKind.Lobby: Current = Lobby; break;
                case ScreenKind.Settings: Current = Settings; break;
                case ScreenKind.Match:
                    Match = new MatchScreen(this, Chosen, seed);
                    Match.FastForward(moves);
                    if (forceResult) Match.ForceResult();
                    Current = Match;
                    break;
            }
            Current.OnEnter();

            using (var bmp = RenderFrame(width, height))
                bmp.Save(path, ImageFormat.Png);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer?.Dispose();
                canvas?.Dispose();
                bgCache?.Dispose();
                Match?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
