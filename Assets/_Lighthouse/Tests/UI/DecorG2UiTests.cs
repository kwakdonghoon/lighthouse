using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.UI.Model;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>꾸미기 G2' 의 화면 모델: 구역 칩·슬롯 격자·잠금 문구·팩 아이템 노출·새 해금 경로 문구. 순수 C#.</summary>
    public sealed class DecorG2UiTests
    {
        static IEnumerable<Block> All(IEnumerable<Block> bs) { foreach (var b in bs) { yield return b; if (b.Kids != null) foreach (var k in All(b.Kids)) yield return k; } }

        static UiFixture.Rig Open(int stage, double gardenLv, double guided = 0, UiFeatures f = null)
        {
            var r = UiFixture.Make(stage, 0, 0, false, f);
            r.Ctx.S.Guided = guided; r.Ctx.S.Growth["garden"] = gardenLv <= 0 ? 0 : Progress.LvAt(r.Ctx.T, (int)gardenLv);
            r.P.Open(PanelMode.Deco); return r;
        }

        static (int open, int locked, string chips) Zone(UiFixture.Rig r, string zone)
        {
            r.P.Act("zone:" + zone); var grid = r.P.Build().Blocks.First(b => b.Kind == BK.Grid && b.Style == "slots");
            var chips = string.Join(",", r.P.Build().Blocks.First(b => b.Style == "zones").Kids.Select(k => k.Text));
            return (grid.Kids.Count(c => !c.Locked), grid.Kids.Count(c => c.Locked), chips);
        }

        [Test]
        public void AllTwentyEightSlotsShowUpAcrossTheFiveZonesAtTheEnd()
        {
            var r = Open(6, 30); int total = 0; var perZone = new Dictionary<string, int> { { "path", 8 }, { "front", 7 }, { "back", 6 }, { "shore", 5 }, { "sky", 2 } };
            foreach (var kv in perZone) { var z = Zone(r, kv.Key); Assert.AreEqual(kv.Value, z.open, kv.Key); Assert.AreEqual(0, z.locked, kv.Key + " 잠긴 칸"); total += z.open; }
            Assert.AreEqual(28, total);
            Assert.AreEqual("길,앞,뒤,물가,하늘", Zone(r, "path").chips);
        }

        [Test]
        public void SkyChipHidesBeforeStageThreeAndBackChipStaysFromTheStart()
        {
            var r = Open(1, 0); Assert.AreEqual("길,앞,뒤,물가", Zone(r, "path").chips);
            Assert.AreEqual(1, Zone(r, "back").open, "처음엔 뒤 구역 B3 한 자리");
            var r3 = Open(3, 0); Assert.AreEqual("길,앞,뒤,물가,하늘", Zone(r3, "path").chips);
        }

        [Test]
        public void GardenLevelSlotsNeedTheIslandStageToo()
        {
            var T = UiFixture.Make(1, 0).Ctx.T;
            // 정원 Lv.16 인데 섬이 3단계: 앞 구역의 F2(Lv.12)·F3(Lv.16)는 닫혀 있고, 잠금 문구는 "섬 4단계"
            var r = Open(3, 16); var z = Zone(r, "front"); var grid = r.P.Build().Blocks.First(b => b.Kind == BK.Grid && b.Style == "slots");
            Assert.AreEqual(4, z.open, "앞: F1·F4·F5·F6"); var locked = grid.Kids.Where(c => c.Locked).ToList(); Assert.That(locked.Count, Is.GreaterThan(0));
            Assert.That(locked.All(c => c.Text == "섬 4단계"), string.Join("|", locked.Select(c => c.Text)));
            r.P.Act("slotlock:F2"); Assert.AreEqual("섬이 4단계가 되면 열려요.", r.P.Build().Blocks.First(b => b.Style == "status").Text);
            // 같은 정원 레벨이어도 섬 4단계가 되면 앞 구역이 7자리
            Assert.AreEqual(7, Zone(Open(4, 16), "front").open);
            // 정원 레벨이 모자란 자리는 정원 레벨 문구
            var r2 = Open(5, 0); r2.P.Act("zone:front"); r2.P.Act("slotlock:F2"); Assert.AreEqual("정원 Lv.12가 되면 열려요.", r2.P.Build().Blocks.First(b => b.Style == "status").Text);
        }

        [Test]
        public void NewItemsAppearInTheirZonesAndLockTextsStateTheCondition()
        {
            var r = Open(6, 10, guided: 0);
            r.P.Act("zone:path"); r.P.Act("slot:P1"); var pick = r.P.Build().Blocks.First(b => b.Kind == BK.Pick);
            var cells = pick.Kids.Where(c => c.Kind == BK.Cell && c.Icon != null && c.Icon.StartsWith("decor:")).ToList();
            var ids = cells.Select(c => c.Icon.Substring(6)).ToList();
            CollectionAssert.AreEquivalent(new[] { "path_stone", "path_shell", "path_brick", "path_plank", "path_petal" }, ids, "길 구역 아이템 5종");
            string Need(string id) => cells.First(c => c.Icon == "decor:" + id).Text;
            Assert.AreEqual("정원 Lv.17", Need("path_brick")); Assert.AreEqual("안내한 배 40,000척", Need("path_plank")); Assert.AreEqual("봄에 만나요", Need("path_petal"));
            r.P.Act("decoopen:path_plank"); Assert.AreEqual("안내한 배가 40,000척이 되면 만날 수 있어요.", r.P.Build().Blocks.SelectMany(b => All(new[] { b })).First(b => b.Style == "need").Text);
            // 하늘: 구름·소원 등불이 아니라 연·풍선·종이비행기·별 모빌
            var rs = Open(6, 30); rs.P.Act("zone:sky"); rs.P.Act("slot:SK1");
            var sky = rs.P.Build().Blocks.First(b => b.Kind == BK.Pick).Kids.Where(c => c.Icon != null && c.Icon.StartsWith("decor:")).Select(c => c.Icon.Substring(6)).ToList();
            CollectionAssert.AreEquivalent(new[] { "cloud_small", "kite_red", "balloon_cluster", "paper_plane", "star_mobile" }, sky);
            rs.P.Act("decoopen:kite_red"); Assert.AreEqual("안내한 배가 5,000척이 되면 만날 수 있어요.", rs.P.Build().Blocks.SelectMany(b => All(new[] { b })).First(b => b.Style == "need").Text);
        }

        [Test]
        public void PumpkinShowsBothFreePaths()
        {
            var r = Open(6, 30); r.P.Act("zone:front"); r.P.Act("slot:F1"); r.P.Act("decoopen:pumpkin_pile");
            string need = r.P.Build().Blocks.SelectMany(b => All(new[] { b })).First(b => b.Style == "need").Text;
            StringAssert.Contains("만나면 계속 가질 수 있어요", need); StringAssert.Contains("안내한 배가 1,500척이 되어도 만날 수 있어요", need);
            var r2 = Open(6, 30, guided: 1500); r2.P.Act("zone:front"); r2.P.Act("slot:F1");
            Assert.IsTrue(r2.P.Build().Blocks.First(b => b.Kind == BK.Pick).Kids.Any(c => c.Act == "put:pumpkin_pile"), "안내한 배 1,500척이면 놓을 수 있다");
        }

        [Test]
        public void PackItemsAreShownLockedOnlyWhileTheShopIsOnAndNeedThePurchase()
        {
            var on = Open(6, 30); on.P.Act("zone:back"); on.P.Act("slot:B2");
            var cellsOn = on.P.Build().Blocks.First(b => b.Kind == BK.Pick).Kids.Where(c => c.Icon != null && c.Icon.StartsWith("decor:festival")).ToList();
            Assert.AreEqual(4, cellsOn.Count, "뒤 B2(큰 자리)에는 팩 4종 모두 보인다(구매 전엔 잠김)"); Assert.IsTrue(cellsOn.All(c => c.Locked));
            Assert.AreEqual("축제 마을 팩", cellsOn[0].Text);
            on.P.Act("decoopen:festival_stall"); Assert.AreEqual("축제 마을 팩에 들어 있어요.", on.P.Build().Blocks.SelectMany(b => All(new[] { b })).First(b => b.Style == "need").Text);
            on.P.Act("put:festival_stall"); Assert.IsFalse(on.Ctx.S.Isle.Put.ContainsKey("B2"), "구매 전에는 놓이지 않는다");
            on.Ctx.S.Owned["pack:festival"] = true;
            on.P.Act("put:festival_stall"); Assert.AreEqual("festival_stall", on.Ctx.S.Isle.Put["B2"]);
            // 상점이 꺼지면 목록에서 사라진다
            var off = Open(6, 30, 0, new UiFeatures { Shop = false, Ads = false, PaymentTest = false }); off.P.Act("zone:back"); off.P.Act("slot:B2");
            Assert.IsFalse(off.P.Build().Blocks.First(b => b.Kind == BK.Pick).Kids.Any(c => c.Icon != null && c.Icon.StartsWith("decor:festival")));
        }

        [Test]
        public void AutoPlacementNeverUsesLockedOrUnboughtItems()
        {
            var r = Open(6, 30, guided: 0); r.P.Act("zone:front"); r.P.Act("auto");
            foreach (var kv in r.Ctx.S.Isle.Put) Assert.IsTrue(DecorRules.Owned(r.Ctx.T, r.Ctx.S, kv.Value), kv.Key + " " + kv.Value);
        }

        [Test]
        public void NewTextsPassTheContentLint()
        {
            var T = UiFixture.Make(1, 0).Ctx.T; var lint = new ContentLint(T);
            Assert.IsEmpty(lint.CheckContent(), string.Join("\n", lint.CheckContent()));
            var texts = lint.DecorTexts();
            foreach (var it in T.DecorItems.Where(i => i.Phase == "G2")) CollectionAssert.Contains(texts, it.Name);
            Assert.IsTrue(texts.Contains("안내한 배가 15,000척이 되면 만날 수 있어요.".Replace("15,000", "15000")));
            Assert.IsTrue(texts.Any(t => t.Contains("섬이 4단계가 되면 열려요.")));
        }
    }
}
