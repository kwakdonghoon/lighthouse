using System.Linq;
using Lighthouse.Core;
using Lighthouse.UI.Model;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>콘텐츠 보완 1차(unity-content-parity P1·P2): 탭·메뉴 배지 글자, 토스트 글 폭 한도, 새 문구 키. Unity 없이 dotnet test 로 돈다.</summary>
    public class ContentFix1Tests
    {
        [TestCase(0, ""), TestCase(1, "1"), TestCase(7, "7"), TestCase(99, "99"), TestCase(100, "99+"), TestCase(250, "99+")]
        public void BadgeCountMatchesOriginalUpdateBadges(int n, string want) => Assert.AreEqual(want, Badges.Count(n));

        [Test]
        public void MenuBadgeShowsUnreadCountElseNewMarkElseNothing()
        {
            Assert.AreEqual("3", Badges.Menu(3, true), "안 읽은 편지가 있으면 그 수");
            Assert.AreEqual("99+", Badges.Menu(100, false));
            Assert.AreEqual("N", Badges.Menu(0, true), "편지가 없으면 도감 새 항목 N");
            Assert.AreEqual("", Badges.Menu(0, false));
        }

        [Test]
        public void PanelTabsCarryBadgeText()
        {
            var r = UiFixture.Make(3, 60, 1); var c = r.Ctx;
            c.S.Letters.Clear(); c.S.DexSeen = c.Discovered();
            r.P.Open(PanelMode.Growth); var m = r.P.Build();
            Assert.IsFalse(m.Tabs.First(t => t.Id == "mail").Badge); Assert.AreEqual("", m.Tabs.First(t => t.Id == "mail").BadgeText);
            Assert.IsFalse(m.Tabs.First(t => t.Id == "dex").Badge);
            for (int i = 0; i < 4; i++) c.S.Letters.Add(new Letter { Id = 9000 + i, Read = false, Text = "t" });
            m = r.P.Build(); Assert.AreEqual("4", m.Tabs.First(t => t.Id == "mail").BadgeText); Assert.IsTrue(m.Tabs.First(t => t.Id == "mail").Badge);
            for (int i = 4; i < 150; i++) c.S.Letters.Add(new Letter { Id = 9000 + i, Read = false, Text = "t" });
            m = r.P.Build(); Assert.AreEqual("99+", m.Tabs.First(t => t.Id == "mail").BadgeText, "100통 이상은 한도 숫자를 드러내지 않는다");
            c.S.DexSeen = Mathf0(c.Discovered() - 1);
            m = r.P.Build(); Assert.AreEqual("N", m.Tabs.First(t => t.Id == "dex").BadgeText); Assert.IsTrue(m.Tabs.First(t => t.Id == "dex").Badge);
        }
        static int Mathf0(int v) => v < 0 ? 0 : v;

        [TestCase(320f, 2.25f), TestCase(360f, 2.25f), TestCase(393f, 3f), TestCase(768f, 4f)]
        public void ToastTextMaxWidthStaysInsideScreen(float canvasW, float dot)
        {
            float w = ScreenLayout.ToastTextMaxW(canvasW, dot);
            Assert.Greater(w, 80f - 0.01f);
            Assert.LessOrEqual(w + 2f * 8f * dot, canvasW - 16f + 0.01f, "상자 전체(글 + 안쪽 여백)가 화면 폭 - 16 안에 들어야 한다 (원본 max-width calc(100vw - 16px))");
        }

        [Test]
        public void NewToastKeysExistAndKeepOriginalWording()
        {
            var s = UiFixture.Strings();
            Assert.AreEqual("새 하늘 기록, 무지개", s.F("toast.firstSky", "무지개"));
            Assert.AreEqual("드문 하늘 기록, 오로라", s.F("toast.firstSkyRare", "오로라"));
            Assert.AreEqual("새 바다 친구, 돌고래", s.F("toast.firstSea", "돌고래"));
            Assert.AreEqual("드문 바다 친구, 고래", s.F("toast.firstSeaRare", "고래"));
            Assert.AreEqual("처음 만난 배, 작은 어선", s.F("toast.firstShip", "작은 어선"));
            Assert.AreEqual("처음 받은 선물, 조개껍데기", s.F("toast.firstGift", "조개껍데기"));
            Assert.AreEqual("도감에 남겨 뒀어요", s.Get("toast.firstSub"));
            Assert.AreEqual("빛나는 선물을 찾았어요", s.Get("world.shiny"));
            Assert.AreEqual("반짝이는 조개껍데기가 왔어요", GameContext.FixJosa(s.F("world.shinySub", "조개껍데기")));
            Assert.AreEqual("반짝이는 별조각이 왔어요", GameContext.FixJosa(s.F("world.shinySub", "별조각")));
            Assert.AreEqual("3통이 와 있어요", s.F("world.letterToastMany", 3));
            Assert.That(s.Missing, Is.Empty);
        }
    }
}
