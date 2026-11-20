using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Render;

namespace Lighthouse.World
{
    /// <summary>
    /// A1 월드 연출 그리기 (원본 drawFarIslands 2072행·drawLife 2089행·waterWarmth 2124행·drawScaffold 3202행·drawBuoys 3589행·drawGulls 4338행·drawShipFlag 4216행·
    /// drawLantern 4224행·drawShipMarks 4241행·drawDrift 4558행·drawMeteors 5060행·drawAurora 5078행·drawSeasonFx 5089행·drawSailor 5117행·shipDraw 의 물보라 2061행·drawDecorMarkers 번호 배지).
    /// 층: 맨 위 층(Ov·Add)은 기존 그대로, 뒤 층 Back(먼 섬·표류물: 뒤쪽 항로 배 아래)과 중간 층 Mid(물가 뒤·섬 앞: 불빛 번짐·부이·뒤쪽 배의 물보라·선원)은 따로 구워 스프라이트 사이에 끼운다 —
    /// 원본이 한 캔버스에 순서대로 그리던 앞뒤를 GPU 스프라이트 정렬값으로 흉내 낸다. 좌표는 Z=1(도트 1칸) 기준이다.
    /// </summary>
    public sealed partial class WorldPainter
    {
        /// <summary>따로 굽는 연출 층 하나(알파 층 + 가산 층 → premultiplied 한 장).</summary>
        public sealed class SceneLayer
        {
            public Overlay Ov; public CpuCanvas Add; public uint[] Buf;
            public bool Changed, Used; public long Sig = long.MinValue;
        }
        public SceneLayer Back { get; private set; }
        public SceneLayer Mid { get; private set; }

        void ResizeLayers() { Back = NewLayer(); Mid = NewLayer(); }
        SceneLayer NewLayer() { var l = new SceneLayer { Ov = new Overlay(W, H), Add = new CpuCanvas(W, H), Buf = new uint[W * H] }; l.Ov.AddLayer = l.Add; return l; }

        void Begin(SceneLayer l) { l.Ov.Clear(); l.Add.ClearAll(); co = l.Ov; ca = l.Add; }
        void End(SceneLayer l, bool used) { co = Ov; ca = Add; l.Used = used; if (used) l.Ov.Merge(l.Add, l.Buf, true); l.Changed = true; }

        static Dictionary<string, Col> hexCache;
        /// <summary>Col.Hex 를 한 번만 읽는다(프레임마다 문자열을 자르면 쓰레기가 쌓인다). 정적 초기화 순서와 무관하게 늦게 만든다.</summary>
        static Col Hx(string hex)
        {
            if (hexCache == null) hexCache = new Dictionary<string, Col>();
            if (!hexCache.TryGetValue(hex, out var c)) hexCache[hex] = c = Col.Hex(hex);
            return c;
        }

        // 색 모음
        static readonly Col CWhite = Hx("#ffffff");
        static Col Shade(string hex, double f) { f = JsMath.Round(f * 50) / 50; return Col.Mul(Hx(hex), f); }

        // ================= 뒤 층·중간 층 (프레임마다 호출) =================
        /// <summary>뒤 층(먼 섬·표류물)과 중간 층(불빛 번짐·부이·뒤쪽 배의 물보라·선원)을 다시 그린다. 바뀐 게 없으면 건너뛴다(Changed 가 false).</summary>
        public void PaintScenery(GameLoop L)
        {
            double t = L.T; bool night = L.Ctx.Night;
            // 뒤 층: 먼 섬은 낮/밤 색과 먼 불빛 깜빡임만, 표류물은 칸이 바뀔 때만(위치·위아래 까딱·기울임·반짝임·흐려짐) 달라진다 → 신호가 같으면 다시 굽고 올리지 않는다
            bool on = night && Math.Sin(t * 1.5) > 0.2; long sig = (night ? 1L : 0L) | (on ? 2L : 0L) | ((long)W << 8) | ((long)(OY & 0xFFFF) << 24);
            foreach (var it in L.Drift)
            {
                double k = it.Take < 0 ? 0 : it.Take;
                sig = sig * 1000003 + (long)JsMath.Round(it.X); sig = sig * 31 + (long)(JsMath.Round(it.Y) + Math.Floor(t * 1.6 + it.Ph) % 2 + JsMath.Round(k * 3));
                sig = sig * 101 + (long)JsMath.Round(Math.Sin(t * 1.3 + it.Ph) * 0.18 * 80) + 50; sig = sig * 3 + (it.Take < 0 && Math.Floor(t * 2 + it.Ph) % 3 == 0 ? 1 : 0); sig = sig * 101 + (long)JsMath.Round(k * 20);
            }
            Back.Changed = false;
            if (sig != Back.Sig) { Begin(Back); PaintFarIslands(L, t); PaintDrift(L, t); End(Back, true); Back.Sig = sig; }
            // 중간 층: 불빛 번짐(해질녘 알파)·부이(깜빡임·까딱)·뒤쪽 항로 배(위치·물보라 칸·선원)가 바뀔 때만
            bool anyBack = false; double dusk = L.Dusk; int nb = BuoyCount(L); bool warm = dusk > 0.4;
            long msig = (night ? 1L : 0L) | (warm ? 2L : 0L) | ((long)nb << 4) | ((long)(warm ? (int)(Math.Min(1, (dusk - 0.4) / 0.4) * 255) : 0) << 12) | ((long)L.Ctx.Stage << 24) | ((long)W << 28) | ((long)(OY & 0xFFFF) << 40);
            for (int i = 0; i < Math.Min(14, nb); i++) msig = msig * 31 + (long)(Math.Floor(t * 1.4 + i * 2) % 2) * 2 + (long)(Math.Floor(t * 1.2 + i) % 2);
            foreach (var sh in L.Ships) if (IsBackLane(sh.Lane)) { anyBack = true; msig = ShipSig(sh, t, msig); }
            Mid.Changed = false;
            if (msig != Mid.Sig)
            {
                Begin(Mid);
                if (warm) { PaintWaterWarmth(L, Math.Min(1, (dusk - 0.4) / 0.4)); }
                PaintBuoys(L, t);
                foreach (var sh in L.Ships) if (IsBackLane(sh.Lane)) PaintShipBody(L, sh, t);
                End(Mid, anyBack || warm || nb > 0); Mid.Sig = msig;
            }
        }

