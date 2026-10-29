using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LambDice.Game;
using LambDice.Net;
using LambDice.Ui;

namespace LambDice.Screens
{
    public enum Phase { Intro, PlayerRoll, PlayerRolling, PlayerPlace, WaitSync, Settle, OppWait, OppRolling, OppThink, Over }

    public sealed class MatchScreen : Screen, IDisposable
    {
        private struct Ghost { public int Value; public PointF Pos; public float Life; public float Size; }
        private struct Particle { public float X, Y, VX, VY, Life, MaxLife, Size; public Color Color; }
        private struct FloatText { public string Text; public float X, Y, Life; public Color Color; public float Size; }

        private sealed class Flying
        {
            public bool Active;
            public int Value;
            public DieTint Tint;
            public PointF From, To;
            public float T, Dur, Size;
            public Side Side;
            public int Column, Index;
        }

        // ---------------------------------------------------------------- 布局
        private const float BoardW = 424f, BoardH = 330f;
        private static readonly RectangleF TopBoard = new RectangleF(588, 8, BoardW, BoardH);
        private static readonly RectangleF BottomBoard = new RectangleF(588, 500, BoardW, BoardH);
        private static readonly RectangleF PlayerTray = new RectangleF(70, 612, 336, 208);
        private static readonly RectangleF OppTray = new RectangleF(1194, 56, 336, 208);
        private static readonly RectangleF PlayerPlate = new RectangleF(70, 516, 336, 90);
        private static readonly RectangleF OppPlate = new RectangleF(1194, 268, 336, 90);
        private static readonly RectangleF CenterBand = new RectangleF(400, 376, 800, 108);
        private static readonly RectangleF ResultPanel = new RectangleF(340, 176, 920, 548);

        private readonly OpponentInfo info;
        private readonly string myName = Config.GetString("player.name", "玩家");
        private readonly Match match;
        private readonly NetSession net;
        private readonly bool online;
        private readonly bool isHost;
        private float time;
        private Phase phase;
        private float phaseTime;
        private float hoverPulse;
        private int hoverColumn = -1;

        private float shownPlayer, shownOpp;
        private readonly float[] scorePop = new float[3];
        private readonly int[] lastScore = { -1, -1, -1 };
        private float bannerT = 99f;
        private string bannerText = "";
        private bool statsRecorded;
        private bool iWantRematch;
        private bool peerWantsRematch;
        private float syncT = 99f;
        private int rollFace;
        private float announceT = 99f;
        private string announce = "";
        private bool alive = true;
        private bool resultShown;
        private float resultT;

        private readonly Flying fly = new Flying();
        private readonly List<Ghost> ghosts = new List<Ghost>();
        private readonly List<Particle> particles = new List<Particle>();
        private readonly List<FloatText> floats = new List<FloatText>();
        private readonly Random fx = new Random(7);

        public MatchScreen(App app, OpponentInfo info) : this(app, info, Environment.TickCount) { }

        public MatchScreen(App app, OpponentInfo info, int seed) : this(app, info, seed, null, false, -1) { }

        public MatchScreen(App app, OpponentInfo info, NetSession net, bool host, int firstTurn)
            : this(app, info, Environment.TickCount, net, host, firstTurn) { }

        private MatchScreen(App app, OpponentInfo info, int seed, NetSession net, bool host, int firstTurn) : base(app)
        {
            this.info = info;
            this.net = net;
            this.isHost = host;
            online = net != null;
            match = new Match(seed);
            if (online && !host) match.Turn = firstTurn == 0 ? Side.Opponent : Side.Player;
            shownPlayer = match.Player.Total;
            shownOpp = match.Opponent.Total;
            announce = (match.Turn == Side.Player ? myName : OppName) + " 先掷骰";
            announceT = 0f;
            phase = Phase.Intro;
            phaseTime = 0f;

            if (online) SetupNetwork();
        }

        public void Dispose()
        {
            if (net == null) return;
            net.OnDisconnected -= HandleDisconnect;
            net.OnRemotePlace -= HandleRemotePlace;
            net.OnState -= ApplyState;
            net.OnRemoteRematch -= HandleRemoteRematch;
        }

        public override void OnEnter()
        {
            InitLeaves(6);
        }

        // ---------------------------------------------------------------- 逻辑

        public override void Update(float dt)
        {
            time += dt;
            phaseTime += dt;
            announceT += dt;
            hoverPulse += dt;
            UpdateLeaves(dt);
            UpdateEffects(dt);

            bannerT += dt;
            syncT += dt;
            for (int c = 0; c < 3; c++)
            {
                if (scorePop[c] > 0f) scorePop[c] = Math.Max(0f, scorePop[c] - dt * 2.6f);
                int now = match.Player.ColumnScore(c);
                if (lastScore[c] >= 0 && now != lastScore[c]) scorePop[c] = 1f;
                lastScore[c] = now;
            }

            shownPlayer += (match.Player.Total - shownPlayer) * Math.Min(1f, dt * 9f);
            shownOpp += (match.Opponent.Total - shownOpp) * Math.Min(1f, dt * 9f);

            hoverColumn = HoverColumn();
            HandleInput();

            if (resultShown)
            {
                resultT += dt;
                return;
            }

            switch (phase)
            {
                case Phase.Intro:
                    if (phaseTime > 2.1f) SetPhase(match.Turn == Side.Player ? Phase.PlayerRoll : Phase.OppWait);
                    break;

                case Phase.PlayerRoll:
                    if (online && !isHost) break;
                    if (phaseTime > 0.30f)
                    {
                        match.Roll();
                        rollFace = match.Die;
                        Sfx.Play("roll");
                        Broadcast("roll", -1, (int)match.Turn);
                        SetPhase(Phase.PlayerRolling);
                    }
                    break;

                case Phase.PlayerRolling:
                    rollFace = fx.Next(1, 7);
                    if (phaseTime > 0.55f) { rollFace = match.Die; SetPhase(Phase.PlayerPlace); }
                    break;

                case Phase.OppWait:
                    if (online && !isHost) break;   // 客机不自己投骰，等房主广播
                    if (phaseTime > 0.55f)
                    {
                        match.Roll();
                        rollFace = match.Die;
                        Sfx.Play("roll");
                        Broadcast("roll", -1, (int)match.Turn);
                        SetPhase(Phase.OppRolling);
                    }
                    break;

                case Phase.OppRolling:
                    rollFace = fx.Next(1, 7);
                    if (phaseTime > 0.55f)
                    {
                        rollFace = match.Die;
                        SetPhase(Phase.OppThink);
                    }
                    break;

                case Phase.OppThink:
                    if (online)
                    {
                        if (isHost && phaseTime > 6f)
                            Toast("等待 " + OppName + " 落子…");
                        break;
                    }
                    if (phaseTime > 0.35f + (int)info.Difficulty * 0.22f)
                    {
                        int col = Ai.ChooseColumn(match.Opponent, match.Player, match.Die, info.Difficulty, match.Rng);
                        Place(col);
                    }
                    break;

                case Phase.WaitSync:
                    break;   // 等房主回包，期间不接受操作

                case Phase.Settle:
                    if (phaseTime > 0.72f) NextTurn();
                    break;

                case Phase.Over:
                    if (phaseTime > 1.0f) { if (!resultShown) PlayResultSound(); resultShown = true; }
                    break;
            }
        }

