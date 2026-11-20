using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Render;
using Lighthouse.UI.Model;

namespace Lighthouse.World
{
    /// <summary>
    /// 게임 루프 한 곳 (원본 frame(), 5192행). 순수 C#: Unity 를 모른다. 시각은 Ctx.Clock(IClock), 난수는 두 갈래다.
    ///  - 규칙 난수(Ctx.Rng): 배 종류 추첨·단골 추첨·선물 추첨 — Simulator 와 같은 순서로 소비된다.
    ///  - 연출 난수(fx): 항로·방향·속도 흔들림·반짝임·날씨 주기 — 규칙 결과에 영향을 주지 않는다 (R2: 난수 호출을 규칙/연출로 분리).
    /// Tick(dt) 의 순서가 곧 동작이다(원본 순서): 낮밤 → 빛 조준 → 배 → 날씨·역할 → 관리인 → 환경(돌고래) → 계절 → 성장 알림 → 공사.
    /// 화면에 그리는 일은 하지 않는다: 렌더(Unity)는 이 객체의 필드(Ships·Keeper·Wx·Dusk·BeamTh…)를 읽어 그린다.
    /// </summary>
    public sealed partial class GameLoop
    {
        public readonly GameContext Ctx; public readonly UiStrings Str; readonly IRng fx;
        public int W = 216, H = 468, OY;                       // 논리 화면 (WorldView 의 LogicalScreen/FitView 가 정한다)
        public double T;                                        // 루프 누적 시각(초) = performance.now()/1000 에 해당
        public double Perf => T * 1000;
        public double Dusk; public bool NightForced;
        public readonly List<Ship> Ships = new List<Ship>();
        public readonly List<Ring> Rings = new List<Ring>();
        public readonly List<Pop> Pops = new List<Pop>();
        public readonly List<Twinkle> Fx = new List<Twinkle>();
        public readonly List<Bird> Birds = new List<Bird>();
        public readonly List<Spark> Sparks = new List<Spark>();
        public readonly List<Dolphin> Dolphins = new List<Dolphin>();
        public readonly List<Siren> Sirens = new List<Siren>();
        public readonly Weather Wx = new Weather();
        public readonly KeeperState Keeper = new KeeperState();
        public BubbleInfo Bubble;
        public readonly List<DecoMarker> Markers = new List<DecoMarker>();     // 화면(App)이 꾸미기 놓기 상태에 맞춰 채운다
        public double BeamTh, AimTh, Spin, Flash;
        public bool Aiming; bool wasAiming, catchUp, ptrDown, ptrMoved; double ptrT0, ptrX, ptrY;
        public double WarnUntil, WarnX, WarnY, SignalUntil, MailAlertUntil, RainbowUntilMs;
        public string LastSeason;
        public bool Paused;                                     // 사진 모드·광고 중에도 시간은 흐른다(원본 그대로) — 필요할 때만 true

        // ----- 화면(Unity)이 구독 -----
        public event Action<string, string> Toast;              // (제목, 부제)
        public event Action<string> Sfx;
        public event Action<string> Rebuild;                    // 섬을 다시 굽는다: "season" | "garden" | "stage" | "start"
        public event Action OpenMail;
        public event Action<BubbleInfo> BubbleChanged;
        public event Action Celebrated;                         // 승급 불꽃
        public event Action<string> Log;                        // 시험·진단용

        readonly Random rareRnd = new Random();               // 희귀 손님 추첨 전용(규칙 난수·원본 동등 기록과 분리)
        int shipId; double spawnIn = 4, reefGiftAt, nextDolphin = 14, nextRescue, sirenAt, finaleHold;

        public GameLoop(GameContext ctx, UiStrings str, IRng fxRng)
        {
            Ctx = ctx; Str = str; fx = fxRng;
            Ctx.RegularOnSea = id => Ships.Any(s => s.Reg == id);
            LastSeason = Ctx.CurSeason(); Ctx.Wx = "clear";
            SetKeeperSpots();
            // 규칙 쪽이 알리는 일을 토스트로 잇는다 (원본 addGift·deliverGuests·deliverFinale 안의 showToast)
            Ctx.ShinyFound += g => { Say("level"); SendToast(Str.Get("world.shiny"), GameContext.FixJosa(Str.F("world.shinySub", GiftName(g)))); };
            Ctx.LettersArrived += n => { MailAlertUntil = Perf + 4000; Say("mail"); SendToast(Str.Get("world.letterToast"), n > 1 ? Str.F("world.letterToastMany", n) : Str.Get("world.letterToastSub")); };
        }

        // ================= 처음 만난 것 알림 (원본 pumpFirst 3144행) =================
        /// <summary>팝업·타이틀·다른 토스트·안내 카드가 떠 있으면 true — 화면(GameApp)이 채운다. 없으면 막지 않는다.</summary>
        public Func<bool> UiBusy;
        double firstLast; int firstShown;