        /// <summary>배 한 척의 물보라·선원이 그려지는 모양을 정하는 값들(칸으로 어림한 것)을 신호에 섞는다 — 같으면 같은 그림이다.</summary>
        long ShipSig(Ship sh, double t, long h)
        {
            double ls = Lanes.S[sh.Lane], ly = Lanes.Y[sh.Lane] + sh.Yo;
            h = h * 1000003 + sh.Type.GetHashCode(); h = h * 31 + sh.Lane * 3 + (sh.Dir > 0 ? 1 : 0); h = h * 1000003 + (long)JsMath.Round(sh.X);
            for (int k = 0; k < 6; k++) { double sn = Math.Sin(t * 3 + k) * 0.5; h = h * 7 + (long)(JsMath.Round(ly + 0.5 + sn) - JsMath.Round(ly)) + 1; h = h * 7 + (long)(JsMath.Round(ly + 1.8 - sn) - JsMath.Round(ly)) + 1; }
            h = h * 1000003 + (long)JsMath.Round(ly);
            if (sh.Wave > 0) { h = h * 1000003 + (long)JsMath.Round(sh.X - sh.Dir * (Lanes.HalfLen(sh.Type) - 9) * ls) + 7; h = h * 3 + (long)(Math.Floor(t * 6) % 2); h = h * 1000003 + (long)JsMath.Round(Lanes.Y[sh.Lane]); }
            return h;
        }

        static bool IsBackLane(int lane) => Lanes.Y[lane] < Terrain.CY - 20;

        // ================= 먼 섬 =================
        public void PaintFarIslands(GameLoop L, double t)
        {
            bool night = L.Ctx.Night; var col = Hx(night ? "#1a3050" : "#8fb0c8"); var hi = Hx(night ? "#27456b" : "#a9c6da"); double y = Horizon + 0.5;
            foreach (var f in FarIsles)                                                    // 수평선에 걸친 봉우리 모양 실루엣
            {
                double x = W * f.fx;
                for (int i = (int)JsMath.Round(-f.rx); i < (int)JsMath.Round(f.rx); i++)
                {
                    double u = (Math.Floor(i / 3.0) * 3 + 1.5) / f.rx, h = JsMath.Round(f.ry * (1 - u * u) * (0.75 + 0.25 * Math.Sin(u * 5 + f.fx * 20)) / 2) * 2;
                    OvQ(x + i, y - h, 1, h, col); OvQ(x + i, y - h, 1, Math.Min(h, 1), hi);
                }
            }
            if (night)                                                                      // 먼 곳의 작은 불빛
            {
                bool on = Math.Sin(t * 1.5) > 0.2;
                OvQ(W * 0.9 + 1, Horizon - 3, 1, 1, Hx(on ? "#ffe9a0" : "#8a7a50"));
                if (on) Fx.DrawGlow(ca, W * 0.9 + 1.5, Horizon - 2.5 + OY, 7, 255, 220, 130, 0.5);
            }
        }
        static readonly (double fx, double rx, double ry)[] FarIsles = { (0.14, 17, 6), (0.22, 9, 3.5), (0.9, 13, 5) };

        // ================= 밤 물가 불빛 번짐·부이 =================
        /// <summary>밤: 등대 불빛이 섬 둘레 물에 따뜻하게 번진다 (waterWarmth). ga: 해가 질수록 켜지는 알파.</summary>
        public void PaintWaterWarmth(GameLoop L, double ga)
        {
            double isleR = 8 * Terrain.N(L.Ctx.Stage), cx = JsMath.Round(W / 2.0), cy = JsMath.Round(Terrain.CY + Terrain.IsleY); var c = new Col(255, 175, 90);
            for (int q = 3; q >= 1; q--) AddEllipse(cx, cy, (isleR + 14) * q / 3, (isleR + 14) * q / 3 * Terrain.KY * 1.1, c, 0.035 * ga);
        }

        static readonly double[] BuoyTh = { 1.1, 2.1, -1.1, -2.1, 0.55, 2.65, -0.5, -2.65, 1.6, -1.6, 0.85, 2.4, -0.85, -2.4 };
        public static int BuoyCountFor(int portLv) { int pl = portLv; return Math.Min(4, pl) + (pl >= 8 ? 2 : 0) + (pl >= 12 ? 2 : 0) + (pl >= 16 ? 2 : 0) + (pl >= 20 ? 2 : 0) + (pl >= 23 ? 2 : 0); }      // 최대 14개 (Lv.20·23 에서 +2, D24)
        int BuoyCount(GameLoop L) => BuoyCountFor(L.Ctx.LvlOf("port"));

