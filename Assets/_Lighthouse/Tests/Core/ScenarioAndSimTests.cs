using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lighthouse.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>
    /// G-SIM·G-SCEN: 같은 씨앗·같은 시각에서 원본 함수가 낸 결과(반환값·최종 저장·난수 호출 수)와 같은지.
    /// 난수 호출 순서까지 같아야 통과하므로 통과 = seed 일치(목표 사항)까지 달성이다.
    /// </summary>
    public class ScenarioAndSimTests
    {
        static GameTables T => M1Fixture.Tables;

        // ---- JSON 만들기 도우미 ----
        static JToken N(double d) => Js.N(d);
        static JToken Arr(params object[] xs) => new JArray(xs.Select(x => x == null ? JValue.CreateNull() : x is JToken t ? t : x is bool b ? new JValue(b) : x is string s ? new JValue(s) : x is double d ? Js.N(d) : x is int i ? new JValue((long)i) : new JValue(x)));
        static JToken Str(string s) => s == null ? JValue.CreateNull() : new JValue(s);
        static Letter L(double id) => new Letter { Id = id, Ship = "순풍", Cap = "마리", Gift = "shell", T = 1700000000000.0 + id, Read = false, Text = "안녕 " + id, Sign = "순풍호 마리 드림", Region = "home" };
        static int CondBits(Cond c) => (c.Night ? 1 : 0) + (c.Dawn ? 2 : 0) + (c.Rain ? 4 : 0) + (c.Fog ? 8 : 0);
        static JToken Ship(GameContext c, RewardResult r, bool full) => full
            ? Arr(Str(r.Gift), r.Shiny ? 1 : 0, r.Letter != null ? N(r.Letter.Id) : N(0), r.Extra != null ? (JToken)Str(r.Extra.Gift) : N(0), r.Guests)
            : Arr(Str(r.Gift), r.Shiny ? 1 : 0);
        static JToken Off(OfflineResult r) => r == null ? JValue.CreateNull() : r.ToJson();
        static JToken Diary(List<DiaryEntry> d) => new JArray(d.Select(x => (JToken)x.ToJson()));

        static JToken ShipsRun(GameContext c, int n, bool cond, bool full)
        {
            var rs = new JArray();
            for (int i = 0; i < n; i++)
            {
                if (cond) c.CondOv = c.SampleCond();
                var r = c.RewardShip(c.ShipTypeAt(c.Rng.NextDouble()), false, true);
                c.CondOv = null; rs.Add(Ship(c, r, full));
            }
            return rs;
        }

        // 시나리오 이름 → 같은 동작 (golden m1_golden.js 의 act 와 1:1)
        static JToken Act(string name, GameContext c, long fix)
        {
            Match m = Regex.Match(name, @"^offline_s(\d)_a(\d+)$");
            if (m.Success) return Off(c.RunOffline(double.Parse(m.Groups[2].Value)));
            switch (name)
            {
                case "offline_first_back": case "offline_first_back_met": return Off(c.RunOffline(600));
                case "offline_night_diary": return Off(c.RunOffline(30000));
                case "offline_winter_s5": return Off(c.RunOffline(50400));
                case "offline_noautooffer": case "offline_focus": return Off(c.RunOffline(20000));
                case "offline_real_clock": case "offline_other_region_gifts": return Off(c.RunOffline(15000));
                case "ships_300_quiet": return ShipsRun(c, 300, true, true);
                case "ships_300_s6_garden": return ShipsRun(c, 300, false, true);
                case "ships_nocond_night_fog": return ShipsRun(c, 200, false, false);
                case "ships_rain_winter": return ShipsRun(c, 100, false, false);
                case "letters_40": { var rs = new JArray(); for (int i = 0; i < 40; i++) rs.Add(c.MakeLetter(c.T.Gifts[i % c.T.Gifts.Count].Id, i % 5 == 0).ToJson()); return rs; }
                case "letters_night_winter": { var rs = new JArray(); for (int i = 0; i < 20; i++) rs.Add(c.MakeLetter("shell", false).ToJson()); return rs; }
                case "letters_o_night": { var rs = new JArray(); for (int i = 0; i < 12; i++) rs.Add(c.MakeLetter("pearl", i % 2 == 0, new LetterOpts { Night = true, Season = "spring" }).ToJson()); return rs; }
                case "diary_8h": return Diary(c.MakeDiary(28800));
                case "diary_30m": return Diary(c.MakeDiary(1800));
                case "diary_14h_s5_winter": return Diary(c.MakeDiary(50400));
                case "diary_week_false": return Diary(c.MakeDiary(28800));
                case "diary_o_given": return Diary(c.MakeDiary(20000, new DiaryOpts { Tier = 3, Week = true, Season = "autumn", Now = fix, Seen = new Dictionary<string, double>() }));
                case "visit_regular_30": { var rs = new JArray(); for (int i = 0; i < 30; i++) { var l = c.VisitRegular("swallow", true); rs.Add(l == null ? JValue.CreateNull() : Arr(N(l.Id), Str(l.Gift), Str(Js.Slice(l.Text, 10)))); } return rs; }
                case "visit_regular_all": { var rs = new JArray(); foreach (var r in c.T.Regulars) for (int i = 0; i < 26; i++) { var l = c.VisitRegular(r.Id, true); if (l != null) rs.Add(Arr(Str(r.Id), N(l.Id), Str(l.Gift))); } return rs; }
                case "pick_regular": { var rs = new JArray(); for (int i = 0; i < 200; i++) { var r = c.PickRegular(); rs.Add(r == null ? JValue.CreateNull() : Str(r.Id)); } return rs; }
                case "guests_season": case "guests_anniv_all": case "guests_not_yet": return N(c.DeliverGuests(true));
                case "guests_twice": return Arr(c.DeliverGuests(true), c.DeliverGuests(true));
                case "finale": return Arr(c.DeliverFinale(true), c.DeliverFinale(true));
                case "finale_stage5": case "finale_none_met": return new JValue(c.DeliverFinale(true));
                case "autooffer": case "autooffer_focus": return N(c.AutoOfferAll());
                case "offer_each": return Arr(N(c.Offer("shell", "auto", 5, false)), N(c.Offer("pearl", "lens", 9, false)), N(c.Offer("blossom", "auto", 7, false)), N(c.Offer("moonshard", "auto", 3, false)), N(c.Offer("shell", "port", 5, true)), N(c.Offer("coral", "auto", 2, false)));
                case "addgift_seq": { var rs = new JArray(); for (int i = 0; i < 100; i++) rs.Add(c.AddGift("shell", new GameContext.AddGiftOpts { Quiet = true }) ? 1 : 0); return rs; }
                case "addgift_opts": return Arr(c.AddGift("shell", new GameContext.AddGiftOpts { Quiet = true, Shiny = true }), c.AddGift("pearl", new GameContext.AddGiftOpts { Quiet = true, NoShiny = true }), c.AddGift("coral", new GameContext.AddGiftOpts { Quiet = true, Shiny = false }), c.AddGift("seed", new GameContext.AddGiftOpts { Quiet = true }));
                case "sea_nature": { var rs = new JArray(); foreach (var s in c.T.Sea) { c.SeeSea(s.Id, false, true); rs.Add(c.S.SeasonLetters.Count); } c.SeeSea("dolphin", true, true); c.SeeSea("dolphin", true, true); rs.Add(N(c.S.Sea["dolphin"])); return rs; }
                case "sea_sky_only": { foreach (var s in c.T.Sea) if (s.Grp == "sky") c.SeeSea(s.Id, false, true); return new JArray(c.S.SeasonLetters.Keys); }
                case "growth_bump": { var rs = new JArray(); foreach (var kv in new[] { ("lens", 5.0), ("lens", 40.0), ("garden", 30.0), ("port", 700.0), ("garden", 5000.0) }) { c.BumpGrowth(kv.Item1, kv.Item2); rs.Add(Arr(Str(c.GrowthEvent), c.NeedRebuild)); } return rs; }
                case "add_growth_focus": case "add_growth_focus2": { for (int i = 0; i < 30; i++) { c.S.Guided++; c.AddGrowth(); } return name == "add_growth_focus" ? (JToken)new JArray() : JValue.CreateNull(); }
                case "canupgrade":
                    {
                        var rs = new JArray();
                        for (int st = 1; st <= 6; st++) { c.Stage = st; rs.Add(Arr(st, c.CanUpgrade(), c.Discovered())); }
                        c.S.Build = new BuildState { To = 4, StartAt = 1, EndAt = 2 }; rs.Add(Arr("build", c.CanUpgrade())); return rs;
                    }
                case "discovered_misc": return Arr(c.Discovered(), c.UnreadCount());
                case "add_letter_trim": { for (int i = 0; i < 40; i++) c.AddLetter(L(c.S.LetterId++)); return N(c.S.Letters.Count); }
                case "add_letter_allkeep": { c.AddLetter(L(c.S.LetterId++)); var l2 = L(c.S.LetterId++); l2.Keep = new JValue("home"); c.AddLetter(l2); return N(c.S.Letters.Count); }
                case "sample_cond": case "sample_cond_t1": { int n = name == "sample_cond" ? 100 : 50; var rs = new JArray(); for (int i = 0; i < n; i++) rs.Add(CondBits(c.SampleCond())); return rs; }
                case "roll_gift_boost": { var o = new JArray(); foreach (var b in new string[] { "fog", "rain", null }) { var rs = new JArray(); for (int i = 0; i < 60; i++) rs.Add(c.RollGift("trade", b)); o.Add(rs); } return o; }
                case "roll_gift_dawn": { var rs = new JArray(); for (int i = 0; i < 50; i++) rs.Add(c.RollGift("fish")); return rs; }
                case "roll_gift_ov": { c.CondOv = new Cond { Night = true, Dawn = true, Rain = true, Fog = true }; var rs = new JArray(); for (int i = 0; i < 100; i++) rs.Add(c.RollGift("cruise")); c.CondOv = null; return rs; }
                default: throw new InvalidOperationException("시나리오 구현 없음: " + name);
            }
        }

        static JToken RecentJson(GameContext c)
        {
            var o = new JObject();
            foreach (var kv in c.LetterRecent) o[kv.Key] = new JArray(kv.Value.Select(x => x == null ? (JToken)JValue.CreateNull() : new JValue(x)));
            return o;
        }

        [Test]
        public void SeededScenariosMatchJsExactly()
        {
            var g = M1Fixture.Golden("scenarios.json"); var mig = M1Fixture.Migrator(); long fix = M1Fixture.FixMs; var fails = new List<string>(); int n = 0;
            foreach (JObject sc in (JArray)g["scenarios"])
            {
                string name = (string)sc["name"]; var def = (JObject)sc["def"]; n++;
                Assert.IsNull(sc["error"].Type == JTokenType.Null ? null : (string)sc["error"], name + " 원본 실행 오류");
                var rng = new Mulberry32((long)Js.Num(def["seed"]));
                var state = GameState.FromJson(mig.Migrate(def["save"].DeepClone()));
                var c = new GameContext(T, state, rng, M1Fixture.Clock()) { Stage = (int)Js.Num(def["stage"]), SeasonForce = (string)def["season"], Night = (bool)def["night"], Wx = (string)def["wx"] };
                state.Stage = c.Stage;
                JToken result;
                try { result = Act(name, c, fix); }
                catch (Exception e) { fails.Add(name + ": 예외 " + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace.Split('\n')[0]); continue; }
                var checks = new List<string>();
                string d;
                if ((d = M1Fixture.Diff(result, sc["result"])) != null) checks.Add("result " + d);
                if (rng.Calls != (long)Js.Num(sc["calls"])) checks.Add("난수 호출 수 " + rng.Calls + " != " + sc["calls"]);
                if ((d = M1Fixture.Diff(state.ToJson(), sc["save"])) != null) checks.Add("save " + d);
                if ((d = M1Fixture.Diff(RecentJson(c), sc["letterRecent"])) != null) checks.Add("letterRecent " + d);
                if ((d = M1Fixture.Diff(c.GrowthEvent == null ? (JToken)JValue.CreateNull() : new JValue(c.GrowthEvent), sc["growthEvent"])) != null) checks.Add("growthEvent " + d);
                if (c.NeedRebuild != (bool)sc["needRebuild"]) checks.Add("needRebuild");
                if ((d = M1Fixture.Diff(new JArray(c.FirstQ.Select(q => (JToken)new JArray(q))), sc["firstQ"])) != null) checks.Add("firstQ " + d);
                if (checks.Count > 0) fails.Add(name + " → " + string.Join(" ; ", checks));
            }
            Assert.GreaterOrEqual(n, 60, "시나리오 수");
            Assert.IsEmpty(fails, n + "건 중 실패 " + fails.Count + "\n" + string.Join("\n", fails.Take(12)));
        }

        [Test]
        public void SimulatorMatchesJsGolden()
        {
            var g = M1Fixture.Golden("sim.json"); var fails = new List<string>(); var exact = new List<string>();
            foreach (JObject s in (JArray)g["sims"])
            {
                var a = (JArray)s["args"];
                var r = Simulator.Run(T, M1Fixture.Clock(), Js.Num(a[0]), (int)Js.Num(a[1]), (long)Js.Num(a[2]), (int)Js.Num(a[3]));
                var actual = r.ToJson(); var exp = (JObject)s["result"]; string tag = Js.Stringify(a);
                string d = M1Fixture.Diff(actual, exp);
                if (d == null) { exact.Add(tag); continue; }
                // seed 가 다르게 흘러갔을 때의 허용 기준(통계 일치, 필수): 단계 도달일 ±2일·최종 레벨 ±1·도감 발견 같음·편지 수 같음
                foreach (var k in new[] { "2", "3", "4", "5", "6" }) { double ea = exp["stage"][k] == null ? double.NaN : Js.Num(exp["stage"][k]), aa = actual["stage"][k] == null ? double.NaN : Js.Num(actual["stage"][k]); if (!(Math.Abs(ea - aa) <= 2)) fails.Add(tag + " 단계 " + k + " 도달일 " + aa + " vs " + ea); }
                foreach (var tr in new[] { "lens", "garden", "port" }) if (Math.Abs(Js.Num(actual["final"]["lv"][tr]) - Js.Num(exp["final"]["lv"][tr])) > 1) fails.Add(tag + " 최종 " + tr + " 레벨");
                if (Js.Num(actual["final"]["discovered"]) != Js.Num(exp["final"]["discovered"])) fails.Add(tag + " 발견 수");
                fails.Add(tag + " (참고: seed 불일치) " + d);
            }
            TestContext.Out.WriteLine("seed 까지 완전 일치: " + exact.Count + "/" + ((JArray)g["sims"]).Count);
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }
    }
}
