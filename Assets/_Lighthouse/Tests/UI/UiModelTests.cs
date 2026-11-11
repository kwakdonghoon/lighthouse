using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Data;
using Lighthouse.UI.Model;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>M3: UI 모델(화면 블록·컨트롤러·레이아웃 계산) 시험. Unity 없이 dotnet test 로도, EditMode 로도 돈다.</summary>
    public static class UiFixture
    {
        public static UiStrings Strings() => UiStrings.Parse(File.ReadAllText(Path.Combine(M1Fixture.DataDir, "ko", "ui.json"), new System.Text.UTF8Encoding(false)));

        public sealed class Rig
        {
            public GameContext Ctx; public UiStrings Str; public RecordingHost Host; public PanelController P; public TestAdAdapter Ads; public TestPaymentAdapter Pay;
        }

        public static Rig Make(int stage, int ships, int regVisits = 0, bool night = false, UiFeatures f = null, long seed = 7)
        {
            var T = M1Fixture.Tables; var clock = M1Fixture.Clock();
            var ctx = SampleState.Make(T, clock, seed, stage, ships, regVisits, night);
            var str = Strings(); var host = new RecordingHost(); var ads = new TestAdAdapter(); var pay = new TestPaymentAdapter();
            var code = new SaveCode(T, M1Fixture.Migrator());
            var p = new PanelController(ctx, str, host, pay, ads, code, f);
            return new Rig { Ctx = ctx, Str = str, Host = host, P = p, Ads = ads, Pay = pay };
        }

        public static IEnumerable<Rig> States()
        {
            yield return Make(1, 0);
            yield return Make(3, 60, 1);
            yield return Make(4, 140, 3, true);
            yield return Make(6, 260, 6);
        }

        public static IEnumerable<PanelModel> AllScreens(Rig r)
        {
            var P = r.P;
            foreach (var m in new[] { PanelMode.Mail, PanelMode.Dex, PanelMode.Growth, PanelMode.Deco, PanelMode.Shop })
            {
                P.Open(m);
                if (m == PanelMode.Dex) foreach (var sub in new[] { "gift", "shine", "reg", "ship", "sea" }) { P.Act("dex:" + sub); yield return P.Build(); }
                else if (m == PanelMode.Growth) foreach (var sub in new[] { "grow", "offer", "mile", "log" }) { P.Act("grow:" + sub); if (sub == "grow") { P.Act("rolebox"); P.Act("trk:lens"); } yield return P.Build(); }
                else if (m == PanelMode.Deco) { yield return P.Build(); P.Act("dtab:skin"); yield return P.Build(); P.Act("dtab:place"); }
                else if (m == PanelMode.Shop) { yield return P.Build(); P.Act("adtoggle"); yield return P.Build(); }
                else yield return P.Build();
            }
            P.Open(PanelMode.Mail); P.Act("gear"); yield return P.Build(); P.Act("setmore"); P.Act("tips"); yield return P.Build(); P.Act("setback");
            P.OpenNews(new OfferNews(r.Ctx.RunOffline(3 * 3600 + 5))); yield return P.Build();
            P.Close();
        }
    }

    public class UiModelTests
    {
        [Test]
        public void StringsLoadAndNoMissingKeysInAnyScreen()
        {
            foreach (var r in UiFixture.States())
            {
                int n = 0;
                foreach (var m in UiFixture.AllScreens(r)) { Assert.IsNotNull(m.Heading); Assert.That(m.Blocks, Is.Not.Null); n++; }
                Assert.That(n, Is.GreaterThan(10));
                Assert.That(r.Str.Missing, Is.Empty, "문구 키가 ui.json 에 없음: " + string.Join(",", r.Str.Missing));
            }
        }

        static string Resolve(string t)
        {
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\{[0-9]\}(?=\{[을이은과]/|[을이은와]\()", "조개");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\{[0-9]\}", "3");
            if (t.StartsWith("을(를)") || t.StartsWith("이(가)")) t = "조개" + t;
            return GameContext.FixJosa(t);
        }

        [Test]
        public void UiTextsPassContentLint()
        {
            var T = M1Fixture.Tables; var str = UiFixture.Strings();
            var lint = new ContentLint(T);
            var all = str.AllTexts().Select(Resolve).ToList();
            foreach (var r in UiFixture.States()) foreach (var m in UiFixture.AllScreens(r)) foreach (var b in m.Blocks.SelectMany(x => x.Walk())) { if (b.Text != null) all.Add(b.Text); if (b.Sub != null) all.Add(b.Sub); if (b.Extra != null) all.Add(b.Extra); }
            var extra = new List<string>(all);
            Assert.That(lint.CheckContent(null, extra), Is.Empty);
            foreach (var t in all) Assert.IsFalse(lint.Forbid(t), "금지어: " + t);
        }

        [Test]
        public void EveryButtonHasActionAndLabelAndNoEmptyText()
        {
            foreach (var r in UiFixture.States())
                foreach (var m in UiFixture.AllScreens(r))
                    foreach (var b in m.Blocks.SelectMany(x => x.Walk()))
                    {
                        if (b.Kind == BK.Button) { Assert.IsFalse(string.IsNullOrEmpty(b.Act), "단추에 Act 없음: " + b.Text); Assert.IsFalse(string.IsNullOrEmpty(b.Text), "단추에 글자 없음: " + b.Act); }
                        if (b.Text != null) Assert.IsFalse(b.Text.Contains("["), "빈 키 표시: " + b.Text);
                        if (b.Text != null) Assert.IsFalse(b.Text.Contains("{") && b.Text.Contains("}") && !b.Text.Contains("{을") , "치환 안 된 자리: " + b.Text);
                    }
        }

        [Test]
        public void MailListMatchesStateAndExpandMarksRead()
        {
            var r = UiFixture.Make(3, 80);
            r.P.Open(PanelMode.Mail);
            var m = r.P.Build();
            var letters = m.Blocks.Where(b => b.Kind == BK.Letter).ToList();
            Assert.That(letters.Count, Is.EqualTo(r.Ctx.S.Letters.Count));
            var first = letters.First(b => b.New);
            Assert.IsFalse(first.Open);
            int un0 = r.Ctx.UnreadCount();
            r.P.Act(first.Act);
            Assert.That(r.Ctx.UnreadCount(), Is.EqualTo(un0 - 1));
            var again = r.P.Build().Blocks.First(b => b.Act == first.Act);
            Assert.IsTrue(again.Open); Assert.IsFalse(again.New); Assert.That(again.Sub.Length, Is.GreaterThan(5));
            r.P.Act("readall");
            Assert.That(r.Ctx.UnreadCount(), Is.EqualTo(0));
            var e = UiFixture.Make(1, 0); e.P.Open(PanelMode.Mail);
            Assert.That(e.P.Build().Blocks[0].Kind, Is.EqualTo(BK.Empty));
        }

        [Test]
        public void DexChipsCountsAndLocks()
        {
            var r = UiFixture.Make(4, 140, 2);
            var T = r.Ctx.T; var S = r.Ctx.S;
            r.P.Open(PanelMode.Dex);
            var m = r.P.Build();
            var chips = m.Blocks.First(b => b.Kind == BK.Seg && b.Style == "dx").Kids;
            Assert.That(chips.Count, Is.EqualTo(5));
            Assert.That(chips[0].Sub, Is.EqualTo(S.Found.Count + "/" + T.Gifts.Count));
            Assert.That(chips[3].Sub, Is.EqualTo(S.Dex.Count + "/" + T.Ships.Count));
            Assert.That(chips[4].Sub, Is.EqualTo(T.Sea.Count(x => GameContext.Get(S.Sea, x.Id) > 0) + "/" + T.Sea.Count));
            var grid = m.Blocks.First(b => b.Kind == BK.Grid);
            Assert.That(grid.Kids.Count, Is.EqualTo(T.Gifts.Count));
            foreach (var c in grid.Kids) if (c.Locked) Assert.That(c.Text, Is.EqualTo("???"));
            Assert.That(S.DexSeen, Is.EqualTo(r.Ctx.Discovered()), "도감을 열면 본 수가 갱신");
        }

        [Test]
        public void GrowthLogbookOnlyAtMaxStageAndOfferMovesPoints()
        {
            var r = UiFixture.Make(5, 200, 1);
            r.P.Open(PanelMode.Growth);
            Assert.That(r.P.Build().Blocks[0].Kids.Count, Is.EqualTo(3));
            var r6 = UiFixture.Make(6, 260, 2); r6.P.Open(PanelMode.Growth);
            Assert.That(r6.P.Build().Blocks[0].Kids.Count, Is.EqualTo(4));
            r.P.Act("grow:offer");
            var rows = r.P.Build().Blocks.Where(b => b.Kind == BK.Row && b.Style == "offer").ToList();
            Assert.That(rows, Is.Not.Empty);
            string gift = rows[0].Kids[0].Act.Substring(rows[0].Kids[0].Act.IndexOf(':') + 1);
            double before = r.Ctx.SpareOf(gift), pts0 = r.Ctx.S.Growth.Values.Sum();
            r.P.Act("offerall:" + gift);
            Assert.That(r.Ctx.SpareOf(gift), Is.LessThan(before));
            Assert.That(r.Ctx.S.Growth.Values.Sum(), Is.GreaterThan(pts0));
            r.P.Act("focus:garden"); Assert.That(r.Ctx.S.Focus, Is.EqualTo("garden"));
            r.P.Act("autooffer:off"); Assert.IsFalse(r.Ctx.S.AutoOffer);
        }

        [Test]
        public void DecoSlotsMatchRulesPlaceUndoRedoClear()
        {
            var r = UiFixture.Make(6, 260, 6);
            var T = r.Ctx.T; var S = r.Ctx.S;
            r.P.Open(PanelMode.Deco);
            int openTotal = DecorRules.OpenCount(T, r.Ctx.Stage, r.Ctx.LvlOf("garden"));
            int shown = 0;
            foreach (var z in T.DecorZones)
            {
                r.P.Act("zone:" + z.Id);
                var grid = r.P.Build().Blocks.FirstOrDefault(b => b.Kind == BK.Grid && b.Style == "slots");
                if (grid == null) continue;
                shown += grid.Kids.Count(c => !c.Locked);
            }
            Assert.That(shown, Is.EqualTo(openTotal));
            r.P.Act("zone:front");
            var openIds = DecorRules.ZoneOpen(T, "front", 6, r.Ctx.LvlOf("garden"));
            string slot = openIds[0].Id;
            r.P.Act("slot:" + slot); Assert.That(r.P.DecoSel, Is.EqualTo(slot));
            var pick = r.P.Build().Blocks.First(b => b.Kind == BK.Pick);
            var okItem = pick.Kids.First(c => c.Kind == BK.Cell && c.Act != null && c.Act.StartsWith("put:"));
            r.P.Act(okItem.Act);
            string placed = okItem.Act.Substring(4);
            Assert.That(S.Isle.Put[slot], Is.EqualTo(placed));
            Assert.That(r.P.UndoCount, Is.EqualTo(1));
            r.P.Act("undo"); Assert.IsFalse(S.Isle.Put.ContainsKey(slot));
            r.P.Act("redo"); Assert.That(S.Isle.Put[slot], Is.EqualTo(placed));
            // 자동 배치: 지금 구역 빈 자리를 채운다
            r.P.Act("auto");
            Assert.That(openIds.All(s => S.Isle.Put.ContainsKey(s.Id)), "빈 자리가 모두 찼다");
            r.P.Act("clearall:ask"); Assert.IsTrue(r.P.Build().Blocks.Any(b => b.Style == "confirm"));
            r.P.Act("clearall:yes");
            Assert.That(S.Isle.Put.Keys.Count(id => DecorRules.SlotOpen(T.DecorSlotById[id], 6, r.Ctx.LvlOf("garden"))), Is.EqualTo(0));
            Assert.That(r.Host.Log, Does.Contain("dirty"));
            // 닫은 칸(막힘)은 누를 수 없다
            var blocked = T.DecorSlots.First(s => s.Blocked);
            r.P.Act("slot:" + blocked.Id); Assert.IsNull(r.P.DecoSel);
        }

        [Test]
        public void DecoLockedSlotAndItemShowCondition()
        {
            var r = UiFixture.Make(2, 10);
            r.P.Open(PanelMode.Deco);
            var m = r.P.Build();
            var lockedItem = false;
            foreach (var z in r.Ctx.T.DecorZones)
            {
                if (!r.P.Build().Blocks.Any(b => b.Kind == BK.Seg && b.Style == "zones" && b.Kids.Any(k => k.Act == "zone:" + z.Id))) continue;
                r.P.Act("zone:" + z.Id);
                var g = r.P.Build().Blocks.First(b => b.Kind == BK.Grid && b.Style == "slots");
                string slot = g.Kids.First(c => !c.Locked).Act.Substring(5);
                r.P.Act("slot:" + slot);
                var pick = r.P.Build().Blocks.First(b => b.Kind == BK.Pick);
                if (pick.Kids.Any(c => c.Locked)) { lockedItem = true; Assert.That(pick.Kids.First(c => c.Locked).Text, Is.Not.Empty); }
                r.P.Act("phclose");
            }
            Assert.IsTrue(lockedItem, "잠긴 장식 조건이 한 번은 보인다");
        }

        [Test]
        public void ShopBuyFlowWithGateAndDeduction()
        {
            var r = UiFixture.Make(4, 140);
            var T = r.Ctx.T;
            r.P.Open(PanelMode.Shop);
            var prods = r.P.Build().Blocks.Where(b => b.Kind == BK.Card && b.Style.StartsWith("prod")).ToList();
            Assert.That(prods.Count, Is.EqualTo(T.Products.Count));
            var p = T.Products.First(x => x.Id == "starter");
            Assert.That(ShopMath.DueOf(T, p, id => false), Is.EqualTo(3300));
            r.P.Act("buy:starter");
            Assert.NotNull(r.P.Gate); Assert.That(r.P.Gate.Type, Is.EqualTo("buy"));
            r.P.Act("gate:ok");
            Assert.IsTrue(r.P.Tick());
            Assert.That(r.Ctx.S.Owned["noads"], Is.Not.Null);
            Assert.That(r.Ctx.S.Owned["tower:gilded"], Is.Not.Null);
            // 이제 광고 제거 상품은 이미 보유
            var noads = r.P.Build().Blocks.First(b => b.Text == T.Products.First(x => x.Id == "noads").Name);
            Assert.That(noads.Style, Does.Contain("own"));
            // 차감 가격: 황금 팩(탑 금빛은 이미 가짐)은 값이 줄어든다
            var gold = T.Products.First(x => x.Id == "goldpack");
            double due = ShopMath.DueOf(T, gold, id => r.Ctx.S.Owned[id] != null);
            Assert.That(due, Is.LessThan(gold.Price));
            // 취소
            var r2 = UiFixture.Make(4, 140); r2.P.Open(PanelMode.Shop);
            r2.P.Act("buy:moonpack"); r2.P.Act("gatex:go"); Assert.IsNull(r2.P.Gate); Assert.IsNull(r2.Ctx.S.Owned["tower:moonlit"]);
            // 실패
            var r3 = UiFixture.Make(4, 140); r3.Pay.Fail = true; r3.P.Open(PanelMode.Shop);
            r3.P.Act("buy:moonpack"); r3.P.Act("gate:ok"); r3.P.Tick();
            Assert.IsNull(r3.Ctx.S.Owned["tower:moonlit"]);
        }

        [Test]
        public void AdFlowGrantsOnlyOnCompletionAndLimit()
        {
            var r = UiFixture.Make(4, 140, night: true);
            r.P.Open(PanelMode.Shop); r.P.Act("adtoggle");
            double gifts0 = r.Ctx.S.Gifts.Values.Sum() + r.Ctx.S.Shiny.Values.Sum();
            r.P.Act("ad:ship");
            Assert.That(r.P.Gate.Type, Is.EqualTo("ad"));
            r.P.Act("gate:" + (r.P.Gate.Right + 100));                 // 오답: 시작 안 함
            Assert.IsFalse(r.Ads.Showing);
            r.P.Act("ad:ship"); r.P.Act("gate:" + r.P.Gate.Right);
            Assert.IsTrue(r.Ads.Showing); r.P.AdShown();
            r.Ads.Finish(false, "closed"); r.P.Tick();               // 닫음: 지급·횟수 소모 없음
            Assert.That(r.Ctx.AdLeft("ship"), Is.EqualTo(3));
            r.P.Act("ad:ship"); r.P.Act("gate:" + r.P.Gate.Right); r.P.AdShown(); r.Ads.Finish(true); r.P.Tick();
            Assert.That(r.Ctx.AdLeft("ship"), Is.EqualTo(2));
            Assert.That(r.Ctx.S.Gifts.Values.Sum() + r.Ctx.S.Shiny.Values.Sum(), Is.EqualTo(gifts0 + 1));
            // 한도
            for (int i = 0; i < 2; i++) { r.P.Act("ad:ship"); r.P.Act("gate:" + r.P.Gate.Right); r.P.AdShown(); r.Ads.Finish(true); r.P.Tick(); }
            Assert.That(r.Ctx.AdLeft("ship"), Is.EqualTo(0));
            var row = r.P.Build().Blocks.SelectMany(b => b.Walk()).First(b => b.Style != null && b.Style.StartsWith("adrow") && b.Icon == "ship");
            Assert.IsTrue(row.Kids.Any(k => k.Disabled), "한도가 다 차면 단추가 막힌다");
            // 광고 제거 보유자는 확인·광고 화면 없이 받는다
            var r2 = UiFixture.Make(4, 140); r2.Ctx.S.Owned["noads"] = true; r2.P.Open(PanelMode.Shop);
            r2.P.Act("ad:ship"); r2.P.Tick(); Assert.IsNull(r2.P.Gate); Assert.That(r2.Ctx.AdLeft("ship"), Is.EqualTo(2));
        }

        [Test]
        public void ShopTabAndAdsHiddenByStageAndFeatures()
        {
            var early = UiFixture.Make(2, 20); early.P.Open(PanelMode.Mail);
            Assert.IsTrue(early.P.Build().Tabs.Any(t => t.Id == "shop"), "상점 기능이 켜져 있으면 탭이 있다");
            var off = UiFixture.Make(2, 20, f: new UiFeatures { Shop = false, Ads = true });
            off.P.Open(PanelMode.Mail);
            Assert.IsFalse(off.P.Build().Tabs.Any(t => t.Id == "shop"), "광고는 3단계부터라 2단계 + 상점 끔이면 탭이 없다");
            off.P.Open(PanelMode.Shop); Assert.That(off.P.Mode, Is.EqualTo(PanelMode.Growth));
        }

        [Test]
        public void SettingsToggleThroughControllerChangesState()
        {
            var r = UiFixture.Make(3, 40);
            r.P.Open(PanelMode.Growth); r.P.Act("gear");
            Assert.That(r.P.Mode, Is.EqualTo(PanelMode.Settings));
            r.P.Act("snd:off"); Assert.IsFalse(r.Ctx.S.Sound);
            r.P.Act("clock:real"); Assert.That(r.Ctx.S.Clock, Is.EqualTo("real"));
            r.P.Act("season:winter"); Assert.That(r.Ctx.S.Season, Is.EqualTo("winter"));
            r.P.Act("view:wide"); Assert.That(r.Host.View, Is.EqualTo("wide")); Assert.That(r.P.ExtraStr("view", "x"), Is.EqualTo("wide"));
            r.P.Act("wake:on"); Assert.IsTrue(r.Host.Wake);
            r.P.Act("photo:go"); Assert.That(r.Host.Log, Does.Contain("photo")); Assert.That(r.P.Mode, Is.EqualTo(PanelMode.None));
            r.P.Open(PanelMode.Mail); r.P.Act("gear"); r.P.Act("setback"); Assert.That(r.P.Mode, Is.EqualTo(PanelMode.Mail));
        }

        [Test]
        public void SaveExportImportRoundTripAndTamperMessage()
        {
            var r = UiFixture.Make(3, 40);
            r.P.Open(PanelMode.Growth); r.P.Act("gear"); r.P.Act("setmore");
            r.P.Act("exp:copy");
            Assert.That(r.Host.LastCopy, Does.StartWith("LHS1."));
            r.P.Act("imp:go");
            Assert.NotNull(r.P.Build().Blocks.SelectMany(b => b.Walk()).FirstOrDefault(b => b.Text != null && b.Text.Contains("한 번 더")), "첫 누름은 확인 문구");
            Assert.IsNull(r.Host.Imported);
            r.P.Act("imp:go"); Assert.NotNull(r.Host.Imported);
            r.P.ImpText = r.Host.LastCopy.Substring(0, r.Host.LastCopy.Length - 3);
            r.P.Act("imp:go");
            Assert.NotNull(r.P.Build().Blocks.SelectMany(b => b.Walk()).FirstOrDefault(b => b.Text != null && b.Text.Contains("중간에 잘렸")), "잘린 코드는 안내");
        }

        [Test]
        public void TipsAppearOnceAtATimeAndStayOff()
        {
            var r = UiFixture.Make(1, 0);
            var tip = new TipEngine(r.Ctx, r.Str); var env = new TipEnv();
            long t = 0; bool hid;
            Assert.IsNull(tip.Update(t, env, out hid));
            r.Ctx.S.Guided = 1;
            var first = tip.Update(t += 3000, env, out hid);
            Assert.That(first.Id, Is.EqualTo("h1"));
            Assert.IsNull(tip.Update(t += 1000, env, out hid), "한 번에 하나");
            tip.Update(t += 4000, env, out hid); Assert.IsTrue(hid);
            Assert.That(r.Ctx.S.Tips["h1"], Is.EqualTo(1));
            env.TitleOpen = true; Assert.IsNull(tip.Update(t += 9000, env, out hid));
            env.TitleOpen = false; r.Ctx.S.Found["shell"] = 1;
            Assert.IsNull(tip.Update(t += 100, env, out hid), "타이틀 뒤 0.8초 대기");
            var second = tip.Update(t += 9000, env, out hid);
            Assert.That(second.Id, Is.EqualTo("h2"));
            tip.TurnOff(); Assert.IsNull(tip.Update(t += 20000, env, out hid)); Assert.IsTrue(r.Ctx.S.TipsOff);
        }

        [Test]
        public void HudLabelsFollowStageGoalAndBuilding()
        {
            var r = UiFixture.Make(3, 80);
            var h = Hud.Compute(r.Ctx, r.Str);
            Assert.That(h.StageText, Is.EqualTo(r.Ctx.T.StageName[3]));
            Assert.That(h.Count, Is.EqualTo(r.Ctx.S.Guided));
            Assert.That(h.Label, Does.StartWith("다음 단계"));
            Assert.That(h.LabelParts.Length, Is.EqualTo(2));
            r.Ctx.S.Guided = r.Ctx.T.Goal[3] + 5; r.Ctx.S.Found.Clear(); r.Ctx.S.Dex.Clear(); r.Ctx.S.Reg.Clear();
            Assert.That(Hud.Compute(r.Ctx, r.Str).Label, Does.StartWith("도감을 더 채워"));
            r.Ctx.S.Build = new BuildState { To = 4, StartAt = r.Ctx.Clock.NowMs - 10000, EndAt = r.Ctx.Clock.NowMs + 50000 };
            var hb = Hud.Compute(r.Ctx, r.Str);
            Assert.That(hb.Label, Does.Contain("공사 중")); Assert.That(hb.StageText, Does.EndWith("공사 중")); Assert.That(hb.Pct, Is.InRange(10f, 20f));
            r.Ctx.Wx = "fog"; Assert.That(Hud.Compute(r.Ctx, r.Str).ForecastKind, Is.EqualTo("fog"));
        }

        [Test]
        public void ToastLifetimeAndJosa()
        {
            var t = new ToastModel(); t.Show("진주을(를) 받았어요", "부제", 1000);
            Assert.That(t.Title, Is.EqualTo("진주를 받았어요"));
            Assert.That(t.Alpha(1000), Is.EqualTo(0f).Within(0.01)); Assert.That(t.Alpha(3000), Is.EqualTo(1f));
            Assert.IsFalse(t.Tick(5900)); Assert.IsTrue(t.Tick(6000)); Assert.IsFalse(t.Visible);
        }

        [TestCase(320, 568), TestCase(360, 640), TestCase(393, 852), TestCase(768, 1024), TestCase(1024, 768), TestCase(300, 480)]
        public void LayoutMatrixCardFitsSafeAreaAndTouchMinimums(int dpW, int dpH)
        {
            foreach (int dpr in new[] { 2, 3 })
            {
                float devW = dpW * dpr, devH = dpH * dpr;
                float s = ScreenLayout.Scale(devW, devH, dpr); ScreenLayout.CanvasSize(devW, devH, out float cw, out float ch, dpr);
                Assert.That(cw * s, Is.EqualTo(devW).Within(0.5)); Assert.That(cw, Is.EqualTo(dpW).Within(0.01), "dp 단위 캔버스");
                var sa = new Insets(0, 24, 0, 16);
                var screen = new LRect(0, 0, cw, ch);
                foreach (var deco in new[] { false, true })
                {
                    var card = ScreenLayout.PanelCard(cw, ch, sa, deco);
                    Assert.IsTrue(card.Inside(new LRect(sa.L, sa.T, cw - sa.L - sa.R, ch - sa.T - sa.B)), $"카드가 안전 영역 안: {dpW}x{dpH} {card.X},{card.Y},{card.W},{card.H} in {cw}x{ch}");
                    Assert.That(card.W, Is.LessThanOrEqualTo(ScreenLayout.CardMaxW + 0.01f));
                    Assert.That(card.H, Is.GreaterThanOrEqualTo(120f));
                }
                float dot = ScreenLayout.DotUnits(s, out int k);
                Assert.That(k, Is.GreaterThanOrEqualTo(2)); Assert.That(dot * s, Is.EqualTo(k).Within(0.001), "도트 한 칸이 기기 픽셀 정수");
                Assert.That(ScreenLayout.GridColumns(cw), Is.EqualTo(cw < 360 ? 3 : 4));
            }
        }

        /// <summary>줄 끝 // 주석을 뗀다(따옴표 안의 //는 남긴다). 코드 문자열 리터럴만 보려는 단순 처리.</summary>
        static string StripComments(string src)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var line in src.Split('\n'))
            {
                int q = 0, cut = -1;
                for (int i = 0; i < line.Length - 1; i++)
                {
                    if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) q++;
                    if (q % 2 == 0 && line[i] == '/' && line[i + 1] == '/') { cut = i; break; }
                }
                sb.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }
            return sb.ToString();
        }

        [Test]
        public void GlyphHarvestCoversDataAndUi()
        {
            var T = M1Fixture.Tables; var str = UiFixture.Strings();
            var set = GlyphHarvest.Collect(T, str);
            Assert.That(set.Count, Is.GreaterThan(500));
            foreach (var g in T.Gifts) foreach (char c in g.Name) Assert.IsTrue(set.Contains(c));
            foreach (char c in "도감 편지함 꾸미기 상점 설정 ★♥") if (c != ' ') Assert.IsTrue(set.Contains(c), "글자 빠짐: " + c);
            Assert.IsTrue(set.Any(c => c >= 0xAC00 && c <= 0xD7A3));
            // 코드(Core·UIModel)의 문자열 리터럴 안 한글도 아틀라스에 있어야 한다(WhenParts 의 '쯤' 이 첫 캡처에서 빠졌던 실측)
            var lost = new SortedSet<char>();
            foreach (var sub in new[] { "Core", "UIModel" })
                foreach (var f in Directory.GetFiles(Path.Combine(M1Fixture.Root, "Assets", "_Lighthouse", "Scripts", sub), "*.cs").Where(x => !x.EndsWith("ContentLint.cs")))      // 검사 도구(개발용 메시지·금지어 정규식)는 화면에 나오지 않는다
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(StripComments(File.ReadAllText(f)), "\"(?:[^\"\\\\\\n]|\\\\.)*\""))
                        foreach (char c in m.Value.Contains("-힣") ? "" : UiText.Display(m.Value)) if (c >= 0xAC00 && c <= 0xD7A3 && !set.Contains(c)) lost.Add(c);
            Assert.That(lost, Is.Empty, "코드 문자열의 한글이 글리프 목록에 없음: " + new string(lost.ToArray()));
            // 폰트 빌드 입력(Tools/build_ui_fonts.py): 쓰는 글자 목록을 매번 같은 내용으로 다시 쓴다
            string dir = Path.Combine(M1Fixture.Root, "Tools", "m3_results"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "ui_glyphs.txt"), new string(set.ToArray()), new System.Text.UTF8Encoding(false));
        }
    }
}
