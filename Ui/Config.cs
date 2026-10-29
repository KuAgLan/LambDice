using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace LambDice.Ui
{
    /// <summary>简易键值配置，存 %LOCALAPPDATA%\LambDice\config.txt。</summary>
    public static class Config
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string pathFile;

        /// <summary>优先 UserAppData，写不进去就放程序目录（绿色版）。</summary>
        public static string PathFile
        {
            get
            {
                if (pathFile != null) return pathFile;

                var candidates = new List<string>
                {
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LambDice"),
                    AppContext.BaseDirectory,
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LambDice")
                };

                foreach (string dir in candidates)
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        pathFile = System.IO.Path.Combine(dir, "config.txt");
                        return pathFile;
                    }
                    catch { }
                }

                pathFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lambdice-config.txt");
                return pathFile;
            }
        }

        public static void Load()
        {
            Values.Clear();
            try
            {
                if (!File.Exists(PathFile)) return;
                foreach (string line in File.ReadAllLines(PathFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    Values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var lines = new List<string>();
                foreach (var kv in Values) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(PathFile, lines);
            }
            catch { }
        }

        public static int GetInt(string key, int fallback)
        {
            string v;
            if (Values.TryGetValue(key, out v))
            {
                int i;
                if (int.TryParse(v, out i)) return i;
            }
            return fallback;
        }

        public static void SetInt(string key, int value)
        {
            Values[key] = value.ToString();
            Save();
        }

        public static bool GetBool(string key, bool fallback)
        {
            return GetInt(key, fallback ? 1 : 0) != 0;
        }

        public static void SetBool(string key, bool value)
        {
            SetInt(key, value ? 1 : 0);
        }

        public static string GetString(string key, string fallback)
        {
            string v;
            return Values.TryGetValue(key, out v) ? v : fallback;
        }

        public static void SetString(string key, string value)
        {
            Values[key] = value;
            Save();
        }

        public static Rectangle GetRect(string key, Rectangle fallback)
        {
            return new Rectangle(
                GetInt(key + ".x", fallback.X),
                GetInt(key + ".y", fallback.Y),
                GetInt(key + ".w", fallback.Width),
                GetInt(key + ".h", fallback.Height));
        }

        public static void SetRect(string key, Rectangle r)
        {
            SetInt(key + ".x", r.X);
            SetInt(key + ".y", r.Y);
            SetInt(key + ".w", r.Width);
            SetInt(key + ".h", r.Height);
        }
    }

    /// <summary>对战绩：每个对手的胜负场次。</summary>
    public static class Stats
    {
        public static void Record(string opponent, bool win, bool draw)
        {
            if (draw) return;
            string key = win ? "wins." : "losses.";
            Config.SetInt(key + opponent, Config.GetInt(key + opponent, 0) + 1);
        }

        public static void Get(string opponent, out int wins, out int losses)
        {
            wins = Config.GetInt("wins." + opponent, 0);
            losses = Config.GetInt("losses." + opponent, 0);
        }

        public static string Line(string opponent)
        {
            int w, l;
            Get(opponent, out w, out l);
            if (w + l == 0) return "";
            return string.Format("胜 {0} · 负 {1}", w, l);
        }

        public static string Total()
        {
            int total = 0;
            foreach (var o in Opponents())
            {
                int w, l;
                Get(o, out w, out l);
                total += w + l;
            }
            if (total == 0) return "";
            return string.Format("已对战 {0} 局", total);
        }

        private static IEnumerable<string> Opponents()
        {
            return new[] { "RATAU", "KLUNKO & BOP", "SHRUMY" };
        }
    }
}
