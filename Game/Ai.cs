using System;
using System.Collections.Generic;
using System.Drawing;

namespace LambDice.Game
{
    /// <summary>三位对手，性格与强度对应原作的三档「蠢度」。</summary>
    public sealed class OpponentInfo
    {
        public string Name;      // 英文名
        public string Chinese;   // 中文名
        public Difficulty Difficulty;
        public string Blurb;
        public int Portrait;     // 立绘种类索引
        public Color Accent;     // 主题色

        public string DifficultyText
        {
            get
            {
                switch (Difficulty)
                {
                    case Difficulty.Easy: return "简单";
                    case Difficulty.Normal: return "普通";
                    default: return "困难";
                }
            }
        }

        public static readonly OpponentInfo[] All =
        {
            new OpponentInfo
            {
                Name = "RATAU", Chinese = "拉陶", Difficulty = Difficulty.Easy,
                Blurb = "老友拉陶，出牌全凭心情，偶尔会漏看你的列。",
                Portrait = 0, Accent = Color.FromArgb(0xC9, 0xA2, 0x27)
            },
            new OpponentInfo
            {
                Name = "KLUNKO & BOP", Chinese = "克伦科与波普", Difficulty = Difficulty.Normal,
                Blurb = "双子商人，账算得很精，喜欢抢你的成对骰子。",
                Portrait = 1, Accent = Color.FromArgb(0x8F, 0xA8, 0x6B)
            },
            new OpponentInfo
            {
                Name = "SHRUMY", Chinese = "舒朗米", Difficulty = Difficulty.Hard,
                Blurb = "骨商舒朗米，从不失手，会提前算到你的下一步。",
                Portrait = 2, Accent = Color.FromArgb(0xC0, 0x39, 0x2B)
            },
        };
    }

    public static class Ai
    {
        public static int ChooseColumn(Board mine, Board theirs, int die, Difficulty difficulty, Random rng)
        {
            var options = new List<int>(3);
            for (int c = 0; c < Board.ColumnCount; c++)
                if (mine.CanPlace(c)) options.Add(c);
            if (options.Count == 0) return 0;
            if (options.Count == 1) return options[0];

            switch (difficulty)
            {
                case Difficulty.Easy:
                    // 拉陶：全凭心情，基本乱放
                    if (rng.NextDouble() < 0.92) return options[rng.Next(options.Count)];
                    return Greedy(mine, theirs, options, die);
                case Difficulty.Normal:
                    if (rng.NextDouble() < 0.15) return options[rng.Next(options.Count)];
                    return Greedy(mine, theirs, options, die);
                default:
                    return Lookahead(mine, theirs, options, die);
            }
        }

        /// <summary>即时收益：自己涨的分 + 对手掉的分。</summary>
        private static double Immediate(Board mine, Board theirs, int column, int die)
        {
            int before = mine.ColumnScore(column) + theirs.ColumnScore(column);

            var mineAfter = new List<int>(mine.Columns[column]) { die };
            var theirsAfter = new List<int>(theirs.Columns[column]);
            theirsAfter.RemoveAll(v => v == die);

            int after = Board.ScoreOf(mineAfter) + Board.ScoreOf(theirsAfter);
            return after - before;
        }

        private static int Greedy(Board mine, Board theirs, List<int> options, int die)
        {
            int best = options[0];
            double bestScore = double.NegativeInfinity;
            foreach (int c in options)
            {
                double score = Immediate(mine, theirs, c, die);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        /// <summary>贪心 + 一步预判：好位置还要考虑对手下一手能从我这里拿走多少。</summary>
        private static int Lookahead(Board mine, Board theirs, List<int> options, int die)
        {
            int best = options[0];
            double bestScore = double.NegativeInfinity;
            foreach (int c in options)
            {
                var mineAfter = mine.Clone();
                mineAfter.Add(c, die);
                var theirsAfter = theirs.Clone();
                theirsAfter.Columns[c].RemoveAll(v => v == die);

                double gain = Immediate(mine, theirs, c, die);
                double threat = AverageThreat(theirsAfter, mineAfter);
                double score = gain - threat * 0.30;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        /// <summary>对手拿各种点数时，一步能拿到的平均收益（含摧毁我的骰子）。</summary>
        private static double AverageThreat(Board them, Board me)
        {
            double total = 0;
            for (int die = 1; die <= 6; die++)
            {
                double best = 0;
                for (int c = 0; c < Board.ColumnCount; c++)
                {
                    if (!them.CanPlace(c)) continue;
                    double v = Immediate(them, me, c, die);
                    if (v > best) best = v;
                }
                total += best;
            }
            return total / 6.0;
        }
    }
}
