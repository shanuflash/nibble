using System;
using System.Drawing;
using Nibble.Devices;

namespace Nibble.UI.Settings
{
    sealed class DpiPage : SettingsPage
    {
        static readonly int[] Presets = { 400, 800, 1200, 1600, 2400, 3200, 6400 };

        RectangleF slider;         // card coordinates
        bool dragging;
        int dragValue;

        public DpiPage(TrayApp app) : base(app) { }

        public override string Title { get { return "DPI"; } }
        public override Symbol Icon { get { return Symbol.Scope; } }
        public override Color Tint { get { return Theme.Hex(0xFF9500); } }

        MouseCaps Caps { get { return Mouse.Device.Caps; } }

        public override void Opened() { dragging = false; }

        public override void Paint(Canvas c, RectangleF area)
        {
            float y = c.Header(area, Title, "Sensitivity stages the DPI button cycles through. Tap a stage to switch to it.");
            if (!HaveSettings(c, area, y)) return;
            var cfg = Mouse.Settings;
            var th = c.Th;

            var row = new RectangleF(area.X, y, area.Width, c.F(56));
            c.Platter(row);
            c.Text("Stages", c.Row, th.Label, c.PlatterFlat(), new RectangleF(row.X + c.F(18), row.Y, c.F(200), row.Height), TextAlign.Left);
            var counts = new string[Caps.MaxStages];
            for (int i = 0; i < counts.Length; i++) counts[i] = (i + 1).ToString();
            c.Segmented(new RectangleF(row.Right - c.F(14) - c.F(300), row.Y + c.F(12), c.F(300), c.F(32)), counts, cfg.StageCount - 1, "stages", delegate (int i)
            {
                int n = i + 1;
                Mouse.Change(x => x.SetStages(Math.Min(x.Stage, n), n), SettingGroup.Dpi);
            });
            y = row.Bottom + c.F(14);

            y = PaintStageCards(c, area, y, cfg) + c.F(14);

            var ed = new RectangleF(area.X, y, area.Width, c.F(236));
            c.Platter(ed);
            var hp = new RectangleF(area.X, ed.Bottom + c.F(12), area.Width, c.F(52));
            c.Platter(hp);
            c.SwitchRow(hp, 0, c.F(52), "On-screen popup", "Show the new DPI when you press the mouse’s DPI button",
                App.Prefs.DpiPopup, "dpihud", delegate { App.UpdatePrefs(p => p.DpiPopup = !p.DpiPopup); });
            PaintEditor(c, ed, cfg);
        }

        float PaintStageCards(Canvas c, RectangleF area, float y, MouseSettings cfg)
        {
            var th = c.Th;
            int max = Caps.MaxStages;
            float gap = c.F(10), cw = (area.Width - gap * (max - 1)) / max, ch = c.F(104);
            for (int k = 1; k <= cfg.StageCount; k++)
            {
                var r = new RectangleF(area.X + (k - 1) * (cw + gap), y, cw, ch);
                bool active = k == cfg.Stage;
                string id = "stage" + k;
                Color fill = active ? (th.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(235, 255, 255, 255)) : (c.Hover == id ? c.PlatterHover() : th.Platter);
                Draw.FillRound(c.G, c.Av(fill), r, c.F(18));
                Draw.Rim(c.G, r, c.F(18), c.Av(th.PlatterRimTop), c.Av(th.PlatterRimBottom), Math.Max(1f, c.F(0.8f)));
                if (active)
                    using (var p = new Pen(c.Av(th.Green), c.F(2)))
                    using (var path = Draw.Round(RectangleF.Inflate(r, -c.F(1), -c.F(1)), c.F(17))) c.G.DrawPath(p, path);
                var flat = Draw.Flatten(fill, c.CardFlat);
                using (var b = new SolidBrush(c.Av(cfg.StageColor(k)))) c.G.FillEllipse(b, r.X + c.F(14), r.Y + c.F(16), c.F(10), c.F(10));
                using (var p = new Pen(c.Av(Color.FromArgb(60, 0, 0, 0)), 1)) c.G.DrawEllipse(p, r.X + c.F(14), r.Y + c.F(16), c.F(10), c.F(10));
                c.Text("Stage " + k, c.Cap, th.Secondary, flat, new RectangleF(r.X + c.F(30), r.Y + c.F(13), r.Width - c.F(36), c.F(16)), TextAlign.Left);
                int shown = active && dragging ? dragValue : cfg.Dpi(k);
                c.Text(shown.ToString(), c.Big, th.Label, flat, new RectangleF(r.X + c.F(12), r.Y + c.F(38), r.Width - c.F(16), c.F(34)), TextAlign.Left);
                c.Text(active ? "Active" : "DPI", c.Cap, active ? th.Green : th.Secondary, flat, new RectangleF(r.X + c.F(14), r.Y + c.F(74), r.Width - c.F(20), c.F(16)), TextAlign.Left);
                int kk = k;
                if (!active) c.Hit(id, r, delegate { Mouse.Change(x => x.SetStages(kk, x.StageCount), SettingGroup.Dpi); });
            }
            return y + ch;
        }

