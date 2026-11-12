using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.UI.Model;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>A2(UI·입력·화면 연출 잔여) 순수 로직 시험: 말풍선 자리, 타이틀·안내 시간표, 꾸미기 초점·스크롤 요청, 낮은 화면 안내 접기, 섬 탭 고르기.</summary>
    public class A2Tests
    {
        static List<UiRectF> Uis(params UiRectF[] r) => r.ToList();

        // ---------- 말풍선 회피 (원본 frame() 5306~5324행) ----------
        [Test]
        public void BubbleClampsToScreenEdgesAndKeepsTailOnTarget()
        {
            var s = BubbleLayout.Resolve(x: 5, baseY: 300, w: 120, h: 40, canvasW: 360, canvasH: 640, uis: Uis());
            Assert.That(s.Cx, Is.EqualTo(60 + 8), "왼쪽 가장자리에서 안쪽으로 밀림");
            Assert.That(s.TailOffset, Is.EqualTo(-60 + 10), "꼬리는 풍선 안에서 가장 왼쪽(대상을 가리킴)");
            Assert.IsFalse(s.Hidden);
            s = BubbleLayout.Resolve(500, 300, 120, 40, 360, 640, Uis());
            Assert.That(s.Cx, Is.EqualTo(360 - 60 - 8)); Assert.That(s.TailOffset, Is.EqualTo(60 - 10));
        }

        [Test]
        public void BubbleMovesBelowUpperUiAndAboveLowerUi()
        {
            var hud = new UiRectF(80, 10, 280, 70);                                   // 위쪽 표시 줄
            var s = BubbleLayout.Resolve(180, 80, 120, 40, 360, 640, Uis(hud));       // 풍선 위가 40~80 → 표시 줄(10~70)과 겹침, 표시 줄이 위쪽 → 그 아래로
            Assert.That(s.Bottom, Is.EqualTo(70 + 6 + 40), "표시 줄 아래(+6 틈)로 내려간다");
            Assert.IsFalse(s.Hidden);
            var tip = new UiRectF(40, 400, 320, 480);                                 // 아래쪽 안내 카드
            s = BubbleLayout.Resolve(180, 470, 120, 40, 360, 640, Uis(tip));          // 카드가 풍선보다 아래쪽 중심? (카드 중심 440 < 풍선 중심 450 → 카드가 위쪽) → 그 아래로
            Assert.That(s.Bottom, Is.EqualTo(480 + 6 + 40));
            s = BubbleLayout.Resolve(180, 430, 120, 40, 360, 640, Uis(tip));          // 풍선 중심 410, 카드 중심 440 → 카드가 아래쪽 → 카드 위로
            Assert.That(s.Bottom, Is.EqualTo(400 - 6));
        }

        [Test]
        public void BubbleHidesWhenNoRoomBetweenUi()
        {
            // 위쪽 줄과 아래쪽 카드 사이가 풍선 높이보다 좁다 → 비킬 자리가 없어 감춘다
            var a = new UiRectF(0, 0, 360, 100); var b = new UiRectF(0, 120, 360, 640);
            var s = BubbleLayout.Resolve(180, 110, 120, 40, 360, 640, Uis(a, b));
            Assert.IsTrue(s.Hidden);
        }

        [Test]
        public void BubbleStaysInsideScreenVertically()
        {
            var s = BubbleLayout.Resolve(180, -50, 120, 40, 360, 640, Uis());
            Assert.That(s.Bottom, Is.EqualTo(44), "위로 넘치면 높이+4 로 고정");
            s = BubbleLayout.Resolve(180, 900, 120, 40, 360, 640, Uis());
            Assert.That(s.Bottom, Is.EqualTo(636));
        }

        // ---------- 시간표 (원본 5603·5604행, CSS 513~517·523~526) ----------
        [Test]
        public void TitleAndHintTiming()
        {
            Assert.That(IntroTiming.AutoCloseSec(true, true), Is.EqualTo(15.6f)); Assert.That(IntroTiming.AutoCloseSec(false, true), Is.EqualTo(6.4f)); Assert.That(IntroTiming.AutoCloseSec(false, false), Is.EqualTo(3.6f));
            Assert.That(IntroTiming.Steps(9.5f, IntroTiming.TaglineAt), Is.EqualTo(0f)); Assert.That(IntroTiming.Steps(9.6f + 0.21f, IntroTiming.TaglineAt), Is.EqualTo(0.5f));
            Assert.That(IntroTiming.Steps(10.1f, IntroTiming.TaglineAt), Is.EqualTo(1f)); Assert.That(IntroTiming.Steps(10.8f + 0.1f, IntroTiming.DayAt), Is.EqualTo(0.25f));
            Assert.That(IntroTiming.HintAlpha(1.0f), Is.EqualTo(0f)); Assert.That(IntroTiming.HintAlpha(1.2f + 1.5f), Is.EqualTo(1f).Within(1e-4)); Assert.That(IntroTiming.HintAlpha(1.2f + 7.8f), Is.EqualTo(1f).Within(1e-4));
            Assert.That(IntroTiming.HintAlpha(11.2f), Is.EqualTo(0f)); Assert.That(IntroTiming.HintAlpha(1.2f + 0.75f), Is.EqualTo(0.5f).Within(1e-4));
        }

        // ---------- 꾸미기: 섬 탭 고르기 · 초점·스크롤 요청 · 낮은 화면 ----------
        static UiFixture.Rig DecoRig(float screenH = 800f)
        {
            var r = UiFixture.Make(6, 260, 6); r.P.ScreenH = screenH; r.P.Open(PanelMode.Deco); r.P.Act("zone:front"); r.P.TakeReq(); return r;
        }

        [Test]
        public void IslandTapPicksNearestOpenSlotAndWarnsWhenTwoAreClose()
        {
            var r = DecoRig(); var ids = r.P.DecoOpenSlotIds(); Assert.That(ids.Count, Is.GreaterThanOrEqualTo(2));
            Assert.IsFalse(r.P.DecoTapPick(new List<(double, string)>()), "22 안에 아무것도 없으면 아무 일도 없다");
            Assert.IsTrue(r.P.DecoTapPick(new List<(double, string)> { (10, ids[0]) })); Assert.That(r.P.DecoSel, Is.EqualTo(ids[0]));
            var req = r.P.TakeReq(); Assert.That(req.Scroll, Is.EqualTo("ph"), "고르면 고르기 시트가 보이게 스크롤");
            Assert.IsTrue(r.P.DecoTapPick(new List<(double, string)> { (10, ids[0]) })); Assert.IsNull(r.P.DecoSel, "같은 자리를 다시 누르면 풀린다");
            Assert.IsNull(r.P.TakeReq());
            // 두 자리가 8 이내 차이로 붙어 있으면 고르지 않고 "자리가 가까워요" (N32)
            Assert.IsTrue(r.P.DecoTapPick(new List<(double, string)> { (12, ids[1]), (7, ids[0]) }));
            Assert.IsNull(r.P.DecoSel);
            string near = r.Str.Get("deco.near");
            Assert.That(r.P.Build().Blocks.Any(b => b.Kind == BK.Note && b.Text == near), "안내 문구가 화면에 있다: " + near);
            Assert.That(near, Does.Contain("자리가 가까워요"));
            // 8 이상 벌어지면 가까운 쪽을 고른다
            Assert.IsTrue(r.P.DecoTapPick(new List<(double, string)> { (21, ids[1]), (5, ids[0]) })); Assert.That(r.P.DecoSel, Is.EqualTo(ids[0]));
        }

        [Test]
        public void DecoActionsRequestScrollAndFocusLikeHtmlDecoReq()
        {
            var r = DecoRig(); var P = r.P; var ids = P.DecoOpenSlotIds();
            var T = r.Ctx.T; string other = T.DecorZones.First(z => z.Id != "front" && DecorRules.ZoneOpen(T, z.Id, 6, r.Ctx.LvlOf("garden")).Count > 0).Id;
            P.Act("zone:" + other); var q = P.TakeReq(); Assert.That(q.Scroll, Is.EqualTo("zones")); Assert.That(q.Focus, Is.EqualTo("zone.on"));
            P.Act("zone:front"); P.TakeReq();
            P.Act("slot:" + ids[0]); q = P.TakeReq(); Assert.That(q.Scroll, Is.EqualTo("ph")); Assert.That(q.Focus, Is.EqualTo("slot:" + ids[0]));
            var put = P.Build().Blocks.First(b => b.Kind == BK.Pick).Kids.First(c => c.Act != null && c.Act.StartsWith("put:"));
            P.Act(put.Act); q = P.TakeReq(); Assert.That(q.Focus == "ph" || q.Focus == "slot:" + ids[0], "다음 빈 자리가 있으면 시트, 없으면 그 칸: " + q.Focus);
            P.Act("undo"); q = P.TakeReq(); Assert.That(q.Near, Is.EqualTo(ids[0])); Assert.That(q.Focus, Is.EqualTo("zone.on"), "되돌릴 것이 더 없으면 구역 칩으로");
            P.Act("redo"); q = P.TakeReq(); Assert.That(q.Near, Is.EqualTo(ids[0])); Assert.That(q.Focus, Is.EqualTo("zone.on"));
            P.Act("phclose"); P.TakeReq(); P.Act("slot:" + ids[1]); P.TakeReq(); P.Act("phclose"); q = P.TakeReq(); Assert.That(q.Focus, Is.EqualTo("slot:" + ids[1]));
            P.Act("phclose"); Assert.IsNull(P.TakeReq(), "고른 자리가 없으면 요청 없음");
            P.Act("peek:on"); q = P.TakeReq(); Assert.That(q.Focus, Is.EqualTo("peek:off")); P.Act("peek:off"); q = P.TakeReq(); Assert.That(q.Focus, Is.EqualTo("peek:on"));
            P.Act("auto"); q = P.TakeReq(); Assert.That(q == null || q.Focus == "auto");
            P.Act("clearall:ask"); q = P.TakeReq(); if (q != null) Assert.That(q.Focus, Is.EqualTo("clearall:no"));
            P.Act("clearall:no"); q = P.TakeReq(); Assert.That(q.Focus, Is.EqualTo("clearall:ask"));
            P.Act("clearall:ask"); P.TakeReq(); P.Act("clearall:yes"); q = P.TakeReq(); Assert.That(q.Focus, Is.EqualTo("zone.on"));
        }

        [Test]
        public void RefocusKeysAndSettingsFocusTargets()
        {
            var r = UiFixture.Make(4, 140, 3); var P = r.P; P.Open(PanelMode.Growth);
            P.Act("rolebox"); Assert.That(P.TakeReq().Focus, Is.EqualTo("rolebox")); P.Act("trk:lens"); Assert.That(P.TakeReq().Focus, Is.EqualTo("trk:lens"));
            P.Act("gear"); Assert.That(P.TakeReq().Focus, Is.EqualTo("setback"), "설정에 들어오면 돌아가기 단추에 초점");
            P.Act("setmore"); Assert.That(P.TakeReq().Focus, Is.EqualTo("setmore")); P.Act("tips"); Assert.That(P.TakeReq().Focus, Is.EqualTo("tips"));
            P.Act("setback"); Assert.That(P.TakeReq().Focus, Is.EqualTo("gear"), "돌아가면 머리의 톱니에 초점");
            P.Open(PanelMode.Shop); P.Act("adtoggle"); Assert.That(P.TakeReq().Focus, Is.EqualTo("adtoggle"));
        }

        [Test]
        public void LowScreenFoldsDecoIntroAfterFirstZoneOrSlot()
        {
            // 낮은 화면(캔버스 높이 ≤ 700): 안내 한 줄은 한 번 본 뒤 접힌다(원본 3846·3882행). 높은 화면은 늘 보인다.
            string intro = UiFixture.Strings().Get("deco.intro");
            bool Has(UiFixture.Rig r) => r.P.Build().Blocks.Any(b => b.Kind == BK.Note && b.Text == intro);
            var low = UiFixture.Make(6, 260, 6); low.P.ScreenH = 640; low.P.Open(PanelMode.Deco);
            Assert.IsTrue(Has(low), "처음에는 보인다"); Assert.That(low.Ctx.S.Isle.TipSeen, Is.EqualTo(0));
            low.P.Act("zone:front"); Assert.That(low.Ctx.S.Isle.TipSeen, Is.EqualTo(1)); Assert.IsFalse(Has(low), "구역을 고른 뒤에는 접힌다");
            var tall = UiFixture.Make(6, 260, 6); tall.P.ScreenH = 852; tall.P.Open(PanelMode.Deco); tall.P.Act("zone:front");
            Assert.That(tall.Ctx.S.Isle.TipSeen, Is.EqualTo(0)); Assert.IsTrue(Has(tall));
        }

        // ---------- 지역(바다) 전환 (원본 switchRegion 1903행) ----------
        [Test]
        public void RegionFadeTimingMatchesHtml()
        {
            var t = new RegionFadeTiming(false);
            Assert.That(t.CommitAt, Is.EqualTo(0.95f).Within(1e-5)); Assert.That(t.FadeOutAt, Is.EqualTo(1.45f).Within(1e-5)); Assert.That(t.EndAt, Is.EqualTo(2.2f).Within(1e-5));
            Assert.That(t.Alpha(0f), Is.EqualTo(0f)); Assert.That(t.Alpha(0.7f), Is.EqualTo(1f).Within(1e-5)); Assert.That(t.Alpha(1.2f), Is.EqualTo(1f)); Assert.That(t.Alpha(1.45f + 0.7f), Is.EqualTo(0f).Within(1e-5));
            var c = new RegionFadeTiming(true);
            Assert.That(c.CommitAt, Is.EqualTo(0.25f).Within(1e-5)); Assert.That(c.EndAt, Is.EqualTo(0.8f).Within(1e-5), "움직임 줄이기는 덮이고 걷히는 시간이 0");
            Assert.That(c.Alpha(0.1f), Is.EqualTo(1f)); Assert.That(c.Alpha(0.76f), Is.EqualTo(0f));
        }

        [Test]
        public void SettingsSeaChoiceSwitchesRegionAndArrives()
        {
            var r = UiFixture.Make(4, 140, 3); var T = r.Ctx.T;
            var far = new RegionDef { Id = "far", Name = "먼바다", Src = "free" }; T.Regions.Add(far); T.RegionById["far"] = far;
            try
            {
                r.P.Open(PanelMode.Growth); r.P.Act("gear");
                var seg = r.P.Build().Blocks.SelectMany(b => b.Walk()).Where(b => b.Act != null && b.Act.StartsWith("region:")).Select(b => b.Act).ToList();
                CollectionAssert.AreEquivalent(new[] { "region:home", "region:far" }, seg, "바다가 둘 이상이면 설정에 바다 칸이 보인다");
                r.P.Act("region:home"); Assert.That(r.Ctx.CurRegion(), Is.EqualTo("home"), "지금 바다를 누르면 아무 일도 없다");
                r.P.Act("region:far");
                Assert.That(r.Ctx.S.Region, Is.EqualTo("far")); Assert.That(r.Host.Log, Does.Contain("dirty"), "바다가 바뀌면 월드를 다시 굽는다");
                Assert.That(r.Host.Log.Any(l => l.StartsWith("toast:먼바다에 도착했어요")), "도착 토스트: " + string.Join("|", r.Host.Log));
                Assert.That(r.P.Mode, Is.EqualTo(PanelMode.None), "바꾸는 순간 팝업은 닫힌다");
                Assert.IsFalse(r.P.RegionBusy);
            }
            finally { T.Regions.Remove(far); T.RegionById.Remove("far"); }
        }

        [Test]
        public void TapAimUsesTwentyTwoCssPixelsAndSlotAnchorThreeAbove()
        {
            var slots = new List<(string id, double sx, double sy)> { ("a", 100, 200), ("b", 110, 200), ("c", 300, 300) };
            // 자리 a 의 기준점은 (100, 197). 설계 1칸 = 2 화면 단위
            var c = DecoTapAim.Candidates(100, 197, slots, 2.0);
            Assert.That(c.Select(x => x.id).ToArray(), Is.EqualTo(new[] { "a", "b" }), "c 는 멀어서 빠진다");
            Assert.That(c[0].d, Is.EqualTo(0).Within(1e-9)); Assert.That(c[1].d, Is.EqualTo(20).Within(1e-9), "b 까지 10칸 x 2 = 20 <= 22");
            Assert.That(DecoTapAim.Candidates(100, 197 + 11.01, slots, 2.0).Count(x => x.id == "a"), Is.EqualTo(0), "11.01칸 x 2 = 22.02 > 22");
            Assert.That(DecoTapAim.Candidates(100, 197 + 11.0, slots, 2.0).Count(x => x.id == "a"), Is.EqualTo(1), "정확히 22 는 안쪽");
            Assert.That(DecoTapAim.Candidates(100, 200, slots, 2.0)[0].d, Is.EqualTo(6).Within(1e-9), "기준점은 자리보다 3 위(sy-3)라 자리 한가운데를 눌러도 3칸 x 2");
            // 화면 배율이 달라도 '화면 단위' 22 로 같다: 1칸 = 1 단위면 22칸까지
            Assert.That(DecoTapAim.Candidates(100 + 21.9, 197, slots, 1.0).Any(x => x.id == "a"), Is.True);
        }
    }
}
