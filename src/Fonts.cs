using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Nibble
{
    // Inter (SIL OFL, see fonts/OFL.txt) is embedded in the exe and registered privately for this process:
    // with GDI so DrawText can render it, and with GDI+ so Font objects and metrics work.
    static class Fonts
    {
        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr data, uint len, IntPtr pdv, ref uint count);

        static readonly string[] Embedded = { "Inter-Regular.ttf.gz", "Inter-SemiBold.ttf.gz", "InterDisplay-SemiBold.ttf.gz" };
        static PrivateFontCollection pfc;

        public const string Text = "Inter", TextSemibold = "Inter SemiBold", Display = "Inter Display SemiBold";

        public static void Load()
        {
            if (pfc != null) return;
            pfc = new PrivateFontCollection();
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in Embedded)
            {
                using (var s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) continue;
                    byte[] data;
                    using (var z = new GZipStream(s, CompressionMode.Decompress))
                    using (var ms = new MemoryStream()) { z.CopyTo(ms); data = ms.ToArray(); }
                    // GDI+ needs the memory to outlive the collection; it lives for the process.
                    IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, mem, data.Length);
                    pfc.AddMemoryFont(mem, data.Length);
                    uint n = 0;
                    AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n);
                }
            }
        }

        // First available family: embedded fonts, then installed ones, then Segoe UI.

        public static FontFamily Family(params string[] names)
        {
            Load();
            foreach (var n in names)
                foreach (var f in pfc.Families)
                    if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f;
            return new FontFamily(Draw.PickFont(names));
        }
    }

    // UI text: unhinted greyscale anti-aliasing, so Inter keeps its real shapes the way browsers and macOS
    // draw it (GDI's hinting distorts Inter, which ships with minimal hints).
    static class TextStyle
    {
        public static readonly StringFormat Left = Make(StringAlignment.Near), Center = Make(StringAlignment.Center), Right = Make(StringAlignment.Far);
        static readonly Bitmap measureBmp = new Bitmap(1, 1);
        static readonly Graphics measure = Prepare(Graphics.FromImage(measureBmp));

        static StringFormat Make(StringAlignment h)
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.Alignment = h;
            f.LineAlignment = StringAlignment.Center;
            f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }

        static Graphics Prepare(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.TextContrast = 1;   // a touch fuller, closer to macOS weight
            return g;
        }

        public static void Draw(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat sf)
        {
            if (c.A == 0 || string.IsNullOrEmpty(s)) return;
            Prepare(g);
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }

        public static SizeF Measure(string s, Font f)
        {
            return measure.MeasureString(s, f, 10000, Left);
        }
    }
}