using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// 골든 대조 밖의 장면 소품: 구름(drawSkyDeco 763~771행, t=0 자리)과 배 몇 척(항로 3개). 보여 주기 위한 미리보기용이고
    /// HTML 프레임과 픽셀 대조하지 않는다(구름 0.5 배율은 최근접 표본 추출로 흉내, 배는 임의 자리).
    /// </summary>
    public static class SceneExtras
    {
        static readonly (double x, double dy, double sc)[] Clouds =
        {
            (0.2, 0.12, 1.1), (0.62, 0.3, 0.8), (0.9, 0.5, 0.65), (0.38, 0.7, 0.5), (0.05, 0.42, 0.7), (0.75, 0.86, 0.4)
        };
        public static readonly double[] LaneY = { 176, 282, 308 };      // SHIP_LANES

        static Bitmap Half(Bitmap b)                                     // 0.5 배율: 짝수 열·행을 뽑는다 (최근접, 골든 실측)
        {
            var o = new Bitmap(b.Ax / 2, b.Ay / 2, b.W / 2, b.H / 2);
            for (int y = 0; y < o.H; y++) for (int x = 0; x < o.W; x++) o.D[y * o.W + x] = b.D[(2 * y) * b.W + 2 * x];
            return o;
        }

        public static List<DrawOp> CloudOps(int W, int oy)
        {
            var ops = new List<DrawOp>();
            Bitmap cd = BgBaker.Cloud(false), cn = BgBaker.Cloud(true);
            Bitmap cdh = Half(cd), cnh = Half(cn);
            double sh = BgBaker.Horizon + oy, top = -oy;
            foreach (var c in Clouds)
            {
                double loop = W + 120, x = (c.x * loop) % loop - 60;
                bool big = c.sc >= 0.75;
                double y = JsMath.Round(top + 14 + c.dy * (sh - 50)) + oy;
                ops.Add(new DrawOp { D = big ? cd : cdh, N = big ? cn : cnh, X = JsMath.Round(x), Y = y, Tag = "cloud", AlphaD = k => (1 - k) * 0.95, AlphaN = k => k * 0.9 });
            }
            return ops;
        }

        /// <summary>(종, 항로, x) 배치 → 그리기 명령. 항로 0(먼 곳)은 섬 뒤, 1·2 는 섬 앞.</summary>
        public static List<DrawOp> ShipOps(int oy, IEnumerable<(string type, int lane, double x)> ships, string skin = "classic")
        {
            var ops = new List<DrawOp>();
            foreach (var (type, lane, x) in ships)
            {
                var d = ShipBaker.Sprite(type, lane, skin, false);
                ops.Add(new DrawOp { D = d, N = Night.Of(d), X = x, Y = LaneY[lane] + oy, Tag = "ship:" + type, AlphaD = k => k < 0.999 ? 1 : 0, AlphaN = k => k > 0.001 ? k : 0 });
            }
            return ops;
        }

        public static readonly (string, int, double)[] ShowShips =
        {
            ("cruise", 0, 168), ("schooner", 0, 44),
            ("trade", 1, 34), ("fish", 1, 112), ("steamer", 1, 180),
            ("galleon", 2, 50), ("ferry", 2, 128), ("rowboat", 2, 188),
        };

        /// <summary>정적 층 + 구름 + 배. 구름은 배경 바로 뒤, 먼 항로 배는 물가 앞(섬 뒤), 가까운 항로 배는 맨 위.</summary>
        public static List<DrawOp> OpsWithExtras(SceneSprites s, double t = 0)
        {
            var baseOps = SceneComposer.Ops(s, t);
            var ships = ShipOps(s.Oy, ShowShips);
            var res = new List<DrawOp> { baseOps[0] };
            res.AddRange(CloudOps(s.W, s.Oy));
            res.AddRange(ships.Where(o => Math.Abs(o.Y - s.Oy - LaneY[0]) < 0.1));
            res.AddRange(baseOps.Skip(1));
            res.AddRange(ships.Where(o => Math.Abs(o.Y - s.Oy - LaneY[0]) >= 0.1));
            return res;
        }
    }
}
