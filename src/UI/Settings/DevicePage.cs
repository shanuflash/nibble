using System;
using System.Drawing;
using Nibble.Devices;

namespace Nibble.UI.Settings
{
    sealed class DevicePage : SettingsPage
    {
        const int ResetWindowMs = 4000;
        int resetArmedAt;
        bool resetArmed;

        public DevicePage(TrayApp app) : base(app) { }

        public override string Title { get { return "Device"; } }
        public override Symbol Icon { get { return Symbol.Mouse; } }
        public override Color Tint { get { return Theme.Hex(0x8E8E93); } }

        public override void Opened()
        {
            resetArmed = false;
            if (Mouse.Firmware == null) Mouse.CheckFirmware();
        }

        public override void Paint(Canvas c, RectangleF area)
        {
            var th = c.Th;
            float y = c.Header(area, Title, "Everything Nibble reads from the mouse.");
            float rh = c.F(44);
            var cfg = Mouse.Settings;
            string[,] rows =
            {
                { "Model", Mouse.Device.Model },
                { "Connection", !Mouse.Found ? "Not connected" : !Mouse.Online ? "Asleep" : Mouse.Wired ? "USB cable" : "2.4 GHz receiver" },
                { "Polling rate", cfg != null ? cfg.PollingHz + " Hz" : "—" },
                { "Current DPI", cfg != null ? cfg.CurrentDpi + " (stage " + cfg.Stage + " of " + cfg.StageCount + ")" : "—" },
            };
            int n = rows.GetLength(0);
            var p = new RectangleF(area.X, y, area.Width, rh * n);
            c.Platter(p);
            var pf = c.PlatterFlat();
            for (int i = 0; i < n; i++)
            {
                var r = new RectangleF(p.X + c.F(18), p.Y + rh * i, p.Width - c.F(36), rh);
                c.Text(rows[i, 0], c.Row, th.Label, pf, r, TextAlign.Left);
                c.Text(rows[i, 1], c.Row, th.Secondary, pf, r, TextAlign.Right);
                if (i < n - 1) c.Sep(p, rh * (i + 1));
            }
            y = p.Bottom + c.F(14);

            y = PaintFirmware(c, area, y) + c.F(14);
            PaintReset(c, area, y, pf);

            c.Text("Settings go straight to the mouse over USB. The firmware check asks RK’s server only when you open this page.", c.Sub, th.Tertiary, c.CardFlat,
                new RectangleF(area.X, area.Bottom - c.F(20), area.Width, c.F(18)), TextAlign.Left);
        }

        // Tap once to arm, again within a few seconds to confirm.
        void PaintReset(Canvas c, RectangleF area, float y, Color pf)
        {
            var th = c.Th;
            bool armed = resetArmed && unchecked(Environment.TickCount - resetArmedAt) < ResetWindowMs;
            var rp = new RectangleF(area.X, y, area.Width, c.F(58));
            string id = "reset";
            Draw.FillRound(c.G, c.Av(c.Hover == id ? c.PlatterHover() : th.Platter), rp, c.F(18));
            Draw.Rim(c.G, rp, c.F(18), c.Av(th.PlatterRimTop), c.Av(th.PlatterRimBottom), Math.Max(1f, c.F(0.8f)));
            var text = new RectangleF(rp.X + c.F(18), rp.Y, rp.Width - c.F(36), rp.Height);
            c.Text(armed ? "Tap again to restore factory settings" : "Restore factory settings", c.Row, th.Red, pf, text, TextAlign.Left);
            c.Text("Resets DPI, polling rate and buttons on the mouse", c.Sub, th.Secondary, pf, text, TextAlign.Right);
            c.Hit(id, rp, delegate
            {
                if (!armed) { resetArmed = true; resetArmedAt = Environment.TickCount; return; }
                resetArmed = false;
                Mouse.FactoryReset();
            });
            if (armed && c.Animate != null) c.Animate();
        }

        // Installed vs. the vendor's published versions, with a hand-off to the vendor's updater.
        float PaintFirmware(Canvas c, RectangleF area, float y)
        {
            float rh = c.F(58);
            var p = new RectangleF(area.X, y, area.Width, rh * 2);
            c.Platter(p);
            var fw = Mouse.Firmware;
            bool busy = Mouse.FirmwareChecking;

            string recNow = fw == null ? (busy ? "Reading…" : "—") : fw.Receiver ?? (Mouse.Wired ? "Not in use (cable)" : "Unknown");
            c.RowLabel(p, 0, rh, "Receiver firmware", "Installed " + recNow + (fw != null && fw.LatestReceiver != null ? " · latest " + fw.LatestReceiver : ""));
            FirmwareAction(c, p, 0, rh, fw, busy, fw != null && fw.ReceiverUpdate, fw != null ? fw.LatestReceiver : null, fw != null ? fw.ReceiverUrl : null,
                fw != null && fw.Receiver != null, "fwrec");
            c.Sep(p, rh);

            // The mouse only reports its version over the cable.
            string mouseNow = fw == null ? (busy ? "Reading…" : "—") : fw.Mouse ?? "plug in the cable to read";
            c.RowLabel(p, 1, rh, "Mouse firmware", "Installed " + mouseNow + (fw != null && fw.LatestMouse != null ? " · latest " + fw.LatestMouse : ""));
            FirmwareAction(c, p, 1, rh, fw, busy, fw != null && fw.MouseUpdate, fw != null ? fw.LatestMouse : null, fw != null ? fw.MouseUrl : null,
                fw != null && fw.Mouse != null, "fwmouse");
            return p.Bottom;
        }

        void FirmwareAction(Canvas c, RectangleF p, int i, float rh, FirmwareInfo fw, bool busy, bool update, string latest, string url, bool known, string id)
        {
            var th = c.Th;
            var pf = c.PlatterFlat();
            float cy = p.Y + rh * i + rh / 2;
            var note = new RectangleF(p.Right - c.F(218), cy - c.F(10), c.F(200), c.F(20));
            if (busy || fw == null)
            {
                c.Text(busy ? "Checking…" : "", c.Sub, th.Secondary, pf, note, TextAlign.Right);
                return;
            }
            if (fw.Failed && latest == null)
            {
                c.Button(c.RowControl(p, i, rh, 120, 30, 18), "Try again", th.Label, id, Mouse.CheckFirmware);
                return;
            }
            if (update)
            {
                c.Button(c.RowControl(p, i, rh, 120, 30, 18), "Get " + latest, Color.White, id, delegate { App.OpenUrl(url); }, th.Blue);
                return;
            }
            if (known)
            {
                c.Text("Up to date", c.SmallSemi, th.Green, pf, note, TextAlign.Right);
                return;
            }
            // Version unknown (mouse on wireless): still offer the updater for the latest build.
            if (url != null)
                c.Button(c.RowControl(p, i, rh, 150, 30, 18), "Updater " + latest, th.Label, id, delegate { App.OpenUrl(url); });
        }
    }
}
