using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LambDice.Net;
using LambDice.Ui;

namespace LambDice.Screens
{
    public sealed class LobbyScreen : Screen
    {
        public enum Mode { Idle, Hosting, Joining }

        private readonly TextField hostPort = new TextField("7777", "7777") { OnlyDigits = true, MaxLength = 5 };
        private readonly TextField joinIp = new TextField("127.0.0.1", "房主 IP");
        private readonly TextField joinPort = new TextField("7777", "7777") { OnlyDigits = true, MaxLength = 5 };

        private NetSession session;
        private Mode mode = Mode.Idle;
        private string status = "";
        private Color statusColor = Theme.CreamDim;
        private float t;

        private static readonly RectangleF HostCard = new RectangleF(150, 196, 620, 430);
        private static readonly RectangleF JoinCard = new RectangleF(830, 196, 620, 430);
        private static readonly RectangleF ReturnButton = new RectangleF(680, 700, 240, 44);

        public LobbyScreen(App app) : base(app) { }

        public bool Busy { get { return mode != Mode.Idle; } }

        private string toast = "";
        private float toastT = 99f;

        public override void OnEnter()
        {
            InitLeaves(8);
            t = 0;
            toastT = 99f;

            // 端口固定默认 7777（对局只用这一个默认值，改端口请手动输入）
            hostPort.Text = NetSession.DefaultPort.ToString();
            joinPort.Text = NetSession.DefaultPort.ToString();
            joinIp.Text = Config.GetString("net.ip", "127.0.0.1");

            if (mode != Mode.Idle) SetIdle("已取消");
        }

        public override void Update(float dt)
        {
            t += dt;
            toastT += dt;
            UpdateLeaves(dt);

            // 点本机 IP 复制
            var ips = NetSession.LocalIPv4List();
            for (int i = 0; i < Math.Min(3, ips.Count); i++)
            {
                if (Click(IpRow(i)))
                {
                    try { Clipboard.SetText(ips[i]); toast = "已复制 " + ips[i]; }
                    catch { toast = "复制失败，请手动记下 " + ips[i]; }
                    toastT = 0f;
                    Ui.Sfx.Play("click");
                }
            }

            NetSession current = session;
            if (current != null) current.Poll();

            if (Click(ReturnButton)) { Cancel(); App.ShowTitle(); return; }

            foreach (var k in App.Pressed)
                if (k == Keys.Escape) { Cancel(); App.ShowTitle(); return; }

            bool hostSubmit = hostPort.Update(App, HostPortRect);
            bool ipSubmit = joinIp.Update(App, JoinIpRect);
            bool portSubmit = joinPort.Update(App, JoinPortRect);
            if (hostSubmit && mode == Mode.Idle) StartHost();
            if ((ipSubmit || portSubmit) && mode == Mode.Idle) StartJoin();

            if (Click(HostButton) && mode == Mode.Idle) StartHost();
            if (Click(JoinButton) && mode == Mode.Idle) StartJoin();
            if (Busy && Click(CancelButton)) Cancel();
        }

        // ------------------------------------------------------------------ 布局

        private static RectangleF HostPortRect => new RectangleF(HostCard.X + 300, HostCard.Y + 190, 180, 48);
        private static RectangleF JoinIpRect => new RectangleF(JoinCard.X + 190, JoinCard.Y + 96, 360, 52);
        private static RectangleF JoinPortRect => new RectangleF(JoinCard.X + 190, JoinCard.Y + 166, 160, 52);
        private static RectangleF IpRow(int index) => new RectangleF(HostCard.X + 60, HostCard.Y + 88 + index * 32, 400, 28);
        private static RectangleF HostButton => new RectangleF(HostCard.X + 110, HostCard.Y + 268, 400, 82);
        private static RectangleF JoinButton => new RectangleF(JoinCard.X + 110, JoinCard.Y + 262, 400, 82);
        private static RectangleF CancelButton => new RectangleF(680, 640, 240, 40);

        // ------------------------------------------------------------------ 动作

        public void AutoHost(int port)
        {
            hostPort.Text = port.ToString();
            StartHost();
        }

        public void AutoJoin(string ip, int port)
        {
            joinIp.Text = ip;
            joinPort.Text = port.ToString();
            StartJoin();
        }

        private void StartHost()
        {
            int port = hostPort.Number(NetSession.DefaultPort, 1024, 65535);
            session = NetSession.CreateHost(port);
            session.LocalName = Config.GetString("player.name", "玩家");
            session.OnPeerConnected += HandlePeer;
            session.OnError += HandleError;
            session.OnDisconnected += HandleDisconnected;
            mode = Mode.Hosting;
            SetStatus("正在监听端口 " + port + "，等待对手连接…", Theme.Gold);
        }

