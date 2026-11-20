using System.Linq;
using Lighthouse.Core;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>꾸미기 조합(U21): 서로 다른 장식 4종 +1%p, 8종 +3%p 빛나는 선물 확률. 같은 장식을 여러 자리에 놓아도 1종, 닫힌 자리의 장식은 세지 않는다.</summary>
    public class DecorComboTests
    {
        static GameContext NewCtx()
        {
            var st = GameState.FromJson(M1Fixture.Migrator().Defaults());
            return new GameContext(M1Fixture.Tables, st, new Mulberry32(5), M1Fixture.Clock()) { Stage = 6 };
        }

        static void Place(GameContext c, int slots, int kinds)
        {
            c.S.Isle.Put.Clear();
            var open = c.T.DecorSlots.Where(s => DecorRules.SlotOpen(s, c.Stage, c.LvlOf("garden"))).Take(slots).ToList();
            var items = c.T.DecorItems.Take(kinds).ToList();
            for (int i = 0; i < open.Count; i++) c.S.Isle.Put[open[i].Id] = items[i % items.Count].Id;
        }

        [Test]
        public void ThresholdsAndSameItemCountsOnce()
        {
            var c = NewCtx();
            Assert.AreEqual(0, c.DecorShinyBonus(), 1e-12);
            Place(c, 10, 3); Assert.AreEqual(3, c.DecorDistinct()); Assert.AreEqual(0, c.DecorShinyBonus(), 1e-12);
            Place(c, 10, 4); Assert.AreEqual(4, c.DecorDistinct()); Assert.AreEqual(0.01, c.DecorShinyBonus(), 1e-12);
            Place(c, 10, 7); Assert.AreEqual(0.01, c.DecorShinyBonus(), 1e-12);
            Place(c, 10, 8); Assert.AreEqual(8, c.DecorDistinct()); Assert.AreEqual(0.03, c.DecorShinyBonus(), 1e-12);
            Place(c, 1, 8); Assert.AreEqual(1, c.DecorDistinct(), "자리가 하나뿐이면 1종");
        }

        [Test]
        public void BonusFlowsIntoShinyChanceAndIgnoresClosedSlots()
        {
            var c = NewCtx(); double baseP = c.ShinyChance();
            Place(c, 10, 8); Assert.AreEqual(baseP + 0.03, c.ShinyChance(), 1e-9);
            var closed = c.T.DecorSlots.FirstOrDefault(s => !DecorRules.SlotOpen(s, c.Stage, c.LvlOf("garden")));
            if (closed != null) { c.S.Isle.Put.Clear(); c.S.Isle.Put[closed.Id] = c.T.DecorItems[0].Id; Assert.AreEqual(0, c.DecorDistinct(), "닫힌 자리는 세지 않는다"); }
        }
    }
}
