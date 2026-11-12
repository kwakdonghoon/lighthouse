using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Render;

namespace Lighthouse.World
{
    /// <summary>
    /// A1 월드 연출의 상태 (원본 updateSeason 5008행·updateMeteors 5048행·spawnDrift 4509행·updateGulls 4319행·drawLife 의 seeSea 호출):
    /// 계절 입자(벚꽃잎·낙엽·눈)·여름 밤 등불·불꽃놀이·별똥별(소원)·표류물(줍기)·갈매기 무리·빵부스러기·하늘/자연 도감 기록.
    /// 난수는 모두 연출 난수(fx)다. 표류물 선물 추첨만 규칙 난수(RollGift, 원본 spawnDrift 안의 rollGift)이고, 가속 시뮬(LoopSim)은 Tick 을 쓰지 않아 영향이 없다.
    /// </summary>
    public sealed partial class GameLoop
    {
        public readonly List<Mote> Motes = new List<Mote>();
        public readonly List<Lantern> Lanterns = new List<Lantern>();
        public readonly List<Meteor> Meteors = new List<Meteor>();
        public readonly List<DriftItem> Drift = new List<DriftItem>();
        public readonly List<Gull> Gulls = new List<Gull>();
        public readonly List<Crumb> Crumbs = new List<Crumb>();

        double nextMote, nextLantern = 4, nextFirework = 14, nextMeteor = 12, nextDrift = 7, skyT;
        public const int Horizon = BgBaker.Horizon;
        static readonly int[] FireworkCols = { 0, 1, 2, 3 };       // SparkCols 의 앞 네 색(원본 '#ffe08a'·'#ff9bb8'·'#a8d4ff'·'#8be096')

        /// <summary>개발 도구(원본 개발 단추 '별똥별'·'불꽃놀이'): 밤이면 곧 별똥별이 흐르게 / 불꽃 한 발.</summary>
#if LIGHTHOUSE_DEV      // 출시 구성에는 이 두 훅이 없다(DevTools 의 바이너리 검사가 이름으로 확인)
        public void DevMeteorSoon() { nextMeteor = 0; }
        public void DevFirework() { Firework(); }
#endif

        /// <summary>렌즈가 자랄수록 별똥별이 자주 흐른다 (원본 meteorMul 1955행, EC-06). 성장 화면 "별똥별 ×N" 과 같은 식.</summary>
        public double MeteorMul() { int L = Ctx.LvlOf("lens"); return (1 + 0.03 * Math.Min(L, 16)) * (L >= 14 ? 2.2 : L >= 9 ? 1.43 : 1); }

        // ================= 계절 연출 (원본 updateSeason) =================
        void UpdateSeason(double dt)
        {
            UpdateMeteors(dt);
            string s = Ctx.CurSeason(); double t = T;
            double gap = s == "spring" ? 0.18 : s == "autumn" ? 0.22 : s == "winter" ? 0.09 : 0;
            nextMote -= dt;
            if (gap > 0 && nextMote <= 0 && Motes.Count < 80)                                         // 떨어지는 꽃잎·낙엽·눈송이
            {
                nextMote = gap * (0.6 + R() * 0.8);
                Motes.Add(new Mote { X = R() * (W + 30) - 15, Y = -OY - 4, Vx = (s == "winter" ? -2 : -6) + R() * 8, Vy = (s == "winter" ? 9 : 7) + R() * 7, Ph = R() * 6, S = s });
            }
            for (int i = Motes.Count - 1; i >= 0; i--)
            {
                var m = Motes[i]; m.Y += m.Vy * dt; m.X += (m.Vx + Math.Sin(t * 1.3 + m.Ph) * 5) * dt;
                if (m.Y > H - OY - 4 || m.S != s) Motes.RemoveAt(i);
            }
            if (s == "summer" && Dusk > 0.6)                                                          // 여름 밤: 떠오르는 종이 등불, 가끔 불꽃놀이
            {
                nextLantern -= dt; nextFirework -= dt;
                if (nextLantern <= 0 && Lanterns.Count < 8) { nextLantern = 3 + R() * 4; Lanterns.Add(new Lantern { X = W / 2.0 + (R() - 0.5) * 70, Y = Terrain.CY - 4, Ph = R() * 6 }); }
                if (nextFirework <= 0) { nextFirework = 20 + R() * 25; Firework(); }
            }
            for (int i = Lanterns.Count - 1; i >= 0; i--) { var l = Lanterns[i]; l.Y -= 7 * dt; l.X += Math.Sin(t * 0.8 + l.Ph) * 3 * dt; if (l.Y < -OY - 10 || s != "summer") Lanterns.RemoveAt(i); }
            SeeSky(dt, s);
        }

