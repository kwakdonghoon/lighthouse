using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Render;

namespace Lighthouse.World
{
    /// <summary>
    /// 관리인 (원본 4252~4430행): 3단계부터 낮에 섬을 거닌다. 걷기·일·말풍선·소소한 선물(낚시·별 보기·갈매기 먹이)의 규칙을 옮겼다.
    /// 갈매기 무리·빵부스러기 그림, 일하는 손동작 그림(drawKeeperAct)은 렌더(KeeperArt)가 act 이름으로 그린다.
    /// </summary>
    public sealed partial class GameLoop
    {
        public readonly List<(double u, double v, string act, int face)> KeeperPath = new List<(double, double, string, int)>(), KeeperNight = new List<(double, double, string, int)>();
        (double u, double v, string act, int face) lampSpot = (0, 0, "lamp", -1), lampVia = (0, 0, "pass", -1);
        readonly HashSet<string> keeperSeen = new HashSet<string>();
        public bool GullsOut;                                   // 먹이 주기 동안 갈매기 무리를 그린다(렌더가 읽는다)

        static readonly (string act, int stage, double a, double b, int face, bool noflag)[] Stops =
        {
            ("polish", 1, -0.5, 2.0, -1, false), ("mail", 2, 3.5, 2.6, 1, false), ("pass", 5, -1e9, 0.5, 1, false), ("fish", 5, 1e9, 0.5, 1, false), ("pass", 5, -1e9, 0.5, 1, false), ("well", 4, 3.5, -1.0, 1, false),
            ("anchor", 6, 3.6, -2.2, 1, false), ("pass", 4, 0, -3.2, 1, false), ("sit", 4, -4.0, -0.5, 1, false), ("pass", 4, -3.6, 0.8, 1, true), ("flag", 5, -3.6, 1.5, -1, false),
            ("water", 3, 2.9, 4.7, 1, false), ("gild", 6, 2.7, 3.4, 1, false), ("feed", 5, -0.5, 1e8, 1, false), ("enter", 3, -3.9, 4.9, -1, false), ("sweep", 1, -1.5, 4.9, -1, false)
        };

        /// <summary>setKeeperSpots: 단계(섬 크기)가 바뀔 때마다 걷는 순서를 다시 정한다.</summary>
        public void SetKeeperSpots()
        {
            int stage = Ctx.Stage, N = Terrain.N(stage);
            (double u, double v) B(double a, double c) => Terrain.Blk2Uv(a, c);
            KeeperPath.Clear(); KeeperNight.Clear();
            foreach (var s in Stops)
            {
                if (stage < s.stage || (s.noflag && stage >= 5)) continue;
                double a = s.a == -1e9 ? N - 1.5 : s.a == 1e9 ? N + 2.6 : s.a;                 // 섬 크기에 따른 값(ISLE_N-1.5, ISLE_N+2.6)
                double c = s.b == 1e8 ? N - 1.5 : s.b;
                if (s.act == "feed") { a = -0.5; c = N - 1.5; }
                var uv = B(a, c); KeeperPath.Add((uv.u, uv.v, s.act, s.face));
            }
            var lp = B(0.5, 2.0); lampSpot = (lp.u, lp.v, "lamp", -1); var lv = B(-1.5, 4.9); lampVia = (lv.u, lv.v, "pass", -1);
            string nightAct; double na, nb;
            if (stage >= 6) { nightAct = "stars"; na = -1.5; nb = N - 1.5; } else if (stage >= 3) { nightAct = "enter"; na = -3.9; nb = 4.9; } else { nightAct = "rest"; na = -0.5; nb = 2.0; }
            if (nightAct == "enter") KeeperNight.Add((lv.u, lv.v, "pass", -1));
            var nu = B(na, nb); KeeperNight.Add((nu.u, nu.v, nightAct, -1));
            var k = Keeper;
            if (Terrain.TerrH(stage, k.U, k.V) < 0 && KeeperPath.Count > 0) { k.U = KeeperPath[0].u; k.V = KeeperPath[0].v; }
            if (KeeperPath.Count > 0) k.I = k.I % KeeperPath.Count;
            k.Ni = 0;
        }

