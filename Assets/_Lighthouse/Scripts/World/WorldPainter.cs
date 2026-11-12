using System;
using System.Collections.Generic;
using Lighthouse.Core;
using Lighthouse.Render;

namespace Lighthouse.World
{
    /// <summary>
    /// 연출 그리기 (원본 frame() 5230~5301행의 그리기 부분): 알파로 얹는 것은 Overlay(Ov), 가산('lighter')으로 얹는 것은 검은 CpuCanvas(Add)에 그린다.
    /// 순수 C#이라 dotnet 에서 골든(HTML 프레임)과 대조할 수 있다. 좌표는 설계 좌표 + OY(= 원본 ctx.translate(0, OY)).
    /// 배·관리인 몸·섬 같은 스프라이트는 GPU(WorldView)가 그리고, 여기서는 그 위에 얹히는 효과만 그린다.
    /// </summary>
    public sealed partial class WorldPainter
    {
        public Overlay Ov { get; private set; } public CpuCanvas Add { get; private set; }
        Overlay co; CpuCanvas ca;                                 // 지금 그리는 층(기본 맨 위 층, A1 의 뒤 층을 그릴 때만 바뀐다)
        int W, H, OY;
        double beamPrevTh, beamPrevT, beamV;                    // 빛줄기 도는 빠르기 (잔상 길이)
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>();
        Bitmap fogTile;

        public WorldPainter(int w, int h, int oy) { Resize(w, h, oy); }

        public void Resize(int w, int h, int oy) { W = w; H = h; OY = oy; Ov = new Overlay(w, h); Add = new CpuCanvas(w, h); Ov.AddLayer = Add; co = Ov; ca = Add; ResizeLayers(); }

        static readonly Col CRing = Hx("#ffeeaf");
        static readonly Col[] SparkCols = { Hx("#ffe08a"), Hx("#ff9bb8"), Hx("#a8d4ff"), Hx("#8be096"), Hx("#ffffff") };
        static readonly Col[] RainbowCols = { Hx("#e0453e"), Hx("#f0a040"), Hx("#f4e060"), Hx("#6fd07a"), Hx("#5aa8e0"), Hx("#6a6ad0"), Hx("#9a60c0") };
        const double KY = Terrain.KY; const int Horizon = BgBaker.Horizon;

        /// <summary>진단 전용: true 면 A1 연출(뒤·중간 층, 비계, 생물, 갈매기 무리, 배 물보라·깃발·표식, 계절 입자)을 그리지 않는다 — 프레임 비용 증가분을 재는 데만 쓴다.</summary>
        public static bool A1Off;

        /// <summary>한 프레임의 연출 층을 새로 그린다(Ov·Add 를 비우고 시작).</summary>
        public void Paint(GameLoop L, SceneSprites s)
        {
            Ov.Clear(); Add.ClearAll();
            double t = L.T, dusk = L.Dusk; bool night = L.Ctx.Night; int stage = L.Ctx.Stage; double cx = W / 2.0, ly = GameLoop.LampY(stage); long nowMs = L.Ctx.Clock.NowMs;
            bool a1 = !A1Off;                                                                             // 진단 전용(실측 비교): A1 연출을 끄면 M4 상태와 같다
            if (a1) PaintScenery(L);                                                                      // A1 뒤 층·중간 층(먼 섬·표류물·불빛 번짐·부이·뒤쪽 배의 물보라)
            PaintRings(L);
            PaintDolphins(L);
            PaintRainbow(L, nowMs);
            PaintReefSpray(L, t);
            if (a1) PaintGulls(L, t);
            PaintKeeperAct(L, t);
            if (a1 && L.Ctx.S.Build != null) PaintScaffold(L, t);                                         // 공사 중인 탑의 비계
            if (a1) foreach (var sh in L.Ships) if (!IsBackLane(sh.Lane)) PaintShipBody(L, sh, t);        // 앞쪽 항로 배의 물보라·선원
            if (a1) { PaintLife(L, t); PaintShipMarks(L, t); }
            PaintDecoMarkers(L, t);
            PaintBirds(L, night); PaintSparks(L); PaintTwinkles(L); PaintPops(L);
            var style = BeamStyleOf(L.Ctx);
            if (!night && (L.Aiming || L.Spin > 0.05)) DrawBeam(L, L.BeamTh, cx, ly, L.Aiming ? 0.8 : Math.Min(1, L.Spin) * 0.8, 1, style);       // 낮에도 누르거나 종을 울리면 빛이 나온다
            if (dusk > 0.4)                                                                               // 해가 지면서 불빛이 서서히 켜진다
            {
                double ga = Math.Min(1, (dusk - 0.4) / 0.4);
                if (s != null) Fx.DrawLights(Add, s, t, dusk, L.Ctx.LvlOf("lens"));
                DrawBeam(L, L.BeamTh, cx, ly, 1, ga, style);
                double gp = (t * 0.4) % 1;                                                                 // 가끔 작은 별빛이 한 번 반짝인다
                if (gp < 0.22)
                {
                    double ga2 = ga * 0.75 * Math.Sin(gp / 0.22 * Math.PI); var c = Hx("#fff2c0");
                    for (int k = 2; k <= 4; k++) { AddQ(cx + k, ly, 1, 1, c, ga2); AddQ(cx - k - 1, ly, 1, 1, c, ga2); AddQ(cx, ly - k, 1, 1, c, ga2); AddQ(cx, ly + k + 0.5, 1, 1, c, ga2); }
                }
            }
            if (a1) PaintSeasonFx(L, t);
            PaintDuskTint(L, dusk, nowMs);
            PaintWeather(L, t);
            PaintRoleEffects(L, cx, ly);
            if (L.Flash > 0) PaintLampPulse(L.Flash, cx, ly);
        }

