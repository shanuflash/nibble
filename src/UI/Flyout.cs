using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Nibble.Platform;

namespace Nibble.UI
{
    // The tray popover: battery, connection, DPI, and a quick settings view. Rendering only happens while
    // visible; all bitmaps are freed on hide.
    sealed class Flyout : Form
    {
        // Logical px. Inner radii are concentric: outer radius minus padding.
        const float PW = 340, PH = 294, M = 2, R = 34, Pad = 16;

        readonly TrayApp app;
        readonly float S;
        Theme th;
        readonly Font fNum, fPct, fName, fSub, fTileVal, fTileCap, fTitle, fRow, fSeg, fBtn, fCharge;

        Bitmap backdrop;
        Surface frame;
        Color bgAvg;
        readonly Timer anim, clock;
        int winX, winY;

        float openP; int openT0;
        float ring, ringFrom, ringTo; int ringT0;
        float nav, navFrom, navTo; int navT0;
        float seg, sw1, sw2, spin;

        float va = 1;              // fade of the view being drawn
        bool recordHits;
        string hover, pressed;
        readonly HitMap hits = new HitMap();
        public DateTime HiddenAt = DateTime.MinValue;

        MouseSession Mouse { get { return app.Mouse; } }

        public Flyout(TrayApp app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Text = AppInfo.Name;

            S = Draw.ScreenScale();
            Size = new Size(Px(PW + 2 * M), Px(PH + 2 * M));

            string disp = Draw.PickFont("Segoe UI Variable Display Semib", "Segoe UI Semibold");
            string text = Draw.PickFont("Segoe UI Variable Text", "Segoe UI");
            string textSb = Draw.PickFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
            fNum = PxFont(disp, 60); fPct = PxFont(disp, 26); fTitle = PxFont(disp, 18); fCharge = PxFont(disp, 36);
            fName = PxFont(textSb, 15); fTileVal = PxFont(textSb, 15); fSeg = PxFont(textSb, 13); fBtn = PxFont(textSb, 15);
            fSub = PxFont(text, 13); fTileCap = PxFont(text, 12); fRow = PxFont(text, 15);

            anim = new Timer { Interval = 15 };
            anim.Tick += delegate { Tick(); };
            clock = new Timer { Interval = 1000 };
            clock.Tick += delegate { if (!anim.Enabled) Render(); };

            th = Theme.Current();
            app.Changed += OnState;
        }

        Font PxFont(string family, float px) { return new Font(family, px * S, FontStyle.Regular, GraphicsUnit.Pixel); }
        int Px(float v) { return (int)Math.Round(v * S); }
        float F(float v) { return v * S; }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x80;   // LAYERED | TOOLWINDOW
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
            int pw = Px(PW), ph = Px(PH), m = Px(M);
            var corner = Layered.Corner(Screen.FromPoint(Cursor.Position), new Size(pw, ph), Px(12));
            winX = corner.X - m; winY = corner.Y - m;

            using (var cap = Glass.Capture(new Rectangle(corner.X, corner.Y, pw, ph)))
                BuildSurfaces(cap);

