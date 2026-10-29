using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace LambDice.Net
{
    public sealed class NetMsg
    {
        public string t;     // start / state / place / bye
        public int[] p;      // 房主棋盘（9 格，0 = 空）
        public int[] o;      // 客人棋盘
        public int turn;     // 0 = 房主，1 = 客人
        public int die;
        public int col;
        public string act;   // roll / place
        public string name;  // 玩家昵称
    }

    /// <summary>TCP 直连 + 房主权威。房主跑全部规则并广播整盘状态，客人只发「放哪一列」。</summary>
    public sealed class NetSession : IDisposable
    {
        public const int DefaultPort = 7777;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

        public bool IsHost { get; private set; }
        public string PeerName { get; private set; } = "对手";
        public string LocalName { get; set; } = "玩家";
        public bool Connected { get; private set; }
        public int Port { get; private set; }

        public event Action OnPeerConnected;
        public event Action<int> OnStart;
        public event Action<int> OnRemotePlace;
        public event Action OnRemoteRematch;
        public event Action<NetMsg> OnState;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        private TcpListener listener;
        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;
        private Thread thread;
        private volatile bool running;
        private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        private readonly object writeLock = new object();

        // ------------------------------------------------------------------ 建连

        public static NetSession CreateHost(int port)
        {
            var s = new NetSession { IsHost = true, Port = port };
            s.StartListening();
            return s;
        }

        public static NetSession Join(string ip, int port)
        {
            var s = new NetSession { IsHost = false, Port = port };
            try
            {
                var c = new TcpClient();
                IAsyncResult ar = c.BeginConnect(ip, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(4000))
                {
                    c.Close();
                    s.Enqueue("evt:error:连接超时，请检查 IP 与端口");
                    return s;
                }
                c.EndConnect(ar);
                c.NoDelay = true;
                s.Attach(c);
                s.SendHello();
                s.Enqueue("evt:peer");
            }
            catch (Exception e)
            {
                s.Enqueue("evt:error:连接失败：" + e.Message);
            }
            return s;
        }

        private void StartListening()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
                running = true;
                thread = new Thread(AcceptLoop) { IsBackground = true };
                thread.Start();
            }
            catch (Exception e)
            {
                Enqueue("evt:error:无法监听端口 " + Port + "：" + e.Message);
            }
        }

        private void AcceptLoop()
        {
            try
            {
                TcpClient c = listener.AcceptTcpClient();
                c.NoDelay = true;
                Attach(c);
                SendHello();
                Enqueue("evt:peer");
                ReadLoop();
            }
            catch (Exception e)
            {
                if (running) Enqueue("evt:error:" + e.Message);
            }
        }

        private void Attach(TcpClient c)
        {
            client = c;
            NetworkStream stream = c.GetStream();
            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            Connected = true;
            if (!IsHost)
            {
                running = true;
                thread = new Thread(ReadLoop) { IsBackground = true };
                thread.Start();
            }
        }

        private void ReadLoop()
        {
            try
            {
                while (running)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    inbox.Enqueue(line);
                }
            }
            catch { }
            if (running) Enqueue("evt:disconnected");
        }

        private void Enqueue(string line)
        {
            inbox.Enqueue(line);
        }

        public void Close()
        {
            running = false;
            Connected = false;
            try { if (writer != null) writer.WriteLine("{\"t\":\"bye\"}"); } catch { }
            try { if (client != null) client.Close(); } catch { }
            try { if (listener != null) listener.Stop(); } catch { }
            client = null;
            listener = null;
            reader = null;
            writer = null;
        }

        public void Dispose() { Close(); }

        // ------------------------------------------------------------------ 发送

        public void SendStart(int firstTurn)
        {
            Send(new NetMsg { t = "start", turn = firstTurn, name = LocalName });
        }

        public void SendHello()
        {
            Send(new NetMsg { t = "hello", name = LocalName });
        }

        public void SendPlace(int column)
        {
            Send(new NetMsg { t = "place", col = column });
        }

        /// <summary>请求再来一局（需要两边都同意）。</summary>
        public void SendRematch()
        {
            Send(new NetMsg { t = "rematch" });
        }

        public void SendState(int[] hostCells, int[] guestCells, int turn, int die, string act)
        {
            Send(new NetMsg { t = "state", p = hostCells, o = guestCells, turn = turn, die = die, act = act });
        }

        private void Send(NetMsg msg)
        {
            if (writer == null) return;
            string json = JsonSerializer.Serialize(msg, Json);
            lock (writeLock)
            {
                try { writer.WriteLine(json); }
                catch { Enqueue("evt:disconnected"); }
            }
        }

        // ------------------------------------------------------------------ 接收（主线程）

        public void Poll()
        {
            string line;
            while (inbox.TryDequeue(out line)) Dispatch(line);
        }

        private void Dispatch(string line)
        {
            if (line.StartsWith("evt:"))
            {
                string rest = line.Substring(4);
                if (rest == "peer") { if (OnPeerConnected != null) OnPeerConnected(); }
                else if (rest == "disconnected")
                {
                    Connected = false;
                    if (OnDisconnected != null) OnDisconnected();
                }
                else if (rest.StartsWith("error:")) { if (OnError != null) OnError(rest.Substring(6)); }
                return;
            }

            NetMsg msg;
            try { msg = JsonSerializer.Deserialize<NetMsg>(line, Json); }
            catch { return; }
            if (msg == null || string.IsNullOrEmpty(msg.t)) return;

            if (!string.IsNullOrEmpty(msg.name)) PeerName = msg.name;

            switch (msg.t)
            {
                case "hello": break;
                case "start": if (OnStart != null) OnStart(msg.turn); break;
                case "place": if (OnRemotePlace != null) OnRemotePlace(msg.col); break;
                case "rematch": if (OnRemoteRematch != null) OnRemoteRematch(); break;
                case "state": if (OnState != null) OnState(msg); break;
                case "bye":
                    Connected = false;
                    if (OnDisconnected != null) OnDisconnected();
                    break;
            }
        }

        // ------------------------------------------------------------------ 工具

        public static System.Collections.Generic.List<string> LocalIPv4List()
        {
            var list = new System.Collections.Generic.List<string>();

            // 截图/文档用：LAMBDICE_IP_OVERRIDE 可替换成占位地址
            string fake = Environment.GetEnvironmentVariable("LAMBDICE_IP_OVERRIDE");
            if (!string.IsNullOrEmpty(fake))
            {
                foreach (string part in fake.Split(','))
                    if (!string.IsNullOrWhiteSpace(part)) list.Add(part.Trim());
                if (list.Count > 0) return list;
            }

            try
            {
                foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (a.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a)) continue;
                    string ip = a.ToString();
                    if (!list.Contains(ip)) list.Add(ip);
                }
            }
            catch { }
            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }

        public static string LocalIPv4()
        {
            try
            {
                foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a.ToString();
            }
            catch { }
            return "127.0.0.1";
        }
    }
}
