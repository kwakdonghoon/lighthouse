using System;
using System.Collections.Generic;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// 타이틀 장면에 올라가는 구운 그림 한 벌 (HTML bakeTitleScene 1296행 + 관리인 KT 5724행).
    /// 어두운 바다 위의 작은 블록 섬(풀밭·돌 테두리·집·나무 셋), 어린 나무 한 그루, 줄무늬 등대(탑 A·B·램프 켠/끈 것), 블록 배, 관리인.
    /// 인트로의 "한 조각씩 내려앉는" 조각(pieces)은 굽지 않는다(인트로는 이식하지 않음).
    /// </summary>
    public sealed class TitleSprites
    {
        public Bitmap Island, Sapling, TowerA, TowerB, LampOn, LampOff, Ship, Keeper;
        public TowerParams P;
        public const double GZ = 2.5, Ground = GZ * Raster.VZ;
        /// <summary>인트로(첫 실행 9초)에서 한 조각씩 내려앉는 섬 조각(원본 pieces). Build(true) 일 때만 만든다. 조각마다 태그와 시작 지연(delay, 초)·바닥 여부.</summary>
        public List<TitlePiece> Pieces;

        public static TitleSprites Build(bool withPieces = false)
        {
            const int N = 5;
            var env = new IslandEnv(); var ib = new IslandBaker(env);
            var S = new VNew(); var GR = env.Grass;
            var gm = Vmat.Make(GR[3], new VmatOpts { Pal = new[] { GR[2], GR[3], GR[3], GR[4], GR[3], GR[2] }, L = "#7a5a3c", R = "#5a4028", G = 0, Bv = 0 });
            const double GK = -1e6;
            void G(double x, double y, double z, double w, double d, double h, Vmat m) => S.Box(x, y, z, w, d, h, m, GK + S.KeyOf(x, y, z, w, d, h));
            S.Tag("floor");
            S.Floor(-N - 1.6, -N - 1.6, N + 1.6, N + 1.6, 0, "#02060f", 0.5);
            S.Floor(-N - 0.6, -N - 0.6, N + 0.6, N + 0.6, 0, "#0a1a3a", 0.4);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < 2 * N; i++)
                for (int j = 0; j < 2 * N; j++)
                {
                    double a = -N + 0.5 + i, b = -N + 0.5 + j, x = a - 0.5, y = b - 0.5; S.Tag("t:" + a.ToString(inv) + ":" + b.ToString(inv));
                    if (Math.Abs(a) > N - 1 || Math.Abs(b) > N - 1)
                    {
                        G(x, y, 0, 1, 1, 1, IslandBaker.StoneMat(0.78, new VmatOpts { Wet = true })); G(x, y, 1, 1, 1, 1, IslandBaker.StoneMat(0.92)); G(x, y, 2, 1, 1, 1, IslandBaker.StoneMat(1.0));
                    }
                    else G(x, y, 0, 1, 1, GZ, gm);
                }
            void Obj(VNew s, double a, double b, Action<VShift> f) { s.Group(a, b); f(new VShift(s, GZ)); s.GroupEnd(); }
            S.Tag("o:-2.6:2.4"); Obj(S, -2.6, 2.4, q => ib.TitleHut(q, -2.6, 2.4));
            S.Tag("o:-3:-2.4"); Obj(S, -3.0, -2.4, q => ib.TitleTree(q, -3.0, -2.4)); S.Tag("o:3:-2.2"); Obj(S, 3.0, -2.2, q => ib.TitleTree(q, 3.0, -2.2)); S.Tag("o:0.2:-3.2"); Obj(S, 0.2, -3.2, q => ib.TitleTree(q, 0.2, -3.2));
            List<TitlePiece> pieces = null;                                                                  // 원본 순서: pieces = bakePieces(), island = bake(2)
            if (withPieces) pieces = S.BakePieces(false).ConvertAll(p => new TitlePiece(p.Tag, p.Spr));
            var sp = new TitleSprites { Island = S.Bake(2), Pieces = pieces };
            var T = new VNew(); T.Group(2.8, 2.6); ib.TitleTree(new VShift(T, GZ), 2.8, 2.6); sp.Sapling = T.Bake(1);
            var P = new TowerParams { N = 7, Stripes = new[] { new[] { 1.0, 3 }, new[] { 4.0, 6 } }, Roof = "#c9403a", WinSrc = new[] { new WinSrc('L', 0.3, 4.2) }, Skirt = 0 }.Dims();
            sp.P = P; sp.TowerA = TowerBaker.BakeA(P); sp.TowerB = TowerBaker.BakeB(P); sp.LampOn = TowerBaker.BakeLamp(P, true); sp.LampOff = TowerBaker.BakeLamp(P, false);
            var sh = new VNew(); sh.Group(0, 0);                                                            // 블록 배: 상자 선체, 갑판 상자, 돛대, 크림 돛 판, 붉은 깃발
            sh.Box(-1.6, -0.6, 0, 3.2, 1.2, 0.55, Vmat.Make("#a8733f", new VmatOpts { G = 0.04, Tx = Tex.Wood })); sh.Box(-1.5, -0.5, 0.55, 3.0, 1.0, 0.12, Vmat.Make("#6a4526", new VmatOpts { G = 0, Bv = 0 }));
            sh.Box(0.9, -0.3, 0.67, 0.6, 0.6, 0.5, Vmat.Make("#7a5030", new VmatOpts { Tx = Tex.Wood }));
            sh.Box(-0.12, -0.12, 0.67, 0.24, 0.24, 2.4, Vmat.Make("#5a3a1f", new VmatOpts { Bv = 0 }));
            sh.Box(-0.9, -0.08, 0.9, 1.8, 0.16, 1.5, Vmat.Make("#e4dcc6", new VmatOpts { L = "#c4baa0", R = "#aca48d", G = 0.04, Bv = 1 }));
            sh.Box(-0.05, -0.05, 3.07, 0.5, 0.1, 0.25, Vmat.Make("#e0453e", new VmatOpts { Bv = 0, G = 0 }));
            sp.Ship = sh.Bake(1);
            string[] head = { "..cccc..", ".cccccc.", "..ssss..", "..ssse..", "..ssss..", ".bbbbbb.", "BbbbbbbB", "BbbbbbbB", "sbbbbbbs", ".bbbbbb.", "..pppp.." };
            string[] legs = { "..pp.pp.", "..pp.pp.", "..dd.dd." };
            var rows = new List<string>(head); rows.AddRange(legs);
            var pal = new Dictionary<char, string> { { 'c', "#3d5a8a" }, { 's', "#f0c8a0" }, { 'b', "#e8b84a" }, { 'p', "#2c3a58" }, { 'd', "#20140c" }, { 'e', "#20140c" } };
            var bc = Col.Hex("#e8b84a"); pal['B'] = Col.HexOf(Col.Mul(bc, 0.78));
            sp.Keeper = ShipBaker.FromDesign(new ShipDesign { Ox = 4, Oy = 14, Rows = rows.ToArray() }, pal);
            return sp;
        }
    }

    /// <summary>인트로 섬 조각 하나(원본 pieces[i] + infoOf 5608행): 떨어지기 시작하는 시각(Delay)·바닥 여부.</summary>
    public sealed class TitlePiece
    {
        public readonly string Tag; public readonly Bitmap Spr; public readonly double Delay; public readonly bool Floor;
        public TitlePiece(string tag, Bitmap spr)
        {
            Tag = tag; Spr = spr;
            if (tag == "floor") { Delay = 1.0; Floor = true; return; }
            var q = tag.Split(':'); var inv = System.Globalization.CultureInfo.InvariantCulture;
            double a = double.Parse(q[1], inv), b = double.Parse(q[2], inv), ring = Math.Max(Math.Abs(a), Math.Abs(b));
            Delay = 1.3 + (ring - 0.5) * 0.55 + (q[0] == "o" ? 0.4 : 0) + JsMath.Hsh((int)JsMath.Round(a * 7), (int)JsMath.Round(b * 7)) * 0.12;
        }
    }

    /// <summary>화면 크기에 맞춘 타이틀 장면의 크기·배율 (HTML sz() 5612행). 길이는 모두 기기 픽셀(dpr 은 이미 곱한 값).</summary>
    public readonly struct TitleSize
    {
        public readonly int Lw, Lh, Sx;
        public TitleSize(int devW, int devH)
        {
            // 섬 장면(폭 약 115·높이 약 130 도트)이 화면 폭의 80%·높이의 50%를 넘지 않게 배율을 정한다
            int bw = (int)Math.Floor(0.8 * devW / 96); double bh = 0.5 * devH / 130;
            Sx = Math.Max(2, Math.Min(bw, devH >= 700 ? Math.Max(3, (int)Math.Floor(bh)) : (int)Math.Floor(bh)));
            Lw = (int)Math.Ceiling((double)devW / Sx); Lh = (int)Math.Ceiling((double)devH / Sx);
        }
    }

    /// <summary>
    /// 타이틀 한 프레임을 CPU 로 그린다 (HTML draw 5675행, 인트로 없는 경우: tI = 99). 같은 t 면 같은 그림(난수는 해시뿐).
    /// 결과는 lw x lh 불투명 픽셀(0xAABBGGRR, 위 행부터). 화면에서는 포인트 필터 Texture2D 로 정수 배율(Sx)로 늘려 보인다.
    /// 캔버스 규칙: 정수 칸 사각형은 알파 합성, 'lighter' 는 가산(255 에서 멈춤), 소수 좌표 사각형(안개 띠)은 가로 덮임비율만큼 알파.
    /// </summary>
    public sealed class TitleRenderer
    {
        public readonly TitleSize Size; public readonly int Lw, Lh; public readonly uint[] Pixels;
        /// <summary>true 이면 첫 실행 인트로(약 9초: 섬이 쌓이고 등대가 켜진 뒤 글자가 내려앉는다, 원본 INTRO). 기본 false = 인트로 없는 장면(tI = 99).</summary>
        public bool Intro;
        /// <summary>인트로 중 블록이 내려앉는 소리(stone·build·lamp) 요청 (원본 EV 5671행). 시각이 앞으로 흘러 문턱을 넘은 프레임에 한 번씩.</summary>
        public event Action<string> Sfx;
        double lastT = -1;
        static readonly (double t, string name)[] SfxEv = MakeSfxEv();
        static (double, string)[] MakeSfxEv() { var l = new List<(double, string)>(); for (double r = 0.5; r <= 4.6; r += 1) l.Add((1.3 + (r - 0.5) * 0.55 + 0.45, "stone")); l.Add((5.9, "build")); l.Add((6.4, "lamp")); return l.ToArray(); }
        public int LogoTop, LogoH;                                    // 글자 그림의 윗줄·높이 (도트): 부제는 그 아래(LogoTop+LogoH+6)에 놓는다
        readonly TitleSprites sp; readonly Col[] sky, sea; readonly CpuCanvas cv;
        Bitmap[] letters = new Bitmap[0], tint = new Bitmap[0]; int letterTotal, letterGap, letterU;

        const double WRot = 0.7, Th0 = -0.6, Dg = 62;
        static readonly string[] SkyHex = { "#03061a", "#050a22", "#070e2c", "#0a1438", "#101c4a", "#1a2658", "#2a3068" }, SeaHex = { "#0e1c4a", "#0a1640", "#08122e", "#060e26", "#050b1e" };
        static readonly string[] Gift = { "shell", "pearl", "mail8", "starfish", "bottle" };

        public TitleRenderer(TitleSprites sprites, int devW, int devH)
        {
            sp = sprites; Size = new TitleSize(devW, devH); Lw = Size.Lw; Lh = Size.Lh; cv = new CpuCanvas(Lw, Lh); Pixels = cv.D;
            sky = Array.ConvertAll(SkyHex, Col.Hex); sea = Array.ConvertAll(SeaHex, Col.Hex);
            BuildLetters();
        }

        // ---------------- 글자 (buildLetters 5618행) ----------------
        static readonly string[][] Gold =
        {
            new[] { "#fff0a8", "#ffe58a", "#ffe08a" }, new[] { "#ffd45a", "#f6c847", "#ffcf55" }, new[] { "#f0b535", "#e8a92d", "#f2b83a" }, new[] { "#d98f22", "#cf8620", "#dd9528" }
        };

        void BuildLetters()
        {
            string chars = TitleGlyphs.Text; int n = chars.Length, S = 22, u = 1;
            for (int c = 3; c >= 1; c--) if (n * ((S + 2) * c + c + 2) + c * (n - 1) - 2 * c <= 0.99 * Lw) { u = c; break; }       // 실제 폭이 화면에 들어가는 가장 큰 배율
            if (u == 1) S = Math.Max(16, Math.Min(28, (int)Math.Floor((0.97 * Lw - 1) / 4) - 5));                              // 좁은 화면은 배율 대신 글자 그림 자체를 크게 그린다
            int D = u, g = S + 2; letters = new Bitmap[n]; tint = new Bitmap[n]; int total = 0;
            for (int ci = 0; ci < n; ci++)
            {
                char ch = chars[ci]; var m = TitleGlyphs.Mask(S, ch) ?? new bool[g * g];
                bool On(int x, int y) => x >= 0 && y >= 0 && x < g && y < g && m[y * g + x];
                int w = g * u + D + 2, h = g * u + D + 4; var B = new Bitmap(0, 0, w, h);
                void R(int x, int y, int rw, int rh, string hex)
                {
                    var c = Col.Hex(hex); uint px = Bitmap.Pack((int)c.R, (int)c.G, (int)c.B, 255);
                    for (int j = Math.Max(0, y); j < Math.Min(h, y + rh); j++) for (int i = Math.Max(0, x); i < Math.Min(w, x + rw); i++) B.D[j * w + i] = px;
                }
                for (int d = D; d >= 1; d--) for (int y = 0; y < g; y++) for (int x = 0; x < g; x++) if (On(x, y)) R(x * u + d, y * u + d + 1, u, u, d == D ? "#2a1204" : "#7a3d12");       // 두께와 그림자
                for (int y = 0; y < g; y++)
                    for (int x = 0; x < g; x++)
                        if (On(x, y))
                        {
                            long hl = ((long)x * 73856093 ^ (long)y * 19349663 ^ ((long)ch + (long)x * y) * 83492791) & 0xFFFFFFFFL; int hs = (int)(hl % 3);   // JS: ToInt32 비트 연산 후 >>> 0
                            R(x * u, y * u + 1, u, u, Gold[Math.Min(3, (int)Math.Floor((double)y / g * 4))][hs]);
                            if (!On(x, y - 1)) R(x * u, y * u + 1, u, 1, "#fffbe0");
                            if (!On(x - 1, y)) R(x * u, y * u + 1, 1, u, "#fff3b8");
                            if (!On(x, y + 1)) R(x * u, y * u + u, u, 1, "#a8631a");
                            if (!On(x + 1, y)) R(x * u + u - 1, y * u + 1, 1, u, "#b8721e");
                            if (!On(x, y - 1) || !On(x - 1, y)) { if (!On(x, y - 1)) R(x * u, y * u, u, 1, "#2a1204"); if (!On(x - 1, y)) R(x * u - 1, y * u + 1, 1, u, "#2a1204"); }
                        }
                letters[ci] = B; tint[ci] = new Bitmap(0, 0, w, h); total += w;
            }
            letterGap = u; letterU = u; letterTotal = total + letterGap * (n - 1) - 2 * D;
            LogoTop = (int)JsMath.Round(Lh * 0.07); LogoH = letters[0].H;
        }

        /// <summary>글자 위에 겹치는 부제(--lt)의 윗 위치: 글자 바로 아래 6도트.</summary>
        public int SubtitleTop => LogoTop + LogoH + 6;
        public int LogoTotalWidth => letterTotal;

        // ---------------- 캔버스 연산 ----------------
        static int Mix(int dst, double c, int a8) { int sp0 = (int)JsMath.Round(c * a8 / 255.0); return Math.Min(255, (int)JsMath.Round(sp0 + dst * (255 - a8) / 255.0)); }
        static int A8(double a) => (int)JsMath.Round(Math.Max(0, Math.Min(1, a)) * 255);

        /// <summary>fillRect(정수 칸, 알파).</summary>
        void Fill(int x0, int y0, int w, int h, Col c, double alpha = 1)
        {
            int a8 = A8(alpha); if (a8 == 0) return;
            for (int j = Math.Max(0, y0); j < Math.Min(Lh, y0 + h); j++)
                for (int i = Math.Max(0, x0); i < Math.Min(Lw, x0 + w); i++)
                {
                    uint d = Pixels[j * Lw + i];
                    Pixels[j * Lw + i] = Bitmap.Pack(Mix((int)(d & 255), c.R, a8), Mix((int)((d >> 8) & 255), c.G, a8), Mix((int)((d >> 16) & 255), c.B, a8), 255);
                }
        }

        /// <summary>'lighter' 로 fillRect(정수 칸): 가산.</summary>
        void Add(int x0, int y0, int w, int h, Col c, double alpha)
        {
            int a8 = A8(alpha); if (a8 == 0) return;
            int ar = (int)JsMath.Round(c.R * a8 / 255.0), ag = (int)JsMath.Round(c.G * a8 / 255.0), ab = (int)JsMath.Round(c.B * a8 / 255.0);
            for (int j = Math.Max(0, y0); j < Math.Min(Lh, y0 + h); j++)
                for (int i = Math.Max(0, x0); i < Math.Min(Lw, x0 + w); i++)
                {
                    uint d = Pixels[j * Lw + i];
                    Pixels[j * Lw + i] = Bitmap.Pack(Math.Min(255, (int)(d & 255) + ar), Math.Min(255, (int)((d >> 8) & 255) + ag), Math.Min(255, (int)((d >> 16) & 255) + ab), 255);
                }
        }

        /// <summary>'lighter' 로 소수 좌표(가로) 사각형: 가장자리 칸은 덮인 비율만큼 알파.</summary>
        void AddAA(double x, int y0, double w, int h, Col c, double alpha)
        {
            double xe = x + w; int i0 = (int)Math.Floor(x), i1 = (int)Math.Ceiling(xe);
            for (int i = i0; i < i1; i++)
            {
                double cov = Math.Min(i + 1, xe) - Math.Max(i, x); if (cov <= 0) continue;
                Add(i, y0, 1, h, c, alpha * cov);
            }
        }

        /// <summary>drawImage 를 'lighter' 로: 스프라이트 색 * 알파 를 더한다.</summary>
        void PutAdd(Bitmap s, double x, double y, double ga)
        {
            int ox = (int)JsMath.Round(x + s.Ax), oy = (int)JsMath.Round(y + s.Ay), g8 = A8(ga);
            for (int j = 0; j < s.H; j++)
            {
                int dy = oy + j; if (dy < 0 || dy >= Lh) continue;
                for (int i = 0; i < s.W; i++)
                {
                    int dx = ox + i; if (dx < 0 || dx >= Lw) continue;
                    uint p = s.D[j * s.W + i]; int a = (int)(p >> 24); if (a == 0) continue;
                    uint d = Pixels[dy * Lw + dx];
                    Func<int, int, int> ad = (dst, c) => Math.Min(255, dst + (int)JsMath.Round(JsMath.Round(c * a / 255.0) * g8 / 255.0));
                    Pixels[dy * Lw + dx] = Bitmap.Pack(ad((int)(d & 255), (int)(p & 255)), ad((int)((d >> 8) & 255), (int)((p >> 8) & 255)), ad((int)((d >> 16) & 255), (int)((p >> 16) & 255)), 255);
                }
            }
        }

        /// <summary>drawImage(s, x, y, w, h): 크기를 바꿔 그리기(보간 끔 = 가장 가까운 점). 좌표는 이미 정수.</summary>
        // 크기를 줄여 그릴 때 원본 브라우저(보간 끔)와 맞춘 표본 위치: 가로는 칸의 왼쪽 가까이, 세로는 칸의 가운데 (골든으로 정함. 반 크기 배는 가로 한 열이 어긋나는 점 2개가 남는다)
        const double SX = 0.25, SY = 0.5;
        void PutScaled(Bitmap s, int ox, int oy, int w, int h, double ga)
        {
            int g8 = A8(ga);
            for (int j = 0; j < h; j++)
            {
                int dy = oy + j; if (dy < 0 || dy >= Lh) continue; int sy = Math.Min(s.H - 1, (int)Math.Floor((j + SY) * s.H / h));
                for (int i = 0; i < w; i++)
                {
                    int dx = ox + i; if (dx < 0 || dx >= Lw) continue; int sx = Math.Min(s.W - 1, (int)Math.Floor((i + SX) * s.W / w));
                    uint p = s.D[sy * s.W + sx]; int a = (int)(p >> 24); if (a == 0) continue;
                    uint d = Pixels[dy * Lw + dx]; int sa = (int)JsMath.Round(a * g8 / 255.0);
                    Func<int, int, int> mx = (dst, c) => Math.Min(255, (int)JsMath.Round(JsMath.Round(JsMath.Round(c * a / 255.0) * g8 / 255.0) + dst * (255 - sa) / 255.0));
                    Pixels[dy * Lw + dx] = Bitmap.Pack(mx((int)(d & 255), (int)(p & 255)), mx((int)((d >> 8) & 255), (int)((p >> 8) & 255)), mx((int)((d >> 16) & 255), (int)((p >> 16) & 255)), 255);
                }
            }
        }

        Col Mixc(Col[] pal, double f)
        {
            double p = Math.Min(pal.Length - 1.001, f * (pal.Length - 1)); int i = (int)Math.Floor(p); var a = pal[i]; var b = pal[i + 1]; double q = p - i;
            return new Col(JsMath.Round(a.R + (b.R - a.R) * q), JsMath.Round(a.G + (b.G - a.G) * q), JsMath.Round(a.B + (b.B - a.B) * q));
        }

        static readonly Col White = new Col(255, 255, 255);
        static double Fmod(double a, double b) => a % b;

        // ---------------- 글자 그리기 (drawLetters 5646행) ----------------
        void DrawLetters(double t)
        {
            int n = letters.Length, x0 = (int)JsMath.Round((Lw - letterTotal) / 2.0), x = x0;
            {
                double ga = Math.Max(0, Math.Min(1, (t - (Intro ? 9.6 : 0.6)) / 0.8));                                                           // 로고 둘레에 반짝이는 네 갈래 별 몇 개
                if (ga > 0)
                    foreach (var q in new[] { new[] { 0.97, 0.05, 0 }, new[] { 0.04, 0.62, 1.7 }, new[] { 0.5, -0.12, 3.1 } })
                    {
                        double s = Math.Sin(t * 2.2 + q[2]); int r = s > 0.35 ? (s > 0.8 ? 3 : 2) : 0; if (r == 0) continue;
                        int sx0 = (int)JsMath.Round(x0 + letterTotal * q[0]), sy0 = (int)JsMath.Round(LogoTop + LogoH * q[1]);
                        var c1 = Col.Hex("#fff6c4"); Fill(sx0 - r, sy0, r * 2 + 1, 1, c1, ga); Fill(sx0, sy0 - r, 1, r * 2 + 1, c1, ga); Fill(sx0, sy0, 1, 1, White, ga);
                    }
            }
            for (int i = 0; i < n; i++)
            {
                var q = letters[i]; double tA = Intro ? 9.0 + i * 0.18 : 0.1 + i * 0.08, f = Math.Max(0, Math.Min(1, (t - tA) / (Intro ? 0.35 : 0.25)));
                if (f > 0)
                {
                    int off = -(int)JsMath.Round((1 - f) * (1 - f) * (Intro ? 44 : 18)), y = LogoTop + off; double ga2 = Math.Min(1, (t - tA) / 0.1);
                    const double PC = 4.49; double sw = -0.25 + (Fmod(t, PC) / PC) * 1.5, xc = (x - x0 + q.W / 2.0) / letterTotal, gl0 = Math.Max(0, 1 - Math.Abs(xc - sw) / 0.3), gl = gl0 * gl0 * (3 - 2 * gl0);   // 등대 빛이 글자 위를 한 번 쓸고 지나간다
                    var tb = tint[i]; Array.Copy(q.D, tb.D, q.D.Length);
                    int a1 = A8(Math.Round(0.34 * (1 - gl) * 1000) / 1000), a2 = A8(Math.Round(0.42 * gl * 1000) / 1000);      // fillStyle 알파는 toFixed(3)
                    for (int k = 0; k < tb.D.Length; k++)
                    {
                        uint p = tb.D[k]; if ((p >> 24) == 0) continue;
                        int r = (int)(p & 255), gg = (int)((p >> 8) & 255), b = (int)((p >> 16) & 255);
                        if (a1 > 0) { r = Mix(r, 6, a1); gg = Mix(gg, 10, a1); b = Mix(b, 40, a1); }
                        if (a2 > 0) { r = Mix(r, 255, a2); gg = Mix(gg, 240, a2); b = Mix(b, 170, a2); }
                        tb.D[k] = Bitmap.Pack(r, gg, b, 255);
                    }
                    cv.Put(tb, x, y, ga2);
                }
                x += q.W + letterGap;
            }
        }

        readonly Dictionary<int, (int gx, int gy, double a)> beamCells = new Dictionary<int, (int, int, double)>();
        static readonly double[] BeamAlphaTab = MakeBeamTab();
        static double[] MakeBeamTab() { var a = new double[41]; for (int q = 0; q <= 40; q++) a[q] = q / 40.0; return a; }

        /// <summary>한 프레임을 Pixels 에 그린다. t &lt; 0 이면 정지 장면(움직임 줄이기).</summary>
        public void Draw(double t)
        {
            const double PER = Math.PI / WRot, TWO = Math.PI * 2;
            var P = sp.P; int cx = (int)JsMath.Round(Lw / 2.0), cy = (int)JsMath.Round(Lh * 0.6); double gy = cy - TitleSprites.Ground, lampY = gy - ((P.Zt * Raster.VZ + 0.5) + 1.5 + 6 / 2.0);
            double shx = cx + Math.Min(Lw * 0.31, 56); int shy = (int)JsMath.Round(cy + 22);
            double thShip = Math.Atan2((shy - cy) * 2, shx - cx), c0 = Fmod(Fmod((thShip - Th0) / WRot, PER) + PER, PER);              // 배가 있는 쪽 각도, 첫 번째로 배를 비추는 시각
            if (t < 0) t = c0 + 1.2;                                                                                                   // 정지 장면: 빛이 배를 비춘 직후
            double tI = Intro ? t : 99; Func<double, double, double> phI = (a, b) => Math.Max(0, Math.Min(1, (tI - a) / (b - a)));
            double starK = Intro ? Math.Min(1, tI / 1.2) : 1, wg = Intro ? Math.Min(1, tI / 1.5) : 1;
            if (Intro && lastT >= 0 && Sfx != null) foreach (var ev in SfxEv) if (lastT < ev.t && ev.t <= t) Sfx(ev.name);
            lastT = t;
            int hz = (int)JsMath.Round(cy - 20);
            // ---- 하늘·바다: 가는 띠로 나눠 색이 부드럽게 이어지되 계단은 남긴다
            const int NB = 18, NS = 12; int bh = (int)Math.Ceiling((double)hz / NB), sh = (int)Math.Ceiling((double)(Lh - hz) / NS);
            for (int i = 0; i < NB; i++) Fill(0, i * bh, Lw, bh + 1, Mixc(sky, i / (double)(NB - 1)));
            for (int i = 0; i < NS; i++) Fill(0, hz + i * sh, Lw, sh + 1, Mixc(sea, i / (double)(NS - 1)));
            foreach (var ha in new[] { new[] { 14, 0.03 }, new[] { 9, 0.05 }, new[] { 5, 0.07 }, new[] { 2, 0.09 } }) Add(0, hz - (int)ha[0], Lw, (int)ha[0], new Col(255, 150, 90), ha[1] * wg);   // 수평선에 남은 저녁빛
            foreach (var mt in new[] { (Col.Hex("#0a1236"), 9, 3, 7), (Col.Hex("#070c28"), 6, 11, 5) })                                    // 먼 산등성이 두 겹 (계단)
                for (int x = 0; x < Lw; x += mt.Item4)
                {
                    int hgt = (int)JsMath.Round(mt.Item2 * (0.35 + 0.65 * Math.Abs(Math.Sin(x * 0.045 + mt.Item3)) * (0.6 + 0.4 * JsMath.Hsh(x / mt.Item4, mt.Item3))));
                    Fill(x, hz - hgt, mt.Item4, hgt, mt.Item1, wg);
                }
            for (int i = 0; i < 18; i++)
            {
                double tw = 0.5 + 0.5 * Math.Sin(t * (0.8 + JsMath.Hsh(i, 3)) + i * 5);
                Fill((int)Math.Floor(JsMath.Hsh(i, 1) * Lw), (int)Math.Floor(JsMath.Hsh(i, 2) * (hz - 8)), 1, 1, Col.Hex(JsMath.Hsh(i, 5) > 0.85 ? "#ffe9b0" : "#c8d4ff"), (0.3 + 0.6 * tw) * starK);
            }
            int mx = (int)JsMath.Round(Lw * 0.84), my = (int)JsMath.Round(hz * 0.8);                                                       // 네모 달과 달무리
            var halo = Col.Hex("#bed2ff"); Fill(mx - 14, my - 14, 28, 28, halo, 0.09 * starK); Fill(mx - 10, my - 10, 20, 20, halo, 0.13 * starK);
            Fill(mx - 6, my - 6, 12, 12, Col.Hex("#f1ebcc"), starK); var d8 = Col.Hex("#d8d0a8"); Fill(mx + 3, my - 6, 3, 12, d8, starK); Fill(mx - 6, my + 3, 12, 3, d8, starK);
            var c5 = Col.Hex("#c5bd92"); Fill(mx - 3, my - 3, 2, 2, c5, starK); Fill(mx + 1, my + 1, 2, 2, c5, starK);
            {   // 바다 위로 길게 늘어지는 안개 띠 + 달빛 길 + 등불이 물에 비친 기둥 ('lighter')
                double mm = 0.08 + 0.04 * Math.Sin(t * 0.5), wa = Intro ? phI(6.4, 7.4) : 1;
                for (int i = 0; i < 3; i++)
                {
                    int y = hz + 2 + i * 9; double w = Lw * 0.5, x = Fmod(t * (1.4 + i * 0.5) + i * 70, Lw + w) - w; var mc = new Col(150, 170, 230); double al = mm * (1 - i * 0.25);
                    AddAA(x, y, w, 2, mc, al * starK); AddAA(x + 6, y - 1, w - 14, 1, mc, al * starK);      // 달 그릴 때 정한 globalAlpha(= starK)가 이 묶음 끝까지 남아 곱해진다(원본 5698~5705행)
                }
                for (int i = 0; i < 11; i++)
                {
                    double sh2 = Math.Sin(t * 1.4 + i * 1.3), w = 3 + i * 0.9;
                    Add(mx - (int)Math.Floor(w / 2) + (int)JsMath.Round(sh2 * 2), hz + 3 + i * 4, (int)JsMath.Round(w), 1, new Col(232, 226, 184), (0.3 - 0.022 * i) * (0.6 + 0.4 * sh2) * starK * starK);      // 채움 알파 × 남은 globalAlpha
                }
                if (wa > 0) for (int i = 0; i < 12; i++)
                {
                    double fl = 0.7 + 0.3 * Math.Sin(t * 2 + i * 1.7); int w = (int)JsMath.Round(9 - i * 0.55 + Math.Sin(t * 1.3 + i) * 1.5);
                    Add(cx - (int)Math.Floor(w / 2.0), cy + 27 + i * 3, w, 1, new Col(255, 200, 110), (0.34 - 0.024 * i) * fl * wa * starK);
                }
            }
            DrawLetters(t);
            if (tI < 4.7 && sp.Pieces != null)                                                                                             // 섬이 쌓인다: 블록이 한 조각씩 위에서 내려앉는다 (가운데에서 바깥으로)
            {
                foreach (var pc in sp.Pieces)
                {
                    double tt = tI - pc.Delay; if (tt < 0) continue;
                    double f = Math.Min(1, tt / 0.38); int off = pc.Floor ? 0 : (int)JsMath.Round(-(1 - f) * (1 - f) * 40);
                    cv.Put(pc.Spr, cx, cy + off, pc.Floor ? Math.Min(1, tt) : Math.Min(1, tt / 0.12));
                }
            }
            else cv.Put(sp.Island, cx, cy);                                                                                                // 섬
            if (tI >= 4.2)                                                                                                                 // 등대가 한 층씩 올라간다 (아래에서 위로 드러남)
            {
                double r = phI(4.2, 5.9);
                if (r >= 1) cv.Put(sp.TowerA, cx, gy); else PutClipTop(sp.TowerA, cx, gy, r);
            }
            if (tI >= 5.9)
            {
                double f = phI(5.9, 6.3); int off = (int)JsMath.Round(-(1 - f) * (1 - f) * 24);
                cv.Put(tI >= 6.4 ? sp.LampOn : sp.LampOff, cx, gy); cv.Put(sp.TowerB, cx, gy + off, Math.Min(1, (tI - 5.9) / 0.1));         // 램프 → 탑 앞
            }
            double tm = Fmod(t, 12), sap = tm < 6 ? 0 : tm < 7 ? tm - 6 : tm < 11 ? 1 : 12 - tm;                                          // 섬이 자란다: 어린 나무가 한 그루 돋았다 사라진다
            if (tI >= 9 && sap > 0.01) cv.Put(sp.Sapling, cx, cy, sap);
            if (tI >= 7.0)
            {   // 등대지기: 탑 앞 풀밭에서 배를 향해 가끔 손을 흔든다
                int kx = cx + 13, ky = cy + 1; bool wv = Fmod(t, 6) > 4.2; double fa = Intro ? phI(7.0, 7.6) : 1;
                Fill(kx - 3, ky - 1, 7, 2, new Col(0, 0, 0), 0.3 * fa); cv.Put(sp.Keeper, kx, ky, fa);
                if (wv) { int up = (int)Math.Floor(t * 5) % 2; var sk = Col.Hex("#f0c8a0"); Fill(kx + 3, ky - 9 - up, 1, 2, sk, fa); Fill(kx + 4 - up, ky - 11 - up, 1, 1, sk, fa); }
            }
            double lampOn = Intro ? phI(6.4, 6.9) : 1, bm = Intro ? phI(6.6, 7.6) : 1, slide = Intro ? phI(7.4, 8.8) : 1, ease = 1 - (1 - slide) * (1 - slide), shxA = shx + (1 - ease) * 70;
            double fade = Intro ? phI(7.4, 8.0) : Math.Max(0, Math.Min(1, (t - 0.3) / 1)), bob = Math.Round(Math.Sin(t * 1.2)), shyA = shy + bob;
            // ---- 램프 번짐(네모 3겹), 빛줄기, 불씨 ('lighter')
            if (lampOn > 0) foreach (var ha in new[] { new[] { 9, 0.04 }, new[] { 6, 0.07 }, new[] { 3, 0.14 } }) Add(cx - (int)ha[0], (int)JsMath.Round(lampY) - (int)ha[0], (int)ha[0] * 2, (int)ha[0] * 2, new Col(255, 205, 110), ha[1] * lampOn);
            if (Intro && tI >= 6.4 && tI < 6.95)                                                                                            // 불이 켜지는 순간 번쩍 (strokeRect 선 굵기 2 = 모서리에서 안팎 1칸)
            {
                double q = (tI - 6.4) / 0.55; int h = (int)JsMath.Round(5 + q * 26), ly = (int)JsMath.Round(lampY); var fc = new Col(255, 230, 150); double al = 0.5 * (1 - q);
                Add(cx - h - 1, ly - h - 1, 2 * h + 2, 2, fc, al); Add(cx - h - 1, ly + h - 1, 2 * h + 2, 2, fc, al); Add(cx - h - 1, ly - h + 1, 2, 2 * h - 2, fc, al); Add(cx + h - 1, ly - h + 1, 2, 2 * h - 2, fc, al);
            }
            if (bm > 0)
            {
                double th = Th0 + WRot * t, pulse = 0.9 + 0.1 * Math.Sin(t * 1.3); beamCells.Clear();
                Action<double, double> beam = (a, mul) =>
                {
                    double ex = cx + Math.Cos(a) * Dg, ey = cy + Math.Sin(a) * Dg * 0.5, dx = ex - cx, dy = ey - lampY, len = Math.Sqrt(dx * dx + dy * dy), ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
                    for (int i = 0; i < 70; i++)
                    {
                        double d = (i + 0.5) / 70, ww = 2 + 20 * d, fx = cx + ux * len * 1.08 * d, fy = lampY + uy * len * 1.08 * d, bas = (1 - d * 0.6) * pulse * mul * bm;
                        for (double j = -ww; j <= ww; j += 1.5)
                        {
                            double k = Math.Abs(j) / ww, al = (k < 0.35 ? 0.3 : k < 0.7 ? 0.2 : 0.1) * bas; int gx = (int)JsMath.Round((fx + nx * j) / 2), gyy = (int)JsMath.Round((fy + ny * j) / 2), key = gx * 8192 + gyy;
                            if (!beamCells.TryGetValue(key, out var o) || o.a < al) beamCells[key] = (gx, gyy, al);
                        }
                    }
                };
                foreach (var om in new[] { new[] { 0, 1.0 }, new[] { -0.16, 0.5 }, new[] { -0.32, 0.25 } }) { beam(th + om[0], om[1]); beam(th + Math.PI + om[0], om[1]); }
                var bc = new Col(255, 214, 120);
                foreach (var o in beamCells.Values) { int q = Math.Max(1, (int)JsMath.Round(o.a * 40)); Add(o.gx * 2, o.gy * 2, 2, 2, bc, q / 40.0); }
            }
            if (lampOn > 0) for (int i = 0; i < 9; i++)
            {
                double ph0 = Fmod(t * 0.22 + JsMath.Hsh(i, 61), 1); int mxp = cx + (int)JsMath.Round(Math.Sin(t * 0.7 + i * 2.3) * 9 + (JsMath.Hsh(i, 62) - 0.5) * 16), myp = (int)JsMath.Round(lampY + 4 - ph0 * 46);
                Add(mxp, myp, 1, 1, new Col(255, 214, 120), Math.Sin(ph0 * Math.PI) * 0.7 * lampOn);
            }
            // ---- 배: 빛을 따라 들어온다
            cv.Put(sp.Ship, shxA, shyA, fade);
            {
                var ship = sp.Ship; double X = cx - Math.Min(Lw * 0.3, 54), Y = cy + 46 + Math.Round(Math.Sin(t * 1.0 + 2));                  // 앞쪽 왼편에 떠 있는 작은 배
                PutScaled(ship, (int)JsMath.Round(X - ship.Ax - ship.W), (int)JsMath.Round(Y + ship.Ay), ship.W, ship.H, fade * 0.9);
                double fx = Fmod(t * 2.2 + Lw * 0.5, Lw + 60) - 30; int fw = (int)JsMath.Round(ship.W * 0.5), fh = (int)JsMath.Round(ship.H * 0.5);   // 수평선 쪽으로 천천히 지나가는 먼 배
                PutScaled(ship, (int)JsMath.Round(fx + ship.Ax * 0.5), hz + 6 - fh + (int)JsMath.Round(Math.Sin(t * 0.8)), fw, fh, fade * 0.55);
            }
            Func<double, double> wrapA = a => { a = Fmod(a + Math.PI, TWO); if (a < 0) a += TWO; return a - Math.PI; };
            double th2 = Th0 + WRot * t, dA = Math.Min(Math.Abs(wrapA(th2 - thShip)), Math.Abs(wrapA(th2 + Math.PI - thShip))), lit = tI >= 8.8 ? Math.Max(0, 1 - dA / 0.45) : 0;
            if (lit > 0.03) PutAdd(sp.Ship, shxA, shyA, lit * 0.45 * fade);                                                                 // 빛이 지나는 동안 배가 밝아진다
            var wk = Col.Hex("#d8ecfa"); for (int i = 0; i < 4; i++) Fill((int)JsMath.Round(shxA - 20 - i * 4), (int)JsMath.Round(shyA + 2 + (i & 1)), 2, 1, wk, fade * (0.7 - i * 0.15));
            {   // 빛이 배를 비출 때마다 선물·편지가 하나씩 떠오른다
                int kk = (int)Math.Floor((t - c0) / PER); double age = t - (c0 + kk * PER) - 0.25;
                if (tI >= 9.2 && kk >= 0 && age >= 0 && age < 2.6)
                {
                    double p = age / 2.6; var ic = giftIcon(kk % Gift.Length);
                    cv.Put(ic, Math.Round(shx + (kk % 2 != 0 ? -10 : 2)), Math.Round(shy - 22 - p * 16), Math.Sin(Math.PI * p) * fade);
                }
            }
            var black = new Col(0, 0, 0);                                                                                                  // 가장자리 어둡게
            for (int i = 0; i < 4; i++) { int e = 5 * (4 - i); Fill(0, 0, Lw, e, black, 0.1); Fill(0, Lh - e, Lw, e, black, 0.1); Fill(0, 0, e, Lh, black, 0.1); Fill(Lw - e, 0, e, Lh, black, 0.1); }
        }

        /// <summary>탑 A 를 아래에서 위로 r(0~1)만큼만 드러낸다 (원본 clip rect 5718행: 위쪽 (1-r) 부분을 자른다).</summary>
        void PutClipTop(Bitmap s, double x, double y, double r)
        {
            int top = (int)JsMath.Round(y + s.Ay), clipY = (int)JsMath.Round(top + s.H * (1 - r)), k = Math.Max(0, Math.Min(s.H, clipY - top)); if (k >= s.H) return;
            var cb = new Bitmap(s.Ax, s.Ay + k, s.W, s.H - k); Array.Copy(s.D, k * s.W, cb.D, 0, (s.H - k) * s.W); cv.Put(cb, x, y);
        }

        readonly Bitmap[] giftIcons = new Bitmap[5];
        Bitmap giftIcon(int i) => giftIcons[i] ?? (giftIcons[i] = IconBaker.Bake(Gift[i]));
    }
}
