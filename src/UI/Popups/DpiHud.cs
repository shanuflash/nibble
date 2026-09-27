using System.Drawing;
using System.Drawing.Drawing2D;
using Nibble.Platform;

namespace Nibble.UI.Popups
{
    // Shows the new DPI when the mouse's DPI button switches stage. Click-through, so it's safe mid-game;
    // another press updates it in place.
    sealed class DpiHud : GlassPopup
    {
        const float W = 236, H = 60;

        static DpiHud current;

        readonly Font fNum, fUnit;
        int dpi, stage, stages;
        Color led;

        public static void Show(int dpi, int stage, int stages, Color led)
        {
            // Can't draw over exclusive fullscreen; skip rather than queue, it's only momentary.
            if (Shell.CurrentScreenMode() == ScreenMode.Exclusive) return;
            if (current == null || current.IsDisposed) current = new DpiHud();
            current.Present(dpi, stage, stages, led);
        }

        DpiHud() : base(W, H, H / 2, true, 160, 1300, 220)
        {
            fNum = new Font(Draw.PickFont("Segoe UI Variable Display Semib", "Segoe UI Semibold"), 26 * S, FontStyle.Regular, GraphicsUnit.Pixel);
            fUnit = new Font(Draw.PickFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold"), 13 * S, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        void Present(int dpi, int stage, int stages, Color led)
        {
            this.dpi = dpi; this.stage = stage; this.stages = stages; this.led = led;
            if (!Showing) Open();
            Hold();
            Render();
        }

        protected override void PaintContent(Graphics g, RectangleF pill)
        {
            float cy = pill.Y + pill.Height / 2, dx = pill.X + F(26);
            using (var glow = new SolidBrush(Color.FromArgb(70, led))) g.FillEllipse(glow, dx - F(9), cy - F(9), F(18), F(18));
            using (var b = new SolidBrush(Color.FromArgb(255, led))) g.FillEllipse(b, dx - F(5), cy - F(5), F(10), F(10));

            if (stages > 1)
            {
                float gap = F(10), d = F(6.5f);
                float x0 = pill.Right - F(24) - (stages - 1) * gap - d;
                for (int i = 1; i <= stages; i++)
                {
                    var r = new RectangleF(x0 + (i - 1) * gap, cy - d / 2, d, d);
                    using (var b = new SolidBrush(i == stage ? Th.Label : Draw.Alpha(Th.Label, 0.28f))) g.FillEllipse(b, r);
                }
            }

            g.Flush(FlushIntention.Sync);
            string num = dpi.ToString();
            var ns = Measure(num, fNum);
            float tx = pill.X + F(46);
            DrawText(num, fNum, Th.Label, new RectangleF(tx, cy - F(15), ns.Width + F(4), F(30)));
            DrawText("DPI", fUnit, Th.Secondary, new RectangleF(tx + ns.Width + F(6), cy - F(6), F(40), F(18)));
        }

        protected override void Finished()
        {
            Hide();
            FreeSurfaces();
        }
    }
}