        private void SetPhase(Phase p)
        {
            phase = p;
            phaseTime = 0f;
        }

        private void NextTurn()
        {
            if (match.Over) { SetPhase(Phase.Over); return; }
            SetPhase(match.Turn == Side.Player ? Phase.PlayerRoll : Phase.OppWait);
            ShowTurnBanner();
        }

        private void ShowTurnBanner()
        {
            if (match.Over) return;
            bannerText = match.Turn == Side.Player ? "你的回合" : OppName + " 回合";
            bannerT = 0f;
        }

        private void HandleInput()
        {
            if (resultShown)
            {
                foreach (var k in App.Pressed)
                    if (k == Keys.Escape) { App.ShowTitle(); return; }
                return;
            }

            foreach (var k in App.Pressed)
            {
                if (k == Keys.Escape) { App.ShowTitle(); return; }
                if (phase == Phase.PlayerPlace)
                {
                    if (k == Keys.D1 || k == Keys.NumPad1) { Place(0); return; }
                    if (k == Keys.D2 || k == Keys.NumPad2) { Place(1); return; }
                    if (k == Keys.D3 || k == Keys.NumPad3) { Place(2); return; }
                }
            }

            if (!App.MouseClicked) return;
            if (phase == Phase.PlayerPlace && hoverColumn >= 0) Place(hoverColumn);
        }

        private int HoverColumn()
        {
            if (phase != Phase.PlayerPlace && phase != Phase.PlayerRoll) return -1;
            if (phase != Phase.PlayerPlace) return -1;
            for (int c = 0; c < 3; c++)
            {
                var span = ColumnHit(c);
                if (span.Contains(App.Mouse) && match.Player.CanPlace(c)) return c;
            }
            return -1;
        }

        private static RectangleF ColumnHit(int column)
        {
            var cell0 = Art.CellRect(BottomBoard, column, 0);
            var cell2 = Art.CellRect(BottomBoard, column, 2);
            return new RectangleF(cell0.X - 4, cell0.Y - 6, cell0.Width + 8, cell2.Bottom - cell0.Y + 12);
        }

        private void Place(int column)
        {
            if (online && !isHost)
            {
                if (phase != Phase.PlayerPlace) return;
                if (!match.Player.CanPlace(column)) return;
                net.SendPlace(column);
                StatOwnSends++;
                Sfx.Play("place");
                syncT = 0f;
                SetPhase(Phase.WaitSync);
                return;
            }
            if (phase != Phase.PlayerPlace && phase != Phase.OppThink) return;
            if (!match.BoardOf(match.Turn).CanPlace(column))
            {
                if (match.Turn == Side.Player) Toast("这一列已经放满三颗骰子了");
                return;
            }

            Side side = match.Turn;
            Board mine = match.BoardOf(side);
            Board theirs = match.BoardOfOther(side);
            int value = match.Die;

            // 记录被摧毁的骰子位置
            var doomed = new List<int>();
            for (int i = 0; i < theirs.Columns[column].Count; i++)
                if (theirs.Columns[column][i] == value) doomed.Add(i);

            int index = mine.Columns[column].Count;
            int myBefore = mine.Total + theirs.Total;

            int actorTurn = (int)side;
            match.Place(column);
            int gain = (mine.Total + theirs.Total) - myBefore;

            // 飞行动画
            var boardRect = side == Side.Player ? BottomBoard : TopBoard;
            RectangleF target = CellRectOn(boardRect, side, column, index);
            PointF from = side == Side.Player
                ? new PointF(PlayerTray.X + PlayerTray.Width / 2f, PlayerTray.Y + PlayerTray.Height / 2f)
                : new PointF(OppTray.X + OppTray.Width / 2f, OppTray.Y + OppTray.Height / 2f);

            fly.Active = true;
            fly.Value = value;
            fly.Tint = side == Side.Player ? DieTint.Normal : DieTint.Normal;
            fly.From = from;
            fly.To = new PointF(target.X + target.Width / 2f, target.Y + target.Height / 2f);
            fly.T = 0;
            fly.Dur = 0.30f;
            fly.Size = DieSize;
            fly.Side = side;
            fly.Column = column;
            fly.Index = index;

            // 被摧毁的骰子
            var theirBoard = side == Side.Player ? TopBoard : BottomBoard;
            foreach (int i in doomed)
            {
                RectangleF cell = CellRectOn(theirBoard, side == Side.Player ? Side.Opponent : Side.Player, column, i);
                ghosts.Add(new Ghost
                {
                    Value = value,
                    Pos = new PointF(cell.X + cell.Width / 2f, cell.Y + cell.Height / 2f),
                    Life = 0.62f,
                    Size = DieSize
                });
            }

            if (doomed.Count > 0)
            {
                Sfx.Play("destroy");
                Shards(fly.To, value);
                AddFloat("摧毁 " + doomed.Count + " 颗", new PointF(fly.To.X, fly.To.Y - 46), Theme.Blood, 22f);
            }
            else
            {
                Sfx.Play("place");
                Dust(fly.To);
            }

            if (gain != 0)
                AddFloat((gain > 0 ? "+" : "") + gain, new PointF(fly.To.X, fly.To.Y - 76), gain > 0 ? Theme.Gold : Theme.Blood, 24f);

            Broadcast("place", column, actorTurn);
            SetPhase(Phase.Settle);
        }