        /// <summary>
        /// 처음 만난 것 알림: 6초 간격, 켠 동안 3번까지. 팝업·타이틀·다른 알림·안내 카드·공사 중에는 기다린다. 놓쳐도 도감에 남는다.
        /// 대기열(Ctx.FirstQ)은 규칙 쪽 QueueFirst(= FirstMet 이벤트와 같은 지점)가 채우고, 여기서 한 개씩 꺼낸다.
        /// </summary>
        void PumpFirst()
        {
            var q = Ctx.FirstQ; if (q.Count == 0) return;
            if (firstShown >= 3) { q.Clear(); return; }
            if (Perf - firstLast < 6000 || (UiBusy != null && UiBusy()) || Ctx.S.Build != null) return;
            var e = q[0]; q.RemoveAt(0); firstLast = Perf; firstShown++;
            string kind = e[0], id = e[1];
            if (kind == "letter") { MailAlertUntil = Perf + 4000; Say("mail"); SendToast(Str.Get("world.letterToast"), Str.Get("world.letterToastSub")); return; }      // 자연 관찰 편지
            string nm = null; SeaDef sea = null;
            if (kind == "sea") { if (Ctx.T.SeaById.TryGetValue(id, out sea)) nm = sea.Name; }
            else if (kind == "ship") { if (Ctx.T.ShipById.TryGetValue(id, out var sd)) nm = sd.Name; }
            else if (Ctx.T.GiftById.TryGetValue(id, out var gd)) nm = gd.Name;
            if (string.IsNullOrEmpty(nm)) return;
            string key = kind == "sea" ? (sea.Grp == "sky" ? (sea.Rare ? "toast.firstSkyRare" : "toast.firstSky") : sea.Rare ? "toast.firstSeaRare" : "toast.firstSea") : kind == "ship" ? "toast.firstShip" : "toast.firstGift";
            SendToast(Str.F(key, nm), Str.Get("toast.firstSub")); Say("tick");
        }

        double R() => fx.NextDouble();
        void Say(string sfx) { Sfx?.Invoke(sfx); }
        void SendToast(string t, string s) { Toast?.Invoke(t, s); }

        // ================= 화면 크기 =================
        public void SetView(int w, int h, int oy) { W = w; H = h; OY = oy; }

        /// <summary>켜자마자 바다가 비어 보이지 않게 배 3척(원본 5580행).</summary>
        public void Boot()
        {
            for (int i = 0; i < 3; i++) SpawnShip(true);
            Dusk = Progress.DuskAt(Ctx.S.Clock, Ctx.Clock.NowMs, Ctx.Clock, Ctx.CycleSec); Ctx.Night = Dusk >= 0.5;
            Rebuild?.Invoke("start");
        }

        // ================= 입력 =================
        /// <summary>손가락/마우스를 눌렀다 (설계 좌표). moved 는 화면 쪽이 8dp 넘게 움직였는지 판정한다.</summary>
        public void PointerDown(double x, double y) { ptrDown = true; ptrMoved = false; ptrX = x; ptrY = y; ptrT0 = Perf; }
        public void PointerMove(double x, double y, bool moved) { if (!ptrDown) return; ptrX = x; ptrY = y; if (moved) ptrMoved = true; }
        /// <summary>떼었다. tap: 움직이지 않았고 350ms 안에 뗐다.</summary>
        public void PointerUp(bool tap) { if (!ptrDown) return; ptrDown = false; if (tap && !ptrMoved && Perf - ptrT0 < 350) TapAt(ptrX, ptrY); }

        // ================= 한 번의 Tick =================
        /// <summary>원본 frame() 의 갱신 부분. dt 는 호출하는 쪽이 0.1 로 자른다(5193행). 시각 Date.now() 는 Ctx.Clock.</summary>
        public void Tick(double dt)
        {
            if (dt > 0.1) dt = 0.1; if (dt < 0) dt = 0;
            T += dt;
            long now = Ctx.Clock.NowMs;
            if (!NightForced) Dusk = Progress.DuskAt(Ctx.S.Clock, now, Ctx.Clock, Ctx.CycleSec);
            Ctx.Night = Dusk >= 0.5;
            UpdateBeam(dt);
            double th0 = BeamTh;
            UpdateShips(dt, th0); UpdateRoles(dt); UpdateKeeper(dt); UpdateAmbient(dt); UpdateDrift(dt); UpdateSeason(dt);
            UpdateParticles(dt); SeeLife();
            string sn = Ctx.CurSeason();
            if (sn != LastSeason) { LastSeason = sn; Rebuild?.Invoke("season"); }
            if (Ctx.NeedRebuild) { Ctx.NeedRebuild = false; Rebuild?.Invoke("garden"); }
            if (Ctx.GrowthEvent != null) { SendToast(Str.Get("world.growth"), Ctx.GrowthEvent); Say("level"); Ctx.GrowthEvent = null; }
            UpdateBuild(now);
            PumpFirst();
            if (Bubble != null && Perf > Bubble.Until) { Bubble = null; BubbleChanged?.Invoke(null); }
        }

