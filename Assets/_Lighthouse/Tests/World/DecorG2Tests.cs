using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Lighthouse.Core;
using Lighthouse.Render;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>
    /// 꾸미기 G2'(U4·U5): 막힌 8자리 재설계(겹침 0·가시율)·아이템 25종·해금·팩 가격·구매 검증. 순수 C#(dotnet test 가능).
    /// 설계: team/design/decor-g2-unity.md. 원본 HTML 골든은 8자리를 "막힘"으로 갖고 있어 새 골든(Tools/golden/g2/decor_g2.json)과 새 아이템 자체 스냅샷(decor_items.txt)을 쓴다.
    /// </summary>
    public sealed class DecorG2Tests
    {
        static GameTables T => M1Fixture.Tables;
        static string G2Dir => Path.Combine(M1Fixture.Root, "Tools", "golden", "g2");
        static readonly string[] Redesigned = { "B1", "B2", "B4", "B5", "B6", "F2", "F3", "F7" };

        // ---------------- 데이터 정합: 표(decor.json) ↔ 굽기용 사본(RenderData) ----------------
        [Test]
        public void TablesAndRenderCopyAgree()
        {
            Assert.AreEqual(T.DecorItems.Count, DecorBaker.Items.Count);
            for (int i = 0; i < T.DecorItems.Count; i++)
            {
                var a = T.DecorItems[i]; var b = DecorBaker.Items[i];
                Assert.AreEqual(a.Id, b.Id); Assert.AreEqual(a.Name, b.Name, a.Id); Assert.AreEqual(a.Size, b.Size, a.Id); Assert.AreEqual(a.Src, b.Src, a.Id);
                CollectionAssert.AreEqual(a.Zones, b.Zones, a.Id); Assert.AreEqual(a.Ax, b.Ax, a.Id); Assert.AreEqual(a.Ay, b.Ay, a.Id); CollectionAssert.AreEqual(a.Rows, b.Rows, a.Id);
            }
            Assert.AreEqual(T.DecorSlots.Count, DecorBaker.Slots.Count);
            for (int i = 0; i < T.DecorSlots.Count; i++)
            {
                var a = T.DecorSlots[i]; var b = DecorBaker.Slots[i];
                Assert.AreEqual(a.Id, b.Id); Assert.AreEqual(a.Chip, b.Chip, a.Id); Assert.AreEqual(a.Size, b.Size, a.Id); Assert.AreEqual(a.Blocked, b.Blocked, a.Id); Assert.AreEqual(a.MinStage, b.MinStage, a.Id);
                for (int st = 1; st <= 6; st++)
                    for (int gl = 0; gl <= 30; gl += 2) Assert.AreEqual(DecorRules.SlotOpen(a, st, gl), b.OpenAt(st, gl), a.Id + " st" + st + " gl" + gl);
                if (!b.Xy) for (int n = 6; n <= 9; n++) { var x = a.AbAt(n); var y = b.AB(n); Assert.AreEqual(x[0], y[0], 1e-9, a.Id); Assert.AreEqual(x[1], y[1], 1e-9, a.Id); }
            }
        }

        [Test]
        public void ItemTableIsTheDesignedTwentyFive()
        {
            Assert.AreEqual(25, T.DecorItems.Count);
            Assert.AreEqual(10, T.DecorItems.Count(i => i.Phase == "GL")); Assert.AreEqual(15, T.DecorItems.Count(i => i.Phase == "G2"));
            Assert.AreEqual(4, T.DecorItems.Count(i => i.Src == "iap" && i.Draft), "팩 4종은 시안 격자(draft, D42)");
            Assert.AreEqual(0, T.DecorItems.Count(i => i.Src != "iap" && i.Draft));
            Assert.AreEqual(3, T.DecorItems.Count(i => i.Src == "ad"));
            Assert.AreEqual(25, T.DecorItems.Select(i => i.Id).Distinct().Count());
            foreach (var z in T.DecorZones)
            {
                int n = T.DecorItems.Count(i => i.Zones.Contains(z.Id) && i.Src != "iap");
                Assert.GreaterOrEqual(n, z.Id == "path" ? 5 : z.Id == "sky" ? 5 : 7, z.Id + " 구역 무료·광고 아이템 수");
            }
            Assert.IsEmpty(new ContentLint(T).DecorSelfCheck(), string.Join("\n", new ContentLint(T).DecorSelfCheck()));
        }

        [Test]
        public void KeeperStopsTableMatchesTheGameLoop()
        {
            var f = typeof(Lighthouse.World.GameLoop).GetField("Stops", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(f, "GameLoop.Stops");
            var loop = ((System.Collections.IEnumerable)f.GetValue(null)).Cast<object>().ToList();
            Assert.AreEqual(DecorCheck.KeeperStops.Length, loop.Count);
            for (int i = 0; i < loop.Count; i++)
            {
                var t = loop[i]; var tt = t.GetType(); var ks = DecorCheck.KeeperStops[i];
                Assert.AreEqual(ks.act, tt.GetField("Item1").GetValue(t)); Assert.AreEqual(ks.stage, tt.GetField("Item2").GetValue(t));
                Assert.AreEqual(ks.a, (double)tt.GetField("Item3").GetValue(t)); Assert.AreEqual(ks.b, (double)tt.GetField("Item4").GetValue(t)); Assert.AreEqual(ks.noflag, (bool)tt.GetField("Item6").GetValue(t));
            }
        }

        // ---------------- 자리: 새 골든(열림 비트·좌표) ----------------
        [Test]
        public void RedesignedSlotsMatchTheNewGolden()
        {
            var g = JObject.Parse(File.ReadAllText(Path.Combine(G2Dir, "decor_g2.json"), new UTF8Encoding(false)));
            var rows = (JArray)g["slots"]; Assert.AreEqual(8, rows.Count);
            CollectionAssert.AreEquivalent(Redesigned, rows.Select(r => (string)r["id"]));
            foreach (JObject r in rows)
            {
                var sl = T.DecorSlotById[(string)r["id"]]; var bits = (JArray)r["bits"];
                Assert.AreEqual((string)r["chip"], sl.Chip, sl.Id); Assert.AreEqual((string)r["size"], sl.Size, sl.Id); Assert.IsFalse(sl.Blocked, sl.Id);
                Assert.AreEqual((int)r["minStage"], sl.MinStage, sl.Id);
                for (int st = 1; st <= 6; st++) for (int gl = 0; gl <= 30; gl++)
                        Assert.AreEqual(((string)bits[st - 1])[gl] == '1', DecorRules.SlotOpen(sl, st, gl), sl.Id + " 단계 " + st + " 정원 " + gl);
                for (int n = 6; n <= 9; n++) { var ab = sl.AbAt(n); Assert.AreEqual((double)r["ab"][0], ab[0], 1e-9, sl.Id); Assert.AreEqual((double)r["ab"][1], ab[1], 1e-9, sl.Id); }
            }
        }

        [Test]
        public void SlotOpenCurveMatchesTheMilestoneClaims()
        {
            // 열린 자리 합계(단계 × 정원 레벨): 정원 Lv.8·12·16 이정표 "꾸미기 자리가 늘어나요"는 단계 4 이상에서 +2 가 사실
            Func<int, int, int> open = (st, gl) => DecorRules.OpenCount(T, st, gl);
            foreach (int st in new[] { 4, 5, 6 })
                foreach (var (lo, hi) in new[] { (7, 8), (11, 12), (15, 16) })
                    Assert.AreEqual(2, open(st, hi) - open(st, lo), "단계 " + st + " 정원 Lv." + hi + " 에서 늘어나는 자리");
            Assert.AreEqual(28, open(6, 16)); Assert.AreEqual(28, open(6, 30));
            // 처음(1단계 정원 Lv.0)과 단계별(정원 0·16) 합계 표 — 설계 문서 3-3
            var want = new Dictionary<int, (int lv0, int lv16)> { { 1, (11, 13) }, { 2, (13, 15) }, { 3, (15, 17) }, { 4, (20, 26) }, { 5, (21, 27) }, { 6, (22, 28) } };
            foreach (var kv in want) { Assert.AreEqual(kv.Value.lv0, open(kv.Key, 0), "단계 " + kv.Key + " 정원 0"); Assert.AreEqual(kv.Value.lv16, open(kv.Key, 16), "단계 " + kv.Key + " 정원 16"); }
            // 어느 구역이든 열리면 놓을 수 있는 아이템이 하나 이상 있다
            foreach (var sl in T.DecorSlots) Assert.IsTrue(T.DecorItems.Any(i => i.Zones.Contains(sl.Chip) && i.Src != "iap" && T.SizeRank[i.Size] <= T.SizeRank[sl.Size]), sl.Id);
        }

        // ---------------- 겹침 0 · 가시율 ----------------
        [Test]
        public void NoOverlapInAllSixStages()
        {
            var r = DecorCheck.SlotCheck(DecorBaker.Slots);
            Assert.IsEmpty(r.Violations, string.Join("\n", r.Violations));
            Assert.AreEqual(28 * 6 - (2 + 2 + 2 + 2) * 0 - SlotStageGaps(), r.Count, "검사한 자리-단계 수");
            Assert.GreaterOrEqual(r.MinPairDots, 3.9);
            TestContext.Out.WriteLine("겹침 검사 자리-단계 " + r.Count + ", 같은 구역 최소 간격 " + r.MinPairDots.ToString("F2") + " 도트(길 포함), 길 밖 " + r.MinPairNonPathDots.ToString("F2") + " 도트");
        }

        static int SlotStageGaps()
        {
            int gaps = 0;                                        // 단계 조건·최소 단계로 아직 없는 자리-단계 수
            foreach (var s in DecorBaker.Slots) for (int st = 1; st <= 6; st++) if (st < s.MinStage || (s.OpenKind == "stage" && st < s.OpenVal)) gaps++;
            return gaps;
        }

        [Test]
        public void VisibilityMeetsTheCriteriaWithEverySlotFilledAndEveryAllowedItem()
        {
            var v = DecorCheck.Visibility(DecorBaker.Slots, DecorBaker.Items);
            Assert.IsEmpty(v.Bad, string.Join("\n", v.Bad));
            var sb = new StringBuilder("가시율 최소 " + (v.Min * 100).ToString("F1") + "% " + v.MinAt);
            foreach (var kv in v.Rows) foreach (var sl in kv.Value) { var s = DecorBaker.Slot(sl.Key); Assert.GreaterOrEqual(sl.Value, DecorCheck.Threshold(s), kv.Key + "단계 " + sl.Key); }
            foreach (var id in Redesigned) { var min = v.Rows.Where(k => k.Value.ContainsKey(id)).Min(k => k.Value[id]); sb.Append("\n  " + id + " 최소 " + (min * 100).ToString("F0") + "%"); }
            TestContext.Out.WriteLine(sb.ToString());
        }

        [Test]
        public void VisibilityPortReproducesTheHtmlMeasurementsOnTheOldBlockedCoordinates()
        {
            // 포팅 검증: HTML(package-decor-g1.md 5절)이 옛 좌표·옛 아이템 10종으로 잰 값과 같은 자리 수 범위. 옛 좌표로 되돌려(메모리 사본) 재측정.
            var old = new Dictionary<string, (double a, double b)> { { "F2", (2.5, 1.5) }, { "F3", (4.5, 1.5) }, { "B1", (-0.5, -3.5) }, { "B2", (-4.5, -3.5) }, { "B4", (1.5, -4.5) }, { "B5", (1, -3.5) } };
            var slots = DecorBaker.Slots.Select(s => old.TryGetValue(s.Id, out var o)
                ? new DecorSlot { Id = s.Id, Chip = s.Chip, Size = s.Size, OpenKind = s.Id == "B5" ? "base" : s.OpenKind, OpenVal = s.Id == "B5" ? 1 : s.OpenVal, Ka = 0, Ca = o.a, Kb = 0, Cb = o.b }
                : s).ToList();
            var legacyItems = DecorBaker.Items.Where(i => DecorLayout.LegacyItemIds.Contains(i.Id)).ToList();
            var v = DecorCheck.Visibility(slots, legacyItems);
            Func<string, int, double> g = (id, st) => v.Rows[st][id];
            Assert.AreEqual(0.0, g("F2", 1), 0.01, "F2 는 나무에 완전히 가림(HTML 0%)");
            Assert.That(g("F3", 1), Is.InRange(0.10, 0.20), "F3 1단계(HTML 14%)");
            Assert.That(g("B2", 6), Is.LessThan(0.02), "B2 6단계(HTML 0%)");
            Assert.That(g("B1", 6), Is.InRange(0.70, 0.76), "B1 6단계(HTML 73%)");
            Assert.That(g("B5", 4), Is.InRange(0.38, 0.44), "B5 4단계(HTML 41%)");
        }

        [Test]
        public void SlotCheckCatchesInsideScenery()
        {
            // 집(-3.5,3.5) 한가운데: 앞쪽 맞닿음 예외가 발자국 안까지 통과시키던 구멍(G1 9절)이 막혔는지
            var s = new List<DecorSlot> { new DecorSlot { Id = "T1", Chip = "front", Size = "S", OpenKind = "base", OpenVal = 1, Ca = -3.5, Cb = 3.5 } };
            var r = DecorCheck.SlotCheck(s);
            Assert.IsTrue(r.Violations.Any(m => m.Contains("집") && m.Contains("풍경 발자국 안")), string.Join("\n", r.Violations.Take(3)));
        }

        // ---------------- 아이템: 낮·밤·계절·외형에서 모두 그려짐, 새 아이템 자체 스냅샷 ----------------
        static ulong Hash(Bitmap b)
        {
            ulong h = 1469598103934665603UL; Action<long> mix = v => { h ^= (ulong)v; h *= 1099511628211UL; };
            mix(b.Ax); mix(b.Ay); mix(b.W); mix(b.H); foreach (var p in b.D) mix(p);
            return h;
        }

        static IEnumerable<string> SnapshotLines()
        {
            foreach (var it in DecorBaker.Items.Where(i => !DecorLayout.LegacyItemIds.Contains(i.Id)))
                foreach (var sn in Palettes.Seasons)
                    foreach (var sk in new[] { "classic", "seaside", "festival" })
                    {
                        var b = DecorBaker.Bake(it, sn, sk);
                        yield return it.Id + ";" + sn + ";" + sk + ";" + Hash(b).ToString("x16") + ";" + Hash(Night.Of(b)).ToString("x16");
                    }
        }

        [Test]
        public void EveryItemBakesInEverySeasonSkinAndNight()
        {
            foreach (var it in DecorBaker.Items)
                foreach (var sn in Palettes.Seasons)
                    foreach (var sk in new[] { "classic", "seaside", "festival", "dev" })
                    {
                        var b = DecorBaker.Bake(it, sn, sk); var n = Night.Of(b);
                        int opaque = b.D.Count(p => (p >> 24) == 255), nOpaque = n.D.Count(p => (p >> 24) == 255);
                        Assert.GreaterOrEqual(opaque, it.Rows.Sum(r => r.Count(c => c != '.')) / 2, it.Id + " " + sn + " " + sk + " 몸통 픽셀");
                        Assert.AreEqual(opaque, nOpaque, it.Id + " 밤 사본 픽셀 수");
                        Assert.AreEqual(it.W + 16, b.W); Assert.AreEqual(-(it.Ax + 8), b.Ax); Assert.AreEqual(-it.Ay, b.Ay);
                        var icon = DecorBaker.Icon(it); Assert.IsTrue(icon.D.Any(p => (p >> 24) != 0), it.Id + " 아이콘");
                    }
        }

        [Test]
        public void NewItemsMatchTheirOwnSnapshot()
        {
            string path = Path.Combine(G2Dir, "decor_items.txt"); var now = SnapshotLines().ToList();
            if (Environment.GetEnvironmentVariable("G2_UPDATE_SNAPSHOT") == "1") { File.WriteAllText(path, string.Join("\n", now) + "\n", new UTF8Encoding(false)); Assert.Inconclusive("스냅샷을 새로 썼다: " + path); }
            var want = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
            Assert.AreEqual(want.Count, now.Count, "행 수(아이템 15 × 계절 4 × 외형 3)");
            var bad = now.Where((l, i) => l != want[i]).Take(5).ToList();
            Assert.IsEmpty(bad, "스냅샷과 다름(의도한 아트 변경이면 G2_UPDATE_SNAPSHOT=1 로 갱신):\n" + string.Join("\n", bad));
        }

        // ---------------- 해금 ----------------
        static GameState Fresh() { var st = GameState.FromJson(M1Fixture.Migrator().Defaults()); return st; }

        [Test]
        public void UnlockRulesPerSource()
        {
            var st = Fresh();
            Assert.IsTrue(DecorRules.Owned(T, st, "path_stone")); Assert.IsFalse(DecorRules.Owned(T, st, "pumpkin_pile"));
            // 광고 전용 3종 업적: 1,000 · 5,000 · 15,000 척 (U5)
            foreach (var (id, need) in new[] { ("lantern_float", 1000.0), ("kite_red", 5000.0), ("balloon_cluster", 15000.0) })
            {
                st.Guided = need - 1; Assert.IsFalse(DecorRules.Owned(T, st, id), id); st.Guided = need; Assert.IsTrue(DecorRules.Owned(T, st, id), id);
            }
            st = Fresh(); st.Isle.Own["kite_red"] = 1; Assert.IsTrue(DecorRules.Owned(T, st, "kite_red"), "광고로 받는 경로 자리(own)");
            // 호박 더미: 가을 경로 + 안내한 배 1,500 척(D44)
            st = Fresh(); st.Guided = 1499; Assert.IsFalse(DecorRules.Owned(T, st, "pumpkin_pile")); st.Guided = 1500; Assert.IsTrue(DecorRules.Owned(T, st, "pumpkin_pile"));
            st = Fresh(); Assert.IsTrue(DecorRules.SeasonTick(T, st, "autumn")); Assert.IsTrue(DecorRules.Owned(T, st, "pumpkin_pile")); Assert.IsFalse(DecorRules.Owned(T, st, "snowman"));
            Assert.IsTrue(DecorRules.SeasonTick(T, st, "winter")); Assert.IsTrue(DecorRules.Owned(T, st, "snowman")); Assert.IsFalse(DecorRules.SeasonTick(T, st, "winter"), "두 번째는 바뀌지 않음");
            Assert.IsTrue(DecorRules.SeasonTick(T, st, "spring")); Assert.IsTrue(DecorRules.Owned(T, st, "path_petal"));
            // 2차: 안내한 배 수
            st = Fresh(); st.Guided = 19999; Assert.IsFalse(DecorRules.Owned(T, st, "sandcastle")); st.Guided = 20000; Assert.IsTrue(DecorRules.Owned(T, st, "sandcastle"));
            // 2차: 레벨
            st = Fresh(); st.Growth["garden"] = Progress.LvAt(T, 16); Assert.IsFalse(DecorRules.Owned(T, st, "path_brick")); st.Growth["garden"] = Progress.LvAt(T, 17); Assert.IsTrue(DecorRules.Owned(T, st, "path_brick"));
            // 팩: 구매 기록
            st = Fresh(); Assert.IsFalse(DecorRules.Owned(T, st, "festival_arch")); st.Owned["pack:festival"] = true; Assert.IsTrue(DecorRules.Owned(T, st, "festival_arch")); Assert.IsTrue(DecorRules.Owned(T, st, "festival_stall"));
        }

        [Test]
        public void AdAchievementValuesDoNotOverlapStageThresholds()
        {
            var ad = T.DecorItems.Where(i => i.Src == "ad").OrderBy(i => i.NeedVal).Select(i => i.NeedVal).ToArray();
            CollectionAssert.AreEqual(new[] { 1000.0, 5000.0, 15000.0 }, ad);
            foreach (double a in ad) foreach (int g in T.Goal.Where(x => x > 0)) { double r = a / g; Assert.IsTrue(r < 0.75 || r > 1.25, a + " 이 승급 문턱 " + g + " 의 ±25% 안"); Assert.AreNotEqual((double)g, a); }
            var all = T.DecorItems.Where(i => i.Src == "guided" || i.Src == "ad" || i.AltStr != null).Select(i => i.Src == "season" ? i.AltNum[1] : i.NeedVal).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(), "안내한 배 기준 값이 서로 겹치지 않음");
            Assert.AreEqual(1500.0, T.DecorItemById["pumpkin_pile"].AltNum[1]);
        }

        // ---------------- 팩 상품: 차감가 · 구매 검증 · 복원 ----------------
        [Test]
        public void FestpackValuesNeverExceedThePriceAndDueStaysPositive()
        {
            var p = T.Products.First(x => x.Id == "festpack");
            CollectionAssert.AreEqual(new[] { "decor:festival", "keeper:sailor", "pack:festival" }, p.Grants);
            double sum = p.Grants.Sum(g => ShopMath.ItemValue(T, p, g));
            Assert.LessOrEqual(sum, p.Price); Assert.AreEqual(2190.0, sum, "1,100 × 3 을 2,200/3,300 비율로 줄이고 10원 단위 내림");
            double last = double.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                double due = ShopMath.DueOf(T, p, id => (mask & (1 << Array.IndexOf(p.Grants, id))) != 0);
                int have = Convert.ToString(mask, 2).Count(c => c == '1');
                if (have == 0) Assert.AreEqual(p.Price, due); else if (have == 3) Assert.AreEqual(0.0, due); else { Assert.Greater(due, 0.0, "mask " + mask); Assert.Less(due, p.Price, "mask " + mask); Assert.AreEqual(0.0, due % T.PriceStep); }
            }
            Assert.AreEqual(730.0, ShopMath.DueOf(T, p, id => id != "pack:festival"), "장식 4종만 남으면 730원");
            // 다른 상품은 이전과 같다(원본 골든이 따로 지킨다): pack 이 없으면 ItemValue 와 같은 값
            foreach (var q in T.Products.Where(x => !x.Grants.Any(ShopMath.IsPackGrant))) foreach (var gr in q.Grants) Assert.AreEqual(ShopMath.ItemValue(T, gr), ShopMath.ItemValue(T, q, gr), q.Id + " " + gr);
        }

        [Test]
        public void OwnedRestoreGivesThePackGrantToEarlierBuyers()
        {
            var o = new JObject { ["decor:festival"] = true };
            Assert.IsTrue(ShopMath.NormalizeOwned(T, o)); Assert.IsTrue((bool)o["pack:festival"]);
            Assert.IsFalse(ShopMath.NormalizeOwned(T, o), "두 번째는 바뀌지 않음");
            var none = new JObject { ["keeper:sailor"] = true }; Assert.IsFalse(ShopMath.NormalizeOwned(T, none), "시작 꾸러미(선원 옷만)로는 팩이 열리지 않음"); Assert.IsNull(none["pack:festival"]);
            // 저장을 불러오면(migrate) 같은 복원이 일어난다
            var s = M1Fixture.Migrator().Defaults(); s["owned"] = new JObject { ["decor:festival"] = true };
            var m = M1Fixture.Migrator().Migrate(s); Assert.IsTrue((bool)m["owned"]["pack:festival"]);
        }

        [Test]
        public void CleanChoicesVerifiesPurchasedAndEarnedDecorOnLoad()
        {
            var mig = M1Fixture.Migrator();
            JObject Save(Action<JObject> f) { var s = mig.Defaults(); s["stage"] = 6; s["growth"]["garden"] = Progress.LvAt(T, 20); f(s); return mig.Migrate(s); }
            // 구매 안 한 팩 장식은 치운다 / 산 뒤에는 남는다
            var a = Save(s => s["isle"]["put"] = new JObject { ["B2"] = "festival_stall", ["F1"] = "festival_lanterns", ["F4"] = "fence_wood" });
            Assert.IsNull(a["isle"]["put"]["B2"]); Assert.IsNull(a["isle"]["put"]["F1"]); Assert.AreEqual("fence_wood", (string)a["isle"]["put"]["F4"]);
            var b = Save(s => { s["owned"] = new JObject { ["pack:festival"] = true }; s["isle"]["put"] = new JObject { ["B2"] = "festival_stall", ["F1"] = "festival_lanterns" }; });
            Assert.AreEqual("festival_stall", (string)b["isle"]["put"]["B2"]); Assert.AreEqual("festival_lanterns", (string)b["isle"]["put"]["F1"]);
            // 예전에 산 사람(decor:festival 만 있음)도 복원돼서 남는다
            var c = Save(s => { s["owned"] = new JObject { ["decor:festival"] = true }; s["isle"]["put"] = new JObject { ["F5"] = "festival_arch" }; });
            Assert.AreEqual("festival_arch", (string)c["isle"]["put"]["F5"]);
            // 안내한 배 수·레벨 조건이 안 되면 놓은 것을 치운다
            var d = Save(s => { s["guided"] = 999; s["isle"]["put"] = new JObject { ["SK1"] = "kite_red", ["SH1"] = "lantern_float", ["P1"] = "path_plank" }; });
            Assert.IsNull(d["isle"]["put"]["SK1"]); Assert.IsNull(d["isle"]["put"]["SH1"]); Assert.IsNull(d["isle"]["put"]["P1"]);
            var e = Save(s => { s["guided"] = 5000; s["isle"]["put"] = new JObject { ["SK1"] = "kite_red", ["SH1"] = "lantern_float", ["P1"] = "path_plank" }; });
            Assert.AreEqual("kite_red", (string)e["isle"]["put"]["SK1"]); Assert.AreEqual("lantern_float", (string)e["isle"]["put"]["SH1"]); Assert.IsNull(e["isle"]["put"]["P1"], "나무판 길은 40,000");
            // 다시 정한 자리: 열려 있지 않으면 치운다(옛 "막힘 자리는 값 보존"은 끝). 단계 1 에서는 B1(정원 Lv.8 + 단계 4) 이 닫혀 있다
            var f = Save(s => { s["stage"] = 1; s["isle"]["put"] = new JObject { ["B1"] = "lamp_post" }; });
            Assert.IsNull(f["isle"]["put"]["B1"]);
            var g = Save(s => { s["stage"] = 4; s["isle"]["put"] = new JObject { ["B1"] = "lamp_post" }; });
            Assert.AreEqual("lamp_post", (string)g["isle"]["put"]["B1"]);
            // 알려지지 않은 own(광고 경로가 아닌 아이템)은 버린다
            var h = Save(s => s["isle"]["own"] = new JObject { ["path_stone"] = 1, ["kite_red"] = 1, ["pumpkin_pile"] = 1 });
            Assert.IsNull(h["isle"]["own"]["path_stone"]); Assert.AreEqual(1, (int)h["isle"]["own"]["kite_red"]); Assert.AreEqual(1, (int)h["isle"]["own"]["pumpkin_pile"]);
        }

        [Test]
        public void ImportKeepsPackPlacementsOnTheSameDevice()
        {
            var mig = M1Fixture.Migrator(); var code = new SaveCode(T, mig);
            var s = mig.Defaults(); s["stage"] = 6; s["growth"]["garden"] = Progress.LvAt(T, 20); s["owned"] = new JObject { ["pack:festival"] = true, ["decor:festival"] = true };
            s["isle"]["put"] = new JObject { ["F5"] = "festival_arch", ["F4"] = "fence_wood" };
            var m = mig.Migrate(s); Assert.AreEqual("festival_arch", (string)m["isle"]["put"]["F5"]);
            string txt = code.Export(m);
            var same = code.Parse(txt, (JObject)s["owned"]); Assert.IsNull(same.Err, same.Err); Assert.AreEqual("festival_arch", (string)same.Data["isle"]["put"]["F5"], "같은 기기 가져오기");
            var other = code.Parse(txt, new JObject()); Assert.IsNull(other.Err); Assert.IsNull(other.Data["isle"]["put"]["F5"], "구매 기록이 없는 기기에서는 팩 장식을 치운다"); Assert.AreEqual("fence_wood", (string)other.Data["isle"]["put"]["F4"]);
            var noHint = code.Parse(txt); Assert.IsNull(noHint.Data["isle"]["put"]["F5"]);
        }

        [Test]
        public void LegacyDevFillIsUnchangedAndFullFillIsValid()
        {
            var legacy = DecorLayout.DevFill();
            Assert.AreEqual(20, legacy.Count); Assert.IsTrue(legacy.All(x => DecorLayout.LegacySlotIds.Contains(x.slot) && DecorLayout.LegacyItemIds.Contains(x.item)));
            var all = DecorLayout.DevFillAll(); Assert.AreEqual(28, all.Count);
            foreach (var (slot, item) in all) { var sl = DecorBaker.Slot(slot); var it = DecorBaker.Item(item); Assert.IsTrue(it.Zones.Contains(sl.Chip) && DecorBaker.SizeRank(it.Size) <= DecorBaker.SizeRank(sl.Size), slot + " " + item); }
        }

        // ---------------- 해금 진행도(시뮬) ----------------
        [Test]
        public void NewExposureDistributionBeatsTheOldDiagnosis()
        {
            // 이전 진단(content-review-structure 2-1): 1차 10종은 10일 안에 소진, 15~60일에 새 아이템 0, 자리 20 은 14일 안에 모두 열림
            var clock = M1Fixture.Clock(); var legacy = new HashSet<string>(DecorLayout.LegacyItemIds);
            foreach (var (name, ships, ve) in new[] { ("A", 468.0, 1), ("C", 48.0, 1), ("Z", 0.0, 1) })
            {
                var r = DecorUnlockModel.Run(T, clock, ships, 120, 1, ve);
                int oldIn15to60 = r.ItemDay.Count(kv => legacy.Contains(kv.Key) && kv.Value >= 15 && kv.Value <= 60);
                int newIn15to60 = r.ItemDay.Count(kv => !legacy.Contains(kv.Key) && T.DecorItemById[kv.Key].Src != "iap" && T.DecorItemById[kv.Key].Src != "season" && kv.Value >= 15 && kv.Value <= 60);
                Assert.AreEqual(0, oldIn15to60, name + " 이전(1차 10종)의 15~60일 새 아이템");
                Assert.GreaterOrEqual(newIn15to60, 5, name + " 지금의 15~60일 새 아이템(달력 계절 제외)");
                Assert.AreEqual(0, r.ItemDay.Count(kv => legacy.Contains(kv.Key) && kv.Value > 14), name + " 1차 10종은 14일 안에 소진(이전 진단과 같음)");
                Assert.GreaterOrEqual(r.SlotsIn(8, 60), name == "A" ? 3 : 1, name + " 8~60일 새 자리(이전 0)");
                Assert.AreEqual(28, r.SlotDay.Count, name + " 120일 안에 28자리 모두");
                TestContext.Out.WriteLine(name + " 15~60일 새 아이템 " + newIn15to60 + "(이전 " + oldIn15to60 + "), 8~60일 새 자리 " + r.SlotsIn(8, 60) + "(이전 0)");
            }
        }

        [Test]
        public void FullFillScenesComposeInEveryStageDayAndNight()
        {
            for (int st = 1; st <= 6; st++)
            {
                var put = DecorLayout.DevFillAll().Where(x => DecorBaker.Slot(x.slot).OpenAt(st, 32)).ToList();
                var bare = SceneSprites.Build(216, BgBaker.DesignH, "summer", st, gardenLv: 6, slotLv: 32, put: new List<(string, string)>());
                var full = SceneSprites.Build(216, BgBaker.DesignH, "summer", st, gardenLv: 6, slotLv: 32, put: put);
                Assert.AreEqual(put.Count, full.Lists.A.Concat(full.Lists.B).Concat(full.Lists.S).Count(e => e.K == 'd'), "단계 " + st + " 그려질 장식 수");
                foreach (int night in new[] { 0, 1 })
                {
                    var a = SceneComposer.Compose(bare, night).D; var b = SceneComposer.Compose(full, night).D; int diff = 0;
                    for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) diff++;
                    Assert.Greater(diff, put.Count * 10, "단계 " + st + (night == 1 ? " 밤" : " 낮") + " 장식이 그려진 픽셀");
                }
            }
        }

        [Test]
        public void NewDecorUnlockDaysAreSpreadOver15To60Days()
        {
            var clock = M1Fixture.Clock();
            var z = DecorUnlockModel.Run(T, clock, 0, 70, 1, 1);          // Z: 방치만
            var a = DecorUnlockModel.Run(T, clock, 468, 70, 1, 1);        // A: 하루 약 1시간
            var newFree = new[] { "path_brick", "sandcastle", "beach_umbrella", "paper_plane", "path_plank", "star_mobile" };
            foreach (var id in newFree)
            {
                Assert.IsTrue(z.ItemDay.ContainsKey(id), "Z " + id + " 70일 안에 해금"); Assert.That(z.ItemDay[id], Is.InRange(15, 60), "Z " + id);
                Assert.IsTrue(a.ItemDay.ContainsKey(id), "A " + id); Assert.That(a.ItemDay[id], Is.InRange(10, 45), "A " + id);
                Assert.LessOrEqual(a.ItemDay[id], z.ItemDay[id] + 1, id + ": 접속하는 쪽이 늦지 않다");
            }
            var zDays = newFree.Select(i => z.ItemDay[i]).OrderBy(x => x).ToArray();
            for (int i = 1; i < zDays.Length; i++) Assert.GreaterOrEqual(zDays[i] - zDays[i - 1], 1, "해금일이 하루씩은 벌어져 있다");
            TestContext.Out.WriteLine("Z: " + string.Join(", ", newFree.Select(i => i + "=" + z.ItemDay[i])));
            TestContext.Out.WriteLine("A: " + string.Join(", ", newFree.Select(i => i + "=" + a.ItemDay[i])));
        }
    }
}
