using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace LambDice.Ui
{
    /// <summary>
    /// 程序化音效：骰子是「骨/塑料击打木桌」的模型——短促宽带噪声瞬态 + 几个高频谐振 + 低频撞击体，
    /// 多次弹跳（间隔越来越短、音量递减），落子是一声干脆的放置音。
    /// </summary>
    public static class Sfx
    {
        private const int Rate = 44100;
        private static readonly Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>();
        private static readonly List<SoundPlayer> Playing = new List<SoundPlayer>();
        private static readonly Random Rng = new Random(20261030);

        public static bool Muted;

        /// <summary>调试统计：各类音效播放次数。</summary>
        public static readonly Dictionary<string, int> PlayCount = new Dictionary<string, int>();

        public static string PlayStats()
        {
            var parts = new List<string>();
            foreach (var kv in PlayCount) parts.Add(kv.Key + ":" + kv.Value);
            return string.Join(",", parts);
        }

        private static int volume = 80;

        /// <summary>0–100，按 10 一档缓存（改音量不会重复合成）。</summary>
        public static int Volume
        {
            get { return volume; }
            set { volume = Math.Max(0, Math.Min(100, value)); }
        }

        public static void Play(string name)
        {
            if (PlayCount.ContainsKey(name)) PlayCount[name]++; else PlayCount[name] = 1;
            if (Muted || volume <= 0) return;
            try
            {
                string key = name;
                if (name == "roll") key = "roll" + Rng.Next(3);
                else if (name == "place") key = "place" + Rng.Next(2);

                int bucket = Math.Max(1, Math.Min(10, (volume + 5) / 10));
                string cacheKey = key + "#" + bucket;

                byte[] wav;
                if (!Cache.TryGetValue(cacheKey, out wav))
                {
                    wav = Build(key, bucket / 10f);
                    Cache[cacheKey] = wav;
                }
                var player = new SoundPlayer(new MemoryStream(wav));
                player.Play();
                Playing.Add(player);
                while (Playing.Count > 16)
                {
                    try { Playing[0].Dispose(); } catch { }
                    Playing.RemoveAt(0);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ 合成

        private static byte[] Build(string name, float vol = 1f)
        {
            switch (name)
            {
                case "roll0": return Render(0.62f, b => Roll(b, 1.00f, 1680f), vol);
                case "roll1": return Render(0.62f, b => Roll(b, 0.95f, 1480f), vol);
                case "roll2": return Render(0.62f, b => Roll(b, 1.05f, 1880f), vol);
                case "place0": return Render(0.30f, b => Place(b, 1.0f, 1620f), vol);
                case "place1": return Render(0.30f, b => Place(b, 0.95f, 1730f), vol);
                case "destroy": return Render(0.55f, b => Destroy(b), vol);
                case "click": return Render(0.16f, b => UiClick(b), vol);
                case "win": return Render(1.0f, b => Arp(b, new[] { 523f, 659f, 784f, 1046f }, 0.9f));
                case "lose": return Render(1.0f, b => Arp(b, new[] { 392f, 330f, 262f, 196f }, 0.9f));
                default: return Render(0.05f, b => { }, vol);
            }
        }

        /// <summary>一次撞击：噪声瞬态 + 三个谐振 + 低频体。</summary>
        private static void Hit(float[] buf, int start, float amp, float freq, float decay, float body, Random r)
        {
            int len = (int)(0.18f * Rate);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if (idx < 0) continue;
                if (idx >= buf.Length) break;

                float t = i / (float)Rate;
                float env = (float)Math.Exp(-t * decay);

                float s = 0f;
                s += (float)Math.Sin(2 * Math.PI * freq * t) * 0.50f;
                s += (float)Math.Sin(2 * Math.PI * freq * 1.74f * t) * 0.26f;
                s += (float)Math.Sin(2 * Math.PI * freq * 2.63f * t) * 0.12f;

                float white = (float)(r.NextDouble() * 2 - 1);
                lp = lp * 0.45f + white * 0.55f;
                s += lp * (float)Math.Exp(-t * 1100f) * 1.5f;

                s += (float)Math.Sin(2 * Math.PI * 150f * t) * (float)Math.Exp(-t * 42f) * body;

                buf[idx] += s * env * amp;
            }
        }

        /// <summary>投骰：一次落桌 + 连续弹跳，间隔越来越短、音量递减。</summary>
        private static void Roll(float[] buf, float gain, float baseFreq)
        {
            var r = new Random((int)baseFreq);

            // 第一次落桌：最实
            Hit(buf, (int)(0.012f * Rate), 1.0f * gain, baseFreq, 44f, 0.85f, r);

            float t = 0.012f + 0.062f;
            float amp = 0.52f * gain;
            float gap = 0.052f;
            for (int i = 0; i < 6; i++)
            {
                float f = baseFreq * (0.86f + (float)r.NextDouble() * 0.4f);
                Hit(buf, (int)(t * Rate), amp, f, 78f + i * 14f, 0.22f, r);
                t += gap;
                gap *= 0.82f;
                amp *= 0.76f;
            }

            // 最后轻轻滚停
            Hit(buf, (int)((t + 0.035f) * Rate), 0.10f * gain, baseFreq * 1.15f, 150f, 0.05f, r);

            // 桌面上的轻微滚动摩擦
            int start = (int)(0.01f * Rate);
            int len = Math.Min(buf.Length - start, (int)(0.30f * Rate));
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float tt = i / (float)Rate;
                float white = (float)(r.NextDouble() * 2 - 1);
                lp = lp * 0.82f + white * 0.18f;
                float env = (float)(Math.Exp(-tt * 11f) * (0.55f + 0.45f * Math.Sin(tt * 140f)));
                buf[start + i] += lp * env * 0.045f * gain;
            }
        }

        /// <summary>落子：骰子放到棋盘上的一声。</summary>
        private static void Place(float[] buf, float gain, float freq)
        {
            var r = new Random((int)freq);
            Hit(buf, (int)(0.004f * Rate), 1.0f * gain, freq, 46f, 0.9f, r);
            Hit(buf, (int)(0.026f * Rate), 0.24f * gain, freq * 1.35f, 110f, 0.12f, r);
        }

        /// <summary>摧毁：一堆骰子被砸碎的连续脆响。</summary>
        private static void Destroy(float[] buf)
        {
            var r = new Random(777);
            for (int i = 0; i < 9; i++)
            {
                float t = 0.004f + i * 0.019f + (float)r.NextDouble() * 0.012f;
                float f = 1500f + (float)r.NextDouble() * 2600f;
                Hit(buf, (int)(t * Rate), (0.85f - i * 0.06f), f, 70f + r.Next(50), 0.42f, r);
            }
            // 低频碎裂
            int start = (int)(0.005f * Rate);
            int len = Math.Min(buf.Length - start, (int)(0.3f * Rate));
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float white = (float)(r.NextDouble() * 2 - 1);
                lp = lp * 0.6f + white * 0.4f;
                float env = (float)Math.Exp(-t * 18f);
                float crackle = r.NextDouble() > 0.985 ? 0.9f : 0.25f;
                buf[start + i] += lp * env * crackle * 0.35f;
            }
        }

        /// <summary>按钮点击：一记干脆的木头「笃」声，明显区别于骰子的脆响。</summary>
        private static void UiClick(float[] buf)
        {
            var r = new Random(31);
            Hit(buf, (int)(0.002f * Rate), 1.0f, 620f, 58f, 1.1f, r);     // 木块主体
            Hit(buf, (int)(0.002f * Rate), 0.35f, 1980f, 170f, 0.06f, r); // 起音的硬边
        }

        private static void Arp(float[] buf, float[] notes, float gain)
        {
            float step = 0.18f;
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(n * step * Rate);
                int len = (int)(0.42f * Rate);
                for (int i = 0; i < len; i++)
                {
                    int idx = start + i;
                    if (idx >= buf.Length) break;
                    float t = i / (float)Rate;
                    float env = (float)Math.Exp(-t * 7.5f);
                    float f = notes[n];
                    float s = (float)(Math.Sin(2 * Math.PI * f * t) * 0.55
                                    + Math.Sin(2 * Math.PI * f * 2f * t) * 0.16
                                    + Math.Sin(2 * Math.PI * f * 3f * t) * 0.06);
                    buf[idx] += s * env * gain * 0.6f;
                }
            }
        }

        // ------------------------------------------------------------------ 写 WAV

        private static byte[] Render(float seconds, Action<float[]> fill, float vol = 1f)
        {
            int n = (int)(Rate * seconds);
            var buf = new float[n];
            fill(buf);

            // 归一化 + 5ms 淡入淡出，避免爆音
            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
            float k = (peak > 0.0001f ? 0.88f / peak : 1f) * vol;
            int fade = (int)(0.005f * Rate);
            for (int i = 0; i < n; i++)
            {
                float v = buf[i] * k;
                if (i < fade) v *= i / (float)fade;
                if (i > n - fade) v *= (n - i) / (float)fade;
                buf[i] = v;
            }

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + n * 2);
                bw.Write(new[] { 'W', 'A', 'V', 'E' });
                bw.Write(new[] { 'f', 'm', 't', ' ' });
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)1);
                bw.Write(Rate);
                bw.Write(Rate * 2);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(n * 2);
                for (int i = 0; i < n; i++) bw.Write((short)(buf[i] * 30000f));
                bw.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>调试：把生成的所有音效写到目录里（波形检查用）。</summary>
        public static void DumpTo(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var name in new[] { "roll0", "roll1", "roll2", "place0", "place1", "destroy", "click", "win", "lose" })
                File.WriteAllBytes(Path.Combine(dir, name + ".wav"), Build(name));
        }
    }
}
