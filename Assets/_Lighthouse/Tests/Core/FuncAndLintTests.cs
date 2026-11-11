using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>G-FUNC: 순수 함수 입출력 표(레벨 곡선·확률·오프라인 상한·배 추첨·자리 열림·보유·조사·시각·차감 가격)와 G-LINT(금지어·checkContent 동등).</summary>
    public class FuncAndLintTests
    {
        static GameTables T => M1Fixture.Tables;
        static JObject F => M1Fixture.Golden("func.json");

        static GameContext NewCtx(Action<GameState> setup = null, string season = "summer")
        {
            var st = GameState.FromJson(M1Fixture.Migrator().Defaults()); setup?.Invoke(st);
            return new GameContext(T, st, new Mulberry32(1), M1Fixture.Clock()) { SeasonForce = season };
        }

        static void NoFails(List<string> fails) => Assert.IsEmpty(fails, string.Join("\n", fails.Take(40)));
        static void Eq(List<string> fails, string what, double actual, JToken expected)
        {
            double e = expected == null || expected.Type == JTokenType.Null ? double.NaN : Js.Num(expected);
            if (!(actual == e || (double.IsNaN(actual) && double.IsNaN(e)))) fails.Add(what + ": " + actual.ToString("R") + " != " + e.ToString("R"));
        }

        [Test]
        public void LevelCurveMatchesJs()
        {
            var f = new List<string>(); var a = (JArray)F["lvAt"];
            for (int n = 1; n <= a.Count; n++) Eq(f, "lvAt(" + n + ")", Progress.LvAt(T, n), a[n - 1]);
            NoFails(f);
        }

        [Test]
        public void ProfilesChancesAndLimitsMatchJs()
        {
            var f = new List<string>();
            foreach (JObject p in (JArray)F["profiles"])
            {
                var g = (JObject)p["growth"]; string tag = Js.Stringify(g);
                var c = NewCtx(s => { s.Growth["lens"] = Js.Num(g["lens"]); s.Growth["garden"] = Js.Num(g["garden"]); s.Growth["port"] = Js.Num(g["port"]); });
                foreach (var k in new[] { "lens", "garden", "port" }) { Eq(f, tag + " lv." + k, c.LvlOf(k), p["lv"][k]); Eq(f, tag + " effLv." + k, c.EffLv(k), p["effLv"][k]); }
                Eq(f, tag + " total", c.TotalLv(), p["total"]); Eq(f, tag + " shiny", c.ShinyChance(), p["shiny"]); Eq(f, tag + " harvest", c.HarvestChance(), p["harvest"]);
                Eq(f, tag + " regBonus", c.RegularBonus(), p["regBonus"]); Eq(f, tag + " lensMul", c.LensMul(), p["lensMul"]); Eq(f, tag + " lensShiny", Progress.LensShiny(T, c.LvlOf("lens")), p["lensShiny"]);
                Eq(f, tag + " offlineMax", c.OfflineMax(), p["offlineMax"]); Eq(f, tag + " rankIdx", c.RankIdx(), p["rankIdx"]);
            }
            NoFails(f);
        }

        [Test]
        public void OfflineShipsAndShipTypeMatchJs()
        {
            var f = new List<string>();
            foreach (JObject o in (JArray)F["offlineShips"]) Eq(f, "offlineShips(" + o["away"] + ")", Progress.OfflineShips(T, Js.Num(o["away"])), o["n"]);
            foreach (JObject s in (JArray)F["shipTypeAt"])
            {
                var c = NewCtx(); c.Stage = (int)Js.Num(s["stage"]); var rs = (JArray)s["r"]; var ts = (JArray)s["t"];
                for (int i = 0; i < rs.Count; i++) { string t = c.ShipTypeAt(Js.Num(rs[i])); if (t != (string)ts[i]) f.Add("shipTypeAt stage " + s["stage"] + " r=" + rs[i] + ": " + t + " != " + ts[i]); }
            }
            NoFails(f);
        }

        [Test]
        public void DecorSlotsOpenPositionsAndOwnershipMatchJs()
        {
            var f = new List<string>();
            foreach (JObject o in (JArray)F["slotOpen"])
            {
                var sl = T.DecorSlotById[(string)o["id"]]; var bits = (JArray)o["bits"];
                for (int st = 1; st <= 6; st++) for (int gl = 0; gl <= 30; gl++) { char b = DecorRules.SlotOpen(sl, st, gl) ? '1' : '0'; if (b != ((string)bits[st - 1])[gl]) f.Add(sl.Id + " st" + st + " gl" + gl); }
            }
            foreach (JObject o in (JArray)F["slotPos"])
            {
                var sl = T.DecorSlotById[(string)o["id"]]; var abs = (JArray)o["ab"]; var xys = (JArray)o["xy"];
                for (int i = 0; i < 6; i++)
                {
                    var exp = abs[i]; double N = T.IsleNs[i + 1]; var act = sl.AbAt(N);
                    if (exp.Type == JTokenType.Null) { if (act != null) f.Add(sl.Id + " ab 기대 없음"); }
                    else if (act == null || act[0] != Js.Num(exp[0]) || act[1] != Js.Num(exp[1])) f.Add(sl.Id + " ab N=" + N + " " + (act == null ? "null" : act[0] + "," + act[1]) + " != " + exp);
                }
                double[] rs = { 20, 30, 41, 55, 60, 77, 99.5, 100 };
                for (int i = 0; i < rs.Length; i++)
                {
                    var exp = xys[i]; var act = sl.XyAt(rs[i]);
                    if (exp.Type == JTokenType.Null) { if (act != null) f.Add(sl.Id + " xy 기대 없음"); }
                    else if (act == null || act[0] != Js.Num(exp[0]) || act[1] != Js.Num(exp[1])) f.Add(sl.Id + " xy R=" + rs[i] + " " + (act == null ? "null" : act[0] + "," + act[1]) + " != " + exp);
                }
            }
            foreach (JObject o in (JArray)F["decorOwned"])
            {
                var js = (JObject)o["state"];
                var st = new GameState();
                if (js["growth"] is JObject gr) foreach (var p in gr.Properties()) if (Js.IsNumber(p.Value)) st.Growth[p.Name] = Js.Num(p.Value);
                if (js["reg"] is JObject rg) foreach (var p in rg.Properties()) st.Reg[p.Name] = new RegRecord { V = p.Value is JObject ro && Js.IsNumber(ro["v"]) ? Js.Num(ro["v"]) : 0 };
                if (js["totals"] is JObject tt && tt["gifts"] is JObject tg) foreach (var p in tg.Properties()) st.Totals[p.Name] = Js.Num(p.Value);
                if (js["isle"] is JObject il && il["own"] is JObject ow) foreach (var p in ow.Properties()) st.Isle.Own[p.Name] = Js.Num(p.Value);
                var ids = ((JArray)o["ids"]).Select(x => (string)x).ToList(); string bits = (string)o["own"];
                for (int i = 0; i < ids.Count; i++) if ((DecorRules.Owned(T, st, ids[i]) ? '1' : '0') != bits[i]) f.Add("decorOwned " + ids[i] + " state " + Js.Stringify(js));
                var lv = (JArray)o["lv"]; string[] ks = { "lens", "garden", "port" };
                for (int i = 0; i < 3; i++) { st.Growth.TryGetValue(ks[i], out double pv); Eq(f, "decorLv " + ks[i], Progress.LevelOf(T, pv), lv[i]); }
            }
            NoFails(f);
        }

        [Test]
        public void JosaGiftPointsAndTextsMatchJs()
        {
            var f = new List<string>();
            foreach (JObject o in (JArray)F["fixJosa"]) { string a = GameContext.FixJosa((string)o["t"]); if (a != (string)o["o"]) f.Add("fixJosa(" + o["t"] + "): " + a + " != " + o["o"]); }
            var c = NewCtx();
            foreach (JObject o in (JArray)F["giftPts"])
            {
                string k = (string)o["k"]; if (c.AffOf(k) != (string)o["aff"]) f.Add("affOf " + k);
                string[] tr = { "lens", "garden", "port" }; var pts = (JArray)o["pts"];
                for (int i = 0; i < 3; i++) { var q = (JArray)pts[i]; Eq(f, k + " pts " + tr[i] + " 0", c.GiftPts(k, tr[i], false), q[0]); Eq(f, k + " pts 1", c.GiftPts(k, tr[i], true), q[1]); Eq(f, k + " pts 2", c.GiftPts(k, tr[i], false, true), q[2]); Eq(f, k + " pts 3", c.GiftPts(k, tr[i], true, true), q[3]); }
            }
            foreach (JObject o in (JArray)F["safeId"]) { string v = o["v"] != null && o["v"].Type == JTokenType.String ? (string)o["v"] : null; string a = SaveMigrator.SafeId(v); string e = o["o"].Type == JTokenType.Null ? null : (string)o["o"]; if (a != e) f.Add("SAFE_ID(" + o["v"] + ")"); }
            foreach (JObject o in (JArray)F["dayNum"]) { var tok = o["v"]; Eq(f, "dayNum(" + tok + ")", SaveMigrator.DayNum(tok), o["o"]); bool bad = M1Fixture.Migrator().AdsDayBad(tok); if (bad != (bool)o["bad"]) f.Add("adsDayBad(" + tok + ")"); }
            var mig = M1Fixture.Migrator();
            foreach (JObject o in (JArray)F["dayKeyOf"]) if (mig.DayKeyOf(Js.Num(o["v"])) != (string)o["o"]) f.Add("dayKeyOf(" + o["v"] + ")");
            if (mig.TodayKey() != (string)F["todayKey"]) f.Add("todayKey");
            foreach (JObject o in (JArray)F["whenParts"])
            {
                if (o["ms"].Type == JTokenType.Null) continue;        // 잘못된 시각 표시는 화면 쪽 서식 소관
                var a = c.WhenParts((long)Js.Num(o["ms"])); var e = (JArray)o["o"];
                if (a[0] != (string)e[0] || a[1] != (string)e[1]) f.Add("whenParts(" + o["ms"] + "): " + a[0] + "/" + a[1] + " != " + e);
            }
            foreach (JObject o in (JArray)F["curSeason"])
            {
                long ms = new DateTimeOffset(2026, (int)o["month"], 15, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                var cc = new GameContext(T, new GameState(), new Mulberry32(1), new FixedClock(ms, M1Fixture.TzMinutes));
                if (cc.CurSeason() != (string)o["auto"]) f.Add("curSeason month " + o["month"] + ": " + cc.CurSeason() + " != " + o["auto"]);
            }
            foreach (JObject o in (JArray)F["seasonKey"]) { var cc = NewCtx(null, (string)o["season"]); if (cc.SeasonKey() != (string)o["key"]) f.Add("seasonKey " + o["season"] + ": " + cc.SeasonKey() + " != " + o["key"]); }
            foreach (JObject o in (JArray)F["giftWhen"]) { string a = new ContentLint(T).GiftWhen(T.GiftById[(string)o["k"]]); if (a != (string)o["o"]) f.Add("giftWhen " + o["k"] + ": " + a + " != " + o["o"]); }
            foreach (JObject o in (JArray)F["seaGrp"]) if (T.SeaById[(string)o["k"]].Grp != (string)o["grp"]) f.Add("seaGrp " + o["k"]);
            var nm = (JArray)F["nextMile"]; string[] tks = { "lens", "garden", "port" }; int[] lvs = { 0, 4, 5, 9, 12, 20, 30 };
            for (int i = 0; i < 3; i++) for (int j = 0; j < lvs.Length; j++) { var m = Progress.NextMile(T, tks[i], lvs[j]); string e = nm[i][j].Type == JTokenType.Null ? null : (string)nm[i][j]; if ((m == null ? null : m.Text) != e) f.Add("nextMile " + tks[i] + " " + lvs[j]); }
            NoFails(f);
        }

        [Test]
        public void DayClockMatchesJs()
        {
            var f = new List<string>(); var clock = M1Fixture.Clock();
            foreach (JObject blk in (JArray)F["dusk"])
            {
                string mode = (string)blk["clock"];
                foreach (JObject r in (JArray)blk["rows"])
                {
                    long ms = (long)Js.Num(r["ms"]);
                    Eq(f, mode + " duskAt(" + ms + ")", Progress.DuskAt(mode, ms, clock), r["d"]);
                    if (Progress.IsDawnAt(mode, ms, clock) != (bool)r["dawn"]) f.Add(mode + " isDawnAt(" + ms + ")");
                }
            }
            NoFails(f);
        }

        [Test]
        public void ShopDuePriceMatchesJs()
        {
            var f = new List<string>();
            foreach (JObject o in (JArray)F["dueOf"])
            {
                var p = T.Products.First(x => x.Id == (string)o["id"]);
                foreach (JObject r in (JArray)o["rows"])
                {
                    int mask = (int)r["mask"];
                    double due = ShopMath.DueOf(T, p, id => { int i = Array.IndexOf(p.Grants, id); return i >= 0 && (mask & (1 << i)) != 0; });
                    Eq(f, p.Id + " mask " + mask, due, r["due"]);
                }
            }
            foreach (JObject o in (JArray)F["itemValue"]) Eq(f, "itemValue " + o["g"], ShopMath.ItemValue(T, (string)o["g"]), o["v"]);
            NoFails(f);
        }

        // ---------------- 글 검사 ----------------
        [Test]
        public void ForbiddenPhrasesMatchJsOnSentenceSet()
        {
            var g = M1Fixture.Golden("lint.json"); var lint = new ContentLint(T); var f = new List<string>(); int n = 0;
            foreach (JObject r in (JArray)g["sentences"])
            {
                string t = (string)r["t"]; n++;
                if (lint.Forbid(t) != (bool)r["forbid"]) f.Add("FORBID_RE(" + t + ") " + lint.Forbid(t) + " != " + r["forbid"]);
                if (lint.JosaLeft(t) != (bool)r["josa"]) f.Add("JOSA_LEFT_RE(" + t + ")");
                if (lint.DayBan(t) != (bool)r["day"]) f.Add("DAY_BAN_RE(" + t + ")");
                if (lint.DiaryBan(t) != (bool)r["diary"]) f.Add("DIARY_BAN_RE(" + t + ")");
            }
            Assert.GreaterOrEqual(n, 200, "문장 세트 200 이상");
            NoFails(f);
        }

        [Test]
        public void CheckContentHasZeroViolations()
        {
            var g = M1Fixture.Golden("lint.json");
            Assert.AreEqual(0, ((JArray)g["checkContent"]).Count, "원본 checkContent 가 0 이어야 골든이 성립");
            var bad = new ContentLint(T).CheckContent();
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void CheckContentCatchesTheSameBreakagesAsJs()
        {
            var lint = new ContentLint(T);
            // 1) 바다 친구 장소 글에 금지 표현
            var sea = T.SeaById["fish"]; string bak = sea.Where;
            try { sea.Where = "당장 와요"; var r = lint.CheckContent(); Assert.AreEqual(1, r.Count, string.Join("|", r)); Assert.AreEqual("바다 친구 fish: 금지 표현 또는 조사 잔여 — 당장 와요", r[0]); } finally { sea.Where = bak; }
            // 2) 친화 표에서 선물 빠짐
            string aff = T.Affinity["shell"];
            try { T.Affinity.Remove("shell"); var r = lint.CheckContent(); Assert.AreEqual(1, r.Count, string.Join("|", r)); Assert.AreEqual("선물 shell: AFFINITY 에 없음", r[0]); } finally { T.Affinity["shell"] = aff; }
            // 3) 아이콘 빠짐
            var ic = T.Lint.Icons["shell"];
            try { T.Lint.Icons.Remove("shell"); var r = lint.CheckContent(); Assert.AreEqual(1, r.Count, string.Join("|", r)); Assert.AreEqual("선물 shell: ICONS 에 아이콘 없음", r[0]); } finally { T.Lint.Icons["shell"] = ic; }
            // 4) 선물 이름에 금지 표현 (원본은 꾸미기 잠금 문구 경로로 여러 줄이 걸린다)
            var gift = T.GiftById["shell"]; string gn = gift.Name;
            try { gift.Name = "빨리"; var r = lint.CheckContent(); Assert.GreaterOrEqual(r.Count, 1); } finally { gift.Name = gn; }
            Assert.IsEmpty(lint.CheckContent());
        }

        [Test]
        public void DecorSelfCheckAndLayoutRules()
        {
            var lint = new ContentLint(T);
            Assert.IsEmpty(lint.DecorSelfCheck(), string.Join("\n", lint.DecorSelfCheck()));
            Assert.AreEqual(20, DecorRules.OpenCount(T, 6, 30), "단계 6·정원 30 에서 열린 자리");
            Assert.AreEqual(0.8, T.Bal.DecorVisMin); Assert.AreEqual(0.65, T.Bal.DecorVisMinBack);
            Assert.IsTrue(T.DecorSlotById["B3"].Chip == "back" && !T.DecorSlotById["B3"].Blocked, "B3 는 열림(뒤 구역 가시 65% 기준)");
            Assert.IsTrue(T.DecorSlotById["B5"].Blocked);
            Assert.AreEqual("F2,F3,F7,B1,B2,B4,B5,B6".Split(',').OrderBy(x => x), T.DecorSlots.Where(s => s.Blocked).Select(s => s.Id).OrderBy(x => x));
        }
    }
}