        // ================= 빛줄기 (원본 5195~5207행) =================
        static double Hyp(double a, double b) { return Math.Sqrt(a * a + b * b); }
        static double WrapPi(double a) { return ((a + Math.PI * 3) % (Math.PI * 2)) - Math.PI; }      // 원본 wrapPi 5115행 그대로(a < -3π 이면 음수 나머지: 원본도 같다, catchUp 이 +2π 로 보정)

        void UpdateBeam(double dt)
        {
            Aiming = ptrDown && (ptrMoved || Perf - ptrT0 > 220);
            if (Aiming && !(Ctx.S.Tips.TryGetValue("h6", out double h6) && h6 != 0)) { Ctx.S.Tips["h6"] = 1; Ctx.NotifyChanged(); }       // 이미 빛을 끌어 본 사람에게는 "빛 조준" 안내를 보이지 않는다 (D30)
            if (Aiming)
            {
                AimTh = Math.Atan2((ptrY - Terrain.CY - Terrain.IsleY) / Terrain.KY, ptrX - W / 2.0);
                if (!wasAiming) catchUp = true;
                double d = WrapPi(AimTh - BeamTh);
                if (catchUp)
                {
                    if (d < -0.6) d += Math.PI * 2;
                    BeamTh += Math.Sign(d) * Math.Min(Math.Abs(d), Math.Max(2.2, Math.Min(5, Math.Abs(d) * 4)) * dt);
                    if (Math.Abs(WrapPi(AimTh - BeamTh)) < 0.12) catchUp = false;
                }
                else BeamTh += d * Math.Min(1, dt * 9);
            }
            else BeamTh += (0.4 + 7 * Spin) * dt;
            wasAiming = Aiming;
            Spin = Math.Max(0, Spin - dt / 1.3); Flash = Math.Max(0, Flash - dt / 0.9);
        }

        /// <summary>beamHits: 빛줄기가 배를 비추는지(0~1). 바닥 평면으로 되돌려 각도를 비교한다.</summary>
        public double BeamHits(double th0, double sx, double sy)
        {
            double gx = sx - W / 2.0, gy = (sy - Terrain.CY - Terrain.IsleY) / Terrain.KY, ang = Math.Atan2(gy, gx);
            if (Hyp(gx, gy) > 230) return 0;
            double best = 0;
            foreach (double th in new[] { th0, th0 + Math.PI })
            {
                double d = Math.Abs(((ang - th) % (Math.PI * 2) + Math.PI * 3) % (Math.PI * 2) - Math.PI);
                best = Math.Max(best, Math.Max(0, 1 - d / 0.3));
            }
            return best;
        }

        /// <summary>lampPos(P): 램프 중심의 설계 y.</summary>
        public static double LampY(int stage) => Terrain.CY - (TowerParams.ForStage(stage).Zt * Terrain.VZ + 0.5 + 1.5 + 3);

        // ================= 연출 입자 수명 (원본은 그리는 곳에서 갱신) =================
        void UpdateParticles(double dt)
        {
            for (int i = Rings.Count - 1; i >= 0; i--) { Rings[i].Age += dt; if (Rings[i].Age > 2.4) Rings.RemoveAt(i); }
            for (int i = Birds.Count - 1; i >= 0; i--) { var b = Birds[i]; b.Life -= dt; if (b.Life <= 0) { Birds.RemoveAt(i); continue; } b.X += b.Vx * dt; b.Y += b.Vy * dt; }
            for (int i = Sparks.Count - 1; i >= 0; i--) { var s = Sparks[i]; if (s.Delay > 0) { s.Delay -= dt; continue; } s.Life -= dt; if (s.Life <= 0) { Sparks.RemoveAt(i); continue; } s.X += s.Vx * dt; s.Y += s.Vy * dt; s.Vy += 38 * dt; s.Vx *= 0.985; }
            for (int i = Fx.Count - 1; i >= 0; i--) { var f = Fx[i]; f.Life -= dt; if (f.Life <= 0) { Fx.RemoveAt(i); continue; } f.X += f.Vx * dt; f.Y += f.Vy * dt; f.Vy += 6 * dt; }
            for (int i = Pops.Count - 1; i >= 0; i--) { Pops[i].Age += dt; if (Pops[i].Age > 1.7) Pops.RemoveAt(i); }
        }

