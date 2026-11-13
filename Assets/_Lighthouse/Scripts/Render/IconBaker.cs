using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>아이콘 한 종: 팔레트(글자→색)와 격자 줄. 8×8(선물·칩) 또는 16×16(메뉴).</summary>
    public sealed class IconDef
    {
        public string Id; public Dictionary<char, string> Pal; public string[] Rows;
    }

    /// <summary>
    /// 글자 격자 → 도트 아이콘 (HTML iconCanvas 2384행 + voxelIcon 2366행). 설계서 2-3-7: 아이콘은 빌드 때 구워 PNG 로 두는 편이 단순하므로
    /// 에디터 도구(M2Tools)가 이 베이커로 PNG 아틀라스를 만들고, 런타임에는 굽지 않는다.
    /// </summary>
    public static class IconBaker
    {
        static Dictionary<string, IconDef> defs;
        public static IReadOnlyList<string> Ids { get { Init(); return ids; } }
        static List<string> ids;

        static void Init()
        {
            if (defs != null) return;
            defs = new Dictionary<string, IconDef>(); ids = new List<string>();
            foreach (var line in RenderData.IconLines)
            {
                var p = line.Split(';');
                var pal = new Dictionary<char, string>();
                foreach (var kv in p[1].Split(',')) pal[kv[0]] = kv.Substring(2);
                defs[p[0]] = new IconDef { Id = p[0], Pal = pal, Rows = p[2].Split('|') };
                if (p[0] != "unknown") ids.Add(p[0]);
            }
        }

        public static IconDef Def(string id) { Init(); return defs.TryGetValue(id, out var d) ? d : defs["unknown"]; }

        /// <summary>모르는 id 는 "?" 대체 아이콘 (ICON_UNKNOWN).</summary>
        public static Bitmap Bake(string id) => Bake(Def(id));

        /// <summary>"id;팔레트;줄|줄…" 한 줄 정의를 직접 굽는다(Unity UI 가 동결 표 대신 쓰는 교체 그림용; 표·골든은 건드리지 않는다).</summary>
        public static Bitmap BakeLine(string line)
        {
            var p = line.Split(';'); var pal = new Dictionary<char, string>();
            foreach (var kv in p[1].Split(',')) pal[kv[0]] = kv.Substring(2);
            return Bake(new IconDef { Id = p[0], Pal = pal, Rows = p[2].Split('|') });
        }

        static Bitmap Bake(IconDef d)
        {
            int n = d.Rows.Length;
            var B = new Bitmap(0, 0, n, n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    if (!d.Pal.TryGetValue(d.Rows[y][x], out var col)) continue;
                    var c = Col.Hex(col); B.D[y * n + x] = Bitmap.Pack((int)c.R, (int)c.G, (int)c.B, 255);
                }
            VoxelIcon(B.D, n, n);
            return B;
        }

        /// <summary>
        /// 아이콘을 블록 문법으로 (voxelIcon): 아주 어두운 윤곽 점은 밝은 이웃 색의 어두운 톤으로, 윗 가장자리는 밝게, 오른쪽·아래 가장자리는 어둡게.
        /// 장식 그림(DecorBaker)도 같은 처리를 쓴다.
        /// </summary>
        public static void VoxelIcon(uint[] d, int w, int h)
        {
            var src = (uint[])d.Clone();
            Func<int, int, bool> A = (x, y) => !(x < 0 || y < 0 || x >= w || y >= h) && (src[y * w + x] >> 24) != 0;
            Func<uint, double> lum = s => 0.3 * (s & 255) + 0.59 * ((s >> 8) & 255) + 0.11 * ((s >> 16) & 255);
            int[][] nb = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    uint s = src[y * w + x]; if ((s >> 24) == 0) continue;
                    double r = s & 255, g = (s >> 8) & 255, b = (s >> 16) & 255;
                    if (lum(s) < 80)
                    {
                        double sr = 0, sg = 0, sb = 0; int k = 0;
                        foreach (var q in nb)
                        {
                            int xx = x + q[0], yy = y + q[1];
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            uint p = src[yy * w + xx];
                            if ((p >> 24) != 0 && lum(p) >= 80) { sr += p & 255; sg += (p >> 8) & 255; sb += (p >> 16) & 255; k++; }
                        }
                        if (k != 0) { r = sr / k * 0.55; g = sg / k * 0.55; b = sb / k * 0.6; }
                    }
                    double f = 1;
                    if (!A(x, y - 1)) f = 1.18; else if (!A(x + 1, y) || !A(x, y + 1)) f = 0.8;
                    d[y * w + x] = Bitmap.Pack(ShipBaker.ClampedByte(Math.Min(255, r * f)), ShipBaker.ClampedByte(Math.Min(255, g * f)), ShipBaker.ClampedByte(Math.Min(255, b * f)), (int)(s >> 24));
                }
        }
    }
}
