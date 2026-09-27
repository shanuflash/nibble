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
}
