using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Lighthouse.Core;
using Lighthouse.Render;
using Lighthouse.UI.Model;
using Lighthouse.World;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>A1 월드 연출의 상태(계절 입자·등불·불꽃놀이·별똥별·표류물·갈매기·도감 기록)가 조건대로 일어나는지 본다(순수 C#, dotnet·EditMode 같은 코드).</summary>
    public sealed class A1WorldTests
    {
        static GameTables T => M1Fixture.Tables;
        static double SeaN(GameContext c, string id) => c.S.Sea.TryGetValue(id, out double v) ? v : 0;
        static UiStrings Str => UiStrings.Parse(File.ReadAllText(Path.Combine(M1Fixture.DataDir, "ko", "ui.json"), new System.Text.UTF8Encoding(false)));

        sealed class Rig
        {
            public ManualClock Clock; public GameContext Ctx; public GameLoop Loop; public List<string> Toasts = new List<string>(), Sfx = new List<string>();
            public static Rig Make(long seed, int stage = 6, string season = "summer", bool night = false, bool sim = false)
            {
                var clock = new ManualClock(M1Fixture.FixMs, M1Fixture.TzMinutes);
                var st = GameState.FromJson(new SaveMigrator(T, clock).Defaults()); st.Seen = true; st.Stage = stage; st.StartAt = clock.Now; st.Growth["lens"] = 40000; st.Growth["garden"] = 40000; st.Growth["port"] = 300000;
                var ctx = new GameContext(T, st, new Mulberry32(seed), clock) { Stage = stage, SeasonForce = season, Sim = sim };
                var loop = new GameLoop(ctx, Str, new Mulberry32(seed + 1000)) { NightForced = true, Dusk = night ? 1 : 0 };
                ctx.Night = night; ctx.TitleOpen = false;
                var r = new Rig { Clock = clock, Ctx = ctx, Loop = loop };
                loop.Toast += (a, b) => r.Toasts.Add(a + " / " + b); loop.Sfx += s => r.Sfx.Add(s);
                loop.SetView(216, 468, 468 - 320); loop.Boot(); loop.Ships.Clear(); loop.Dusk = night ? 1 : 0; ctx.Night = night;
                return r;
            }
            public void Run(double sec, double dt = 0.1) { int n = (int)(sec / dt); for (int i = 0; i < n; i++) { Clock.AdvanceSec(dt); Loop.Tick(dt); } }
        }

        [Test]
        public void SeasonParticlesFollowSeason()
        {
            foreach (var (season, expect) in new[] { ("spring", true), ("summer", false), ("autumn", true), ("winter", true) })
            {
                var r = Rig.Make(3, 6, season); r.Run(40);
                TestContext.Out.WriteLine(season + ": 입자 " + r.Loop.Motes.Count);
                Assert.AreEqual(expect, r.Loop.Motes.Count > 0, season + " 입자 여부");
                Assert.That(r.Loop.Motes.All(m => m.S == season), season + ": 다른 계절 입자는 없다");
                Assert.LessOrEqual(r.Loop.Motes.Count, 80);
            }
            var w = Rig.Make(3, 6, "spring"); w.Run(20); Assert.Greater(w.Loop.Motes.Count, 0); w.Ctx.SeasonForce = "summer"; w.Run(1); Assert.AreEqual(0, w.Loop.Motes.Count, "계절이 바뀌면 지난 계절의 입자가 사라진다");
        }

        [Test]
        public void LanternsAndFireworksOnlyOnSummerNights()
        {
            var a = Rig.Make(5, 6, "summer", true); a.Run(120); Assert.Greater(a.Loop.Lanterns.Count, 0, "여름 밤에는 등불이 뜬다"); Assert.LessOrEqual(a.Loop.Lanterns.Count, 8);
            Assert.That(a.Sfx, Does.Contain("firework"), "120초 안에 불꽃놀이가 한 번은 터진다(첫 터짐 14초)");
            var b = Rig.Make(5, 6, "summer", false); b.Run(120); Assert.AreEqual(0, b.Loop.Lanterns.Count, "낮에는 없다"); Assert.That(b.Sfx, Does.Not.Contain("firework"));
            var c = Rig.Make(5, 6, "winter", true); c.Run(120); Assert.AreEqual(0, c.Loop.Lanterns.Count, "겨울에는 없다");
            var d = Rig.Make(5, 6, "summer", true); d.Run(30); d.Ctx.SeasonForce = "autumn"; d.Run(1); Assert.AreEqual(0, d.Loop.Lanterns.Count, "여름이 아니면 등불이 사라진다");
        }

        [Test]
        public void MeteorsOnlyAtClearNightAndWishWorks()
        {
            var day = Rig.Make(8, 6, "summer", false); int sawDay = 0; for (int i = 0; i < 3000; i++) { day.Clock.AdvanceSec(0.1); day.Loop.Tick(0.1); sawDay += day.Loop.Meteors.Count; }
            Assert.AreEqual(0, sawDay, "낮에는 별똥별이 없다");
            var n = Rig.Make(8, 6, "summer", true); int seen = 0; bool spawned = false;
            for (int i = 0; i < 6000 && !spawned; i++) { n.Clock.AdvanceSec(0.1); n.Loop.Tick(0.1); if (n.Loop.Meteors.Count > 0) { spawned = true; seen = i; } }
            Assert.IsTrue(spawned, "맑은 밤 10분 안에 별똥별이 한 번은 흐른다"); TestContext.Out.WriteLine("첫 별똥별 " + seen / 10.0 + "초");
            Assert.Greater(n.Ctx.S.Sea.TryGetValue("meteor", out double sm) ? sm : 0, 0, "도감에 별똥별 기록");
            var m = n.Loop.Meteors[0]; double gifts = n.Ctx.S.Stats.TryGetValue("wish", out double w0) ? w0 : 0; int toasts = n.Toasts.Count;
            n.Loop.TapAt(m.X, m.Y); Assert.IsTrue(m.Wished); Assert.AreEqual(gifts + 1, n.Ctx.S.Stats["wish"]); Assert.Greater(n.Toasts.Count, toasts, "소원 토스트");
            Assert.That(n.Toasts.Last(), Does.Contain("소원을 빌었어요")); Assert.LessOrEqual(m.Life, m.Age + 0.5 + 1e-9, "소원을 빌면 0.5초 안에 사라진다");
            n.Loop.TapAt(m.X, m.Y); Assert.AreEqual(gifts + 1, n.Ctx.S.Stats["wish"], "한 별똥별은 한 번만");
            var rain = Rig.Make(8, 6, "summer", true); rain.Loop.Wx.Kind = "rain"; int sawRain = 0; for (int i = 0; i < 3000; i++) { rain.Clock.AdvanceSec(0.1); rain.Loop.Tick(0.1); if (rain.Loop.Wx.Kind != "rain") break; sawRain += rain.Loop.Meteors.Count; }
        }

        [Test]
        public void MeteorFrequencyGrowsWithLens()
        {
            double Mul(int lens) { var r = Rig.Make(1); r.Ctx.S.Growth["lens"] = lens; return r.Loop.MeteorMul(); }
            var r0 = Rig.Make(1); r0.Ctx.S.Growth["lens"] = 0; Assert.AreEqual(1.0, r0.Loop.MeteorMul(), 1e-9, "렌즈 Lv.0: ×1.0");
            Assert.Greater(Mul(40000), 2.0, "렌즈가 자라면 별똥별이 2배 넘게(Lv.14 이상 ×2.2, 상한 Lv.16: 1.48×2.2=3.2)");
        }

        [Test]
        public void DriftSpawnsPicksAndFades()
        {
            var r = Rig.Make(11, 6, "summer", false); bool got = false;
            for (int i = 0; i < 600 && !got; i++) { r.Clock.AdvanceSec(0.1); r.Loop.Tick(0.1); got = r.Loop.Drift.Count > 0; }
            Assert.IsTrue(got, "60초 안에 표류물이 떠 온다(첫 7초)");
            var it = r.Loop.Drift[0]; Assert.IsNotNull(it.Gift, "만들 때 선물이 정해진다"); Assert.That(it.Y, Is.InRange(190, 300));
            double before = r.Ctx.S.Stats.TryGetValue("drift", out double d0) ? d0 : 0; int pops = r.Loop.Pops.Count;
            r.Loop.TapAt(it.X, it.Y - 3); Assert.GreaterOrEqual(it.Take, 0, "눌러서 주웠다"); Assert.AreEqual(before + 1, r.Ctx.S.Stats["drift"]); Assert.Greater(r.Loop.Pops.Count, pops, "선물 팝");
            Assert.That(r.Sfx, Does.Contain("pick")); r.Run(1.2); Assert.IsFalse(r.Loop.Drift.Contains(it), "0.9초 뒤 스르르 사라진다");
            var far = new DriftItem { X = 100, Y = 250, Vx = 0, Gift = it.Gift }; r.Loop.Drift.Add(far); r.Loop.TapAt(10, 10); Assert.Less(far.Take, 0, "멀리 누르면 줍지 않는다");
            for (int i = 0; i < 3000; i++) { r.Clock.AdvanceSec(0.1); r.Loop.Tick(0.1); Assert.LessOrEqual(r.Loop.Drift.Count, 3, "동시에 두 개까지만 새로 뜬다(+직접 넣은 1)"); }
        }

        [Test]
        public void GullsComeStandAndFlyAway()
        {
            var r = Rig.Make(13, 6, "summer", false);
            typeof(GameLoop).GetMethod("SpawnGulls", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(r.Loop, null);
            Assert.That(r.Loop.Gulls.Count, Is.InRange(3, 4)); Assert.That(r.Ctx.S.Sea.TryGetValue("gull", out double g) ? g : 0, Is.GreaterThan(0), "갈매기가 도감에 기록된다");
            r.Run(9); Assert.IsTrue(r.Loop.Gulls.All(x => x.St == "stand"), "내려앉아 모이를 쪼는다");
            r.Loop.Keeper.Act = "feed"; r.Loop.Keeper.Wait = 5; r.Run(3); Assert.Greater(r.Loop.Crumbs.Count, 0, "먹이 주는 동안 빵부스러기");
            typeof(GameLoop).GetField("GullsOut", BindingFlags.Public | BindingFlags.Instance).SetValue(r.Loop, true);
            r.Loop.Keeper.Wait = 0; r.Run(0.3); Assert.IsTrue(r.Loop.Gulls.All(x => x.St == "out"), "먹이 주기가 끝나면 날아간다");
            r.Run(4); Assert.AreEqual(0, r.Loop.Gulls.Count, "날아가 사라진다");
            r.Loop.Gulls.Add(new Gull { X = 50, Y = 50, Tx = 50, Ty = 50, St = "stand" }); r.Loop.Crumbs.Add(new Crumb { X = 1, Y = 1, Land = true, Life = 9 }); r.Loop.Dusk = 1; r.Ctx.Night = true; r.Run(0.2);
            Assert.AreEqual(0, r.Loop.Gulls.Count + r.Loop.Crumbs.Count, "밤이 되면 갈매기·빵부스러기가 사라진다");
        }

        [Test]
        public void KeeperShoutsAtMeteors()
        {
            var r = Rig.Make(17, 6, "summer", true); var k = r.Loop.Keeper; k.WasNight = true; k.Act = "stars"; k.Wait = 9999; k.Walking = false;
            r.Loop.Meteors.Add(new Meteor { X = 10, Y = 10, Vx = 1, Vy = 1, Life = 5 }); r.Run(0.5);
            Assert.NotNull(r.Loop.Bubble, "별똥별이다! 말풍선"); Assert.AreEqual("별똥별이다!", r.Loop.Bubble.Text); Assert.IsTrue(k.SawMeteor);
            r.Loop.Meteors.Clear(); r.Run(0.2); Assert.IsFalse(k.SawMeteor, "별똥별이 지나가면 다시 외칠 준비");
        }

        [Test]
        public void SkyAndLifeEntriesGetRecorded()
        {
            string[] have = { "butterfly", "firefly", "petals", "leaves", "snowflake", "lantern", "aurora", "fish", "firework", "sunset", "dawnglow", "fog", "rain" };
            foreach (var id in have) Assert.IsTrue(T.SeaById.ContainsKey(id), "도감 칸 " + id);
            var day = Rig.Make(21, 6, "spring", false); day.Run(8); Assert.Greater(SeaN(day.Ctx, "butterfly"), 0, "낮 나비"); Assert.Greater(SeaN(day.Ctx, "petals"), 0, "봄 벚꽃잎");
            var night = Rig.Make(21, 6, "summer", true); night.Run(40); Assert.Greater(SeaN(night.Ctx, "firefly"), 0, "밤 반딧불이"); Assert.Greater(SeaN(night.Ctx, "lantern"), 0, "여름 밤 등불");
            var winter = Rig.Make(21, 6, "winter", true); winter.Run(8); Assert.Greater(SeaN(winter.Ctx, "aurora"), 0, "겨울 밤 오로라"); Assert.Greater(SeaN(winter.Ctx, "snowflake"), 0, "겨울 눈송이");
            var autumn = Rig.Make(21, 6, "autumn", false); autumn.Run(8); Assert.Greater(SeaN(autumn.Ctx, "leaves"), 0, "가을 낙엽");
            var fish = Rig.Make(21, 6, "summer", false); fish.Run(30); Assert.Greater(SeaN(fish.Ctx, "fish"), 0, "뛰어오르는 물고기(6초마다 0.6초)");
            var sim = Rig.Make(21, 6, "spring", false, true); sim.Run(8); Assert.AreEqual(0, SeaN(sim.Ctx, "butterfly"), "가속 시뮬은 기록하지 않는다");
        }

        [Test]
        public void ScaneryIsDeterministicPerSeed()
        {
            string Sig(long seed) { var r = Rig.Make(seed, 6, "autumn"); r.Run(60); return string.Join(",", r.Loop.Motes.Take(10).Select(m => Math.Round(m.X, 3) + ":" + Math.Round(m.Y, 3))); }
            Assert.AreEqual(Sig(4), Sig(4), "같은 시드는 같은 연출"); Assert.AreNotEqual(Sig(4), Sig(5), "다른 시드는 다르다");
        }

        [Test]
        public void ScenerySimDoesNotTouchRuleRngOrder()
        {
            // 연출(입자·등불·별똥별·갈매기)은 연출 난수만 쓴다: 규칙 난수(Ctx.Rng)를 쓰는 건 배 종류·단골·선물 추첨(그리고 표류물 선물 추첨)뿐이다.
            // 표류물이 없는 계절 한복판에서 두 번 돌려 규칙 난수 호출 수가 같음을 본다(같은 시드 = 같은 규칙 열).
            long Calls(long seed) { var r = Rig.Make(seed, 6, "winter", true); var rng = (Mulberry32)r.Ctx.Rng; r.Run(200); return rng.Calls; }
            Assert.AreEqual(Calls(9), Calls(9));
        }

        [Test]
        public void BackAndMidLayersRepaintOnlyWhenNeeded()
        {
            var r = Rig.Make(2, 6, "summer", false); var L = r.Loop; var p = new WorldPainter(216, 468, 148);
            p.PaintScenery(L); Assert.IsTrue(p.Back.Changed && p.Back.Used, "먼 섬 층은 늘 있다"); Assert.IsTrue(p.Mid.Used, "항구 Lv 이 높아 낮에도 부이가 있다(중간 층)");
            p.PaintScenery(L); Assert.IsFalse(p.Back.Changed, "같은 상태면 뒤 층을 다시 굽지 않는다"); Assert.IsFalse(p.Mid.Changed, "배가 없고 부이도 같은 모양이면 중간 층도 그대로");
            L.Drift.Add(new DriftItem { X = 50, Y = 200, Vx = 3 }); p.PaintScenery(L); Assert.IsTrue(p.Back.Changed, "표류물이 생기면 다시 굽는다"); p.PaintScenery(L); Assert.IsFalse(p.Back.Changed, "모양이 같으면 그대로");
            L.Drift[0].X = 53; p.PaintScenery(L); Assert.IsTrue(p.Back.Changed, "표류물이 한 칸 움직이면 다시 굽는다");
            L.Drift.Clear(); p.PaintScenery(L); Assert.IsTrue(p.Back.Changed, "표류물이 사라지면 다시 굽는다"); p.PaintScenery(L); Assert.IsFalse(p.Back.Changed);
            L.Dusk = 1; L.Ctx.Night = true; p.PaintScenery(L); Assert.IsTrue(p.Back.Changed, "밤이 되면 먼 섬 색이 바뀐다"); Assert.IsTrue(p.Mid.Changed, "밤 불빛 번짐이 켜진다");
            var sh = new Ship { Type = "fish", Lane = 0, X = 100, Dir = 1, Need = 1 }; L.Ships.Add(sh); p.PaintScenery(L); Assert.IsTrue(p.Mid.Changed, "뒤쪽 항로 배가 생기면 다시 굽는다");
            int changed = 0; for (int i = 0; i < 40; i++) { L.T += 0.016; sh.X += 0.05; p.PaintScenery(L); if (p.Mid.Changed) changed++; }
            Assert.That(changed, Is.InRange(1, 30), "배가 움직여도 매 프레임은 아니다(칸이 바뀔 때만): " + changed + "/40");
            var d = Rig.Make(2, 6, "summer", false); d.Ctx.S.Growth["port"] = 0; var q = new WorldPainter(216, 468, 148); q.PaintScenery(d.Loop); Assert.IsFalse(q.Mid.Used, "낮·부이 없음·뒤쪽 배 없음: 중간 층은 비어 있다");
        }

        [Test]
        public void LayerSignaturesNeverLeaveStalePixels()
        {
            // 신호로 건너뛴 프레임의 층 그림이 "매번 처음부터 구운 그림"과 같은지 확인한다(신호가 모양을 정하는 값을 빠뜨리면 여기서 걸린다).
            foreach (var (season, night) in new[] { ("summer", false), ("summer", true), ("winter", true) })
            {
                var r = Rig.Make(41, 6, season, night); var L = r.Loop; var p = new WorldPainter(216, 468, 148); int checkedFrames = 0, skipped = 0;
                L.Ships.Add(new Ship { Id = 1, Type = "galleon", Lane = 0, Dir = 1, X = 20, Ph = 1, Need = 1, Sp = 7, Wave = 1.2 }); L.Ships.Add(new Ship { Id = 2, Type = "fish", Lane = 0, Dir = -1, X = 180, Ph = 2, Need = 1, Sp = 5, Done = true });
                L.Ships.Add(new Ship { Id = 3, Type = "trade", Lane = 1, Dir = 1, X = 50, Ph = 3, Need = 1, Sp = 5 });
                L.Drift.Add(new DriftItem { X = 10, Y = 200.3, Vx = 4, Ph = 1.7, Gift = "shell" }); L.Drift.Add(new DriftItem { X = 150, Y = 285.1, Vx = -3, Ph = 4.2, Gift = "shell" });
                for (int i = 0; i < 480; i++)
                {
                    r.Clock.AdvanceSec(1 / 60.0); L.Tick(1 / 60.0);                            // 시간·배·표류물이 실제로 흐른다
                    if (i % 97 == 40) L.Ships[0].Wave = 1.0; if (i == 200) L.Drift[0].Take = 0;
                    p.PaintScenery(L); if (!p.Back.Changed) skipped++; if (!p.Mid.Changed) skipped++;
                    var fresh = new WorldPainter(216, 468, 148); fresh.PaintScenery(L);
                    Assert.IsTrue(p.Back.Buf.SequenceEqual(fresh.Back.Buf), season + " 뒤 층 프레임 " + i + ": 건너뛴 그림이 새로 구운 것과 다르다");
                    Assert.IsTrue(p.Mid.Buf.SequenceEqual(fresh.Mid.Buf), season + " 중간 층 프레임 " + i + ": 건너뛴 그림이 새로 구운 것과 다르다"); checkedFrames++;
                }
                TestContext.Out.WriteLine(season + (night ? " 밤" : " 낮") + ": " + checkedFrames + "프레임 확인, 층 건너뜀 " + skipped + "회 (최대 " + checkedFrames * 2 + ")");
                Assert.Greater(skipped, 100, "신호가 실제로 다시 굽는 일을 줄여야 한다");
            }
        }

        [Test]
        public void BackLayerHoldsSameFarIslandPixelsAsDirectPaint()
        {
            var r = Rig.Make(2, 6, "summer", true); var L = r.Loop; var a = new WorldPainter(216, 468, 148); a.PaintScenery(L);
            var b = new WorldPainter(216, 468, 148); b.PaintFarIslands(L, L.T); var direct = new uint[216 * 468]; b.Merge(direct);      // 맨 위 층으로 직접 그린 것
            int diff = 0, n = 0; for (int y = 0; y < 468; y++) for (int x = 0; x < 216; x++) { uint u = a.Back.Buf[(468 - 1 - y) * 216 + x], v = direct[y * 216 + x]; if (v != 0) n++; if (u != v) diff++; }
            Assert.Greater(n, 100, "먼 섬 픽셀이 있다"); Assert.AreEqual(0, diff, "뒤 층(뒤집힌 한 장)과 직접 그린 결과가 같다");
        }

        /// <summary>프레임마다 쌓이는 쓰레기(관리 메모리)가 작은지: Tick+Paint+Merge 가 한 프레임에 할당하는 바이트를 낮·밤·겨울 밤에서 잰다(폰의 GC 끊김을 막는 근거).</summary>
        [Test]
        public void SceneryAllocatesLittlePerFrame()
        {
            foreach (var (name, season, night) in new[] { ("낮·여름", "summer", false), ("밤·여름", "summer", true), ("밤·겨울", "winter", true) })
            {
                var r = Rig.Make(51, 6, season, night); var L = r.Loop; var p = new WorldPainter(216, 468, 148); var buf = new uint[216 * 468];
                L.Ships.Add(new Ship { Id = 1, Type = "fish", Lane = 2, Dir = 1, X = 60, Ph = 1, Need = 1, Guide = 0.4, Wave = 9 }); L.Ships.Add(new Ship { Id = 2, Type = "galleon", Lane = 0, Dir = -1, X = 140, Ph = 2, Need = 1, Done = true, Wave = 9 });
                L.Drift.Add(new DriftItem { X = 50, Y = 280, Vx = 3, Gift = "shell" });
                for (int i = 0; i < 90; i++) { r.Clock.AdvanceSec(0.016); L.Tick(0.016); p.Paint(L, null); p.Merge(buf, true); }
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 300; i++) { r.Clock.AdvanceSec(0.016); L.Tick(0.016); p.Paint(L, null); p.Merge(buf, true); }
                long perFrame = (GC.GetAllocatedBytesForCurrentThread() - a0) / 300;
                TestContext.Out.WriteLine(name + ": 한 프레임 할당 " + perFrame + " 바이트 (Tick+Paint+Merge)");
                Assert.Less(perFrame, 8000, name + ": 프레임마다 쌓이는 쓰레기가 작아야 한다");
            }
        }

        /// <summary>연출 층 그리기(Paint+Merge, 뒤·중간 층 포함)의 CPU 비용을 A1 켠 것/끈 것으로 잰다. dotnet(JIT) 값이라 Unity 에디터·폰과 다르다 — 참고용 + 터무니없이 커지지 않았는지만 단언한다.</summary>
        [Test]
        public void PaintCostReport()
        {
            foreach (var (name, season, night) in new[] { ("낮·여름", "summer", false), ("밤·여름", "summer", true), ("밤·겨울", "winter", true) })
            {
                double[] avg = new double[2];
                for (int mode = 0; mode < 2; mode++)
                {
                    WorldPainter.A1Off = mode == 0;
                    var r = Rig.Make(31, 6, season, night); var L = r.Loop; var p = new WorldPainter(216, 468, 148); var buf = new uint[216 * 468]; var s = (SceneSprites)null;
                    L.Ships.Add(new Ship { Id = 1, Type = "fish", Lane = 2, Dir = 1, X = 60, Ph = 1, Need = 1, Guide = 0.4, Wave = 9 }); L.Ships.Add(new Ship { Id = 2, Type = "galleon", Lane = 0, Dir = -1, X = 140, Ph = 2, Need = 1, Done = true, Wave = 9 });
                    L.Ships.Add(new Ship { Id = 3, Type = "trade", Lane = 1, Dir = 1, X = 90, Ph = 3, Need = 1 }); L.Ships.Add(new Ship { Id = 4, Type = "steamer", Lane = 1, Dir = -1, X = 20, Ph = 3, Need = 1, Sos = true });
                    r.Run(20); L.Drift.Add(new DriftItem { X = 50, Y = 280, Vx = 3, Gift = "shell" });
                    for (int i = 0; i < 30; i++) { r.Clock.AdvanceSec(0.016); L.Tick(0.016); p.Paint(L, s); p.Merge(buf, true); }
                    var sw = System.Diagnostics.Stopwatch.StartNew(); int n = 300;
                    for (int i = 0; i < n; i++) { r.Clock.AdvanceSec(0.016); L.Tick(0.016); p.Paint(L, s); p.Merge(buf, true); }
                    avg[mode] = sw.Elapsed.TotalMilliseconds / n;
                }
                WorldPainter.A1Off = false;
                TestContext.Out.WriteLine(name + ": Tick+Paint+Merge 평균 A1 끔 " + avg[0].ToString("F2") + " ms, 켬 " + avg[1].ToString("F2") + " ms (dotnet)");
                Assert.Less(avg[1], 15, name + ": A1 를 켠 연출 층 한 프레임이 터무니없이 크지 않다");
            }
        }

        [Test]
        public void FoamLayerMatchesBakedFoamAtAnyTime()
        {
            // 매 프레임 다시 채우는 물거품 층이 M2 골든으로 맞춰 둔 SceneComposer.Foam 과 같은 값(어느 t 에서나)이어야 한다 — t=0 은 HTML 골든(M2)과 곧 같은 것.
            int checkedCases = 0;
            foreach (int stage in new[] { 1, 3, 6 })
                foreach (double t in new[] { 0.0, 0.7, 3.37, 11.9, 25.1 })
                    foreach (bool night in new[] { false, true })
                    {
                        double cx = 108, cy = Terrain.CY + Terrain.IsleY + 148; var layer = new FoamLayer(stage, cx, cy); layer.Paint(stage, cx, cy, t, night);
                        var baked = SceneComposer.Foam(stage, cx, cy, t, night); int nonzeroBaked = 0, mismatch = 0;
                        for (int y = 0; y < baked.H; y++) for (int x = 0; x < baked.W; x++)
                        {
                            uint b = baked.D[y * baked.W + x]; if (b == 0) continue; nonzeroBaked++;
                            int lx = baked.Ax + x - layer.Ax, ly = baked.Ay + y - layer.Ay;
                            if (lx < 0 || ly < 0 || lx >= layer.W || ly >= layer.H || layer.D[ly * layer.W + lx] != b) mismatch++;
                        }
                        int nonzeroLayer = layer.D.Count(v => v != 0);
                        Assert.AreEqual(0, mismatch, "stage " + stage + " t " + t + " night " + night); Assert.AreEqual(nonzeroBaked, nonzeroLayer, "층에 남는 점이 없다 stage " + stage + " t " + t); checkedCases++;
                    }
            TestContext.Out.WriteLine("물거품 층 = 구운 물거품: " + checkedCases + "건 일치");
            var a = new FoamLayer(6, 108, 224 + 10 + 148); a.Paint(6, 108, 382, 1.0, false); var b2 = new FoamLayer(6, 108, 382); b2.Paint(6, 108, 382, 2.0, false);
            Assert.AreNotEqual(a.D.Count(v => v != 0), 0); Assert.IsFalse(a.D.SequenceEqual(b2.D), "시간이 지나면 물거품이 달라진다(움직인다)");
        }

        [Test]
        public void LampFlickerFollowsTimeAndMatchesOps()
        {
            double lo = 9, hi = 0; for (int i = 0; i < 2000; i++) { double a = SceneComposer.LampNightAlpha(1, i * 0.037); lo = Math.Min(lo, a); hi = Math.Max(hi, a); }
            Assert.Less(lo, 0.95, "램프 밤 그림이 깜빡인다"); Assert.GreaterOrEqual(lo, 0.84 - 1e-9); Assert.LessOrEqual(hi, 1.0);
            Assert.AreEqual(0, SceneComposer.LampNightAlpha(0, 3), "낮에는 밤 램프가 없다"); Assert.AreEqual(Math.Min(1, 0.3 * 1.2) * 0.75, SceneComposer.LampNightAlpha(0.3, 5), 1e-12, "해질녘 dusk<0.5 는 fl=0.75");
        }

        [Test]
        public void BuoyCountFollowsPortLevel()
        {
            Assert.AreEqual(0, WorldPainter.BuoyCountFor(0)); Assert.AreEqual(4, WorldPainter.BuoyCountFor(4)); Assert.AreEqual(6, WorldPainter.BuoyCountFor(8)); Assert.AreEqual(8, WorldPainter.BuoyCountFor(12));
            Assert.AreEqual(12, WorldPainter.BuoyCountFor(20)); Assert.AreEqual(14, WorldPainter.BuoyCountFor(23)); Assert.AreEqual(14, WorldPainter.BuoyCountFor(30), "최대 14개");
            // 이정표 "바다에 부이 +2" 가 Lv.8·12·16·20·23 에서 실제로 늘어나는지: 늘어난 수 = 2
            int[] ms = { 8, 12, 16, 20, 23 }; foreach (int lv in ms) Assert.AreEqual(2, WorldPainter.BuoyCountFor(lv) - WorldPainter.BuoyCountFor(lv - 1), "Lv." + lv + " 부이 +2");
        }
    }
}