        private void StartJoin()
        {
            string ip = string.IsNullOrEmpty(joinIp.Text) ? "127.0.0.1" : joinIp.Text.Trim();
            int port = joinPort.Number(NetSession.DefaultPort, 1024, 65535);
            Config.SetString("net.ip", ip);
            session = NetSession.Join(ip, port);
            session.LocalName = Config.GetString("player.name", "玩家");
            session.OnStart += HandleStart;
            session.OnError += HandleError;
            session.OnDisconnected += HandleDisconnected;
            mode = Mode.Joining;
            SetStatus("正在连接 " + ip + ":" + port + " …", Theme.Gold);
        }

        private void HandlePeer()
        {
            if (session == null || !session.IsHost) return;
            SetStatus("对手已连接，开始对局！", Theme.Gold);
            NetSession s = session;
            session = null;
            mode = Mode.Idle;
            App.StartHostMatch(s);
        }

        private void HandleStart(int firstTurn)
        {
            if (session == null) return;
            NetSession s = session;
            session = null;
            mode = Mode.Idle;
            App.StartGuestMatch(s, firstTurn);
        }

        private void HandleError(string message)
        {
            SetStatus(message, Theme.Blood);
            Cancel();
        }

        private void HandleDisconnected()
        {
            if (mode == Mode.Idle) return;
            SetStatus("连接已断开", Theme.Blood);
            Cancel();
        }

        private void Cancel()
        {
            if (session != null)
            {
                session.Close();
                session = null;
            }
            mode = Mode.Idle;
            SetStatus("", Theme.CreamDim);
        }

        private void SetIdle(string text)
        {
            Cancel();
            if (!string.IsNullOrEmpty(text)) SetStatus(text, Theme.CreamDim);
        }

        private void SetStatus(string text, Color color)
        {
            status = text;
            statusColor = color;
        }

        // ------------------------------------------------------------------ 渲染

        public override void Render(Graphics g)
        {
            DrawBackground(g);

            Theme.TextOutlined(g, "联 机 对 战", Theme.Font(46f, true), Theme.Parchment, Color.FromArgb(70, 0, 0, 0),
                new RectangleF(0, 48, Theme.Width, 62), 4f);
            FlourishPair(g, Theme.Width / 2f, 80, 1.0f, Theme.CreamDim, 0.45f);

            Theme.Text(g, "同一局域网可直接连；公网需要房主在路由器上做端口转发，或用虚拟局域网", Theme.Font(17f),
                Color.FromArgb(210, 0xD8, 0xCF, 0xB8), new RectangleF(0, 124, Theme.Width, 28));

            // 创建房间
            DrawCard(g, HostCard, "创建房间", mode == Mode.Hosting);
            Theme.Text(g, "本机 IP（点一下复制）", Theme.Font(17f), Color.FromArgb(190, 0xC9, 0xBE, 0xA2),
                new RectangleF(HostCard.X + 60, HostCard.Y + 66, 400, 24), StringAlignment.Near);

            var ips = NetSession.LocalIPv4List();
            for (int i = 0; i < Math.Min(3, ips.Count); i++)
            {
                var row = IpRow(i);
                bool hover = Hover(row);
                Color c = i == 0 ? Theme.Parchment : Color.FromArgb(215, 0xC9, 0xBE, 0xA2);
                if (hover) c = Theme.Gold;
                Theme.Text(g, ips[i] + (hover ? "   ⧉" : ""), Theme.Font(i == 0 ? 24f : 19f, i == 0), c,
                    row, StringAlignment.Near, StringAlignment.Center);
            }

            Theme.Text(g, "端口", Theme.Font(20f), Theme.CreamDim,
                new RectangleF(HostCard.X + 60, HostCard.Y + 200, 220, 30), StringAlignment.Near);
            hostPort.Draw(g, HostPortRect);

            Theme.Text(g, "把 IP 和端口告诉对手", Theme.Font(16f), Color.FromArgb(170, 0xC9, 0xBE, 0xA2),
                new RectangleF(HostCard.X + 60, HostCard.Y + 240, 460, 24), StringAlignment.Near);

            bool canHost = mode == Mode.Idle;
            DrawButton(g, HostButton, mode == Mode.Hosting ? "等待对手连接…" : "创建房间", canHost, StartHost);

            // 加入房间
            DrawCard(g, JoinCard, "加入房间", mode == Mode.Joining);
            Theme.Text(g, "房主 IP", Theme.Font(20f), Theme.CreamDim,
                new RectangleF(JoinCard.X + 60, JoinCard.Y + 106, 130, 30), StringAlignment.Near);
            joinIp.Draw(g, JoinIpRect);
            Theme.Text(g, "端口", Theme.Font(20f), Theme.CreamDim,
                new RectangleF(JoinCard.X + 60, JoinCard.Y + 176, 130, 30), StringAlignment.Near);
            joinPort.Draw(g, JoinPortRect);
            Theme.Text(g, "两边端口要一致", Theme.Font(16f), Color.FromArgb(170, 0xC9, 0xBE, 0xA2),
                new RectangleF(JoinCard.X + 380, JoinCard.Y + 178, 200, 26), StringAlignment.Near);

            bool canJoin = mode == Mode.Idle;
            DrawButton(g, JoinButton, mode == Mode.Joining ? "连接中…" : "加入房间", canJoin, StartJoin);

            if (!string.IsNullOrEmpty(status))
                Theme.Text(g, status, Theme.Font(19f), statusColor, new RectangleF(0, 596, Theme.Width, 30));

            if (toastT < 2.2f && !string.IsNullOrEmpty(toast))
            {
                float a = Math.Min(1f, (2.2f - toastT) / 0.5f);
                Theme.Text(g, toast, Theme.Font(17f), Color.FromArgb((int)(a * 220), 0xD8, 0xC0, 0x7A),
                    new RectangleF(0, 566, Theme.Width, 26));
            }

            if (Busy)
            {
                bool hoverC = Hover(CancelButton);
                Theme.Text(g, "取消", Theme.Font(22f), hoverC ? Theme.Parchment : Theme.CreamDim,
                    CancelButton, StringAlignment.Center, StringAlignment.Center);
            }

            TextButton(g, ReturnButton, "返回", 22f, () => { Cancel(); App.ShowTitle(); }, Color.FromArgb(160, 0xC9, 0xBE, 0xA2));

            KeyHint(g, "点输入框后直接键入     回车 确认     Esc 返回", 838);
        }