        public void PaintBuoys(GameLoop L, double t)
        {
            bool night = L.Ctx.Night; int nb = Math.Min(14, BuoyCount(L)); double isleR = 8 * Terrain.N(L.Ctx.Stage);
            var shadow = night ? new Col(0, 6, 24) : new Col(10, 50, 90); double sa = night ? 0.4 : 0.3;
            for (int i = 0; i < nb; i++)
            {
                double th = BuoyTh[i], r = isleR / (Math.Abs(Math.Cos(th)) + Math.Abs(Math.Sin(th))) * (i < 4 ? 1.42 : 1.62), x = JsMath.Round(W / 2.0 + Math.Cos(th) * r);
                double y = JsMath.Round(Terrain.CY + Terrain.IsleY + Math.Sin(th) * r * Terrain.KY + Math.Floor(t * 1.4 + i * 2) % 2); bool on = Math.Floor(t * 1.2 + i) % 2 == 0;
                OvEllipse(x, y + 2, 4, 1.2, shadow, sa);
                OvQ(x - 1, y - 4, 3, 2, Hx("#e8e6dc")); OvQ(x - 2, y - 2, 5, 2, Hx("#c9403a")); OvQ(x - 2, y, 5, 1, Hx("#e8e6dc")); OvQ(x, y - 7, 1, 3, Hx("#3a2a1a"));
                OvQ(x, y - 8, 1, 1, Hx(night && on ? "#ffe9a0" : "#9a8a60"));
                if (night && on) Fx.DrawGlow(ca, x + 0.5, y - 7.5 + OY, 5, 255, 220, 130, 0.5);
            }
        }

        // ================= 표류물 =================
        static readonly string[] Barrel = { "..hhhh..", ".bBBBBb.", ".bBBBBb.", "hhhhhhhh", "bBBBBBBb", "bBBBBBBb", "hhhhhhhh", ".bBBBBb.", "..bbbb.." };      // 조난품 오크통: 나무통에 쇠테
        static Bitmap barrelBmp;
        static Bitmap BarrelBmp()
        {
            if (barrelBmp != null) return barrelBmp;
            var pal = new Dictionary<char, Col> { { 'b', Hx("#6a4222") }, { 'B', Hx("#9a6a3a") }, { 'h', Hx("#4a4a52") } };
            var b = new Bitmap(0, 0, 8, 9);
            for (int y = 0; y < 9; y++) for (int x = 0; x < 8; x++) if (pal.TryGetValue(Barrel[y][x], out var c)) b.D[y * 8 + x] = Bitmap.Pack((int)c.R, (int)c.G, (int)c.B, 255);
            uint wood = Bitmap.Pack(0xc8, 0x90, 0x5a, 255);                                                       // 왼쪽 윗면 결
            b.D[1 * 8 + 2] = wood; b.D[2 * 8 + 2] = wood; b.D[4 * 8 + 2] = wood; b.D[5 * 8 + 2] = wood;
            return barrelBmp = b;
        }

        public void PaintDrift(GameLoop L, double t)
        {
            bool night = L.Ctx.Night; var shadow = night ? new Col(0, 6, 24) : new Col(10, 50, 90); double sa = night ? 0.4 : 0.3;
            foreach (var it in L.Drift)
            {
                double k = it.Take < 0 ? 0 : it.Take, a = 1 - JsMath.Round(k * 20) / 20;                           // k: 주운 뒤 흐려지는 정도(0~1)
                double x = JsMath.Round(it.X), y = JsMath.Round(it.Y) + Math.Floor(t * 1.6 + it.Ph) % 2 + JsMath.Round(k * 3);      // 흐려지며 스르르 가라앉는다
                OvEllipse(x, y + 3, 5, 1.6, shadow, sa, a);
                co.PutRotated(BarrelBmp(), x, y - 1 + OY, JsMath.Round(Math.Sin(t * 1.3 + it.Ph) * 0.18 * 80) / 80, -4, -5, a);      // 파도에 천천히 기우뚱
                if (it.Take < 0 && Math.Floor(t * 2 + it.Ph) % 3 == 0) { var c = Hx("#fff6cc"); OvQ(x + 4, y - 8, 1, 3, c); OvQ(x + 3, y - 7, 3, 1, c); }      // 반짝임: 눌러 주울 수 있다
            }
        }