        /// <summary>집·탑 안, 섬 밖(부두는 제외)은 걸을 수 없다.</summary>
        bool KeeperBlocked(double u, double v)
        {
            int stage = Ctx.Stage;
            var (a, b) = Terrain.Uv2Blk(u, v); double tw = stage >= 4 ? 1.95 : 1.05;
            if (Ctx.Tier() >= 2 && a > -4.6 && a < -2.4 && b > 2.4 && b < 4.6) return true;
            if (Math.Abs(a) < tw && Math.Abs(b) < tw) return true;
            return Terrain.TerrH(stage, u, v) < 0;
        }

        public (int x, int y) KeeperPos()
        {
            int stage = Ctx.Stage; var k = Keeper;
            return ((int)JsMath.Round(W / 2.0 + k.U), (int)JsMath.Round(Terrain.CY + Terrain.IsleY + k.V * Terrain.KY - Terrain.TerrH(stage, k.U, k.V)));
        }

        void KeeperSay(string msg)
        {
            if (Perf < (Bubble?.Until ?? 0)) return;
            ShowBubble(msg, null, true);
        }

        void KeeperStepSound()
        {
            int stage = Ctx.Stage; var (a, _) = Terrain.Uv2Blk(Keeper.U, Keeper.V);
            bool pier = Terrain.TerrH(stage, Keeper.U, Keeper.V) == Terrain.Deck && a > Terrain.N(stage);
            Say(pier ? "plank" : "grass");
        }

        static readonly Dictionary<string, (string sfx, double every)> WorkSfx = new Dictionary<string, (string, double)>
        {
            { "polish", ("brush", 0.7) }, { "anchor", ("brush", 0.7) }, { "gild", ("brush", 0.7) }, { "sweep", ("brush", 0.45) }, { "well", ("creak", 1.6) },
            { "flag", ("pull", 0.9) }, { "water", ("pour", 1.4) }, { "lamp", ("lamp", 99) }, { "fish", ("bloop", 99) }
        };

        void KeeperItemGift(string kind, double x, double y, string stat, bool say = false)
        {
            string g = Ctx.RollGift(kind); Ctx.AddGift(g); if (stat != null) Bump(stat); Ctx.NotifyChanged();
            Pops.Add(new Pop { X = x, Y = y, Gift = g }); Say("pick");
        }