        // ================= 공사·승급 (원본 3182~3215행, 5206행) =================
        /// <summary>공사 시작: 조건이 차면 알아서 시작한다.</summary>
        public void StartBuild()
        {
            double sec = Ctx.T.BuildSec[Ctx.Stage]; long now = Ctx.Clock.NowMs;
            Ctx.S.Build = new BuildState { To = Ctx.Stage + 1, StartAt = now, EndAt = now + sec * 1000 };
            Ctx.NotifyChanged();
            SendToast(Str.Get("world.buildStart"), Str.F("world.buildStartSub", Ctx.T.StageName[Ctx.Stage + 1]));
        }

        public void FinishBuild()
        {
            Ctx.Stage = (int)Ctx.S.Build.To; Ctx.S.Stage = Ctx.Stage; Ctx.S.Build = null; Ctx.NotifyChanged();
            SetKeeperSpots();
            finaleHold = Perf + 7000;
            Celebrate(); Say("build");
            SendToast(GameContext.FixJosa(Str.F("world.buildDone", Ctx.T.StageName[Ctx.Stage])), Str.F("world.buildDoneSub", Ctx.T.StageRoles[Ctx.Stage]));
            Rebuild?.Invoke("stage");
        }

        /// <summary>시뮬레이터용: 공사 시간 없이 바로 올린다(원본 simulate 의 check).</summary>
        public int UpgradeInstant(Action<int> onStage = null)
        {
            int n = 0;
            while (Ctx.CanUpgrade()) { Ctx.Stage++; Ctx.S.Stage = Ctx.Stage; n++; onStage?.Invoke(Ctx.Stage); }
            return n;
        }

        void UpdateBuild(long now)
        {
            var S = Ctx.S;
            if (S.Build != null) { if (now >= S.Build.EndAt) FinishBuild(); }
            else if (Ctx.CanUpgrade()) StartBuild();
            else if (!(S.Finale != 0) && Ctx.Stage >= Ctx.T.MaxStage && Flash <= 0 && Spin <= 0 && Sparks.Count == 0 && Perf >= finaleHold) Ctx.DeliverFinale(false);   // 6단계 완결 편지 (한 번)
        }

        /// <summary>celebrate: 램프 번쩍, 빛 한 바퀴, 갈매기, 불꽃놀이(불꽃은 시각에 맞춰 터뜨린다: Fireworks 대기열).</summary>
        public void Celebrate()
        {
            Flash = 1; Spin = 1;
            double ly = LampY(Ctx.Stage);
            for (int k = 0; k < 8; k++) Birds.Add(new Bird { X = W / 2.0 + (R() - 0.5) * 16, Y = ly + 8, Vx = (R() < 0.5 ? -1 : 1) * (12 + R() * 22), Vy = -(8 + R() * 14), Life = 4, Ph = R() * 6 });
            for (int b = 0; b < 6; b++)
            {
                double x = W / 2.0 + (R() - 0.5) * 90, y = Terrain.CY - 70 - R() * 50; int col = (int)(R() * 5);
                for (int i = 0; i < 24; i++) { double a = i / 24.0 * Math.PI * 2, sp = 20 + R() * 14; Sparks.Add(new Spark { X = x, Y = y, Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, Life = 1.3, Delay = b * 0.38, Col = col }); }     // 원본은 380ms 간격 setTimeout
            }
            Celebrated?.Invoke();
        }

        // ================= 안내 완료 한 척의 규칙 처리 (실제 배·시뮬레이터가 같이 쓴다) =================
        /// <summary>
        /// 배 한 척이 안내를 마쳤을 때 규칙 쪽 처리: CondOv 를 cond 로 덮고 RewardShip, quiet 이면 AutoOfferAll 을 따로 부른다(원본 simulate 와 같은 순서).
        /// 실제 배는 cond=null(지금의 날씨·밤낮), quiet=false. 시뮬레이터 구동은 cond=SampleCond(), quiet=true.
        /// </summary>
        public RewardResult Arrive(string type, Cond cond, bool quiet)
        {
            Ctx.CondOv = cond;
            var rw = Ctx.RewardShip(type, false, quiet);
            Ctx.CondOv = null;
            if (quiet) Ctx.AutoOfferAll();
            return rw;
        }

        /// <summary>접속하지 않은 동안의 보상(앱 시작·복귀). 90초 미만이면 null.</summary>
        public OfflineResult ReturnFromAway(double awaySec) => Ctx.RunOffline(awaySec);

        void Bump(string k) { Ctx.S.Stats.TryGetValue(k, out double v); Ctx.S.Stats[k] = v + 1; }
        string GiftName(string id) => Ctx.T.GiftById.TryGetValue(id, out var g) ? g.Name : id;
    }
}