        // ================= 공사 비계 =================
        /// <summary>공사 중인 탑: 비계, 튀는 불꽃, 날리는 먼지 (drawScaffold). 높이는 TOWER[STAGE] 기본 치수(외형과 무관).</summary>
        public void PaintScaffold(GameLoop L, double t)
        {
            double cx = W / 2.0, top = TowerParams.ForStage(L.Ctx.Stage).Zt * Terrain.VZ + 0.5 + 8, hw = 8 + 5, f = L.Ctx.Night ? 0.65 : 1;
            var wood = Shade("#a77a4a", f); var dark = Shade("#6a4a2c", f); double CY = Terrain.CY;
            foreach (int sx in new[] { -1, 1 }) OvQ(cx + sx * hw, CY - top, 1, top + 3, wood);
            for (double y = 10; y < top; y += 12) { OvQ(cx - hw, CY - y, hw * 2 + 1, 1, dark); OvQ(cx - hw, CY - y + 1, hw * 2 + 1, 1, wood); }
            for (double y = 10; y + 12 < top; y += 12) for (int s = 0; s < 12; s++) OvQ(cx - hw + s * hw * 2 / 12, CY - y - s, 1, 1, dark);
            var spark = Hx("#ffe08a");
            for (int i = 0; i < 3; i++)
            {
                int k = (int)Math.Floor(t * 7) + i * 5;
                if (JsMath.Hsh(k, 91) < 0.6) OvQ(cx + (JsMath.Hsh(k, 92) - 0.5) * hw * 2, CY - 6 - JsMath.Hsh(k, 93) * (top - 10), 1, 1, spark);
            }
            var dust = Hx("#e0d6c0");
            for (int i = 0; i < 5; i++)
            {
                double age = (t * 0.6 + i / 5.0) % 1;
                OvQ(cx + (JsMath.Hsh(i, 94) - 0.5) * 26 + age * 5, CY + 1 - age * 16, 2, 2, dust, (1 - age) * 0.7);
            }
        }

        // ================= 뛰어오르는 물고기·반딧불이·나비 =================
        static readonly Col[] ButterflyCols = { Hx("#fff5c0"), Hx("#ffb0d0"), Hx("#c8e0ff") };
        public void PaintLife(GameLoop L, double t)
        {
            double cx = W / 2.0, period = 6, jd = 0.1; int k = (int)Math.Floor(t / period); double ph = (t % period) / period;
            double x0 = W * (0.12 + 0.76 * JsMath.Hsh(k, 71)), y0 = JsMath.Hsh(k, 73) < 0.5 ? 172 + 22 * JsMath.Hsh(k, 72) : 278 + 30 * JsMath.Hsh(k, 72); int dir = JsMath.Hsh(k, 74) < 0.5 ? 1 : -1;
            bool night = L.Ctx.Night; var ripple = Hx("#e6f4fc");
            for (int j = 0; j < 2; j++)                                                    // 뛰어오를 때와 떨어질 때의 물결
            {
                double age = j == 0 ? ph * period : (ph - jd) * period, a0 = j == 0 ? 1 : 0.8;
                if (age < 0 || age > 1.6) continue;
                double rx = x0 + (j == 0 ? 0 : dir * 10), rad = 2 + age * 6, ga = (1 - age / 1.6) * a0 * (night ? 0.7 : 1);
                for (double a = 0; a < 6.28; a += 0.45) OvQ(rx + Math.Cos(a) * rad, y0 + 1 + Math.Sin(a) * rad * Terrain.KY, 1, 1, ripple, ga);
            }
            if (ph < jd)
            {
                double u = ph / jd, fx = x0 + dir * u * 10, fy = y0 - 4 * 11 * u * (1 - u);
                OvQ(fx - 1.5, fy, 3, 1, Hx("#d6ecf6")); OvQ(fx - 1, fy + 0.5, 2.5, 0.5, Hx("#8fb2c8")); OvQ(fx - dir * 2.5 - (dir > 0 ? 0 : -0.5), fy - 0.5, 1, 1.5, Hx("#aac8dc")); OvQ(fx + dir * 1, fy, 0.5, 0.5, Hx("#20303c"));
            }
            int garden = L.Ctx.EffLv("garden");
            if (night)                                                                      // 반딧불이: 섬 위 풀밭에서 깜빡인다
            {
                var ff = Hx("#fff1a0"); int n = 9 + 3 * garden;
                for (int i = 0; i < n; i++)
                {
                    double a = t * 0.22 * (0.6 + JsMath.Hsh(i, 81)) + i * 2.1, s = Math.Max(0, Math.Sin(t * (0.9 + JsMath.Hsh(i, 82)) + i * 3)), blink = s * s;
                    if (blink < 0.05) continue;
                    double x = cx + Math.Cos(a) * (22 + 20 * JsMath.Hsh(i, 83)), y = Terrain.CY - 4 - 22 * JsMath.Hsh(i, 84) + Math.Sin(a * 1.7) * 5;
                    AddQ(x, y, 1, 1, ff, blink); Fx.DrawGlow(ca, x + 0.5, y + 0.5 + OY, 3, 255, 230, 130, 0.4 * blink);
                }
            }
            else                                                                            // 나비: 섬 위를 천천히 날아다닌다
            {
                for (int i = 0; i < 3 + garden; i++)
                {
                    double x = cx + Math.Sin(t * 0.3 + i * 2.2) * 40, y = Terrain.CY - 12 - 11 * i + Math.Sin(t * 0.55 + i * 1.7) * 7, up = Math.Sin(t * 13 + i * 2) > 0 ? 1 : 0; var c = ButterflyCols[i % 3];
                    OvQ(x - 1.5, y - up, 1.5, 1, c); OvQ(x + 0.5, y - up, 1.5, 1, c); OvQ(x - 0.25, y, 0.5, 1, Hx("#3a2a1a"));
                }
            }
        }