        void Firework()
        {
            Ctx.SeeSea("firework");
            double x = 15 + R() * (W - 30), y = Math.Max(-OY + 12, Horizon - 45 - R() * 55); int col = FireworkCols[(int)(R() * FireworkCols.Length)];
            for (int i = 0; i < 26; i++) { double a = i / 26.0 * Math.PI * 2, sp = 18 + R() * 12; Sparks.Add(new Spark { X = x, Y = y, Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, Life = 1.4, Col = col }); }
            Say("firework");
        }

        /// <summary>자연 도감 하늘·날씨 칸: 지금 장면에 실제로 보이는 것만 올린다 (반 초마다 한 번, 원본 seeSky 5029행). 가속 시뮬은 기록하지 않는다(SeeSea 가 Sim 이면 빠진다).</summary>
        void SeeSky(double dt, string s)
        {
            skyT -= dt; if (skyT > 0) return; skyT = 0.5;
            if (Ctx.Sim) return;
            long now = Ctx.Clock.NowMs;
            if (s != "summer" && Motes.Any(m => m.Y > -OY && m.S == s)) Ctx.SeeSea(s == "spring" ? "petals" : s == "autumn" ? "leaves" : "snowflake");      // 여름은 없다
            if (Wx.Amt > 0.3) { if (Wx.Kind == "fog") Ctx.SeeSea("fog"); else if (Wx.Kind == "rain") Ctx.SeeSea(s == "winter" ? "snowflake" : "rain"); }       // 겨울의 비는 눈으로 그려진다
            if (!Ctx.Night && RainbowUntilMs - now > 0 && RainbowUntilMs - now < 42000) Ctx.SeeSea("rainbow");                    // 무지개는 낮에만 그려진다 (서서히 나타난 뒤)
            if (Lanterns.Any(l => l.Y > -OY)) Ctx.SeeSea("lantern");
            if (s == "winter" && Dusk > 0.65) Ctx.SeeSea("aurora");                                                              // 오로라가 보일 만큼 어두워진 겨울 밤
            double k = Math.Sin(Math.PI * Dusk);
            if (k > 0.3) Ctx.SeeSea(Progress.DuskAt(Ctx.S.Clock, now + 3000, Ctx.Clock, Ctx.CycleSec) >= Dusk ? "sunset" : "dawnglow");   // 해가 지는 중이면 저녁 노을, 뜨는 중이면 새벽빛
        }

        /// <summary>drawLife 안의 seeSea: 뛰어오르는 물고기·밤의 반딧불이·낮의 나비를 본 것으로 친다(원본은 그릴 때마다 호출, 이미 만난 것은 가장 싼 길로 빠진다).</summary>
        void SeeLife()
        {
            double t = T, period = 6, ph = (t % period) / period;
            if (ph < 0.1) Ctx.SeeSea("fish");
            Ctx.SeeSea(Ctx.Night ? "firefly" : "butterfly");
        }

        // ================= 별똥별 (원본 updateMeteors 5048행) =================
        void UpdateMeteors(double dt)
        {
            nextMeteor -= dt;
            if (nextMeteor <= 0)
            {
                nextMeteor = (25 + R() * 35) / MeteorMul();                                    // 렌즈가 자랄수록 자주 (EC-06)
                if (Ctx.Night && Dusk > 0.7 && Wx.Kind == "clear" && Meteors.Count == 0)
                {
                    int dir = R() < 0.5 ? 1 : -1;
                    Ctx.SeeSea("meteor", true);
                    Meteors.Add(new Meteor { X = dir > 0 ? 10 + R() * 60 : W - 10 - R() * 60, Y = -OY + 8 + R() * 30, Vx = dir * (55 + R() * 20), Vy = 28 + R() * 12 });
                }
            }
            for (int i = Meteors.Count - 1; i >= 0; i--) { var m = Meteors[i]; m.Age += dt; m.X += m.Vx * dt; m.Y += m.Vy * dt; if (m.Age > m.Life) Meteors.RemoveAt(i); }
        }

        /// <summary>흐르는 별똥별을 누르면 소원이 이뤄진다 (원본 tryWish 5071행).</summary>
        bool TryWish(double x, double y)
        {
            foreach (var m in Meteors)
                if (!m.Wished && Hyp(x - m.X, y - m.Y) < 30)
                {
                    m.Wished = true; Bump("wish"); m.Life = Math.Min(m.Life, m.Age + 0.5);
                    BonusGift(Str.Get("world.wish"), Str.Get("world.wishSub"), null); return true;
                }
            return false;
        }

        // ================= 표류물 (원본 spawnDrift 4509행·tryPickDrift 4570행) =================
        void SpawnDrift()
        {
            bool left = R() < 0.5; double y = R() < 0.5 ? 190 + R() * 20 : 274 + R() * 26;
            Drift.Add(new DriftItem { X = left ? -8 : W + 8, Y = y, Vx = (left ? 1 : -1) * (3 + R() * 3), Gift = Ctx.RollGift("fish"), Ph = R() * 6 });      // 선물은 만들 때 정한다(규칙 난수, 원본과 같다)
        }

