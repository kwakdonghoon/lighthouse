#if LIGHTHOUSE_DEV
using System;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Dev;
using Lighthouse.UI.Model;
using Lighthouse.World;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>개발 도구(영역 C ④) 시험. LIGHTHOUSE_DEV 가 있을 때만 컴파일된다(출시 구성에서는 이 파일도 없다). 순수 C#: dotnet test -p:LhDev=true 와 Unity EditMode(심볼 켠 때) 공용.</summary>
    public sealed class DevToolsTests
    {
        static GameTables T => M1Fixture.Tables;
        static UiStrings Str => UiStrings.Parse(File.ReadAllText(Path.Combine(M1Fixture.DataDir, "ko", "ui.json"), new System.Text.UTF8Encoding(false)));

        sealed class Rig { public DevClock Clock; public GameContext Ctx; public GameLoop Loop; public SaveMigrator Mig; }
        static Rig Make(int stage = 1)
        {
            var dc = new DevClock(new FixedClock(M1Fixture.FixMs, M1Fixture.TzMinutes));
            var mig = new SaveMigrator(T, dc); var st = GameState.FromJson(mig.Defaults()); st.Seen = true; st.Stage = stage; st.StartAt = dc.NowMs;
            var ctx = new GameContext(T, st, new Mulberry32(5), dc) { Stage = stage };
            var loop = new GameLoop(ctx, Str, new Mulberry32(6)); loop.SetView(216, 468, 468 - 320);
            return new Rig { Clock = dc, Ctx = ctx, Loop = loop, Mig = mig };
        }

        [Test] public void DevClockAddsTimeAndKeepsOffset()
        {
            var fx = new FixedClock(1000, 540); var dc = new DevClock(fx);
            dc.AddExtraSec(3600); Assert.AreEqual(1000 + 3600000, dc.NowMs); Assert.AreEqual(540, dc.OffsetMinutes(0)); Assert.AreEqual(3600000, dc.ExtraMs);
            Assert.AreEqual(3600000, new DevClock(fx, dc.ExtraMs).ExtraMs, "다시 시작해도 이어받는다(GameApp.Restart)");
        }

        [Test] public void JumpStageViewOnlyAndPersistent()
        {
            var r = Make(); DevCommands.JumpStage(r.Ctx, 4, false);
            Assert.AreEqual(4, r.Ctx.Stage); Assert.AreEqual(1, r.Ctx.S.Stage, "보기만: 저장은 그대로");
            DevCommands.JumpStage(r.Ctx, 99, true); Assert.AreEqual(T.MaxStage, r.Ctx.Stage); Assert.AreEqual(T.MaxStage, r.Ctx.S.Stage); Assert.IsNull(r.Ctx.S.Build);
            DevCommands.JumpStage(r.Ctx, -3, false); Assert.AreEqual(1, r.Ctx.Stage);
        }

        [Test] public void GiftsLevelGuidedStartDaysAndSeason()
        {
            var r = Make(); DevCommands.GiveGifts(r.Ctx, 7);
            Assert.IsTrue(T.Gifts.All(g => r.Ctx.S.Gifts[g.Id] == 7 && r.Ctx.S.Totals[g.Id] == 7 && r.Ctx.S.Found[g.Id] == 1));
            DevCommands.SetLevel(r.Ctx, 12); Assert.IsTrue(T.Tracks.All(t => r.Ctx.LvlOf(t.Id) == 12));
            DevCommands.SetLevel(r.Ctx, 0); Assert.IsTrue(T.Tracks.All(t => r.Ctx.LvlOf(t.Id) == 0));
            DevCommands.SetGuided(r.Ctx, 250); Assert.AreEqual(250, r.Ctx.S.Guided);
            DevCommands.SetStartDays(r.Ctx, 30); Assert.AreEqual(30, (int)r.Ctx.DaysSinceStart());
            DevCommands.SetSeason(r.Ctx, "winter"); Assert.AreEqual("winter", r.Ctx.CurSeason()); DevCommands.SetSeason(r.Ctx, "bogus"); Assert.IsNull(r.Ctx.SeasonForce);
        }

        [Test] public void NightDuskAndWeather()
        {
            var r = Make(3); DevCommands.SetNight(r.Loop, true);
            Assert.IsTrue(r.Loop.NightForced); r.Loop.Tick(0.1); Assert.IsTrue(r.Ctx.Night); Assert.AreEqual(1, r.Loop.Dusk);
            DevCommands.SetNight(r.Loop, false); r.Loop.Tick(0.1); Assert.IsFalse(r.Ctx.Night);
            DevCommands.SetDusk(r.Loop, 0.3); r.Loop.Tick(0.1); Assert.IsFalse(r.Ctx.Night); DevCommands.SetDusk(r.Loop, 0.7); r.Loop.Tick(0.1); Assert.IsTrue(r.Ctx.Night);
            DevCommands.SetNight(r.Loop, null); Assert.IsFalse(r.Loop.NightForced);
            Assert.AreEqual("clear", r.Loop.Wx.Kind); DevCommands.StartWeather(r.Loop, "rain");
            var dc = (DevClock)r.Ctx.Clock; for (int i = 0; i < 40; i++) { dc.AddExtraSec(0.1); r.Loop.Tick(0.1); }
            Assert.AreEqual("rain", r.Loop.Wx.Kind, "0.8초 뒤 비 시작");
        }

        [Test] public void FastModeShortensWeatherPeriod() { var r = Make(3); DevCommands.SetFast(r.Loop, true); Assert.AreEqual(6, r.Loop.WxSlow); DevCommands.SetFast(r.Loop, false); Assert.AreEqual(1, r.Loop.WxSlow); }

        [Test] public void SkipTimeAdvancesClockAndPaysOfflineReward()
        {
            var r = Make(3); long t0 = r.Clock.NowMs;
            var news = DevCommands.SkipTime(r.Clock, r.Ctx, r.Loop, 8 * 3600);
            Assert.AreEqual(t0 + 8 * 3600 * 1000L, r.Clock.NowMs); Assert.AreEqual(r.Clock.NowMs, r.Ctx.S.LastSeen);
            Assert.IsNotNull(news); Assert.Greater(news.Count, 0);
            Assert.IsNull(DevCommands.SkipTime(r.Clock, r.Ctx, r.Loop, 30), "90초 미만은 보상 없음");
        }

        [Test] public void AwayPaysAndResetsLastSeen()
        {
            var r = Make(3); var news = DevCommands.Away(r.Ctx, r.Loop, 4 * 3600);
            Assert.IsNotNull(news); Assert.AreEqual(r.Clock.NowMs, r.Ctx.S.LastSeen);
        }

        [Test] public void FreshStateIsDefaultsWithStartAt()
        {
            var r = Make(); var st = DevCommands.FreshState(r.Mig, 12345);
            Assert.AreEqual(12345, st.StartAt); Assert.AreEqual(1, st.Stage); Assert.AreEqual(0, st.Guided);
        }

        [Test] public void SelfChecksAllPass()
        {
            var res = DevChecks.RunAll(T, new FixedClock(M1Fixture.FixMs, M1Fixture.TzMinutes), Str);
            foreach (var x in res) TestContext.Out.WriteLine(x.ToString());
            var failed = res.Where(x => !x.Ok).ToList();
            Assert.AreEqual(0, failed.Count, string.Join("\n", failed.Select(f => f.ToString())));
            Assert.GreaterOrEqual(res.Count(x => !x.Skipped), 9, "실제로 도는 점검 수");
            Assert.AreEqual(14, res.Count, "점검 줄 수(실제 10 + 다른 시험으로 대신하는 4)");
        }

        [Test] public void SimulateTextIsDeterministicForSameSeed()
        {
            var clock = new FixedClock(M1Fixture.FixMs, M1Fixture.TzMinutes);
            string a = DevChecks.SimulateText(T, clock, 120, 20, 7, 1), b = DevChecks.SimulateText(T, clock, 120, 20, 7, 1), c = DevChecks.SimulateText(T, clock, 120, 20, 8, 1);
            string Strip(string s) => string.Join("\n", s.Split('\n').Skip(1));          // 첫 줄은 걸린 시간(ms)
            Assert.AreEqual(Strip(a), Strip(b)); Assert.AreNotEqual(Strip(a), Strip(c));
        }
    }
}
#endif