        // Editor for the active stage: big value with -/+, slider, presets and LED colour.
        void PaintEditor(Canvas c, RectangleF ed, MouseSettings cfg)
        {
            var th = c.Th;
            var pf = c.PlatterFlat();
            int s = cfg.Stage, v = dragging ? dragValue : cfg.Dpi(s);
            c.Text("Stage " + s, c.Head, th.Label, pf, new RectangleF(ed.X + c.F(20), ed.Y + c.F(16), c.F(200), c.F(24)), TextAlign.Left);
            c.Text("Drag, pick a preset, or nudge by " + Caps.DpiStep + ".", c.Sub, th.Secondary, pf, new RectangleF(ed.X + c.F(20), ed.Y + c.F(40), c.F(300), c.F(18)), TextAlign.Left);

            var plus = new RectangleF(ed.Right - c.F(20) - c.F(34), ed.Y + c.F(20), c.F(34), c.F(34));
            var minus = new RectangleF(plus.X - c.F(8) - c.F(34), plus.Y, c.F(34), c.F(34));
            c.Circle(minus, "minus"); c.Circle(plus, "plus");
            using (var p = c.StrokePen(th.Label, 2f))
            {
                float cy = minus.Y + minus.Height / 2, h = c.F(6);
                c.PlusMinus(p, minus.X + minus.Width / 2, cy, h, false);
                c.PlusMinus(p, plus.X + plus.Width / 2, cy, h, true);
            }
            c.Hit("minus", minus, delegate { SetDpi(v - Caps.DpiStep); });
            c.Hit("plus", plus, delegate { SetDpi(v + Caps.DpiStep); });
            string vs = v.ToString();
            var vsz = c.Measure(vs, c.Huge);
            var unitX = minus.X - c.F(18) - c.Measure("DPI", c.Semi).Width;
            var vr = new RectangleF(unitX - c.F(8) - vsz.Width, ed.Y + c.F(12), vsz.Width + c.F(2), c.F(50));
            c.Text(vs, c.Huge, th.Label, pf, vr, TextAlign.Left);
            c.Text("DPI", c.Semi, th.Secondary, pf, new RectangleF(unitX, vr.Y + c.F(21), c.F(40), c.F(24)), TextAlign.Left);

            slider = new RectangleF(ed.X + c.F(24), ed.Y + c.F(92), ed.Width - c.F(48), c.F(28));
            float track = c.F(6), frac = (v - Caps.DpiMin) / (float)(Caps.DpiMax - Caps.DpiMin);
            var tr = new RectangleF(slider.X, slider.Y + (slider.Height - track) / 2, slider.Width, track);
            Draw.FillRound(c.G, c.Av(th.Track), tr, track / 2);
            Draw.FillRound(c.G, c.Av(th.Blue), new RectangleF(tr.X, tr.Y, Math.Max(track, tr.Width * frac), track), track / 2);
            float kx = tr.X + tr.Width * frac, kd = c.F(26);
            var knob = new RectangleF(kx - kd / 2, slider.Y + (slider.Height - kd) / 2, kd, kd);
            Draw.FillRound(c.G, c.Av(Color.FromArgb(50, 0, 0, 0)), new RectangleF(knob.X, knob.Y + c.F(1.5f), kd, kd), kd / 2);
            using (var b = new SolidBrush(c.Av(Color.White))) c.G.FillEllipse(b, knob);
            c.Text(Caps.DpiMin.ToString(), c.Cap, th.Secondary, pf, new RectangleF(tr.X, tr.Bottom + c.F(10), c.F(60), c.F(14)), TextAlign.Left);
            c.Text(Caps.DpiMax.ToString(), c.Cap, th.Secondary, pf, new RectangleF(tr.Right - c.F(60), tr.Bottom + c.F(10), c.F(60), c.F(14)), TextAlign.Right);
            c.Hit("slider", RectangleF.Inflate(slider, 0, c.F(6)), delegate { });

            float px = ed.X + c.F(20), py = ed.Y + c.F(150);
            c.Text("Presets", c.Sub, th.Secondary, pf, new RectangleF(px, py, c.F(80), c.F(30)), TextAlign.Left);
            px += c.F(70);
            foreach (int preset in Presets)
            {
                if (preset < Caps.DpiMin || preset > Caps.DpiMax) continue;
                string label = preset.ToString();
                float w = c.Measure(label, c.SmallSemi).Width + c.F(26);
                int pv = preset;
                c.Chip(new RectangleF(px, py, w, c.F(30)), label, v == preset, "preset" + preset, delegate { SetDpi(pv); });
                px += w + c.F(8);
            }

            if (Caps.LedColors == null) return;
            float cy2 = ed.Y + c.F(196);
            c.Text("Colour", c.Sub, th.Secondary, pf, new RectangleF(ed.X + c.F(20), cy2, c.F(80), c.F(30)), TextAlign.Left);
            float sx = ed.X + c.F(90);
            var cur = cfg.StageColor(s);
            foreach (var col in Caps.LedColors)
            {
                var sr = new RectangleF(sx, cy2 + c.F(3), c.F(24), c.F(24));
                bool sel = cur.R == col.R && cur.G == col.G && cur.B == col.B;
                if (sel) using (var p = new Pen(c.Av(th.Label), c.F(2))) c.G.DrawEllipse(p, RectangleF.Inflate(sr, c.F(3.5f), c.F(3.5f)));
                using (var b = new SolidBrush(c.Av(col))) c.G.FillEllipse(b, sr);
                using (var p = new Pen(c.Av(Color.FromArgb(50, 0, 0, 0)), 1)) c.G.DrawEllipse(p, sr);
                var cc = col;
                c.Hit("sw" + col.ToArgb(), RectangleF.Inflate(sr, c.F(4), c.F(4)), delegate { Mouse.Change(x => x.SetStageColor(x.Stage, cc), SettingGroup.Dpi); });
                sx += c.F(36);
            }
        }

        void SetDpi(int dpi)
        {
            int step = Caps.DpiStep;
            dpi = Math.Max(Caps.DpiMin, Math.Min(Caps.DpiMax, (int)Math.Round(dpi / (double)step) * step));
            if (Mouse.Settings == null || Mouse.Settings.CurrentDpi == dpi) return;
            Mouse.Change(x => x.SetDpi(x.Stage, dpi), SettingGroup.Dpi);
        }

        int SliderValue(float x)
        {
            float frac = Math.Max(0, Math.Min(1, (x - slider.X) / slider.Width));
            return (int)Math.Round((Caps.DpiMin + frac * (Caps.DpiMax - Caps.DpiMin)) / (double)Caps.DpiStep) * Caps.DpiStep;
        }

        public override bool BeginDrag(string hitId, float x)
        {
            if (hitId != "slider" || Mouse.Settings == null) return false;
            dragging = true;
            dragValue = SliderValue(x);
            return true;
        }

        public override void DragTo(float x) { dragValue = SliderValue(x); }

        public override void EndDrag()
        {
            dragging = false;
            SetDpi(dragValue);
        }
    }
}
