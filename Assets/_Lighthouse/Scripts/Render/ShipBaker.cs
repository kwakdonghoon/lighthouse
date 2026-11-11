using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>배 도면 한 장 (글자 격자). ox, oy = 왼쪽 위에서 (배 중심, 물에 닿는 선)까지의 거리.</summary>
    public sealed class ShipDesign
    {
        public int Ox, Oy; public string[] Rows;
        public int W => Rows[0].Length; public int H => Rows.Length;
    }

    /// <summary>
    /// 배 9종 × 항로 3 (HTML 1328~1480행): 블록 설계도 → 글자 격자 → 도트 스프라이트(voxelize). 오른쪽이 뱃머리.
    /// 항로 u = 2·3·4 (블록 하나 = 2·3·4 도트; 먼 항로는 선체를 어둡게).
    /// </summary>
    public static class ShipBaker
    {
        public static readonly string[] Types = { "fish", "trade", "ferry", "rowboat", "schooner", "steamer", "galleon", "cruise", "explorer" };
        public static readonly string[] SkinIds = { "classic", "coral", "friends", "gilded", "dev" };

        static readonly Dictionary<string, (string[] hull, string[] rows)> BP = new Dictionary<string, (string[], string[])>
        {
            { "fish", (new[] { "j", "k", "s" }, new[] { "..f.....", "..MS....", "..MSS...", "..MSSS..", "..MSSSS.", "..M...H.", "HHHWHHHH", ".HHHHHH." }) },
            { "trade", (new[] { "F", "L", "E" }, new[] { "...f.....f..", "...M.....M..", "..SMS...SMS.", "..SMS...SMS.", "..SMS...SMS.", "...M.....M..", "HHHHHHHHHHHH", "HHHWHHWHHWHH", ".HHHHHHHHHH.", "..HHHHHHHH.." }) },
            { "ferry", (new[] { "F", "L", "E" }, new[] { ".....Y...f..", ".....R...M..", "...CCCC..M..", "...CWCW..M..", ".CCCCCCCCC..", ".CWCWCWCWC..", "HHHHHHHHHHHH", ".HHHHHHHHHH.", "..HHHHHHHH.." }) },
            { "rowboat", (new[] { "j", "k", "s" }, new[] { "..f...", "..MS..", "..MSS.", "..M..H", "HHHHHH", ".HHHH." }) },
            { "schooner", (new[] { "F", "L", "E" }, new[] { "..f.....f.", "..M.....M.", ".SMS...SMS.", ".SMS...SMS.", "..M.....M.", "HHHHHHHHHH", ".HHWHHWHH.", "..HHHHHH.." }) },
            { "steamer", (new[] { "E", "z", "M" }, new[] { "..Y..Y....f..", "..R..R....M..", ".CCCCCCC..M..", ".CWCWCWC..M..", ".CCCCCCCC.M..", "HHHHHHHHHHHHH", ".HHWHHWHHWHH.", "..HHHHHHHHH.." }) },
            { "galleon", (new[] { "m", "w", "o" }, new[] { "......f...f...f..", "......M...M...M..", ".....SMS.SMS.SMS.", ".....SMS.SMS.SMS.", ".....SMS.SMS.SMS.", ".....SMS.SMS.SMS.", "......M...M...M..", "HHHH..M...M...M..", "HHHHHHHHHHHHHHHHH", "HWHWHHWHHWHHWHHHH", ".HHHHHHHHHHHHHHH.", "..HHHHHHHHHHHHH.." }) },
            { "cruise", (new[] { "J", "J", "O" }, new[] { "........Y.......f..", "........R.......M..", "...CCCCCCCCC....M..", "...CWCWCWCWC....M..", ".CCCCCCCCCCCCC..M..", ".CWCWCWCWCWCWC..M..", "CCCCCCCCCCCCCCCCC..", "HHHHHHHHHHHHHHHHHHH", ".HWHWHWHWHWHWHWHWH.", "..HHHHHHHHHHHHHHH.." }) },
            { "explorer", (new[] { "s", "x", "t" }, new[] { "..f...f...f..", "..M...M...M..", ".SMS.SMS.SMS.", ".SMS.SMS.SMS.", ".SMS.SMS.SMS.", ".SMS.SMS.SMS.", "..M...M...M..", "HHHHHHHHHHHHH", ".HHWHHWHHWHH.", "..HHHHHHHHH.." }) },
        };

        // 선체 색 (shipcolor): [윗면 밝게, 가운데, 아래·오른쪽 어둡게, (먼 항로 어둡기)]
        static readonly Dictionary<string, (string[] c, double far)> HullC = new Dictionary<string, (string[], double)>
        {
            { "fish", (new[] { "#f2d8a0", "#d8a868", "#8a5a2c" }, 0.86) }, { "rowboat", (new[] { "#f2d8a0", "#d8a868", "#8a5a2c" }, 0.86) },
            { "trade", (new[] { "#f8b094", "#ec8a6c", "#8a3a2c" }, 0.95) },
            { "ferry", (new[] { "#fbe49a", "#e8c05c", "#9a7426" }, 0.86) },
            { "schooner", (new[] { "#ffd8c0", "#f2b08c", "#a0604c" }, 0.86) },
            { "steamer", (new[] { "#f0ebe0", "#d2cabb", "#7a7468" }, 0.86) },
            { "galleon", (new[] { "#efc088", "#cf9156", "#68381a" }, 0.95) },
            { "explorer", (new[] { "#e8d890", "#c4aa5e", "#6e5a24" }, 0.86) },
        };

        // SHIP_PAL (1335~1380행)
        static readonly string PalText = "a=#e0453e b=#7a5030 c=#5a3a1f d=#c9403a e=#5a4a30 f=#c2bba4 g=#ddd4ba h=#aca48d i=#20140c j=#d4a468 k=#a8733f l=#ffe9a0 m=#c0915a n=#ad8351 o=#55351b p=#f1ead6 q=#d4cebc r=#c4baa0 s=#9a6638 t=#7a4b26 u=#c9c2ae v=#e4dcc6 w=#9e7443 x=#8a5a30 y=#b0814a z=#1f293e A=#f6f0e0 B=#d8d3c5 C=#cbc2a8 D=#b3ab94 E=#2c3a58 F=#3d4f78 G=#b83a36 H=#f0c040 J=#cfd6e0 I=#7a5236 K=#3a2414 L=#3a4c72 M=#101a30 N=#f6d890 O=#8a8068 P=#3e382c Q=#9a3030 R=#4a1414";

        static Dictionary<char, string> basePal;
        static Dictionary<string, (char[] near, char[] far)> hullKeys;
        static Dictionary<string, ShipDesign[]> designs;

        static string DkHex(string h, double f)
        {
            var c = Col.Hex(h);
            Func<double, string> t = v => ((int)JsMath.Round(v * f)).ToString("x2");
            return "#" + t(c.R) + t(c.G) + t(c.B);
        }

        static void Init()
        {
            if (basePal != null) return;
            var pal = new Dictionary<char, string>();
            foreach (var kv in PalText.Split(' ')) pal[kv[0]] = kv.Substring(2);
            hullKeys = new Dictionary<string, (char[], char[])>();
            int hn = 0;
            Func<string, bool, char[]> keys = (type, far) => HullC[type].c.Select(c => { char ch = (char)(0x3b1 + hn++); pal[ch] = far ? DkHex(c, HullC[type].far) : c; return ch; }).ToArray();
            foreach (var k in BP.Keys.ToList()) if (HullC.ContainsKey(k)) { var near = keys(k, false); var far = keys(k, true); hullKeys[k] = (near, far); }
            basePal = pal;
            designs = new Dictionary<string, ShipDesign[]>();
            foreach (var k in Types) designs[k] = new[] { 2, 3, 4 }.Select(u => Ship(k, u)).ToArray();
        }

        static bool Solid(char c) => "HWCRY".IndexOf(c) >= 0;

        /// <summary>도면 → 글자 격자 (HTML ship(bp, u) 1411행).</summary>
        static ShipDesign Ship(string type, int u)
        {
            var bp = BP[type];
            int wB0 = bp.rows.Max(r => r.Length);
            var rows = bp.rows.Select(r => r.PadRight(wB0, '.')).ToArray();
            int h = rows.Length, wB = wB0, W = wB * u, H = h * u;
            var g = new char[H][]; for (int i = 0; i < H; i++) { g[i] = new char[W]; for (int j = 0; j < W; j++) g[i][j] = '.'; }
            Action<int, int, char> put = (x, y, c) => { if (x >= 0 && y >= 0 && x < W && y < H) g[y][x] = c; };
            Func<int, int, char> at = (cx, cy) => (cy >= 0 && cy < rows.Length && cx >= 0 && cx < rows[cy].Length) ? rows[cy][cx] : '.';
            char hl, hm, hd;
            if (hullKeys.TryGetValue(type, out var hk)) { var a = u == 2 ? hk.far : hk.near; hl = a[0]; hm = a[1]; hd = a[2]; }
            else { hl = bp.hull[0][0]; hm = bp.hull[1][0]; hd = bp.hull[2][0]; }
            for (int cy = 0; cy < h; cy++)
                for (int cx = 0; cx < wB; cx++)
                {
                    char ch = rows[cy][cx]; int x0 = cx * u, y0 = cy * u;
                    if (ch == '.') continue;
                    Action<char, char, char> fill = (m, light, dark) =>
                    {
                        for (int y = 0; y < u; y++) for (int x = 0; x < u; x++) put(x0 + x, y0 + y, m);
                        if (!Solid(at(cx, cy - 1))) for (int x = 0; x < u; x++) put(x0 + x, y0, light);
                        if (u >= 3) { for (int x = 0; x < u; x++) put(x0 + x, y0 + u - 1, dark); for (int y = 0; y < u; y++) put(x0 + u - 1, y0 + y, dark); }
                        else put(x0 + 1, y0 + 1, dark);
                    };
                    if (ch == 'H') fill(hm, hl, hd);
                    else if (ch == 'C') fill('p', 'p', 'q');
                    else if (ch == 'W')
                    {
                        fill(hm, hl, hd);
                        for (int y = 0; y < Math.Max(1, u - 1); y++) for (int x = 0; x < Math.Max(1, u - 1); x++) put(x0 + x, y0 + y, 'l');
                    }
                    else if (ch == 'Y') { for (int y = 0; y < u; y++) for (int x = 0; x < u; x++) put(x0 + x, y0 + y, 'P'); }
                    else if (ch == 'R') { for (int y = 0; y < u; y++) for (int x = 0; x < u; x++) put(x0 + x, y0 + y, x == u - 1 ? 'Q' : 'G'); }
                    else if (ch == 'S')
                    {
                        for (int y = 0; y < u; y++) for (int x = 0; x < u; x++) put(x0 + x, y0 + y, 'A');
                        bool sailRight = at(cx + 1, cy) == 'S' || (at(cx + 1, cy) == 'M' && at(cx + 2, cy) == 'S');
                        if (!sailRight) for (int y = 0; y < u; y++) put(x0 + u - 1, y0 + y, 'B');
                        if (at(cx, cy + 1) != 'S') for (int x = 0; x < u; x++) put(x0 + x, y0 + u - 1, 'C');
                    }
                    else if (ch == 'M' || ch == 'f')
                    {
                        if (ch == 'M' && at(cx - 1, cy) == 'S' && at(cx + 1, cy) == 'S') for (int y = 0; y < u; y++) for (int x = 0; x < u; x++) put(x0 + x, y0 + y, 'A');
                        int mx = x0 + (int)Math.Floor((u - 2) / 2.0);
                        for (int y = 0; y < u; y++) { put(mx, y0 + y, 'b'); put(mx + 1, y0 + y, 'c'); }
                        if (ch == 'f') for (int i = 2; i <= 4; i++) { put(mx + i, y0, 'a'); put(mx + i, y0 + 1, 'd'); }
                    }
                }
            return new ShipDesign { Ox = W / 2, Oy = H - 1, Rows = g.Select(r => new string(r)).ToArray() };
        }

        /// <summary>종 × 항로(0 먼 곳, 1 가운데, 2 가까운 곳)의 도면.</summary>
        public static ShipDesign Design(string type, int lane) { Init(); return designs[type][lane]; }

        /// <summary>팔레트: 기본 + 외형이 덮는 색. 깃발(a, d)은 빼고(자리 비움), helped 이면 돛 글자 12개를 따뜻한 금빛으로.</summary>
        public static Dictionary<char, string> Palette(string skin, bool helped)
        {
            Init();
            var p = new Dictionary<char, string>(basePal);
            foreach (var kv in Palettes.ShipSkin(skin)) p[kv.Key] = kv.Value;
            if (helped) foreach (char k in "pqgfhruvABCD") p[k] = WarmSail(p[k]);
            p['a'] = null; p['d'] = null;
            return p;
        }

        // 도움을 받은 배: rgb(min(255,r+14), round(g*0.93), round(b*0.55))
        static string WarmSail(string hex)
        {
            var c = Col.Hex(hex);
            return string.Format("#{0:x2}{1:x2}{2:x2}", (int)Math.Min(255, c.R + 14), (int)JsMath.Round(c.G * 0.93), (int)JsMath.Round(c.B * 0.55));
        }

        /// <summary>Uint8ClampedArray 대입: 0..255 로 고정하고 .5 는 짝수 쪽으로 (JS ToUint8Clamp).</summary>
        public static int ClampedByte(double v)
        {
            if (!(v > 0)) return 0;
            if (v >= 255) return 255;
            double f = Math.Floor(v), d = v - f;
            if (d < 0.5) return (int)f;
            if (d > 0.5) return (int)f + 1;
            return ((int)f % 2 == 0) ? (int)f : (int)f + 1;
        }

        /// <summary>배를 블록 질감으로 (voxelize 1459행): 어두운 점은 밝히고, 윗가장자리는 밝게·오른쪽/아래 가장자리는 어둡게, 4×4 바둑 결.</summary>
        static void Voxelize(uint[] d, int w, int h)
        {
            var src = (uint[])d.Clone();
            Func<int, int, uint> A = (x, y) => (x < 0 || y < 0 || x >= w || y >= h) ? 0u : src[y * w + x] >> 24;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    uint s = src[y * w + x]; if ((s >> 24) == 0) continue;
                    double r = s & 255, g = (s >> 8) & 255, b = (s >> 16) & 255, f = 1;
                    if (0.3 * r + 0.59 * g + 0.11 * b < 62) { r = r * 1.4 + 16; g = g * 1.4 + 14; b = b * 1.4 + 12; }
                    if (A(x, y - 1) == 0) f = 1.2; else if (A(x + 1, y) == 0 || A(x, y + 1) == 0) f = 0.78;
                    f *= (((x >> 2) ^ (y >> 2)) & 1) != 0 ? 0.95 : 1.03;
                    d[y * w + x] = Bitmap.Pack(ClampedByte(Math.Min(255, r * f)), ClampedByte(Math.Min(255, g * f)), ClampedByte(Math.Min(255, b * f)), (int)(s >> 24));
                }
        }

        /// <summary>글자 격자 → 스프라이트 (spriteFromData 1472행): 글자 하나 = 도트 하나, 팔레트에 없거나 null 이면 투명.</summary>
        public static Bitmap FromDesign(ShipDesign d, Dictionary<char, string> pal)
        {
            int w = d.W, h = d.H;
            var B = new Bitmap(-d.Ox, -d.Oy, w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!pal.TryGetValue(d.Rows[y][x], out var col) || col == null) continue;
                    var c = Col.Hex(col); B.D[y * w + x] = Bitmap.Pack((int)c.R, (int)c.G, (int)c.B, 255);
                }
            Voxelize(B.D, w, h);
            return B;
        }

        public static Bitmap Sprite(string type, int lane, string skin = "classic", bool helped = false) => FromDesign(Design(type, lane), Palette(skin, helped));
    }
}
