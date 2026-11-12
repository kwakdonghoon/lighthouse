using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lighthouse.Core;
using Lighthouse.Render;
using Lighthouse.Render.Verify;

namespace Lighthouse.G2Tool
{
    public static partial class Program
    {
        static partial void OtherCmd(string cmd, string[] a, ref int rc, ref bool handled)
        {
            handled = true; rc = 0;
            switch (cmd)
            {
                case "vis": Vis(a.Length > 1 && a[1] == "all"); break;
                case "sheet": Sheet(a.Length > 1 ? a[1] : "sheet.png", a.Length > 2 ? a[2] : "summer"); break;
                case "search": Search(a); break;
                case "unlock": Unlock(); break;
                case "searchrand": SearchRand(a); break;
                case "scene": Scene(a); break;
                case "chk": Chk(a.Length > 1 && a[1] == "all"); break;
                default: handled = false; rc = 1; break;
            }
        }

        static void Sheet(string path, string season)
        {
            const int cw = 24, ch = 28, cols = 9; var items = DecorBaker.Items; int rows = (items.Count + cols - 1) / cols;
            var px = new uint[cols * cw * rows * ch];
            for (int k = 0; k < items.Count; k++)
            {
                var c = new CpuCanvas(cw, ch); for (int i = 0; i < c.D.Length; i++) c.D[i] = (i / cw) > ch - 7 ? Bitmap.Pack(78, 130, 80, 255) : Bitmap.Pack(150, 196, 226, 255); c.Put(DecorBaker.Bake(items[k], season, "classic"), cw / 2, ch - 6);
                int ox = (k % cols) * cw, oy = (k / cols) * ch;
                for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++) px[(oy + y) * cols * cw + ox + x] = c.D[y * cw + x];
            }
            PngLite.Save(path, px, cols * cw, rows * ch, 6);
            Console.WriteLine("saved " + path + " items " + items.Count);
        }

        static void Chk(bool all)
        {
            var r = DecorCheck.SlotCheck(DecorBaker.Slots, all);
            Console.WriteLine("count " + r.Count + " violations " + r.Violations.Count + " minPair " + r.MinPairDots.ToString("F2") + " nonPath " + r.MinPairNonPathDots.ToString("F2"));
            foreach (var v in r.Violations) Console.WriteLine("  [겹침] " + v);
            foreach (var v in r.FrontTouch.Take(8)) Console.WriteLine("  [앞쪽] " + v);
        }

        static void Vis(bool all)
        {
            var r = DecorCheck.Visibility(DecorBaker.Slots, DecorBaker.Items, all);
            Console.WriteLine("min " + r.Min.ToString("F3") + " at " + r.MinAt + "  bad " + r.Bad.Count);
            var ids = new List<string>(); foreach (var st in r.Rows.Values) foreach (var k in st.Keys) if (!ids.Contains(k)) ids.Add(k);
            foreach (var id in ids)
            {
                var sb = new StringBuilder(id.PadRight(4));
                for (int st = 1; st <= 6; st++) sb.Append(r.Rows[st].TryGetValue(id, out var v) ? (v * 100).ToString("F0").PadLeft(5) : "    -");
                Console.WriteLine(sb);
            }
            foreach (var b in r.Bad) Console.WriteLine("  [가림] " + b);
        }
    }
}
