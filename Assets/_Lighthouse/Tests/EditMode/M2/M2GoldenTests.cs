using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lighthouse.Render.Verify;
using NUnit.Framework;
using UnityEngine;

namespace Lighthouse.Tests.M2
{
    /// <summary>M2 렌더 골든(Tools/golden/m2/out) 대조 — 순수 C# 대조(M2Checks)를 Unity 에서도 돌린다. 세트별 한 시험.</summary>
    public class M2GoldenTests
    {
        [OneTimeSetUp]
        public void Setup() { M2Checks.Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/golden/m2/out")); }

        static void Expect(List<Result> res)
        {
            var bad = res.Where(r => !r.Ok).Take(8).Select(r => r.ToString()).ToList();
            Assert.That(res.Count, Is.GreaterThan(0));
            Assert.That(bad, Is.Empty, "골든과 다른 항목:\n" + string.Join("\n", bad));
        }

        [Test] public void Islands_Seasons_And_Skins() => Expect(M2Checks.Islands());
        [Test] public void Tower_Skins_And_Lamps() => Expect(M2Checks.Towers());
        [Test] public void Night_Copies_Props() => Expect(M2Checks.Nights());
        [Test] public void Background_And_Clouds() => Expect(M2Checks.Backgrounds());
        [Test] public void Ships() => Expect(M2Checks.Ships());
        [Test] public void Icons() => Expect(M2Checks.Icons());
        [Test] public void Decor() => Expect(M2Checks.Decor());
        [Test] public void Scene_Frames_Cpu() => Expect(M2Checks.Frames());
        [Test] public void Fx_Sky_Sea_Glow_Fit() => Expect(M2Checks.Fx());
    }
}
