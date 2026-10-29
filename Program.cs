using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using LambDice.Game;
using LambDice.Screens;
using LambDice.Ui;

namespace LambDice
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            // 界面全部自绘，不需要系统视觉样式；跳过它还能避免首次运行写临时文件
            Application.SetCompatibleTextRenderingDefault(false);

            if (Has(args, "--auto")) { Auto(args); return; }
            if (Has(args, "--shot")) { Shot(args); return; }
            if (Has(args, "--balance")) { Balance(args); return; }
            if (Has(args, "--autoplay")) { AutoPlay(args); return; }
            if (Has(args, "--click")) { ClickTest(args); return; }
            if (Has(args, "--drag")) { DragTest(args); return; }
            if (Has(args, "--cliptest")) { ClipTest(args); return; }
            if (Has(args, "--bench")) { Bench(args); return; }
            if (Has(args, "--diedump")) { DieDump(Value(args, "--diedump", "Shots/dice")); return; }
            if (Has(args, "--makeicon")) { MakeIcon(Value(args, "--makeicon", "icon.ico")); return; }
            if (Has(args, "--where")) { Console.WriteLine(Config.PathFile + " exists=" + File.Exists(Config.PathFile)); return; }
            if (Has(args, "--sfxdump")) { Sfx.DumpTo(Value(args, "--sfxdump", "sfx")); return; }
            if (Has(args, "--nethost")) { NetHost(args); return; }
            if (Has(args, "--netjoin")) { NetJoin(args); return; }
            if (Has(args, "--duel")) { Duel(args); return; }
            if (Has(args, "--probe")) { Probe(); return; }

            Application.Run(new App());
        }

        private static bool Has(string[] args, string name)
        {
            return Array.IndexOf(args, name) >= 0;
        }

        private static string Value(string[] args, string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            if (i < 0 || i + 1 >= args.Length) return fallback;
            return args[i + 1];
        }

        // ------------------------------------------------------------------ 自动对局（AI 自测）

        private static void Auto(string[] args)
        {
            string outPath = Value(args, "--auto", "auto.txt");
            int games = int.Parse(Value(args, "--games", "1"));
            int seed = int.Parse(Value(args, "--seed", "12345"));

            var lines = new List<string>();
            int playerWins = 0, oppWins = 0, draws = 0;
            for (int g = 0; g < games; g++)
            {
                var match = new Match(seed + g);
                int moves = 0;
                while (!match.Over && moves < 60)
                {
                    match.Roll();
                    var mine = match.BoardOf(match.Turn);
                    var theirs = match.BoardOfOther(match.Turn);
                    var diff = match.Turn == Side.Player ? Difficulty.Normal : Difficulty.Hard;
                    int col = Ai.ChooseColumn(mine, theirs, match.Die, diff, match.Rng);
                    match.Place(col);
                    moves++;
                }

                if (match.Outcome > 0) playerWins++;
                else if (match.Outcome < 0) oppWins++;
                else draws++;

                lines.Add(string.Format("game={0} over={1} moves={2} player={3} opponent={4} pDice={5} oDice={6}",
                    g, match.Over, moves, match.Player.Total, match.Opponent.Total, match.Player.Count, match.Opponent.Count));
            }

            lines.Add(string.Format("SUMMARY games={0} playerWins={1} opponentWins={2} draws={3}", games, playerWins, oppWins, draws));
            File.WriteAllLines(outPath, lines);
            Console.WriteLine(lines[lines.Count - 1]);
        }

        private static void DieDump(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            foreach (int size in new[] { 64, 96, 112, 256 })
            {
                using (var bmp = Art.Die(6, DieTint.Pair, size))
                    bmp.Save(System.IO.Path.Combine(dir, "die" + size + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("dice -> " + dir);
        }

        // ------------------------------------------------------------------ 性能测试

        private static void Bench(string[] args)
        {
            string screen = Value(args, "--screen", "match");
            int frames = int.Parse(Value(args, "--frames", "240"));
            int w = int.Parse(Value(args, "--w", "1600"));
            int h = int.Parse(Value(args, "--h", "900"));

            ScreenKind kind = ScreenKind.Match;
            if (screen == "title") kind = ScreenKind.Title;
            else if (screen == "help") kind = ScreenKind.Instructions;
            else if (screen == "opp") kind = ScreenKind.Opponents;
            else if (screen == "lobby") kind = ScreenKind.Lobby;
            else if (screen == "settings") kind = ScreenKind.Settings;

            using (var app = new App())
            {
                app.SetScreenForTest(kind);
                if (kind == ScreenKind.Match) app.Match.FastForward(9);

                using (var bmp = new Bitmap(w, h))
                using (var g = Graphics.FromImage(bmp))
                {
                    for (int i = 0; i < 30; i++) app.DrawScene(g, w, h);   // 预热

                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < frames; i++) app.DrawScene(g, w, h);
                    sw.Stop();

                    double ms = sw.Elapsed.TotalMilliseconds / frames;
                    Console.WriteLine(string.Format("{0} {1}x{2}: {3:0.00} ms/frame (~{4:0} fps)", screen, w, h, ms, 1000.0 / ms));
                }
            }
        }

        // ------------------------------------------------------------------ 图标

        private static void MakeIcon(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            using (var preview = RenderIcon(256))
                preview.Save(System.IO.Path.ChangeExtension(path, ".png"), System.Drawing.Imaging.ImageFormat.Png);

            foreach (int size in sizes)
            {
                using (var bmp = RenderIcon(size))
                using (var ms = new System.IO.MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            }

            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);
                bw.Write((short)1);
                bw.Write((short)sizes.Length);

                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((short)1);
                    bw.Write((short)32);
                    bw.Write(images[i].Length);
                    bw.Write(offset);
                    offset += images[i].Length;
                }

                foreach (var bytes in images) bw.Write(bytes);
            }

            Console.WriteLine("icon -> " + path);
        }

        private static Bitmap RenderIcon(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float pad = size * 0.035f;
                var rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
                using (var path = Theme.RoundRect(rect, size * 0.22f))
                {
                    using (var brush = new LinearGradientBrush(rect,
                        Color.FromArgb(0x26, 0x1F, 0x18), Color.FromArgb(0x0E, 0x0B, 0x09), 90f))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(110, 0x6E, 0x5A, 0x42), Math.Max(1f, size * 0.014f)))
                        g.DrawPath(pen, path);
                }

                Art.BoneMark(g, new PointF(size / 2f, size / 2f), size * 0.40f, Theme.Parchment, 1f);
            }
            return bmp;
        }

        // ------------------------------------------------------------------ 裁切自测（窗口拖到屏幕外）

        private static void ClipTest(string[] args)
        {
            string a = Value(args, "--clip-full", "Shots/clip_full.png");
            string b = Value(args, "--clip-part", "Shots/clip_part.png");
            int left = int.Parse(Value(args, "--clip-left", "700"));

            using (var app = new App())
            {
                app.SetScreenForTest(ScreenKind.Title);

                using (var full = app.RenderFrame(1600, 900)) full.Save(a, System.Drawing.Imaging.ImageFormat.Png);

                using (var part = new Bitmap(1600, 900))
                {
                    using (var g = Graphics.FromImage(part))
                    {
                        g.SetClip(new Rectangle(left, 0, 1600 - left, 900));
                        app.DrawScene(g, 1600, 900);
                    }
                    part.Save(b, System.Drawing.Imaging.ImageFormat.Png);

                    using (var full = new Bitmap(a))
                    {
                        int same = 0, total = 0;
                        for (int y = 4; y < 900; y += 7)
                        {
                            for (int x = left + 4; x < 1600; x += 7)
                            {
                                total++;
                                if (full.GetPixel(x, y) == part.GetPixel(x, y)) same++;
                            }
                        }
                        Console.WriteLine(string.Format("clip region match: {0}/{1} = {2:0.0}%", same, total, 100.0 * same / total));
                    }
                }
            }
        }

        // ------------------------------------------------------------------ 拖动自测

        private static void DragTest(string[] args)
        {
            int x1 = int.Parse(Value(args, "--x1", "500"));
            int y1 = int.Parse(Value(args, "--y1", "280"));
            int x2 = int.Parse(Value(args, "--x2", "1000"));
            int y2 = int.Parse(Value(args, "--y2", "280"));

            using (var app = new App())
            {
                app.SetScreenForTest(ScreenKind.Settings);
                using (var bmp = app.RenderFrame(1600, 900)) { }

                int before = Ui.Sfx.Volume;
                app.SimulateClick(new Point(x1, y1));
                app.StepForTest(1f / 60f);
                int afterPress = Ui.Sfx.Volume;

                for (int i = 1; i <= 12; i++)
                {
                    float k = i / 12f;
                    app.SimulateMove(new Point((int)(x1 + (x2 - x1) * k), (int)(y1 + (y2 - y1) * k)));
                    app.StepForTest(1f / 60f);
                }
                int afterDrag = Ui.Sfx.Volume;
                app.SimulateRelease();
                app.StepForTest(1f / 60f);

                Ui.Sfx.Volume = before;
                Config.SetInt("volume", before);

                Console.WriteLine(string.Format("volume before={0} afterPress={1} afterDrag={2} (restored)", before, afterPress, afterDrag));
            }
        }

        // ------------------------------------------------------------------ 联机自测

        private static void NetHost(string[] args)
        {
            string outPath = Value(args, "--nethost", "nethost.txt");
            int port = int.Parse(Value(args, "--port", "7799"));
            using (var app = new App())
            {
                app.SetScreenForTest(ScreenKind.Lobby);
                app.Lobby.AutoHost(port);
                RunNet(app, outPath, true, 140);
            }
        }

        private static void NetJoin(string[] args)
        {
            int i = Array.IndexOf(args, "--netjoin");
            string ip = i + 1 < args.Length ? args[i + 1] : "127.0.0.1";
            string outPath = i + 2 < args.Length ? args[i + 2] : "netjoin.txt";
            int port = int.Parse(Value(args, "--port", "7799"));
            using (var app = new App())
            {
                app.SetScreenForTest(ScreenKind.Lobby);
                app.Lobby.AutoJoin(ip, port);
                RunNet(app, outPath, false, 140);
            }
        }

        private static void RunNet(App app, string outPath, bool host, float seconds)
        {
            bool rematch = Has(Environment.GetCommandLineArgs(), "--rematch");
            float leaveAt = float.Parse(Value(Environment.GetCommandLineArgs(), "--leave", "0"));
            string secArg = Value(Environment.GetCommandLineArgs(), "--seconds", "0");
            float sec;
            if (float.TryParse(secArg, out sec) && sec > 0) seconds = sec;
            var clock = Stopwatch.StartNew();
            float last = 0f;
            var trace = new StringBuilder();
            var results = new List<string>();
            MatchScreen recorded = null;
            int matches = 0;

            while (clock.Elapsed.TotalSeconds < seconds)
            {
                float now = (float)clock.Elapsed.TotalSeconds;
                float dt = Math.Min(0.05f, now - last);
                last = now;

                if (leaveAt > 0 && now > leaveAt && app.Match != null)
                {
                    trace.AppendLine(string.Format("{0:0}s leave match -> title", now));
                    app.ShowTitle();
                    Thread.Sleep(300);
                    break;
                }

                app.StepForTest(dt);

                MatchScreen current = app.Match;
                if (current != null) current.AutoPlay();

                if ((int)(now * 2) != (int)((now - dt) * 2))
                    trace.AppendLine(string.Format("{0:0}s {1}", now, current == null ? "lobby/title" : "in-match " + current.DebugState));

                if (current != null && current.Finished && !ReferenceEquals(current, recorded))
                {
                    recorded = current;
                    results.Add(string.Format("match{0}: host={1} me={2} opp={3} player={4} opponent={5}",
                        matches + 1, host, current.NameForTest, current.OppNameForTest, current.PlayerScore, current.OpponentScore));
                    matches++;

                    if (matches == 1 && rematch)
                    {
                        Thread.Sleep(250);
                        current.RequestRematchForTest();   // 双方都点「再来一局」
                        Thread.Sleep(250);
                        continue;
                    }
                    break;
                }

                if (recorded != null && current == null) break;   // 对手断开
                Thread.Sleep(4);
            }

            Thread.Sleep(2500);   // 让对手也跑完结算，避免先退连接
            if (recorded != null)
                results.Add(string.Format("stats: states={0} ownSends={1}", recorded.StatStates, recorded.StatOwnSends));
            results.Add("sounds: " + Ui.Sfx.PlayStats());
            File.WriteAllText(outPath, string.Join(Environment.NewLine, results));
            File.WriteAllText(outPath + ".trace", trace.ToString());
            Console.WriteLine(string.Join(" | ", results));
        }

        // ------------------------------------------------------------------ 点击自测

        private static void ClickTest(string[] args)
        {
            int x = int.Parse(Value(args, "--click-x", "800"));
            int y = int.Parse(Value(args, "--click-y", "597"));
            string screen = Value(args, "--screen", "title");
            using (var app = new App())
            {
                ScreenKind kind = ScreenKind.Title;
                if (screen == "help") kind = ScreenKind.Instructions;
                else if (screen == "opp") kind = ScreenKind.Opponents;
                else if (screen == "lobby") kind = ScreenKind.Lobby;
                else if (screen == "settings") kind = ScreenKind.Settings;
                else if (screen == "match") kind = ScreenKind.Match;

                app.SetScreenForTest(kind);
                using (var bmp = app.RenderFrame(1600, 900)) { }
                app.SimulateClick(new Point(x, y));
                app.StepForTest(1f / 60f);   // 让界面处理这次点击
                try
                {
                    using (var bmp = app.RenderFrame(1600, 900))
                    {
                        string save = Value(args, "--save", null);
                        if (!string.IsNullOrEmpty(save)) bmp.Save(save, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Console.WriteLine(string.Format("after click ({0},{1}) on {2}: kind={3} match={4}", x, y, screen, app.Kind, app.Match != null));
                }
                catch (Exception)
                {
                    Console.WriteLine(string.Format("after click ({0},{1}) on {2}: app closed (结束)", x, y, screen));
                }
            }
        }

        // ------------------------------------------------------------------ 界面自测

        private static void AutoPlay(string[] args)
        {
            string outPath = Value(args, "--autoplay", "autoplay.txt");
            int seed = int.Parse(Value(args, "--seed", "555"));
            using (var app = new App())
            {
                app.Seed = seed;
                app.StartMatch();
                var screen = app.Match;
                using (var bmp = new Bitmap(Theme.Width, Theme.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var trace = new StringBuilder();
                    for (int i = 0; i < 9000 && !screen.Finished; i++)
                    {
                        screen.Update(1f / 60f);
                        screen.AutoPlay();
                        if (i % 20 == 0) { screen.Render(g); g.Clear(Color.Black); }
                        if (i % 300 == 0) trace.AppendLine(i + " " + screen.DebugState);
                    }
                    File.WriteAllText(outPath + ".trace", trace.ToString());
                    File.WriteAllText(outPath, string.Format("finished={0} player={1} opponent={2}", screen.Finished, screen.PlayerScore, screen.OpponentScore));
                }
            }
        }

        private static void Probe()
        {
            var mine = new Board(); var theirs = new Board();
            mine.Add(1, 6);
            theirs.Add(0, 6);
            var rng = new Random(1);
            for (int i = 0; i < 3; i++)
            {
                foreach (var d in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
                    Console.WriteLine(string.Format("die=6 {0} -> col {1} (repeat {2})", d, Ai.ChooseColumn(mine, theirs, 6, d, rng), i));
            }

            // 对比：300 局里“每次都用最优即时收益” vs “随机”
            int win = 0, lose = 0;
            for (int g = 0; g < 300; g++)
            {
                var m = new Match(g * 13 + 5);
                int moves = 0;
                while (!m.Over && moves < 40)
                {
                    var me = m.BoardOf(m.Turn);
                    var him = m.BoardOfOther(m.Turn);
                    if (me.IsFull) break;
                    m.Roll();
                    int col;
                    if (m.Turn == Side.Player)
                    {
                        // best immediate
                        double best = double.MinValue; int bc = 0;
                        for (int c = 0; c < 3; c++)
                        {
                            if (!me.CanPlace(c)) continue;
                            double v = BestGain(me, him, c, m.Die);
                            if (v > best) { best = v; bc = c; }
                        }
                        col = bc;
                    }
                    else
                    {
                        col = m.Rng.Next(3);
                        int guard = 0;
                        while (!me.CanPlace(col) && guard++ < 8) col = m.Rng.Next(3);
                    }
                    m.Place(col);
                    moves++;
                }
                if (m.Outcome > 0) win++; else if (m.Outcome < 0) lose++;
            }
            Console.WriteLine("greedy-first vs random-second: win=" + win + " lose=" + lose);
        }

        private static double BestGain(Board mine, Board theirs, int c, int die)
        {
            int before = mine.ColumnScore(c) + theirs.ColumnScore(c);
            var a = new List<int>(mine.Columns[c]) { die };
            var b = new List<int>(theirs.Columns[c]);
            b.RemoveAll(v => v == die);
            return Board.ScoreOf(a) + Board.ScoreOf(b) - before;
        }

        private static Difficulty ParseDiff(string s)
        {
            if (s == "easy" || s == "0") return Difficulty.Easy;
            if (s == "hard" || s == "2") return Difficulty.Hard;
            return Difficulty.Normal;
        }

        private static void Duel(string[] args)
        {
            string outPath = Value(args, "--duel", "duel.txt");
            int games = int.Parse(Value(args, "--games", "200"));
            var a = ParseDiff(Value(args, "--a", "hard"));
            var b = ParseDiff(Value(args, "--b", "easy"));
            var lines = new List<string>();

            foreach (bool aFirst in new[] { true, false })
            {
                int aw = 0, bw = 0, draw = 0;
                for (int g = 0; g < games; g++)
                {
                    var match = new Match(31 + g * 977);
                    // 强制先手
                    match.Turn = aFirst ? Side.Player : Side.Opponent;
                    int moves = 0;
                    while (!match.Over && moves < 60)
                    {
                        var mine = match.BoardOf(match.Turn);
                        if (mine.IsFull) break;
                        match.Roll();
                        var theirs = match.BoardOfOther(match.Turn);
                        var diff = match.Turn == Side.Player ? a : b;
                        int col = Ai.ChooseColumn(mine, theirs, match.Die, diff, match.Rng);
                        match.Place(col);
                        moves++;
                    }
                    if (match.Outcome > 0) aw++; else if (match.Outcome < 0) bw++; else draw++;
                }
                lines.Add(string.Format("{0} 先手: A({1}) 胜 {2} / B({3}) 胜 {4} / 平 {5}", aFirst ? "A" : "B", a, aw, b, bw, draw));
            }
            File.WriteAllLines(outPath, lines);
            foreach (var l in lines) Console.WriteLine(l);
        }

        // ------------------------------------------------------------------ 平衡性测试

        private static void Balance(string[] args)
        {
            string outPath = Value(args, "--balance", "balance.txt");
            int games = int.Parse(Value(args, "--games", "300"));
            var lines = new List<string>();
            var difficulties = new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard };
            foreach (var d in difficulties)
            {
                int wins = 0, losses = 0, draws = 0;
                for (int g = 0; g < games; g++)
                {
                    var match = new Match(9000 + g * 7);
                    int moves = 0;
                    while (!match.Over && moves < 60)
                    {
                        match.Roll();
                        var mine = match.BoardOf(match.Turn);
                        var theirs = match.BoardOfOther(match.Turn);
                        var diff = match.Turn == Side.Player ? Difficulty.Hard : d;
                        int col = Ai.ChooseColumn(mine, theirs, match.Die, diff, match.Rng);
                        match.Place(col);
                        moves++;
                    }
                    if (match.Outcome > 0) wins++; else if (match.Outcome < 0) losses++; else draws++;
                }
                lines.Add(string.Format("{0}: 玩家(Hard) 胜 {1}  负 {2}  平 {3}  ({4} 局)", d, wins, losses, draws, games));
            }
            File.WriteAllLines(outPath, lines);
            foreach (var l in lines) Console.WriteLine(l);
        }

        // ------------------------------------------------------------------ 截图

        private static void Shot(string[] args)
        {
            string outPath = Value(args, "--shot", "shot.png");
            string screen = Value(args, "--screen", "title");
            int moves = int.Parse(Value(args, "--moves", "0"));
            int seed = int.Parse(Value(args, "--seed", "424242"));
            int opp = int.Parse(Value(args, "--opp", "2"));
            string size = Value(args, "--size", "1600x900");
            int sw = 1600, sh = 900;
            int xi = size.IndexOf('x');
            if (xi > 0)
            {
                int.TryParse(size.Substring(0, xi), out sw);
                int.TryParse(size.Substring(xi + 1), out sh);
                if (sw < 320) sw = 1600;
                if (sh < 240) sh = 900;
            }

            ScreenKind kind = ScreenKind.Title;
            if (screen == "help") kind = ScreenKind.Instructions;
            else if (screen == "opp") kind = ScreenKind.Opponents;
            else if (screen == "match") kind = ScreenKind.Match;
            else if (screen == "lobby") kind = ScreenKind.Lobby;
            else if (screen == "settings") kind = ScreenKind.Settings;
            else if (screen == "result") kind = ScreenKind.Match;

            using (var app = new App())
            {
                if (screen == "result") app.RenderTo(outPath, kind, 9, seed, opp, true, sw, sh);
                else app.RenderTo(outPath, kind, moves, seed, opp, false, sw, sh);
            }
            Console.WriteLine("saved " + outPath);
        }
    }
}