        /// <summary>알파 층 + 가산 층을 premultiplied RGBA 한 장으로.</summary>
        public void Merge(uint[] dst, bool flipY = false) { Ov.Merge(Add, dst, flipY); }

        // ----- 도우미 -----
        void AddQ(double x, double y, double w, double h, Col c, double ga) => Fx.AddRect(ca, x, y + OY, w, h, c, ga);
        void OvQ(double x, double y, double w, double h, Col c, double ga = 1) => co.Q(x, y + OY, w, h, c, ga);

        Bitmap Icon(string id) { if (!icons.TryGetValue(id, out var b)) icons[id] = b = IconBaker.Bake(id); return b; }

        /// <summary>HTML ellipse(): 줄마다 사각형 한 줄씩.</summary>
        void OvEllipse(double cx, double cy, double rx, double ry, Col c, double alpha, double ga = 1)
        {
            int n = (int)Math.Max(1, JsMath.Round(ry)); double y0 = JsMath.Round(cy);
            for (int i = -n; i < n; i++)
            {
                double v = (i + 0.5) / n, w = rx * Math.Sqrt(1 - v * v);
                co.FillRect((int)JsMath.Round(cx - w), (int)(y0 + i) + OY, (int)JsMath.Round(w * 2), 1, c, alpha, ga);
            }
        }

        void AddEllipse(double cx, double cy, double rx, double ry, Col c, double alpha)
        {
            int n = (int)Math.Max(1, JsMath.Round(ry)); double y0 = JsMath.Round(cy);
            for (int i = -n; i < n; i++)
            {
                double v = (i + 0.5) / n, w = rx * Math.Sqrt(1 - v * v); int ww = (int)JsMath.Round(w * 2);
                if (ww > 0) Fx.AddRect(ca, JsMath.Round(cx - w), y0 + i + OY, ww, 1, c, alpha);
            }
        }

        // ----- 빛줄기 (원본 2152~2190행) -----
        public sealed class BeamStyle { public bool Rainbow; public Col Rgb = new Col(255, 232, 160); }

        public static BeamStyle BeamStyleOf(GameContext c)
        {
            foreach (var bc in c.T.BeamColors)
            {
                if (bc.Id != c.S.Beam) continue;
                bool usable = bc.Src == "iap" ? c.S.Owned["beam:" + bc.Id] != null && Js.Truthy(c.S.Owned["beam:" + bc.Id]) : c.LvlOf("lens") >= bc.Lens;
                if (usable) return ParseBeam(bc);
                break;
            }
            return ParseBeam(c.T.BeamColors[0]);
        }

        static BeamStyle ParseBeam(BeamColorDef bc)
        {
            if (bc.Id == "rainbow" || bc.Rgb == null) return new BeamStyle { Rainbow = true };
            var p = bc.Rgb.Split(','); return new BeamStyle { Rgb = new Col(double.Parse(p[0]), double.Parse(p[1]), double.Parse(p[2])) };
        }

