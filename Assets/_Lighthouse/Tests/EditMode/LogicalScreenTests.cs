using Lighthouse.Core;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    public class LogicalScreenTests
    {
        // 기대값은 HTML fit()(2234행) 수식을 손으로 계산: k = max(1, floor(min(devW/192, devH/(192*1.41)) + 0.3)), devH/k >= 225 이 될 때까지 k--
        [Test]
        public void Fit_1080x2340_Is5x()
        {
            var ls = new LogicalScreen(1080, 2340);       // min(5.625, 8.64)+0.3 = 5.9 -> 5
            Assert.AreEqual(5, ls.Scale); Assert.AreEqual(216, ls.W); Assert.AreEqual(468, ls.H); Assert.AreEqual(148, ls.OY);
        }

        [Test]
        public void Fit_720x1280_Is4x()
        {
            var ls = new LogicalScreen(720, 1280);       // min(3.75, 4.73)+0.3 = 4.05 -> 4
            Assert.AreEqual(4, ls.Scale); Assert.AreEqual(180, ls.W); Assert.AreEqual(320, ls.H); Assert.AreEqual(0, ls.OY);
        }

        [Test]
        public void Fit_NeverBelow225LogicalHeight()
        {
            for (int devH = 450; devH <= 3200; devH += 7)
                for (int devW = 300; devW <= 1600; devW += 37)
                {
                    var ls = new LogicalScreen(devW, devH);
                    Assert.IsTrue(ls.Scale == 1 || (double)devH / ls.Scale >= 225, $"{devW}x{devH} s={ls.Scale}");
                    Assert.AreEqual(ls.H - 320, ls.OY);
                }
        }
    }
}
