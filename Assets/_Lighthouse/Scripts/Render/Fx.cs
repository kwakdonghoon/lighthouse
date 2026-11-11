using System;
using System.Collections.Generic;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// 프레임 단위 연출 (M2 2차): 하늘 장식(별·달·해·구름·갈매기·별똥별, drawSkyDeco 737행), 바다 물결·반짝임(drawSea 706행), 불빛 번짐(glow 666행).
    /// 모두 시각 t(초)와 dusk 만의 함수 — 난수는 해시(hsh)뿐이라 결정적이다. 좌표는 설계 좌표(번역 OY 전), 그린 뒤 +oy.
    /// </summary>
    public static class Fx
    {
        const int Horizon = BgBaker.Horizon, DesignH = BgBaker.DesignH;

        static readonly (double x, double y, double s, double ph)[] Stars = MakeStars();
        static (double, double, double, double)[] MakeStars()
        {
            var a = new (double, double, double, double)[90];
            for (int i = 0; i < 90; i++) a[i] = (JsMath.Hsh(i, 11), JsMath.Hsh(i, 12), JsMath.Hsh(i, 13), JsMath.Hsh(i, 14) * 6.28);
            return a;
        }
        static readonly (double x, double dy, double sc, double sp)[] Clouds =
        {
            (0.2, 0.12, 1.1, 1.6), (0.62, 0.3, 0.8, 1.1), (0.9, 0.5, 0.65, 0.8), (0.38, 0.7, 0.5, 0.6), (0.05, 0.42, 0.7, 0.9), (0.75, 0.86, 0.4, 0.5)
        };
        static readonly (double d, double x, double sp, double ph)[] Waves = MakeWaves();
        static (double, double, double, double)[] MakeWaves()
        {
            var a = new (double, double, double, double)[190];
            for (int i = 0; i < 190; i++) a[i] = (Math.Pow(JsMath.Hsh(i, 1), 1.25), JsMath.Hsh(i, 2) * 1.2, JsMath.Hsh(i, 4), JsMath.Hsh(i, 5) * 6.28);
            return a;
        }

        static readonly Col StarC = Col.Hex("#e8f0ff"), Halo1 = Col.Hex("#bed2ff");
        static Bitmap cloudD, cloudN, cloudDh, cloudNh;

        public static double MoonX(int W) => W * 0.76;
        public static double MoonY(int oy) => -oy + (Horizon + oy) * 0.4;

        /// <summary>하늘 장식. dusk: 0 낮 ~ 1 밤 (낮·밤 요소를 dusk 비율로 섞는다).</summary>
        public static void DrawSkyDeco(CpuCanvas c, int W, int oy, double t, double dusk)
        {
            double top = -oy, sh = Horizon - top, nk = dusk, dk = 1 - dusk, moonX = MoonX(W), moonY = MoonY(oy);
            double drop = Math.Max(0, Math.Min(46, Horizon - 6 - moonY));
            Action<double, double, double, double, Col, double> Q = (x, y, w, h, col, a) => c.Q(x, JsMath.Round(y) + oy, w, h, col, a);
            if (nk > 0.01)
            {
                foreach (var s in Stars)
                {
                    double y = top + s.y * (sh - 10), tw = 0.55 + 0.45 * Math.Sin(t * (0.8 + s.s * 1.6) + s.ph);
                    double a = tw * (0.25 + 0.75 * (1 - s.y * 0.55)) * nk, sz = s.s > 0.85 ? 1 : 0.5;
                    Q(s.x * W, y, sz, sz, StarC, a);
                    if (s.s > 0.93 && tw > 0.85) { Q(s.x * W - 1, y + 0.25, 2.5, 0.25, StarC, a); Q(s.x * W + 0.25, y - 1, 0.5, 2, StarC, a); }
                }
                double my = moonY + drop * (1 - nk);
                Q(moonX - 16, my - 16, 32, 32, Halo1, 0.09 * nk); Q(moonX - 12, my - 12, 24, 24, Halo1, 0.12 * nk);
                Q(moonX - 8, my - 8, 16, 16, Col.Hex("#f1ebcc"), nk); Q(moonX + 4, my - 8, 4, 16, Col.Hex("#d8d0a8"), nk);
                Q(moonX - 8, my + 4, 16, 4, Col.Hex("#d8d0a8"), nk); Q(moonX - 8, my - 8, 16, 2, Col.Hex("#fbf6dc"), nk);
                foreach (var d in new[] { (-5, -4), (1, 0), (-3, 3) }) Q(moonX + d.Item1, my + d.Item2, 2, 2, Col.Hex("#c5bd92"), nk);
                double ph = (t % 34) / 34;                                                    // 가끔 스치는 별똥별
                if (ph < 0.045)
                {
                    double k = ph / 0.045, ep = Math.Floor(t / 34);
                    double sx0 = W * (0.15 + 0.5 * JsMath.Hsh((int)ep, 3)), sy0 = top + 12 + 40 * JsMath.Hsh((int)ep, 4);
                    for (int j = 0; j < 9; j++) Q(sx0 + (k * 46 - j * 2.2), sy0 + (k * 22 - j * 1.1), 1, 0.5, Col.Hex("#f4f8ff"), (1 - k) * (1 - j / 9.0) * nk);
                }
            }
            if (dk > 0.01)
            {
                double sy = moonY + drop * nk;
                var sun = Col.Hex("#fff0b8");
                Q(moonX - 20, sy - 20, 40, 40, sun, 0.12 * dk); Q(moonX - 14, sy - 14, 28, 28, sun, 0.16 * dk);
                Q(moonX - 8, sy - 8, 16, 16, Col.Hex("#fff4c0"), dk); Q(moonX - 6, sy - 6, 12, 12, Col.Hex("#fffbe2"), dk);
            }
            if (cloudD == null) { cloudD = BgBaker.Cloud(false); cloudN = BgBaker.Cloud(true); cloudDh = Half(cloudD); cloudNh = Half(cloudN); }
            foreach (var (night, k) in new[] { (false, dk * 0.95), (true, nk * 0.9) })
            {
                if (k < 0.01) continue;
                foreach (var cl in Clouds)
                {
                    double loop = W + 120, x = ((cl.x * loop + t * cl.sp) % loop) - 60;
                    bool big = cl.sc >= 0.75;
                    c.Put(night ? (big ? cloudN : cloudNh) : (big ? cloudD : cloudDh), JsMath.Round(x), JsMath.Round(top + 14 + cl.dy * (sh - 50)) + oy, k);
                }
            }
            if (dk > 0.01)
                for (int i = 0; i < 5; i++)                                                    // 낮에는 갈매기
                {
                    double loop = W + 60, x = ((t * (7 + i * 2) + i * 50) % loop) - 30, y = top + 24 + i * 19 + Math.Sin(t * 0.7 + i) * 4, f = Math.Sin(t * 5 + i * 2) > 0 ? 1 : -0.4;
                    var white = Col.Hex("#ffffff");
                    for (int k = 1; k <= 3; k++) { Q(x - k, y - k * 0.5 * f, 1, 0.5, white, dk); Q(x + k, y - k * 0.5 * f, 1, 0.5, white, dk); }
                    Q(x, y, 1, 0.5, Col.Hex("#e8eef6"), dk);
                }
        }

        /// <summary>0.5 배율(최근접): 홀수 열·행을 뽑는다 (canvas drawImage 의 imageSmoothing=false 와 같은 표본).</summary>
        static Bitmap Half(Bitmap b)
        {
            var o = new Bitmap(b.Ax / 2, b.Ay / 2, b.W / 2, b.H / 2);
            for (int y = 0; y < o.H; y++) for (int x = 0; x < o.W; x++) o.D[y * o.W + x] = b.D[(2 * y) * b.W + 2 * x];
            return o;
        }

        /// <summary>바다 물결 190개와 달빛(햇빛) 반짝임 46개.</summary>
        public static void DrawSea(CpuCanvas c, int W, int oy, double t, double dusk)
        {
            bool night = dusk >= 0.5;
            Col hi = Col.Hex(night ? "#4a7ab4" : "#d6f0fa"), lo = Col.Hex(night ? "#0a1a38" : "#2a78ae");
            double y0 = Horizon + 3, span = DesignH - y0, moonX = MoonX(W);
            Action<double, double, double, double, Col, double> Q = (x, y, w, h, col, a) => c.Q(x, JsMath.Round(y) + oy, w, h, col, a);
            foreach (var w in Waves)
            {
                double len = 2 * JsMath.Round((1.5 + w.d * 8) / 2) + 2, sp = 1.2 + w.d * 6, loop = W + 40;
                double x = ((w.x * loop + t * sp * (w.sp > 0.5 ? 1 : -0.6)) % loop + loop) % loop - 20;
                double y = JsMath.Round(y0 + w.d * span + (Math.Sin(t * 1.3 + w.ph) > 0.3 ? 1 : 0));
                double a = Math.Max(0.25, JsMath.Round((0.25 + 0.4 * Math.Max(0, Math.Sin(t * 0.8 + w.ph * 2))) * (0.5 + w.d * 0.5) * 4) / 4);
                Q(JsMath.Round(x / 2) * 2, y + 1, len, 1, lo, a); Q(JsMath.Round(x / 2) * 2 + 2, y, len - 2, 1, hi, a);
            }
            for (int i = 0; i < 46; i++)
            {
                double s = Math.Sin(t * (1.1 + JsMath.Hsh(i, 9)) + JsMath.Hsh(i, 8) * 40);
                if (s < 0.82) continue;
                double dd = Math.Pow(JsMath.Hsh(i, 7), 1.1), x = moonX + (JsMath.Hsh(i, 6) - 0.5) * (14 + 90 * dd);
                Q(JsMath.Round(x / 2) * 2, JsMath.Round(y0 + dd * span), i % 3 != 0 ? 2 : 4, 1, night ? Col.Hex("#dfeaff") : Col.Hex("#ffffff"), (s - 0.82) * 5);
            }
        }

        // ---- glow: 점을 흩뿌린 디더링 원(사각 3겹, 겹치면 알파 누적) 한 번 굽고 'lighter'(가산) 로 얹는다 ----
        sealed class GlowSpr { public int Ax, Ay, W; public int[] R, G, B, A; }   // premultiplied 8비트
        static readonly Dictionary<string, GlowSpr> glowCache = new Dictionary<string, GlowSpr>();

        static GlowSpr BakeGlow(int r, int cr, int cg, int cb)
        {
            int n = r * 2 + 2; var s = new GlowSpr { Ax = -r - 1, Ay = -r - 1, W = n, R = new int[n * n], G = new int[n * n], B = new int[n * n], A = new int[n * n] };
            int sa = (int)JsMath.Round(0.2 * 255);
            int pr = (int)JsMath.Round(cr * sa / 255.0), pg = (int)JsMath.Round(cg * sa / 255.0), pb = (int)JsMath.Round(cb * sa / 255.0);
            foreach (double k in new[] { 1, 0.66, 0.33 })
            {
                int h = (int)Math.Max(1, JsMath.Round(r * k));
                for (int y = -h; y < h; y++) for (int x = -h; x < h; x++)
                {
                    int bx = x - s.Ax, by = y - s.Ay; if (bx < 0 || by < 0 || bx >= n || by >= n) continue;
                    int i = by * n + bx;
                    s.R[i] = (int)JsMath.Round(pr + s.R[i] * (255 - sa) / 255.0); s.G[i] = (int)JsMath.Round(pg + s.G[i] * (255 - sa) / 255.0);
                    s.B[i] = (int)JsMath.Round(pb + s.B[i] * (255 - sa) / 255.0); s.A[i] = (int)JsMath.Round(sa + s.A[i] * (255 - sa) / 255.0);
                }
            }
            return s;
        }

        /// <summary>glow(x, y, r, col, a) — 'lighter' 합성(가산, 255 에서 멈춤). groupAlpha: 바깥에서 건 globalAlpha.</summary>
        public static void DrawGlow(CpuCanvas c, double x, double y, double r, int cr, int cg, int cb, double a, double groupAlpha = 1)
        {
            int ri = (int)Math.Max(2, JsMath.Round(r)); string key = ri + "|" + cr + "," + cg + "," + cb;
            if (!glowCache.TryGetValue(key, out var s)) glowCache[key] = s = BakeGlow(ri, cr, cg, cb);
            int ga8 = (int)JsMath.Round(Math.Min(1, a * 1.1) * groupAlpha * 255);
            int ox = (int)JsMath.Round(JsMath.Round(x) + s.Ax), oy = (int)JsMath.Round(JsMath.Round(y) + s.Ay);
            for (int j = 0; j < s.W; j++)
            {
                int dy = oy + j; if (dy < 0 || dy >= c.H) continue;
                for (int i = 0; i < s.W; i++)
                {
                    int dx = ox + i; if (dx < 0 || dx >= c.W) continue;
                    int k = j * s.W + i; if (s.A[k] == 0) continue;
                    uint d = c.D[dy * c.W + dx];
                    int nr = Math.Min(255, (int)(d & 255) + (int)JsMath.Round(s.R[k] * ga8 / 255.0));
                    int ng = Math.Min(255, (int)((d >> 8) & 255) + (int)JsMath.Round(s.G[k] * ga8 / 255.0));
                    int nb = Math.Min(255, (int)((d >> 16) & 255) + (int)JsMath.Round(s.B[k] * ga8 / 255.0));
                    c.D[dy * c.W + dx] = Bitmap.Pack(nr, ng, nb, 255);
                }
            }
        }

        /// <summary>'lighter' 로 사각형(창 불빛): 가산.</summary>
        public static void AddRect(CpuCanvas c, double x, double y, double w, double h, Col col, double ga)
        {
            int x0 = (int)JsMath.Round(x), y0 = (int)JsMath.Round(y), w0 = (int)Math.Max(1, JsMath.Round(w)), h0 = (int)Math.Max(1, JsMath.Round(h)), g8 = (int)JsMath.Round(ga * 255);
            for (int j = y0; j < y0 + h0; j++) for (int i = x0; i < x0 + w0; i++)
            {
                if (i < 0 || j < 0 || i >= c.W || j >= c.H) continue;
                uint d = c.D[j * c.W + i];
                Func<int, double, int> ad = (dst, cc) => Math.Min(255, dst + (int)JsMath.Round(cc * g8 / 255.0));
                c.D[j * c.W + i] = Bitmap.Pack(ad((int)(d & 255), col.R), ad((int)((d >> 8) & 255), col.G), ad((int)((d >> 16) & 255), col.B), 255);
            }
        }

        /// <summary>밤 불빛 한 벌(섬 불빛·창 불빛·램프 번짐). dusk &gt; 0.4 부터 알파 (dusk-0.4)/0.4 로 서서히 켜진다.</summary>
        public static void DrawLights(CpuCanvas c, SceneSprites s, double t, double dusk, int lensLv = 0)
        {
            if (!(dusk > 0.4)) return;
            double ga = Math.Min(1, (dusk - 0.4) / 0.4), cx = s.W / 2.0, oy = s.Oy;
            var P = TowerParams.ForStage(s.Stage);
            foreach (var l in s.Island.Lights) DrawGlow(c, cx + l[0], Terrain.CY + l[1] + oy, l[2] * 0.65, 255, 190, 90, 0.5, ga);
            foreach (var w in P.Win)
            {
                AddRect(c, cx + w.dx - 0.75, Terrain.CY - w.dz - 1.2 + oy, 1.5, 2.4, Col.Hex("#ffd890"), ga);
                DrawGlow(c, cx + w.dx, Terrain.CY - w.dz + oy, 4.5, 255, 190, 90, 0.3, ga);
            }
            double ly = Terrain.CY - ((P.Zt * Terrain.VZ + 0.5) + 1.5 + 6 / 2.0), br = 0.5 + 0.5 * Math.Sin(t * 1.3);
            DrawGlow(c, cx, ly + oy, 12 + Math.Min(4, lensLv), 255, 205, 115, 0.15 + 0.06 * br, ga);
            DrawGlow(c, cx, ly + oy, 5, 255, 240, 190, 0.42 + 0.1 * br, ga);
        }
    }

    /// <summary>
    /// 화면 맞춤 (HTML fit() 2234행) — 가깝게/넓게의 반 단계(0.5) 배율 포함. 설계 높이 320·최소 높이 225·최소 폭 150 규칙 그대로.
    /// Core 의 LogicalScreen 은 '보통' 정수 배율만 가지므로 Render 쪽에 같은 수식을 따로 둔다(Core 무수정).
    /// </summary>
    public readonly struct FitView
    {
        public enum View { Near, Normal, Wide }
        public readonly double Scale, DevW, DevH; public readonly int W, H, OY;

        public FitView(double devW, double devH, View view = View.Normal)
        {
            DevW = devW; DevH = devH;
            Func<double, int> scaleFor = vw => { int k = Math.Max(1, (int)Math.Floor(Math.Min(devW / vw, devH / (vw * 1.41)) + 0.3)); while (k > 1 && devH / k < 225) k--; return k; };
            Func<double, bool> roomFor = k => devH / k >= 215 && devW / k >= 150;
            double b = scaleFor(192), s;
            if (view == View.Near) s = roomFor(b + 1) && b + 1 <= b * 1.5 ? b + 1 : roomFor(b + 0.5) ? b + 0.5 : b;
            else if (view == View.Wide) s = b - 1 >= 1 && b - 1 >= b * 0.6 ? b - 1 : b - 0.5 >= 1 ? b - 0.5 : b;
            else s = b;
            Scale = s; W = (int)Math.Ceiling(devW / s); H = (int)Math.Ceiling(devH / s); OY = H - BgBaker.DesignH;
        }
    }
}
