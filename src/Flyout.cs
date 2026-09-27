using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Nibble
{
    // Liquid Glass popover: a per-pixel-alpha layered window drawn with GDI+.
    // Rendering only happens while visible and animating; all bitmaps are freed on hide.
    class Flyout : Form
    {
        #region native
        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref PT pos, ref SZ size, IntPtr src, ref PT srcPos, int key, ref BLEND blend, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        #endregion

        static readonly int[] Intervals = { 30, 60, 300, 900 };
        static readonly string[] IntervalNames = { "30s", "1m", "5m", "15m" };

        // Geometry in logical px. Inner radii are concentric: outer radius minus padding.
        const float PW = 340, PH = 294, M = 2, R = 34, Pad = 16;

        readonly TrayApp app;
        readonly float S;
        Theme th;
        readonly Font fNum, fPct, fName, fSub, fTileVal, fTileCap, fTitle, fRow, fSeg, fBtn, fCharge;
        readonly StringFormat sfL, sfC, sfR;

        Bitmap backdrop;
        Surface frame;
        Color bgAvg;
        readonly Timer anim, clock;
        int winX, winY;

        float openP; int openT0;
        float ring, ringFrom, ringTo; int ringT0;
        float nav, navFrom, navTo; int navT0;
        float seg, sw1, sw2, spin;

        float va = 1;              // alpha multiplier for the view being drawn
        float tx, ty;              // current translation, for hit rects
        bool recordHits;
        string hover, pressed;
        readonly List<Hit> hits = new List<Hit>();
        public DateTime HiddenAt = DateTime.MinValue;

        class Hit { public string Id; public RectangleF R; public Action Do; }

        public Flyout(TrayApp app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Text = TrayApp.AppName;

            using (var g = CreateGraphics()) S = g.DpiX / 96f;
            Size = new Size(Px(PW + 2 * M), Px(PH + 2 * M));

            string disp = Draw.PickFont("Segoe UI Variable Display Semib", "Segoe UI Semibold");
            string text = Draw.PickFont("Segoe UI Variable Text", "Segoe UI");
            string textSb = Draw.PickFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
            fNum = PxFont(disp, 60); fPct = PxFont(disp, 26); fTitle = PxFont(disp, 18); fCharge = PxFont(disp, 36);
            fName = PxFont(textSb, 15); fTileVal = PxFont(textSb, 15); fSeg = PxFont(textSb, 13); fBtn = PxFont(textSb, 15);
            fSub = PxFont(text, 13); fTileCap = PxFont(text, 12); fRow = PxFont(text, 15);

            sfL = MakeFormat(StringAlignment.Near);
            sfC = MakeFormat(StringAlignment.Center);
            sfR = MakeFormat(StringAlignment.Far);

            anim = new Timer { Interval = 15 };
            anim.Tick += delegate { Tick(); };
            clock = new Timer { Interval = 1000 };
            clock.Tick += delegate { if (!anim.Enabled) Render(); };

            th = Theme.Current();
            app.StateChanged += OnState;
        }

        static StringFormat MakeFormat(StringAlignment h)
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.Alignment = h;
            f.LineAlignment = StringAlignment.Center;
            f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }

        Font PxFont(string family, float px) { return new Font(family, px * S, FontStyle.Regular, GraphicsUnit.Pixel); }
        int Px(float v) { return (int)Math.Round(v * S); }
        float F(float v) { return v * S; }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80; // WS_EX_LAYERED | WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public void ThemeChanged() { if (Visible) HideFlyout(); }

        // ---------- show / hide ----------

        public void ShowFlyout()
        {
            th = Theme.Current();
            var scr = Screen.FromPoint(Cursor.Position);
            Rectangle wa = scr.WorkingArea, b = scr.Bounds;
            int gap = Px(12), pw = Px(PW), ph = Px(PH), m = Px(M);
            int px = wa.Right - pw - gap, py = wa.Bottom - ph - gap;
            if (wa.Top > b.Top) py = wa.Top + gap;
            else if (wa.Left > b.Left) px = wa.Left + gap;
            winX = px - m; winY = py - m;

            using (var cap = Glass.Capture(new Rectangle(px, py, pw, ph)))
                BuildSurfaces(cap);

            nav = navFrom = navTo = 0;
            ring = 0; StartRing(Target());
            seg = SegIndex(); sw1 = app.AutoStart ? 1 : 0; sw2 = app.LowAlert ? 1 : 0;
            openP = 0; openT0 = Environment.TickCount;
            hover = pressed = null;

            Location = new Point(winX, winY);
            Render();
            Show();
            Activate();
            clock.Start();
            Kick();
        }

        void BuildSurfaces(Bitmap capture)
        {
            FreeSurfaces();
            var panel = new Size(Px(PW), Px(PH));
            backdrop = Glass.Frost(capture, panel, th.Tint, th.Fallback, 1.8f);
            bgAvg = Glass.Average(backdrop);
            frame = new Surface(Size.Width, Size.Height);
            frame.SetMask(new RectangleF(F(M), F(M), F(PW), F(PH)), F(R));
        }

        void FreeSurfaces()
        {
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
        }

        public void HideFlyout()
        {
            if (!Visible) return;
            Hide();
            HiddenAt = DateTime.Now;
            anim.Stop();
            clock.Stop();
            FreeSurfaces();
            app.Trim(true);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HideFlyout();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { if (navTo > 0) Navigate(0); else HideFlyout(); }
            else if (e.KeyCode == Keys.F5 || e.KeyCode == Keys.R) app.RefreshNow();
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && Visible) { e.Cancel = true; HideFlyout(); }
            base.OnFormClosing(e);
        }

        // ---------- animation ----------

        float Target() { return app.Percent >= 0 ? app.Percent / 100f : 0f; }
        float SegIndex() { return Math.Max(0, Array.IndexOf(Intervals, app.IntervalSec)); }

        void StartRing(float to) { ringFrom = ring; ringTo = to; ringT0 = Environment.TickCount; }

        void Navigate(float to) { navFrom = nav; navTo = to; navT0 = Environment.TickCount; hover = pressed = null; Kick(); }

        void OnState(object s, EventArgs e)
        {
            if (!Visible) return;
            if (Math.Abs(Target() - ringTo) > 0.001f) StartRing(Target());
            Kick();
        }

        void Kick() { if (Visible && !anim.Enabled) anim.Start(); }

        static float EaseOut(float p) { p = Clamp01(p); return 1 - (float)Math.Pow(1 - p, 3); }
        static float EaseInOut(float p) { p = Clamp01(p); return p < 0.5f ? 4 * p * p * p : 1 - (float)Math.Pow(-2 * p + 2, 3) / 2; }
        static float Clamp01(float p) { return p < 0 ? 0 : (p > 1 ? 1 : p); }
        static float Approach(float v, float to, float k) { float d = to - v; return Math.Abs(d) < 0.003f ? to : v + d * k; }

        void Tick()
        {
            int now = Environment.TickCount;
            bool more = false;

            openP = EaseOut((now - openT0) / 260f);
            if (openP < 1) more = true;

            float rp = (now - ringT0) / 800f;
            ring = ringFrom + (ringTo - ringFrom) * EaseOut(rp);
            if (rp < 1) more = true;

            float np = (now - navT0) / 380f;
            nav = navFrom + (navTo - navFrom) * EaseInOut(np);
            if (np < 1) more = true;

            float segTo = SegIndex(), a1 = app.AutoStart ? 1 : 0, a2 = app.LowAlert ? 1 : 0;
            seg = Approach(seg, segTo, 0.3f); sw1 = Approach(sw1, a1, 0.3f); sw2 = Approach(sw2, a2, 0.3f);
            if (seg != segTo || sw1 != a1 || sw2 != a2) more = true;

            if (app.Busy) { spin = (spin + 10) % 360; more = true; }
            else if (spin != 0) { spin = spin + 14 >= 360 ? 0 : spin + 14; more = true; }

            Render();
            if (!more) anim.Stop();
        }

        // ---------- rendering ----------

        void Render()
        {
            if (frame == null || !IsHandleCreated) return;
            using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
            frame.ApplyMask();
            Push((byte)(255 * openP), (int)Math.Round((1 - openP) * F(10)));
        }

        void Compose(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.None;
            var panel = new RectangleF(F(M), F(M), F(PW), F(PH));
            // Rounded corners come from the surface mask, so plain rectangle fills are enough here.
            g.DrawImageUnscaled(backdrop, Px(M), Px(M));
            // Soft sheen toward the top edge, like light falling across glass.
            using (var sheen = new LinearGradientBrush(panel, Color.FromArgb(th.Dark ? 20 : 60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            {
                sheen.SetBlendTriangularShape(0f, 1f);
                g.FillRectangle(sheen, panel);
            }

            hits.Clear();
            var clip = g.Clip;
            g.SetClip(new RectangleF(panel.X + F(2), panel.Y + F(2), panel.Width - F(4), panel.Height - F(4)));
            if (nav < 0.999f) DrawView(g, false, -nav * F(PW) * 0.3f, 1 - nav, navTo < 0.5f);
            if (nav > 0.001f) DrawView(g, true, (1 - nav) * F(PW), Clamp01(nav * 1.4f), navTo > 0.5f);
            g.Clip = clip;

            Draw.Rim(g, panel, F(R), th.RimTop, th.RimBottom, Math.Max(1f, F(1.1f)));
        }

        void DrawView(Graphics g, bool settings, float ox, float alpha, bool interactive)
        {
            var st = g.Save();
            tx = F(M) + ox; ty = F(M);
            g.TranslateTransform(tx, ty);
            va = alpha;
            recordHits = interactive;
            if (settings) PaintSettings(g); else PaintMain(g);
            g.Restore(st);
            va = 1;
        }

        void Push(byte alpha, int dy)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            var size = new SZ { W = frame.W, H = frame.H };
            var src = new PT();
            var pos = new PT { X = winX, Y = winY + dy };
            var blend = new BLEND { Op = 0, Flags = 0, Alpha = alpha, Format = 1 };
            UpdateLayeredWindow(Handle, screen, ref pos, ref size, frame.Dc, ref src, 0, ref blend, 2);
            ReleaseDC(IntPtr.Zero, screen);
        }

        // ---------- main view ----------

        void PaintMain(Graphics g)
        {
            // Battery ring with the mouse inside, like the iOS Batteries widget.
            float d = F(104), stroke = F(10), rx = F(22), ry = F(24);
            var rr = new RectangleF(rx + stroke / 2, ry + stroke / 2, d - stroke, d - stroke);
            float x = F(148), w = F(PW - 148 - 20);
            bool chargingUnknown = app.Online && app.ChargeFromLast;

            if (chargingUnknown)
            {
                // The M3 hides its level while charging: a soft green ring with a bolt, and a
                // "Charging" headline, instead of a number.
                using (var p = new Pen(A(Draw.Alpha(th.Green, 0.35f)), stroke)) g.DrawEllipse(p, rr);
                Draw.Bolt(g, A(th.Green), new RectangleF(rx + d / 2 - F(13), ry + d / 2 - F(20), F(26), F(40)));
                Txt(g, "RK M3", fName, th.Secondary, new RectangleF(x, F(30), w, F(18)), sfL);
                Txt(g, "Charging", fCharge, th.Label, new RectangleF(x - F(2), F(52), w + F(10), F(46)), sfL);
                Txt(g, app.Wired ? "Over USB cable" : "Plugged in", fSub, th.Green, new RectangleF(x, F(102), w, F(18)), sfL);
            }
            else
            {
                using (var p = new Pen(A(th.Track), stroke)) g.DrawEllipse(p, rr);
                if (ring > 0.004f)
                    using (var p = new Pen(A(RingColor()), stroke))
                    {
                        p.StartCap = p.EndCap = LineCap.Round;
                        g.DrawArc(p, rr, -90, 360 * Math.Min(1, ring));
                    }
                MouseGlyph(g, rx + d / 2, ry + d / 2, app.Online ? Draw.Alpha(th.Label, 0.9f) : th.Secondary);

                // Headline number
                Txt(g, "RK M3", fName, th.Secondary, new RectangleF(x, F(30), w, F(18)), sfL);
                bool known = app.Percent >= 0;
                string num = known ? ((int)Math.Round(ring * 100)).ToString() : "\u2014";
                SizeF ns = Measure(num, fNum);
                var nr = new RectangleF(x - F(3), F(46), ns.Width + F(4), F(62));
                Txt(g, num, fNum, app.Online ? th.Label : th.Secondary, nr, sfL);
                if (known)
                {
                    float baseline = nr.Y + nr.Height / 2 + Ascent(fNum) - LineH(fNum) / 2;
                    float top = baseline - Ascent(fPct);
                    Txt(g, "%", fPct, th.Secondary, new RectangleF(nr.X + ns.Width + F(2), top, F(30), LineH(fPct)), sfL);
                }

                // Status line
                string status = HeroStatus();
                Color sc = th.Secondary;
                if (app.Online && (app.Charging || app.FullyCharged)) sc = th.Green;
                else if (app.Online && known && app.Percent <= 20) sc = th.Red;
                float sx = x;
                if (app.Online && app.Charging) { Draw.Bolt(g, A(sc), new RectangleF(sx, F(114), F(9), F(14))); sx += F(14); }
                Txt(g, status, fSub, sc, new RectangleF(sx, F(112), w - (sx - x), F(18)), sfL);
            }
            // Three glass tiles
            float ty0 = F(150), tw = F((PW - 2 * Pad - 16) / 3f), th0 = F(74);
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(F(Pad) + i * (tw + F(8)), ty0, tw, th0);
                Platter(g, r, F(20));
                var ir = new RectangleF(r.X + F(13), r.Y + F(13), F(16), F(16));
                string val, cap;
                if (i == 0)
                {
                    if (app.Found && app.Wired) PlugGlyph(g, ir, th.Blue); else WaveGlyph(g, ir, app.Online ? th.Blue : th.Secondary);
                    val = ConnectionText(); cap = "Connection";
                }
                else if (i == 1)
                {
                    DpiGlyph(g, ir, th.Orange);
                    val = app.CurrentDpi > 0 ? app.CurrentDpi.ToString() : "—";
                    cap = app.Config != null && app.Config.Stages > 1 ? "DPI · Stage " + app.CurrentStage : "DPI";
                }
                else
                {
                    ClockGlyph(g, ir, th.Green);
                    val = Ago(app.Updated); cap = "Synced";
                }
                Txt(g, val, fTileVal, th.Label, new RectangleF(r.X + F(13), r.Y + F(36), r.Width - F(18), F(18)), sfL);
                Txt(g, cap, fTileCap, th.Secondary, new RectangleF(r.X + F(13), r.Y + F(54), r.Width - F(18), F(14)), sfL);
            }

            // Floating control bar
            float by = F(238), bd = F(40);
            var gear = new RectangleF(F(PW - Pad) - bd, by, bd, bd);
            var refresh = new RectangleF(gear.X - F(8) - bd, by, bd, bd);
            GlassButton(g, gear, bd / 2, "settings", delegate { Navigate(1); });
            SlidersGlyph(g, gear, th.Label);
            GlassButton(g, refresh, bd / 2, "refresh", app.RefreshNow);
            RefreshGlyph(g, refresh, th.Label);

            string dl = "Customize";
            float tw1 = Measure(dl, fBtn).Width;
            var drv = new RectangleF(F(Pad), by, tw1 + F(18 + 10 + 9 + 16), bd);
            GlassButton(g, drv, bd / 2, "customize", delegate { HideFlyout(); app.OpenSettings(); });
            Txt(g, dl, fBtn, th.Label, new RectangleF(drv.X + F(18), drv.Y, tw1 + F(4), bd), sfL);
            ArrowGlyph(g, new RectangleF(drv.X + F(18) + tw1 + F(10), drv.Y + bd / 2 - F(4.5f), F(9), F(9)), th.Secondary);
        }

        // ---------- settings view ----------

        void PaintSettings(Graphics g)
        {
            float bd = F(40);
            var back = new RectangleF(F(Pad), F(16), bd, bd);
            GlassButton(g, back, bd / 2, "back", delegate { Navigate(0); });
            ChevronGlyph(g, back, th.Label);
            Txt(g, "Settings", fTitle, th.Label, new RectangleF(0, F(16), F(PW), bd), sfC);

            var grp = new RectangleF(F(Pad), F(70), F(PW - 2 * Pad), F(150));
            Platter(g, grp, F(22));
            float rh = F(50);

            // Refresh interval: capsule segmented control
            var r0 = new RectangleF(grp.X, grp.Y, grp.Width, rh);
            Txt(g, "Refresh", fRow, th.Label, new RectangleF(r0.X + F(16), r0.Y, F(90), rh), sfL);
            float sw = F(168), sh = F(32);
            var sr = new RectangleF(r0.Right - F(10) - sw, r0.Y + (rh - sh) / 2, sw, sh);
            Draw.FillRound(g, A(th.SegTrack), sr, sh / 2);
            float iw = sw / Intervals.Length;
            var thumb = new RectangleF(sr.X + seg * iw + F(2), sr.Y + F(2), iw - F(4), sh - F(4));
            if (!th.Dark) Draw.FillRound(g, A(Color.FromArgb(28, 0, 0, 0)), new RectangleF(thumb.X, thumb.Y + F(1), thumb.Width, thumb.Height), thumb.Height / 2);
            Draw.FillRound(g, A(th.SegThumb), thumb, thumb.Height / 2);
            Draw.Rim(g, thumb, thumb.Height / 2, A(th.RimTop), A(th.RimBottom), Math.Max(1f, F(0.8f)));
            for (int i = 0; i < Intervals.Length; i++)
            {
                var ir = new RectangleF(sr.X + i * iw, sr.Y, iw, sh);
                bool on = Math.Abs(seg - i) < 0.5f;
                Txt(g, IntervalNames[i], fSeg, on ? th.Label : th.Secondary, ir, sfC);
                int idx = i;
                AddHit("seg" + i, ir, delegate { app.SetInterval(Intervals[idx]); });
            }
            Separator(g, grp.X + F(16), grp.Right - F(16), r0.Bottom);

            var r1 = new RectangleF(grp.X, grp.Y + rh, grp.Width, rh);
            SwitchRow(g, r1, "Launch at Login", sw1, "sw1", delegate { app.AutoStart = !app.AutoStart; });
            Separator(g, grp.X + F(16), grp.Right - F(16), r1.Bottom);
            var r2 = new RectangleF(grp.X, grp.Y + rh * 2, grp.Width, rh);
            SwitchRow(g, r2, "Battery Alerts", sw2, "sw2", delegate { app.SetLowAlert(!app.LowAlert); });

            var quit = new RectangleF(F(Pad), F(232), F(PW - 2 * Pad), F(46));
            GlassButton(g, quit, quit.Height / 2, "quit", app.Quit);
            Txt(g, "Quit Nibble", fBtn, th.Red, quit, sfC);
        }

        void SwitchRow(Graphics g, RectangleF r, string label, float pos, string id, Action toggle)
        {
            Txt(g, label, fRow, th.Label, new RectangleF(r.X + F(16), r.Y, r.Width - F(100), r.Height), sfL);
            float w = F(56), h = F(30);
            var tr = new RectangleF(r.Right - F(14) - w, r.Y + (r.Height - h) / 2, w, h);
            Draw.FillRound(g, A(Draw.Lerp(th.SwitchOff, th.Green, pos)), tr, h / 2);
            // iOS 26 style pill knob, stretching slightly while pressed.
            float kw = F(pressed == id ? 38 : 34), kh = h - F(4);
            var knob = new RectangleF(tr.X + F(2) + pos * (w - F(4) - kw), tr.Y + F(2), kw, kh);
            Draw.FillRound(g, A(Color.FromArgb(40, 0, 0, 0)), new RectangleF(knob.X, knob.Y + F(1.5f), knob.Width, knob.Height), kh / 2);
            Draw.FillRound(g, A(Color.White), knob, kh / 2);
            AddHit(id, r, toggle);
        }

        // ---------- primitives ----------

        Color A(Color c) { return va >= 1 ? c : Draw.Alpha(c, va); }

        void Platter(Graphics g, RectangleF r, float rad)
        {
            Draw.FillRound(g, A(th.Platter), r, rad);
            Draw.Rim(g, r, rad, A(th.PlatterRimTop), A(th.PlatterRimBottom), Math.Max(1f, F(0.9f)));
        }

        void GlassButton(Graphics g, RectangleF r, float rad, string id, Action a)
        {
            Color fill = pressed == id ? th.ControlPress : (hover == id ? th.ControlHover : th.Control);
            if (!th.Dark) Draw.FillRound(g, A(Color.FromArgb(18, 0, 0, 0)), new RectangleF(r.X, r.Y + F(1.5f), r.Width, r.Height), rad);
            Draw.FillRound(g, A(fill), r, rad);
            Draw.Rim(g, r, rad, A(th.RimTop), A(th.RimBottom), Math.Max(1f, F(0.9f)));
            AddHit(id, r, a);
        }

        void Separator(Graphics g, float x1, float x2, float y)
        {
            using (var p = new Pen(A(th.Separator), Math.Max(1f, F(0.8f)))) g.DrawLine(p, x1, y, x2, y);
        }

        void AddHit(string id, RectangleF r, Action a)
        {
            if (!recordHits) return;
            hits.Add(new Hit { Id = id, R = new RectangleF(r.X + tx, r.Y + ty, r.Width, r.Height), Do = a });
        }

        void Txt(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
        {
            // GDI text for native ClearType. It ignores GDI+ transforms, so apply the view offset by hand,
            // and flush pending GDI+ drawing first so text lands on top of it.
            g.Flush(FlushIntention.Sync);
            int align = sf == sfC ? 1 : (sf == sfR ? 2 : 0);
            var rr = Rectangle.Round(new RectangleF(r.X + tx, r.Y + ty, r.Width, r.Height));
            frame.Text(s, f, Flat(c), rr, align);
        }

        // Opaque equivalent of a translucent color over the frosted backdrop, including view fade.
        Color Flat(Color c)
        {
            float k = c.A / 255f * va;
            return Color.FromArgb(255,
                (int)(bgAvg.R + (c.R - bgAvg.R) * k),
                (int)(bgAvg.G + (c.G - bgAvg.G) * k),
                (int)(bgAvg.B + (c.B - bgAvg.B) * k));
        }

        SizeF Measure(string s, Font f) { return frame.Measure(s, f); }

        float Ascent(Font f) { return f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style); }
        float LineH(Font f) { return f.Size * (f.FontFamily.GetCellAscent(f.Style) + f.FontFamily.GetCellDescent(f.Style)) / f.FontFamily.GetEmHeight(f.Style); }

        Pen GlyphPen(Color c, float w)
        {
            var p = new Pen(A(c), F(w));
            p.StartCap = p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }

        void MouseGlyph(Graphics g, float cx, float cy, Color c)
        {
            var body = new RectangleF(cx - F(13), cy - F(20), F(26), F(40));
            using (var p = GlyphPen(c, 2.2f))
            using (var path = Draw.Round(body, F(13)))
            {
                g.DrawPath(p, path);
                g.DrawLine(p, cx, body.Y + F(1), cx, body.Y + F(13));
            }
            Draw.FillRound(g, A(c), new RectangleF(cx - F(1.6f), body.Y + F(6), F(3.2f), F(6)), F(1.6f));
        }

        void RefreshGlyph(Graphics g, RectangleF r, Color c)
        {
            var st = g.Save();
            g.TranslateTransform(r.X + r.Width / 2, r.Y + r.Height / 2);
            g.RotateTransform(spin);
            float rad = F(7);
            using (var p = GlyphPen(c, 1.9f))
            using (var cap = new AdjustableArrowCap(2.3f, 2.3f, true))
            {
                p.CustomEndCap = cap;
                g.DrawArc(p, -rad, -rad, rad * 2, rad * 2, -60, 290);
            }
            g.Restore(st);
        }

        void SlidersGlyph(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, hw = F(8);
            float[] ys = { -F(5), F(0), F(5) };
            float[] ks = { F(3), -F(3.5f), F(1.5f) };
            using (var p = GlyphPen(c, 1.8f))
                for (int i = 0; i < 3; i++) g.DrawLine(p, cx - hw, cy + ys[i], cx + hw, cy + ys[i]);
            using (var b = new SolidBrush(A(c)))
            using (var hole = new SolidBrush(A(th.Dark ? Color.FromArgb(255, 60, 60, 66) : Color.White)))
                for (int i = 0; i < 3; i++)
                {
                    g.FillEllipse(b, cx + ks[i] - F(3), cy + ys[i] - F(3), F(6), F(6));
                    g.FillEllipse(hole, cx + ks[i] - F(1.4f), cy + ys[i] - F(1.4f), F(2.8f), F(2.8f));
                }
        }

        void ChevronGlyph(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2 - F(1), cy = r.Y + r.Height / 2;
            using (var p = GlyphPen(c, 2.2f))
                g.DrawLines(p, new[] { new PointF(cx + F(3.5f), cy - F(7)), new PointF(cx - F(3.5f), cy), new PointF(cx + F(3.5f), cy + F(7)) });
        }

        void ArrowGlyph(Graphics g, RectangleF r, Color c)
        {
            using (var p = GlyphPen(c, 1.8f))
            {
                g.DrawLine(p, r.X, r.Bottom, r.Right, r.Y);
                g.DrawLines(p, new[] { new PointF(r.X + r.Width * 0.3f, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Y + r.Height * 0.7f) });
            }
        }

        void WaveGlyph(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2, by = r.Bottom - F(1);
            using (var p = GlyphPen(c, 1.8f))
            {
                g.DrawArc(p, cx - F(8), by - F(8) - F(4), F(16), F(16), 225, 90);
                g.DrawArc(p, cx - F(4.5f), by - F(4.5f) - F(4), F(9), F(9), 225, 90);
            }
            using (var b = new SolidBrush(A(c))) g.FillEllipse(b, cx - F(1.8f), by - F(4.6f), F(3.6f), F(3.6f));
        }

        void PlugGlyph(Graphics g, RectangleF r, Color c)
        {
            using (var p = GlyphPen(c, 1.8f))
            {
                g.DrawLine(p, r.X + F(5), r.Y + F(1), r.X + F(5), r.Y + F(5));
                g.DrawLine(p, r.X + F(11), r.Y + F(1), r.X + F(11), r.Y + F(5));
                using (var path = Draw.Round(new RectangleF(r.X + F(2.5f), r.Y + F(5), F(11), F(6)), F(2))) g.DrawPath(p, path);
                g.DrawLine(p, r.X + F(8), r.Y + F(11), r.X + F(8), r.Bottom);
            }
        }

        void DpiGlyph(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var p = GlyphPen(c, 1.8f))
            {
                g.DrawEllipse(p, cx - F(5.5f), cy - F(5.5f), F(11), F(11));
                g.DrawLine(p, cx, r.Y, cx, cy - F(3)); g.DrawLine(p, cx, cy + F(3), cx, r.Bottom);
                g.DrawLine(p, r.X, cy, cx - F(3), cy); g.DrawLine(p, cx + F(3), cy, r.Right, cy);
            }
        }

        void ClockGlyph(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var p = GlyphPen(c, 1.8f))
            {
                g.DrawEllipse(p, r.X + F(0.5f), r.Y + F(0.5f), r.Width - F(1), r.Height - F(1));
                g.DrawLines(p, new[] { new PointF(cx, cy - F(4)), new PointF(cx, cy), new PointF(cx + F(3), cy + F(2)) });
            }
        }

        // ---------- input ----------

        Hit HitAt(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return hits[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = HitAt(e.Location);
            string id = h == null ? null : h.Id;
            Cursor = h == null ? Cursors.Default : Cursors.Hand;
            if (id != hover) { hover = id; if (!anim.Enabled) Render(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = null; if (!anim.Enabled) Render(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var h = HitAt(e.Location);
            pressed = h == null ? null : h.Id;
            if (!anim.Enabled) Render();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var h = HitAt(e.Location);
            string p = pressed;
            pressed = null;
            if (h != null && h.Id == p && e.Button == MouseButtons.Left) h.Do();
            Kick();
        }

        // ---------- copy ----------

        Color RingColor()
        {
            if (!app.Online) return th.Tertiary;
            if (app.Charging) return th.Green;
            if (app.Percent <= 20) return th.Red;
            return th.Green;
        }

        string HeroStatus()
        {
            if (!app.Found) return "Plug in the receiver";
            if (!app.Online) return app.Percent >= 0 ? "Asleep · " + Ago(app.Updated).ToLowerInvariant() : "Move mouse to wake";
            return app.StatusText();
        }

        string ConnectionText()
        {
            if (!app.Found) return "None";
            if (!app.Online) return "Asleep";
            return app.Wired ? "USB" : "2.4 GHz";
        }

        static string Ago(DateTime t)
        {
            if (t == DateTime.MinValue) return "Never";
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 10) return "Just now";
            if (d.TotalSeconds < 60) return string.Format("{0}s ago", (int)d.TotalSeconds);
            if (d.TotalMinutes < 60) return string.Format("{0}m ago", (int)d.TotalMinutes);
            if (t.Date == DateTime.Today) return t.ToString("HH:mm");
            return t.ToString("d MMM");
        }

        // ---------- design review ----------

        // Renders the panel over a synthetic wallpaper: Nibble.exe --snapshot out.png dark|light ...
        public void Snapshot(string path, bool dark, bool settings)
        {
            th = Theme.Current(dark);
            var canvas = new Rectangle(0, 0, Size.Width, Size.Height);
            using (var wall = Wallpaper(canvas.Size))
            {
                using (var cap = wall.Clone(new Rectangle(Px(M), Px(M), Px(PW), Px(PH)), wall.PixelFormat))
                    BuildSurfaces(cap);
                ring = Target(); seg = SegIndex(); sw1 = app.AutoStart ? 1 : 0; sw2 = app.LowAlert ? 1 : 0;
                nav = settings ? 1 : 0; navTo = nav; openP = 1;
                using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
                frame.ApplyMask();
                using (var g = Graphics.FromImage(wall)) g.DrawImageUnscaled(frame.Bmp, 0, 0);
                wall.Save(path, ImageFormat.Png);
            }
            FreeSurfaces();
        }

        static Bitmap Wallpaper(Size s)
        {
            var b = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new LinearGradientBrush(new Rectangle(Point.Empty, s), Theme.Hex(0x1D2B64), Theme.Hex(0x7B3F9E), 60f))
                    g.FillRectangle(br, 0, 0, s.Width, s.Height);
                using (var br = new SolidBrush(Theme.Hex(0xFF7A45))) g.FillEllipse(br, s.Width * 0.55f, s.Height * 0.05f, s.Width * 0.6f, s.Width * 0.6f);
                using (var br = new SolidBrush(Theme.Hex(0x2EC4B6))) g.FillEllipse(br, -s.Width * 0.2f, s.Height * 0.55f, s.Width * 0.7f, s.Width * 0.7f);
                using (var br = new SolidBrush(Color.FromArgb(200, 255, 209, 102))) g.FillEllipse(br, s.Width * 0.25f, s.Height * 0.3f, s.Width * 0.3f, s.Width * 0.3f);
            }
            return b;
        }
    }
}