        private void Toast(string text)
        {
            announce = text;
            announceT = 0f;
        }

        private static RectangleF CellRectOn(RectangleF board, Side side, int column, int index)
        {
            int row = side == Side.Opponent ? 2 - index : index;
            return Art.CellRect(board, column, row);
        }

        private const float DieSize = 96f;

        private bool resultSound;

        private void PlayResultSound()
        {
            if (!statsRecorded && !online)
            {
                statsRecorded = true;
                Stats.Record(info.Name, match.Outcome > 0, match.Outcome == 0);
            }
            if (resultSound) return;
            resultSound = true;
            Sfx.Play(match.Outcome > 0 ? "win" : match.Outcome < 0 ? "lose" : "place");
        }

        // ------------------------------------------------------------------ 联机

        private void SetupNetwork()
        {
            net.OnDisconnected += HandleDisconnect;
            if (isHost)
            {
                net.OnRemotePlace += HandleRemotePlace;
                net.OnRemoteRematch += HandleRemoteRematch;
                net.SendStart((int)match.Turn);
                Broadcast("", -1, (int)match.Turn);
            }
            else
            {
                net.OnState += ApplyState;
                net.OnRemoteRematch += HandleRemoteRematch;
            }
        }

        private static int[] Pack(Board b)
        {
            int[] cells = new int[9];
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 3; i++)
                    cells[c * 3 + i] = i < b.Columns[c].Count ? b.Columns[c][i] : 0;
            return cells;
        }

        private static void Fill(Board b, int[] cells)
        {
            for (int c = 0; c < 3; c++)
            {
                b.Columns[c].Clear();
                for (int i = 0; i < 3; i++)
                {
                    int v = cells[c * 3 + i];
                    if (v > 0) b.Columns[c].Add(v);
                }
            }
        }

        private static System.Collections.Generic.List<int>[] Snapshot(Board b)
        {
            var copy = new System.Collections.Generic.List<int>[3];
            for (int c = 0; c < 3; c++) copy[c] = new System.Collections.Generic.List<int>(b.Columns[c]);
            return copy;
        }

        /// <summary>turn 传「行动方」：roll/place 都是谁做的动作，由客机自己推下一步轮到谁。</summary>
        private void Broadcast(string act, int column, int actorTurn)
        {
            if (!online || !isHost) return;
            net.SendState(Pack(match.Player), Pack(match.Opponent), actorTurn, match.Die, act);
        }

        private void HandleRemotePlace(int column)
        {
            if (!online || !isHost) return;
            if (match.Turn != Side.Opponent) return;
            if (phase != Phase.OppThink && phase != Phase.OppRolling) return;
            if (!match.Opponent.CanPlace(column)) return;
            Place(column);
        }

        private void RequestRematch()
        {
            if (!online || iWantRematch) return;
            iWantRematch = true;
            net.SendRematch();
            Sfx.Play("click");
            if (isHost && peerWantsRematch) App.StartHostRematch();
        }

        private void HandleRemoteRematch()
        {
            if (!online) return;
            peerWantsRematch = true;
            if (isHost && iWantRematch) App.StartHostRematch();
        }

        private void HandleDisconnect()
        {
            if (!alive) return;
            alive = false;
            App.OnNetworkLost();
        }

        private void ApplyState(NetMsg m)
        {
            if (!alive) return;

            var oldMine = Snapshot(match.Player);
            var oldTheirs = Snapshot(match.Opponent);

            Fill(match.Player, m.o);
            Fill(match.Opponent, m.p);
            match.Die = m.die;
            match.Rolled = m.die > 0;

            bool actorIsHost = m.turn == 0;
            // roll：还是行动方在下；place：换到对面；开局：就是这个回合方
            bool hostToAct = m.act == "place" ? !actorIsHost : actorIsHost;
            match.Turn = hostToAct ? Side.Opponent : Side.Player;

            if (m.act == "place")
            {
                bool hostActed = m.turn == 0;
                int col = m.col < 0 ? 0 : m.col;
                Board gained = hostActed ? match.Opponent : match.Player;
                int idx = gained.Columns[col].Count - 1;
                if (idx >= 0 && idx < 3)
                {
                    int val = gained.Columns[col][idx];
                    RectangleF boardRect = hostActed ? TopBoard : BottomBoard;
                    float size = 96f;
                    PointF from = hostActed ? TrayCenter(OppTray) : TrayCenter(PlayerTray);
                    RectangleF cell = CellRectOn(boardRect, hostActed ? Side.Opponent : Side.Player, col, idx);
                    PointF to = new PointF(cell.X + cell.Width / 2f, cell.Y + cell.Height / 2f);

                    fly.Active = true;
                    fly.Value = val;
                    fly.Tint = DieTint.Normal;
                    fly.From = from;
                    fly.To = to;
                    fly.T = 0;
                    fly.Dur = 0.30f;
                    fly.Size = size;
                    fly.Side = hostActed ? Side.Opponent : Side.Player;
                    fly.Column = col;
                    fly.Index = idx;

                    bool mineLost = hostActed;
                    Board lostBoard = mineLost ? match.Player : match.Opponent;
                    var oldLost = mineLost ? oldMine : oldTheirs;
                    int before = oldLost[col].Count;
                    int after = lostBoard.Columns[col].Count;
                    int destroyedNow = before - after;
                    for (int i = after; i < before; i++)
                    {
                        RectangleF ghostCell = CellRectOn(mineLost ? BottomBoard : TopBoard, mineLost ? Side.Player : Side.Opponent, col, i);
                        ghosts.Add(new Ghost
                        {
                            Value = val,
                            Pos = new PointF(ghostCell.X + ghostCell.Width / 2f, ghostCell.Y + ghostCell.Height / 2f),
                            Life = 0.62f,
                            Size = 88f
                        });
                    }
                }
            }

            StatStates++;
            if (m.act == "roll") Sfx.Play("roll");
            else if (m.act == "place")
            {
                bool hostActed = m.turn == 0;
                int destroyed = 0;
                for (int c = 0; c < 3; c++)
                {
                    int before = (hostActed ? oldMine : oldTheirs)[c].Count;
                    int after = (hostActed ? match.Player : match.Opponent).Columns[c].Count;
                    if (before > after) destroyed += before - after;
                }
                if (hostActed) Sfx.Play(destroyed > 0 ? "destroy" : "place");
                else if (destroyed > 0) Sfx.Play("destroy");
            }

            announceT = 99f;
            syncT = 99f;
            if (match.Over) phase = Phase.Over;
            else if (m.act == "roll") phase = match.Turn == Side.Player ? Phase.PlayerRolling : Phase.OppRolling;
            else if (match.Turn == Side.Player) phase = match.Die > 0 ? Phase.PlayerPlace : Phase.PlayerRoll;
            else phase = match.Die > 0 ? Phase.OppThink : Phase.OppWait;
            phaseTime = 0f;

            if (phase == Phase.PlayerRolling) rollFace = match.Die;
            shownPlayer = match.Player.Total;
            shownOpp = match.Opponent.Total;
        }

