using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Nibble.UI.Settings
{
    // Drawing context and control kit for the settings window. The window sets the per-frame fields
    // (Graphics, fade, hover...) and pages paint through it, registering hit targets as they go.
    sealed class Canvas
    {
        public readonly float U;          // px per logical unit
        public readonly Font Title, Huge, Big, Head, Row, Sub, Cap, Semi, SmallSemi, Nav;

        public Graphics G;
        public Surface Frame;
        public Theme Th;
        public Color CardFlat;            // average colour of the card, what text on it is flattened against
        public float Fade = 1;            // content fade while switching pages
        public bool Interactive = true;   // false while a page fades in
        public string Hover, Pressed;
        public readonly HitMap Hits = new HitMap();
        public Action Animate;            // keep frames coming (for time-based state)

        readonly Dictionary<string, float> springs = new Dictionary<string, float>();
        readonly Dictionary<string, float> targets = new Dictionary<string, float>();
        readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>();

        public Canvas(float u)
        {
            U = u;
            string disp = Draw.PickFont("Segoe UI Variable Display Semib", "Segoe UI Semibold");
            string text = Draw.PickFont("Segoe UI Variable Text", "Segoe UI");
            string textSb = Draw.PickFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
            Title = PxFont(disp, 30); Huge = PxFont(disp, 44); Big = PxFont(disp, 26); Head = PxFont(textSb, 17);
            Row = PxFont(text, 15); Sub = PxFont(text, 12.5f); Cap = PxFont(text, 11.5f); Semi = PxFont(textSb, 14); SmallSemi = PxFont(textSb, 12.5f); Nav = PxFont(text, 14);
        }

        Font PxFont(string f, float px) { return new Font(f, px * U, FontStyle.Regular, GraphicsUnit.Pixel); }
        public float F(float v) { return v * U; }
        public int Pi(float v) { return (int)Math.Round(v * U); }

        // ---------- animation ----------

        // Current value of an animated property heading toward `to`.
        public float Spring(string id, float to)
        {
            targets[id] = to;
            float v;
            if (!springs.TryGetValue(id, out v)) { springs[id] = to; return to; }
            return v;
        }

        // Advances all springs one frame; true while any is still moving.
        public bool StepSprings()
        {
            bool more = false;
            foreach (var k in new List<string>(targets.Keys))
            {
                float v = springs.ContainsKey(k) ? springs[k] : targets[k], t = targets[k];
                float d = t - v;
                if (Math.Abs(d) < 0.002f) v = t; else { v += d * 0.28f; more = true; }
                springs[k] = v;
            }
            return more;
        }

        public void ResetSprings() { springs.Clear(); targets.Clear(); }

        // ---------- colour ----------

        public Color Av(Color c) { return Fade >= 1 ? c : Draw.Alpha(c, Fade); }
        public Color PlatterFlat() { return Draw.Flatten(Th.Platter, CardFlat); }
        public Color PlatterHover() { return Th.Dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(170, 255, 255, 255); }

        // ---------- text ----------

        // GDI text straight into the DIB for native ClearType; translucent colours are flattened onto
        // `under`. GDI ignores GDI+ transforms, so the card offset is applied by hand.
        public void Text(string s, Font f, Color c, Color under, RectangleF r, TextAlign align)
        {
            G.Flush(FlushIntention.Sync);
            var o = Hits.Offset;
            Frame.Text(s, f, Draw.Flatten(c, under, Fade), Rectangle.Round(new RectangleF(r.X + o.X, r.Y + o.Y, r.Width, r.Height)), align);
        }

        public Size Measure(string s, Font f)
        {
            return Frame != null ? Size.Round(Frame.Measure(s, f)) : TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.NoPadding);
        }

        // ---------- hit targets ----------

        public void Hit(string id, RectangleF r, Action a) { if (Interactive) Hits.Add(id, r, a); }

        // ---------- layout pieces ----------

        // Page title and subtitle; returns where content starts.
        public float Header(RectangleF c, string title, string sub)
        {
            Text(title, Title, Th.Label, CardFlat, new RectangleF(c.X, c.Y - F(4), c.Width - F(260), F(40)), TextAlign.Left);
            Text(sub, Sub, Th.Secondary, CardFlat, new RectangleF(c.X, c.Y + F(36), c.Width - F(60), F(18)), TextAlign.Left);
            return c.Y + F(74);
        }

        public void Platter(RectangleF r)
        {
            Draw.FillRound(G, Av(Th.Platter), r, F(18));
            Draw.Rim(G, r, F(18), Av(Th.PlatterRimTop), Av(Th.PlatterRimBottom), Math.Max(1f, F(0.8f)));
        }

        // Separator inside a platter, `dy` below its top.
        public void Sep(RectangleF p, float dy)
        {
            using (var pen = new Pen(Av(Th.Separator), Math.Max(1f, F(0.8f)))) G.DrawLine(pen, p.X + F(18), p.Y + dy, p.Right - F(18), p.Y + dy);
        }

        // Title and subtitle of row i (height rh) inside platter p.
        public void RowLabel(RectangleF p, int i, float rh, string title, string sub)
        {
            var pf = PlatterFlat();
            Text(title, Row, Th.Label, pf, new RectangleF(p.X + F(18), p.Y + rh * i + rh / 2 - F(19), F(300), F(20)), TextAlign.Left);
            Text(sub, Sub, Th.Secondary, pf, new RectangleF(p.X + F(18), p.Y + rh * i + rh / 2 + F(1), F(330), F(18)), TextAlign.Left);
        }

        // A control's rect at the right end of row i.
        public RectangleF RowControl(RectangleF p, int i, float rh, float w, float h, float inset)
        {
            return new RectangleF(p.Right - F(inset) - F(w), p.Y + rh * i + (rh - F(h)) / 2, F(w), F(h));
        }

        // ---------- controls ----------

        public void Switch(RectangleF r, bool on, string id, Action toggle)
        {
            float pos = Spring("sw:" + id, on ? 1 : 0);
            Draw.FillRound(G, Av(Draw.Lerp(Th.SwitchOff, Th.Green, pos)), r, r.Height / 2);
            float kw = F(Pressed == id ? 38 : 34), kh = r.Height - F(4);
            var knob = new RectangleF(r.X + F(2) + pos * (r.Width - F(4) - kw), r.Y + F(2), kw, kh);
            Draw.FillRound(G, Av(Color.FromArgb(45, 0, 0, 0)), new RectangleF(knob.X, knob.Y + F(1.5f), kw, kh), kh / 2);
            Draw.FillRound(G, Av(Color.White), knob, kh / 2);
            Hit(id, RectangleF.Inflate(r, F(6), F(6)), toggle);
        }

        public void SwitchRow(RectangleF p, int i, float rh, string title, string sub, bool on, string id, Action toggle)
        {
            RowLabel(p, i, rh, title, sub);
            Switch(RowControl(p, i, rh, 56, 30, 18), on, id, toggle);
        }

        public void Segmented(RectangleF r, string[] items, int selected, string id, Action<int> pick)
        {
            float pos = Spring("seg:" + id, Math.Max(0, selected));
            Draw.FillRound(G, Av(Th.SegTrack), r, r.Height / 2);
            float iw = r.Width / items.Length;
            var thumb = new RectangleF(r.X + pos * iw + F(2), r.Y + F(2), iw - F(4), r.Height - F(4));
            if (selected >= 0)
            {
                if (!Th.Dark) Draw.FillRound(G, Av(Color.FromArgb(26, 0, 0, 0)), new RectangleF(thumb.X, thumb.Y + F(1), thumb.Width, thumb.Height), thumb.Height / 2);
                Draw.FillRound(G, Av(Th.SegThumb), thumb, thumb.Height / 2);
                Draw.Rim(G, thumb, thumb.Height / 2, Av(Th.RimTop), Av(Th.RimBottom), Math.Max(1f, F(0.8f)));
            }
            var trackFlat = Draw.Flatten(Th.SegTrack, PlatterFlat());
            var thumbFlat = Draw.Flatten(Th.SegThumb, trackFlat);
            for (int i = 0; i < items.Length; i++)
            {
                var ir = new RectangleF(r.X + i * iw, r.Y, iw, r.Height);
                bool on = Math.Abs(pos - i) < 0.5f;
                if (!on && Hover == id + i) Draw.FillRound(G, Av(Th.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(60, 255, 255, 255)), RectangleF.Inflate(ir, -F(2), -F(2)), (r.Height - F(4)) / 2);
                Text(items[i], SmallSemi, on ? Th.Label : Th.Secondary, on ? thumbFlat : trackFlat, ir, TextAlign.Center);
                int idx = i;
                if (i != selected) Hit(id + i, ir, delegate { pick(idx); });
            }
        }

        public void Stepper(RectangleF r, string value, string id, Action minus, Action plus)
        {
            Draw.FillRound(G, Av(Th.SegTrack), r, r.Height / 2);
            var m = new RectangleF(r.X, r.Y, r.Height * 1.3f, r.Height);
            var p = new RectangleF(r.Right - r.Height * 1.3f, r.Y, r.Height * 1.3f, r.Height);
            var flat = Draw.Flatten(Th.SegTrack, PlatterFlat());
            if (Hover == id + "-") Draw.FillRound(G, Av(Th.ControlHover), RectangleF.Inflate(m, -F(2), -F(2)), (r.Height - F(4)) / 2);
            if (Hover == id + "+") Draw.FillRound(G, Av(Th.ControlHover), RectangleF.Inflate(p, -F(2), -F(2)), (r.Height - F(4)) / 2);
            using (var pen = StrokePen(Th.Label, 2f))
            {
                float cy = r.Y + r.Height / 2, h = F(5.5f);
                PlusMinus(pen, m.X + m.Width / 2, cy, h, false);
                PlusMinus(pen, p.X + p.Width / 2, cy, h, true);
            }
            Text(value, SmallSemi, Th.Label, flat, r, TextAlign.Center);
            Hit(id + "-", m, minus);
            Hit(id + "+", p, plus);
        }

        public void PlusMinus(Pen pen, float cx, float cy, float h, bool plus)
        {
            G.DrawLine(pen, cx - h, cy, cx + h, cy);
            if (plus) G.DrawLine(pen, cx, cy - h, cx, cy + h);
        }

        public void Chip(RectangleF r, string label, bool selected, string id, Action a)
        {
            Color fill = selected ? Th.Blue : (Hover == id ? Th.ControlHover : Th.SegTrack);
            Draw.FillRound(G, Av(fill), r, r.Height / 2);
            Text(label, SmallSemi, selected ? Color.White : Th.Label, Draw.Flatten(selected ? Th.Blue : fill, PlatterFlat()), r, TextAlign.Center);
            if (!selected) Hit(id, r, a);
        }

        public void Button(RectangleF r, string label, Color fg, string id, Action a) { Button(r, label, fg, id, a, Color.Empty); }

        public void Button(RectangleF r, string label, Color fg, string id, Action a, Color fill)
        {
            if (fill.IsEmpty) fill = Pressed == id ? Th.ControlPress : Hover == id ? Th.ControlHover : Th.SegTrack;
            else if (Hover == id) fill = Draw.Lerp(fill, Color.White, 0.12f);
            Draw.FillRound(G, Av(fill), r, r.Height / 2);
            Text(label, SmallSemi, fg, Draw.Flatten(fill, PlatterFlat()), r, TextAlign.Center);
            Hit(id, r, a);
        }

        public void Circle(RectangleF r, string id)
        {
            Color fill = Pressed == id ? Th.ControlPress : Hover == id ? Th.ControlHover : Th.Control;
            using (var b = new SolidBrush(fill)) G.FillEllipse(b, r);
        }

        public Pen StrokePen(Color c, float w) { return Draw.RoundPen(Av(c), F(w)); }

        // ---------- symbols ----------

        public void DrawSymbol(Symbol s, PointF center, float ink, Color c) { Icons.Fill(G, s, center, ink, Av(c)); }

        // A symbol pre-rendered into a small cached bitmap, for the sidebar tiles.
        public void SymbolTile(Symbol s, RectangleF box, Color c)
        {
            int size = (int)Math.Round(box.Width);
            string key = (int)s + ":" + size + ":" + c.ToArgb();
            Bitmap bmp;
            if (!iconCache.TryGetValue(key, out bmp))
            {
                bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                using (var bg = Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(c))
                {
                    bg.SmoothingMode = SmoothingMode.AntiAlias;
                    bg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    var parts = Icons.Parts(s);
                    var b = parts[0].GetBounds();
                    foreach (var part in parts) b = RectangleF.Union(b, part.GetBounds());
                    float ink = size * 0.64f, k = ink / Math.Max(b.Width, b.Height);
                    using (var m = new Matrix())
                    {
                        m.Translate(size / 2f, size / 2f);
                        m.Scale(k, k);
                        m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2));
                        foreach (var part in parts) { part.Transform(m); bg.FillPath(brush, part); part.Dispose(); }
                    }
                }
                iconCache[key] = bmp;
            }
            G.DrawImageUnscaled(bmp, (int)Math.Round(box.X), (int)Math.Round(box.Y));
        }

        // macOS-style icon tile: squircle with a soft vertical gradient and a top highlight.
        public void Tile(RectangleF r, Color c)
        {
            float rad = r.Width * 0.27f;
            using (var path = Draw.Round(r, rad))
            using (var br = new LinearGradientBrush(r, Draw.Lerp(c, Color.White, 0.22f), Draw.Lerp(c, Color.Black, 0.08f), 90f))
                G.FillPath(br, path);
            Draw.Rim(G, r, rad, Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Math.Max(1f, F(0.8f)));
        }

        // Battery ring shared by the sidebar and the Power page, matching the flyout: the level as an arc,
        // or a soft green ring with a bolt while charging hides the level.
        public void BatteryRing(MouseSession m, RectangleF box, float stroke, bool mouseInside)
        {
            var rr = RectangleF.Inflate(box, -stroke / 2, -stroke / 2);
            var center = new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
            if (m.Online && m.LevelHidden)
            {
                using (var p = new Pen(Av(Draw.Alpha(Th.Green, 0.35f)), stroke)) G.DrawEllipse(p, rr);
                DrawSymbol(Symbol.Bolt, center, box.Width * 0.44f, Th.Green);
                return;
            }
            using (var p = new Pen(Av(Th.Track), stroke)) G.DrawEllipse(p, rr);
            if (m.Percent > 0)
                using (var p = new Pen(Av(!m.Online ? Th.Tertiary : m.Percent <= 20 && !m.Charging ? Th.Red : Th.Green), stroke))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    G.DrawArc(p, rr, -90, 3.6f * m.Percent);
                }
            if (mouseInside)
                DrawSymbol(Symbol.Mouse, center, box.Width * 0.46f, m.Online ? Th.Label : Th.Secondary);
        }
    }
}
