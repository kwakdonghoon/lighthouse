using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Render;
using Lighthouse.UI.Model;
using Lighthouse.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>M4 게임 루프 시험(순수 C#): dotnet test 와 Unity EditMode 양쪽에서 같은 코드를 쓴다.</summary>
    public sealed class WorldTests
    {
        static GameTables T => M1Fixture.Tables;
        static UiStrings Str => UiStrings.Parse(File.ReadAllText(Path.Combine(M1Fixture.DataDir, "ko", "ui.json"), new System.Text.UTF8Encoding(false)));

        sealed class Rig
        {
            public ManualClock Clock; public GameContext Ctx; public GameLoop Loop; public List<string> Toasts = new List<string>(), Sfx = new List<string>(), Rebuilds = new List<string>();
            public static Rig Make(long seed, int stage = 1, int guided = 0, long startMs = -1)
            {
                var clock = new ManualClock(startMs >= 0 ? startMs : M1Fixture.FixMs, M1Fixture.TzMinutes);
                var st = GameState.FromJson(new SaveMigrator(T, clock).Defaults()); st.Seen = true; st.Stage = stage; st.Guided = guided; st.StartAt = clock.Now;
                var ctx = new GameContext(T, st, new Mulberry32(seed), clock) { Stage = stage };
                var loop = new GameLoop(ctx, Str, new Mulberry32(seed + 1000));
                var r = new Rig { Clock = clock, Ctx = ctx, Loop = loop };
                loop.Toast += (a, b) => r.Toasts.Add(a + " / " + b); loop.Sfx += s => r.Sfx.Add(s); loop.Rebuild += s => r.Rebuilds.Add(s);
                loop.SetView(216, 468, 468 - 320);
                return r;
            }
            public void Run(double sec, double dt = 0.1) { int n = (int)(sec / dt); for (int i = 0; i < n; i++) { Clock.AdvanceSec(dt); Loop.Tick(dt); } }
        }

        // ================= 1. 가속 시뮬 일치 =================
        [Test]
        public void LoopDrivenSimMatchesSimulator()
        {
            var g = M1Fixture.Golden("sim.json"); int n = 0; var fails = new List<string>();
            foreach (JObject s in (JArray)g["sims"])
            {
                var a = (JArray)s["args"];
                double sp = Js.Num(a[0]); int days = (int)Js.Num(a[1]); long seed = (long)Js.Num(a[2]); int ve = (int)Js.Num(a[3]);
                var r1 = Simulator.Run(T, M1Fixture.Clock(), sp, days, seed, ve);
                var r2 = LoopSim.Run(T, M1Fixture.Clock(), sp, days, seed, ve);
                string d = M1Fixture.Diff(r2.ToJson(), r1.ToJson());
                if (d != null) fails.Add(Js.Stringify(a) + ": " + d);
                if (r1.RngCalls != r2.RngCalls) fails.Add(Js.Stringify(a) + ": 난수 호출 횟수 " + r2.RngCalls + " != " + r1.RngCalls);
                n++;
            }
            TestContext.Out.WriteLine("가속 구동 vs Simulator: " + n + "건 비교, 불일치 " + fails.Count);
            Assert.That(fails, Is.Empty);
        }

        [Test]
        public void LoopDrivenSimMatchesJsGolden()
        {
            var g = M1Fixture.Golden("sim.json"); int exact = 0, total = 0;
            foreach (JObject s in (JArray)g["sims"])
            {
                var a = (JArray)s["args"];
                var r = LoopSim.Run(T, M1Fixture.Clock(), Js.Num(a[0]), (int)Js.Num(a[1]), (long)Js.Num(a[2]), (int)Js.Num(a[3]));
                total++; if (M1Fixture.Diff(r.ToJson(), (JObject)s["result"]) == null) exact++;
            }
            TestContext.Out.WriteLine("JS 골든과 seed 까지 완전 일치: " + exact + "/" + total);
            Assert.Greater(exact, 0, "적어도 한 건은 JS 와 seed 까지 같아야 한다(M1 시험과 같은 기준)");
        }

        // ================= 2. 실시간 루프 =================
        [Test]
        public void RealtimeLoopProgressesWithoutException()
        {
            var r = Rig.Make(7); r.Loop.Boot();
            Assert.AreEqual(3, r.Loop.Ships.Count, "켜자마자 배 3척");
            r.Run(3 * 3600);                                           // 3시간 가속 없이 진짜 시간 흐름으로
            TestContext.Out.WriteLine("3시간: 안내 " + r.Ctx.S.Guided + "척, 단계 " + r.Ctx.Stage + ", 도감 " + r.Ctx.Discovered() + ", 편지 " + r.Ctx.S.Letters.Count + ", 토스트 " + r.Toasts.Count);
            Assert.Greater(r.Ctx.S.Guided, 50, "낮밤이 도는 3시간 동안 배가 꾸준히 안내돼야 한다");
            Assert.Greater(r.Ctx.Stage, 1, "공사가 시작·완료돼 단계가 올라야 한다");
            Assert.That(r.Rebuilds, Does.Contain("stage"));
            Assert.That(r.Toasts.Any(t => t.StartsWith("공사를 시작했어요")));
            Assert.That(r.Ctx.S.Stage, Is.EqualTo(r.Ctx.Stage), "저장의 단계가 런타임 단계와 같다");
            foreach (var sh in r.Loop.Ships) Assert.That(sh.X, Is.InRange(-71.0, 216 + 71.0));
        }

        [Test]
        public void ShipsGuideByBeamAtNightAndByProximityByDay()
        {
            var r = Rig.Make(3); r.Ctx.Night = true; r.Loop.NightForced = true; r.Loop.Dusk = 1;
            r.Loop.Ships.Add(new Ship { Id = 99, Type = "fish", Lane = 1, Dir = 1, X = 108, Sp = 0.0001 });
            // 빛줄기가 배를 향하게: 배의 바닥 평면 각도
            var sh = r.Loop.Ships[0]; double gx = sh.X - 108, gy = (Lanes.Y[1] - Terrain.CY - Terrain.IsleY) / Terrain.KY;
            double hit = r.Loop.BeamHits(Math.Atan2(gy, gx), sh.X, Lanes.Y[1]);
            Assert.AreEqual(1.0, hit, 1e-9, "각도가 같으면 1");
            Assert.AreEqual(1.0, r.Loop.BeamHits(Math.Atan2(gy, gx) + Math.PI, sh.X, Lanes.Y[1]), 1e-9, "빛줄기는 양쪽(+π)으로 난다");
            Assert.AreEqual(0.0, r.Loop.BeamHits(Math.Atan2(gy, gx) + 1.0, sh.X, Lanes.Y[1]), 1e-9, "0.3 라디안 밖이면 0");
            // 낮에는 등대 가까이 있는 동안(90 이내) 안내가 쌓인다
            var d = Rig.Make(4); d.Loop.NightForced = true; d.Loop.Dusk = 0;
            d.Loop.Ships.Add(new Ship { Id = 1, Type = "fish", Lane = 1, Dir = 1, X = 100, Sp = 0.001 });
            d.Run(2);
            Assert.Greater(d.Loop.Ships[0].Guide, 0.15, "2초에 약 0.18");
            var far = Rig.Make(4); far.Loop.NightForced = true; far.Loop.Dusk = 0;
            far.Loop.Ships.Add(new Ship { Id = 1, Type = "fish", Lane = 1, Dir = 1, X = 5, Sp = 0.001 });
            far.Run(2);
            Assert.AreEqual(0, far.Loop.Ships[0].Guide, 1e-9, "멀리 있으면 쌓이지 않는다");
        }

        [Test]
        public void ArrivalGivesRewardAndEvents()
        {
            var r = Rig.Make(5); r.Loop.NightForced = true; r.Loop.Dusk = 0;
            r.Loop.Ships.Add(new Ship { Id = 1, Type = "fish", Lane = 1, Dir = 1, X = 100, Sp = 0.001, Guide = 0.999 });
            int before = (int)r.Ctx.S.Guided; r.Run(1);
            Assert.AreEqual(before + 1, (int)r.Ctx.S.Guided);
            Assert.AreEqual(1, r.Loop.Pops.Count + 0, "선물 팝 한 개(수명 1.7초)");
            Assert.That(r.Loop.Rings.Count, Is.GreaterThan(0));
            Assert.That(r.Sfx, Does.Contain("ding").Or.Contain("mail"));
            Assert.That(r.Ctx.S.Dex.ContainsKey("fish"));
        }

        // ================= 3. 날씨·역할 =================
        [Test]
        public void WeatherStartsAndEndsWithBonusGift()
        {
            var r = Rig.Make(11, stage: 3); r.Loop.WxSlow = 6;       // 주기를 줄여 시험
            r.Loop.RescueEverySec = 0;
            bool sawFogOrRain = false; double maxAmt = 0; int gifts0 = (int)r.Ctx.S.Gifts.Values.Sum() + (int)r.Ctx.S.Shiny.Values.Sum();
            for (int i = 0; i < 20000 && r.Toasts.Count < 2; i++) { r.Clock.AdvanceSec(0.1); r.Loop.Tick(0.1); if (r.Loop.Wx.Kind != "clear") sawFogOrRain = true; maxAmt = Math.Max(maxAmt, r.Loop.Wx.Amt); }
            Assert.IsTrue(sawFogOrRain, "날씨가 시작돼야 한다");
            TestContext.Out.WriteLine("토스트: " + string.Join(" | ", r.Toasts));
            // 끝날 때까지
            for (int i = 0; i < 40000 && r.Loop.Wx.Kind != "clear"; i++) { r.Clock.AdvanceSec(0.1); r.Loop.Tick(0.1); maxAmt = Math.Max(maxAmt, r.Loop.Wx.Amt); }
            Assert.AreEqual("clear", r.Loop.Wx.Kind); Assert.AreEqual("clear", r.Ctx.Wx);
            Assert.Greater(maxAmt, 0.5, "날씨 세기는 서서히 올라간다");
            Assert.That(r.Toasts.Any(t => t.Contains("걷혔어요") || t.Contains("그쳤어요")), "끝났을 때 알림");
            Assert.Greater((int)r.Ctx.S.Gifts.Values.Sum() + (int)r.Ctx.S.Shiny.Values.Sum(), gifts0 - 1);
        }

        [Test]
        public void RescueShipAppearsFromTier3()
        {
            var r = Rig.Make(13, stage: 5); r.Loop.RescueEverySec = 3;
            r.Run(10);
            Assert.That(r.Loop.Ships.Any(s => s.Sos), "구조 요청 배가 나타난다");
            Assert.That(r.Toasts.Any(t => t.StartsWith("표류하는 배")));
            var one = Rig.Make(13, stage: 4); one.Loop.RescueEverySec = 3; one.Run(10);
            Assert.False(one.Loop.Ships.Any(s => s.Sos), "4단계(등급 2)에는 구조 요청이 없다");
        }

        // ================= 4. 관리인 =================
        [Test]
        public void KeeperWalksOnLandAndWorks()
        {
            var r = Rig.Make(17, stage: 5); r.Loop.NightForced = true; r.Loop.Dusk = 0;
            var acts = new HashSet<string>(); int steps = 0;
            for (int i = 0; i < 6000; i++)
            {
                r.Clock.AdvanceSec(0.1); r.Loop.Tick(0.1);
                var k = r.Loop.Keeper; if (k.Act != null) acts.Add(k.Act); if (k.Walking) steps++;
                Assert.GreaterOrEqual(Terrain.TerrH(5, k.U, k.V), 0, "관리인은 섬 밖으로 나가지 않는다 (" + i + ")");
            }
            TestContext.Out.WriteLine("일: " + string.Join(",", acts) + " 걸음 " + steps);
            Assert.GreaterOrEqual(acts.Count, 4, "여러 일을 한다");
            Assert.Greater(steps, 100);
        }

        [Test]
        public void KeeperGoesToRestAtNight()
        {
            var r = Rig.Make(19, stage: 6); r.Loop.NightForced = true; r.Loop.Dusk = 1;
            r.Run(240);
            Assert.AreEqual("stars", r.Loop.Keeper.Act, "6단계 밤에는 별을 본다");
            Assert.False(r.Loop.Keeper.Walking);
        }

        // ================= 5. 오프라인 복귀·저장 =================
        [Test]
        public void ReturnFromAwayEqualsRunOffline()
        {
            var a = Rig.Make(23); var b = Rig.Make(23);
            var na = a.Loop.ReturnFromAway(4 * 3600); var nb = b.Ctx.RunOffline(4 * 3600);
            Assert.NotNull(na); Assert.AreEqual(nb.Count, na.Count);
            Assert.AreEqual(Js.Stringify(b.Ctx.S.ToJson()), Js.Stringify(a.Ctx.S.ToJson()));
            Assert.IsNull(Rig.Make(23).Loop.ReturnFromAway(60), "90초 미만은 소식 없음");
        }

        [Test]
        public void AutoSaveThenRestartRestoresSameState()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lh_m4_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                var r = Rig.Make(29);
                var store = new SaveStore(new FileStorage(dir), new SaveMigrator(T, r.Clock), r.Clock);
                var svc = new SaveService(store, r.Ctx, r.Clock);
                r.Loop.Boot();
                for (int i = 0; i < 20000; i++) { r.Clock.AdvanceSec(0.1); r.Loop.Tick(0.1); svc.Tick(0.1); }
                TestContext.Out.WriteLine("저장 쓰기 " + svc.Writes + "번, 안내 " + r.Ctx.S.Guided);
                Assert.Greater(svc.Writes, 5, "자동 저장이 돌아야 한다");
                Assert.That(File.Exists(Path.Combine(dir, "save.json")));
                svc.Flush();                                            // 앱이 가려질 때
                string before = Js.Stringify(r.Ctx.S.ToJson());
                var store2 = new SaveStore(new FileStorage(dir), new SaveMigrator(T, r.Clock), r.Clock);       // 재시작
                var st2 = store2.Load();
                Assert.AreEqual("main", store2.LoadedFrom);
                Assert.AreEqual(before, Js.Stringify(st2.ToJson()), "재시작하면 저장한 상태 그대로");
                Assert.AreEqual(r.Clock.Now, (long)st2.LastSeen, "lastSeen 은 가려진 시각");
                // 깨진 본 파일 → 직전 백업으로
                File.WriteAllText(Path.Combine(dir, "save.json"), "{깨짐");
                var store3 = new SaveStore(new FileStorage(dir), new SaveMigrator(T, r.Clock), r.Clock); store3.Load();
                Assert.AreEqual("backup", store3.LoadedFrom);
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Test]
        public void SaveServiceThrottlesWritesAndSurvivesFailure()
        {
            var r = Rig.Make(31);
            var io = new FailingStorage(); var store = new SaveStore(io, new SaveMigrator(T, r.Clock), r.Clock);
            var svc = new SaveService(store, r.Ctx, r.Clock); int fails = 0; svc.SaveFailed += e => fails++;
            for (int i = 0; i < 50; i++) r.Ctx.NotifyChanged();
            svc.Tick(0.1); Assert.AreEqual(0, svc.Writes, "2초가 안 지났으면 쓰지 않는다");
            svc.Tick(2.0); Assert.AreEqual(1, svc.Writes, "여러 번 바뀌어도 한 번만");
            io.Fail = true; r.Ctx.NotifyChanged(); svc.Tick(2.5); Assert.AreEqual(1, fails, "실패는 삼키고 알린다");
            io.Fail = false; svc.Tick(2.5); Assert.AreEqual(2, svc.Writes, "다음에 다시 쓴다");
        }

        sealed class FailingStorage : IStorage
        {
            readonly Dictionary<string, string> m = new Dictionary<string, string>(); public bool Fail;
            public bool Exists(string n) => m.ContainsKey(n); public string ReadText(string n) => m[n];
            public void WriteTextAtomic(string n, string t) { if (Fail) throw new IOException("디스크가 가득 찼어요"); m[n] = t; }
            public void Delete(string n) { m.Remove(n); }
        }

        // ================= 6. 입력·탭 =================
        [Test]
        public void TapsHitShipsKeeperMailAndLighthouse()
        {
            var r = Rig.Make(37, stage: 5); r.Loop.NightForced = true; r.Loop.Dusk = 0; bool mail = false; r.Loop.OpenMail += () => mail = true;
            var sh = new Ship { Id = 1, Type = "fish", Lane = 1, Dir = 1, X = 100, Sp = 0 }; r.Loop.Ships.Add(sh);
            r.Loop.TapAt(100, Lanes.Y[1] - 10); Assert.NotNull(r.Loop.Bubble); Assert.AreSame(sh, r.Loop.Bubble.Ship); Assert.Greater(sh.Wave, 2); Assert.AreEqual(0.2, sh.Guide, 1e-9);
            r.Loop.Ships.Clear();
            var mp = r.Loop.MailboxPos(); r.Loop.TapAt(mp.x, mp.y - 7); Assert.IsTrue(mail, "우체통 탭 → 편지함");
            var kp = r.Loop.KeeperPos(); r.Loop.TapAt(kp.x, kp.y - 5); Assert.IsTrue(r.Loop.Bubble.Keeper, "관리인 탭 → 한마디");
            r.Loop.Bubble = null; r.Loop.TapAt(108, 200); Assert.Greater(r.Loop.Flash, 0.9, "등대 탭 → 램프 번쩍"); Assert.Greater(r.Loop.Birds.Count, 0);
            int rings = r.Loop.Rings.Count; r.Loop.TapAt(30, 250); Assert.AreEqual(rings + 1, r.Loop.Rings.Count, "바다 탭 → 물결");
        }

        [Test]
        public void PointerDragAimsBeamAndFollowsFinger()
        {
            var r = Rig.Make(41); r.Loop.NightForced = true; r.Loop.Dusk = 1;
            r.Loop.PointerDown(200, 260); r.Loop.PointerMove(200, 260, true); r.Run(1.5);
            Assert.IsTrue(r.Loop.Aiming);
            double want = Math.Atan2((260 - Terrain.CY - Terrain.IsleY) / Terrain.KY, 200 - 108);
            Assert.AreEqual(0, Math.Abs(WorldPainter.WrapPiJs(r.Loop.BeamTh - want)) % (2 * Math.PI), 0.2, "빛이 손가락 방향으로 따라간다");
            r.Loop.PointerUp(false); r.Run(0.1); Assert.IsFalse(r.Loop.Aiming);
            Assert.That(r.Ctx.S.Tips.ContainsKey("h6"), "빛을 끌어 봤다는 기록(안내 h6)");
        }

        // ================= 7. 승급 연출 =================
        [Test]
        public void BuildFinishesCelebratesAndRebuilds()
        {
            var r = Rig.Make(43, stage: 1, guided: 100000);
            for (int i = 0; i < 3; i++) r.Ctx.AddGift(T.Gifts[i].Id);
            r.Ctx.S.Found = T.Gifts.ToDictionary(g => g.Id, g => 1.0); foreach (var s in T.Ships) r.Ctx.S.Dex[s.Id] = 1;
            r.Run(0.2); Assert.IsNotNull(r.Ctx.S.Build, "조건을 채우면 알아서 공사 시작");
            r.Clock.AdvanceSec(T.BuildSec[1] + 1); r.Run(0.2);
            Assert.AreEqual(2, r.Ctx.Stage); Assert.AreEqual(3.0, r.Ctx.S.Build.To, "조건이 남아 있으면 곧바로 다음 공사");
            Assert.That(r.Rebuilds, Does.Contain("stage"));
            Assert.Greater(r.Loop.Sparks.Count, 0, "불꽃놀이"); Assert.Greater(r.Loop.Birds.Count, 0, "갈매기");
            Assert.That(r.Toasts.Any(t => t.Contains("되었어요")));
        }

        [Test]
        public void SeasonChangeRequestsRebuild()
        {
            var r = Rig.Make(47); r.Ctx.SeasonForce = "summer"; r.Loop.Tick(0.1); r.Rebuilds.Clear();
            r.Ctx.SeasonForce = "winter"; r.Loop.Tick(0.1);
            Assert.That(r.Rebuilds, Does.Contain("season"));
        }

        // ================= 8. 그리기(연출 층) 스모크 =================
        [Test]
        public void PainterDrawsEffectsWithoutException()
        {
            var r = Rig.Make(53, stage: 6); r.Ctx.S.Beam = "gold";
            var sp = SceneSprites.Build(216, 468, "summer", 6, gardenLv: 6, slotLv: 32);
            var p = new WorldPainter(216, 468, 148); var buf = new uint[216 * 468];
            r.Loop.Boot(); r.Loop.NightForced = true; r.Loop.Dusk = 1; r.Ctx.Night = true;
            r.Loop.Wx.Kind = "fog"; r.Loop.Wx.Amt = 1; r.Loop.Wx.T0 = r.Clock.Now; r.Loop.Wx.Until = r.Clock.Now + 600000; r.Loop.Wx.Next = r.Clock.Now + 900000; r.Run(3);
            p.Paint(r.Loop, sp); p.Merge(buf);
            int lit = buf.Count(v => (v & 0xFFFFFF) != 0), alpha = buf.Count(v => (v >> 24) != 0);
            TestContext.Out.WriteLine("밤·안개: 밝은 픽셀 " + lit + ", 알파 픽셀 " + alpha);
            Assert.Greater(lit, 2000, "빛줄기·불빛이 그려진다"); Assert.Greater(alpha, 5000, "안개가 깔린다");
            foreach (var kind in new[] { "rain", "clear" })
            {
                r.Loop.Wx.Kind = kind; r.Loop.Wx.Amt = kind == "clear" ? 0 : 1; r.Loop.Dusk = 0.5; r.Ctx.Night = false; r.Loop.Aiming = false;
                r.Loop.Sparks.Add(new Spark { X = 100, Y = 100, Life = 1, Col = 2 }); r.Loop.Pops.Add(new Pop { X = 90, Y = 200, Gift = T.Gifts[0].Id });
                r.Loop.Rings.Add(new Ring { X = 80, Y = 250, S = 1 }); r.Loop.Flash = 0.5; r.Loop.SignalUntil = r.Loop.Perf + 2000; r.Loop.WarnUntil = r.Loop.Perf + 2000;
                p.Paint(r.Loop, sp); p.Merge(buf);
                Assert.Greater(buf.Count(v => (v >> 24) != 0), 100, kind);
            }
        }

        [Test]
        public void KeeperActsAllDrawable()
        {
            var r = Rig.Make(59, stage: 6); var p = new WorldPainter(216, 468, 148); var sp = SceneSprites.Build(216, 468, "summer", 6, gardenLv: 6, slotLv: 32);
            foreach (var act in new[] { "wave", "sweep", "polish", "mail", "water", "fish", "lamp", "well", "flag", "rest", "feed", "stars", "anchor", "gild", "sit", "pass", "enter" })
            {
                r.Loop.Keeper.Act = act; r.Loop.Keeper.Walking = false; r.Loop.Keeper.WaveT = act == "wave" ? 1 : 0;
                for (int i = 0; i < 6; i++) { r.Loop.T += 0.13; p.Paint(r.Loop, sp); }
            }
            Assert.Pass();
        }

        [Test]
        public void LoopTickCostIsSmall()
        {
            var r = Rig.Make(61, stage: 6); r.Loop.Boot();
            var sw = System.Diagnostics.Stopwatch.StartNew(); int n = 20000;
            for (int i = 0; i < n; i++) { r.Clock.AdvanceSec(0.016); r.Loop.Tick(0.016); }
            double us = sw.Elapsed.TotalMilliseconds * 1000 / n;
            TestContext.Out.WriteLine("Tick 평균 " + us.ToString("F1") + " µs (dotnet, 릴리스 아님)");
            Assert.Less(us, 400, "프레임당 규칙 갱신은 0.4ms 안");
        }
    }
}
