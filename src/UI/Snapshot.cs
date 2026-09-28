using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Nibble.UI
{
    // Design snapshots: a window rendered over a wallpaper with even padding, saved as a PNG with
    // rounded corners. Every panel uses the same padding and radius so the README reads as one set.
    static class Snapshot
    {
        const float Pad = 48, Radius = 26;

        // The wallpaper canvas for a window of this size; origin is where the window goes.
        public static Bitmap Backdrop(Size window, float scale, bool dark, out Point origin)
        {
            int pad = (int)Math.Round(Pad * scale);
            origin = new Point(pad, pad);
            return Wallpaper.For(dark, new Size(window.Width + 2 * pad, window.Height + 2 * pad));
        }

        public static void Save(Bitmap canvas, Bitmap window, Point origin, string path, float scale)
        {
            using (var g = Graphics.FromImage(canvas)) g.DrawImageUnscaled(window, origin.X, origin.Y);
            using (var rounded = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(rounded))
                using (var texture = new TextureBrush(canvas))
                using (var shape = Draw.Round(new RectangleF(0, 0, canvas.Width, canvas.Height), Radius * scale))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(texture, shape);
                }
                rounded.Save(path, ImageFormat.Png);
            }
        }
    }
}
