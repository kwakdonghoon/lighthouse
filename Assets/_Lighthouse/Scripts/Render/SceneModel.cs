using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>섬 지형 판정과 땅 높이 (HTML 969~992행) — 장식 자리 좌표가 쓴다.</summary>
    public static class Terrain
    {
        public const double GZ = 2.5, VS = 4, VZ = 4, KY = 0.5;
        public const int CY = 224;                              // 섬 위쪽 면 중심 y (설계 좌표)
        public const double Ground = GZ * VZ, Rim = 3 * VZ, Deck = 2 * VZ, IsleY = Ground;
        static readonly int[] IsleNs = { 0, 6, 6, 7, 8, 8, 9 };
        static readonly int[] TierOf = { 0, 1, 1, 2, 2, 3, 3 };

        public static int N(int stage) => IsleNs[stage] != 0 ? IsleNs[stage] : 7;
        public static bool HasPier(int stage) => TierOf[stage] >= 3;

        public static bool TileSand(int stage, double a, double b)
        {
            int n = N(stage);
            if (b > n - 1 && Math.Abs(a + 0.5) < 1.6) return true;
            if (HasPier(stage) && a > n - 1 && Math.Abs(b - 0.5) < 1.6) return true;
            return false;
        }

        public static (double a, double b) Uv2Blk(double u, double v) => ((u + v) / (2 * VS), (v - u) / (2 * VS));
        public static (double u, double v) Blk2Uv(double a, double b) => ((a - b) * VS, (a + b) * VS);

        public static double TerrH(int stage, double u, double v)
        {
            var (a, b) = Uv2Blk(u, v); int n = N(stage);
            if (HasPier(stage) && a > n - 0.01 && a < n + 4.01 && b > 0 && b < 1) return Deck;
            if (Math.Abs(a) > n || Math.Abs(b) > n) return -1;
            if (Math.Abs(a) > n - 1 || Math.Abs(b) > n - 1)
                return TileSand(stage, Math.Min(n - 0.5, Math.Max(-n + 0.5, Math.Floor(a) + 0.5)), Math.Min(n - 0.5, Math.Max(-n + 0.5, Math.Floor(b) + 0.5))) ? Ground : Rim;
            return Ground;
        }
    }

    /// <summary>꾸미기 자리 좌표·놓기 목록 (HTML 3324~3405행, decorDevFill 3572행).</summary>
    public static class DecorLayout
    {
        public static readonly string[] Zones = { "path", "front", "back", "shore", "sky" };

        /// <summary>자리의 화면 좌표(땅에 닿는 점), 논리 폭 W 기준.</summary>
        public static (int x, int y) Pos(DecorSlot sl, int stage, int W)
        {
            int n = Terrain.N(stage);
            if (sl.Xy)
            {
                double R = 8 * n; double dx, dy;
                if (sl.Id == "SK1") { dx = -JsMath.Round(R * 0.62); dy = -46; }
                else if (sl.Id == "SK2") { dx = JsMath.Round(R * 0.70); dy = -64; }
                else throw new NotSupportedException(sl.Id);
                return ((int)JsMath.Round(W / 2.0 + dx), (int)(Terrain.CY + Terrain.IsleY + dy));
            }
            var ab = sl.AB(n); var (u, v) = Terrain.Blk2Uv(ab[0], ab[1]);
            return ((int)JsMath.Round(W / 2.0 + u), (int)JsMath.Round(Terrain.CY + Terrain.IsleY + v * Terrain.KY - Terrain.TerrH(stage, u, v)));
        }

        public static double V(DecorSlot sl, int stage) { if (sl.Xy) return 0; var ab = sl.AB(Terrain.N(stage)); return (ab[0] + ab[1]) * 4; }

        /// <summary>decorDevFill: 구역 순서대로, 자리 번호 순서로, 허용 구역·크기에 맞는 장식을 돌려 가며 채운다(삽입 순서 보존).</summary>
        public static List<(string slot, string item)> DevFill()
        {
            var put = new List<(string, string)>();
            foreach (var z in Zones)
            {
                var slots = DecorBaker.Slots.Where(s => s.Chip == z && !s.Blocked).OrderBy(s => s.Num).ToList();
                for (int i = 0; i < slots.Count; i++)
                {
                    var sl = slots[i];
                    var fit = DecorBaker.Items.Where(k => k.Zones.Contains(z) && DecorBaker.SizeRank(k.Size) <= DecorBaker.SizeRank(sl.Size)).ToList();
                    if (fit.Count > 0) put.Add((sl.Id, fit[i % fit.Count].Id));
                }
            }
            return put;
        }
    }

    /// <summary>그릴 것 한 개 (장식 d · 풍경 조각 p · 우체통 m).</summary>
    public sealed class SceneEntry
    {
        public char K; public double V; public bool Flat, Sky; public string ItemId; public int X, Y; public int PieceIndex = -1;
    }

    /// <summary>패스 A(길 바닥 + 탑 뒤) / B(탑 앞) / S(하늘). 같은 패스 안에서는 v=(a+b)*4 로 정렬. 안정 정렬 (부록 B-3).</summary>
    public sealed class SceneLists
    {
        public List<SceneEntry> A = new List<SceneEntry>(), B = new List<SceneEntry>(), S = new List<SceneEntry>();

        public static SceneLists Build(int stage, int gardenLvForSlots, List<(string slot, string item)> put, IList<double> pieceV, int W)
        {
            // decorRenderList
            var list = new List<SceneEntry>();
            foreach (var (slotId, itemId) in put)
            {
                var sl = DecorBaker.Slot(slotId); var im = DecorBaker.Item(itemId);
                if (!sl.OpenAt(stage, gardenLvForSlots) || !im.Zones.Contains(sl.Chip) || DecorBaker.SizeRank(im.Size) > DecorBaker.SizeRank(sl.Size)) continue;
                var (x, y) = DecorLayout.Pos(sl, stage, W);
                list.Add(new SceneEntry { K = 'd', ItemId = itemId, X = x, Y = y, V = DecorLayout.V(sl, stage), Flat = sl.Chip == "path", Sky = sl.Xy });
            }
            list = list.OrderBy(e => e.V).ThenBy(e => e.X).ToList();
            // sceneLists
            var L = new SceneLists();
            Action<SceneEntry> add = e => (e.Sky ? L.S : (e.Flat || e.V < -2) ? L.A : L.B).Add(e);
            foreach (var e in list) add(e);
            for (int i = 0; i < pieceV.Count; i++) add(new SceneEntry { K = 'p', V = pieceV[i], PieceIndex = i });
            add(new SceneEntry { K = 'm', V = 20 });                       // 우체통 (3.5, 1.5)
            Func<char, int> rk = k => k == 'p' ? 0 : k == 'm' ? 1 : 2;
            Func<SceneEntry, double> key = e => (e.Flat ? -1e6 : 0) + e.V;
            Func<List<SceneEntry>, List<SceneEntry>> sort = l => l.OrderBy(key).ThenBy(e => rk(e.K)).ThenBy(e => e.X).ToList();
            L.A = sort(L.A); L.B = sort(L.B); L.S = sort(L.S);
            return L;
        }
    }
}