        void UpdateKeeper(double dt)
        {
            var k = Keeper; bool night = Ctx.Night; int stage = Ctx.Stage;
            if (night != k.WasNight)                                   // 해가 지면 별을 보러, 해가 뜨면 하루 일과로 돌아간다
            {
                k.WasNight = night; k.Ni = 0; k.LampPending = stage >= 2; k.LampVia = stage >= 3; k.Wait = 0; k.Act = null; k.WaveT = 0; k.StarT = 0; GullsOut = false;
            }
            if (k.WaveT > 0) { k.WaveT -= dt; k.Walking = false; return; }          // 안내를 마친 배에 손을 흔든다
            if (stage >= 5 && !night && !k.Walking)
                foreach (var sh in Ships)
                    if (sh.Done && sh.DoneAge < 0.4 && k.Wait > 1.2) { k.WaveT = 2.2; k.Dir = sh.X >= W / 2.0 + k.U ? 1 : -1; return; }
            if (k.Wait > 0)
            {
                k.Wait -= dt; k.Walking = false; k.ActT += dt;
                if (k.Act != null && WorkSfx.TryGetValue(k.Act, out var w))      // 일하는 소리(등불은 시작할 때 한 번)
                {
                    k.WorkT -= dt;
                    if (k.WorkT <= 0) { k.WorkT = w.every; if (!(w.every == 99 && k.ActT > 1)) Say(w.sfx); }
                }
                if (k.Act == "feed")
                {
                    k.NextToss -= dt;
                    if (k.NextToss <= 0) k.NextToss = 0.8 + R() * 0.5;       // 빵 부스러기 던지기(그림은 렌더)
                }
                if (k.Act == "stars")                                          // 망원경으로 별 보기: 가끔 작은 선물을 발견한다 (별똥별 외침은 별똥별을 옮기지 않아 생략)
                {
                    k.StarT += dt;
                    if (k.StarT >= 32)
                    {
                        k.StarT = 0; Bump("stars");
                        if (R() < 0.3) { var p = KeeperPos(); KeeperItemGift("trade", p.x + k.Dir * 6, p.y - 14, null); KeeperSay(Str.Get("world.starFound")); }
                    }
                }
                if (k.Act == "fish")
                {
                    k.NextCast -= dt;
                    if (k.NextCast <= 0)                                       // 낚싯대를 휙 올린다: 가끔 작은 선물을 낚는다
                    {
                        k.NextCast = 7 + R() * 4;
                        var p = KeeperPos(); double bx = p.x + 9, by = p.y + 4;
                        Rings.Add(new Ring { X = bx, Y = by, S = 0.45 });
                        if (R() < 0.3) KeeperItemGift("fish", bx, by - 14, "fished");
                    }
                }
                return;
            }
            var route = night ? KeeperNight : KeeperPath;
            if (route.Count == 0) return;
            int nx = night ? Math.Min(k.Ni, route.Count - 1) : (k.I + 1) % route.Count;
            bool wasLamp = k.LampPending;
            var tgt = wasLamp ? (k.LampVia ? lampVia : lampSpot) : route[nx];
            double du = tgt.u - k.U, dv = tgt.v - k.V, d = Math.Sqrt(du * du + dv * dv);
            if (d < 0.7)
            {
                k.WorkT = 0.2; if (tgt.act == "enter") Say("door"); else if (tgt.act == "sit") Say("sit");
                if (wasLamp)
                {
                    if (k.LampVia) { k.LampVia = false; k.Wait = 0.05; k.Act = "pass"; return; }
                    k.LampPending = false;
                }
                else if (night) k.Ni = Math.Min(k.Ni + 1, route.Count - 1); else k.I = nx;
                bool last = night && !wasLamp && nx >= route.Count - 1;
                k.Wait = last ? 9999 : tgt.act == "pass" ? 0.05 : tgt.act == "lamp" ? 5 : 6 + R() * 5;
                k.Act = tgt.act; k.ActT = 0; k.Dir = tgt.face; k.NextCast = 3; k.NextToss = 0.5; k.StarT = 0;
                if (tgt.act == "feed") GullsOut = true;
                bool first = keeperSeen.Add(tgt.act);
                if (Str.Has("world.act." + tgt.act) && (first || last || R() < 0.35)) KeeperSay(PickOf(Lines("world.act." + tgt.act), R()));      // 새 일은 처음 한 번 꼭, 그 뒤로는 가끔
                return;
            }
            if (k.Act == "feed" && GullsOut)                                   // 먹이 주기가 끝나면 갈매기들이 날아간다: 가끔 고마움의 선물을 남긴다
            {
                GullsOut = false;
                if (R() < 0.5) { var p = KeeperPos(); KeeperItemGift("fish", p.x + k.Dir * 8, p.y - 8, "fed"); }
            }
            k.Act = null; k.Walking = true; k.StepD += 9 * dt; if (k.StepD >= 5) { k.StepD = 0; KeeperStepSound(); }
            {
                double sp = 9 * dt, a0 = Math.Atan2(dv, du); bool cur = KeeperBlocked(k.U, k.V);
                foreach (double kk in new[] { 0, 0.6, -0.6, 1.2, -1.2, 1.8, -1.8 })     // 막히면 비스듬히 돌아간다 (돌던 쪽으로 계속)
                {
                    double da = kk * k.Dsign, nu = k.U + Math.Cos(a0 + da) * sp, nv = k.V + Math.Sin(a0 + da) * sp;
                    if (cur || !KeeperBlocked(nu, nv)) { k.U = nu; k.V = nv; if (kk != 0) k.Dsign = Math.Sign(da) != 0 ? Math.Sign(da) : 1; break; }
                }
            }
            k.Dir = du >= 0 ? 1 : -1;
        }
    }
}
