using System;
using Nibble.UI.Popups;

namespace Nibble
{
    sealed class BatteryAlerts
    {
        const int LowAt = 20, RearmAbove = 25;
        bool lowShown, fullShown;

        public void Check(MouseSession m, Preferences prefs, Action onClick)
        {
            if (!m.Online || m.Percent < 0) return;
            if (m.Charging || m.Percent > RearmAbove) lowShown = false;
            if (!m.FullyCharged) fullShown = false;
            if (!prefs.BatteryAlerts) return;

            if (!m.Charging && m.Percent <= LowAt && !lowShown)
            {
                lowShown = true;
                ShowLow(m.Device.Name, m.Percent, onClick, prefs.AlertsOverGames);
            }
            if (m.FullyCharged && !fullShown)
            {
                fullShown = true;
                ShowFull(m.Device.Name, onClick, prefs.AlertsOverGames);
            }
        }

        public const string LowTitle = "Mouse battery low", FullTitle = "Fully charged";
        public static string LowBody(string mouse, int percent) { return string.Format("{0} is at {1}%. Plug it in soon.", mouse, percent); }
        public static string FullBody(string mouse) { return mouse + " is at 100%. You can unplug it."; }

        public static void ShowLow(string mouse, int percent, Action onClick, bool overGames)
        {
            Toast.Show(Toast.Kind.Low, LowTitle, LowBody(mouse, percent), onClick, overGames);
        }

        public static void ShowFull(string mouse, Action onClick, bool overGames)
        {
            Toast.Show(Toast.Kind.Full, FullTitle, FullBody(mouse), onClick, overGames);
        }
    }
}