        private static PointF TrayCenter(RectangleF tray)
        {
            return new PointF(tray.X + tray.Width / 2f, tray.Y + tray.Height / 2f);
        }

        private string OppName
        {
            get
            {
                if (online && net != null && !string.IsNullOrEmpty(net.PeerName)) return net.PeerName;
                return info.Chinese;
            }
        }

        public bool Finished { get { return resultShown; } }
        public int StatStates, StatOwnSends;
        public string NameForTest { get { return myName; } }
        public string OppNameForTest { get { return OppName; } }

        /// <summary>自测用：模拟点击「再来一局」。</summary>
        public void RequestRematchForTest() { RequestRematch(); }
        public string DebugState
        {
            get
            {
                return string.Format("phase={0} turn={1} die={2} rolled={3} p={4} o={5} result={6}",
                    phase, match.Turn, match.Die, match.Rolled, match.Player.Count, match.Opponent.Count, resultShown);
            }
        }
        public int PlayerScore { get { return match.Player.Total; } }
        public int OpponentScore { get { return match.Opponent.Total; } }

        /// <summary>自测：让 AI 代打玩家这一侧。</summary>
        public void AutoPlay()
        {
            if (resultShown) return;
            if (phase == Phase.PlayerPlace)
            {
                int col = Ai.ChooseColumn(match.Player, match.Opponent, match.Die, Difficulty.Normal, match.Rng);
                Place(col);
            }
        }

        public void ForceResult()
        {
            resultSound = true;
            phase = Phase.Over;
            resultShown = true;
            resultT = 1f;
            announceT = 99f;
        }

        /// <summary>把对局快进若干手（截图/演示用）。</summary>
        public void FastForward(int moves)
        {
            for (int i = 0; i < moves && !match.Over; i++)
            {
                match.Roll();
                var mine = match.BoardOf(match.Turn);
                var theirs = match.BoardOfOther(match.Turn);
                int col = Ai.ChooseColumn(mine, theirs, match.Die, Difficulty.Normal, match.Rng);
                match.Place(col);
            }

            // 让玩家手里留一颗骰子
            if (!match.Over && match.Turn == Side.Player)
            {
                match.Roll();
                rollFace = match.Die;
                phase = Phase.PlayerPlace;
            }
            else if (!match.Over)
            {
                phase = Phase.OppWait;
            }
            else
            {
                phase = Phase.Over;
                resultShown = true;
                resultT = 1f;
            }
            announceT = 99f;
            shownPlayer = match.Player.Total;
            shownOpp = match.Opponent.Total;
        }

        // ---------------------------------------------------------------- 特效

