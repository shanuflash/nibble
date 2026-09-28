using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Nibble.UI.Popups
{
    // A small frosted card in the tray corner that slides in from the right, holds, then fades out.
    // It never takes focus; click-through ones also let the mouse pass to whatever is underneath.
    abstract class GlassPopup : Form
    {
        const float Inset = 2, Gap = 12, Slide = 40;

        protected readonly float S;
        protected Theme Th { get; private set; }
        protected RectangleF Card { get { return new RectangleF(F(Inset), F(Inset), F(cardW), F(cardH)); } }

        readonly float cardW, cardH, radius;
        readonly bool clickThrough;
        readonly int inMs, holdMs, outMs;
        readonly Timer anim;
        Bitmap backdrop;
        Surface frame;
        Color under;
        int restX, winY;
        float t;                   // 0 hidden .. 1 fully in
        int t0, shownAt;
        bool closing;

        protected GlassPopup(float width, float height, float radius, bool clickThrough, int inMs, int holdMs, int outMs)
        {
            cardW = width; cardH = height; this.radius = radius;
            this.clickThrough = clickThrough;
            this.inMs = inMs; this.holdMs = holdMs; this.outMs = outMs;
            S = Draw.ScreenScale();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Px(width + 2 * Inset), Px(height + 2 * Inset));
            anim = new Timer { Interval = 15 };
            anim.Tick += delegate { Tick(); };
        }

        protected int Px(float v) { return (int)Math.Round(v * S); }
        protected float F(float v) { return v * S; }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80 | 0x08000000;   // LAYERED | TOOLWINDOW | NOACTIVATE
                if (clickThrough) cp.ExStyle |= 0x20;       // TRANSPARENT
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected bool Showing { get { return Visible && !closing; } }

        // Captures and frosts what's behind the corner, then slides in.
        protected void Open()
        {
            Th = Theme.Current();
            var size = new Size(Px(cardW), Px(cardH));
            var corner = Layered.Corner(Screen.PrimaryScreen, size, Px(Gap));
            restX = corner.X - Px(Inset);
            winY = corner.Y - Px(Inset);
            using (var cap = Glass.Capture(new Rectangle(corner, size)))
            {
                if (backdrop != null) backdrop.Dispose();
                backdrop = Glass.Frost(cap, size, Glass.AdaptiveTint(cap, Th.Tint, Th.Dark, 90, 175), Th.Fallback, 1.8f);
            }
            under = Glass.Average(backdrop);
            if (frame == null)
            {
                frame = new Surface(Size.Width, Size.Height);
                frame.SetMask(Card, F(radius));
            }
            closing = false;
            t = 0; t0 = shownAt = Environment.TickCount;
            Location = new Point(restX + Px(Slide), winY);
            Render();
            Show();
            anim.Start();
        }

        // Restarts the hold time, e.g. when the content changed.
        protected void Hold()
        {
            shownAt = Environment.TickCount;
            anim.Start();
        }

        protected void Dismiss()
        {
            if (closing) return;
            closing = true; t0 = Environment.TickCount;
            anim.Start();
        }

        protected virtual bool Paused { get { return false; } }
        protected abstract void PaintContent(Graphics g, RectangleF card);
        protected abstract void Finished();

        protected void DrawText(string s, Font f, Color c, RectangleF r)
        {
            frame.Text(s, f, Draw.Flatten(c, under), Rectangle.Round(r), TextAlign.Left);
        }

        protected SizeF Measure(string s, Font f) { return frame.Measure(s, f); }

        protected void FreeSurfaces()
        {
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
        }

        void Tick()
        {
            int now = Environment.TickCount;
            if (closing)
            {
                t = 1 - Math.Min(1, (now - t0) / (float)outMs);
                if (t <= 0) { anim.Stop(); Finished(); return; }
            }
            else
            {
                float p = Math.Min(1, (now - t0) / (float)inMs);
                t = 1 - (float)Math.Pow(1 - p, 3);
                if (Paused) shownAt = now;
                if (p >= 1 && now - shownAt > holdMs) Dismiss();
            }
            Render();
        }

        protected void Render()
        {
            if (frame == null || backdrop == null || !IsHandleCreated) return;
            using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
            frame.ApplyMask();
            Layered.Push(Handle, frame, restX + (int)Math.Round((1 - t) * F(Slide)), winY, (byte)(255 * Math.Max(0, Math.Min(1, t))));
        }

        void Compose(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = Card;
            g.DrawImageUnscaled(backdrop, Px(Inset), Px(Inset));
            using (var sheen = new LinearGradientBrush(card, Color.FromArgb(Th.Dark ? 20 : 60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            {
                sheen.SetBlendTriangularShape(0f, 1f);
                g.FillRectangle(sheen, card);
            }
            Draw.Rim(g, card, F(radius), Th.RimTop, Th.RimBottom, Math.Max(1f, F(1)));
            PaintContent(g, card);
        }

        // Renders the card fully shown over a wallpaper, for design review.
        public void Snapshot(string path, bool dark)
        {
            Th = Theme.Current(dark);
            var size = new Size(Px(cardW), Px(cardH));
            Point o;
            using (var wall = UI.Snapshot.Backdrop(Size, S, dark, out o))
            {
                using (var cap = wall.Clone(new Rectangle(o.X + Px(Inset), o.Y + Px(Inset), size.Width, size.Height), wall.PixelFormat))
                    backdrop = Glass.Frost(cap, size, Glass.AdaptiveTint(cap, Th.Tint, Th.Dark, 90, 175), Th.Fallback, 1.8f);
                under = Glass.Average(backdrop);
                frame = new Surface(Size.Width, Size.Height);
                frame.SetMask(Card, F(radius));
                t = 1;
                using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
                frame.ApplyMask();
                UI.Snapshot.Save(wall, frame.Bmp, o, path, S);
            }
            FreeSurfaces();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            anim.Stop();
            FreeSurfaces();
            base.OnFormClosed(e);
        }
    }
}