        void UpdateDrift(double dt)
        {
            nextDrift -= dt;
            if (nextDrift <= 0) { nextDrift = 16 + R() * 14; if (Drift.Count < 2) SpawnDrift(); }
            for (int i = Drift.Count - 1; i >= 0; i--)
            {
                var it = Drift[i]; it.X += it.Vx * dt;
                if (it.Take >= 0) it.Take += dt / 0.9;                                          // 약 0.9초에 걸쳐 스르르 사라진다
                if (it.Take >= 1 || it.X < -12 || it.X > W + 12) Drift.RemoveAt(i);
            }
        }

        bool TryPickDrift(double x, double y)
        {
            foreach (var it in Drift)
            {
                if (it.Take >= 0) continue;
                if (Hyp(x - it.X, y - (it.Y - 3)) < 14)
                {
                    it.Take = 0; it.Vx *= 0.3;                                                    // 사라지는 중: UpdateDrift 가 흐리고 지운다
                    Ctx.AddGift(it.Gift); Bump("drift"); Ctx.NotifyChanged(); Say("pick");
                    Pops.Add(new Pop { X = it.X, Y = it.Y - 8, Gift = it.Gift });
                    for (int k = 0; k < 6; k++) Fx.Add(new Twinkle { X = it.X, Y = it.Y - 4, Vx = (R() - 0.5) * 16, Vy = -6 - R() * 8, Life = 0.9 });
                    Rings.Add(new Ring { X = it.X, Y = it.Y + 2, S = 0.6 });
                    return true;
                }
            }
            return false;
        }

        // ================= 갈매기 무리·빵부스러기 (원본 4301~4337행) =================
        void SpawnGulls()
        {
            Ctx.SeeSea("gull", true);
            var kp = KeeperPos(); int n = 3 + (R() < 0.5 ? 1 : 0);
            for (int i = 0; i < n; i++)
            {
                int side = i % 2 != 0 ? 1 : -1;
                Gulls.Add(new Gull
                {
                    X = kp.x + side * (70 + R() * 30), Y = kp.y - 40 - R() * 25, Tx = kp.x + Keeper.Dir * (9 + i * 9) + (R() - 0.5) * 3, Ty = kp.y + (R() - 0.5) * 4,
                    St = "in", Ph = R() * 6, Peck = R() * 1.5, Pk = 0, Face = Keeper.Dir * -1, Delay = i * 0.7
                });
            }
        }

        /// <summary>먹이 주기가 끝나면 갈매기들이 날아간다. 날아간 갈매기가 있었는지를 돌려준다(원본 scatterGulls 의 any).</summary>
        bool ScatterGulls()
        {
            bool any = false;
            foreach (var g in Gulls)
                if (g.St != "out") { any = true; g.St = "out"; g.Vx = (R() < 0.5 ? -1 : 1) * (18 + R() * 14); g.Vy = -(14 + R() * 10); g.Life = 3; }
            return any;
        }

        void ThrowCrumbs()
        {
            var kp = KeeperPos(); int d = Keeper.Dir;
            for (int k = 0; k < 3; k++) Crumbs.Add(new Crumb { X = kp.x + d * 3, Y = kp.y - 6, Vx = d * (9 + R() * 9), Vy = -(12 + R() * 8), Gy = kp.y + (R() - 0.3) * 3 });
        }

        void UpdateGulls(double dt)
        {
            for (int i = Crumbs.Count - 1; i >= 0; i--)
            {
                var c = Crumbs[i];
                if (c.Land) { c.Life -= dt; if (c.Life <= 0) Crumbs.RemoveAt(i); continue; }
                c.Vy += 60 * dt; c.X += c.Vx * dt; c.Y += c.Vy * dt;
                if (c.Vy > 0 && c.Y >= c.Gy) { c.Y = c.Gy; c.Land = true; c.Life = 2.2 + R(); }
            }
            for (int i = Gulls.Count - 1; i >= 0; i--)
            {
                var g = Gulls[i];
                if (g.Delay > 0) { g.Delay -= dt; continue; }
                if (g.St == "in")
                {
                    double dx = g.Tx - g.X, dy = g.Ty - g.Y, d = Hyp(dx, dy);
                    if (d < 1.5) { g.St = "stand"; g.X = g.Tx; g.Y = g.Ty; g.Face = dx >= 0 ? 1 : -1; } else { g.X += dx / d * 30 * dt; g.Y += dy / d * 30 * dt; g.Face = dx >= 0 ? 1 : -1; }
                }
                else if (g.St == "stand")
                {
                    g.Peck -= dt; g.Pk -= dt;
                    if (g.Peck <= 0) { g.Peck = 0.5 + R() * 1.3; g.Pk = 0.22; }
                }
                else { g.X += g.Vx * dt; g.Y += g.Vy * dt; g.Life -= dt; if (g.Life <= 0) Gulls.RemoveAt(i); }
            }
        }
    }
}
