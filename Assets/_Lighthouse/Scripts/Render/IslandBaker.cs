using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// 굽기 입력 색 (HTML 의 기본 외형 'classic'·첫 바다 'home' 기준). 계절 팔레트는 M0 에서 여름만 옮겼다.
    /// 나머지 계절·외형은 M2 에서 데이터(JSON)로 옮긴다.
    /// </summary>
    public sealed class DecorPal
    {
        public string Wall = "#ece5d0", WallS = "#cbc3aa", RoofL = "#d0453e", RoofR = "#a3322c", RoofE = "#7e2624";
        public string[] Flowers = { "#f4a0b8", "#ffe070", "#ffffff", "#b8a0f0", "#ff9a70" };
        public string Fence = "#e8e0c8", FlagA = "#e0453e", FlagB = "#c9403a";
    }

    public sealed class IslandEnv
    {
        public int Stage = 1;
        public string[] Grass = { "#2a6236", "#336f3f", "#3d8049", "#4a9255", "#5ca963" };   // SEASON_GRASS.summer
        public string[] Tree = { "#2d6738", "#3a7d46", "#3a7d46", "#4a9455", "#8be096" };    // TREE_COL.summer
        public bool Winter = false;
        public int GardenLv = 0;                                                             // effLv('garden')
        public DecorPal Decor = new DecorPal();
    }

    public sealed class IslandResult
    {
        public int N;
        public Bitmap Island;
        public List<PieceV> Pieces;                       // v(깊이) 순으로 정렬된 풍경 조각
        public List<double[]> Lights = new List<double[]>();
    }

    public sealed class PieceV
    {
        public double V;
        public Bitmap Spr;
    }

    /// <summary>HTML bakeIsland(1088행) 와 섬 위의 물체들(997~1086, 1134~1141행)의 이식.</summary>
    public sealed class IslandBaker
    {
        static readonly int[] IsleNs = { 0, 6, 6, 7, 8, 8, 9 };
        const double GZ = 2.5;
        static readonly string[] Stone = { "#8f97c4", "#8890bd", "#9aa1cb", "#868eb8" };
        static readonly string[] SandB = { "#dccd8c", "#d2c280", "#e4d698" };
        static readonly string[] PathB = { "#b09a68", "#a89260", "#b8a372" };
        const string Wood = "#9a6a3c", WoodD = "#6a4526", WoodL = "#b88a54";
        static readonly int[] TierOf = { 0, 1, 1, 2, 2, 3, 3 };

        readonly IslandEnv env;
        int N;
        double isleY;                                   // ISLE_Y = GROUND = GZ*VZ
        readonly List<double[]> lights = new List<double[]>();

        public IslandBaker(IslandEnv env) { this.env = env; }

        int Stage => env.Stage;
        int Tier => TierOf[Stage];
        bool HasPier => Tier >= 3;

        // ---- 지형 판정 ----
        bool TileSand(double a, double b)                 // 길 끝의 해변 (테두리가 터진 곳)
        {
            if (b > N - 1 && Math.Abs(a + 0.5) < 1.6) return true;
            if (HasPier && a > N - 1 && Math.Abs(b - 0.5) < 1.6) return true;
            return false;
        }
        bool TilePath(double a, double b)
        {
            if (Math.Abs(a + 0.5) < 0.01 && b > 1 && b < N) return true;                                 // 문에서 해변으로
            if (Tier >= 2 && Math.Abs(b - 3.5) < 0.01 && a < -0.9 && a > -2) return true;                // 집으로
            if (Math.Abs(b - 0.5) < 0.01 && a > 1 && a < (HasPier ? N : 4.1)) return true;               // 우체통·부두로
            return false;
        }
        bool TilePlaza(double a, double b) => Stage >= 6 && Math.Abs(a) < 3 && Math.Abs(b) < 3 && !(Math.Abs(a) < 2 && Math.Abs(b) < 2);

        void LightAt(double a, double b, double c, double r)
        {
            var p = Raster.VProj(a, b, c);
            lights.Add(new[] { p.x, p.y + isleY, r });
        }

        public static Vmat StoneMat(double f = 1, VmatOpts o = null)
        {
            var op = new VmatOpts
            {
                Pal = Stone.Select(s => Col.HexOf(Col.Mul(Col.Hex(s), f))).ToArray(),
                Tx = Tex.Stone
            };
            if (o != null) { op.Wet = o.Wet; /* 이 호출들은 wet 만 덮어쓴다 */ }
            return Vmat.Make(Col.Mul(Col.Hex(Stone[0]), f), op);
        }

        // ---- 섬 위의 것들 (모두 블록으로 쌓는다. z 는 땅 위 높이) ----
        void VTree(VShift S, double a, double b)
        {
            var c = env.Tree;
            var tm = Vmat.Make(WoodD, new VmatOpts { L = "#5a3a20", R = "#3e2814", Tx = Tex.Wood });
            var lm = Vmat.Make(c[1], new VmatOpts { T = c[3], L = c[1], R = c[0], G = 0.05, Bv = 0, Tx = Tex.Leaf });
            var lm2 = Vmat.Make(c[1], new VmatOpts { T = c[3], L = c[1], R = c[0], G = 0.05, Bv = 0, Tx = Tex.Leaf });
            S.Box(a - 0.25, b - 0.25, 0, 0.5, 0.5, 2.25, tm);
            S.Box(a - 1, b - 1, 1.5, 2, 2, 1, lm);
            S.Box(a - 0.75, b - 0.75, 2.5, 1.5, 1.5, 1, lm2);
            S.Box(a - 0.35, b - 0.35, 3.5, 0.7, 0.7, 0.5, lm2);
        }

        /// <summary>타이틀 장면(TitleSprites)이 같은 나무·집 그림을 쓰도록 여는 문. 굽는 내용은 그대로(M0~M2 골든이 지킨다).</summary>
        public void TitleTree(VShift S, double a, double b) => VTree(S, a, b);
        public void TitleHut(VShift S, double a, double b) => VHut(S, a, b);

        static void VRock(VShift S, double a, double b, double s = 1, double z = 0)
        {
            var m = Vmat.Make("#8a90ae", new VmatOpts { G = 0.05, Tx = Tex.Stone });
            var m2 = Vmat.Make("#757b9a", new VmatOpts { G = 0.05, Tx = Tex.Stone });
            S.Box(a - 0.5 * s, b - 0.5 * s, z, s, s, 0.75 * s, m);
            S.Box(a - 0.1 * s, b - 0.35 * s, z + 0.75 * s, 0.55 * s, 0.55 * s, 0.5 * s, m2);
        }

        void VHut(VShift S, double a, double b)             // 관리인의 집: 2×2 흰 벽 + 붉은 지붕
        {
            var DP = env.Decor;
            var wm = Vmat.Make(DP.Wall, new VmatOpts { L = DP.WallS, R = Col.HexOf(Col.Mul(Col.Hex(DP.WallS), 0.8)), G = 0.08 });
            double x = a - 1, y = b - 1, k = S.KeyOf(x, y, 0, 2, 2, 1.5);
            S.Box(x, y, 0, 2, 2, 1.5, wm);
            S.RectL(x + 0.3, x + 0.9, y + 2, 0, 1, "#2a1a10", k + 0.2); S.RectL(x + 0.38, x + 0.82, y + 2, 0.08, 0.92, "#7a4b2a", k + 0.3);          // 문
            S.RectL(x + 1.2, x + 1.75, y + 2, 0.6, 1.2, "#2a1a10", k + 0.2); S.RectL(x + 1.28, x + 1.67, y + 2, 0.68, 1.12, "#6a95b8", k + 0.3);      // 창
            S.RectR(y + 0.5, y + 1.3, x + 2, 0.55, 1.15, "#2a1a10", k + 0.2); S.RectR(y + 0.58, y + 1.22, x + 2, 0.63, 1.07, "#6a95b8", k + 0.3);
            var rm = Vmat.Make(DP.RoofL, new VmatOpts { L = DP.RoofL, R = DP.RoofR, G = 0.12 });
            var rm2 = Vmat.Make(DP.RoofL, new VmatOpts { T = "#e5645c", L = DP.RoofL, R = DP.RoofR, G = 0.12 });
            S.Box(x - 0.25, y - 0.25, 1.5, 2.5, 2.5, 0.5, Vmat.Make(DP.RoofE, new VmatOpts { T = DP.RoofL, L = DP.RoofE, R = Col.HexOf(Col.Mul(Col.Hex(DP.RoofE), 0.7)) }));
            S.Box(x + 0.1, y + 0.1, 2, 1.8, 1.8, 0.5, rm); S.Box(x + 0.45, y + 0.45, 2.5, 1.1, 1.1, 0.5, rm2);
            S.Box(x + 1.35, y + 0.2, 2, 0.4, 0.4, 1, Vmat.Make("#6a4a40"));                                                                           // 굴뚝
            LightAt(x + 0.6, y + 2, GZ + 0.5, 9);
        }

        void VPier(VNew S)                                  // 작은 부두 (오른쪽 아래로 뻗는다)
        {
            for (int k = 0; k < 4; k++)
            {
                S.Box(N + k, 0, 1.5, 1, 1, 0.5, Vmat.Make(k % 2 != 0 ? Wood : WoodL, new VmatOpts { G = 0.04, Tx = Tex.Wood }));
                if (k % 2 == 0)
                {
                    S.Box(N + k + 0.1, 0.05, 0, 0.3, 0.3, 1.5, Vmat.Make(WoodD, new VmatOpts { Bv = 0 }));
                    S.Box(N + k + 0.1, 0.65, 0, 0.3, 0.3, 1.5, Vmat.Make(WoodD, new VmatOpts { Bv = 0 }));
                }
            }
            Obj(S, N + 3.5, 0.7, 0, s =>
            {
                s.Box(N + 3.55, 0.55, 2, 0.25, 0.25, 1.4, Vmat.Make(WoodD));                                                                         // 등불 기둥
                s.Box(N + 3.45, 0.45, 3.4, 0.45, 0.45, 0.45, Vmat.Make("#ffd36b", new VmatOpts { T = "#fff0b0", L = "#f2c050", R = "#c8922a", Bv = 0, G = 0 }));
            });
            LightAt(N + 3.68, 0.68, 3.6, 10);
        }

        void VFlowerbed(VShift S, double a, double b)
        {
            var DP = env.Decor;
            var fm = Vmat.Make(DP.Fence, new VmatOpts { G = 0.05 });
            S.Box(a - 1, b - 0.6, 0, 2, 1.2, 0.25, Vmat.Make("#5a3d28", new VmatOpts { Bv = 0 }));
            double k0 = S.KeyOf(a - 1, b - 0.6, 0, 2, 1.2, 0.25) + 0.2;
            for (int i = 0; i < 9; i++)
            {
                double fa = a - 0.9 + JsMath.Hsh(i, 61) * 1.8, fb = b - 0.5 + JsMath.Hsh(i, 62) * 1;
                string fc = DP.Flowers[i % 5];
                S.Box(fa - 0.06, fb - 0.06, 0.25, 0.12, 0.12, 0.28, Vmat.Make("#4a9a50", new VmatOpts { G = 0, Bv = 0 }), k0 + i * 0.001);
                S.Dot(fa - 0.06, fb - 0.06, 0.62, fc, k0 + 0.1 + i * 0.001, 2);
            }
            S.Box(a - 1.05, b + 0.55, 0, 2.1, 0.1, 0.5, fm, k0 + 0.3);
            S.Box(a + 0.95, b - 0.6, 0, 0.1, 1.2, 0.5, fm, k0 + 0.4);
        }

        static void VBench(VShift S, double a, double b)
        {
            var m = Vmat.Make(WoodL, new VmatOpts { G = 0.04, Tx = Tex.Wood }); var d = Vmat.Make(WoodD, new VmatOpts { Tx = Tex.Wood });
            S.Box(a - 0.7, b - 0.25, 0, 0.15, 0.5, 0.45, d); S.Box(a + 0.55, b - 0.25, 0, 0.15, 0.5, 0.45, d);
            S.Box(a - 0.75, b - 0.28, 0.45, 1.5, 0.56, 0.18, m); S.Box(a - 0.75, b - 0.3, 0.63, 1.5, 0.12, 0.5, m);
        }

        static void VWell(VShift S, double a, double b)
        {
            var sm = Vmat.Make("#9aa0b8", new VmatOpts { G = 0.05, Tx = Tex.Stone });
            S.Box(a - 0.7, b - 0.7, 0, 1.4, 1.4, 0.8, sm); S.Box(a - 0.45, b - 0.45, 0.8, 0.9, 0.9, 0.05, Vmat.Make("#20304e", new VmatOpts { Bv = 0, G = 0 }));
            S.Box(a - 0.7, b - 0.7, 0.8, 0.12, 0.12, 1.2, Vmat.Make(WoodD)); S.Box(a + 0.58, b + 0.58, 0.8, 0.12, 0.12, 1.2, Vmat.Make(WoodD));
            S.Box(a - 0.85, b - 0.85, 2, 1.7, 1.7, 0.3, Vmat.Make("#b8472f", new VmatOpts { T = "#d0453e" }));
        }

        void VFlag(VShift S, double a, double b)
        {
            var DP = env.Decor;
            S.Box(a - 0.1, b - 0.1, 0, 0.2, 0.2, 4.2, Vmat.Make("#e8e0c8"));
            S.Box(a - 0.15, b - 0.15, 4.2, 0.3, 0.3, 0.3, Vmat.Make("#f0c850", new VmatOpts { T = "#fff0a0" }));
            for (int i = 0; i < 4; i++)
                S.Box(a + 0.1 + i * 0.3, b - 0.04, 3.4 - (i > 1 ? 0.1 : 0), 0.3, 0.08, 0.7, Vmat.Make(i < 2 ? DP.FlagA : DP.FlagB, new VmatOpts { Bv = 0, G = 0.05 }), S.KeyOf(a, b, 3.4, 0.2, 0.2, 0.7) + 0.2 + i * 0.01);
        }

        static void VCrates(VShift S, double a, double b)
        {
            S.Box(a - 0.7, b - 0.4, 0, 0.8, 0.8, 0.8, Vmat.Make(Wood, new VmatOpts { G = 0.04, Tx = Tex.Wood }));
            S.Box(a + 0.1, b - 0.35, 0, 0.7, 0.7, 0.7, Vmat.Make("#8a5a30", new VmatOpts { G = 0.04, Tx = Tex.Wood }));
            S.Box(a - 0.5, b - 0.2, 0.8, 0.55, 0.55, 0.55, Vmat.Make(WoodL, new VmatOpts { G = 0.04, Tx = Tex.Wood }));
        }

        void VGild(VShift S, double a, double b)
        {
            S.Box(a - 0.1, b - 0.1, 0, 0.2, 0.2, 1.8, Vmat.Make("#3a3030"));
            S.Box(a - 0.3, b - 0.3, 1.8, 0.6, 0.6, 0.6, Vmat.Make("#f0c850", new VmatOpts { T = "#fff4b8", L = "#e0b040", R = "#a8761e", Bv = 0, G = 0 }));
            S.Box(a - 0.35, b - 0.35, 2.4, 0.7, 0.7, 0.15, Vmat.Make("#8a5e16"));
            LightAt(a, b, GZ + 2.1, 10);
        }

        static void VAnchor(VShift S, double a, double b)
        {
            var m = Vmat.Make("#d6dae8", new VmatOpts { G = 0.1 }); var sm = Vmat.Make("#8c92aa", new VmatOpts { G = 0.15 });
            S.Box(a - 0.8, b - 0.8, 0, 1.6, 1.6, 0.5, sm); S.Box(a - 0.5, b - 0.5, 0.5, 1, 1, 0.3, sm);
            S.Box(a - 0.12, b - 0.12, 0.8, 0.24, 0.24, 2.2, m); S.Box(a - 0.5, b - 0.12, 2.2, 1, 0.24, 0.22, m);
            S.Box(a - 0.2, b - 0.2, 3, 0.4, 0.4, 0.4, m);
            S.Box(a - 0.62, b - 0.12, 1.05, 0.24, 0.24, 0.5, m); S.Box(a + 0.38, b - 0.12, 1.05, 0.24, 0.24, 0.5, m); S.Box(a - 0.38, b - 0.12, 0.8, 0.76, 0.24, 0.24, m);
        }

        static void VSign(VShift S, double a, double b)
        {
            S.Box(a - 0.07, b - 0.07, 0, 0.14, 0.14, 1.3, Vmat.Make(WoodD));
            S.Box(a - 0.5, b - 0.06, 1.0, 1, 0.12, 0.4, Vmat.Make(WoodL, new VmatOpts { G = 0.15 }));
            S.Box(a - 0.4, b - 0.06, 0.55, 0.9, 0.12, 0.35, Vmat.Make("#a8794a", new VmatOpts { G = 0.15 }));
        }

        static void VRowboat(VShift S, double a, double b)
        {
            var hm = Vmat.Make("#8a5a30", new VmatOpts { G = 0.04, Tx = Tex.Wood }); var im = Vmat.Make("#3a2414", new VmatOpts { Bv = 0 });
            S.Box(a - 0.9, b - 0.45, 0, 1.8, 0.9, 0.4, hm); S.Box(a - 0.75, b - 0.3, 0.3, 1.5, 0.6, 0.15, im);
            S.Box(a - 0.95, b - 0.4, 0.4, 0.2, 0.8, 0.2, Vmat.Make(WoodL)); S.Box(a + 0.75, b - 0.4, 0.4, 0.2, 0.8, 0.2, Vmat.Make(WoodL));
            S.Box(a + 0.1, b - 0.06, 0.4, 0.1, 0.1, 1.1, Vmat.Make(WoodL));
        }

        // vobj(S, a, b, dz, f): 한 물체로 묶고 땅 높이만큼 올려서 그린다
        static void Obj(VNew S, double a, double b, double dz, Action<VShift> f)
        {
            S.Group(a, b); f(new VShift(S, dz)); S.GroupEnd();
        }

        // ---- 굽기 ----
        public IslandResult Bake()
        {
            N = IsleNs[Stage] != 0 ? IsleNs[Stage] : 7;
            lights.Clear();
            double ground = GZ * Raster.VZ;
            isleY = ground;
            var S = new VNew();
            var GR = env.Grass;
            var grassM = Vmat.Make(GR[3], new VmatOpts { Pal = new[] { GR[2], GR[3], GR[3], GR[4], GR[3], GR[2] }, L = "#7a5a3c", R = "#5a4028", G = 0, Bv = 0 });
            var grassDk = Vmat.Make(Col.HexOf(Col.Mul(Col.Hex(GR[3]), 0.86)), new VmatOpts { Pal = new[] { GR[1], GR[2], GR[2], GR[3], GR[2], GR[1] }, L = "#7a5a3c", R = "#5a4028", G = 0, Bv = 0 });
            var sandM = Vmat.Make(SandB[0], new VmatOpts { Pal = SandB, L = "#a89868", R = "#8a7a50", G = 0, Bv = 0 });
            var pathM = Vmat.Make(PathB[0], new VmatOpts { Pal = PathB, L = "#7a6844", R = "#5e4e32", G = 0, Bv = 0 });
            var plazaM = Vmat.Make("#aab0c8", new VmatOpts { Pal = new[] { "#aab0c8", "#9aa0b8", "#b4bad0" }, L = "#7e84a0", R = "#5e647e", G = 0, Bv = 1 });
            S.Floor(-N - 1.4, -N - 1.4, N + 1.4, N + 1.4, 0, "#050c20", 0.3);                      // 섬 아래로 드리운 물그림자
            S.Floor(-N - 0.5, -N - 0.5, N + 0.5, N + 0.5, 0, "#0a1a3a", 0.25);
            Func<double, double, bool> ao = (a, b) => (Math.Abs(a) < 2.1 && Math.Abs(b) < 2.1) || (Stage >= 4 && Math.Abs(a) < 3 && Math.Abs(b) < 3);
            const double GK = -1e6;                                                                 // 땅은 모두 물체보다 먼저 칠한다
            Action<double, double, double, double, double, double, Vmat> Gb = (x, y, z, w, d, h, m) => S.Box(x, y, z, w, d, h, m, GK + S.KeyOf(x, y, z, w, d, h));
            for (int i = 0; i < 2 * N; i++)
                for (int j = 0; j < 2 * N; j++)
                {
                    double a = -N + 0.5 + i, b = -N + 0.5 + j;
                    bool rim = Math.Abs(a) > N - 1 || Math.Abs(b) > N - 1, sand = rim && TileSand(a, b);
                    double x = a - 0.5, y = b - 0.5;
                    if (rim && !sand)                                    // 돌 테두리: 3층, 아래층은 어둡고 물에 젖는다
                    {
                        Gb(x, y, 0, 1, 1, 1, StoneMat(0.78, new VmatOpts { Wet = true }));
                        Gb(x, y, 1, 1, 1, 1, StoneMat(0.92));
                        Gb(x, y, 2, 1, 1, 1, StoneMat(1.0));
                    }
                    else if (sand)
                    {
                        Gb(x, y, 0, 1, 1, 1, StoneMat(0.78, new VmatOpts { Wet = true }));
                        Gb(x, y, 1, 1, 1, 0.5, StoneMat(0.92));
                        Gb(x, y, 1.5, 1, 1, 1, sandM);
                    }
                    else Gb(x, y, 0, 1, 1, GZ, TilePlaza(a, b) ? plazaM : TilePath(a, b) ? pathM : ao(a, b) ? grassDk : grassM);
                }
            if (HasPier) VPier(S);
            int gl = env.GardenLv;
            var PC = new List<double>();                                                            // 풍경 조각(G1)의 깊이 v=(a+b)*4
            Action<double, double, double, Action<VShift, double, double>> pc = (a, b, dz, f) =>
            {
                S.Tag("pc" + PC.Count); PC.Add((a + b) * 4);
                Obj(S, a, b, dz, s => f(s, a, b));
                S.Tag(null);
            };
            var trees = new List<double[]> { new[] { -4.5, -1.5 }, new[] { -2.5, -4.5 } };
            if (Tier >= 2) trees.Add(new[] { 4.5, -2.5 });
            if (gl >= 2) trees.Add(new[] { 0.5, -4.5 });
            if (gl >= 4) trees.Add(new[] { 4.5, 2.5 });
            if (gl >= 8) trees.Add(new[] { -4.5, -3.5 });
            foreach (var t in trees) if (Math.Abs(t[0]) < N - 1 && Math.Abs(t[1]) < N - 1) pc(t[0], t[1], GZ, (q, aa, bb) => VTree(q, aa, bb));
            foreach (var r in new[] { new[] { N - 0.5, -N + 2.5, 1 }, new[] { -N + 1.5, -N + 0.5, 0.8 }, new[] { -N + 0.5, 1.5, 1 }, new[] { N - 2.5, N - 0.5, 0.8 } })
            {
                double rs = r[2];
                pc(r[0], r[1], 0, (q, aa, bb) => VRock(q, aa, bb, rs, 3));                          // 테두리 위의 바위
            }
            Obj(S, N + 1.2, -2.5, 0, q => VRock(q, N + 1.2, -2.5, 1.1));                           // 물 속 바위 (조각이 아니라 섬 그림에 들어간다)
            Obj(S, -N - 1.0, 2.5, 0, q => VRock(q, -N - 1.0, 2.5, 1.0));
            Obj(S, 2.5, N + 1.2, 0, q => VRock(q, 2.5, N + 1.2, 0.8));
            if (Tier >= 2) pc(-3.5, 3.5, GZ, (q, aa, bb) => VHut(q, aa, bb));
            StageDecor(pc);
            if (!env.Winter)
            {
                string[] fcol = { "#ffffff", "#ffe27a", "#ff9bb8", "#a8d4ff" };
                for (int k = 0; k < 40 + 8 * gl; k++)                                               // 풀 사이의 꽃
                {
                    double a = (JsMath.Hsh(k, 31) * 2 - 1) * (N - 1.5), b = (JsMath.Hsh(k, 32) * 2 - 1) * (N - 1.5), ta = Math.Floor(a) + 0.5, tb = Math.Floor(b) + 0.5;
                    if (ao(a, b) || TilePath(ta, tb) || TileSand(ta, tb) || (Tier >= 2 && Math.Abs(a + 3.5) < 2 && Math.Abs(b - 3.5) < 2)) continue;
                    S.Dot(a, b, GZ, fcol[k % 4], GK / 2 + (a + b) * 1000, 1);
                }
            }
            var island = S.Bake(2, true);                                                           // 풍경 조각을 뺀 한 장
            var pieces = S.BakePieces(true).Select(p => new PieceV { Spr = p.Spr, V = PC[int.Parse(p.Tag.Substring(2))] }).ToList();
            // 같은 v 에서는 굽기 순서(k 순) 유지: 안정 정렬
            pieces = pieces.OrderBy(p => p.V).ToList();
            return new IslandResult { N = N, Island = island, Pieces = pieces, Lights = new List<double[]>(lights) };
        }

        void StageDecor(Action<double, double, double, Action<VShift, double, double>> pc)
        {
            void O(double a, double b, Action<VShift, double, double> f, double dz = GZ) => pc(a, b, dz, f);
            if (Stage >= 2) { O(-2.6, N - 0.8, (q, a, b) => VRowboat(q, a, b)); O(3.5, 2.5, (q, a, b) => VSign(q, a, b)); }
            if (Stage >= 3) O(1.5, 4.7, (q, a, b) => VFlowerbed(q, a, b));
            if (Stage >= 4) { O(-4.0, -0.5, (q, a, b) => VBench(q, a, b)); O(3.5, -2.0, (q, a, b) => VWell(q, a, b)); }
            if (Stage >= 5) { O(-4.5, 1.5, (q, a, b) => VFlag(q, a, b)); O(N - 2.1, 2.4, (q, a, b) => VCrates(q, a, b)); }
            if (Stage >= 6) { O(3.6, -3.2, (q, a, b) => VAnchor(q, a, b)); O(-2.4, 3.3, (q, a, b) => VGild(q, a, b)); O(3.4, 3.4, (q, a, b) => VGild(q, a, b)); O(-0.5, N - 1.6, (q, a, b) => VGild(q, a, b)); }
        }
    }
}
