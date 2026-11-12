using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Render;
using Lighthouse.Render.Verify;

namespace Lighthouse.G2Tool
{
    public static partial class Program
    {
        /// <summary>scene out.png stage [night(0|1)] [mode(tall|cycle)]: 열린 자리를 모두 채운 장면(검토용 PNG, 3배).</summary>
        static void Scene(string[] a)
        {
            string path = a[1]; int st = int.Parse(a[2]); int night = a.Length > 3 ? int.Parse(a[3]) : 0; string mode = a.Length > 4 ? a[4] : "tall";
            var put = new List<(string, string)>(); int k = 0;
            foreach (var sl in DecorBaker.Slots)
            {
                if (!sl.OpenAt(st, 32)) continue;
                var al = DecorBaker.Items.Where(i => i.Zones.Contains(sl.Chip) && DecorBaker.SizeRank(i.Size) <= DecorBaker.SizeRank(sl.Size)).ToList();
                if (al.Count == 0) continue;
                var it = mode == "tall" ? al.OrderByDescending(i => i.H).First() : al[k++ % al.Count];
                put.Add((sl.Id, it.Id));
            }
            var sp = SceneSprites.Build(216, BgBaker.DesignH, "summer", st, gardenLv: 6, slotLv: 32, put: put);
            var c = SceneComposer.Compose(sp, night);
            int y0 = 120, y1 = 320; var px = new uint[216 * (y1 - y0)];
            Array.Copy(c.D, y0 * 216, px, 0, px.Length);
            PngLite.Save(path, px, 216, y1 - y0, 4);
            Console.WriteLine("saved " + path + " slots " + put.Count);
        }
    }
}
