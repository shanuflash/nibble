using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Nibble
{
    // On-screen DPI indicator: a glass pill at the bottom centre, like the system volume display.
    // Click-through and never focused, so it's safe mid-game. Pressing again updates it in place.
    class DpiHud : Form
    {
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        const float HW = 236, HH = 60, M = 2;

        readonly float S;
        Theme th;
        readonly Font fNum, fUnit;
        Bitmap backdrop;
        Surface frame;
        Color bgAvg;
        int winX, winY;
        float t; int t0, shownAt; bool closing;
        readonly Timer anim;

        int dpi, stage, stages;
        Color led;

        static DpiHud current;

        public static void Show(int dpi, int stage, int stages, Color led)
        {
            int state;
            // Exclusive fullscreen and presentations can't be drawn over; skip rather than queue (it's transient).
            if (SHQueryUserNotificationState(out state) == 0 && (state == 3 || state == 4)) return;
            if (current == null || current.IsDisposed) current = new DpiHud();
            current.Update(dpi, stage, stages, led);
        }

        DpiHud()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            using (var g = CreateGraphics()) S = g.DpiX / 96f;
            Size = new Size(Px(HW + 2 * M), Px(HH + 2 * M));
            string disp = Draw.PickFont("Segoe UI Variable Display Semib", "Segoe UI Semibold");
            fNum = new Font(disp, 26 * S, FontStyle.Regular, GraphicsUnit.Pixel);
            fUnit = new Font(Draw.PickFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold"), 13 * S, FontStyle.Regular, GraphicsUnit.Pixel);
            anim = new Timer { Interval = 15 };
            anim.Tick += delegate { Tick(); };
        }

        int Px(float v) { return (int)Math.Round(v * S); }
        float F(float v) { return v * S; }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80 | 0x08000000 | 0x20; // LAYERED | TOOLWINDOW | NOACTIVATE | TRANSPARENT
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        void Update(int dpi, int stage, int stages, Color led)
        {
            this.dpi = dpi; this.stage = stage; this.stages = stages; this.led = led;
            int now = Environment.TickCount;
            if (!Visible || closing)
            {
                th = Theme.Current();
                var wa = Screen.PrimaryScreen.WorkingArea;
                winX = wa.X + (wa.Width - Size.Width) / 2;
                winY = wa.Bottom - Size.Height - Px(56);
                using (var cap = Glass.Capture(new Rectangle(winX + Px(M), winY + Px(M), Px(HW), Px(HH))))
                {
                    if (backdrop != null) backdrop.Dispose();
                    backdrop = Glass.Frost(cap, new Size(Px(HW), Px(HH)), Glass.AdaptiveTint(cap, th.Tint, th.Dark, 90, 175), th.Fallback, 1.8f);
                }
                bgAvg = Glass.Average(backdrop);
                if (frame == null)
                {
                    frame = new Surface(Size.Width, Size.Height);
                    frame.SetMask(new RectangleF(F(M), F(M), F(HW), F(HH)), F(HH / 2));
                }
                closing = false;
                t = 0; t0 = now;
                Location = new Point(winX, winY);
                Render();
                Show();
            }
            shownAt = now;
            anim.Start();
            Render();
        }

        void Tick()
        {
            int now = Environment.TickCount;
            if (closing)
            {
                t = 1 - Math.Min(1, (now - t0) / 220f);
                if (t <= 0) { anim.Stop(); Hide(); FreeSurfaces(); return; }
            }
            else
            {
                float p = Math.Min(1, (now - t0) / 160f);
                t = 1 - (float)Math.Pow(1 - p, 3);
                if (p >= 1 && now - shownAt > 1300) { closing = true; t0 = now; }
            }
            Render();
        }

        void FreeSurfaces()
        {
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
        }

        void Render()
        {
            if (frame == null || backdrop == null || !IsHandleCreated) return;
            using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
            frame.ApplyMask();
            // Rises a few pixels as it fades in.
            Layered.Push(Handle, frame, winX, winY + (int)Math.Round((1 - t) * F(8)), (byte)(255 * Math.Max(0, Math.Min(1, t))));
        }

        void Compose(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var pill = new RectangleF(F(M), F(M), F(HW), F(HH));
            g.DrawImageUnscaled(backdrop, Px(M), Px(M));
            using (var sheen = new LinearGradientBrush(pill, Color.FromArgb(th.Dark ? 20 : 60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            {
                sheen.SetBlendTriangularShape(0f, 1f);
                g.FillRectangle(sheen, pill);
            }
            Draw.Rim(g, pill, F(HH / 2), th.RimTop, th.RimBottom, Math.Max(1f, F(1)));

            // Stage LED colour, as a glowing dot.
            float cy = pill.Y + pill.Height / 2, dx = pill.X + F(26);
            using (var glow = new SolidBrush(Color.FromArgb(70, led))) g.FillEllipse(glow, dx - F(9), cy - F(9), F(18), F(18));
            using (var b = new SolidBrush(Color.FromArgb(255, led))) g.FillEllipse(b, dx - F(5), cy - F(5), F(10), F(10));

            // Stage dots on the right (only when there's more than one stage).
            float right = pill.Right - F(24);
            if (stages > 1)
            {
                float gap = F(10), d = F(6.5f);
                float x0 = right - (stages - 1) * gap - d;
                for (int i = 1; i <= stages; i++)
                {
                    var r = new RectangleF(x0 + (i - 1) * gap, cy - d / 2, d, d);
                    using (var b = new SolidBrush(i == stage ? th.Label : Draw.Alpha(th.Label, 0.28f))) g.FillEllipse(b, r);
                }
            }

            g.Flush(FlushIntention.Sync);
            string num = dpi.ToString();
            var ns = frame.Measure(num, fNum);
            float tx = pill.X + F(46);
            frame.Text(num, fNum, Flat(th.Label), Rectangle.Round(new RectangleF(tx, cy - F(15), ns.Width + F(4), F(30))), 0);
            frame.Text("DPI", fUnit, Flat(th.Secondary), Rectangle.Round(new RectangleF(tx + ns.Width + F(6), cy - F(6), F(40), F(18))), 0);
        }

        Color Flat(Color c)
        {
            float k = c.A / 255f;
            return Color.FromArgb(255, (int)(bgAvg.R + (c.R - bgAvg.R) * k), (int)(bgAvg.G + (c.G - bgAvg.G) * k), (int)(bgAvg.B + (c.B - bgAvg.B) * k));
        }
    }
}