        private void DrawCard(Graphics g, RectangleF r, string title, bool active)
        {
            Art.Shadow(g, r, 16, 100, 8);
            using (var path = Theme.RoundRect(r, 14))
            {
                using (var brush = new LinearGradientBrush(r, Color.FromArgb(240, 0x25, 0x20, 0x1A), Color.FromArgb(240, 0x15, 0x12, 0x0E), 90f))
                    g.FillPath(brush, path);
                using (var pen = new Pen(active ? Theme.Gold : Color.FromArgb(80, 0x6E, 0x5F, 0x48), active ? 2.6f : 1.5f))
                    g.DrawPath(pen, path);
            }

            Theme.Text(g, title, Theme.Font(32f, true), Theme.Parchment,
                new RectangleF(r.X, r.Y + 20, r.Width, 44));

            if (active)
            {
                float pulse = 0.5f + 0.5f * (float)Math.Sin(t * 3f);
                Art.Dot(g, r.Right - 28, r.Y + 30, 6f, Color.FromArgb((int)(120 + 100 * pulse), Theme.Gold));
            }
        }

        private void DrawButton(Graphics g, RectangleF r, string label, bool enabled, Action action)
        {
            float hover = enabled && Hover(r) ? 1f : 0f;
            float press = hover > 0 && App.MouseHeld ? 1f : 0f;

            if (enabled)
            {
                Art.Ribbon(g, r, hover, press);
            }
            else
            {
                using (var path = new GraphicsPath())
                {
                    float notch = r.Height * 0.34f;
                    path.AddPolygon(new[]
                    {
                        new PointF(r.X + notch, r.Y), new PointF(r.Right - notch, r.Y),
                        new PointF(r.Right, r.Y + r.Height / 2f), new PointF(r.Right - notch, r.Bottom),
                        new PointF(r.X + notch, r.Bottom), new PointF(r.X, r.Y + r.Height / 2f)
                    });
                    using (var brush = new SolidBrush(Color.FromArgb(200, 0x3A, 0x2A, 0x22)))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(140, 0x6E, 0x4A, 0x36), 2f))
                        g.DrawPath(pen, path);
                }
            }

            Theme.Text(g, label, Theme.Font(28f, true), enabled ? Theme.Parchment : Color.FromArgb(170, 0xC9, 0xBE, 0xA2),
                r, StringAlignment.Center, StringAlignment.Center, enabled);

            if (enabled && Click(r)) action();
        }
    }
}
