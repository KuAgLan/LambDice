using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LambDice.Ui
{
    /// <summary>自绘输入框（IP / 端口），支持光标、退格、回车提交。</summary>
    public sealed class TextField
    {
        public string Text = "";
        public string Placeholder = "";
        public int MaxLength = 20;
        public bool Focused;
        public bool OnlyDigits;

        private float caret;

        public TextField(string text, string placeholder)
        {
            Text = text;
            Placeholder = placeholder;
        }

        /// <summary>处理输入；返回 true 表示按了回车。</summary>
        public bool Update(App app, RectangleF rect)
        {
            caret += 1f / 60f;

            if (app.MouseClicked)
            {
                bool hit = rect.Contains(app.Mouse);
                if (hit && !Focused) caret = 0f;
                Focused = hit;
            }

            if (!Focused) return false;

            foreach (char c in app.Typed)
            {
                if (OnlyDigits && !char.IsDigit(c)) continue;
                if (!OnlyDigits && !(char.IsDigit(c) || c == '.' || c == '-' || char.IsLetter(c))) continue;
                if (Text.Length >= MaxLength) break;
                Text += c;
                caret = 0f;
            }

            bool submit = false;
            foreach (var k in app.Pressed)
            {
                if (k == Keys.Back)
                {
                    if (Text.Length > 0) Text = Text.Substring(0, Text.Length - 1);
                    caret = 0f;
                }
                else if (k == Keys.Enter) submit = true;
                else if (k == Keys.Escape) Focused = false;
            }
            return submit;
        }

        public void Draw(Graphics g, RectangleF rect)
        {
            Color border = Focused ? Theme.Gold : Color.FromArgb(150, Theme.Gold);
            using (var path = Theme.RoundRect(rect, 8))
            {
                using (var brush = new SolidBrush(Color.FromArgb(235, 0x1A, 0x16, 0x12)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(Focused ? 230 : 120, border), Focused ? 2.4f : 1.6f))
                    g.DrawPath(pen, path);
            }

            string shown = string.IsNullOrEmpty(Text) ? Placeholder : Text;
            Color color = string.IsNullOrEmpty(Text) ? Color.FromArgb(90, 0xD8, 0xCF, 0xB8) : Theme.Parchment;
            var font = Theme.Font(24f);
            Theme.Text(g, shown, font, color, new RectangleF(rect.X + 16, rect.Y, rect.Width - 32, rect.Height),
                StringAlignment.Near, StringAlignment.Center);

            if (Focused && (int)(caret * 2f) % 2 == 0)
            {
                float width = string.IsNullOrEmpty(Text) ? 0 : g.MeasureString(Text, font).Width;
                float x = rect.X + 16 + width + 2;
                using (var pen = new Pen(Theme.Gold, 2f))
                    g.DrawLine(pen, x, rect.Y + rect.Height * 0.26f, x, rect.Bottom - rect.Height * 0.26f);
            }
        }

        public int Number(int fallback, int min, int max)
        {
            int v;
            if (!int.TryParse(Text, out v)) return fallback;
            return Math.Max(min, Math.Min(max, v));
        }
    }
}
