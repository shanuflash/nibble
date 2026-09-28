using System;
using System.Collections.Generic;
using System.Drawing;

namespace Nibble.UI
{
    // Clickable regions recorded while painting a frame, in window coordinates.
    sealed class HitMap
    {
        public sealed class Target
        {
            public string Id;
            public RectangleF Bounds;
            public Action Invoke;
            public bool OnPress;   // fires on mouse down instead of on release, like switches
        }

        readonly List<Target> targets = new List<Target>();

        // Added to every rect; painting code works in its own translated coordinates.
        public PointF Offset;

        public void Clear() { targets.Clear(); }

        public void Add(string id, RectangleF r, Action invoke) { Add(id, r, invoke, false); }

        public void Add(string id, RectangleF r, Action invoke, bool onPress)
        {
            targets.Add(new Target { Id = id, Bounds = new RectangleF(r.X + Offset.X, r.Y + Offset.Y, r.Width, r.Height), Invoke = invoke, OnPress = onPress });
        }

        // The topmost target under p, or null.
        public Target At(Point p)
        {
            for (int i = targets.Count - 1; i >= 0; i--)
                if (targets[i].Bounds.Contains(p)) return targets[i];
            return null;
        }
    }
}
