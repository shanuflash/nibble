using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Nibble.UI
{
    // A floating, draggable glass card window centred on screen. What's behind the card is frosted on
    // open and after each move. Subclasses paint the card through Canvas and get hit testing for free.
    abstract class GlassCard : Form
    {
        const float M = 2;

        protected readonly Canvas C;
        protected Theme Th { get; private set; }
        protected readonly float CardW, CardH;

        readonly float radius;
        readonly Timer ticker, clock;
        Bitmap glass;              // frosted snapshot of what's behind the card, tint included
        Surface frame;
        RectangleF card;
        int winX, winY;
        bool moving, dragging;
        int moveDX, moveDY;
        float fade; int fadeT0; bool closing;

        protected GlassCard(float width, float height, float radius)
        {
            CardW = width; CardH = height; this.radius = radius;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            KeyPreview = true;
            Text = AppInfo.Name;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            // Shrink to fit small screens.
            var wa = Screen.PrimaryScreen.Bounds;
            C = new Canvas(Math.Min(Draw.ScreenScale(), Math.Min(wa.Width * 0.94f / width, wa.Height * 0.9f / height)));
            C.Animate = Kick;

            ticker = new Timer { Interval = 15 };
            ticker.Tick += delegate { Tick(); };
            clock = new Timer { Interval = 1000 };
            clock.Tick += delegate { Render(); };
        }

        protected float F(float v) { return C.F(v); }
        protected int Pi(float v) { return C.Pi(v); }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000;   // WS_EX_LAYERED
                return cp;
            }
        }

        // ---------- for subclasses ----------

        protected abstract Color CardTint(bool dark);
        protected abstract void PaintCard();                // translated to the card's origin
        protected virtual void Opening() { }                // reset per-open state
        protected virtual void GlassBuilt() { }              // C.CardFlat is fresh
        protected virtual bool Step(int now) { return false; }   // extra animation; true while moving
        protected virtual bool IsDragZone(float x, float y) { return true; }   // empty card space, card coordinates
        protected virtual bool BeginDrag(string hitId, float x) { return false; }
        protected virtual void DragTo(float x) { }
        protected virtual void EndDrag() { }

        // ---------- lifecycle ----------

        public void ShowOn(Screen screen)
        {
            if (Visible) { Activate(); return; }
            Th = Theme.Current();
            var wa = screen.WorkingArea;
            var size = new Size(Pi(CardW + 2 * M), Pi(CardH + 2 * M));
            winX = wa.X + (wa.Width - size.Width) / 2;
            winY = wa.Y + (wa.Height - size.Height) / 2;
            Bounds = new Rectangle(winX, winY, size.Width, size.Height);
            CreateFrame(size);
            using (var cap = Glass.Capture(CardScreenRect())) BuildGlass(cap);

            Opening();
            C.ResetSprings();
            closing = false; fade = 0; fadeT0 = Environment.TickCount;
            Render();
            Show();
            Activate();
            clock.Start();
            Kick();
        }

        void CreateFrame(Size size)
        {
            card = new RectangleF(F(M), F(M), F(CardW), F(CardH));
            frame = new Surface(size.Width, size.Height);
            frame.SetMask(card, F(radius));
        }

        Rectangle CardScreenRect() { return new Rectangle(winX + Pi(M), winY + Pi(M), Pi(CardW), Pi(CardH)); }

        void BuildGlass(Bitmap capture)
        {
            if (glass != null) glass.Dispose();
            glass = Glass.Frost(capture, new Size(Pi(CardW), Pi(CardH)), Glass.AdaptiveTint(capture, CardTint(Th.Dark), Th.Dark, 130, 215), Th.Fallback, 1.6f);
            C.CardFlat = Glass.Average(glass);
            GlassBuilt();
        }

        // Re-frost after the card moved or the desktop behind it changed. The window hides itself from
        // screen capture for a moment so it can see what's underneath.
        void Refrost()
        {
            if (frame == null) return;
            using (var cap = Glass.CaptureBehind(Handle, CardScreenRect()))
                if (cap != null) BuildGlass(cap);
            Render();
        }

        // Call when app state changes; re-tints the glass if the appearance flipped.
        protected void AppChanged()
        {
            if (!Visible) return;
            if (Th != null && Th.Dark != Theme.Current().Dark) { Th = Theme.Current(); Refrost(); }
            Kick();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (frame != null && !closing && fade >= 1) Refrost();
        }

        protected void Dismiss()
        {
            if (closing) return;
            closing = true; fadeT0 = Environment.TickCount;
            Kick();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ticker.Stop(); clock.Stop();
            if (glass != null) { glass.Dispose(); glass = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Dismiss();
            base.OnKeyDown(e);
        }

        // ---------- animation and painting ----------

        protected void Kick() { if (!ticker.Enabled) ticker.Start(); }

        void Tick()
        {
            bool more = false;
            int now = Environment.TickCount;

            float fp = Math.Min(1, (now - fadeT0) / (closing ? 140f : 200f));
            fade = closing ? 1 - fp : 1 - (float)Math.Pow(1 - fp, 3);
            if (fp < 1) more = true;
            else if (closing) { ticker.Stop(); Close(); return; }

            if (Step(now)) more = true;
            if (C.StepSprings()) more = true;
            if (closing) more = true;   // Step may have just dismissed the card

            Render();
            if (!more) ticker.Stop();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected void Render()
        {
            if (frame == null || glass == null || !IsHandleCreated) return;
            using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
            frame.ApplyMask();
            Layered.Push(Handle, frame, winX, winY, (byte)(255 * Math.Max(0, Math.Min(1, fade))));
        }

        void Compose(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            C.G = g; C.Frame = frame; C.Th = Th;
            C.Hits.Clear();

            // The rounded corners come from the surface mask; the frosted glass already carries the tint.
            g.DrawImageUnscaled(glass, Pi(M), Pi(M));
            Draw.Rim(g, card, F(radius), Th.RimTop, Th.RimBottom, Math.Max(1f, F(1)));

            var st = g.Save();
            g.TranslateTransform(card.X, card.Y);
            C.Hits.Offset = card.Location;
            PaintCard();
            g.Restore(st);
        }

        // Close button in the card's top-right corner.
        protected void PaintClose(float right, float top)
        {
            var close = new RectangleF(F(right - 32), F(top), F(32), F(32));
            C.Circle(close, "close");
            using (var p = C.StrokePen(Th.Secondary, 1.8f))
            {
                float k = F(5.5f), cx = close.X + close.Width / 2, cy = close.Y + close.Height / 2;
                C.G.DrawLine(p, cx - k, cy - k, cx + k, cy + k);
                C.G.DrawLine(p, cx - k, cy + k, cx + k, cy - k);
            }
            C.Hit("close", close, Dismiss);
        }

        // ---------- input ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (moving)
            {
                var s = Cursor.Position;
                winX = s.X - moveDX; winY = s.Y - moveDY;
                Layered.Push(Handle, frame, winX, winY, 255);
                return;
            }
            if (dragging) { DragTo(e.X - card.X); Render(); return; }
            var h = C.Hits.At(e.Location);
            string id = h == null ? null : h.Id;
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            if (id != C.Hover) { C.Hover = id; Render(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var h = C.Hits.At(e.Location);
            C.Pressed = h == null ? null : h.Id;
            if (h != null && BeginDrag(h.Id, e.X - card.X)) { dragging = true; Capture = true; }
            else if (h == null && IsDragZone(e.X - card.X, e.Y - card.Y))
            {
                var s = Cursor.Position;
                moving = true; moveDX = s.X - winX; moveDY = s.Y - winY; Capture = true;
                return;
            }
            Render();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (moving)
            {
                moving = false; Capture = false;
                Location = new Point(winX, winY);
                Refrost();
                return;
            }
            if (dragging)
            {
                dragging = false; Capture = false;
                EndDrag();
                C.Pressed = null; Render();
                return;
            }
            var h = C.Hits.At(e.Location);
            string p = C.Pressed;
            C.Pressed = null;
            if (e.Button == MouseButtons.Left && h != null && h.Id == p) h.Invoke();
            Kick();
        }

        protected void CancelDrag() { dragging = false; }

        // ---------- design review ----------

        protected void SnapshotTo(string path, bool dark)
        {
            Th = Theme.Current(dark);
            var size = new Size(Pi(CardW + 2 * M), Pi(CardH + 2 * M));
            Point o;
            using (var wall = Snapshot.Backdrop(size, C.U, dark, out o))
            {
                CreateFrame(size);
                using (var cap = wall.Clone(new Rectangle(o.X + Pi(M), o.Y + Pi(M), Pi(CardW), Pi(CardH)), wall.PixelFormat)) BuildGlass(cap);
                fade = 1;
                using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
                frame.ApplyMask();
                Snapshot.Save(wall, frame.Bmp, o, path, C.U);
            }
            frame.Dispose(); frame = null;
            glass.Dispose(); glass = null;
        }
    }
}