        /// <summary>hslRGB(h, s, l) → 0~255 반올림 색.</summary>
        public static Col HslRgb(double h, double s, double l)
        {
            double a = s * Math.Min(l, 1 - l);
            Func<double, double> f = n => { double k = (n + h / 30) % 12; return l - a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)); };
            return new Col(JsMath.Round(f(0) * 255), JsMath.Round(f(8) * 255), JsMath.Round(f(4) * 255));
        }

        struct Cell { public int Gx, Gy, Seg; public double A; }
        readonly Dictionary<int, Cell> cells = new Dictionary<int, Cell>();
        readonly Dictionary<long, List<Cell>> groups = new Dictionary<long, List<Cell>>();

        Col SegRgb(GameLoop L, BeamStyle st, int seg) => st.Rainbow ? HslRgb((L.Perf / 30 + seg * 22) % 360, 0.9, 0.78) : st.Rgb;

        void BeamCone(GameLoop L, double lx, double ly, double th, double am, double wsc, int N, double ga, BeamStyle st)
        {
            const double Lg = 190; double c = Math.Cos(th), s = Math.Sin(th), dy = s * KY * 0.55, len = Math.Sqrt(c * c + dy * dy), nx = -dy / len, ny = c / len;
            double w = (7 + 15 * (0.5 + 0.5 * s) + 1.6 * Math.Min(6, L.Ctx.LvlOf("lens"))) * wsc, dirA = (s > 0 ? 1.25 : 0.85) * 1.3;
            const int M = 96; cells.Clear();
            for (int i = 0; i < M; i++)
            {
                double d = (i + 0.5) / M, f = Math.Ceiling(Math.Pow(1 - d, 1.25) * 6) / 6, ww = 1.2 + w * (0.1 + 0.9 * d), cx0 = lx + c * Lg * d, cy0 = ly + dy * Lg * d; int seg = (int)Math.Min(N - 1, Math.Floor(d * N));
                for (double j = -ww; j <= ww; j += 1.5)
                {
                    double k = Math.Abs(j) / ww, al = (k < 0.3 && d < 0.62 ? 0.365 : k < 0.62 && d < 0.85 ? 0.195 : 0.085) * f;
                    int gx = (int)JsMath.Round((cx0 + nx * j) / 2), gy = (int)JsMath.Round((cy0 + ny * j) / 2), key = gx * 8192 + gy;
                    if (!cells.TryGetValue(key, out var o) || o.A < al) cells[key] = new Cell { Gx = gx, Gy = gy, A = al, Seg = seg };
                }
            }
            foreach (var g in groups.Values) g.Clear();
            foreach (var o in cells.Values)
            {
                int q = (int)Math.Max(1, JsMath.Round(o.A * am * dirA * 40));
                long key = ((long)(st.Rainbow ? o.Seg : 0) << 16) | (uint)q;
                if (!groups.TryGetValue(key, out var lst)) groups[key] = lst = new List<Cell>();
                lst.Add(o);
            }
            foreach (var kv in groups)
            {
                if (kv.Value.Count == 0) continue;
                int q = (int)(kv.Key & 0xFFFF); int seg = (int)(kv.Key >> 16);
                var col = SegRgb(L, st, seg);
                foreach (var o in kv.Value) Fx.AddRect(Add, o.Gx * 2, o.Gy * 2 + OY, 2, 2, col, (q / 40.0) * ga);
            }
        }

        public void SetBeamPrev(double th, double t, double v) { beamPrevTh = th; beamPrevT = t; beamV = v; }
        public void Clear() { Ov.Clear(); Add.ClearAll(); }
        public void DrawBeam(GameLoop L, double th0, double lx, double ly, double k0, double ga, BeamStyle st)
        {
            double now = L.Perf;
            if (now - beamPrevT > 5) { beamV += (WrapPiJs(th0 - beamPrevTh) / Math.Max(0.001, (now - beamPrevT) / 1000) - beamV) * 0.25; beamPrevTh = th0; beamPrevT = now; }
            double off = Math.Min(0.3, Math.Max(0, beamV) * 0.3);       // 돌고 있을 때만 뒤에 옅은 잔상이 따른다
            var rgb = st.Rainbow ? new Col(255, 232, 160) : st.Rgb;
            for (int tk = 0; tk < 2; tk++)
            {
                double th = tk == 0 ? th0 : th0 + Math.PI;
                BeamCone(L, lx, ly, th, k0, 1, 16, ga, st);
                if (off > 0.03) { BeamCone(L, lx, ly, th - off * 0.55, k0 * 0.4, 1, 10, ga, st); BeamCone(L, lx, ly, th - off * 1.1, k0 * 0.16, 0.95, 8, ga, st); }
                double c = Math.Cos(th), s = Math.Sin(th);
                foreach (double d in SmudgeD)        // 빛줄기가 훑고 지나가는 바다 위의 밝은 얼룩
                {
                    double px = lx + c * d, py = Terrain.CY + Terrain.IsleY + s * d * KY, a3 = (s > 0 ? 0.2 : 0.12) * (1 - d / 200) * k0, r = 20 + d * 0.14;
                    for (int q = 3; q >= 1; q--) AddEllipse(JsMath.Round(px), JsMath.Round(py), r * q / 3, r * q / 3 * KY * 1.1, rgb, (a3 / 2.4) * ga);
                }
            }
        }

        static readonly double[] SmudgeD = { 48.0, 92.0, 140.0 };
        public static double WrapPiJs(double a) => ((a + Math.PI * 3) % (Math.PI * 2)) - Math.PI;

        // ----- 물결·돌고래·무지개·암초 물보라 -----
        public void PaintRings(GameLoop L)
        {
            foreach (var r in L.Rings)
            {
                double rad = (5 + r.Age * 15) * r.S, ga = 1 - r.Age / 2.4;
                for (double a = 0; a < 6.28; a += 0.35) OvQ(r.X + Math.Cos(a) * rad, r.Y + Math.Sin(a) * rad * KY, 1, 1, CRing, ga);
            }
        }

        static readonly (double lag, double sz, string col)[] DolphinBody = { (0, 3, "#7a9bbd"), (0.05, 3, "#6a8aac"), (0.1, 3, "#6a8aac"), (0.15, 2, "#5a7a9c"), (0.2, 2, "#5a7a9c"), (0.26, 1, "#4a6a8c") };
        public void PaintDolphins(GameLoop L)
        {

            foreach (var d in L.Dolphins)
            {
                if (d.T < 0) continue;
                foreach (var b in DolphinBody)
                {
                    var (x, y, up) = GameLoop.DolphinAt(d, d.T - b.lag);
                    if (!up) continue;
                    OvQ(x - b.sz / 2, y - b.sz / 2, b.sz, b.sz, Hx(b.col));
                    if (b.lag == 0) OvQ(x + d.Dir * 1, y, 1, 1, Hx("#e8f4ff"));
                    if (b.lag == 0.1) OvQ(x, y - 2, 1, 1, Hx("#4a6a8c"));
                }
            }
        }

        public void PaintRainbow(GameLoop L, long nowMs)
        {
            if (L.Ctx.Night || nowMs >= L.RainbowUntilMs) return;
            double fade = Math.Min(1, Math.Min((L.RainbowUntilMs - nowMs) / 5000.0, (nowMs - (L.RainbowUntilMs - 45000)) / 3000.0)), rcx = W * 0.3, rcy = BgBaker.Horizon + 4;
            double ga = 0.5 * Math.Max(0, fade);
            for (int i = 0; i < RainbowCols.Length; i++)
            {
                double r = 80 - i * 2.2;
                for (double a = Math.PI; a <= Math.PI * 2; a += 0.9 / r) OvQ(rcx + Math.Cos(a) * r, rcy + Math.Sin(a) * r * 0.9, 1, 2, RainbowCols[i], ga);
            }
        }

        void PaintReefSpray(GameLoop L, double t)
        {
            if (L.Ctx.Tier() < 2) return;
            var c = Hx("#e6f4fa");
            foreach (var reef in Lanes.Reefs)
            {
                double x = JsMath.Round(W * reef[0]), ry = reef[1];
                for (int i = 0; i < 3; i++) OvQ(x - 7 + i * 7 + Math.Sin(t + i) * 2, ry + 1.5, 3, 1, c, 0.5 + 0.4 * Math.Sin(t * 2 + i));     // 암초에 부서지는 물보라
            }
        }

        /// <summary>움직임 줄이기: 켜면 화살표가 위아래로 까딱이지 않는다(원본 REDUCE_MOTION). 설정이 연결되면 화면 쪽이 채운다.</summary>
        public bool ReduceMotion;

        /// <summary>꾸미기 자리 표시(원본 drawDecorMarkers 3903행): 빈 자리·고른 자리는 점선 마름모, 자리마다 번호 배지, 고른 자리는 노란 화살표.</summary>
        public void PaintDecoMarkers(GameLoop L, double t)
        {
            var shade = new Col(10, 20, 50);
            foreach (var m in L.Markers)
            {
                double ry = m.Rx / 2; var col = Hx(m.Sel ? "#ffd93a" : "#ffffff");
                if (!m.Full || m.Sel)
                {
                    double ga = m.Sel ? 1 : 0.85;
                    for (int k = 0; k < 24; k++)
                    {
                        if (!m.Sel && k % 2 != 0) continue;
                        double th = k / 24.0 * Math.PI * 2, px = JsMath.Round(m.X + Math.Cos(th) * m.Rx), py = JsMath.Round(m.Y - 2 + Math.Sin(th) * ry);
                        OvQ(px + 1, py + 1, 1, 1, shade, 0.7 * ga); OvQ(px, py, 1, 1, col, ga);
                    }
                }
                PaintDecoBadge(m);                                                                  // 자리 번호 배지
                if (m.Sel)
                {
                    double bob = ReduceMotion ? 0 : JsMath.Round(Math.Sin(t * 5) * 1.2), ay = m.Y - 22 + bob;
                    for (int k = 0; k < 4; k++) OvQ(m.X - 3 + k, ay + k, 7 - k * 2, 1, Hx("#ffd93a"));
                    OvQ(m.X - 3, ay - 1, 7, 1, shade, 0.7);
                }
            }
        }

        // ----- 새·불꽃·반짝임·선물 -----
        public void PaintBirds(GameLoop L, bool night)
        {
            var col = Hx(night ? "#cfd8f0" : "#ffffff");
            foreach (var b in L.Birds)
            {
                int f = (int)(Math.Floor(L.T * 7 + b.Ph) % 2); double ga = Math.Min(1, b.Life);
                OvQ(b.X - 2, b.Y - f, 2, 1, col, ga); OvQ(b.X, b.Y, 1, 1, col, ga); OvQ(b.X + 1, b.Y - f, 2, 1, col, ga);
            }
        }

        public void PaintSparks(GameLoop L) { foreach (var s in L.Sparks) { if (s.Delay > 0) continue; OvQ(s.X, s.Y, 1, 1, SparkCols[s.Col % SparkCols.Length], Math.Min(1, s.Life * 1.6)); } }

        public void PaintTwinkles(GameLoop L)
        {
            Col a = Hx("#ffe9a0"), b = Hx("#fff6cc");
            foreach (var f in L.Fx) { double ga = Math.Min(1, f.Life * 1.5); OvQ(f.X, f.Y, 1, 1, a, ga); OvQ(f.X - 0.5, f.Y + 0.25, 2, 0.5, b, ga); OvQ(f.X + 0.25, f.Y - 0.5, 0.5, 2, b, ga); }
        }

        public void PaintPops(GameLoop L)
        {
            foreach (var p in L.Pops)
            {
                var ic = Icon(p.Gift); double ga = Math.Min(1, (1.7 - p.Age) * 2.5);
                Ov.Put(ic, JsMath.Round(p.X - 4) - ic.Ax, JsMath.Round(p.Y - p.Age * 11 - 4) + OY - ic.Ay, ga);        // drawImage(iconCanvas, round(x-4), round(y-age*11-4)) — 아이콘은 왼쪽 위가 원점
            }
        }

        // ----- 해질녘·날씨·역할 -----
        public void PaintDuskTint(GameLoop L, double dusk, long nowMs)
        {
            double k = Math.Sin(Math.PI * dusk);
            if (k < 0.02) return;
            bool evening = Progress.DuskAt(L.Ctx.S.Clock, nowMs + 3000, L.Ctx.Clock, L.Ctx.CycleSec) >= dusk;
            if (evening) Ov.FillRect(0, 0, W, H, new Col(255, 130, 70), 0.2 * k); else Ov.FillRect(0, 0, W, H, new Col(255, 160, 150), 0.16 * k);
            Fx.DrawGlow(Add, Fx.MoonX(W), BgBaker.Horizon - 3 + OY, 70, evening ? 255 : 255, evening ? 140 : 170, evening ? 70 : 140, 0.5 * k);
        }

        static double Smooth(double t) => t * t * (3 - 2 * t);
        static double VNoise(double x, double y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y); double xf = Smooth(x - xi), yf = Smooth(y - yi);
            double a = JsMath.Hsh(xi, yi), b = JsMath.Hsh(xi + 1, yi), c = JsMath.Hsh(xi, yi + 1), d = JsMath.Hsh(xi + 1, yi + 1);
            return a + (b - a) * xf + (c - a) * yf + (a - b - c + d) * xf * yf;
        }
        static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        /// <summary>가로로 이어지는 안개 무늬(디더링) 한 번 굽기 (원본 bakeFog 4128행).</summary>
        static Bitmap BakeFog()
        {
            int w = 64, h = (int)Math.Ceiling((double)BgBaker.DesignH - BgBaker.Horizon + 14); var b = new Bitmap(0, 0, w, h);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int xb = x & ~1, yb = y & ~1; double a = (double)xb / w, n = (1 - a) * VNoise(xb * 0.06, yb * 0.1) + a * VNoise((xb - w) * 0.06, yb * 0.1);
                if (Math.Max(0, Math.Min(1, n * 1.25 - 0.1 + (double)yb / h * 0.1)) > (Bayer[((yb >> 1) & 3) * 4 + ((xb >> 1) & 3)] + 0.5) / 16) b.D[y * w + x] = Bitmap.Pack(228, 234, 242, 255);
            }
            return b;
        }

        public void PaintWeather(GameLoop L, double t)
        {
            var wx = L.Wx; if (wx.Amt < 0.01) return;
            if (wx.Kind == "fog")
            {
                if (fogTile == null) fogTile = BakeFog();
                Ov.FillRect(0, (int)(BgBaker.Horizon - 10) + OY, W, (int)(BgBaker.DesignH - BgBaker.Horizon + 10), Hx("#cfd9e6"), 1, wx.Amt * 0.2);
                double off = (t * 5) % 64; int ox = -(int)Math.Floor(off + 0.5 - 1e-9), oy0       /* 원본: 소수 이동은 가장 가까운 칸으로(실측: .2·.3 내림, .55·.7·.85·.9 올림), 정확히 .5 는 내림(원본은 열에 따라 섞음) */  = (int)(BgBaker.Horizon - 14) + OY; double ga = wx.Amt * 0.75;
                for (int x0 = ox; x0 < W; x0 += 64) Ov.Put(fogTile, x0, oy0, ga);        // 패턴 채우기: 가로로 타일을 이어 붙인다
            }
            else
            {
                bool snow = L.Ctx.CurSeason() == "winter";
                Ov.FillRect(0, 0, W, H, Hx(snow ? "#8fa0b8" : "#0a1426"), 1, wx.Amt * (snow ? 0.12 : 0.3));
                double ga = wx.Amt * 0.8; var snowC = Hx("#ffffff"); var rainC = Hx("#bcd6f0");
                for (int i = 0; i < 80; i++)
                {
                    double dx = JsMath.Hsh(i, 201), dy = JsMath.Hsh(i, 202), sp = 0.8 + JsMath.Hsh(i, 203) * 0.6;
                    double y = (dy * (H + 30) + t * (snow ? 24 : 130) * sp) % (H + 30) - 10;       // (원본은 -OY 한 뒤 translate(OY) 로 되돌린다: 화면 좌표 그대로)
                    double x = ((dx * (W + 40) - t * (snow ? 6 + 8 * Math.Sin(t * 0.7 + dy * 9) : 20) * sp) % (W + 40) + W + 40) % (W + 40) - 10;
                    if (snow) Ov.Q(JsMath.Round(x / 2) * 2, JsMath.Round(y / 2) * 2, 2, 2, snowC, ga); else { Ov.Q(x, y, 1, 3, rainC, ga); Ov.Q(x - 1, y + 3, 1, 1, rainC, ga); }
                }
            }
        }

        public void PaintRoleEffects(GameLoop L, double cx, double ly)
        {
            double now = L.Perf;
            if (now < L.WarnUntil)
            {
                bool on = Math.Floor(now / 140) % 2 == 0;
                if (on) { Fx.DrawGlow(Add, cx, ly + OY, 8, 255, 80, 70, 0.5); Fx.DrawGlow(Add, L.WarnX, L.WarnY - 6 + OY, 8, 255, 60, 50, 0.6); OvQ(L.WarnX - 1, L.WarnY - 8, 2, 2, Hx("#ff5a4a")); }
            }
            if (now < L.SignalUntil)                                  // 신호: 짧게 세 번, 쉬었다가 다시 세 번 작게 깜빡인다
            {
                int slot = (int)Math.Floor((now - (L.SignalUntil - 2600)) / 190); int[] pat = { 1, 0, 1, 0, 1, 0, 0, 0, 1, 0, 1, 0, 1 };
                if (slot >= 0 && slot < pat.Length && pat[slot] == 1)
                {
                    Fx.DrawGlow(Add, cx, ly + OY, 8, 255, 245, 200, 0.45); var c = Hx("#fff2c0");
                    for (int k = 2; k <= 4; k++) { OvQ(cx + k, ly, 1, 1, c, 0.8); OvQ(cx - k - 1, ly, 1, 1, c, 0.8); OvQ(cx, ly - k, 1, 1, c, 0.8); OvQ(cx, ly + k + 0.5, 1, 1, c, 0.8); }
                }
            }
            var sc = Hx("#dfe9ff");
            foreach (var s in L.Sirens)
            {
                double r = s.Age * 46, ga = Math.Max(0, 1 - s.Age / 2.2) * 0.85;
                for (double a = 0; a < 6.28; a += 0.5 / Math.Max(1, r) * 6) OvQ(cx + Math.Cos(a) * r, ly + Math.Sin(a) * r * 0.35, 1, 1, sc, ga);
            }
        }

        /// <summary>등대를 누르면 램프가 은은하게 반짝이고 빛 고리가 퍼진다 (원본 5390행).</summary>
        public void PaintLampPulse(double flash, double cx, double ly)
        {
            double p = 1 - flash, e = Math.Min(1, p / 0.12) * Math.Pow(flash, 1.5);
            Fx.DrawGlow(Add, cx, ly + OY, 6 + 4 * p, 255, 240, 190, 0.26 * e);
            double L = 3 + 4 * Math.Min(1, p / 0.3); Col a = Hx("#fff2c0"), b = Hx("#ffe29a");
            for (int k = 1; k <= L; k++)
            {
                double al = 1 - k / (L + 1), ga = 0.85 * e * al;
                AddQ(cx + k, ly, 1, 1, a, ga); AddQ(cx - k - 1, ly, 1, 1, a, ga); AddQ(cx, ly - k, 1, 1, a, ga); AddQ(cx, ly + k + 0.5, 1, 1, a, ga);
                if (k <= L * 0.55) { AddQ(cx + k, ly - k, 1, 1, b, ga); AddQ(cx - k, ly - k, 1, 1, b, ga); AddQ(cx + k, ly + k, 1, 1, b, ga); AddQ(cx - k, ly + k, 1, 1, b, ga); }
            }
            AddQ(cx - 1, ly - 1, 2, 2, Hx("#ffffff"), 1);
            double R = 3 + 12 * (1 - Math.Pow(flash, 2)); var rc = Hx("#ffe9a8");
            for (int i = 0; i < 24; i++) { double ang = i / 24.0 * Math.PI * 2; if ((i & 1) != 0 && R > 8) continue; AddQ(cx + Math.Cos(ang) * R, ly + Math.Sin(ang) * R * 0.8, 1, 1, rc, 0.5 * Math.Pow(flash, 1.2)); }
        }

        // ----- 관리인의 일하는 손동작 (원본 drawKeeperAct 4431행): 몸은 GPU 스프라이트, 손·도구·불빛만 여기서 -----
        void PaintKeeperAct(GameLoop L, double t)
        {
            var k = L.Keeper; var kp = L.KeeperPos();
            PaintKeeperActAt(t, kp.x, kp.y, k.Act, k.Dir, k.Walking, k.WaveT);
        }

        /// <summary>관리인 손동작을 (x, y0) 에 선 관리인 기준으로 그린다. 시험이 같은 값으로 HTML drawKeeperAct 와 대조한다.</summary>
        public void PaintKeeperActAt(double t, double x, double y0, string kact, int kdir, bool kwalking, double kwave)
        {
            if (kact == "enter" && !kwalking && kwave <= 0) return;
            bool sit = kact == "sit" && !kwalking && kwave <= 0; double y = sit ? y0 + 3 : y0;
            double dr = kdir, hx = dr > 0 ? x + 3 : x - 4, hy = y - 6; Col skin = Hx("#f0c8a0"); int tick = (int)(Math.Floor(t * 5) % 2);
            string act = kwave > 0 ? "wave" : kwalking ? null : kact;
            Action<double, double, double, double, Col, double> Q = (qx, qy, qw, qh, qc, ga) => OvQ(qx, qy, qw, qh, qc, ga);
            Action<double, double, double, double, string> Qh = (qx, qy, qw, qh, hex) => OvQ(qx, qy, qw, qh, Hx(hex), 1);
            Action<double, double, double, int, int, int, double> G = (gx, gy, gr, cr, cg, cb, ga) => Fx.DrawGlow(Add, gx, gy + OY, gr, cr, cg, cb, ga);
            if (act == "wave") { Q(hx, hy - 3 - (tick != 0 ? 1 : 0), 1, 2, skin, 1); Q(hx + (tick != 0 ? dr : -dr), hy - 5 - (tick != 0 ? 1 : 0), 1, 1, skin, 1); }
            else if (act == "sweep")
            {
                double sw = Math.Sin(t * 5) * 2.2; Q(hx, hy, 1, 1, skin, 1);
                for (int j = 0; j <= 5; j++) Qh(hx + dr * (j * 0.6 + sw * j / 5), hy + j, 1, 1, "#d8b878");
                double ex = hx + dr * (3 + sw); Qh(ex - 1, y - 1, 3, 2, "#d8b860"); Qh(ex - 1, y, 3, 1, "#a88838");
                double p = (t * 6) % 4; if (Math.Abs(sw) > 1.4) Q(ex + dr * (2 + p), y - 1 - p * 0.6, 1, 1, new Col(225, 215, 185), 0.7);
            }
            else if (act == "polish" || act == "anchor" || act == "gild")
            {
                int up = (int)(Math.Floor(t * 4) % 2); Q(hx, hy - 2, 1, 2, skin, 1); Qh(hx - (dr < 0 ? 1 : 0), hy - 4 - up, 2, 2, "#ffffff");
                if (Math.Floor(t * 3) % 2 != 0) { double sx = x + dr * 7, sy = y - 11 - (Math.Floor(t * 2) % 3) * 2; Qh(sx, sy, 1, 1, "#fff6cc"); Q(sx - 1, sy, 3, 1, new Col(255, 246, 204), 0.6); Q(sx, sy - 1, 1, 3, new Col(255, 246, 204), 0.6); }
            }
            else if (act == "mail") { Q(hx, hy - 2, 1, 2, skin, 1); if (Math.Floor(t * 1.5) % 2 != 0) { var ic = Icon("mail8"); Ov.Put(ic, x - 4 - ic.Ax, y - 27 + OY - ic.Ay, 1); } }
            else if (act == "water")
            {
                double cx0 = dr > 0 ? hx + 1 : hx - 3; Q(hx, hy, 1, 1, skin, 1);
                Qh(cx0, hy, 3, 2, "#6a9bd0"); Qh(cx0, hy - 1, 3, 1, "#8ab8e0"); Qh(dr > 0 ? cx0 + 3 : cx0 - 1, hy + 1, 1, 1, "#6a9bd0"); Qh(dr > 0 ? cx0 + 4 : cx0 - 2, hy + 2, 1, 1, "#6a9bd0");
                for (int j = 0; j < 3; j++) { double ph = (t * 5 + j * 1.3) % 3; Qh(hx + dr * (6 + ph * 0.6), hy + 3 + ph * 1.6, 1, 1, "#a8d4ff"); }
            }
            else if (act == "fish")
            {
                Q(hx, hy, 1, 1, skin, 1);
                for (int j = 0; j <= 9; j++) Qh(hx + dr * j, hy - j * 0.65, 1, 1, "#7a5a38");
                double tx = hx + dr * 9, ty = hy - 6, by = y + 4 + Math.Floor(t * 1.5) % 2;
                for (double yy = ty; yy < by; yy += 1) if (((int)yy & 1) == 0) Q(tx, yy, 1, 1, new Col(235, 245, 255), 0.85);
                Qh(tx - 1, by, 2, 1, "#e0453e"); Qh(tx - 1, by - 1, 2, 1, "#ffffff");
            }
            else if (act == "lamp") { int up = (int)(Math.Floor(t * 3) % 2); Q(hx, hy - 2 - up, 1, 3, skin, 1); Qh(hx + dr, hy - 5 - up, 2, 2, "#ffd36b"); G(hx + dr + 0.5, hy - 4 - up, 5, 255, 200, 110, 0.35); }
            else if (act == "well")
            {
                double ph = (t * 1.2) % 2, dn = ph < 1 ? ph : 2 - ph, bx = hx + dr * 3, by = JsMath.Round(hy - 4 + dn * 7); Q(hx, hy, 1, 1, skin, 1);
                for (double yy = hy - 7; yy < by; yy++) Qh(bx, yy, 1, 1, "#d8c8a0"); Qh(bx - 1, by, 3, 2, "#8a5a30"); Qh(bx - 1, by, 3, 1, "#b88a54");
            }
            else if (act == "flag") { int pull = (int)(Math.Floor(t * 3) % 2); Q(hx, hy - 2 - pull, 1, 2, skin, 1); Qh(hx + dr, hy - 6 - pull, 1, 5, "#e8e0c8"); }
            else if (act == "rest") { double lx = x - dr * 5; Qh(lx, y - 2, 2, 2, "#a85a20"); Qh(lx, y - 3, 2, 1, "#ffd890"); G(lx + 1, y - 2, 5, 255, 190, 90, 0.4 + 0.08 * Math.Sin(t * 3)); }
            else if (act == "feed") { int th = (int)(Math.Floor(t * 2.5) % 2); Q(hx, hy - 1 - th, 1, 1, skin, 1); Qh(hx + dr, hy - 2 - th, 1, 1, "#f2e2b0"); Qh(hx + dr, hy - 1 - th, 1, 1, "#d8c690"); }
            else if (act == "stars")
            {
                double bx = x + dr * 4;
                Qh(bx, y - 6, 1, 5, "#8a7660"); Qh(bx - 1, y - 2, 1, 2, "#8a7660"); Qh(bx + 1, y - 2, 1, 2, "#8a7660");
                for (int j = 0; j < 9; j++) Qh(bx + dr * j * 0.75 - (dr < 0 ? 1 : 0), y - 7 - j * 0.95, 2, 2, j > 6 ? "#f4eed0" : j > 1 ? "#d2b878" : "#a88c52");
                Q(hx, hy - 1, 1, 1, skin, 1);
                if (Math.Floor(t * 1.3) % 3 == 0) { double tx = bx + dr * 7, ty = y - 16; Qh(tx, ty, 1, 1, "#fff6cc"); Q(tx - 1, ty, 3, 1, new Col(255, 246, 204), 0.55); Q(tx, ty - 1, 1, 3, new Col(255, 246, 204), 0.55); }
                double lx = x - dr * 5; Qh(lx, y - 2, 2, 2, "#a85a20"); Qh(lx, y - 3, 2, 1, "#ffd890"); G(lx + 1, y - 2, 5, 255, 190, 90, 0.4 + 0.08 * Math.Sin(t * 3));
            }
        }
    }
}