        // ================= 계절 입자·등불·오로라·별똥별 =================
        public void PaintSeasonFx(GameLoop L, double t)
        {
            foreach (var m in L.Motes)                                                     // 벚꽃잎(분홍), 낙엽(주황·빨강), 눈송이(흰색)
            {
                int k = (int)Math.Floor(m.Ph * 3);
                if (m.S == "spring") { OvQ(m.X, m.Y, 2, 1, Hx(k == 0 ? "#f6b8cc" : k == 1 ? "#fbd8e2" : "#e89ab4")); OvQ(m.X + 1, m.Y + 1, 1, 1, Hx("#f6b8cc")); }
                else if (m.S == "autumn") { OvQ(m.X, m.Y, 2, 2, Hx(k == 0 ? "#e05a2a" : k == 1 ? "#f0a030" : "#c9402a")); OvQ(m.X + 1, m.Y, 1, 1, Hx("#8a3a14")); }
                else OvQ(m.X, m.Y, 1, 1, CWhite);
            }
            foreach (var l in L.Lanterns)                                                  // 하늘로 떠오르는 종이 등불
            {
                double x = JsMath.Round(l.X), y = JsMath.Round(l.Y); int fl = (int)(Math.Floor(t * 3 + l.Ph) % 2);
                OvQ(x, y, 3, 4, Hx(fl != 0 ? "#ffb85a" : "#ffa040")); OvQ(x, y, 3, 1, Hx("#f0e0a0")); OvQ(x, y + 3, 3, 1, Hx("#a85a20"));
                Fx.DrawGlow(ca, x + 1.5, y + 2 + OY, 6, 255, 190, 100, 0.4);
            }
            if (L.Ctx.CurSeason() == "winter" && L.Dusk > 0.5) PaintAurora(L, t);
            PaintMeteors(L);
        }

        public void PaintAurora(GameLoop L, double t)
        {
            double k = Math.Min(1, (L.Dusk - 0.5) / 0.4), b1 = Math.Min(-OY + 34, Horizon - 36);
            for (int cur = 0; cur < 2; cur++)
            {
                Col col = cur == 0 ? AuroraGreen : AuroraViolet; double bs = cur == 0 ? b1 : b1 + 14, amp = cur == 0 ? 7.0 : 6.0, ph = cur == 0 ? 0.0 : 2.2;
                for (int x = 0; x < W; x += 4)                                              // 4도트 폭 기둥을 2도트 높이 블록으로 쌓는다 (위로 갈수록 옅은 3단계)
                {
                    int y0 = (int)(JsMath.Round((bs + Math.Sin(x * 0.045 + t * 0.35 + ph) * amp) / 2) * 2), hgt = (int)(JsMath.Round((12 + Math.Sin(x * 0.11 + t * 0.5 + ph) * 5) / 2) * 2);
                    for (int j = 0; j < hgt; j += 2) { double f = 1 - (double)j / hgt; co.FillRect(x, y0 + j + OY, 4, 2, col, 1, 0.5 * k * (f > 0.66 ? 1 : f > 0.33 ? 0.6 : 0.3)); }
                }
            }
        }
        static readonly Col AuroraGreen = Col.Hex("#5ff0a8"), AuroraViolet = Col.Hex("#a070ff");

        static readonly Col[] MeteorCols = { Hx("#ffffff"), Hx("#e8f4ff"), Hx("#a8c8f0"), Hx("#6a8ac0") };
        public void PaintMeteors(GameLoop L)
        {
            foreach (var m in L.Meteors)
            {
                double fade = 1 - Math.Max(0, m.Age - m.Life * 0.6) / (m.Life * 0.4), ga = Math.Max(0, fade);
                for (int k = 0; k < 14; k++)                                                // 꼬리: 지나온 자리에 점점 푸르고 어두운 점
                {
                    double x = m.X - m.Vx * k * 0.016, y = m.Y - m.Vy * k * 0.016, sz = k < 3 ? 2 : 1;
                    OvQ(x, y, sz, sz, MeteorCols[k == 0 ? 0 : k < 4 ? 1 : k < 8 ? 2 : 3], ga);
                }
            }
        }

        // ================= 갈매기 무리·빵부스러기 =================
        public void PaintGulls(GameLoop L, double t)
        {
            var cr = Hx("#f2e2b0"); foreach (var c in L.Crumbs) OvQ(c.X, c.Y, 1, 1, cr);
            Col white = CWhite, gray = Hx("#aab4c4"), orange = Hx("#f0a030"), dark = Hx("#3a3a44"), leg = Hx("#e08a30");
            foreach (var g in L.Gulls)
            {
                if (g.Delay > 0) continue;
                int f = g.Face; double x = JsMath.Round(g.X), y = JsMath.Round(g.Y);
                if (g.St == "stand")                                                         // 서서 쪼아 먹는다
                {
                    int dn = g.Pk > 0 ? 1 : 0;
                    OvEllipse(x, y, 3, 1, new Col(0, 0, 0), 0.25);
                    double hx0 = f > 0 ? x + 1 : x - 3;                                      // 머리가 향하는 쪽
                    OvQ(x - 2, y - 4, 4, 2, white); OvQ(x - 2, y - 4, 4, 1, gray); OvQ(x - f * 3 - (f > 0 ? 1 : 0), y - 3, 1, 1, gray);        // 몸통, 회색 등, 꼬리
                    OvQ(hx0, y - 6 + dn * 2, 2, 2, white); OvQ(f > 0 ? hx0 + 2 : hx0 - 1, y - 5 + dn * 2, 1, 1, orange); OvQ(hx0 + (f > 0 ? 1 : 0), y - 6 + dn * 2, 1, 1, dark);        // 머리, 부리, 눈
                    OvQ(x - 1, y - 2, 1, 1, leg); OvQ(x + 1, y - 2, 1, 1, leg);
                }
                else                                                                         // 날고 있다
                {
                    int fl = (int)(Math.Floor(t * 8 + g.Ph) % 2); double ga = g.St == "out" ? Math.Min(1, g.Life) : 1;
                    OvQ(x - 3, y - fl, 3, 1, white, ga); OvQ(x, y, 1, 1, white, ga); OvQ(x + 1, y - fl, 3, 1, white, ga); OvQ(x - 1, y + 1, 2, 1, gray, ga);
                }
            }
        }

