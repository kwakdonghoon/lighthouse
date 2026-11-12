using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// 불투명 캔버스 위에 스프라이트·사각형을 알파 합성하는 CPU 합성기 (HTML 의 ctx.drawImage / fillRect 를 premultiplied 8비트 규칙으로 흉내).
    /// 장면 골든 대조와 한 장짜리 미리보기용. 게임 화면은 Unity 스프라이트 렌더(GPU)가 같은 순서로 그린다.
    /// </summary>
    public sealed class CpuCanvas
    {
        public readonly int W, H; public readonly uint[] D;
        public CpuCanvas(int w, int h) { W = w; H = h; D = new uint[w * h]; }
        /// <summary>가산층으로 쓸 때: Fx.AddRect/DrawGlow 가 쓴 줄 범위(Overlay.Merge 가 그 줄만 합친다). 비어 있으면 MinY &gt; MaxY.</summary>
        public int MinY = int.MaxValue, MaxY = -1;
        public void Touch(int y0, int y1) { if (y0 < MinY) MinY = y0; if (y1 > MaxY) MaxY = y1; }
        /// <summary>전부 0 으로(가산층 비우기) + 줄 범위 초기화.</summary>
        public void ClearAll() { Array.Clear(D, 0, D.Length); MinY = int.MaxValue; MaxY = -1; }

        public void Copy(Bitmap bg) { Array.Copy(bg.D, D, D.Length); }
        public void CopyFrom(CpuCanvas o) { Array.Copy(o.D, D, D.Length); }

        static int Ga8(double ga) => (int)JsMath.Round(ga * 255);

        // src(straight c, a) 를 globalAlpha 로 줄여 dst(불투명) 위에: out = srcPremul + dst*(1-srcAlpha)
        static int Mix(int dst, int c, int a, int ga8)
        {
            int sa = (int)JsMath.Round(a * ga8 / 255.0);
            int sp = (int)JsMath.Round(JsMath.Round(c * a / 255.0) * ga8 / 255.0);
            return (int)JsMath.Round(sp + dst * (255 - sa) / 255.0);
        }

        /// <summary>drawImage(s.c, round(x+ax), round(y+ay)) — 좌표는 이미 번역(translate)을 더한 값.</summary>
        public void Put(Bitmap s, double x, double y, double globalAlpha = 1)
        {
            int ox = (int)JsMath.Round(x + s.Ax), oy = (int)JsMath.Round(y + s.Ay), g8 = Ga8(globalAlpha);
            for (int j = 0; j < s.H; j++)
            {
                int dy = oy + j; if (dy < 0 || dy >= H) continue;
                for (int i = 0; i < s.W; i++)
                {
                    int dx = ox + i; if (dx < 0 || dx >= W) continue;
                    uint p = s.D[j * s.W + i]; int a = (int)(p >> 24); if (a == 0) continue;
                    uint d = D[dy * W + dx];
                    D[dy * W + dx] = Bitmap.Pack(Mix((int)(d & 255), (int)(p & 255), a, g8), Mix((int)((d >> 8) & 255), (int)((p >> 8) & 255), a, g8), Mix((int)((d >> 16) & 255), (int)((p >> 16) & 255), a, g8), 255);
                }
            }
        }

        /// <summary>Q(x, y, w, h, color): 도트 칸에 맞춘 fillRect (Math.round), globalAlpha 적용.</summary>
        public void Q(double x, double y, double w, double h, Col c, double globalAlpha = 1)
        {
            int x0 = (int)JsMath.Round(x), y0 = (int)JsMath.Round(y), w0 = (int)Math.Max(1, JsMath.Round(w)), h0 = (int)Math.Max(1, JsMath.Round(h)), g8 = Ga8(globalAlpha);
            for (int j = y0; j < y0 + h0; j++)
            {
                if (j < 0 || j >= H) continue;
                for (int i = x0; i < x0 + w0; i++)
                {
                    if (i < 0 || i >= W) continue;
                    uint d = D[j * W + i];
                    D[j * W + i] = Bitmap.Pack(Mix((int)(d & 255), (int)c.R, 255, g8), Mix((int)((d >> 8) & 255), (int)c.G, 255, g8), Mix((int)((d >> 16) & 255), (int)c.B, 255, g8), 255);
                }
            }
        }

        public Bitmap ToBitmap() { var b = new Bitmap(0, 0, W, H); Array.Copy(D, b.D, D.Length); return b; }
    }

    /// <summary>장면에 올라가는 구운 그림 한 벌 (낮 + 밤 사본). 단계·계절·외형이 바뀔 때 새로 만든다 (HTML rebuild 2206행).</summary>
    public sealed class SceneSprites
    {
        public int Stage, W, H, Oy; public string Season;
        public IslandResult Island; public Bitmap IslandN;
        public List<Bitmap> PieceN = new List<Bitmap>();
        public Bitmap TowerA, TowerAN, TowerB, TowerBN, LampD, LampN, MailDn, MailDnN, Shore, BgD, BgN;
        public Dictionary<string, Bitmap> Decor = new Dictionary<string, Bitmap>(), DecorN = new Dictionary<string, Bitmap>();
        public SceneLists Lists;
        public List<(string slot, string item)> Put;

        /// <summary>한 장면에 필요한 것을 모두 굽는다. put == null 이면 꾸미기 없음. gardenLv: 섬 풍경(나무·꽃)용, slotLv: 꾸미기 자리 열림용.</summary>
        public static SceneSprites Build(int w, int h, string season, int stage, string isleSkin = "classic", string decorSkin = "classic", string towerSkin = "classic",
                                         int gardenLv = 0, int slotLv = 0, List<(string slot, string item)> put = null, int t1v = 2)
        {
            var s = new SceneSprites { Stage = stage, W = w, H = h, Oy = h - BgBaker.DesignH, Season = season, Put = put ?? new List<(string, string)>() };
            s.Island = new IslandBaker(Palettes.IslandEnvFor(season, stage, isleSkin, decorSkin, gardenLv)).Bake();
            s.IslandN = Night.Of(s.Island.Island);
            foreach (var p in s.Island.Pieces) s.PieceN.Add(Night.Of(p.Spr));
            var sk = Palettes.TowerSkinOf(towerSkin); var P = TowerParams.ForStage(stage, t1v, sk);
            s.TowerA = TowerBaker.BakeA(P, sk); s.TowerB = TowerBaker.BakeB(P);
            s.TowerAN = Night.Of(s.TowerA); s.TowerBN = Night.Of(s.TowerB);
            s.LampD = TowerBaker.BakeLamp(P, false); s.LampN = TowerBaker.BakeLamp(P, true);
            s.MailDn = PropBaker.Mailbox(false); s.MailDnN = Night.Of(s.MailDn);
            s.Shore = PropBaker.Shore(Terrain.N(stage));
            s.BgD = BgBaker.MakeBg(false, w, h, s.Oy); s.BgN = BgBaker.MakeBg(true, w, h, s.Oy);
            s.Lists = SceneLists.Build(stage, slotLv, s.Put, s.Island.Pieces.Select(p => p.V).ToList(), w);
            foreach (var e in s.Lists.A.Concat(s.Lists.B).Concat(s.Lists.S))
                if (e.K == 'd' && !s.Decor.ContainsKey(e.ItemId))
                {
                    var it = DecorBaker.Item(e.ItemId); var b = DecorBaker.Bake(it, season, decorSkin);
                    s.Decor[e.ItemId] = b; s.DecorN[e.ItemId] = Night.Of(b);
                }
            return s;
        }

        public (int x, int y) MailboxPos()
        {
            var (u, v) = Terrain.Blk2Uv(3.5, 1.5);
            return ((int)JsMath.Round(W / 2.0 + u), (int)JsMath.Round(Terrain.CY + Terrain.IsleY + v * Terrain.KY - Terrain.TerrH(Stage, u, v)));
        }
    }
}