        private void UpdateEffects(float dt)
        {
            if (fly.Active)
            {
                fly.T += dt / fly.Dur;
                if (fly.T >= 1f) fly.Active = false;
            }

            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                var gh = ghosts[i];
                gh.Life -= dt;
                if (gh.Life <= 0) ghosts.RemoveAt(i); else ghosts[i] = gh;
            }

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.Life -= dt;
                p.VY += 900f * dt;
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                if (p.Life <= 0) particles.RemoveAt(i); else particles[i] = p;
            }

            for (int i = floats.Count - 1; i >= 0; i--)
            {
                var f = floats[i];
                f.Life -= dt;
                f.Y -= 34f * dt;
                if (f.Life <= 0) floats.RemoveAt(i); else floats[i] = f;
            }
        }

        private void AddFloat(string text, PointF pos, Color color, float size)
        {
            floats.Add(new FloatText { Text = text, X = pos.X, Y = pos.Y, Life = 1.1f, Color = color, Size = size });
        }

        private void Dust(PointF pos)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = (float)(fx.NextDouble() * Math.PI * 2);
                float s = (float)(fx.NextDouble() * 90 + 30);
                particles.Add(new Particle
                {
                    X = pos.X, Y = pos.Y,
                    VX = (float)Math.Cos(a) * s, VY = (float)Math.Sin(a) * s - 40,
                    Life = 0.5f, MaxLife = 0.5f, Size = (float)(fx.NextDouble() * 4 + 3),
                    Color = Color.FromArgb(190, 0xD8, 0xCF, 0xB8)
                });
            }
        }

        private void Shards(PointF pos, int value)
        {
            for (int i = 0; i < 18; i++)
            {
                float a = (float)(fx.NextDouble() * Math.PI * 2);
                float s = (float)(fx.NextDouble() * 320 + 90);
                particles.Add(new Particle
                {
                    X = pos.X, Y = pos.Y,
                    VX = (float)Math.Cos(a) * s, VY = (float)Math.Sin(a) * s - 120,
                    Life = 0.75f, MaxLife = 0.75f, Size = (float)(fx.NextDouble() * 7 + 4),
                    Color = i % 3 == 0 ? Theme.Blood : Color.FromArgb(0xF1, 0xE7, 0xC9)
                });
            }
        }

        // ---------------------------------------------------------------- 渲染

        public override void Render(Graphics g)
        {
            DrawBackground(g);

            DrawBoard(g, TopBoard, Side.Opponent);
            DrawBoard(g, BottomBoard, Side.Player);

            // 列分数
            for (int c = 0; c < 3; c++)
            {
                var cellTop = Art.CellRect(TopBoard, c, 0);
                var cellBottom = Art.CellRect(BottomBoard, c, 0);
                DrawColumnScore(g, match.Opponent.ColumnScore(c), cellTop.X + cellTop.Width / 2f, 356f, 0f);
                DrawColumnScore(g, match.Player.ColumnScore(c), cellBottom.X + cellBottom.Width / 2f, 456f, scorePop[c]);
            }

            DrawTrays(g);
            DrawPlates(g);
            DrawCenter(g);

            // 飞行的骰子
            if (fly.Active)
            {
                float t = Math.Min(1f, fly.T);
                float e = t * t * (3 - 2 * t);
                float x = fly.From.X + (fly.To.X - fly.From.X) * e;
                float y = fly.From.Y + (fly.To.Y - fly.From.Y) * e - (float)Math.Sin(e * Math.PI) * 70f;
                Art.DrawDie(g, new RectangleF(x - fly.Size / 2, y - fly.Size / 2, fly.Size, fly.Size), fly.Value, fly.Tint, 1f, (1 - e) * 18f);
            }

            DrawGhosts(g);
            DrawParticles(g);
            DrawFloats(g);

            // 底部提示
            string hint = resultShown ? "" : HintText();
            if (!string.IsNullOrEmpty(hint))
                Theme.Text(g, hint, Theme.Font(17f), Color.FromArgb(200, 0xC9, 0xBE, 0xA2),
                    new RectangleF(0, 842, Theme.Width, 26));
            KeyHint(g, "1 / 2 / 3 或点击列 放下骰子     Esc 返回菜单", 868);


        }

        private string HintText()
        {
            switch (phase)
            {
                case Phase.PlayerRoll: return "正在投骰…";
                case Phase.PlayerPlace: return "骰子已就位，点击一列放下（1 / 2 / 3）";
                case Phase.OppWait:
                case Phase.OppRolling:
                case Phase.OppThink: return OppName + " 正在行动…";
                default: return "";
            }
        }

        private void DrawBoard(Graphics g, RectangleF rect, Side side)
        {
            Art.Shadow(g, rect, 16, 110, 10);
            float k = Theme.DeviceScale;
            Blit.Image(g, Art.Board((int)Math.Round(rect.Width * k), (int)Math.Round(rect.Height * k),
                side == Side.Player ? 17 : 23, side == Side.Opponent), rect);

            Board board = match.BoardOf(side);
            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < board.Columns[c].Count; i++)
                {
                    if (fly.Active && fly.Side == side && fly.Column == c && fly.Index == i) continue;
                    RectangleF cell = CellRectOn(rect, side, c, i);
                    float size = Math.Min(cell.Width, cell.Height) * 0.86f;
                    var dieRect = new RectangleF(cell.X + (cell.Width - size) / 2f, cell.Y + (cell.Height - size) / 2f, size, size);
                    int value = board.Columns[c][i];
                    Art.DrawDie(g, dieRect, value, TintFor(board, c, value), 1f, 0f);
                }
            }

            // 悬停高亮
            if (side == Side.Player && phase == Phase.PlayerPlace && hoverColumn >= 0)
            {
                for (int r = 0; r < 3; r++)
                {
                    RectangleF cell = Art.CellRect(rect, hoverColumn, r);
                    using (var path = Theme.RoundRect(new RectangleF(cell.X - 2, cell.Y - 2, cell.Width + 4, cell.Height + 4), 8))
                    using (var pen = new Pen(Color.FromArgb(140 + (int)(Math.Sin(hoverPulse * 6) * 40), Theme.Gold), 3f))
                        g.DrawPath(pen, path);
                }

                // 落点虚影
                int idx = board.Columns[hoverColumn].Count;
                if (idx < 3)
                {
                    RectangleF cell = CellRectOn(rect, side, hoverColumn, idx);
                    float size = Math.Min(cell.Width, cell.Height) * 0.86f;
                    var dieRect = new RectangleF(cell.X + (cell.Width - size) / 2f, cell.Y + (cell.Height - size) / 2f, size, size);
                    Art.DrawDie(g, dieRect, match.Die, DieTint.Ghost, 0.45f, 0f);
                }
            }
        }

        private static DieTint TintFor(Board board, int column, int value)
        {
            int same = 0;
            foreach (int v in board.Columns[column]) if (v == value) same++;
            if (same >= 3) return DieTint.Triple;
            if (same == 2) return DieTint.Pair;
            return DieTint.Normal;
        }

        private void DrawColumnScore(Graphics g, int score, float cx, float y, float pop)
        {
            Color c = score > 0 ? Theme.Parchment : Color.FromArgb(70, 0xC9, 0xBE, 0xA2);
            if (pop > 0.01f) c = Theme.Lerp(c, Theme.Gold, Math.Min(1f, pop * 1.4f));
            float size = 34f * (1f + 0.22f * pop);
            Theme.Text(g, score.ToString(), Theme.Font(size, true), c,
                new RectangleF(cx - 60, y - pop * 4f, 120, 40), StringAlignment.Center, StringAlignment.Center, score > 0);
        }

        private void DrawTrays(Graphics g)
        {
            // 玩家骰盆
            bool playerActive = phase == Phase.PlayerRoll;
            DrawTrayGlow(g, PlayerTray, playerActive, Theme.Gold);
            Blit.Image(g, Art.Tray((int)Math.Round(PlayerTray.Width * Theme.DeviceScale), (int)Math.Round(PlayerTray.Height * Theme.DeviceScale)), PlayerTray);
            if (phase == Phase.PlayerRoll || phase == Phase.PlayerPlace || phase == Phase.PlayerRolling)
                DrawTrayDie(g, PlayerTray, phase == Phase.PlayerRolling ? rollFace : match.Die, phase == Phase.PlayerRolling);

            // 对手骰盆
            bool oppActive = phase == Phase.OppWait || phase == Phase.OppRolling || phase == Phase.OppThink;
            DrawTrayGlow(g, OppTray, oppActive, Theme.Blood);
            Blit.Image(g, Art.Tray((int)Math.Round(OppTray.Width * Theme.DeviceScale), (int)Math.Round(OppTray.Height * Theme.DeviceScale)), OppTray);
            if (oppActive && match.Rolled)
                DrawTrayDie(g, OppTray, phase == Phase.OppRolling ? rollFace : match.Die, phase == Phase.OppRolling);
        }

        private void DrawTrayGlow(Graphics g, RectangleF tray, bool active, Color color)
        {
            float a = active ? 0.10f + 0.03f * (float)Math.Sin(time * 3f) : 0.03f;
            using (var path = Theme.RoundRect(new RectangleF(tray.X - 18, tray.Y - 18, tray.Width + 36, tray.Height + 36), 24))
            using (var brush = new SolidBrush(Color.FromArgb((int)(a * 255), color)))
                g.FillPath(brush, path);
        }

        private void DrawTrayDie(Graphics g, RectangleF tray, int value, bool rolling)
        {
            float size = 112f;
            var rect = new RectangleF(tray.X + tray.Width / 2f - size / 2f, tray.Y + tray.Height / 2f - size / 2f, size, size);
            float angle = rolling ? (float)Math.Sin(time * 30f) * 8f : 0f;
            Art.DrawDie(g, rect, value, DieTint.Normal, 1f, angle);
        }

        private void DrawPlates(Graphics g)
        {
            bool myTurn = match.Turn == Side.Player && !resultShown;
            bool hisTurn = match.Turn == Side.Opponent && !resultShown;
            DrawPlate(g, PlayerPlate, myName, Theme.Gold, false, myTurn);
            DrawPlate(g, OppPlate, OppName, info.Accent, true, hisTurn);
        }

        private void DrawPlate(Graphics g, RectangleF r, string name, Color accent, bool right, bool active)
        {
            if (active)
            {
                float pulse = 0.5f + 0.5f * (float)Math.Sin(time * 3.2f);
                using (var path = Theme.RoundRect(new RectangleF(r.X - 12, r.Y - 8, r.Width + 24, r.Height + 16), 22))
                using (var brush = new SolidBrush(Color.FromArgb((int)(16 + 14 * pulse), accent)))
                    g.FillPath(brush, path);
            }

            var portrait = new RectangleF(r.X + 4, r.Y + 6, 68, 68);
            if (right) Art.Portrait(g, portrait, info.Portrait);
            else Art.Sheep(g, portrait);

            float nameSize = name.Length <= 4 ? 30f : name.Length <= 6 ? 24f : 20f;
            var nameFont = Theme.Font(nameSize, true);
            var nameRect = new RectangleF(r.X + 78, r.Y + 2, r.Width - 84, 44);
            Theme.Text(g, name, nameFont, Theme.Parchment, nameRect, StringAlignment.Center, StringAlignment.Center, true);

            float textWidth = TextRenderer.MeasureText(name, nameFont).Width;
            float cx = nameRect.X + nameRect.Width / 2f;
            Art.Flourish(g, new PointF(cx - textWidth / 2f - 26f, r.Y + 24), 0.62f, false, Theme.CreamDim, 0.42f);
            Art.Flourish(g, new PointF(cx + textWidth / 2f + 26f, r.Y + 24), 0.62f, true, Theme.CreamDim, 0.42f);

            var scoreRect = new RectangleF(r.X + 78, r.Y + 46, r.Width - 84, 40);
            Theme.Text(g, Mathf.RoundToInt(right ? shownOpp : shownPlayer).ToString(), Theme.Font(34f, true),
                accent, scoreRect, StringAlignment.Center, StringAlignment.Center, true);
        }

        private static Bitmap bandCache;
        private static int bandW;
        private static float bandScale;

        public override void RenderBehind(Graphics g, RectangleF screen)
        {
            var band = new RectangleF(screen.X - 10, 352, screen.Width + 20, 146);

            int need = Math.Max(1, (int)Math.Round(band.Width * Theme.DeviceScale));
            if (bandCache == null || bandW != need || Math.Abs(bandScale - Theme.DeviceScale) > 0.01f)
            {
                bandCache?.Dispose();
                bandCache = new Bitmap(need, Math.Max(1, (int)Math.Round(208 * Theme.DeviceScale)), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                bandW = need;
                bandScale = Theme.DeviceScale;
                using (var bg = Graphics.FromImage(bandCache))
                {
                    bg.ScaleTransform(Theme.DeviceScale, Theme.DeviceScale);
                    DrawBandShape(bg, new RectangleF(0, 0, band.Width, 208));
                }
            }

            Blit.Image(g, bandCache, new RectangleF(band.X, band.Y - 26, band.Width, 208));
        }

        private static void DrawBandShape(Graphics g, RectangleF area)
        {
            var band = new RectangleF(area.X, 26, area.Width, 146);
            using (var brush = new SolidBrush(Color.FromArgb(238, 4, 3, 3)))
                g.FillRectangle(brush, band);

            // 上下柔边
            using (var v = new LinearGradientBrush(new RectangleF(band.X, band.Y - 26, band.Width, 26),
                Color.FromArgb(0, 4, 3, 3), Color.FromArgb(238, 4, 3, 3), 90f))
                g.FillRectangle(v, band.X, band.Y - 26, band.Width, 26);
            using (var v = new LinearGradientBrush(new RectangleF(band.X, band.Bottom, band.Width, 26),
                Color.FromArgb(238, 4, 3, 3), Color.FromArgb(0, 4, 3, 3), 90f))
                g.FillRectangle(v, band.X, band.Bottom, band.Width, 26);
        }

        private float shownPlayerOr(int total, bool right) => right ? shownOpp : shownPlayer;

        private void DrawCenter(Graphics g)
        {
            if (bannerT < 0.95f && !resultShown)
            {
                float k = bannerT / 0.95f;
                float a = k < 0.15f ? k / 0.15f : 1f - Math.Max(0f, (k - 0.6f) / 0.4f);
                a = Math.Max(0f, Math.Min(1f, a));
                float bannerSize = 34f + 10f * (1f - Math.Min(1f, k * 5f));
                Theme.TextOutlined(g, bannerText, Theme.Font(bannerSize, true),
                    Color.FromArgb((int)(a * 255), Theme.Parchment), Color.FromArgb((int)(a * 140), 0, 0, 0),
                    new RectangleF(CenterBand.X, CenterBand.Y - 6, CenterBand.Width, CenterBand.Height + 12), 3f);
                return;
            }

            bool showAnnounce = announceT < 2.4f;
            string text = showAnnounce ? announce :
                phase == Phase.PlayerRoll || phase == Phase.PlayerRolling || phase == Phase.PlayerPlace
                    ? "你的回合"
                    : phase == Phase.Over || resultShown ? "" : "对手回合";

            if (string.IsNullOrEmpty(text)) return;

            float alpha = showAnnounce ? Math.Min(1f, (2.4f - announceT) / 0.4f) : 0.85f;
            Color c = showAnnounce ? Theme.Parchment : Color.FromArgb(190, 0xC9, 0xBE, 0xA2);
            float size = showAnnounce ? 40f : 28f;

            if (showAnnounce)
            {
                var band = new RectangleF(CenterBand.X + 60, CenterBand.Y - 4, CenterBand.Width - 120, CenterBand.Height + 8);
                using (var path = Theme.RoundRect(band, 10))
                using (var brush = new SolidBrush(Color.FromArgb((int)(alpha * 190), 0, 0, 0)))
                    g.FillPath(brush, path);
            }

            Theme.TextOutlined(g, text, Theme.Font(size, showAnnounce), Color.FromArgb((int)(alpha * 255), c),
                Color.FromArgb((int)(alpha * 160), 0, 0, 0), CenterBand, 3f);
        }

        private void DrawGhosts(Graphics g)
        {
            foreach (var gh in ghosts)
            {
                float k = Math.Max(0f, gh.Life / 0.62f);
                float size = gh.Size * (1f + (1f - k) * 0.15f);
                var rect = new RectangleF(gh.Pos.X - size / 2, gh.Pos.Y - size / 2, size, size);
                Art.DrawDie(g, rect, gh.Value, DieTint.Destroyed, k, 0f);
                using (var pen = new Pen(Color.FromArgb((int)(255 * k), Theme.Blood), 7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, rect.X + size * 0.26f, rect.Y + size * 0.26f, rect.Right - size * 0.26f, rect.Bottom - size * 0.26f);
                    g.DrawLine(pen, rect.Right - size * 0.26f, rect.Y + size * 0.26f, rect.X + size * 0.26f, rect.Bottom - size * 0.26f);
                }
            }
        }

        private void DrawParticles(Graphics g)
        {
            foreach (var p in particles)
            {
                float k = Math.Max(0f, p.Life / p.MaxLife);
                using (var brush = new SolidBrush(Color.FromArgb((int)(255 * k), p.Color)))
                    g.FillRectangle(brush, p.X, p.Y, p.Size, p.Size);
            }
        }

        private void DrawFloats(Graphics g)
        {
            foreach (var f in floats)
            {
                float k = Math.Max(0f, Math.Min(1f, f.Life / 0.5f));
                Theme.Text(g, f.Text, Theme.Font(f.Size, true), Color.FromArgb((int)(255 * k), f.Color),
                    new RectangleF(f.X - 140, f.Y, 280, 40), StringAlignment.Center, StringAlignment.Center, true);
            }
        }

        public override void RenderOver(Graphics g, RectangleF screen)
        {
            if (resultShown) DrawResult(g, screen);
        }

        private void DrawResult(Graphics g, RectangleF screen)
        {
            float dim = Math.Min(1f, resultT / 0.30f);
            using (var brush = new SolidBrush(Color.FromArgb((int)(dim * 205), 0, 0, 0)))
                g.FillRectangle(brush, screen.X - 10, screen.Y - 10, screen.Width + 20, screen.Height + 20);

            int outcome = match.Outcome;
            string title = outcome > 0 ? "胜 利" : outcome < 0 ? "失 败" : "平 局";
            Color accent = outcome > 0 ? Theme.Gold : outcome < 0 ? Theme.Blood : Theme.CreamDim;

            // 面板从 92% 弹到 100%
            float k = Math.Min(1f, Math.Max(0f, (resultT - 0.04f) / 0.38f));
            float ease = 1f - (float)Math.Pow(1f - k, 3);
            float scale = 0.92f + 0.08f * ease;
            var center = new PointF(ResultPanel.X + ResultPanel.Width / 2f, ResultPanel.Y + ResultPanel.Height / 2f);

            var state = g.Save();
            g.TranslateTransform(center.X, center.Y);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-center.X, -center.Y);

            Art.Shadow(g, ResultPanel, 20, 160, 12);
            using (var path = Theme.RoundRect(ResultPanel, 18))
            {
                using (var brush = new LinearGradientBrush(ResultPanel, Color.FromArgb(0x28, 0x22, 0x1A), Color.FromArgb(0x13, 0x10, 0x0D), 90f))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(170, accent), 2.5f))
                    g.DrawPath(pen, path);
            }

            // 胜者头像 + 光环
            float pk = Math.Min(1f, Math.Max(0f, (resultT - 0.18f) / 0.35f));
            if (pk > 0.01f)
            {
                var pc = new PointF(center.X, ResultPanel.Y + 96);
                float r = 56f;

                if (outcome > 0)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(pc.X - r * 1.9f, pc.Y - r * 1.9f, r * 3.8f, r * 3.8f);
                        using (var brush = new PathGradientBrush(path)
                        {
                            CenterColor = Color.FromArgb((int)(70 * pk), Theme.Gold),
                            SurroundColors = new[] { Color.FromArgb(0, Theme.Gold) }
                        })
                            g.FillPath(brush, path);
                    }

                    // 光芒
                    using (var pen = new Pen(Color.FromArgb((int)(120 * pk), Theme.Gold), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        for (int i = 0; i < 12; i++)
                        {
                            double ang = i * Math.PI / 6 + resultT * 0.4;
                            float x0 = pc.X + (float)Math.Cos(ang) * (r + 12);
                            float y0 = pc.Y + (float)Math.Sin(ang) * (r + 12);
                            float x1 = pc.X + (float)Math.Cos(ang) * (r + 26 + 6 * (float)Math.Sin(resultT * 3 + i));
                            float y1 = pc.Y + (float)Math.Sin(ang) * (r + 26 + 6 * (float)Math.Sin(resultT * 3 + i));
                            g.DrawLine(pen, x0, y0, x1, y1);
                        }
                    }
                }

                using (var brush = new SolidBrush(Color.FromArgb((int)(pk * 235), 0x1A, 0x16, 0x12)))
                    g.FillEllipse(brush, pc.X - r, pc.Y - r, r * 2, r * 2);
                using (var pen = new Pen(Color.FromArgb((int)(pk * 220), accent), 2.4f))
                    g.DrawEllipse(pen, pc.X - r, pc.Y - r, r * 2, r * 2);

                var portrait = new RectangleF(pc.X - r * 0.78f, pc.Y - r * 0.80f, r * 1.56f, r * 1.56f);
                if (outcome > 0) Art.Sheep(g, portrait);
                else Art.Portrait(g, portrait, info.Portrait);
            }

            // 标题（随头像出现）
            float tk = Math.Min(1f, Math.Max(0f, (resultT - 0.10f) / 0.30f));
            Theme.TextOutlined(g, title, Theme.Font(84f, true),
                Color.FromArgb((int)(tk * 255), accent), Color.FromArgb((int)(tk * 90), 0, 0, 0),
                new RectangleF(ResultPanel.X, ResultPanel.Y + 162, ResultPanel.Width, 104), 5f);

            // 比分（数字滚动）
            float ck = Math.Min(1f, Math.Max(0f, (resultT - 0.28f) / 0.55f));
            ck = 1f - (float)Math.Pow(1f - ck, 2);
            int shownMine = (int)Math.Round(match.Player.Total * ck);
            int shownTheirs = (int)Math.Round(match.Opponent.Total * ck);
            Theme.Text(g, string.Format("你 {0}      {1} {2}", shownMine, OppName, shownTheirs),
                Theme.Font(28f), Theme.Parchment, new RectangleF(ResultPanel.X, ResultPanel.Y + 272, ResultPanel.Width, 40));

            for (int c = 0; c < 3; c++)
            {
                string line = string.Format("第 {0} 列     你 {1}   ·   对手 {2}", c + 1, match.Player.ColumnScore(c), match.Opponent.ColumnScore(c));
                Theme.Text(g, line, Theme.Font(20f), Color.FromArgb(200, 0xC9, 0xBE, 0xA2),
                    new RectangleF(ResultPanel.X, ResultPanel.Y + 316 + c * 32, ResultPanel.Width, 30));
            }

            bool ready = resultT > 0.5f;
            if (ready)
            {
                if (online)
                {
                    var back = new RectangleF(ResultPanel.X + 90, ResultPanel.Bottom - 122, 320, 76);
                    RibbonButton(g, back, "返回标题", 28f, () => App.ShowTitle());

                    var again = new RectangleF(ResultPanel.Right - 410, ResultPanel.Bottom - 122, 320, 76);
                    if (iWantRematch)
                    {
                        using (var path = Theme.RoundRect(again, 12))
                        {
                            using (var brush = new SolidBrush(Color.FromArgb(200, 0x33, 0x2B, 0x20)))
                                g.FillPath(brush, path);
                            using (var pen = new Pen(Color.FromArgb(150, 0x7A, 0x6A, 0x52), 2f))
                                g.DrawPath(pen, path);
                        }
                        float pulse = 0.6f + 0.4f * (float)Math.Sin(resultT * 4f);
                        Theme.Text(g, "等待对手同意…", Theme.Font(22f),
                            Color.FromArgb((int)(150 + 90 * pulse), 0xD8, 0xCF, 0xB8),
                            again, StringAlignment.Center, StringAlignment.Center);
                    }
                    else
                    {
                        RibbonButton(g, again, "再来一局", 28f, RequestRematch);
                    }

                    if (peerWantsRematch && !iWantRematch)
                    {
                        float pulse = 0.6f + 0.4f * (float)Math.Sin(resultT * 4f);
                        Theme.Text(g, OppName + " 想再来一局", Theme.Font(19f),
                            Color.FromArgb((int)(140 + 90 * pulse), 0xE0, 0xC0, 0x76),
                            new RectangleF(ResultPanel.Right - 424, ResultPanel.Bottom - 160, 330, 28), StringAlignment.Center);
                    }
                }
                else
                {
                    var again = new RectangleF(ResultPanel.X + 90, ResultPanel.Bottom - 122, 320, 76);
                    var change = new RectangleF(ResultPanel.Right - 410, ResultPanel.Bottom - 122, 320, 76);
                    RibbonButton(g, again, "再来一局", 28f, () => App.StartMatch());
                    RibbonButton(g, change, "更换对手", 28f, () => App.ShowOpponents());
                }
            }

            g.Restore(state);
        }
    }

    internal static class Mathf
    {
        public static int RoundToInt(float v) => (int)Math.Round(v);
    }
}