        // ================= 배: 물보라·선원·깃발·등불·표식 =================
        /// <summary>배 한 척의 물보라와 손 흔드는 선원(원본 shipDraw 의 뒤따르는 물보라·뱃머리 물결, drawSailor). 몸·그림자는 GPU 스프라이트.</summary>
        public void PaintShipBody(GameLoop L, Ship sh, double t)
        {
            double ls = Lanes.S[sh.Lane], ly = Lanes.Y[sh.Lane] + sh.Yo, Lh = Lanes.HalfLen(sh.Type) * ls, xt = JsMath.Round(sh.X); bool night = L.Ctx.Night; int dir = sh.Dir;
            var c1 = Hx("#e6f4fa"); var c2 = Hx("#a8cde8");
            void SQ(double lx, double y, double w, double h, Col c, double ga)                  // 반전(dir<0)된 좌표계의 fillRect
            {
                double x0 = JsMath.Round(lx), w0 = Math.Max(1, JsMath.Round(w)), yy = JsMath.Round(y), h0 = Math.Max(1, JsMath.Round(h));
                double sx = dir < 0 ? xt - x0 - w0 : xt + x0;
                co.FillRect((int)sx, (int)yy + OY, (int)w0, (int)h0, c, 1, ga);
            }
            for (int k = 0; k < 6; k++)                                                      // 뒤따르는 물보라
            {
                double ga = 0.6 * (1 - k / 6.0) * (night ? 0.7 : 1);
                SQ(-Lh - 1 - k * 3.4 * ls, ly + 0.5 + Math.Sin(t * 3 + k) * 0.5, (3 - k * 0.4) * ls, 0.5, c1, ga);
                SQ(-Lh + 1 - k * 3.4 * ls, ly + 1.8 - Math.Sin(t * 3 + k) * 0.5, (2.2 - k * 0.3) * ls, 0.5, c2, ga);
            }
            SQ(Lh - 1.5 * ls, ly + 0.2, 3 * ls, 0.5, c1, 0.75); SQ(Lh - 0.5 * ls, ly - 0.3, 2 * ls, 0.5, CWhite, 0.75);      // 뱃머리 물결
            if (sh.Wave > 0) PaintSailor(sh, t);
        }

        /// <summary>손을 흔드는 선원 (drawSailor 5117행). 항로 y 는 옆걸음(Yo)을 더하지 않는다(원본 그대로).</summary>
        public void PaintSailor(Ship sh, double t)
        {
            double ls = Lanes.S[sh.Lane], ly = Lanes.Y[sh.Lane]; int big = ls >= 0.7 ? 1 : 0;
            double x = JsMath.Round(sh.X - sh.Dir * (Lanes.HalfLen(sh.Type) - 9) * ls), y = JsMath.Round(ly - (Lanes.Top(sh.Type) + 3) * ls); int up = (int)(Math.Floor(t * 6) % 2);
            var skin = Hx("#f0c8a0"); var blue = Hx("#3d5a8a");
            OvQ(x - 1, y - 6 - big, 2 + big, 2 + big, skin); OvQ(x - 1, y - 4, 2 + big, 3 + big, blue);
            OvQ(x + 1 + big, y - (up != 0 ? 9 : 7) - big, 1, 3, skin); OvQ(x - 2, y - 3, 1, 3, blue);
        }

        // 깃발: 스프라이트의 깃발 글자(a, d)를 깃대 기준 배 뒤쪽으로 뒤집은 위치
        sealed class Pennant { public List<(int dx, int dy, char ch, int dist)> Px = new List<(int, int, char, int)>(); public List<(int dx, int dy, int dist)> Tips = new List<(int, int, int)>(); }
        static readonly Dictionary<string, Pennant> pennants = new Dictionary<string, Pennant>();
        static readonly Dictionary<string, (double dx, double dy)> lanterns = new Dictionary<string, (double, double)>();

        static Pennant PennantOf(string type, int lane)
        {
            string key = type + lane; if (pennants.TryGetValue(key, out var pn)) return pn;
            var d = ShipBaker.Design(type, lane); var raw = new List<(int x, int y, char ch)>();
            for (int y = 0; y < d.Rows.Length; y++) for (int x = 0; x < d.Rows[y].Length; x++) if (d.Rows[y][x] == 'a' || d.Rows[y][x] == 'd') raw.Add((x, y, d.Rows[y][x]));
            var sorted = raw.OrderBy(p => p.x).ToList();                                     // JS sort 는 안정 정렬
            var groups = new List<(List<(int x, int y, char ch)> px, int max)>();            // 깃발이 둘인 배(상선)는 가까운 열끼리 묶는다
            foreach (var p in sorted)
            {
                if (groups.Count > 0 && p.x - groups[groups.Count - 1].max <= 2) { var g = groups[groups.Count - 1]; g.px.Add(p); groups[groups.Count - 1] = (g.px, Math.Max(g.max, p.x)); }
                else groups.Add((new List<(int, int, char)> { p }, p.x));
            }
            pn = new Pennant();
            foreach (var g in groups)
            {
                var ds = g.px.Where(p => p.ch == 'd').Select(p => p.x).ToList();
                int m = (ds.Count > 0 ? ds.Min() : g.px.Min(p => p.x)) - 1;                   // m = 깃대 오른쪽 끝 열
                int minNx = 1000000000, topY = 1000000000;
                foreach (var (x, y, ch) in g.px) { int nx = 2 * m - 1 - x; minNx = Math.Min(minNx, nx); topY = Math.Min(topY, y); pn.Px.Add((nx - d.Ox, y - d.Oy, ch, m - 1 - nx)); }
                pn.Tips.Add((minNx - 1 - d.Ox, topY - d.Oy, m - 1 - (minNx - 1)));
            }
            return pennants[key] = pn;
        }

