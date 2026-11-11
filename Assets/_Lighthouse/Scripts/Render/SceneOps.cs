using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>그리기 명령 한 개: 낮 그림 D, 밤 그림 N 과 dusk(0 낮 ~ 1 밤)에 따른 투명도 규칙. 좌표는 번역(OY)을 더한 캔버스 좌표.</summary>
    public sealed class DrawOp
    {
        public Bitmap D, N; public double X, Y;
        public Func<double, double> AlphaD, AlphaN;       // dusk → 투명도(0 이하면 그리지 않는다)
        public string Tag;
    }

    /// <summary>
    /// 장면의 그리기 순서를 한 곳에 둔다. CPU 합성(골든 대조)과 Unity 스프라이트 렌더(GPU)가 같은 목록을 쓴다.
    /// </summary>
    public static class SceneComposer
    {
        /// <summary>HTML putDN: 낮은 dusk &lt; 0.999 일 때, 밤은 dusk &gt; 0.001 일 때 alpha=dusk 로.</summary>
        static DrawOp DN(Bitmap d, Bitmap n, double x, double y, string tag) => new DrawOp
        {
            D = d, N = n, X = x, Y = y, Tag = tag,
            AlphaD = k => k < 0.999 ? 1 : 0,
            AlphaN = k => k > 0.001 ? k : 0
        };

        /// <summary>
        /// 정적 층의 그리기 순서(HTML frame 5222~5240행): 배경(낮·밤) → 물가 → 섬 → 패스 A → 탑 뒤 → 램프 → 탑 앞 → 패스 B → 패스 S.
        /// 하늘·바다 움직임·배·관리인·날씨는 뺀다. t = 물거품이 깜빡이는 시각(골든은 0).
        /// </summary>
        public static List<DrawOp> Ops(SceneSprites s, double t = 0)
        {
            var ops = new List<DrawOp>(); double oy = s.Oy, cx = s.W / 2.0, cyIsle = Terrain.CY + Terrain.IsleY + oy, cyTower = Terrain.CY + oy;
            ops.Add(DN(s.BgD, s.BgN, 0, 0, "bg"));
            ops.Add(new DrawOp { D = s.Shore, N = s.Shore, X = cx, Y = cyIsle, Tag = "shore", AlphaD = k => k >= 0.5 ? 0 : 1, AlphaN = k => k >= 0.5 ? 0.7 : 0 });
            var foamD = Foam(s, cx, cyIsle, t, false); var foamN = Foam(s, cx, cyIsle, t, true);
            ops.Add(new DrawOp { D = foamD, N = foamN, X = 0, Y = 0, Tag = "foam", AlphaD = k => k >= 0.5 ? 0 : 1, AlphaN = k => k >= 0.5 ? 1 : 0 });
            ops.Add(DN(s.Island.Island, s.IslandN, cx, cyIsle, "island"));
            Action<List<SceneEntry>> pass = list =>
            {
                foreach (var e in list)
                {
                    if (e.K == 'p') ops.Add(DN(s.Island.Pieces[e.PieceIndex].Spr, s.PieceN[e.PieceIndex], cx, cyIsle, "piece" + e.PieceIndex));
                    else if (e.K == 'm') { var mp = s.MailboxPos(); ops.Add(DN(s.MailDn, s.MailDnN, mp.x, mp.y + oy, "mail")); }
                    else ops.Add(DN(s.Decor[e.ItemId], s.DecorN[e.ItemId], e.X, e.Y + oy, "decor:" + e.ItemId));
                }
            };
            pass(s.Lists.A);
            ops.Add(DN(s.TowerA, s.TowerAN, cx, cyTower, "towerA"));
            ops.Add(new DrawOp                                                                                // 램프: 낮 램프는 늘 그리고, 밤 램프는 dusk 와 깜빡임(fl)만큼 얹는다
            {
                D = s.LampD, N = s.LampN, X = cx, Y = cyTower, Tag = "lamp",
                AlphaD = k => 1,
                AlphaN = k => { if (!(k > 0.01)) return 0; double fl = k >= 0.5 ? 0.92 + 0.08 * Math.Sin(t * 11) * Math.Sin(t * 3.7) : 0.75; return Math.Min(1, k * 1.2) * Math.Min(1, fl); }
            });
            ops.Add(DN(s.TowerB, s.TowerBN, cx, cyTower, "towerB"));
            pass(s.Lists.B); pass(s.Lists.S);
            return ops;
        }

        static readonly Col FoamA = Col.Hex("#eaf6fc"), FoamB = Col.Hex("#b4dcf0");

        /// <summary>섬 둘레 블록 물거품 (drawShore 1270~1281행): 가장자리를 따라 깜빡이는 점들을 투명 비트맵 한 장(좌표는 캔버스 그대로)으로.</summary>
        static Bitmap Foam(SceneSprites s, double cx, double cy, double t, bool night)
        {
            var px = new List<(int x, int y, uint c)>(); int N = Terrain.N(s.Stage), i = 0;
            Action<double, double, double, double, Col, double> rect = (x, y, w, h, c, ga) =>
            {
                int x0 = (int)JsMath.Round(x), y0 = (int)JsMath.Round(y), w0 = (int)Math.Max(1, JsMath.Round(w)), h0 = (int)Math.Max(1, JsMath.Round(h)), g8 = (int)JsMath.Round(ga * 255);
                for (int j = y0; j < y0 + h0; j++) for (int q = x0; q < x0 + w0; q++) px.Add((q, j, Bitmap.Pack((int)c.R, (int)c.G, (int)c.B, g8)));
            };
            Action<double, double> edge = (a, b) =>
            {
                double ph = Math.Sin(t * 1.6 + i * 1.7), x = cx + (a - b) * Terrain.VS, y = cy + (a + b) * Terrain.VS / 2; i++;
                if (ph < -0.1) return;
                double ga = (0.45 + 0.4 * ph) * (night ? 0.7 : 0.95);
                rect(JsMath.Round(x / 2) * 2 - 1, JsMath.Round(y), 2, 1, FoamA, ga);
                if (ph > 0.5) rect(JsMath.Round(x / 2) * 2 + 1, JsMath.Round(y) + 1, 1, 1, FoamB, ga);
            };
            for (double sv = -N + 0.5; sv <= N - 0.4; sv += 1) { edge(N + 0.15, sv); edge(sv, N + 0.15); edge(-N - 0.15, sv); edge(sv, -N - 0.15); }
            if (px.Count == 0) return new Bitmap(0, 0, 1, 1);
            int ax = px.Min(p => p.x), ay = px.Min(p => p.y), bx = px.Max(p => p.x), by = px.Max(p => p.y);
            var B = new Bitmap(ax, ay, bx - ax + 1, by - ay + 1);
            foreach (var p in px) B.D[(p.y - ay) * B.W + (p.x - ax)] = p.c;                  // 겹치면 뒤의 것이 덮는다(실제로는 겹치지 않는다)
            return B;
        }

        /// <summary>고정된 낮(dusk=0)·밤(dusk=1) 한 장 CPU 합성. 골든 대조와 미리보기용.</summary>
        public static CpuCanvas Compose(SceneSprites s, double dusk, double t = 0)
        {
            var c = new CpuCanvas(s.W, s.H);
            foreach (var op in Ops(s, t))
            {
                double a = op.AlphaD(dusk);
                if (a > 0) { if (op.Tag == "bg") c.Copy(op.D); else c.Put(op.D, op.X, op.Y, a); }
                a = op.AlphaN(dusk);
                if (a > 0) { if (op.Tag == "bg" && a >= 0.999) c.Copy(op.N); else c.Put(op.N, op.X, op.Y, a); }
            }
            return c;
        }
    }
}
