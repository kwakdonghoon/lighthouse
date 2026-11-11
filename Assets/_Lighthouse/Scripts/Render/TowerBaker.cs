using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    public struct WinSrc
    {
        public char Face; public double Off, Zc;
        public WinSrc(char face, double off, double zc) { Face = face; Off = off; Zc = zc; }
    }

    /// <summary>등대 한 단계의 모양 (HTML TOWER/TOWER1 표와 towerDims 1161행).</summary>
    public sealed class TowerParams
    {
        public int N;
        public double[][] Stripes = new double[0][];
        public string Roof, Trim;
        public string[] Flag;
        public WinSrc[] WinSrc;
        public double Skirt;
        public bool Stone, Plaster, Spire, Wide;
        // towerDims 가 채우는 값
        public double Z0, Zt;
        public (double dx, double dz, char face, double off, double hw, double z)[] Win;

        public TowerParams Dims()
        {
            Z0 = Skirt != 0 ? Skirt : 0.5; Zt = Z0 + N;
            Win = WinSrc.Select(w =>
            {
                double hw = ShaftHW(w.Zc), z = Z0 + w.Zc, off = w.Off;
                return w.Face == 'L'
                    ? ((off - hw) * Raster.VS, z * Raster.VZ - (off + hw) * Raster.VS / 2, 'L', off, hw, z)
                    : ((hw - off) * Raster.VS, z * Raster.VZ - (hw + off) * Raster.VS / 2, 'R', off, hw, z);
            }).ToArray();
            return this;
        }

        /// <summary>shaftHW: 위로 갈수록 가늘어진다. 1 - 0.125 * floor(3*i/n)</summary>
        public double ShaftHW(double lv)
        {
            int i = (int)Math.Max(0, Math.Min(N - 1, Math.Floor(lv)));
            return 1 - 0.125 * Math.Floor(3.0 * i / N);
        }

        static readonly WinSrc[] W1 = { new WinSrc('L', 0.3, 4.2) };

        static double[][] St(params double[][] a) => a;

        /// <summary>stage 2~6 (TOWER[2..6]). 1단계는 Tower1(variant).</summary>
        public static TowerParams ForStage(int stage, int t1v = 2, TowerSkin skin = null)
        {
            var b = ForStageBase(stage, t1v);
            return skin == null ? b : b.WithSkin(skin);
        }

        /// <summary>HTML skinTower(P) (1884행): 외형의 형태·지붕·금빛 띠 색을 덮어쓴 사본. 치수(Dims)는 그대로.</summary>
        public TowerParams WithSkin(TowerSkin o)
        {
            var q = (TowerParams)MemberwiseClone();
            switch (o.Form)
            {
                case "stone": q.Stone = true; q.Plaster = false; break;
                case "plaster": q.Plaster = true; q.Stone = false; break;
                case "bands":
                    q.Stone = false; q.Plaster = false;
                    q.Stripes = Enumerable.Range(0, N).Where(i => i % 2 == 0).Select(i => new[] { (double)i, i + 1 }).ToArray(); break;
                case "spire": q.Spire = true; break;
                case "wide": q.Wide = true; break;
            }
            q.Roof = !string.IsNullOrEmpty(o.Roof) ? o.Roof : Roof;
            q.Trim = Trim != null ? (!string.IsNullOrEmpty(o.Trim) ? o.Trim : Trim) : null;
            return q;
        }

        static TowerParams ForStageBase(int stage, int t1v)
        {
            if (stage == 1) return Tower1(t1v);
            switch (stage)
            {
                case 2: return new TowerParams { N = 8, Stripes = St(new[] { 2.0, 4 }, new[] { 5.0, 7 }), Roof = "#c9403a", WinSrc = new[] { new WinSrc('L', 0.3, 4.2), new WinSrc('R', -0.3, 6.2) }, Skirt = 0 }.Dims();
                case 3: return new TowerParams { N = 9, Stripes = St(new[] { 3.0, 5 }, new[] { 6.0, 8 }), Roof = "#c9403a", WinSrc = new[] { new WinSrc('L', 0.3, 3.2), new WinSrc('R', -0.3, 6.2) }, Skirt = 0 }.Dims();
                case 4: return new TowerParams { N = 10, Stripes = St(new[] { 1.0, 3 }, new[] { 4.0, 6 }, new[] { 7.0, 9 }), Roof = "#c9403a", Trim = "#d9a93a", WinSrc = new[] { new WinSrc('L', 0.3, 4.2), new WinSrc('R', -0.3, 6.2), new WinSrc('L', 0.3, 8.2) }, Skirt = 1 }.Dims();
                case 5: return new TowerParams { N = 12, Stripes = St(new[] { 2.0, 4 }, new[] { 5.0, 7 }, new[] { 8.0, 10 }), Roof = "#c9403a", Trim = "#d9a93a", Flag = new[] { "#e0453e", "#c9403a", "#a3322c" }, WinSrc = new[] { new WinSrc('L', 0.3, 4.2), new WinSrc('R', -0.3, 7.2), new WinSrc('L', 0.3, 10.2) }, Skirt = 1 }.Dims();
                case 6: return new TowerParams { N = 14, Stripes = St(new[] { 2.0, 4 }, new[] { 5.0, 7 }, new[] { 8.0, 10 }, new[] { 11.0, 13 }), Roof = "#e0b040", Trim = "#f0c850", Flag = new[] { "#f6d25a", "#e0b040", "#b88a24", "#e0b040" }, WinSrc = new[] { new WinSrc('L', 0.3, 4.2), new WinSrc('R', -0.3, 8.2), new WinSrc('L', 0.3, 12.2) }, Skirt = 2 }.Dims();
            }
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        /// <summary>TOWER1: 0 돌탑, 1 크림, 2 줄무늬 (기본 T1V=2).</summary>
        public static TowerParams Tower1(int v)
        {
            var p = new TowerParams { N = 7, WinSrc = W1, Skirt = 0 };
            if (v == 0) { p.Stone = true; p.Roof = "#5a3a28"; }
            else if (v == 1) { p.Plaster = true; p.Roof = "#b8472f"; }
            else { p.Stripes = St(new[] { 2.0, 4 }, new[] { 5.0, 6.5 }); p.Roof = "#c9403a"; }
            return p.Dims();
        }
    }

    /// <summary>HTML bakeTowerA/B (1173~1218행). 기본 외형(classic) 팔레트.</summary>
    public static class TowerBaker
    {
        const string StripeC0 = "#c9403a", WallC0 = "#e8e6dc", StoneC = "#9aa0b8", DoorC0 = "#7a4b2a";   // TOWER_PAL (skirt 색은 쓰이지 않음)

        static string Or(string v, string d) => string.IsNullOrEmpty(v) ? d : v;

        static string LevelColor(TowerParams P, TowerSkin sk, int i)
        {
            if (P.Stone) return StoneC;
            if (P.Plaster) return i < 1 ? "#cdb98c" : "#f0e4c8";
            foreach (var s in P.Stripes) if (i >= s[0] && i < s[1]) return Or(sk?.Stripe, StripeC0);
            return Or(sk?.Wall, WallC0);
        }

        /// <summary>탑 뒤쪽(A): 몸통·문·창·회랑·뒤 난간</summary>
        public static Bitmap BakeA(TowerParams P, TowerSkin sk = null)
        {
            var S = new VNew(); double z0 = P.Z0;
            S.Group(0, 0);
            S.Floor(-2.2, -2.2, 2.2, 2.2, 0, "#10180e", 0.3);
            if (P.Skirt != 0)                                          // 큰 등대: 돌로 쌓은 밑동
            {
                S.Box(-2, -2, 0, 4, 4, 1, IslandBaker.StoneMat(0.92));
                if (P.Skirt > 1) S.Box(-1.5, -1.5, 1, 3, 3, 1, IslandBaker.StoneMat(1.02));
            }
            else S.Box(-1.25, -1.25, 0, 2.5, 2.5, 0.5, IslandBaker.StoneMat(0.8));
            for (int i = 0; i < P.N; i++)                              // 몸통: 한 층씩 쌓고 위로 갈수록 가늘어진다
            {
                double hw = P.ShaftHW(i);
                string bc = (P.Trim != null && i == P.N - 1) ? P.Trim : LevelColor(P, sk, i);
                S.Box(-hw, -hw, z0 + i, hw * 2, hw * 2, 1, Vmat.Make(bc, new VmatOpts { G = P.Stone ? 0.05 : 0.07, Tx = P.Stone ? Tex.Stone : Tex.None }));
            }
            double hw0 = P.ShaftHW(0), kd = S.KeyOf(0, 0, z0 + P.N - 0.6, 0, 0, 0);
            S.RectL(-0.78, -0.22, hw0, z0, z0 + 1.0, "#2a1a10", kd);                                                  // 문
            S.RectL(-0.7, -0.3, hw0, z0, z0 + 0.92, Or(sk?.Door, DoorC0), kd + 0.01);
            S.Dot(-0.36, hw0, z0 + 0.5, "#ffd36b", kd + 0.02, 1);
            foreach (var w in P.Win)                                                                                  // 창
            {
                if (w.face == 'L') { S.RectL(w.off - 0.3, w.off + 0.3, w.hw, w.z - 0.5, w.z + 0.5, "#2a1a10", kd + 0.03); S.RectL(w.off - 0.2, w.off + 0.2, w.hw, w.z - 0.4, w.z + 0.4, "#6e9cc4", kd + 0.04); }
                else { S.RectR(w.off - 0.3, w.off + 0.3, w.hw, w.z - 0.5, w.z + 0.5, "#2a1a10", kd + 0.03); S.RectR(w.off - 0.2, w.off + 0.2, w.hw, w.z - 0.4, w.z + 0.4, "#6e9cc4", kd + 0.04); }
            }
            double zt = P.Zt;
            var gm = Vmat.Make(P.Trim != null ? Col.HexOf(Col.Mul(Col.Hex(P.Trim), 0.8)) : "#4c5472", new VmatOpts { T = P.Trim != null ? Col.HexOf(Col.Mul(Col.Hex(P.Trim), 1.05)) : "#6a74a0", G = 0.08 });
            S.Box(-1.5, -1.5, zt, 3, 3, 0.5, gm);                                                                      // 회랑
            var rm = Vmat.Make("#2a2e46", new VmatOpts { Bv = 0, G = 0 });
            var rl = Vmat.Make("#8a92b8", new VmatOpts { Bv = 0, G = 0, L = "#6a729a", R = "#4a5278" });
            S.Box(-1.5, -1.5, zt + 0.5, 0.2, 0.2, 0.7, rm); S.Box(1.3, -1.5, zt + 0.5, 0.2, 0.2, 0.7, rm); S.Box(-1.5, 1.3, zt + 0.5, 0.2, 0.2, 0.7, rm);   // 모서리 기둥
            S.Box(-1.5, -1.5, zt + 1.1, 3, 0.15, 0.15, rl); S.Box(-1.5, -1.5, zt + 1.1, 0.15, 3, 0.15, rl);            // 뒤쪽 난간
            return S.Bake(2);
        }

        /// <summary>탑 앞쪽(B): 앞 기둥·앞 난간·처마·지붕·꼭대기·깃발</summary>
        public static Bitmap BakeB(TowerParams P)
        {
            var S = new VNew(); double zt = P.Zt, zl = zt + 0.5 + 1.5; string rc = P.Roof;
            Func<double, string> rcm = k => Col.HexOf(Col.Mul(Col.Hex(rc), k));
            S.Group(0, 0);
            var rm = Vmat.Make(rc, new VmatOpts { T = rcm(1.15), L = rc, R = rcm(0.66), G = 0.12, Bv = 1 });
            var rl = Vmat.Make("#8a92b8", new VmatOpts { Bv = 0, G = 0, L = "#6a729a", R = "#4a5278" });
            var pm = Vmat.Make("#2a2e46", new VmatOpts { Bv = 0, G = 0 });
            S.Box(1.3, 1.3, zt + 0.5, 0.2, 0.2, 0.7, pm);                                                              // 앞 모서리 기둥
            S.Box(-1.5, 1.35, zt + 1.1, 3, 0.15, 0.15, rl); S.Box(1.35, -1.5, zt + 1.1, 0.15, 3, 0.15, rl);            // 앞쪽 난간
            S.Box(-1.15, -1.15, zl, 2.3, 2.3, 0.5, Vmat.Make(rcm(0.62), new VmatOpts { T = rc, L = rcm(0.62), R = rcm(0.45) }));   // 처마
            S.Box(-0.85, -0.85, zl + 0.5, 1.7, 1.7, 0.5, rm);
            S.Box(-0.5, -0.5, zl + 1.0, 1.0, 1.0, 0.5, rm);
            double tipZ = P.Spire ? zl + 3.0 : zl + 1.5;
            if (P.Spire) { S.Box(-0.32, -0.32, zl + 1.5, 0.64, 0.64, 0.5, rm); S.Box(-0.14, -0.14, zl + 2.0, 0.28, 0.28, 1.0, rm); }
            S.Box(-0.2, -0.2, tipZ, 0.4, 0.4, 0.4, Vmat.Make("#ffd36b", new VmatOpts { T = "#fff0b0", L = "#f2c050", R = "#c8922a", Bv = 0, G = 0 }));
            if (P.Flag != null)
            {
                S.Box(-0.04, -0.04, tipZ + 0.4, 0.08, 0.08, 1.4, pm);
                for (int i = 0; i < P.Flag.Length; i++)
                    S.Box(0.04 + i * 0.22, -0.04, tipZ + 1.5 - Math.Floor(i / 2.0) * 0.12, 0.22, 0.08, 0.34, Vmat.Make(P.Flag[i], new VmatOpts { Bv = 0, G = 0.05 }), 1e9 + i);
            }
            return S.Bake(2);
        }

        /// <summary>HTML bakeLamp (1219행): 램프실 1×1 유리 상자. 낮·밤 두 장.</summary>
        public static Bitmap BakeLamp(TowerParams P, bool night)
        {
            var S = new VNew(); double z = P.Zt + 0.5;
            var glass = night ? Vmat.Make("#ffd36b", new VmatOpts { T = "#fff0b0", L = "#ffd060", R = "#e0a030", G = 0.04, Bv = 0 })
                              : Vmat.Make("#e8dcae", new VmatOpts { T = "#f4ecc8", L = "#d4c690", R = "#a89a68", G = 0.06, Bv = 0 });
            S.Group(0, 0);
            double q = P.Wide ? 0.75 : 0.5;                                     // 넓은 램프 스킨은 램프실 한 변이 1.5 블록
            S.Box(-q, -q, z, q * 2, q * 2, 1.5, glass);
            double k = S.KeyOf(0, 0, z + 1, 0, 0, 0); string bar = night ? "#6a4a18" : "#4a3418";
            S.RectL(-q, -q + 0.1, q, z, z + 1.5, bar, k); S.RectL(q - 0.1, q, q, z, z + 1.5, bar, k);
            S.RectR(-q, -q + 0.1, q, z, z + 1.5, bar, k); S.RectR(q - 0.1, q, q, z, z + 1.5, bar, k);
            S.RectL(-q, q, q, z + 0.7, z + 0.8, bar, k); S.RectR(-q, q, q, z + 0.7, z + 0.8, bar, k);
            if (night) S.RectL(-q + 0.2, q - 0.2, q, z + 0.3, z + 1.2, "#fffbe0", k + 0.1, 0.85);
            return S.Bake(1);
        }
    }
}
