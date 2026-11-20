using System.Linq;
using Lighthouse.Core;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>이야기를 다 들은 단골의 안부 편지(마지막 이야기 이후 10·20번째 방문마다). 골든(원본 동등) 경로는 건드리지 않는다.</summary>
    public class RegularGreetingTests
    {
        static GameContext NewCtx()
        {
            var st = GameState.FromJson(M1Fixture.Migrator().Defaults());
            return new GameContext(M1Fixture.Tables, st, new Mulberry32(7), M1Fixture.Clock()) { SeasonForce = "autumn" };
        }

        [Test]
        public void CompletedRegularSendsGreetingEveryTenVisitsAfterTheLastStory()
        {
            var c = NewCtx(); var last = c.T.Bal.VisitAt.Last(); var r = c.T.Regulars[0];
            int got = 0, stories = 0;
            for (int i = 1; i <= last + 25; i++)
            {
                var l = c.VisitRegular(r.Id, true);
                if (l == null) continue;
                if (i <= last) { stories++; Assert.IsNotNull(l.Reg, "이야기 편지는 남겨 둔다(Reg)"); continue; }
                got++;
                Assert.AreEqual(last + 10 * got, i, "마지막 이야기 이후 10번째마다");
                Assert.IsNull(l.Reg, "안부 편지는 일반 편지(오래되면 지워진다)");
                StringAssert.Contains(r.Ship + "호 " + r.Cap + "입니다.", l.Text);
                Assert.IsNotNull(l.Gift);
                Assert.IsFalse(l.Text.Contains("{"), "자리표시가 남지 않는다: " + l.Text);
            }
            Assert.AreEqual(c.T.Bal.VisitAt.Length, stories, "기존 이야기 편지는 그대로");
            Assert.AreEqual(2, got, "last+10, last+20");
        }
    }
}
