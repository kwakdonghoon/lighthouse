using System;
using System.Linq;
using Lighthouse.Core;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>희귀 손님(U20): 조건에 맞을 때만 오고, 첫 만남은 남겨 두는 편지 + 빛나는 선물, 다시 만나면 평범한 선물. 저장 구조는 늘리지 않는다(편지 keep 이 기록).</summary>
    public class RareGuestTests
    {
        sealed class Fixed : Random { readonly double v; public Fixed(double v) { this.v = v; } protected override double Sample() => v; }

        static GameContext NewCtx(bool night, string wx, string season, int stage = 6)
        {
            var st = GameState.FromJson(M1Fixture.Migrator().Defaults());
            return new GameContext(M1Fixture.Tables, st, new Mulberry32(3), M1Fixture.Clock()) { SeasonForce = season, Night = night, Wx = wx, Stage = stage };
        }

        [Test]
        public void ConditionsGateWhoCanAppear()
        {
            var day = NewCtx(false, "clear", "summer");
            Assert.IsNull(day.PickRareGuest(new Fixed(0)), "맑은 여름 낮에는 아무도 오지 않는다");
            Assert.AreEqual("moonpeddler", NewCtx(true, "clear", "summer").PickRareGuest(new Fixed(0))?.Id);
            Assert.AreEqual("rainpost", NewCtx(false, "rain", "summer").PickRareGuest(new Fixed(0))?.Id);
            Assert.AreEqual("fogpilgrim", NewCtx(false, "fog", "summer").PickRareGuest(new Fixed(0))?.Id);
            var winterNight = NewCtx(true, "clear", "winter"); var ids = winterNight.T.RareGuests.Where(r => winterNight.RareCondOk(r.Cond)).Select(r => r.Id).ToList();
            CollectionAssert.AreEquivalent(new[] { "moonpeddler", "snowlantern" }, ids);
            Assert.IsNull(NewCtx(true, "clear", "summer", 1).PickRareGuest(new Fixed(0)), "1단계에는 아직 오지 않는다");
        }

        [Test]
        public void ChanceIsLowAndRepeatIsLower()
        {
            var c = NewCtx(true, "clear", "summer");
            Assert.IsNull(c.PickRareGuest(new Fixed(0.99)), "확률 밖이면 오지 않는다");
            c.MeetRare("moonpeddler", out _);
            Assert.IsNotNull(c.PickRareGuest(new Fixed(GameContext.RareRepeatChance - 0.001)));
            Assert.IsNull(c.PickRareGuest(new Fixed(GameContext.RareRepeatChance + 0.001)), "이미 만난 손님은 더 드물다");
        }

        [Test]
        public void FirstMeetingKeepsLetterAndShinyGiftThenOnlyPlainGifts()
        {
            var c = NewCtx(true, "clear", "summer");
            Assert.IsFalse(c.RareMet("moonpeddler"));
            var l = c.MeetRare("moonpeddler", out string g);
            Assert.IsNotNull(l); Assert.AreEqual("moonshard", g); Assert.IsTrue(c.RareMet("moonpeddler"));
            Assert.IsTrue(l.KeepTruthy, "첫 만남 편지는 남겨 둔다"); Assert.AreEqual(true, l.Shiny); Assert.IsTrue(c.S.Shiny.ContainsKey("moonshard"));
            int letters = c.S.Letters.Count;
            Assert.IsNull(c.MeetRare("moonpeddler", out string g2)); Assert.IsNotNull(g2);
            Assert.AreEqual(letters, c.S.Letters.Count, "다시 만나도 편지는 늘지 않는다");
            Assert.IsFalse(c.RareMet("rainpost"));
        }

        [Test]
        public void RecordSurvivesSaveRoundTrip()
        {
            var c = NewCtx(true, "clear", "summer");
            c.MeetRare("fogpilgrim", out _);
            var back = GameState.FromJson(c.S.ToJson());
            var c2 = new GameContext(M1Fixture.Tables, back, new Mulberry32(3), M1Fixture.Clock());
            Assert.IsTrue(c2.RareMet("fogpilgrim")); Assert.IsFalse(c2.RareMet("rainpost"));
        }
    }
}
