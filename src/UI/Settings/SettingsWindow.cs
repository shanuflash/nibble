using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Nibble.UI.Settings
{
    // A floating glass card with a sidebar of pages. What's behind the card is frosted on open and
    // after each move. It only exists while open.
    sealed class SettingsWindow : Form
    {
        const float CW = 980, CH = 640, CR = 30, SideW = 236, M = 2;

        readonly TrayApp app;
        readonly Canvas c;
        readonly SettingsPage[] pages;
        readonly string[] groupTitles = { "Mouse", "App" };
        readonly SettingsPage[][] groups;

        Theme th;
        Bitmap glass;              // frosted snapshot of what's behind the card, tint included
        Surface frame;
        Color sideFlat;
        RectangleF card;
        int winX, winY;
        bool moving, dragging;
        int moveDX, moveDY;

        int page;
        float pageT = 1; int pageT0;
        float fade; int fadeT0; bool closing;
        readonly Timer ticker, clock;

        public SettingsWindow(TrayApp app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            KeyPreview = true;
            Text = AppInfo.Name;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            // Shrink to fit small screens.
            var wa = Screen.PrimaryScreen.Bounds;
            c = new Canvas(Math.Min(Draw.ScreenScale(), Math.Min(wa.Width * 0.94f / CW, wa.Height * 0.9f / CH)));
            c.Animate = Kick;

            var dpi = new DpiPage(app);
            var perf = new PerformancePage(app);
            var power = new PowerPage(app);
            var general = new GeneralPage(app);
            var device = new DevicePage(app);
            pages = new SettingsPage[] { dpi, perf, power, general, device };
            groups = new[] { new SettingsPage[] { dpi, perf, power, device }, new SettingsPage[] { general } };

            ticker = new Timer { Interval = 15 };
            ticker.Tick += delegate { Tick(); };
            clock = new Timer { Interval = 1000 };
            clock.Tick += delegate { Render(); };
            app.Changed += OnState;
        }

        float F(float v) { return c.F(v); }
        int Pi(float v) { return c.Pi(v); }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000;   // WS_EX_LAYERED
                return cp;
            }
        }

        // ---------- lifecycle ----------

        public void ShowOn(Screen screen)
        {
            if (Visible) { Activate(); return; }
            th = Theme.Current();
            var wa = screen.WorkingArea;
            var size = new Size(Pi(CW + 2 * M), Pi(CH + 2 * M));
            winX = wa.X + (wa.Width - size.Width) / 2;
            winY = wa.Y + (wa.Height - size.Height) / 2;
            Bounds = new Rectangle(winX, winY, size.Width, size.Height);
            CreateFrame(size);
            using (var cap = Glass.Capture(CardScreenRect())) BuildGlass(cap);

            page = 0; pageT = 1;
            pages[0].Opened();
            c.ResetSprings();
            closing = false; fade = 0; fadeT0 = Environment.TickCount;
            Render();
            Show();
            Activate();
            clock.Start();
            Kick();
        }

        void CreateFrame(Size size)
        {
            card = new RectangleF(F(M), F(M), F(CW), F(CH));
            frame = new Surface(size.Width, size.Height);
            frame.SetMask(card, F(CR));
        }

        Rectangle CardScreenRect() { return new Rectangle(winX + Pi(M), winY + Pi(M), Pi(CW), Pi(CH)); }

        void BuildGlass(Bitmap capture)
        {
            if (glass != null) glass.Dispose();
            glass = Glass.Frost(capture, new Size(Pi(CW), Pi(CH)), Glass.AdaptiveTint(capture, CardColor(), th.Dark, 130, 215), th.Fallback, 1.6f);
            c.CardFlat = Glass.Average(glass);
            sideFlat = Draw.Flatten(SideColor(), c.CardFlat);
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

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (frame != null && !closing && fade >= 1) Refrost();
        }

        void Dismiss()
        {
            if (closing) return;
            closing = true; fadeT0 = Environment.TickCount;
            Kick();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            app.Changed -= OnState;
            ticker.Stop(); clock.Stop();
            if (glass != null) { glass.Dispose(); glass = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Dismiss();
            else if (e.KeyCode >= Keys.D1 && e.KeyCode < Keys.D1 + pages.Length) GoTo(e.KeyCode - Keys.D1);
            else if (e.KeyCode == Keys.Down) GoTo(Math.Min(pages.Length - 1, page + 1));
            else if (e.KeyCode == Keys.Up) GoTo(Math.Max(0, page - 1));
            base.OnKeyDown(e);
        }

        void GoTo(int p)
        {
            if (p == page) return;
            page = p; pageT = 0; pageT0 = Environment.TickCount;
            dragging = false;
            pages[p].Opened();
            Kick();
        }

        void OnState(object s, EventArgs e)
        {
            if (!Visible) return;
            // Appearance changed from the General page: re-tint the glass in place.
            if (th != null && th.Dark != Theme.Current().Dark) { th = Theme.Current(); Refrost(); }
            Kick();
        }

        // ---------- animation ----------

        void Kick() { if (!ticker.Enabled) ticker.Start(); }

        void Tick()
        {
            bool more = false;
            int now = Environment.TickCount;

            float fp = Math.Min(1, (now - fadeT0) / (closing ? 140f : 200f));
            fade = closing ? 1 - fp : 1 - (float)Math.Pow(1 - fp, 3);
            if (fp < 1) more = true;
            else if (closing) { ticker.Stop(); Close(); return; }

            if (pageT < 1) { pageT = Math.Min(1, (now - pageT0) / 180f); more = true; }
            if (c.StepSprings()) more = true;
            if (app.Mouse.Saving) more = true;

            Render();
            if (!more) ticker.Stop();
        }

        // ---------- painting ----------

        Color CardColor() { return th.Dark ? Color.FromArgb(205, 24, 24, 28) : Color.FromArgb(200, 250, 250, 252); }
        Color SideColor() { return th.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(120, 255, 255, 255); }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        void Render()
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
            c.G = g; c.Frame = frame; c.Th = th;
            c.Hits.Clear();

            // The rounded corners come from the surface mask; the frosted glass already carries the tint.
            g.DrawImageUnscaled(glass, Pi(M), Pi(M));
            Draw.Rim(g, card, F(CR), th.RimTop, th.RimBottom, Math.Max(1f, F(1)));

            var st = g.Save();
            g.TranslateTransform(card.X, card.Y);
            c.Hits.Offset = card.Location;

            PaintSidebar();

            var content = new RectangleF(F(SideW + 34), F(34), F(CW - SideW - 34 - 34), F(CH - 68));
            c.Fade = pageT;
            c.Interactive = pageT >= 1;
            pages[page].Paint(c, content);
            c.Fade = 1;
            c.Interactive = true;
            PaintChrome();
            g.Restore(st);
        }

        void PaintSidebar()
        {
            var side = new RectangleF(F(12), F(12), F(SideW - 12), F(CH - 24));
            Draw.FillRound(c.G, SideColor(), side, F(CR - 12));
            Draw.Rim(c.G, side, F(CR - 12), th.PlatterRimTop, th.PlatterRimBottom, Math.Max(1f, F(0.8f)));

            int ai = Pi(28);
            using (var art = IconArt.AppArt(ai)) c.G.DrawImageUnscaled(art, (int)(side.X + F(16)), (int)(side.Y + F(18)));
            c.Text(AppInfo.Name, c.Head, th.Label, sideFlat, new RectangleF(side.X + F(16) + ai + F(10), side.Y + F(18), F(140), ai), TextAlign.Left);

            var dev = new RectangleF(side.X + F(10), side.Y + F(62), side.Width - F(20), F(60));
            Draw.FillRound(c.G, th.Platter, dev, F(14));
            Draw.Rim(c.G, dev, F(14), th.PlatterRimTop, th.PlatterRimBottom, Math.Max(1f, F(0.8f)));
            DeviceBadge(new RectangleF(dev.X + F(11), dev.Y + F(11), F(38), F(38)), Draw.Flatten(th.Platter, sideFlat), dev.X + F(60), dev.Width - F(66));

            float y = dev.Bottom + F(18);
            for (int gi = 0; gi < groups.Length; gi++)
            {
                c.Text(groupTitles[gi], c.Cap, th.Secondary, sideFlat, new RectangleF(side.X + F(18), y, F(160), F(16)), TextAlign.Left);
                y += F(20);
                foreach (var pg in groups[gi])
                {
                    int i = Array.IndexOf(pages, pg);
                    var r = new RectangleF(side.X + F(8), y, side.Width - F(16), F(36));
                    string id = "nav" + i;
                    bool sel = i == page;
                    Color rowFill = sel ? th.Blue : c.Hover == id ? (th.Dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(110, 255, 255, 255)) : Color.Empty;
                    if (!rowFill.IsEmpty) Draw.FillRound(c.G, rowFill, r, F(10));
                    var tile = new RectangleF(r.X + F(7), r.Y + F(6), F(24), F(24));
                    c.Tile(tile, pg.Tint);
                    c.SymbolTile(pg.Icon, tile, Color.White);
                    c.Text(pg.Title, sel ? c.Semi : c.Nav, sel ? Color.White : th.Label, sideFlat,
                        new RectangleF(tile.Right + F(11), r.Y, r.Width - F(46), r.Height), TextAlign.Left);
                    c.Hit(id, r, delegate { GoTo(i); });
                    y += F(38);
                }
                y += F(14);
            }
        }

        // Battery ring around the mouse, with its name and status.
        void DeviceBadge(RectangleF box, Color under, float textX, float textW)
        {
            var m = app.Mouse;
            c.BatteryRing(m, box, Math.Max(2.5f, box.Width * 0.08f), true);
            string link = m.Wired ? "USB" : "2.4 GHz";
            string sub = !m.Found ? "Receiver not found"
                : !m.Online ? (m.Percent >= 0 ? m.Percent + "% · asleep" : "Asleep")
                : m.Percent < 0 ? (m.Charging ? "Charging" : "Connected")
                : m.PercentText + " · " + (m.FullyCharged ? "charged" : m.Charging ? "charging" : link);
            float cy = box.Y + box.Height / 2;
            c.Text(m.Device.Name, c.Semi, th.Label, under, new RectangleF(textX, cy - F(19), textW, F(20)), TextAlign.Left);
            c.Text(sub, c.Sub, th.Secondary, under, new RectangleF(textX, cy + F(1), textW, F(18)), TextAlign.Left);
        }

        // Close button and the save status pill.
        void PaintChrome()
        {
            var close = new RectangleF(F(CW - 34 - 32), F(26), F(32), F(32));
            c.Circle(close, "close");
            using (var p = c.StrokePen(th.Secondary, 1.8f))
            {
                float k = F(5.5f), cx = close.X + close.Width / 2, cy = close.Y + close.Height / 2;
                c.G.DrawLine(p, cx - k, cy - k, cx + k, cy + k);
                c.G.DrawLine(p, cx - k, cy + k, cx + k, cy - k);
            }
            c.Hit("close", close, Dismiss);

            var m = app.Mouse;
            string msg = null; Color mc = th.Secondary;
            if (m.Saving) msg = "Saving…";
            else if (m.SaveFailed && (DateTime.Now - m.SavedAt).TotalSeconds < 6) { msg = "Couldn’t reach the mouse"; mc = th.Red; }
            else if ((DateTime.Now - m.SavedAt).TotalSeconds < 2.5) { msg = "Saved to mouse"; mc = th.Green; }
            else if (!m.Online) { msg = m.Found ? "Mouse asleep — move it to wake" : "Receiver not connected"; mc = th.Orange; }
            if (msg == null) return;
            var sz = c.Measure(msg, c.SmallSemi);
            var pill = new RectangleF(close.X - F(12) - sz.Width - F(28), F(29), sz.Width + F(28), F(26));
            var fill = th.Dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(170, 255, 255, 255);
            Draw.FillRound(c.G, fill, pill, pill.Height / 2);
            c.Text(msg, c.SmallSemi, mc, Draw.Flatten(fill, c.CardFlat), pill, TextAlign.Center);
        }

        // ---------- input ----------

        float CardX(int x) { return x - card.X; }

        // Empty space in the header strip or the sidebar drags the window.
        bool IsDragZone(Point p)
        {
            float x = p.X - card.X, y = p.Y - card.Y;
            return y < F(70) || x < F(SideW);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (moving)
            {
                var s = Cursor.Position;
                winX = s.X - moveDX; winY = s.Y - moveDY;
                Layered.Push(Handle, frame, winX, winY, 255);
                return;
            }
            if (dragging) { pages[page].DragTo(CardX(e.X)); Render(); return; }
            var h = c.Hits.At(e.Location);
            string id = h == null ? null : h.Id;
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            if (id != c.Hover) { c.Hover = id; Render(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var h = c.Hits.At(e.Location);
            c.Pressed = h == null ? null : h.Id;
            if (h != null && pages[page].BeginDrag(h.Id, CardX(e.X))) { dragging = true; Capture = true; }
            else if (h == null && IsDragZone(e.Location))
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
                pages[page].EndDrag();
                c.Pressed = null; Render();
                return;
            }
            var h = c.Hits.At(e.Location);
            string p = c.Pressed;
            c.Pressed = null;
            if (e.Button == MouseButtons.Left && h != null && h.Id == p) h.Invoke();
            Kick();
        }

        // ---------- design review ----------

        public void Snapshot(string path, bool dark, int pageIndex)
        {
            th = Theme.Current(dark);
            int pad = Pi(40);
            var size = new Size(Pi(CW + 2 * M), Pi(CH + 2 * M));
            using (var wall = Wallpaper.Large(new Size(size.Width + 2 * pad, size.Height + 2 * pad)))
            {
                CreateFrame(size);
                using (var cap = wall.Clone(new Rectangle(pad + Pi(M), pad + Pi(M), Pi(CW), Pi(CH)), wall.PixelFormat)) BuildGlass(cap);
                page = pageIndex; pageT = 1; fade = 1;
                using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
                frame.ApplyMask();
                using (var g = Graphics.FromImage(wall)) g.DrawImageUnscaled(frame.Bmp, pad, pad);
                wall.Save(path, ImageFormat.Png);
            }
            frame.Dispose(); frame = null;
            glass.Dispose(); glass = null;
        }
    }
}
