using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Nibble
{
    // Settings window: a floating glass card (macOS 26 style, inset glass sidebar) with five pages. What's
    // behind the card is frosted on open and after each move. It only exists while open.
    class SettingsWindow : Form
    {
        const float CW = 980, CH = 640, CR = 30, SideW = 236, M = 2;
        static readonly string[] Pages = { "DPI", "Performance", "Power", "General", "Device" };
        static readonly int[] Presets = { 400, 800, 1200, 1600, 2400, 3200, 6400 };
        static readonly int[] Swatches = { 0xFF0000, 0xFF8000, 0xFFFF00, 0x00FF00, 0x00FFFF, 0x0000FF, 0xFF00FF, 0xFFFFFF };   // what the LED can show
        static readonly string[] SleepNames = { "Never", "30 s", "1 min", "2 min", "3 min", "5 min", "10 min", "15 min", "30 min", "60 min" };
        static readonly string[] RateNames = { "125", "250", "500", "1K", "2K", "4K", "8K" };
        const int MinDpi = 50, MaxDpi = 26000;

        readonly TrayApp app;
        readonly float U;          // px per logical unit
        Theme th;
        Bitmap glass;              // frosted snapshot of what's behind the card, tint included
        Surface frame;             // per-pixel-alpha render target handed to UpdateLayeredWindow
        Color cardFlat, sideFlat;
        RectangleF card;
        int winX, winY;
        bool moving; int moveDX, moveDY;

        readonly Font fTitle, fBig, fHuge, fHead, fRow, fSub, fCap, fSemi, fSmallSemi, fNav;
        int page;
        float pageT = 1;           // content fade after switching pages
        int pageT0;
        float fade; int fadeT0; bool closing;

        readonly Dictionary<string, float> anim = new Dictionary<string, float>();
        readonly Dictionary<string, float> target = new Dictionary<string, float>();
        readonly Timer ticker, clock;

        class Hit { public string Id; public RectangleF R; public Action Do; }
        readonly List<Hit> hits = new List<Hit>();
        string hover, pressed;
        float tx, ty, va = 1;

        bool dragging; int dragValue; RectangleF sliderRect;
        int resetArmedAt = int.MinValue;

        public SettingsWindow(TrayApp app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            KeyPreview = true;
            Text = "Nibble";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            float dpi;
            using (var g = CreateGraphics()) dpi = g.DpiX / 96f;
            var wa = Screen.PrimaryScreen.Bounds;
            U = Math.Min(dpi, Math.Min(wa.Width * 0.94f / CW, wa.Height * 0.9f / CH));

            string disp = Draw.PickFont("Segoe UI Variable Display Semib", "Segoe UI Semibold");
            string text = Draw.PickFont("Segoe UI Variable Text", "Segoe UI");
            string textSb = Draw.PickFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
            fTitle = Px(disp, 30); fHuge = Px(disp, 44); fBig = Px(disp, 26); fHead = Px(textSb, 17);
            fRow = Px(text, 15); fSub = Px(text, 12.5f); fCap = Px(text, 11.5f); fSemi = Px(textSb, 14); fSmallSemi = Px(textSb, 12.5f); fNav = Px(text, 14);

            ticker = new Timer { Interval = 15 };
            ticker.Tick += delegate { Tick(); };
            clock = new Timer { Interval = 1000 };
            clock.Tick += delegate { InvalidateCard(); };
            app.StateChanged += OnState;
        }

        Font Px(string f, float px) { return new Font(f, px * U, FontStyle.Regular, GraphicsUnit.Pixel); }
        float F(float v) { return v * U; }
        int Pi(float v) { return (int)Math.Round(v * U); }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000; // WS_EX_LAYERED: per-pixel alpha for the rounded glass card
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
            card = new RectangleF(F(M), F(M), F(CW), F(CH));
            frame = new Surface(size.Width, size.Height);
            frame.SetMask(card, F(CR));
            using (var cap = Glass.Capture(CardScreenRect())) BuildGlass(cap);

            page = 0; pageT = 1;
            anim.Clear(); target.Clear();
            closing = false; fade = 0; fadeT0 = Environment.TickCount;
            Render();
            Show();
            Activate();
            clock.Start();
            Kick();
        }

        Rectangle CardScreenRect() { return new Rectangle(winX + Pi(M), winY + Pi(M), Pi(CW), Pi(CH)); }

        void BuildGlass(Bitmap capture)
        {
            if (glass != null) glass.Dispose();
            glass = Glass.Frost(capture, new Size(Pi(CW), Pi(CH)), CardColor(), th.Fallback, 1.6f);
            cardFlat = Glass.Average(glass);
            sideFlat = Blend(cardFlat, SideColor());
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
            app.StateChanged -= OnState;
            ticker.Stop(); clock.Stop();
            if (glass != null) { glass.Dispose(); glass = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Dismiss();
            else if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D5) GoTo(e.KeyCode - Keys.D1);
            else if (e.KeyCode == Keys.Down) GoTo(Math.Min(Pages.Length - 1, page + 1));
            else if (e.KeyCode == Keys.Up) GoTo(Math.Max(0, page - 1));
            base.OnKeyDown(e);
        }

        void GoTo(int p)
        {
            if (p == page) return;
            page = p; pageT = 0; pageT0 = Environment.TickCount;
            dragging = false; resetArmedAt = int.MinValue;
            // Network only when the page that shows it is opened.
            if (p == 4 && app.Fw == null) app.CheckFirmware();
            if (p == 3 && app.Upd == null) app.CheckUpdates();
            Kick();
        }

        void OnState(object s, EventArgs e)
        {
            if (!Visible) return;
            // Appearance changed from the Nibble page: re-tint the glass in place.
            if (th != null && th.Dark != Theme.Current().Dark) { th = Theme.Current(); Refrost(); }
            Kick();
        }

        // ---------- animation ----------

        float A(string id, float to)
        {
            target[id] = to;
            float v;
            if (!anim.TryGetValue(id, out v)) { anim[id] = to; return to; }
            return v;
        }

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

            foreach (var k in new List<string>(target.Keys))
            {
                float v = anim.ContainsKey(k) ? anim[k] : target[k], t = target[k];
                float d = t - v;
                if (Math.Abs(d) < 0.002f) v = t; else { v += d * 0.28f; more = true; }
                anim[k] = v;
            }
            if (app.Saving) more = true;

            InvalidateCard();
            if (!more) ticker.Stop();
        }

        void InvalidateCard() { Render(); }

        // ---------- colours ----------

        Color CardColor() { return th.Dark ? Color.FromArgb(205, 24, 24, 28) : Color.FromArgb(200, 250, 250, 252); }
        Color SideColor() { return th.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(120, 255, 255, 255); }

        static Color Blend(Color under, Color over)
        {
            float k = over.A / 255f;
            return Color.FromArgb(255, (int)(under.R + (over.R - under.R) * k), (int)(under.G + (over.G - under.G) * k), (int)(under.B + (over.B - under.B) * k));
        }

        Color Av(Color c) { return va >= 1 ? c : Draw.Alpha(c, va); }

        // ---------- painting ----------

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        void Render()
        {
            if (frame == null || glass == null || !IsHandleCreated) return;
            using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
            frame.ApplyMask();
            Push((byte)(255 * Math.Max(0, Math.Min(1, fade))));
        }

        void Push(byte alpha)
        {
            Layered.Push(Handle, frame, winX, winY, alpha);
        }

        void Compose(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            hits.Clear();

            // Rounded corners come from the surface mask; the frosted glass already carries the card tint.
            g.DrawImageUnscaled(glass, Pi(M), Pi(M));
            Draw.Rim(g, card, F(CR), th.RimTop, th.RimBottom, Math.Max(1f, F(1)));

            var st = g.Save();
            g.TranslateTransform(card.X, card.Y);
            tx = card.X; ty = card.Y;

            PaintSidebar(g);

            va = pageT;
            var content = new RectangleF(F(SideW + 34), F(34), F(CW - SideW - 34 - 34), F(CH - 68));
            switch (page)
            {
                case 0: PaintDpi(g, content); break;
                case 1: PaintPerformance(g, content); break;
                case 2: PaintPower(g, content); break;
                case 3: PaintNibble(g, content); break;
                default: PaintDevice(g, content); break;
            }
            va = 1;
            PaintChrome(g, content);
            g.Restore(st);
        }

        public static int SideStyle;   // 0 = inset panel, 1 = iPadOS flush, 2 = minimal (design exploration)

        static readonly int[][] NavGroups = { new[] { 0, 1, 2, 4 }, new[] { 3 } };

        void PaintSidebar(Graphics g)
        {
            if (SideStyle == 1) { PaintSidebarFlush(g); return; }
            if (SideStyle == 2) { PaintSidebarMinimal(g); return; }
            PaintSidebarInset(g);
        }

        // Battery ring around the mouse glyph + name/status, shared by the sidebar styles.
        void DeviceBadge(Graphics g, RectangleF box, Color under, bool compactText, float textX, float textW)
        {
            float rs = Math.Max(2.5f, box.Width * 0.08f);
            var rr = RectangleF.Inflate(box, -rs / 2, -rs / 2);
            using (var p = new Pen(th.Track, rs)) g.DrawEllipse(p, rr);
            if (app.Percent > 0)
                using (var p = new Pen(!app.Online ? th.Tertiary : app.Percent <= 20 && !app.Charging ? th.Red : th.Green, rs))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawArc(p, rr, -90, 3.6f * app.Percent);
                }
            int ms = (int)Math.Round(box.Width * 0.5f);
            using (var m = Glyphs.Compose(ms, Glyphs.CenteredCoverage(Glyphs.Mouse.ToString(), Glyphs.IconFace, ms, ms, 400), app.Online ? th.Label : th.Secondary))
                g.DrawImageUnscaled(m, (int)(box.X + (box.Width - ms) / 2), (int)(box.Y + (box.Height - ms) / 2));
            string sub = !app.Found ? "Receiver not found"
                : !app.Online ? (app.Percent >= 0 ? app.Percent + "% · asleep" : "Asleep")
                : string.Format("{0}% · {1}", app.Percent, app.Charging ? "charging" : app.Wired ? "USB" : "2.4 GHz");
            float cy = box.Y + box.Height / 2;
            Txt(g, "RK M3", compactText ? fSemi : fHead, th.Label, under, new RectangleF(textX, cy - F(19), textW, F(20)), 0);
            Txt(g, sub, fSub, th.Secondary, under, new RectangleF(textX, cy + F(1), textW, F(18)), 0);
        }

        // A: iPadOS Settings — flush full-height sidebar, device card row, roomy rows with icon tiles.
        void PaintSidebarFlush(Graphics g)
        {
            var side = new RectangleF(0, 0, F(SideW), F(CH));
            var fill = th.Dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(90, 255, 255, 255);
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, side);
            using (var p = new Pen(th.Separator, Math.Max(1f, F(0.8f)))) g.DrawLine(p, side.Right, 0, side.Right, side.Bottom);
            var under = Blend(cardFlat, fill);

            var dev = new RectangleF(F(14), F(22), side.Width - F(28), F(64));
            Draw.FillRound(g, th.Platter, dev, F(14));
            var devFlat = Blend(under, th.Platter);
            DeviceBadge(g, new RectangleF(dev.X + F(12), dev.Y + F(12), F(40), F(40)), devFlat, true, dev.X + F(62), dev.Width - F(70));

            float y = dev.Bottom + F(18);
            foreach (var group in NavGroups)
            {
                foreach (int i in group)
                {
                    var r = new RectangleF(F(14), y, side.Width - F(28), F(40));
                    string id = "nav" + i;
                    bool sel = i == page;
                    Color rowFill = sel ? th.Blue : hover == id ? (th.Dark ? Color.FromArgb(16, 255, 255, 255) : Color.FromArgb(110, 255, 255, 255)) : Color.Empty;
                    if (!rowFill.IsEmpty) Draw.FillRound(g, rowFill, r, F(10));
                    var tile = new RectangleF(r.X + F(10), r.Y + F(8), F(24), F(24));
                    Tile(g, tile, PageTint(i));
                    PageIcon(g, i, tile, Color.White);
                    Txt(g, Pages[i], sel ? fSemi : fNav, sel ? Color.White : th.Label, sel ? Blend(under, th.Blue) : rowFill.IsEmpty ? under : Blend(under, rowFill),
                        new RectangleF(tile.Right + F(12), r.Y, r.Width - F(50), r.Height), 0);
                    int idx = i;
                    AddHit(id, r, delegate { GoTo(idx); });
                    y += F(42);
                }
                y += F(16);
            }
        }

        // B: minimal (Music/Finder) — monochrome glyphs, tinted selection, device status docked at the bottom.
        void PaintSidebarMinimal(Graphics g)
        {
            var side = new RectangleF(0, 0, F(SideW), F(CH));
            var under = cardFlat;
            using (var p = new Pen(th.Separator, Math.Max(1f, F(0.8f)))) g.DrawLine(p, side.Right, F(24), side.Right, side.Bottom - F(24));

            float y = F(34);
            Txt(g, "Settings", fHead, th.Label, under, new RectangleF(F(26), y, F(180), F(24)), 0);
            y += F(40);
            foreach (var group in NavGroups)
            {
                foreach (int i in group)
                {
                    var r = new RectangleF(F(14), y, side.Width - F(28), F(36));
                    string id = "nav" + i;
                    bool sel = i == page;
                    Color tint = Color.FromArgb(th.Dark ? 46 : 30, th.Blue);
                    Color rowFill = sel ? tint : hover == id ? (th.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(10, 0, 0, 0)) : Color.Empty;
                    if (!rowFill.IsEmpty) Draw.FillRound(g, rowFill, r, F(9));
                    Color fg = sel ? th.Blue : th.Label;
                    var icon = new RectangleF(r.X + F(10), r.Y + F(7), F(22), F(22));
                    PageIcon(g, i, icon, sel ? th.Blue : th.Secondary);
                    Txt(g, Pages[i], sel ? fSemi : fNav, fg, rowFill.IsEmpty ? under : Blend(under, rowFill),
                        new RectangleF(icon.Right + F(12), r.Y, r.Width - F(50), r.Height), 0);
                    int idx = i;
                    AddHit(id, r, delegate { GoTo(idx); });
                    y += F(38);
                }
                y += F(14);
            }

            // Device docked at the bottom, separated by a hairline.
            float dy = side.Bottom - F(78);
            using (var p = new Pen(th.Separator, Math.Max(1f, F(0.8f)))) g.DrawLine(p, F(24), dy, side.Right - F(24), dy);
            DeviceBadge(g, new RectangleF(F(24), dy + F(18), F(40), F(40)), under, true, F(76), side.Width - F(90));
        }

        // C: inset floating panel (the previous design).
        void PaintSidebarInset(Graphics g)
        {
            var side = new RectangleF(F(12), F(12), F(SideW - 12), F(CH - 24));
            Draw.FillRound(g, SideColor(), side, F(CR - 12));
            Draw.Rim(g, side, F(CR - 12), th.PlatterRimTop, th.PlatterRimBottom, Math.Max(1f, F(0.8f)));

            // Branding: app icon + wordmark.
            int ai = Pi(28);
            using (var art = IconArt.AppArt(ai)) g.DrawImageUnscaled(art, (int)(side.X + F(16)), (int)(side.Y + F(18)));
            Txt(g, "Nibble", fHead, th.Label, sideFlat, new RectangleF(side.X + F(16) + ai + F(10), side.Y + F(18), F(140), ai), 0);

            // Device card: live battery ring around the mouse, name and status.
            var card2 = new RectangleF(side.X + F(10), side.Y + F(62), side.Width - F(20), F(60));
            Draw.FillRound(g, th.Platter, card2, F(14));
            Draw.Rim(g, card2, F(14), th.PlatterRimTop, th.PlatterRimBottom, Math.Max(1f, F(0.8f)));
            DeviceBadge(g, new RectangleF(card2.X + F(11), card2.Y + F(11), F(38), F(38)), Blend(sideFlat, th.Platter), true, card2.X + F(60), card2.Width - F(66));

            // Grouped navigation, macOS-style: small group titles, compact rows, accent selection.
            string[] titles = { "Mouse", "App" };
            float y = card2.Bottom + F(18);
            for (int gi = 0; gi < NavGroups.Length; gi++)
            {
                Txt(g, titles[gi], fCap, th.Secondary, sideFlat, new RectangleF(side.X + F(18), y, F(160), F(16)), 0);
                y += F(20);
                foreach (int i in NavGroups[gi])
                {
                    var r = new RectangleF(side.X + F(8), y, side.Width - F(16), F(36));
                    string id = "nav" + i;
                    bool sel = i == page;
                    Color rowFill = sel ? th.Blue : hover == id ? (th.Dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(110, 255, 255, 255)) : Color.Empty;
                    if (!rowFill.IsEmpty) Draw.FillRound(g, rowFill, r, F(10));
                    var tile = new RectangleF(r.X + F(7), r.Y + F(6), F(24), F(24));
                    Tile(g, tile, PageTint(i));
                    PageIcon(g, i, tile, Color.White);
                    Txt(g, Pages[i], sel ? fSemi : fNav, sel ? Color.White : th.Label, sideFlat,
                        new RectangleF(tile.Right + F(11), r.Y, r.Width - F(46), r.Height), 0);
                    int idx = i;
                    AddHit(id, r, delegate { GoTo(idx); });
                    y += F(38);
                }
                y += F(14);
            }
        }

        readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>();

        // Page icons: custom symbols on a 24-unit grid (bold 2-unit strokes, filled where it reads better).
        // Each is one path, scaled to the same ink size and centred on its true geometric bounds.
        void PageIcon(Graphics g, int i, RectangleF box, Color c)
        {
            int s = (int)Math.Round(box.Width);
            string key = i + ":" + s + ":" + c.ToArgb();
            Bitmap bmp;
            if (!iconCache.TryGetValue(key, out bmp))
            {
                bmp = new Bitmap(s, s, PixelFormat.Format32bppPArgb);
                using (var bg = Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(c))
                {
                    bg.SmoothingMode = SmoothingMode.AntiAlias;
                    bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    var parts = Icons.Parts(i);
                    var b = parts[0].GetBounds();
                    foreach (var part in parts) b = RectangleF.Union(b, part.GetBounds());
                    float ink = s * 0.64f, k = ink / Math.Max(b.Width, b.Height);
                    using (var m = new Matrix())
                    {
                        m.Translate(s / 2f, s / 2f);
                        m.Scale(k, k);
                        m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2));
                        foreach (var part in parts) { part.Transform(m); bg.FillPath(brush, part); part.Dispose(); }
                    }
                }
                iconCache[key] = bmp;
            }
            g.DrawImageUnscaled(bmp, (int)Math.Round(box.X), (int)Math.Round(box.Y));
        }
        Color PageTint(int i)
        {
            switch (i)
            {
                case 0: return Theme.Hex(0xFF9500);
                case 1: return Theme.Hex(0x007AFF);
                case 2: return Theme.Hex(0x34C759);
                case 3: return Theme.Hex(0xAF52DE);
                default: return Theme.Hex(0x8E8E93);
            }
        }

        // macOS-style icon tile: squircle with a soft top-to-bottom gradient and an inner top highlight.
        void Tile(Graphics g, RectangleF r, Color c)
        {
            float rad = r.Width * 0.27f;
            using (var path = Draw.Round(r, rad))
            using (var br = new LinearGradientBrush(r, Draw.Lerp(c, Color.White, 0.22f), Draw.Lerp(c, Color.Black, 0.08f), 90f))
                g.FillPath(br, path);
            Draw.Rim(g, r, rad, Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Math.Max(1f, F(0.8f)));
        }

        void PaintChrome(Graphics g, RectangleF content)
        {
            // Close button
            var close = new RectangleF(F(CW - 34 - 32), F(26), F(32), F(32));
            Circle(g, close, "close");
            using (var p = GPen(th.Secondary, 1.8f))
            {
                float c = F(5.5f), cx = close.X + close.Width / 2, cy = close.Y + close.Height / 2;
                g.DrawLine(p, cx - c, cy - c, cx + c, cy + c);
                g.DrawLine(p, cx - c, cy + c, cx + c, cy - c);
            }
            AddHit("close", close, Dismiss);

            // Save status pill
            string msg = null; Color mc = th.Secondary;
            if (app.Saving) msg = "Saving…";
            else if (app.SaveFailed && (DateTime.Now - app.SavedAt).TotalSeconds < 6) { msg = "Couldn’t reach the mouse"; mc = th.Red; }
            else if ((DateTime.Now - app.SavedAt).TotalSeconds < 2.5) { msg = "Saved to mouse"; mc = th.Green; }
            else if (!app.Online) { msg = app.Found ? "Mouse asleep — move it to wake" : "Receiver not connected"; mc = th.Orange; }
            if (msg != null)
            {
                var sz = MeasureT(msg, fSmallSemi);
                var pill = new RectangleF(close.X - F(12) - sz.Width - F(28), F(29), sz.Width + F(28), F(26));
                var fill = th.Dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(170, 255, 255, 255);
                Draw.FillRound(g, fill, pill, pill.Height / 2);
                Txt(g, msg, fSmallSemi, mc, Blend(cardFlat, fill), pill, 1);
            }
        }

        // ---------- pages ----------

        float Header(Graphics g, RectangleF c, string title, string sub)
        {
            Txt(g, title, fTitle, th.Label, cardFlat, new RectangleF(c.X, c.Y - F(4), c.Width - F(260), F(40)), 0);
            Txt(g, sub, fSub, th.Secondary, cardFlat, new RectangleF(c.X, c.Y + F(36), c.Width - F(60), F(18)), 0);
            return c.Y + F(74);
        }

        bool NeedConfig(Graphics g, RectangleF c, float y)
        {
            if (app.Config != null) return true;
            var p = new RectangleF(c.X, y, c.Width, F(120));
            Platter(g, p);
            Txt(g, app.Found ? "Reading settings from the mouse…" : "Plug in the RK M3 receiver to change settings.", fRow, th.Secondary, PlatterFlat(), p, 1);
            return false;
        }

        void PaintDpi(Graphics g, RectangleF c)
        {
            float y = Header(g, c, "DPI", "Sensitivity stages the DPI button cycles through. Tap a stage to switch to it.");
            if (!NeedConfig(g, c, y)) return;
            var cfg = app.Config;

            // Stage count
            var row = new RectangleF(c.X, y, c.Width, F(56));
            Platter(g, row);
            Txt(g, "Stages", fRow, th.Label, PlatterFlat(), new RectangleF(row.X + F(18), row.Y, F(200), row.Height), 0);
            var seg = new RectangleF(row.Right - F(14) - F(300), row.Y + F(12), F(300), F(32));
            Segmented(g, seg, new[] { "1", "2", "3", "4", "5", "6" }, cfg.Stages - 1, "stages", delegate (int i)
            {
                int n = i + 1;
                app.Change(x => x.SetStages(Math.Min(x.Stage, n), n), x => RkM3.WriteDpiStage(x.Stage, x.Stages, x.CurrentDpi, x.StageColor(x.Stage)));
            });
            y = row.Bottom + F(14);

            // Stage cards
            float gap = F(10), cw = (c.Width - gap * 5) / 6, ch = F(104);
            for (int k = 1; k <= cfg.Stages; k++)
            {
                var r = new RectangleF(c.X + (k - 1) * (cw + gap), y, cw, ch);
                bool active = k == cfg.Stage;
                string id = "stage" + k;
                Color fill = active ? (th.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(235, 255, 255, 255)) : (hover == id ? PlatterHover() : th.Platter);
                Draw.FillRound(g, Av(fill), r, F(18));
                Draw.Rim(g, r, F(18), Av(th.PlatterRimTop), Av(th.PlatterRimBottom), Math.Max(1f, F(0.8f)));
                if (active)
                    using (var p = new Pen(Av(th.Green), F(2)))
                    using (var path = Draw.Round(RectangleF.Inflate(r, -F(1), -F(1)), F(17))) g.DrawPath(p, path);
                var flat = Blend(cardFlat, fill);
                var col = cfg.StageColor(k);
                using (var b = new SolidBrush(Av(col))) g.FillEllipse(b, r.X + F(14), r.Y + F(16), F(10), F(10));
                using (var p = new Pen(Av(Color.FromArgb(60, 0, 0, 0)), 1)) g.DrawEllipse(p, r.X + F(14), r.Y + F(16), F(10), F(10));
                Txt(g, "Stage " + k, fCap, th.Secondary, flat, new RectangleF(r.X + F(30), r.Y + F(13), r.Width - F(36), F(16)), 0);
                int shown = active && dragging ? dragValue : cfg.Dpi(k);
                Txt(g, shown.ToString(), fBig, th.Label, flat, new RectangleF(r.X + F(12), r.Y + F(38), r.Width - F(16), F(34)), 0);
                Txt(g, active ? "Active" : "DPI", fCap, active ? th.Green : th.Secondary, flat, new RectangleF(r.X + F(14), r.Y + F(74), r.Width - F(20), F(16)), 0);
                int kk = k;
                if (!active) AddHit(id, r, delegate
                {
                    app.Change(x => x.SetStages(kk, x.Stages), x => RkM3.WriteDpiStage(kk, x.Stages, x.Dpi(kk), x.StageColor(kk)));
                });
            }
            y += ch + F(14);

            // Editor for the active stage
            int s = cfg.Stage, v = dragging ? dragValue : cfg.Dpi(s);
            var ed = new RectangleF(c.X, y, c.Width, F(246));
            Platter(g, ed);
            var pf = PlatterFlat();
            Txt(g, "Stage " + s, fHead, th.Label, pf, new RectangleF(ed.X + F(20), ed.Y + F(16), F(200), F(24)), 0);
            Txt(g, "Drag, pick a preset, or nudge by 50.", fSub, th.Secondary, pf, new RectangleF(ed.X + F(20), ed.Y + F(40), F(300), F(18)), 0);

            // Big value with -/+ nudgers
            var plus = new RectangleF(ed.Right - F(20) - F(34), ed.Y + F(20), F(34), F(34));
            var minus = new RectangleF(plus.X - F(8) - F(34), plus.Y, F(34), F(34));
            Circle(g, minus, "minus"); Circle(g, plus, "plus");
            using (var p = GPen(th.Label, 2f))
            {
                float cx = minus.X + minus.Width / 2, cy = minus.Y + minus.Height / 2, h = F(6);
                g.DrawLine(p, cx - h, cy, cx + h, cy);
                cx = plus.X + plus.Width / 2;
                g.DrawLine(p, cx - h, cy, cx + h, cy); g.DrawLine(p, cx, cy - h, cx, cy + h);
            }
            AddHit("minus", minus, delegate { SetStageDpi(v - 50); });
            AddHit("plus", plus, delegate { SetStageDpi(v + 50); });
            string vs = v.ToString();
            var vsz = MeasureT(vs, fHuge);
            var unitX = minus.X - F(18) - MeasureT("DPI", fSemi).Width;
            var vr = new RectangleF(unitX - F(8) - vsz.Width, ed.Y + F(12), vsz.Width + F(2), F(50));
            Txt(g, vs, fHuge, th.Label, pf, vr, 0);
            Txt(g, "DPI", fSemi, th.Secondary, pf, new RectangleF(unitX, vr.Y + F(21), F(40), F(24)), 0);

            // Slider
            sliderRect = new RectangleF(ed.X + F(24), ed.Y + F(92), ed.Width - F(48), F(28));
            float track = F(6), frac = (v - MinDpi) / (float)(MaxDpi - MinDpi);
            var tr = new RectangleF(sliderRect.X, sliderRect.Y + (sliderRect.Height - track) / 2, sliderRect.Width, track);
            Draw.FillRound(g, Av(th.Track), tr, track / 2);
            Draw.FillRound(g, Av(th.Blue), new RectangleF(tr.X, tr.Y, Math.Max(track, tr.Width * frac), track), track / 2);
            float kx = tr.X + tr.Width * frac, kd = F(26);
            var knob = new RectangleF(kx - kd / 2, sliderRect.Y + (sliderRect.Height - kd) / 2, kd, kd);
            Draw.FillRound(g, Av(Color.FromArgb(50, 0, 0, 0)), new RectangleF(knob.X, knob.Y + F(1.5f), kd, kd), kd / 2);
            using (var b = new SolidBrush(Av(Color.White))) g.FillEllipse(b, knob);
            Txt(g, MinDpi.ToString(), fCap, th.Secondary, pf, new RectangleF(tr.X, tr.Bottom + F(10), F(60), F(14)), 0);
            Txt(g, MaxDpi.ToString(), fCap, th.Secondary, pf, new RectangleF(tr.Right - F(60), tr.Bottom + F(10), F(60), F(14)), 2);
            AddHit("slider", RectangleF.Inflate(sliderRect, 0, F(6)), delegate { });

            // Presets
            float px = ed.X + F(20), py = ed.Y + F(150);
            Txt(g, "Presets", fSub, th.Secondary, pf, new RectangleF(px, py, F(80), F(30)), 0);
            px += F(70);
            foreach (int preset in Presets)
            {
                string label = preset.ToString();
                float w = MeasureT(label, fSmallSemi).Width + F(26);
                var chip = new RectangleF(px, py, w, F(30));
                int pv = preset;
                Chip(g, chip, label, v == preset, "preset" + preset, delegate { SetStageDpi(pv); });
                px += w + F(8);
            }

            // Indicator colour
            float cy2 = ed.Y + F(196);
            Txt(g, "Colour", fSub, th.Secondary, pf, new RectangleF(ed.X + F(20), cy2, F(80), F(30)), 0);
            float sx = ed.X + F(90);
            var cur = cfg.StageColor(s);
            foreach (int hex in Swatches)
            {
                var col = Theme.Hex(hex);
                var sr = new RectangleF(sx, cy2 + F(3), F(24), F(24));
                bool sel = cur.R == col.R && cur.G == col.G && cur.B == col.B;
                if (sel) using (var p = new Pen(Av(th.Label), F(2))) g.DrawEllipse(p, RectangleF.Inflate(sr, F(3.5f), F(3.5f)));
                using (var b = new SolidBrush(Av(col))) g.FillEllipse(b, sr);
                using (var p = new Pen(Av(Color.FromArgb(50, 0, 0, 0)), 1)) g.DrawEllipse(p, sr);
                var cc = col;
                AddHit("sw" + hex, RectangleF.Inflate(sr, F(4), F(4)), delegate
                {
                    app.Change(x => x.SetStageColor(x.Stage, cc), x => RkM3.WriteDpiStage(x.Stage, x.Stages, x.CurrentDpi, cc));
                });
                sx += F(36);
            }
        }

        void SetStageDpi(int dpi)
        {
            dpi = Math.Max(MinDpi, Math.Min(MaxDpi, (int)Math.Round(dpi / 50.0) * 50));
            if (app.Config == null || app.Config.CurrentDpi == dpi) return;
            app.Change(x => x.SetDpi(x.Stage, dpi), x => RkM3.WriteDpiStage(x.Stage, x.Stages, x.CurrentDpi, x.StageColor(x.Stage)));
        }

        void PaintPerformance(Graphics g, RectangleF c)
        {
            float y = Header(g, c, "Performance", "How the sensor and radio behave. Changes apply instantly.");
            if (!NeedConfig(g, c, y)) return;
            var cfg = app.Config;
            float rh = F(58);

            var p1 = new RectangleF(c.X, y, c.Width, rh * 2);
            Platter(g, p1);
            var pf = PlatterFlat();
            RowLabel(g, p1, 0, rh, "Polling rate", "Reports per second, in Hz");
            Segmented(g, new RectangleF(p1.Right - F(14) - F(392), p1.Y + (rh - F(32)) / 2, F(392), F(32)), RateNames,
                Array.IndexOf(MouseConfig.RateIds, cfg.RateId), "rate", delegate (int i)
                {
                    int id = MouseConfig.RateIds[i];
                    app.Change(x => x.RateId = id, x => RkM3.WriteReportRate(x.RateId));
                });
            Sep(g, p1, rh);
            string[] modeTips = { "Low power: slower response, longest battery", "Balanced response and battery", "Fastest response, highest power draw" };
            RowLabel(g, p1, 1, rh, "Sensor mode", modeTips[cfg.SensorMode]);
            Segmented(g, new RectangleF(p1.Right - F(14) - F(360), p1.Y + rh + (rh - F(32)) / 2, F(360), F(32)), MouseConfig.SensorModes,
                cfg.SensorMode, "mode", delegate (int i) { app.Change(x => x.SensorMode = i, WritePerf); });
            y = p1.Bottom + F(14);

            var p2 = new RectangleF(c.X, y, c.Width, rh * 4);
            Platter(g, p2);
            bool at8k = cfg.RateHzValue >= 8000;
            ToggleRow(g, p2, 0, rh, "Motion Sync", at8k ? "Not supported at 8000 Hz" : "Aligns sensor frames with each report", MouseConfig.BitMotionSync);
            Sep(g, p2, rh);
            ToggleRow(g, p2, 1, rh, "Ripple Control", "Smooths jitter above 9000 DPI", MouseConfig.BitRipple);
            Sep(g, p2, rh * 2);
            ToggleRow(g, p2, 2, rh, "Angle Snapping", "Straightens slightly off-axis lines", MouseConfig.BitAngleSnap);
            Sep(g, p2, rh * 3);
            ToggleRow(g, p2, 3, rh, "Glass Mode", "Better tracking on glass surfaces", MouseConfig.BitGlass);
            y = p2.Bottom + F(14);

            var p3 = new RectangleF(c.X, y, c.Width, rh * 2);
            Platter(g, p3);
            RowLabel(g, p3, 0, rh, "Lift-off distance", "Height where tracking stops");
            Segmented(g, new RectangleF(p3.Right - F(14) - F(270), p3.Y + (rh - F(32)) / 2, F(270), F(32)), MouseConfig.LodNames,
                cfg.Lod - 1, "lod", delegate (int i) { app.Change(x => x.Lod = i + 1, WritePerf); });
            Sep(g, p3, rh);
            RowLabel(g, p3, 1, rh, "Click debounce", "Lower is faster; raise it if clicks double");
            Stepper(g, new RectangleF(p3.Right - F(14) - F(150), p3.Y + rh + (rh - F(32)) / 2, F(150), F(32)), cfg.Debounce + " ms", "deb",
                delegate { app.Change(x => x.Debounce = x.Debounce - 1, WritePerf); },
                delegate { app.Change(x => x.Debounce = x.Debounce + 1, WritePerf); });
        }

        static bool WritePerf(MouseConfig x) { return RkM3.WritePerformance(x.Lod, x.Debounce, x.PerfBits); }

        void PaintPower(Graphics g, RectangleF c)
        {
            float y = Header(g, c, "Power", "Battery, sleep, and how Nibble keeps you posted.");
            float rh = F(58);

            // Battery hero
            var hero = new RectangleF(c.X, y, c.Width, F(92));
            Platter(g, hero);
            var pf = PlatterFlat();
            float d = F(72), stroke = F(8);
            var rr = new RectangleF(hero.X + F(22) + stroke / 2, hero.Y + (hero.Height - d) / 2 + stroke / 2, d - stroke, d - stroke);
            using (var p = new Pen(Av(th.Track), stroke)) g.DrawEllipse(p, rr);
            if (app.Percent > 0)
                using (var p = new Pen(Av(!app.Online ? th.Tertiary : app.Percent <= 20 && !app.Charging ? th.Red : th.Green), stroke))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawArc(p, rr, -90, 3.6f * app.Percent);
                }
            Txt(g, app.Percent >= 0 ? app.Percent + "%" : "—", fBig, th.Label, pf, new RectangleF(hero.X + F(114), hero.Y + F(16), F(200), F(34)), 0);
            string status = !app.Found ? "Receiver not connected" : !app.Online ? "Asleep" : app.Charging ? "Charging" : app.StatusText();
            Txt(g, status + (app.Online ? (app.Wired ? " · USB cable" : " · 2.4 GHz") : ""), fSub, app.Charging ? th.Green : th.Secondary, pf,
                new RectangleF(hero.X + F(116), hero.Y + F(52), F(300), F(18)), 0);
            y = hero.Bottom + F(14);

            // Sleep timer
            var sp = new RectangleF(c.X, y, c.Width, F(132));
            Platter(g, sp);
            RowLabel(g, sp, 0, rh, "Sleep after", "The mouse powers down after sitting still this long");
            if (app.Config != null)
            {
                float cw = (sp.Width - F(28) - F(8) * 4) / 5;
                for (int i = 0; i < MouseConfig.SleepOptions.Length; i++)
                {
                    var chip = new RectangleF(sp.X + F(14) + (i % 5) * (cw + F(8)), sp.Y + F(58) + (i / 5) * F(36), cw, F(30));
                    int secs = MouseConfig.SleepOptions[i];
                    Chip(g, chip, SleepNames[i], app.Config.SleepSeconds == secs, "sleep" + i, delegate
                    {
                        app.Change(x => x.SleepSeconds = secs, x => RkM3.WriteSleep(x.SleepSeconds));
                    });
                }
            }
            y = sp.Bottom + F(14);

        }

        void PaintNibble(Graphics g, RectangleF c)
        {
            float y = Header(g, c, "General", "How Nibble looks and behaves.");
            float rh = F(58);

            // Tray icon gallery: live previews of each style at the current level.
            var gp = new RectangleF(c.X, y, c.Width, F(176));
            Platter(g, gp);
            var pf = PlatterFlat();
            Txt(g, "Tray icon", fRow, th.Label, pf, new RectangleF(gp.X + F(18), gp.Y + F(12), F(200), F(22)), 0);
            Txt(g, "Hover it any time for the exact percentage", fSub, th.Secondary, pf, new RectangleF(gp.X + F(18), gp.Y + F(32), F(360), F(18)), 0);
            int n = IconArt.TrayStyles.Length;
            float gap = F(10), tw = (gp.Width - F(36) - gap * (n - 1)) / n, th0 = F(104);
            int pct = app.Percent >= 0 ? app.Percent : 60;
            for (int i = 0; i < n; i++)
            {
                var r = new RectangleF(gp.X + F(18) + i * (tw + gap), gp.Y + F(58), tw, th0);
                bool sel = app.TrayStyle == i;
                string id = "tray" + i;
                // Preview well tinted like the taskbar, so the icon is judged in context.
                var well = new RectangleF(r.X + F(8), r.Y + F(8), r.Width - F(16), F(60));
                Color wellColor = Theme.TaskbarLight() ? Color.FromArgb(255, 238, 238, 240) : Color.FromArgb(255, 40, 40, 44);
                Draw.FillRound(g, Av(sel ? th.Blue : hover == id ? th.ControlHover : th.SegTrack), r, F(16));
                Draw.FillRound(g, Av(wellColor), well, F(10));
                int ps = Pi(32);
                using (var art = IconArt.TrayArt(i, ps, pct, app.Charging, !app.Online && app.Found, Theme.TaskbarLight()))
                    g.DrawImageUnscaled(art, (int)(well.X + (well.Width - ps) / 2), (int)(well.Y + (well.Height - ps) / 2));
                Txt(g, IconArt.TrayStyles[i], fSmallSemi, sel ? Color.White : th.Label, Blend(pf, sel ? th.Blue : th.SegTrack),
                    new RectangleF(r.X, well.Bottom + F(6), r.Width, F(22)), 1);
                int idx = i;
                if (!sel) AddHit(id, r, delegate { app.SetTrayStyle(idx); });
            }
            y = gp.Bottom + F(14);

            var p = new RectangleF(c.X, y, c.Width, rh * 5);
            Platter(g, p);
            PaintUpdateRow(g, p, 4, rh);
            Sep(g, p, rh * 4);
            RowLabel(g, p, 0, rh, "Appearance", "Nibble’s panels and this window");
            Segmented(g, new RectangleF(p.Right - F(14) - F(270), p.Y + (rh - F(32)) / 2, F(270), F(32)), new[] { "System", "Light", "Dark" },
                app.Appearance, "appearance", delegate (int i) { app.SetAppearance(i); });
            Sep(g, p, rh);
            RowLabel(g, p, 1, rh, "Launch at login", "Start Nibble in the tray with Windows");
            Switch(g, SwitchRect(p, 1, rh), app.AutoStart, "login", delegate { app.AutoStart = !app.AutoStart; });
            Sep(g, p, rh * 2);
            RowLabel(g, p, 2, rh, "Check battery every", "Live changes still arrive instantly");
            int[] iv = { 30, 60, 300, 900 };
            Segmented(g, new RectangleF(p.Right - F(14) - F(240), p.Y + rh * 2 + (rh - F(32)) / 2, F(240), F(32)), new[] { "30s", "1m", "5m", "15m" },
                Array.IndexOf(iv, app.IntervalSec), "interval", delegate (int i) { app.SetInterval(iv[i]); });
            Sep(g, p, rh * 3);
            RowLabel(g, p, 3, rh, "Battery alerts", "Notify at 20% and when fully charged");
            Switch(g, SwitchRect(p, 3, rh), app.LowAlert, "alerts", delegate { app.SetLowAlert(!app.LowAlert); });
        }

        void PaintDevice(Graphics g, RectangleF c)
        {
            float y = Header(g, c, "Device", "Everything Nibble reads from the mouse.");
            float rh = F(44);
            var cfg = app.Config;
            string[,] rows =
            {
                { "Model", "Royal Kludge M3" },
                { "Connection", !app.Found ? "Not connected" : !app.Online ? "Asleep" : app.Wired ? "USB cable" : "2.4 GHz receiver" },
                { "Polling rate", cfg != null ? cfg.RateHzValue + " Hz" : "—" },
                { "Current DPI", cfg != null ? cfg.CurrentDpi + " (stage " + cfg.Stage + " of " + cfg.Stages + ")" : "—" },
            };
            int n = rows.GetLength(0);
            var p = new RectangleF(c.X, y, c.Width, rh * n);
            Platter(g, p);
            var pf = PlatterFlat();
            for (int i = 0; i < n; i++)
            {
                var r = new RectangleF(p.X + F(18), p.Y + rh * i, p.Width - F(36), rh);
                Txt(g, rows[i, 0], fRow, th.Label, pf, r, 0);
                Txt(g, rows[i, 1], fRow, th.Secondary, pf, r, 2);
                if (i < n - 1) Sep(g, p, rh * (i + 1));
            }
            y = p.Bottom + F(14);

            y = PaintFirmware(g, c, y) + F(14);

            // Factory reset: tap once to arm, again within 4 s to confirm.
            bool armed = resetArmedAt != int.MinValue && unchecked(Environment.TickCount - resetArmedAt) < 4000;
            var rp = new RectangleF(c.X, y, c.Width, F(58));
            string id = "reset";
            Draw.FillRound(g, Av(hover == id ? PlatterHover() : th.Platter), rp, F(18));
            Draw.Rim(g, rp, F(18), Av(th.PlatterRimTop), Av(th.PlatterRimBottom), Math.Max(1f, F(0.8f)));
            Txt(g, armed ? "Tap again to restore factory settings" : "Restore factory settings", fRow, th.Red, pf, new RectangleF(rp.X + F(18), rp.Y, rp.Width - F(36), rp.Height), 0);
            Txt(g, "Resets DPI, polling rate and buttons on the mouse", fSub, th.Secondary, pf, new RectangleF(rp.X + F(18), rp.Y, rp.Width - F(36), rp.Height), 2);
            AddHit(id, rp, delegate
            {
                if (!armed) { resetArmedAt = Environment.TickCount; return; }
                resetArmedAt = int.MinValue;
                app.Change(x => { }, x => RkM3.FactoryReset());
            });
            if (armed) Kick();

            Txt(g, "Settings go straight to the mouse over USB. The firmware check asks RK’s server only when you open this page.", fSub, th.Tertiary, cardFlat,
                new RectangleF(c.X, c.Bottom - F(20), c.Width, F(18)), 0);
        }

        // Nibble's own version vs. the latest GitHub release.
        void PaintUpdateRow(Graphics g, RectangleF p, int i, float rh)
        {
            var u = app.Upd;
            string sub = app.UpdChecking ? "Checking GitHub…"
                : u == null ? "Latest releases from GitHub"
                : u.Available ? "Version " + u.Tag + " is available"
                : u.NoReleases ? "No releases published yet"
                : u.Failed ? "Couldn’t reach GitHub"
                : "You’re up to date";
            RowLabel(g, p, i, rh, "Nibble " + Updates.Current, sub);
            float cy = p.Y + rh * i + rh / 2;
            var r = new RectangleF(p.Right - F(18) - F(130), cy - F(15), F(130), F(30));
            if (app.UpdChecking) return;
            if (u != null && u.Available)
                Button(g, r, "Get " + u.Tag, Color.White, "upd", delegate { app.OpenUrl(u.Url ?? Updates.ReleasesPage); }, th.Blue);
            else
                Button(g, r, u == null || u.Failed ? "Check now" : "Check again", th.Label, "upd", app.CheckUpdates);
        }

        // Firmware: installed vs. RK's published versions, with a hand-off to RK's official updater.
        float PaintFirmware(Graphics g, RectangleF c, float y)
        {
            float rh = F(58);
            var p = new RectangleF(c.X, y, c.Width, rh * 2);
            Platter(g, p);
            var fw = app.Fw;
            bool busy = app.FwChecking;

            // Receiver row
            string recNow = fw == null ? (busy ? "Reading…" : "—") : fw.Receiver ?? (app.Wired ? "Not in use (cable)" : "Unknown");
            RowLabel(g, p, 0, rh, "Receiver firmware", "Installed " + recNow + (fw != null && fw.LatestReceiver != null ? " · latest " + fw.LatestReceiver : ""));
            FirmwareAction(g, p, 0, rh, fw, busy, fw != null && fw.ReceiverUpdate, fw != null ? fw.LatestReceiver : null, fw != null ? fw.ReceiverUrl : null,
                fw != null && fw.Receiver != null, "fwrec");
            Sep(g, p, rh);

            // Mouse row (the mouse only reports its version over the cable)
            string mouseNow = fw == null ? (busy ? "Reading…" : "—") : fw.Mouse ?? "plug in the cable to read";
            RowLabel(g, p, 1, rh, "Mouse firmware", "Installed " + mouseNow + (fw != null && fw.LatestMouse != null ? " · latest " + fw.LatestMouse : ""));
            FirmwareAction(g, p, 1, rh, fw, busy, fw != null && fw.MouseUpdate, fw != null ? fw.LatestMouse : null, fw != null ? fw.MouseUrl : null,
                fw != null && fw.Mouse != null, "fwmouse");
            return p.Bottom;
        }

        void FirmwareAction(Graphics g, RectangleF p, int i, float rh, FirmwareInfo fw, bool busy, bool update, string latest, string url, bool known, string id)
        {
            var pf = PlatterFlat();
            float cy = p.Y + rh * i + rh / 2;
            if (busy || fw == null)
            {
                Txt(g, busy ? "Checking…" : "", fSub, th.Secondary, pf, new RectangleF(p.Right - F(218), cy - F(10), F(200), F(20)), 2);
                return;
            }
            if (fw.Failed && latest == null)
            {
                Button(g, new RectangleF(p.Right - F(18) - F(120), cy - F(15), F(120), F(30)), "Try again", th.Label, id, app.CheckFirmware);
                return;
            }
            if (update)
            {
                string label = "Get " + latest;
                Button(g, new RectangleF(p.Right - F(18) - F(120), cy - F(15), F(120), F(30)), label, Color.White, id, delegate { app.OpenUrl(url); }, th.Blue);
                return;
            }
            if (known)
            {
                Txt(g, "Up to date", fSmallSemi, th.Green, pf, new RectangleF(p.Right - F(218), cy - F(10), F(200), F(20)), 2);
                return;
            }
            // Version unknown (mouse on wireless): still offer RK's updater for the latest build.
            if (url != null)
                Button(g, new RectangleF(p.Right - F(18) - F(150), cy - F(15), F(150), F(30)), "Updater " + latest, th.Label, id, delegate { app.OpenUrl(url); });
        }

        // Same metrics as the GDI text renderer.
        Size MeasureT(string s, Font f) { return frame != null ? Size.Round(frame.Measure(s, f)) : TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.NoPadding); }

        void Button(Graphics g, RectangleF r, string label, Color fg, string id, Action a) { Button(g, r, label, fg, id, a, Color.Empty); }

        void Button(Graphics g, RectangleF r, string label, Color fg, string id, Action a, Color fill)
        {
            if (fill.IsEmpty) fill = pressed == id ? th.ControlPress : hover == id ? th.ControlHover : th.SegTrack;
            else if (hover == id) fill = Draw.Lerp(fill, Color.White, 0.12f);
            Draw.FillRound(g, Av(fill), r, r.Height / 2);
            Txt(g, label, fSmallSemi, fg, Blend(PlatterFlat(), fill), r, 1);
            AddHit(id, r, a);
        }

        // ---------- controls ----------

        Color PlatterHover() { return th.Dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(170, 255, 255, 255); }
        Color PlatterFlat() { return Blend(cardFlat, th.Platter); }

        void Platter(Graphics g, RectangleF r)
        {
            Draw.FillRound(g, Av(th.Platter), r, F(18));
            Draw.Rim(g, r, F(18), Av(th.PlatterRimTop), Av(th.PlatterRimBottom), Math.Max(1f, F(0.8f)));
        }

        void Sep(Graphics g, RectangleF p, float dy)
        {
            using (var pen = new Pen(Av(th.Separator), Math.Max(1f, F(0.8f)))) g.DrawLine(pen, p.X + F(18), p.Y + dy, p.Right - F(18), p.Y + dy);
        }

        void RowLabel(Graphics g, RectangleF p, int i, float rh, string title, string sub)
        {
            var pf = PlatterFlat();
            Txt(g, title, fRow, th.Label, pf, new RectangleF(p.X + F(18), p.Y + rh * i + rh / 2 - F(19), F(300), F(20)), 0);
            Txt(g, sub, fSub, th.Secondary, pf, new RectangleF(p.X + F(18), p.Y + rh * i + rh / 2 + F(1), F(330), F(18)), 0);
        }

        void ToggleRow(Graphics g, RectangleF p, int i, float rh, string title, string sub, int bit)
        {
            RowLabel(g, p, i, rh, title, sub);
            bool on = app.Config.Flag(bit);
            Switch(g, SwitchRect(p, i, rh), on, "bit" + bit, delegate { app.Change(x => x.SetFlag(bit, !x.Flag(bit)), WritePerf); });
        }

        RectangleF SwitchRect(RectangleF p, int i, float rh) { return new RectangleF(p.Right - F(18) - F(56), p.Y + rh * i + (rh - F(30)) / 2, F(56), F(30)); }

        void Switch(Graphics g, RectangleF r, bool on, string id, Action toggle)
        {
            float pos = A("sw:" + id, on ? 1 : 0);
            Draw.FillRound(g, Av(Draw.Lerp(th.SwitchOff, th.Green, pos)), r, r.Height / 2);
            float kw = F(pressed == id ? 38 : 34), kh = r.Height - F(4);
            var knob = new RectangleF(r.X + F(2) + pos * (r.Width - F(4) - kw), r.Y + F(2), kw, kh);
            Draw.FillRound(g, Av(Color.FromArgb(45, 0, 0, 0)), new RectangleF(knob.X, knob.Y + F(1.5f), kw, kh), kh / 2);
            Draw.FillRound(g, Av(Color.White), knob, kh / 2);
            AddHit(id, RectangleF.Inflate(r, F(6), F(6)), toggle);
        }

        void Segmented(Graphics g, RectangleF r, string[] items, int selected, string id, Action<int> pick)
        {
            float pos = A("seg:" + id, Math.Max(0, selected));
            Draw.FillRound(g, Av(th.SegTrack), r, r.Height / 2);
            float iw = r.Width / items.Length;
            var thumb = new RectangleF(r.X + pos * iw + F(2), r.Y + F(2), iw - F(4), r.Height - F(4));
            if (selected >= 0)
            {
                if (!th.Dark) Draw.FillRound(g, Av(Color.FromArgb(26, 0, 0, 0)), new RectangleF(thumb.X, thumb.Y + F(1), thumb.Width, thumb.Height), thumb.Height / 2);
                Draw.FillRound(g, Av(th.SegThumb), thumb, thumb.Height / 2);
                Draw.Rim(g, thumb, thumb.Height / 2, Av(th.RimTop), Av(th.RimBottom), Math.Max(1f, F(0.8f)));
            }
            var trackFlat = Blend(PlatterFlat(), th.SegTrack);
            var thumbFlat = Blend(trackFlat, th.SegThumb);
            for (int i = 0; i < items.Length; i++)
            {
                var ir = new RectangleF(r.X + i * iw, r.Y, iw, r.Height);
                bool on = Math.Abs(pos - i) < 0.5f;
                if (!on && hover == id + i) Draw.FillRound(g, Av(th.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(60, 255, 255, 255)), RectangleF.Inflate(ir, -F(2), -F(2)), (r.Height - F(4)) / 2);
                Txt(g, items[i], fSmallSemi, on ? th.Label : th.Secondary, on ? thumbFlat : trackFlat, ir, 1);
                int idx = i;
                if (i != selected) AddHit(id + i, ir, delegate { pick(idx); });
            }
        }

        void Stepper(Graphics g, RectangleF r, string value, string id, Action minus, Action plus)
        {
            Draw.FillRound(g, Av(th.SegTrack), r, r.Height / 2);
            var m = new RectangleF(r.X, r.Y, r.Height * 1.3f, r.Height);
            var p = new RectangleF(r.Right - r.Height * 1.3f, r.Y, r.Height * 1.3f, r.Height);
            var flat = Blend(PlatterFlat(), th.SegTrack);
            if (hover == id + "-") Draw.FillRound(g, Av(th.ControlHover), RectangleF.Inflate(m, -F(2), -F(2)), (r.Height - F(4)) / 2);
            if (hover == id + "+") Draw.FillRound(g, Av(th.ControlHover), RectangleF.Inflate(p, -F(2), -F(2)), (r.Height - F(4)) / 2);
            using (var pen = GPen(th.Label, 2f))
            {
                float cy = r.Y + r.Height / 2, h = F(5.5f), cx = m.X + m.Width / 2;
                g.DrawLine(pen, cx - h, cy, cx + h, cy);
                cx = p.X + p.Width / 2;
                g.DrawLine(pen, cx - h, cy, cx + h, cy); g.DrawLine(pen, cx, cy - h, cx, cy + h);
            }
            Txt(g, value, fSmallSemi, th.Label, flat, r, 1);
            AddHit(id + "-", m, minus);
            AddHit(id + "+", p, plus);
        }

        void Chip(Graphics g, RectangleF r, string label, bool selected, string id, Action a)
        {
            Color fill = selected ? th.Blue : (hover == id ? th.ControlHover : th.SegTrack);
            Draw.FillRound(g, Av(fill), r, r.Height / 2);
            Txt(g, label, fSmallSemi, selected ? Color.White : th.Label, selected ? Blend(PlatterFlat(), th.Blue) : Blend(PlatterFlat(), fill), r, 1);
            if (!selected) AddHit(id, r, a);
        }

        void Circle(Graphics g, RectangleF r, string id)
        {
            Color fill = pressed == id ? th.ControlPress : hover == id ? th.ControlHover : th.Control;
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, r);
        }

        Pen GPen(Color c, float w)
        {
            var p = new Pen(Av(c), F(w));
            p.StartCap = p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }

        void AddHit(string id, RectangleF r, Action a)
        {
            if (pageT < 1 && id.StartsWith("nav") == false && id != "close") return; // ignore taps while a page fades in
            hits.Add(new Hit { Id = id, R = new RectangleF(r.X + tx, r.Y + ty, r.Width, r.Height), Do = a });
        }

        // GDI text straight into the DIB for native ClearType; translucent colours are pre-blended
        // onto the surface below. GDI ignores GDI+ transforms, so the card offset is applied by hand.
        void Txt(Graphics g, string s, Font f, Color c, Color under, RectangleF r, int align)
        {
            float k = c.A / 255f * va;
            var col = Color.FromArgb(255, (int)(under.R + (c.R - under.R) * k), (int)(under.G + (c.G - under.G) * k), (int)(under.B + (c.B - under.B) * k));
            g.Flush(FlushIntention.Sync);
            frame.Text(s, f, col, Rectangle.Round(new RectangleF(r.X + tx, r.Y + ty, r.Width, r.Height)), align);
        }

        // ---------- input ----------

        Hit HitAt(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return hits[i];
            return null;
        }

        int SliderValue(int x)
        {
            float frac = (x - tx - sliderRect.X) / sliderRect.Width;
            frac = Math.Max(0, Math.Min(1, frac));
            return (int)Math.Round((MinDpi + frac * (MaxDpi - MinDpi)) / 50.0) * 50;
        }

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
                Push((byte)255);
                return;
            }
            if (dragging) { dragValue = SliderValue(e.X); InvalidateCard(); return; }
            var h = HitAt(e.Location);
            string id = h == null ? null : h.Id;
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            if (id != hover) { hover = id; InvalidateCard(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var h = HitAt(e.Location);
            pressed = h == null ? null : h.Id;
            if (pressed == "slider" && app.Config != null) { dragging = true; dragValue = SliderValue(e.X); Capture = true; }
            else if (h == null && IsDragZone(e.Location))
            {
                var s = Cursor.Position;
                moving = true; moveDX = s.X - winX; moveDY = s.Y - winY; Capture = true;
                return;
            }
            InvalidateCard();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (moving)
            {
                moving = false; Capture = false;
                Location = new Point(winX, winY);
                Refrost();   // glass now reflects the new spot
                return;
            }
            if (dragging)
            {
                dragging = false; Capture = false;
                SetStageDpi(dragValue);
                pressed = null; InvalidateCard();
                return;
            }
            var h = HitAt(e.Location);
            string p = pressed;
            pressed = null;
            if (e.Button == MouseButtons.Left && h != null && h.Id == p) h.Do();
            Kick();
        }

        // ---------- design review ----------

        public void Snapshot(string path, bool dark, int pageIndex)
        {
            th = Theme.Current(dark);
            int pad = Pi(40);
            var size = new Size(Pi(CW + 2 * M), Pi(CH + 2 * M));
            var canvas = new Size(size.Width + 2 * pad, size.Height + 2 * pad);
            using (var wall = new Bitmap(canvas.Width, canvas.Height))
            {
                using (var g = Graphics.FromImage(wall))
                using (var br = new LinearGradientBrush(new Rectangle(Point.Empty, canvas), Theme.Hex(0x1D2B64), Theme.Hex(0xC06C84), 35f))
                {
                    g.FillRectangle(br, 0, 0, canvas.Width, canvas.Height);
                    using (var b2 = new SolidBrush(Theme.Hex(0x2EC4B6))) g.FillEllipse(b2, -canvas.Width * 0.1f, canvas.Height * 0.5f, canvas.Width * 0.5f, canvas.Width * 0.5f);
                    using (var b3 = new SolidBrush(Theme.Hex(0xFF9F0A))) g.FillEllipse(b3, canvas.Width * 0.7f, -canvas.Height * 0.2f, canvas.Width * 0.4f, canvas.Width * 0.4f);
                }
                card = new RectangleF(F(M), F(M), F(CW), F(CH));
                frame = new Surface(size.Width, size.Height);
                frame.SetMask(card, F(CR));
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
