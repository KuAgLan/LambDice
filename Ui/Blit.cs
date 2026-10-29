using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LambDice.Ui
{
    /// <summary>
    /// 把位图按设计坐标贴到已经缩放过的 Graphics 上。
    /// 关键：先换算到设备像素、取整、再 ResetTransform 贴图——否则小数坐标会让 GDI+
    /// 对每张图做一次重采样，帧率直接掉一半。
    /// </summary>
    public static class Blit
    {
        public static void Image(Graphics g, Bitmap bmp, RectangleF rect)
        {
            if (bmp == null) return;

            float[] e = g.Transform.Elements;   // [0]=M11 [1]=M12 [2]=M21 [3]=M22 [4]=DX [5]=DY
            float sx = e[0] <= 0.0001f ? 1f : e[0];
            float sy = e[3] <= 0.0001f ? 1f : e[3];

            float dx = e[4] + rect.X * sx;
            float dy = e[5] + rect.Y * sy;
            float dw = rect.Width * sx;
            float dh = rect.Height * sy;

            var state = g.Save();
            g.ResetTransform();
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;   // 设备像素已对齐，无需插值
            g.DrawImage(bmp, new Rectangle(
                (int)Math.Round(dx), (int)Math.Round(dy),
                Math.Max(1, (int)Math.Round(dw)), Math.Max(1, (int)Math.Round(dh))));
            g.Restore(state);
        }
    }
}
