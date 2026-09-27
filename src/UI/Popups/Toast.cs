using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Nibble.Platform;

namespace Nibble.UI.Popups
{
    // Nibble's notification card. Over a borderless game it can show click-through, so aim and clicks still
    // reach the game. Exclusive fullscreen and presentations can't be drawn over, so it waits for them to end.
    sealed class Toast : GlassPopup
    {
        public enum Kind { Low, Full }

        static Toast current, pending;
        static Timer waitTimer;

        readonly Kind kind;
        readonly string title, body;
        readonly Action onClick;
        readonly Font fTitle, fBody;
        bool hovering;

        public static void Show(Kind kind, string title, string body, Action onClick, bool overGames)
        {
            var mode = Shell.CurrentScreenMode();
            bool game = mode == ScreenMode.Game;
            var toast = new Toast(kind, title, body, onClick, game && overGames);
            if (mode == ScreenMode.Exclusive || (game && !overGames)) Defer(toast);
            else toast.Present();
        }

        static void Defer(Toast toast)
        {
            if (pending != null) pending.Dispose();
            pending = toast;
            if (waitTimer == null)
            {
                waitTimer = new Timer { Interval = 15000 };
                waitTimer.Tick += delegate
                {
                    if (pending == null || Shell.CurrentScreenMode() != ScreenMode.Normal) return;
                    waitTimer.Stop();
                    var p = pending; pending = null;
                    p.Present();
                };
            }
            waitTimer.Start();
        }

        Toast(Kind kind, string title, string body, Action onClick, bool clickThrough)
            : base(340, 76, 22, clickThrough, 280, 6000, 180)
        {
            this.kind = kind; this.title = title; this.body = body; this.onClick = onClick;
            fTitle = new Font(Draw.PickFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold"), 15 * S, FontStyle.Regular, GraphicsUnit.Pixel);
            fBody = new Font(Draw.PickFont("Segoe UI Variable Text", "Segoe UI"), 13 * S, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        void Present()
        {
            if (current != null) current.Dismiss();
            current = this;
            Open();
        }

        protected override bool Paused { get { return hovering; } }

        protected override void PaintContent(Graphics g, RectangleF card)
        {
            var tile = new RectangleF(card.X + F(16), card.Y + F(16), F(44), F(44));
            Color c = kind == Kind.Low ? Th.Red : Th.Green;
            using (var path = Draw.Round(tile, F(12)))
            using (var br = new LinearGradientBrush(tile, Draw.Lerp(c, Color.White, 0.2f), Draw.Lerp(c, Color.Black, 0.08f), 90f))
                g.FillPath(br, path);
            Draw.Rim(g, tile, F(12), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Math.Max(1f, F(0.8f)));
            Icons.Fill(g, kind == Kind.Low ? Symbol.Battery : Symbol.Bolt, new PointF(tile.X + tile.Width / 2, tile.Y + tile.Height / 2), F(26), Color.White);

            g.Flush(FlushIntention.Sync);
            float x = tile.Right + F(14), w = card.Right - x - F(16);
            DrawText(title, fTitle, Th.Label, new RectangleF(x, card.Y + F(17), w, F(20)));
            DrawText(body, fBody, Th.Secondary, new RectangleF(x, card.Y + F(39), w, F(18)));
        }

        protected override void Finished() { Close(); }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Dismiss();
            if (e.Button == MouseButtons.Left && onClick != null) onClick();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (current == this) current = null;
            fTitle.Dispose(); fBody.Dispose();
            base.OnFormClosed(e);
        }
    }
}
