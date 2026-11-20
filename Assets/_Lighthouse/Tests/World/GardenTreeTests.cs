using Lighthouse.Core;
using Lighthouse.Render;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>정원 Lv8 나무(U22): 원본에서는 효과 레벨이 6에서 멈춰 닿지 않던 자리를 실제 레벨로 살린다. 기본(treeLv 0)은 원본 그대로.</summary>
    public class GardenTreeTests
    {
        static int Pieces(int treeLv) => new IslandBaker(Palettes.IslandEnvFor("summer", 6, gardenLv: 6, treeLv: treeLv)).Bake().Pieces.Count;

        [Test]
        public void EighthGardenLevelAddsOneTreePieceAndDefaultIsUnchanged()
        {
            int baseline = Pieces(0);
            Assert.AreEqual(baseline, Pieces(7), "Lv7 까지는 그대로");
            Assert.AreEqual(baseline + 1, Pieces(8), "Lv8 에서 나무 한 그루");
            Assert.AreEqual(baseline + 1, Pieces(30), "더 올라도 그 한 그루");
        }

        [Test]
        public void CrossingLevelEightAfterTheCapRequestsAnIslandRebuild()
        {
            var st = GameState.FromJson(M1Fixture.Migrator().Defaults());
            var c = new GameContext(M1Fixture.Tables, st, new Mulberry32(2), M1Fixture.Clock()) { Stage = 6 };
            st.Growth["garden"] = Progress.LvAt(c.T, 7);
            Assert.AreEqual(7, c.LvlOf("garden")); c.NeedRebuild = false;
            c.BumpGrowth("garden", Progress.LvAt(c.T, 8) - st.Growth["garden"]);
            Assert.GreaterOrEqual(c.LvlOf("garden"), 8);
            Assert.IsTrue(c.NeedRebuild, "상한(6)을 넘은 뒤에도 Lv8 에 닿으면 섬을 다시 굽는다");
            c.NeedRebuild = false; c.BumpGrowth("garden", 1);
            Assert.IsFalse(c.NeedRebuild, "그 밖의 상한 뒤 레벨 업에는 다시 굽지 않는다");
        }
    }
}
