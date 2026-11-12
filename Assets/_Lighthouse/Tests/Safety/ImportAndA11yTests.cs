using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Lighthouse.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>
    /// 가져오기 경로(영역 C ②)·기기 시계 정책(③)·접근성 라벨 규칙(⑤) 시험. 순수 C#: dotnet test 와 Unity EditMode 공용.
    /// 가져오기→Restart 의 Unity 쪽(GameApp)은 PlayMode 시험(M7PlayTests)이 본다. 여기서는 그 안에서 부르는 순수 부분(SaveCode.Parse → SaveImport.Apply → SaveStore.Save/Load)을 시험한다.
    /// </summary>
    public sealed class ImportAndA11yTests
    {
        static GameTables T => M1Fixture.Tables;
        static SaveMigrator Mig => M1Fixture.Migrator();
        static SaveCode Sc => new SaveCode(T, Mig);

        sealed class Mem : IStorage
        {
            public readonly Dictionary<string, string> F = new Dictionary<string, string>();
            public bool Exists(string n) => F.ContainsKey(n); public string ReadText(string n) => F[n];
            public void WriteTextAtomic(string n, string t) { F[n] = t; } public void Delete(string n) { F.Remove(n); }
        }

        static string Code(string json) => CodeOf(Encoding.UTF8.GetBytes(json));
        static string CodeOf(byte[] raw) { string b = Convert.ToBase64String(raw); return "LHS1." + b + "." + Js.HashStr(b); }
        static JObject Base() { var d = Mig.Defaults(); return d; }
        static GameState Device()
        {
            var dev = GameState.FromJson(Mig.Defaults()); dev.Owned["noads"] = 1; dev.Guided = 5; dev.Ads.Day = "2026-11-11"; dev.Ads.Used["home"] = 2; dev.Ads.Used["ship"] = 3; dev.Ads.HomeDay = "2026-11-11"; return dev;
        }

        // ================= 가져오기 =================
        [Test]
        public void ImportKeepsDeviceOwnedAndAdsAndResetsLastSeen()
        {
            var dev = Device(); long now = M1Fixture.FixMs;
            var other = Base(); other["stage"] = 4; other["guided"] = 900; other["lastSeen"] = now - 999999999L;
            // 코드에 owned·ads 를 위조해서 넣어도(내보내기는 빼지만 직접 만든 코드는 넣을 수 있다) 이 기기 것이 남는다
            other["owned"] = new JObject { ["everything"] = 1 }; other["ads"] = new JObject { ["day"] = "2026-11-11", ["used"] = new JObject { ["home"] = 0, ["star"] = 0, ["ship"] = 0 }, ["homeDay"] = "" };
            var r = Sc.Parse(Code(Js.Stringify(other))); Assert.IsNull(r.Err, r.Err);
            var st = SaveImport.Apply(dev, r.Data, Mig, now);
            Assert.AreEqual(4, st.Stage); Assert.AreEqual(900, (int)st.Guided);
            Assert.IsNotNull(st.Owned["noads"]); Assert.IsNull(st.Owned["everything"], "위조한 구매 기록은 버린다");
            Assert.AreEqual(2, st.Ads.Used["home"]); Assert.AreEqual(3, st.Ads.Used["ship"], "광고 횟수는 이 기기 것");
            Assert.AreEqual(now, st.LastSeen);
            Assert.IsNull(new GameContext(T, st, new Mulberry32(1), M1Fixture.Clock()).RunOffline((now - st.LastSeen) / 1000.0), "가져온 직후 오프라인 보상 없음");
            Assert.AreNotSame(dev.Owned, st.Owned, "기기 상태 객체와 공유하지 않는다(복사)");
        }

        [Test]
        public void ImportedSaveSurvivesWriteReadAndKeepsPreImportCopy()
        {
            var io = new Mem(); var store = new SaveStore(io, Mig, M1Fixture.Clock());
            var cur = store.Load(); cur.Guided = 77; store.Save(cur);
            var other = Base(); other["stage"] = 3; other["guided"] = 321;
            var r = Sc.Parse(Sc.Export(other)); Assert.IsNull(r.Err);
            var st = SaveImport.Apply(cur, r.Data, Mig, M1Fixture.FixMs);
            store.KeepBeforeImport(); store.Save(st);
            Assert.AreEqual(77, (int)((JObject)Js.Parse(io.F[SaveStore.PreImportName]))["guided"], "가져오기 전 저장이 한 벌 남는다");
            var s2 = new SaveStore(io, Mig, M1Fixture.Clock()); var back = s2.Load();
            Assert.AreEqual("main", s2.LoadedFrom); Assert.AreEqual(321, (int)back.Guided); Assert.AreEqual(3, back.Stage);
        }

        [Test]
        public void ImportAfterRestartLooksLikeAFreshLoad()
        {
            // GameApp.Restart 는 저장 파일을 다시 읽는다 → 방금 쓴 파일을 읽은 상태가 가져온 상태와 같아야 한다 (lastSeen·startAt 보정 포함)
            var io = new Mem(); var store = new SaveStore(io, Mig, M1Fixture.Clock());
            var cur = store.Load();
            var other = Base(); other["stage"] = 5; other["guided"] = 4000; other["startAt"] = 0;
            var st = SaveImport.Apply(cur, Sc.Parse(Sc.Export(other)).Data, Mig, M1Fixture.FixMs);
            store.Save(st);
            var back = new SaveStore(io, Mig, M1Fixture.Clock()).Load();
            Assert.IsNull(M1Fixture.Diff(back.ToJson(), st.ToJson().DeepClone().Also(j => { ((JObject)j)["startAt"] = M1Fixture.FixMs; })), "startAt=0 은 불러올 때 지금으로 채워진다(원본 1620행)");
        }

        // ================= 악성·변조 코드 =================
        static IEnumerable<TestCaseData> Bad()
        {
            yield return new TestCaseData("공백", "   ");
            yield return new TestCaseData("접두사만", "LHS1.");
            yield return new TestCaseData("틀린 접두사", "LHS2.AAAA.zzz");
            yield return new TestCaseData("해시 불일치", "LHS1.e30=.0");
            yield return new TestCaseData("JSON 아님", Code("이건 JSON 이 아니에요"));
            yield return new TestCaseData("배열", Code("[1,2,3]"));
            yield return new TestCaseData("null", Code("null"));
            yield return new TestCaseData("빈 객체", Code("{}"));
            yield return new TestCaseData("깊은 중첩 5000", Code(new string('[', 5000) + new string(']', 5000)));
            yield return new TestCaseData("깊은 객체 중첩", Code(string.Concat(Enumerable.Repeat("{\"a\":", 3000)) + "1" + new string('}', 3000)));
            yield return new TestCaseData("잘못된 UTF-8", CodeOf(new byte[] { 0xFF, 0xFE, 0xC0, 0xAF, 0x80 }));
            yield return new TestCaseData("단계 0", Code(Js.Stringify(With("stage", 0))));
            yield return new TestCaseData("단계 최대+1", Code(Js.Stringify(With("stage", T.MaxStage + 1))));
            yield return new TestCaseData("단계 1.5", Code(Js.Stringify(With("stage", 1.5))));
            yield return new TestCaseData("단계 문자열", Code(Js.Stringify(With("stage", "3"))));
            yield return new TestCaseData("gifts 가 배열", Code(Js.Stringify(With("gifts", new JArray()))));
            yield return new TestCaseData("letters 가 객체", Code(Js.Stringify(With("letters", new JObject()))));
        }
        static JObject With(string k, JToken v) { var d = Base(); d[k] = v; return d; }

        [TestCaseSource(nameof(Bad))]
        public void MaliciousOrBrokenCodeIsRejectedCleanly(string why, string code)
        {
            SaveCodeResult r = null;
            Assert.DoesNotThrow(() => r = Sc.Parse(code), why);
            Assert.IsNotNull(r.Err, why + ": 거부되어야 한다");
            Assert.IsNull(r.Data);
        }

        [Test]
        public void NullCodeIsRejected() { var r = Sc.Parse(null); Assert.IsNotNull(r.Err); }

        [Test]
        public void TamperedHashAndTruncatedCodeAreRejected()
        {
            string good = Sc.Export(Base());
            Assert.IsNull(Sc.Parse(good).Err);
            Assert.IsNotNull(Sc.Parse(good.Substring(0, good.Length - 3)).Err, "잘림");
            string[] parts = good.Split('.');
            Assert.IsNotNull(Sc.Parse(parts[0] + "." + parts[1].Substring(0, parts[1].Length - 4) + "AAAA" + "." + parts[2]).Err, "내용이 바뀜");
            Assert.IsNotNull(Sc.Parse(parts[0] + "." + parts[1] + "." + parts[2] + "x").Err, "해시 뒤에 덧붙임");
        }

        [Test]
        public void HugeCodeIsHandledInReasonableTime()
        {
            var d = Base(); d["padding"] = new string('A', 4_000_000);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = Sc.Parse(Code(Js.Stringify(d)));
            Assert.Less(sw.ElapsedMilliseconds, 8000);
            Assert.IsNull(r.Err, "허용 목록에 없는 큰 칸은 그냥 버린다: " + r.Err);
            Assert.IsNull(r.Data["padding"]);
        }

        [Test]
        public void SanitizedImportHoldsInvariantsForNastyValues()
        {
            var d = Base();
            d["gifts"] = JObject.Parse("{\"__proto__\":{\"x\":1},\"constructor\":5,\"toString\":3,\"shell\":3,\"shell2\":-4,\"pearl\":1e308}");
            d["growth"] = JObject.Parse("{\"lens\":1e308,\"garden\":-5,\"port\":\"x\"}");
            d["guided"] = -50; d["lastSeen"] = 1e20; d["startAt"] = -1; d["letterId"] = "abc"; d["dexSeen"] = 1e30;
            d["dex"] = new JObject(); for (int i = 0; i < 500; i++) d["dex"]["k" + i] = 1;
            var big = new JArray(); for (int i = 0; i < 3000; i++) big.Add(new JObject { ["id"] = i, ["text"] = new string('가', 3000), ["ship"] = new string('x', 3000) }); d["letters"] = big;
            d["stats"] = JObject.Parse("{\"ok_key\":5,\"<script>\":1,\"constructor\":2,\"hasOwnProperty\":1}");
            d["skin"] = JObject.Parse("{\"tower\":\"__proto__\",\"keeper\":{\"a\":1}}");
            d["focus"] = "'; DROP TABLE"; d["season"] = "winter\n"; d["beam"] = 12; d["region"] = "../../etc";
            var r = Sc.Parse(Code(Js.Stringify(d))); Assert.IsNull(r.Err, r.Err);
            var dev = Device(); var st = SaveImport.Apply(dev, r.Data, Mig, M1Fixture.FixMs);
            Assert.IsTrue(st.Gifts.Keys.All(k => T.GiftById.ContainsKey(k)), "선물 키는 표에 있는 것만");
            Assert.IsTrue(st.Gifts.Values.All(v => v >= 0 && v <= 1e9));
            Assert.IsTrue(st.Growth.Values.All(v => v >= 0 && v <= 1e12));
            Assert.GreaterOrEqual(st.Guided, 0); Assert.LessOrEqual(st.LastSeen, T.Bal.MaxT);
            Assert.LessOrEqual(st.Letters.Count, T.Bal.LetterLimCount); Assert.IsTrue(st.Letters.All(l => (l.Text ?? "").Length < 6000));
            Assert.IsTrue(st.Dex.Keys.All(k => T.ShipById.ContainsKey(k)));
            Assert.IsTrue(st.Stats.Keys.All(k => System.Text.RegularExpressions.Regex.IsMatch(k, "^[A-Za-z0-9_]{1,30}$")), "통계 키는 정해진 꼴만(<script> 같은 것은 버린다)");
            Assert.IsFalse(st.Stats.ContainsKey("constructor") || st.Stats.ContainsKey("hasOwnProperty"), "Object 기본 이름은 버린다");
            Assert.IsTrue(T.RegionById.ContainsKey(st.Region));
            // 이어서 게임이 돌아가야 한다(예외 없음) + 쓰고 다시 읽기
            var ctx = new GameContext(T, st, new Mulberry32(3), M1Fixture.Clock());
            Assert.DoesNotThrow(() => ctx.RunOffline(7200));
            var io = new Mem(); var store = new SaveStore(io, Mig, M1Fixture.Clock()); store.Save(st);
            var s2 = new SaveStore(io, Mig, M1Fixture.Clock()); s2.Load(); Assert.AreEqual("main", s2.LoadedFrom);
        }

        // ================= 기기 시계 (HTML 정책 그대로, 새 정책 없음) =================
        static GameContext Ctx(long nowMs, GameState st = null)
        {
            var clock = new FixedClock(nowMs, M1Fixture.TzMinutes);
            return new GameContext(T, st ?? GameState.FromJson(new SaveMigrator(T, clock).Defaults()), new Mulberry32(1), clock);
        }

        [Test]
        public void ClockGoingBackwardGivesNoOfflineRewardAndDoesNotChangeState()
        {
            var ctx = Ctx(M1Fixture.FixMs); string before = Js.Stringify(ctx.S.ToJson());
            Assert.IsNull(ctx.RunOffline(-3600), "시계가 뒤로 가면(음수) 보상 0 — 원본 runOffline 의 '눈에 띌 만큼'");
            Assert.IsNull(ctx.RunOffline(-1e12));
            Assert.AreEqual(before, Js.Stringify(ctx.S.ToJson()));
        }

        [Test]
        public void ClockJumpingFarForwardIsCappedByOfflineMax()
        {
            var ctx = Ctx(M1Fixture.FixMs); var res = ctx.RunOffline(10 * 365 * 86400.0);
            Assert.IsNotNull(res); Assert.LessOrEqual(res.Away, ctx.OfflineMax() + 1e-6, "시계를 10년 앞으로 해도 한도(8~14시간)까지만");
        }

        [Test]
        public void AdsTodayFollowsHtmlRulesForDeviceClock()
        {
            // 원본 adsToday(1750행, D15·D20): 날짜가 앞으로 가면 초기화, 기기 날짜가 2일 이내로 과거로 가면 그대로, 2일 넘게 미래/형식 오류면 오늘로 초기화
            long now = M1Fixture.FixMs; var probe = Ctx(now); string today = probe.TodayKey();
            string Day(int delta) => DateTime.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(delta).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            double Used(string day) { var st = GameState.FromJson(Mig.Defaults()); st.Ads.Day = day; st.Ads.Used["home"] = 3; return Ctx(now, st).AdsToday().Used["home"]; }
            Assert.AreEqual(3, Used(today), "같은 날: 그대로");
            Assert.AreEqual(0, Used(Day(-1)), "하루가 지나면 초기화(기기 시계를 앞으로 돌려도 같다 — 알려진 한계, 서버 없음)");
            Assert.AreEqual(3, Used(Day(+1)), "기기 날짜가 하루 뒤로 가도(기록이 내일) 초기화하지 않는다");
            Assert.AreEqual(3, Used(Day(+2)), "2일까지는 그대로");
            Assert.AreEqual(0, Used(Day(+3)), "2일 넘게 미래면 오늘로 초기화(D20)");
            Assert.AreEqual(0, Used("엉터리"), "형식 오류도 초기화");
        }

        // ================= 접근성 라벨 규칙 =================
        [Test]
        public void A11yCleanStripsRichTextAndWhitespace()
        {
            Assert.AreEqual("안녕 하세요", A11y.Clean("<color=#fff>안녕</color>  하세요\n"));
            Assert.AreEqual("", A11y.Clean("<sprite name=\"x\">  ​"));
            Assert.AreEqual("a b", A11y.Clean("a \t b"));
            Assert.AreEqual("", A11y.Clean(null));
        }

        [Test]
        public void A11yKeyAndLabelTable()
        {
            Assert.AreEqual("gear", A11y.KeyOfName("hb:gear")); Assert.AreEqual("close", A11y.KeyOfName("Close")); Assert.AreEqual("a_b", A11y.KeyOfName("x:A B"));
            foreach (var k in new[] { "gear", "sound", "close", "menu" }) Assert.IsNotNull(A11y.LabelFor(k), k);
            Assert.AreEqual("설정 열기", A11y.LabelFor("gear")); Assert.AreEqual("소리 끄기", A11y.LabelFor("sound")); Assert.AreEqual("닫기", A11y.LabelFor("close"));
            Assert.IsNull(A11y.LabelFor("nope")); Assert.IsNull(A11y.LabelFor(null));
        }

        [Test]
        public void A11yOrderReadsTopToBottomLeftToRightAndDropsEmpty()
        {
            A11yItem It(string l, float x, float y, float w = 40, float h = 20) => new A11yItem { Label = l, X = x, Y = y, W = w, H = h };
            var o = A11y.Order(new[] { It("c", 10, 100), It("b2", 100, 52), It("a", 10, 10), It("b1", 10, 50), It("", 0, 0), It("zero", 0, 0, 0, 0) });
            Assert.AreEqual("a,b1,b2,c", string.Join(",", o.Select(i => i.Label)), "같은 줄은 왼쪽부터");
            Assert.AreNotEqual(A11y.Signature(o), A11y.Signature(A11y.Order(new[] { It("a", 10, 11) })));
        }
    }

    static class TokenExt { public static JToken Also(this JToken t, Action<JToken> f) { f(t); return t; } }
}