        static (double dx, double dy) LanternOf(string type, int lane)
        {
            string key = type + lane; if (lanterns.TryGetValue(key, out var v)) return v;
            var d = ShipBaker.Design(type, lane); double sx = 0, sy = 0; int n = 0;
            for (int y = 0; y < d.Rows.Length; y++) for (int x = 0; x < d.Rows[y].Length; x++) if (d.Rows[y][x] == 'l') { sx += x; sy += y; n++; }
            return lanterns[key] = n > 0 ? (sx / n - d.Ox, sy / n - d.Oy) : (-6, -6);
        }

        /// <summary>모든 배의 깃발은 뒤쪽으로 날린다. 도움을 준 배는 금빛으로 펄럭이고, 못 받은 배는 붉은 깃발이 가만히 있다 (drawShipFlag 4216행).</summary>
        public void PaintShipFlag(GameLoop L, Ship sh, double t)
        {
            double ls = Lanes.S[sh.Lane], fy = Lanes.Y[sh.Lane] + sh.Yo, fx = sh.X; int dir = sh.Dir;
            double bob = JsMath.Round(Math.Sin(t * 1.1 + sh.Ph) * 0.8 * ls); var P = PennantOf(sh.Type, sh.Lane); bool helped = sh.Done;
            double Wave(int dist) => helped ? JsMath.Round(Math.Sin(t * 7 - dist * 1.3 + sh.Ph * 6) * Math.Min(1, dist * 0.5)) : 0;       // 깃대에서 멀수록 더 흔들린다
            double k = 1 - 0.4 * L.Dusk; Col A = helped ? Hx("#ffd84a") : Shade("#e0453e", k), D = helped ? Hx("#e8b84a") : Shade("#c9403a", k);
            void Put1(double dx, double dy, Col col) => OvQ(JsMath.Round(fx) + (dir > 0 ? dx : -dx - 1), JsMath.Round(fy + dy + bob), 1, 1, col);      // (대리자 없이 지역 함수: 프레임마다 쓰레기가 쌓이지 않게)
            foreach (var (dx, dy, ch, dist) in P.Px) Put1(dx, dy + Wave(dist), ch == 'a' ? A : D);
            if (helped) { var tip = Hx("#fff0a0"); foreach (var (dx, dy, dist) in P.Tips) Put1(dx, dy + Wave(dist), tip); }      // 한 칸 더 긴 깃발 끝
        }

        /// <summary>밤의 갑판 등불: 도움을 못 받은 배는 차갑고 희미한 점, 도움받는 중이면 따뜻하게 켜지고, 받은 배는 환하게 반짝인다 (drawLantern 4224행).</summary>
        public void PaintShipLantern(GameLoop L, Ship sh, double t)
        {
            double k = Math.Min(1, (L.Dusk - 0.35) / 0.4); if (k <= 0) return;
            double w = sh.Done ? 1 : Math.Min(0.9, sh.Guide / (sh.Need != 0 ? sh.Need : 1)), ls = Lanes.S[sh.Lane], fy = Lanes.Y[sh.Lane] + sh.Yo;
            var (dx, dy) = LanternOf(sh.Type, sh.Lane); double bob = JsMath.Round(Math.Sin(t * 1.1 + sh.Ph) * 0.8 * ls);
            double x = JsMath.Round(sh.X + (sh.Dir > 0 ? dx : -dx)), y = JsMath.Round(fy + dy + bob), ph = (t * 0.62 + sh.Ph) % 1;
            if (w < 0.12) { OvQ(x, y, 1, 1, Hx("#a8bddc"), k * 0.85); return; }                 // 못 받은 배도 초라하지 않게: 차분하지만 또렷한 점
            var warm = Hx("#ffe9a0");                                                           // 항상 보이는 옅은 십자: 등불이라는 걸 알아보게 한다
            OvQ(x - 1, y, 1, 1, warm, k * 0.5 * w); OvQ(x + 2, y, 1, 1, warm, k * 0.5 * w); OvQ(x, y - 1, 1, 1, warm, k * 0.5 * w); OvQ(x, y + 2, 1, 1, warm, k * 0.5 * w);
            OvQ(x, y, w > 0.5 ? 2 : 1, w > 0.5 ? 2 : 1, warm, k); OvQ(x, y, 1, 1, CWhite, k);       // 등불 알맹이
            if (sh.Done && ph < 0.28)                                                                // 반짝: 도움을 받은 배만 십자가 커졌다 줄어든다 (번짐은 없다)
            {
                double r = Math.Sin(ph / 0.28 * Math.PI) * 4; var c = Hx("#fff0b8");
                for (int i = 2; i <= r + 1; i++) { OvQ(x - i, y, 1, 1, c, k * 0.9); OvQ(x + 1 + i, y, 1, 1, c, k * 0.9); OvQ(x, y - i, 1, 1, c, k * 0.9); OvQ(x, y + 1 + i, 1, 1, c, k * 0.9); }
            }
        }

