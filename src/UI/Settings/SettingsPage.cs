using System.Drawing;
using Nibble.Devices;

namespace Nibble.UI.Settings
{
    abstract class SettingsPage
    {
        protected readonly TrayApp App;
        protected SettingsPage(TrayApp app) { App = app; }

        protected MouseSession Mouse { get { return App.Mouse; } }

        public abstract string Title { get; }
        public abstract Symbol Icon { get; }
        public abstract Color Tint { get; }

        public virtual void Opened() { }
        public abstract void Paint(Canvas c, RectangleF area);

        // Pointer capture for sliders; x is in card coordinates.
        public virtual bool BeginDrag(string hitId, float x) { return false; }
        public virtual void DragTo(float x) { }
        public virtual void EndDrag() { }

        // Placeholder shown until the mouse's settings have been read. False if there's nothing to edit yet.
        protected bool HaveSettings(Canvas c, RectangleF area, float y)
        {
            if (Mouse.Settings != null) return true;
            var p = new RectangleF(area.X, y, area.Width, c.F(120));
            c.Platter(p);
            string msg = Mouse.Found ? "Reading settings from the mouse…" : "Plug in the " + Mouse.Device.Name + " receiver to change settings.";
            c.Text(msg, c.Row, c.Th.Secondary, c.PlatterFlat(), p, TextAlign.Center);
            return false;
        }
    }
}