            nav = navFrom = navTo = 0;
            ring = 0; StartRing(RingTarget());
            SyncControls();
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
            backdrop = Glass.Frost(capture, panel, Glass.AdaptiveTint(capture, th.Tint, th.Dark, 90, 175), th.Fallback, 1.8f);
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
            Shell.TrimMemory(true);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HideFlyout();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { if (navTo > 0) Navigate(0); else HideFlyout(); }
            else if (e.KeyCode == Keys.F5 || e.KeyCode == Keys.R) Mouse.Refresh();
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && Visible) { e.Cancel = true; HideFlyout(); }
            base.OnFormClosing(e);
        }

        // ---------- animation ----------

        float RingTarget() { return Mouse.Percent >= 0 ? Mouse.Percent / 100f : 0f; }
        float IntervalIndex() { return Math.Max(0, Array.IndexOf(Preferences.Intervals, app.Prefs.IntervalSec)); }
        void SyncControls() { seg = IntervalIndex(); sw1 = app.AutoStart ? 1 : 0; sw2 = app.Prefs.BatteryAlerts ? 1 : 0; }

        void StartRing(float to) { ringFrom = ring; ringTo = to; ringT0 = Environment.TickCount; }

        void Navigate(float to) { navFrom = nav; navTo = to; navT0 = Environment.TickCount; hover = pressed = null; Kick(); }

        void OnState(object s, EventArgs e)
        {
            if (!Visible) return;
            if (Math.Abs(RingTarget() - ringTo) > 0.001f) StartRing(RingTarget());
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

            float segTo = IntervalIndex(), a1 = app.AutoStart ? 1 : 0, a2 = app.Prefs.BatteryAlerts ? 1 : 0;
            seg = Approach(seg, segTo, 0.3f); sw1 = Approach(sw1, a1, 0.3f); sw2 = Approach(sw2, a2, 0.3f);
            if (seg != segTo || sw1 != a1 || sw2 != a2) more = true;

            if (Mouse.Busy) { spin = (spin + 10) % 360; more = true; }
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
            Layered.Push(Handle, frame, winX, winY + (int)Math.Round((1 - openP) * F(10)), (byte)(255 * openP));
        }

        void Compose(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.None;
            var panel = new RectangleF(F(M), F(M), F(PW), F(PH));
            g.DrawImageUnscaled(backdrop, Px(M), Px(M));
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
            hits.Offset = new PointF(F(M) + ox, F(M));
            g.TranslateTransform(hits.Offset.X, hits.Offset.Y);
            va = alpha;
            recordHits = interactive;
            if (settings) PaintSettings(g); else PaintMain(g);
            g.Restore(st);
            va = 1;
        }

        // ---------- main view ----------

        void PaintMain(Graphics g)
        {
            // Battery ring with the mouse inside, like the iOS Batteries widget.
            float d = F(104), stroke = F(10), rx = F(22), ry = F(24);
            var rr = new RectangleF(rx + stroke / 2, ry + stroke / 2, d - stroke, d - stroke);
            float x = F(148), w = F(PW - 148 - 20);
            string name = Mouse.Device.Name;

            if (Mouse.Online && Mouse.LevelHidden)
            {
                using (var p = new Pen(A(Draw.Alpha(th.Green, 0.35f)), stroke)) g.DrawEllipse(p, rr);
                Icons.Fill(g, Symbol.Bolt, new PointF(rx + d / 2, ry + d / 2), F(44), A(th.Green));
                Txt(g, name, fName, th.Secondary, new RectangleF(x, F(30), w, F(18)), TextAlign.Left);
                Txt(g, "Charging", fCharge, th.Label, new RectangleF(x - F(2), F(52), w + F(10), F(46)), TextAlign.Left);
                Txt(g, Mouse.Wired ? "Over USB cable" : "Plugged in", fSub, th.Green, new RectangleF(x, F(102), w, F(18)), TextAlign.Left);
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
                LineIcons.Mouse(g, rx + d / 2, ry + d / 2, A(Mouse.Online ? Draw.Alpha(th.Label, 0.9f) : th.Secondary), S);

                Txt(g, name, fName, th.Secondary, new RectangleF(x, F(30), w, F(18)), TextAlign.Left);
                bool known = Mouse.Percent >= 0;
                string num = known ? ((int)Math.Round(ring * 100)).ToString() : "—";
                SizeF ns = frame.Measure(num, fNum);
                var nr = new RectangleF(x - F(3), F(46), ns.Width + F(4), F(62));
                Txt(g, num, fNum, Mouse.Online ? th.Label : th.Secondary, nr, TextAlign.Left);
                if (known)
                {
                    // "%" sits on the number's baseline.
                    float baseline = nr.Y + nr.Height / 2 + Ascent(fNum) - LineH(fNum) / 2;
                    float top = baseline - Ascent(fPct);
                    Txt(g, "%", fPct, th.Secondary, new RectangleF(nr.X + ns.Width + F(2), top, F(30), LineH(fPct)), TextAlign.Left);
                }

                Color sc = th.Secondary;
                if (Mouse.Online && (Mouse.Charging || Mouse.FullyCharged)) sc = th.Green;
                else if (Mouse.Online && known && Mouse.Percent <= 20) sc = th.Red;
                float sx = x;
                if (Mouse.Online && Mouse.Charging) { Draw.Bolt(g, A(sc), new RectangleF(sx, F(114), F(9), F(14))); sx += F(14); }
                Txt(g, HeroStatus(), fSub, sc, new RectangleF(sx, F(112), w - (sx - x), F(18)), TextAlign.Left);
            }

            float ty0 = F(150), tw = F((PW - 2 * Pad - 16) / 3f), th0 = F(74);
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(F(Pad) + i * (tw + F(8)), ty0, tw, th0);
                Platter(g, r, F(20));
                var ir = new RectangleF(r.X + F(13), r.Y + F(13), F(16), F(16));
                string val, cap;
                if (i == 0)
                {
                    if (Mouse.Found && Mouse.Wired) LineIcons.Plug(g, ir, A(th.Blue), S);
                    else LineIcons.Wave(g, ir, A(Mouse.Online ? th.Blue : th.Secondary), S);
                    val = ConnectionText(); cap = "Connection";
                }
                else if (i == 1)
                {
                    LineIcons.Crosshair(g, ir, A(th.Orange), S);
                    var s = Mouse.Settings;
                    val = s != null && s.CurrentDpi > 0 ? s.CurrentDpi.ToString() : "—";
                    cap = s != null && s.StageCount > 1 ? "DPI · Stage " + s.Stage : "DPI";
                }
                else
                {
                    LineIcons.Clock(g, ir, A(th.Green), S);
                    val = Ago(Mouse.SyncedAt); cap = "Synced";
                }
                Txt(g, val, fTileVal, th.Label, new RectangleF(r.X + F(13), r.Y + F(36), r.Width - F(18), F(18)), TextAlign.Left);
                Txt(g, cap, fTileCap, th.Secondary, new RectangleF(r.X + F(13), r.Y + F(54), r.Width - F(18), F(14)), TextAlign.Left);
            }

            float by = F(238), bd = F(40);
            var gear = new RectangleF(F(PW - Pad) - bd, by, bd, bd);
            var refresh = new RectangleF(gear.X - F(8) - bd, by, bd, bd);
            GlassButton(g, gear, bd / 2, "settings", delegate { Navigate(1); });
            LineIcons.Sliders(g, gear, A(th.Label), A(th.Dark ? Color.FromArgb(255, 60, 60, 66) : Color.White), S);
            GlassButton(g, refresh, bd / 2, "refresh", Mouse.Refresh);
            LineIcons.Refresh(g, refresh, A(th.Label), S, spin);

            string dl = "Customize";
            float tw1 = frame.Measure(dl, fBtn).Width;
            var drv = new RectangleF(F(Pad), by, tw1 + F(18 + 10 + 9 + 16), bd);
            GlassButton(g, drv, bd / 2, "customize", delegate { HideFlyout(); app.OpenSettings(); });
            Txt(g, dl, fBtn, th.Label, new RectangleF(drv.X + F(18), drv.Y, tw1 + F(4), bd), TextAlign.Left);
            LineIcons.Arrow(g, new RectangleF(drv.X + F(18) + tw1 + F(10), drv.Y + bd / 2 - F(4.5f), F(9), F(9)), A(th.Secondary), S);
        }

        // ---------- settings view ----------

        void PaintSettings(Graphics g)
        {
            float bd = F(40);
            var back = new RectangleF(F(Pad), F(16), bd, bd);
            GlassButton(g, back, bd / 2, "back", delegate { Navigate(0); });
            LineIcons.Chevron(g, back, A(th.Label), S);
            Txt(g, "Settings", fTitle, th.Label, new RectangleF(0, F(16), F(PW), bd), TextAlign.Center);

            var grp = new RectangleF(F(Pad), F(70), F(PW - 2 * Pad), F(150));
            Platter(g, grp, F(22));
            float rh = F(50);

            var r0 = new RectangleF(grp.X, grp.Y, grp.Width, rh);
            Txt(g, "Refresh", fRow, th.Label, new RectangleF(r0.X + F(16), r0.Y, F(90), rh), TextAlign.Left);
            float sw = F(168), sh = F(32);
            var sr = new RectangleF(r0.Right - F(10) - sw, r0.Y + (rh - sh) / 2, sw, sh);
            Draw.FillRound(g, A(th.SegTrack), sr, sh / 2);
            int n = Preferences.Intervals.Length;
            float iw = sw / n;
            var thumb = new RectangleF(sr.X + seg * iw + F(2), sr.Y + F(2), iw - F(4), sh - F(4));
            if (!th.Dark) Draw.FillRound(g, A(Color.FromArgb(28, 0, 0, 0)), new RectangleF(thumb.X, thumb.Y + F(1), thumb.Width, thumb.Height), thumb.Height / 2);
            Draw.FillRound(g, A(th.SegThumb), thumb, thumb.Height / 2);
            Draw.Rim(g, thumb, thumb.Height / 2, A(th.RimTop), A(th.RimBottom), Math.Max(1f, F(0.8f)));
            for (int i = 0; i < n; i++)
            {
                var ir = new RectangleF(sr.X + i * iw, sr.Y, iw, sh);
                bool on = Math.Abs(seg - i) < 0.5f;
                Txt(g, Preferences.IntervalNames[i], fSeg, on ? th.Label : th.Secondary, ir, TextAlign.Center);
                int sec = Preferences.Intervals[i];
                AddHit("seg" + i, ir, delegate { app.UpdatePrefs(p => p.IntervalSec = sec); });
            }
            Separator(g, grp.X + F(16), grp.Right - F(16), r0.Bottom);

            var r1 = new RectangleF(grp.X, grp.Y + rh, grp.Width, rh);
            SwitchRow(g, r1, "Launch at Login", sw1, "sw1", delegate { app.AutoStart = !app.AutoStart; });
            Separator(g, grp.X + F(16), grp.Right - F(16), r1.Bottom);
            var r2 = new RectangleF(grp.X, grp.Y + rh * 2, grp.Width, rh);
            SwitchRow(g, r2, "Battery Alerts", sw2, "sw2", delegate { app.UpdatePrefs(p => p.BatteryAlerts = !p.BatteryAlerts); });

            var quit = new RectangleF(F(Pad), F(232), F(PW - 2 * Pad), F(46));
            GlassButton(g, quit, quit.Height / 2, "quit", app.Quit);
            Txt(g, "Quit " + AppInfo.Name, fBtn, th.Red, quit, TextAlign.Center);
        }

        void SwitchRow(Graphics g, RectangleF r, string label, float pos, string id, Action toggle)
        {
            Txt(g, label, fRow, th.Label, new RectangleF(r.X + F(16), r.Y, r.Width - F(100), r.Height), TextAlign.Left);
            float w = F(56), h = F(30);
            var tr = new RectangleF(r.Right - F(14) - w, r.Y + (r.Height - h) / 2, w, h);
            Draw.FillRound(g, A(Draw.Lerp(th.SwitchOff, th.Green, pos)), tr, h / 2);
            // The knob stretches while pressed.
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

        void AddHit(string id, RectangleF r, Action a) { if (recordHits) hits.Add(id, r, a); }

        // GDI text for native ClearType. It ignores GDI+ transforms, so the view offset is applied by hand,
        // and pending GDI+ drawing is flushed first so the text lands on top.
        void Txt(Graphics g, string s, Font f, Color c, RectangleF r, TextAlign align)
        {
            g.Flush(FlushIntention.Sync);
            var rr = Rectangle.Round(new RectangleF(r.X + hits.Offset.X, r.Y + hits.Offset.Y, r.Width, r.Height));
            frame.Text(s, f, Draw.Flatten(c, bgAvg, va), rr, align);
        }

        static float Ascent(Font f) { return f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style); }
        static float LineH(Font f) { return f.Size * (f.FontFamily.GetCellAscent(f.Style) + f.FontFamily.GetCellDescent(f.Style)) / f.FontFamily.GetEmHeight(f.Style); }

        // ---------- input ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = hits.At(e.Location);
            string id = h == null ? null : h.Id;
            Cursor = h == null ? Cursors.Default : Cursors.Hand;
            if (id != hover) { hover = id; if (!anim.Enabled) Render(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = null; if (!anim.Enabled) Render(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var h = hits.At(e.Location);
            pressed = h == null ? null : h.Id;
            if (!anim.Enabled) Render();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var h = hits.At(e.Location);
            string p = pressed;
            pressed = null;
            if (h != null && h.Id == p && e.Button == MouseButtons.Left) h.Invoke();
            Kick();
        }

        // ---------- copy ----------

        Color RingColor()
        {
            if (!Mouse.Online) return th.Tertiary;
            if (!Mouse.Charging && Mouse.Percent <= 20) return th.Red;
            return th.Green;
        }

        string HeroStatus()
        {
            if (!Mouse.Found) return "Plug in the receiver";
            if (!Mouse.Online) return Mouse.Percent >= 0 ? "Asleep · " + Ago(Mouse.SyncedAt).ToLowerInvariant() : "Move mouse to wake";
            return Mouse.StatusText;
        }

        string ConnectionText()
        {
            if (!Mouse.Found) return "None";
            if (!Mouse.Online) return "Asleep";
            return Mouse.Wired ? "USB" : "2.4 GHz";
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

        public void Snapshot(string path, bool dark, bool settingsView)
        {
            th = Theme.Current(dark);
            using (var wall = Wallpaper.Small(Size))
            {
                using (var cap = wall.Clone(new Rectangle(Px(M), Px(M), Px(PW), Px(PH)), wall.PixelFormat))
                    BuildSurfaces(cap);
                ring = RingTarget(); SyncControls();
                nav = settingsView ? 1 : 0; navTo = nav; openP = 1;
                using (var g = Graphics.FromImage(frame.Bmp)) Compose(g);
                frame.ApplyMask();
                using (var g = Graphics.FromImage(wall)) g.DrawImageUnscaled(frame.Bmp, 0, 0);
                wall.Save(path, ImageFormat.Png);
            }
            FreeSurfaces();
        }
    }
}
