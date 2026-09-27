using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Nibble
{
    // Nibble's own notification: a frosted glass card that slides in above the tray. It never takes focus.
    // Over a borderless/fullscreen-optimised game it can show click-through (input goes to the game);
    // exclusive fullscreen and presentations can't be drawn over, so it waits for those to end.
    class Toast : Form
    {
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

        const float TW = 356, TH = 76, TR = 22, M = 2;

        public enum Kind { Low, Full }

        readonly Kind kind;
        readonly string title, body;
        readonly Action onClick;
        readonly bool clickThrough;
        readonly float S;
        readonly Theme th;
        readonly Font fTitle, fBody;
        Bitmap backdrop;
        Surface frame;
        Color bgAvg;
        int restX, winY;
        float t;                   // 0 hidden .. 1 fully in
        int t0, shownAt;
        bool closing, hovering;
        readonly Timer anim;

        static Toast current;
        static Toast pending;
        static Timer waitTimer;

        // Shows now (click-through over a game when allowed), or once the user is out of an exclusive
        // fullscreen game or presentation.
        public static void Show(Kind kind, string title, string body, Action onClick, bool overGames)
        {
            int state = State();
            bool game = state == QUNS_BUSY;
            bool mustWait = state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE || (game && !overGames);
            var toast = new Toast(kind, title, body, onClick, game && overGames);
            if (mustWait)
            {
                if (pending != null) pending.Dispose();
                pending = toast;
                if (waitTimer == null)
                {
                    waitTimer = new Timer { Interval = 15000 };
                    waitTimer.Tick += delegate
                    {
                        int s = State();
                        if (pending == null || s == QUNS_RUNNING_D3D_FULL_SCREEN || s == QUNS_PRESENTATION_MODE || s == QUNS_BUSY) return;
                        waitTimer.Stop();
                        var p = pending; pending = null;
                        p.Present();
                    };
                }
                waitTimer.Start();
                return;
            }
            toast.Present();
        }

        static int State()
        {
            int s;
            try { if (SHQueryUserNotificationState(out s) == 0) return s; }
            catch { }
            return 5; // accepts notifications
        }

        Toast(Kind kind, string title, string body, Action onClick, bool clickThrough)
        {
            this.kind = kind; this.title = title; this.body = body; this.onClick = onClick; this.clickThrough = clickThrough;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            using (var g = CreateGraphics()) S = g.DpiX / 96f;
            Size = new Size(Px(TW + 2 * M), Px(TH + 2 * M));
            th = Theme.Current();
            fTitle = new Font(Draw.PickFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold"), 15 * S, FontStyle.Regular, GraphicsUnit.Pixel);
            fBody = new Font(Draw.PickFont("Segoe UI Variable Text", "Segoe UI"), 13 * S, FontStyle.Regular, GraphicsUnit.Pixel);
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
                cp.ExStyle |= 0x80000 | 0x80 | 0x08000000; // LAYERED | TOOLWINDOW | NOACTIVATE
                if (clickThrough) cp.ExStyle |= 0x20;     // TRANSPARENT: clicks and aim go to the game underneath
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        void Present()
        {
            if (current != null) current.Dismiss();
            current = this;
            var wa = Screen.PrimaryScreen.WorkingArea;
            int gap = Px(12);
            restX = wa.Right - Px(TW) - gap - Px(M);
            winY = wa.Bottom - Px(TH) - gap - Px(M);
            using (var cap = Glass.Capture(new Rectangle(restX + Px(M), winY + Px(M), Px(TW), Px(TH))))
                backdrop = Glass.Frost(cap, new Size(Px(TW), Px(TH)), th.Tint, th.Fallback, 1.8f);
            bgAvg = Glass.Average(backdrop);
            frame = new Surface(Size.Width, Size.Height);
            frame.SetMask(new RectangleF(F(M), F(M), F(TW), F(TH)), F(TR));
            Location = new Point(restX + Px(40), winY);
            t = 0; t0 = Environment.TickCount; shownAt = t0;
            Render();
            Show();
            anim.Start();
        }

        void Dismiss()
        {
            if (closing) return;
            closing = true; t0 = Environment.TickCount;
            anim.Start();
        }

        void Tick()
        {
            int now = Environment.TickCount;
            if (closing)
            {
                t = 1 - Math.Min(1, (now - t0) / 180f);
                if (t <= 0) { anim.Stop(); Close(); return; }
            }
            else
            {
                float p = Math.Min(1, (now - t0) / 280f);
                t = 1 - (float)Math.Pow(1 - p, 3);
                if (hovering) shownAt = now;                        // hover holds it on screen
                if (p >= 1 && now - shownAt > 6000) Dismiss();
            }
            Render();
        }

        void Render()
        {
            if (frame == null || !IsHandleCreated) return;
            using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
            frame.ApplyMask();
            Layered.Push(Handle, frame, restX + (int)Math.Round((1 - t) * F(40)), winY, (byte)(255 * Math.Max(0, Math.Min(1, t))));
        }

        void Compose(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new RectangleF(F(M), F(M), F(TW), F(TH));
            g.DrawImageUnscaled(backdrop, Px(M), Px(M));
            // Same sheen as the flyout: light falling across the top of the glass.
            using (var sheen = new LinearGradientBrush(card, Color.FromArgb(th.Dark ? 20 : 60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            {
                sheen.SetBlendTriangularShape(0f, 1f);
                g.FillRectangle(sheen, card);
            }
            Draw.Rim(g, card, F(TR), th.RimTop, th.RimBottom, Math.Max(1f, F(1)));

            // Icon tile: red battery for low, green bolt for fully charged.
            var tile = new RectangleF(card.X + F(16), card.Y + F(16), F(44), F(44));
            Color c = kind == Kind.Low ? th.Red : th.Green;
            using (var path = Draw.Round(tile, F(12)))
            using (var br = new LinearGradientBrush(tile, Draw.Lerp(c, Color.White, 0.2f), Draw.Lerp(c, Color.Black, 0.08f), 90f))
                g.FillPath(br, path);
            Draw.Rim(g, tile, F(12), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Math.Max(1f, F(0.8f)));
            Icons.Fill(g, kind == Kind.Low ? 2 : 1, new PointF(tile.X + tile.Width / 2, tile.Y + tile.Height / 2), F(26), Color.White);

            g.Flush(FlushIntention.Sync);
            float x = tile.Right + F(14), w = card.Right - x - F(16);
            frame.Text(title, fTitle, Flat(th.Label), Rectangle.Round(new RectangleF(x, card.Y + F(17), w, F(20))), 0);
            frame.Text(body, fBody, Flat(th.Secondary), Rectangle.Round(new RectangleF(x, card.Y + F(39), w, F(18))), 0);
        }

        Color Flat(Color c)
        {
            float k = c.A / 255f;
            return Color.FromArgb(255, (int)(bgAvg.R + (c.R - bgAvg.R) * k), (int)(bgAvg.G + (c.G - bgAvg.G) * k), (int)(bgAvg.B + (c.B - bgAvg.B) * k));
        }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Dismiss();
            if (e.Button == MouseButtons.Left && onClick != null) onClick();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            anim.Stop();
            if (current == this) current = null;
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            fTitle.Dispose(); fBody.Dispose();
            base.OnFormClosed(e);
        }
    }
}
