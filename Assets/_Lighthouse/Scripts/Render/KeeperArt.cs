using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// 관리인 몸 스프라이트(원본 KEEPER 4252행 + drawKeeper 4420행의 굽기)와 옷 외형(SKINS.keeper 1650행), 줄마다 사각형 한 줄인 그림자 타원(ellipse 604행).
    /// 순수 C#이라 dotnet 에서 HTML 골든과 대조한다. 몸은 8×14 도트 블록 인물을 voxelize 한 장이다.
    /// </summary>
    public static class KeeperArt
    {
        static readonly string[] Head = { "..cccc..", ".cccccc.", "..ssss..", "..ssse..", "..ssss..", ".bbbbbb.", "BbbbbbbB", "BbbbbbbB", "sbbbbbbs", ".bbbbbb.", "..pppp.." };
        static readonly string[][] Legs = { new[] { "..pp.pp.", "..pp.pp.", "..dd.dd." }, new[] { "..pppp..", "..pppp..", "..dddd.." } };
        static readonly Dictionary<char, string> BasePal = new Dictionary<char, string> { { 'c', "#3d5a8a" }, { 's', "#f0c8a0" }, { 'b', "#e8b84a" }, { 'p', "#2c3a58" }, { 'd', "#20140c" }, { 'e', "#20140c" } };

        /// <summary>옷 외형 id → 덮어쓰는 색 (원본 SKINS.keeper 의 pal). 노을빛·달빛은 항구 이정표, 선원은 구매, dev 는 시험용.</summary>
        public static readonly Dictionary<string, Dictionary<char, string>> SkinPal = new Dictionary<string, Dictionary<char, string>>
        {
            { "classic", new Dictionary<char, string>() },
            { "dusk", new Dictionary<char, string> { { 'c', "#b4573e" }, { 'b', "#e8c86a" } } },
            { "moonlit", new Dictionary<char, string> { { 'c', "#3f4f86" }, { 'b', "#c8d4f0" } } },
            { "sailor", new Dictionary<char, string> { { 'c', "#f0f0e8" }, { 'b', "#2f4a7a" }, { 'p', "#2f4a7a" } } },
            { "dev", new Dictionary<char, string> { { 'c', "#8a3d5a" }, { 'b', "#d8b04a" } } },
        };
        public static bool IsSkin(string id) => id != null && SkinPal.ContainsKey(id);

        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        /// <summary>몸 스프라이트: dir &gt; 0 오른쪽을 본다, frame 0·1 은 다리 모양(걸을 때 번갈아). 원점은 (4, 14).</summary>
        public static Bitmap Body(string skin, int dir, int frame)
        {
            if (!IsSkin(skin)) skin = "classic";
            string key = skin + ":" + (dir > 0 ? 1 : -1) + ":" + frame; if (cache.TryGetValue(key, out var b)) return b;
            var pal = new Dictionary<char, string>(BasePal); foreach (var kv in SkinPal[skin]) pal[kv.Key] = kv.Value;
            pal['B'] = Col.HexOf(Col.Mul(Col.Hex(pal['b']), 0.78));
            var rows = Head.Concat(Legs[frame]).Select(r => dir > 0 ? r : new string(r.Reverse().ToArray())).ToArray();
            return cache[key] = ShipBaker.FromDesign(new ShipDesign { Ox = 4, Oy = 14, Rows = rows }, pal);
        }

        /// <summary>HTML ellipse(): 줄마다 사각형 한 줄. 중심이 (0, 0) 인 straight RGBA 비트맵(원점 = 중심 한 줄 아래).</summary>
        public static Bitmap Ellipse(double rx, double ry, int r, int g, int b, int a)
        {
            int n = (int)Math.Max(1, JsMath.Round(ry)), hw = (int)Math.Ceiling(rx) + 2;
            var bm = new Bitmap(-hw, -n, hw * 2, n * 2);
            for (int i = -n; i < n; i++)
            {
                double v = (i + 0.5) / n, w = rx * Math.Sqrt(1 - v * v); int x0 = (int)JsMath.Round(0 - w), ww = (int)JsMath.Round(w * 2);
                for (int x = x0; x < x0 + ww; x++) { int bx = x + hw; if (bx >= 0 && bx < bm.W) bm.D[(i + n) * bm.W + bx] = Bitmap.Pack(r, g, b, a); }
            }
            return bm;
        }
    }
}