        /// <summary>구조 요청의 느낌표, 정박 중인 배의 닻, 단골 손님 별 (drawShipMarks 4241행). 깃발·등불도 여기서.</summary>
        public void PaintShipMarks(GameLoop L, double t)
        {
            for (int lane = 0; lane < 3; lane++)                                           // 항로 y 순서(뒤→앞), 같은 항로에서는 목록 순서 — OrderBy 와 같은 순서, 쓰레기 없이
            foreach (var sh in L.Ships)
            {
                if (sh.Lane != lane) continue;
                double ls = Lanes.S[sh.Lane], fy = Lanes.Y[sh.Lane] + sh.Yo, x = JsMath.Round(sh.X);
                PaintShipFlag(L, sh, t);
                if (L.Dusk > 0.35) PaintShipLantern(L, sh, t);
                if (sh.Sos && !sh.Done && Math.Floor(t * 3) % 2 == 0)
                {
                    double y = JsMath.Round(fy - 36 * ls); var red = Hx("#e0453e");
                    OvQ(x - 1, y - 7, 3, 5, red); OvQ(x - 1, y - 1, 3, 2, red); OvQ(x, y - 6, 1, 3, Hx("#fff0e8"));
                }
                if (sh.RestT > 0) { var ic = Icon("anchor"); co.Put(ic, x - 4 - ic.Ax, JsMath.Round(fy - 34 * ls) + OY - ic.Ay, 1); }
                if (sh.Reg != null || sh.Rare != null) { var ic = Icon("star"); co.Put(ic, x - 4 - ic.Ax, JsMath.Round(fy - ShipBaker.Design(sh.Type, sh.Lane).Oy - 11 - Math.Floor(t * 2) % 2) + OY - ic.Ay, 1); }      // 단골 손님 표시
            }
        }

        // ================= 꾸미기 번호 배지 =================
        static readonly string[][] DecoDigits =
        {
            new[] { "###", "#.#", "#.#", "#.#", "###" }, new[] { ".#.", "##.", ".#.", ".#.", "###" }, new[] { "##.", "..#", ".#.", "#..", "###" }, new[] { "##.", "..#", ".#.", "..#", "##." }, new[] { "#.#", "#.#", "###", "..#", "..#" },
            new[] { "###", "#..", "##.", "..#", "##." }, new[] { ".##", "#..", "###", "#.#", "###" }, new[] { "###", "..#", ".#.", ".#.", ".#." }, new[] { "###", "#.#", "###", "#.#", "###" }, new[] { "###", "#.#", "###", "..#", "##." }
        };

        /// <summary>slotNum(id): 자리 id 의 숫자 부분(원본 +id.replace(/\D/g, '')).</summary>
        public static int SlotNum(string id) { int n = 0; foreach (char ch in id ?? "") if (ch >= '0' && ch <= '9') n = n * 10 + (ch - '0'); return n; }

        /// <summary>자리 번호 배지(3×5 도트 숫자). U1: 원본은 숫자 표가 0~9 뿐이라 10번 이상이 전부 0 모양이었다 — 두 자리 이상은 숫자를 자릿수대로 그린다.
        /// 한 자리(0~9)는 원본과 픽셀이 같다(골든 배지 프레임). 두 자리는 글자 사이 1칸, 배지 중심은 한 자리 때와 같은 곳(m.X-1.5)이다.</summary>
        void PaintDecoBadge(DecoMarker m)
        {
            string s = (m.Num > 0 ? m.Num : 0).ToString(System.Globalization.CultureInfo.InvariantCulture); int n = s.Length, w = n * 4 - 1;
            double bx = m.X - 3 - (w - 3) / 2, by = m.Y + 3;
            OvQ(bx - 1, by - 1, w + 2, 7, Hx(m.Sel ? "#7e4a06" : "#20304e")); OvQ(bx, by, w, 5, Hx("#20304e"));
            var ink = Hx(m.Sel ? "#ffe98a" : "#ffffff");
            for (int k = 0; k < n; k++)
            {
                var d = DecoDigits[s[k] - '0']; double dx = bx + k * 4;
                for (int j = 0; j < 5; j++) for (int i = 0; i < 3; i++) if (d[j][i] == '#') OvQ(dx + i, by + j, 1, 1, ink);
            }
        }

        /// <summary>시험용: 번호 배지 한 개를 그린 도트 격자('#'=글자, 'o'=바탕, '.'=없음). 가로 (자릿수×4+1)·세로 7.</summary>
        public static string[] BadgeDots(int num)
        {
            string s = (num > 0 ? num : 0).ToString(System.Globalization.CultureInfo.InvariantCulture); int n = s.Length, w = n * 4 - 1;
            var rows = new string[5];
            for (int j = 0; j < 5; j++) { var sb = new System.Text.StringBuilder(); for (int k = 0; k < n; k++) { if (k > 0) sb.Append('.'); sb.Append(DecoDigits[s[k] - '0'][j]); } rows[j] = sb.ToString(); }
            return rows;
        }
    }
}
