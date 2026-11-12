using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    public sealed class DecorItem
    {
        public string Id, Name, Size, Src; public string[] Zones; public int Ax, Ay; public bool Ss, Sk; public string[] Rows;
        public bool Flat => Zones[0] == "path" || Zones[0] == "sky";
        public int W => Rows[0].Length; public int H => Rows.Length;
    }

    public sealed class DecorSlot
    {
        public string Id, Chip, Size, OpenKind; public bool Xy, Blocked; public double Ka, Ca, Kb, Cb; public int OpenVal; public int MinStage = 1;     // MinStage: 정원 레벨로 열리는 자리도 이 섬 단계 이상이어야 열린다(G2')
        public int Num => int.Parse(new string(Id.Where(char.IsDigit).ToArray()));
        public double[] AB(int N) => new[] { Ka * N + Ca, Kb * N + Cb };
        /// <summary>HTML slotOpenS: 막힘이 아니고(base 처음부터 / stage 단계 이상 / lv 정원 레벨 이상).</summary>
        public bool OpenAt(int stage, int gardenLv) => !Blocked && stage >= MinStage && (OpenKind == "base" || (OpenKind == "stage" && stage >= OpenVal) || (OpenKind == "lv" && gardenLv >= OpenVal));
    }

    /// <summary>꾸미기 장식(HTML 3221~3365행): 격자 → 도트 → 윤곽 처리(voxelIcon) → 바닥 그림자. 아이템 10종 × 계절 4 × 마을 외형.</summary>
    public static class DecorBaker
    {
        public static readonly Dictionary<char, string> Letters = RenderData.DecorLetters.ToDictionary(s => s[0], s => s.Substring(2));
        static readonly Dictionary<string, string[]> Season = RenderData.DecorSeason.Select(s => s.Split(';')).ToDictionary(p => p[0] + "|" + p[1], p => new[] { p[2], p[3], p[4] });
        public static readonly List<DecorItem> Items = RenderData.DecorItems.Select(l =>
        {
            var p = l.Split(';');
            return new DecorItem { Id = p[0], Name = p[1], Size = p[2], Zones = p[3].Split(','), Src = p[4], Ax = int.Parse(p[5]), Ay = int.Parse(p[6]), Ss = p[7] == "1", Sk = p[8] == "1", Rows = p[9].Split('|') };
        }).ToList();
        public static readonly List<DecorSlot> Slots = RenderData.DecorSlots.Select(l =>
        {
            var p = l.Split(';'); Func<string, double> d = s => double.Parse(s, CultureInfo.InvariantCulture);
            return new DecorSlot { Id = p[0], Chip = p[1], Size = p[2], Xy = p[3] == "xy", Ka = d(p[4]), Ca = d(p[5]), Kb = d(p[6]), Cb = d(p[7]), OpenKind = p[8], OpenVal = int.Parse(p[9]), Blocked = p[10] == "1", MinStage = p.Length > 11 ? int.Parse(p[11]) : 1 };
        }).ToList();

        public static DecorItem Item(string id) => Items.First(i => i.Id == id);
        public static DecorSlot Slot(string id) => Slots.First(s => s.Id == id);
        public static int SizeRank(string s) => s == "S" ? 1 : s == "M" ? 2 : 3;

        /// <summary>글자 → 색 표 (계절·마을 외형 치환 적용). dp: 외형이 정한 꽃·울타리 색(sk 아이템만).</summary>
        public static Dictionary<char, string> Colors(string season, DecorPal dp)
        {
            var c = new Dictionary<char, string>(Letters);
            if (dp != null)
            {
                c['1'] = dp.Flowers[0]; c['2'] = dp.Flowers[1];
                if (dp.Fence != Letters['F'])
                {
                    var f = Col.Hex(dp.Fence);
                    c['F'] = dp.Fence; c['f'] = Col.HexOf(Col.Mul(f, 0.84)); c['g'] = Col.HexOf(Col.Mul(f, 0.6));
                }
            }
            var pine = Season["pine|" + season]; c['P'] = pine[0]; c['p'] = pine[1]; c['q'] = pine[2];
            var leaf = Season["leaf|" + season]; c['L'] = leaf[0]; c['l'] = leaf[1]; c['m'] = leaf[2];
            if (season == "winter") { c['F'] = "#f4f8fc"; c['C'] = "#e8f0f8"; c['T'] = "#dfeaf2"; c['1'] = "#c8d4de"; c['2'] = "#e4ecf4"; c['M'] = "#e4ecf4"; }   // 눈 덮임: 윗면만
            return c;
        }

        /// <summary>격자 → 비트맵 → voxelIcon. fixedColors: 계절·외형과 무관한 기본(낮·여름) 색(아이콘용).</summary>
        public static Bitmap Body(DecorItem it, string season, string decorSkin, bool fixedColors)
        {
            var col = Colors(fixedColors ? "summer" : season, !fixedColors && it.Sk ? Palettes.Decor(decorSkin) : null);
            var B = new Bitmap(0, 0, it.W, it.H);
            for (int y = 0; y < it.H; y++)
                for (int x = 0; x < it.W; x++)
                {
                    char ch = it.Rows[y][x];
                    if (ch == '.' || !col.TryGetValue(ch, out var hex)) continue;
                    var c = Col.Hex(hex); B.D[y * it.W + x] = Bitmap.Pack((int)c.R, (int)c.G, (int)c.B, 255);
                }
            IconBaker.VoxelIcon(B.D, B.W, B.H);
            return B;
        }

        const int PX = 8, PB = 4;

        /// <summary>낮 한 장: 몸통 + 바닥 그림자(길·하늘 장식은 그림자 없음). 원점은 땅에 닿는 점.</summary>
        public static Bitmap Bake(DecorItem it, string season, string decorSkin)
        {
            var body = Body(it, season, decorSkin, false);
            int w = it.W, h = it.H;
            var B = new Bitmap(-(it.Ax + PX), -it.Ay, w + PX * 2, h + PB);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) B.D[y * B.W + x + PX] = body.D[y * w + x];
            if (!it.Flat)                                                                 // destination-over: 비어 있는 곳에만 rgba(0,0,0,0.28)
            {
                double rx = it.Size == "S" ? 4 : it.Size == "M" ? 6 : 8, ry = it.Size == "S" ? 1.5 : it.Size == "M" ? 2 : 3;
                double cx = it.Ax + PX, cy = it.Ay;
                int n = (int)Math.Max(1, JsMath.Round(ry)); double y0 = JsMath.Round(cy);
                uint shadow = Bitmap.Pack(0, 0, 0, (int)JsMath.Round(0.28 * 255));
                for (int i = -n; i < n; i++)
                {
                    double v = (i + 0.5) / n, ww = rx * Math.Sqrt(1 - v * v);
                    int x0 = (int)JsMath.Round(cx - ww), rw = (int)JsMath.Round(ww * 2), yy = (int)(y0 + i);
                    if (yy < 0 || yy >= B.H) continue;
                    for (int x = x0; x < x0 + rw; x++)
                    {
                        if (x < 0 || x >= B.W) continue;
                        if ((B.D[yy * B.W + x] >> 24) == 0) B.D[yy * B.W + x] = shadow;
                    }
                }
            }
            return B;
        }

        /// <summary>꾸미기 목록 아이콘: 낮 그림(그림자 제외)을 16×16 한가운데에.</summary>
        public static Bitmap Icon(DecorItem it)
        {
            var body = Body(it, "summer", "classic", true);
            var B = new Bitmap(0, 0, 16, 16);
            int ox = (16 - body.W) >> 1, oy = (int)Math.Floor((16 - body.H) / 2.0);
            for (int y = 0; y < body.H; y++)
                for (int x = 0; x < body.W; x++)
                {
                    int bx = x + ox, by = y + oy;
                    if (bx < 0 || by < 0 || bx >= 16 || by >= 16) continue;
                    if ((body.D[y * body.W + x] >> 24) != 0) B.D[by * 16 + bx] = body.D[y * body.W + x];
                }
            return B;
        }
    }
}
