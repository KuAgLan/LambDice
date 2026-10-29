using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LambDice.Ui;

namespace LambDice.Screens
{
    public abstract class Screen
    {
        protected readonly App App;

        private struct Leaf
        {
            public float X, Y, Size, Angle, Spin, Speed, Alpha;
        }

        private readonly List<Leaf> leaves = new List<Leaf>();
        private readonly Random leafRng = new Random(991);

        protected Screen(App app)
        {
            App = app;
        }

        public virtual void OnEnter() { }

        public abstract void Update(float dt);

        /// <summary>界面层（设计坐标 1600×900，画布透明）。</summary>
        public abstract void Render(Graphics g);

        /// <summary>通栏层：画在界面下面，screen = 窗口在设计坐标里的范围（可超出 1600×900）。</summary>
        public virtual void RenderBehind(Graphics g, RectangleF screen) { }

        /// <summary>覆盖层：画在界面上面（结算遮罩等）。</summary>
        public virtual void RenderOver(Graphics g, RectangleF screen) { }

        protected void InitLeaves(int count)
        {
            leaves.Clear();
            for (int i = 0; i < count; i++)
            {
                leaves.Add(new Leaf
                {
                    X = leafRng.Next(0, Theme.Width),
                    Y = leafRng.Next(0, Theme.Height),
                    Size = leafRng.Next(26, 62),
                    Angle = leafRng.Next(0, 360),
                    Spin = (float)(leafRng.NextDouble() * 8 - 4),
                    Speed = (float)(leafRng.NextDouble() * 6 + 2),
                    Alpha = (float)(leafRng.NextDouble() * 0.35 + 0.10)
                });
            }
        }

        protected void UpdateLeaves(float dt)
        {
            for (int i = 0; i < leaves.Count; i++)
            {
                Leaf l = leaves[i];
                l.Y -= l.Speed * dt;
                l.Angle += l.Spin * dt;
                if (l.Y < -80) { l.Y = Theme.Height + 60; l.X = leafRng.Next(0, Theme.Width); }
                leaves[i] = l;
            }
        }

        /// <summary>背景由 App 铺满窗口，这里只画漂在界面层的符文。</summary>
        protected void DrawBackground(Graphics g)
        {
            foreach (var l in leaves)
                Art.RuneLeaf(g, new PointF(l.X, l.Y), l.Size, l.Angle, l.Alpha * 0.55f);
        }

        protected bool Hover(RectangleF r) => r.Contains(App.Mouse);

        protected bool Click(RectangleF r) => App.TakeClick(r);

        // ------------------------------------------------------------------ 控件

        protected bool RibbonButton(Graphics g, RectangleF r, string label, float fontSize, Action onClick)
        {
            bool hover = Hover(r);
            bool press = hover && App.MouseHeld;
            Art.Ribbon(g, r, hover ? 1f : 0f, press ? 1f : 0f);
            Theme.Text(g, label, Theme.Font(fontSize, true), Theme.Parchment, r, StringAlignment.Center, StringAlignment.Center, true);
            if (Click(r)) { Sfx.Play("click"); onClick(); return true; }
            return false;
        }

        protected bool TextButton(Graphics g, RectangleF r, string label, float fontSize, Action onClick, Color? color = null)
        {
            bool hover = Hover(r);
            Color baseColor = color ?? Theme.CreamDim;
            Color c = hover ? Theme.Parchment : baseColor;
            if (hover) c = Theme.Lerp(c, Color.White, 0.25f);
            Theme.Text(g, label, Theme.Font(fontSize, false), c, r, StringAlignment.Center, StringAlignment.Center, hover);
            if (Click(r)) { Sfx.Play("click"); onClick(); return true; }
            return false;
        }

        protected void KeyHint(Graphics g, string text, float y)
        {
            Theme.Text(g, text, Theme.Font(15f), Color.FromArgb(150, 0xC9, 0xBE, 0xA2),
                new RectangleF(0, y, Theme.Width, 26), StringAlignment.Center, StringAlignment.Center);
        }

        protected void FlourishPair(Graphics g, float cx, float y, float scale, Color color, float alpha)
        {
            Art.Flourish(g, new PointF(cx - 210 * scale, y), scale, false, color, alpha);
            Art.Flourish(g, new PointF(cx + 210 * scale, y), scale, true, color, alpha);
        }

        protected string[] ColumnLabel = { "一", "二", "三" };
    }
}
