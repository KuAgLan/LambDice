using System;
using System.Collections.Generic;
using System.Linq;

namespace LambDice.Game
{
    public enum Side { Player = 0, Opponent = 1 }

    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>一方棋盘：3 列，每列最多 3 颗。列内顺序 = 从靠中线的一排往自己这边堆。</summary>
    public sealed class Board
    {
        public const int ColumnCount = 3;
        public const int ColumnHeight = 3;

        public readonly List<int>[] Columns =
        {
            new List<int>(3), new List<int>(3), new List<int>(3)
        };

        public int Count => Columns[0].Count + Columns[1].Count + Columns[2].Count;
        public bool IsFull => Count >= ColumnCount * ColumnHeight;

        public bool CanPlace(int column)
        {
            return column >= 0 && column < ColumnCount && Columns[column].Count < ColumnHeight;
        }

        /// <summary>单列得分 = Σ 点数 × 该点数颗数²</summary>
        public static int ScoreOf(IEnumerable<int> dice)
        {
            int total = 0;
            var counts = new int[7];
            foreach (int v in dice)
            {
                if (v >= 1 && v <= 6) counts[v]++;
            }
            for (int v = 1; v <= 6; v++)
            {
                if (counts[v] > 0) total += v * counts[v] * counts[v];
            }
            return total;
        }

        public int ColumnScore(int column) => ScoreOf(Columns[column]);

        public int Total => ColumnScore(0) + ColumnScore(1) + ColumnScore(2);

        /// <summary>放下骰子，返回它在列中的位置索引。</summary>
        public int Add(int column, int value)
        {
            Columns[column].Add(value);
            return Columns[column].Count - 1;
        }

        /// <summary>摧毁对方同列中所有相同点数的骰子，返回被摧毁的点数集合（按位置）。</summary>
        public List<int> Destroy(Board other, int column, int value)
        {
            var removedValues = other.Columns[column].Where(v => v == value).ToList();
            other.Columns[column].RemoveAll(v => v == value);
            return removedValues;
        }

        public Board Clone()
        {
            var b = new Board();
            for (int c = 0; c < ColumnCount; c++) b.Columns[c].AddRange(Columns[c]);
            return b;
        }
    }

    public sealed class Match
    {
        public readonly Board Player = new Board();
        public readonly Board Opponent = new Board();
        public readonly Random Rng;

        public Side Turn;
        public int Die;
        public bool Rolled;

        public Match(int seed)
        {
            Rng = new Random(seed);
            Turn = Rng.Next(2) == 0 ? Side.Player : Side.Opponent;
        }

        /// <summary>任意一方先把自己的三列填满，对局立刻结束。</summary>
        public bool Over => Player.IsFull || Opponent.IsFull;

        public int PlayerScore => Player.Total;
        public int OpponentScore => Opponent.Total;

        /// <summary>1 = 玩家胜，-1 = 对手胜，0 = 平局</summary>
        public int Outcome
        {
            get
            {
                if (Player.Total > Opponent.Total) return 1;
                if (Opponent.Total > Player.Total) return -1;
                return 0;
            }
        }

        public Board BoardOf(Side side) => side == Side.Player ? Player : Opponent;
        public Board BoardOfOther(Side side) => side == Side.Player ? Opponent : Player;

        public int Roll()
        {
            Die = Rng.Next(1, 7);
            Rolled = true;
            return Die;
        }

        /// <summary>把当前骰子放进本回合方的某一列。返回实际销毁的骰子数。</summary>
        public int Place(int column)
        {
            if (!Rolled) throw new InvalidOperationException("还没有投骰");
            Board mine = BoardOf(Turn);
            Board theirs = BoardOfOther(Turn);
            if (!mine.CanPlace(column)) return 0;

            mine.Add(column, Die);
            int destroyed = 0;
            for (int i = theirs.Columns[column].Count - 1; i >= 0; i--)
            {
                if (theirs.Columns[column][i] == Die)
                {
                    theirs.Columns[column].RemoveAt(i);
                    destroyed++;
                }
            }

            Die = 0;
            Rolled = false;
            if (!Over) Turn = Turn == Side.Player ? Side.Opponent : Side.Player;
            return destroyed;
        }

        /// <summary>用于 AI 推演：临时落子并返回收益。</summary>
        public static int SimulateGain(Board mine, Board theirs, int column, int die)
        {
            int before = mine.ColumnScore(column) + theirs.ColumnScore(column);
            var after = new List<int>(mine.Columns[column]) { die };
            var theirAfter = new List<int>(theirs.Columns[column]);
            theirAfter.RemoveAll(v => v == die);
            return (Board.ScoreOf(after) + Board.ScoreOf(theirAfter)) - before;
        }
    }
}
