using System.Collections.Generic;
using System.Linq;
using Lighthouse.EditorTools;
using Lighthouse.UI;
using NUnit.Framework;
using UnityEngine;

namespace Lighthouse.Tests
{
    /// <summary>PixelKit(도트 아이콘 도구) 시험: 모든 아이콘 정의가 모양·팔레트 검사를 통과하고, 만들기·되읽기가 서로 맞아야 한다.</summary>
    public class PixelKitTests
    {
        [Test]
        public void EveryIconDefinitionPassesLint()
        {
            var all = PixelKit.All(); Assert.That(all.Count, Is.GreaterThan(60));
            var bad = all.SelectMany(i => PixelKit.Lint(i).Select(e => i.Id + ": " + e)).ToList();
            Assert.That(bad, Is.Empty, string.Join("\n", bad));
        }

        [Test]
        public void GearOverrideIsSymmetricAndMatchesTheMirrorGenerator()
        {
            var gear = PixelKit.All().First(i => i.Id == "gear"); Assert.That(gear.Source, Is.EqualTo("Unity"));
            Assert.That(PixelKit.Lint(gear, true), Is.Empty, "톱니 윤곽은 좌우·상하 대칭이어야 한다");
            var half = new[] { "......XX", "......XX", "..XXX.XX", "..XXXXXX", "..XXXXXX", "...XXXXX", "XXXXXXXX", "XXXXXXXX" };
            var err = new List<string>(); var line = PixelKit.Mirror("gear", gear.Pal, half, true, true, err);
            Assert.That(err, Is.Empty); Assert.That(line, Is.EqualTo(UiTheme.UnityIcons["gear"]), "mirror 결과가 UiTheme.UnityIcons[gear] 와 같아야 한다(만든 방법이 기록으로 남는다)");
        }

        [Test]
        public void RenderThenFromTextureRoundTrips()
        {
            foreach (var ic in PixelKit.All().Where(i => i.Id == "gear" || i.Id == "mail" || i.Id == "sfxOn"))
            {
                var t = PixelKit.Render(ic, 1, new Color(0, 0, 0, 0));
                try
                {
                    var err = new List<string>(); var warn = new List<string>(); var line = PixelKit.FromTexture(t, ic.Id, ic.Pal, warn, err);
                    Assert.That(err, Is.Empty, ic.Id); Assert.That(warn, Is.Empty, ic.Id);
                    Assert.That(PixelKit.Parse(line, "x").Rows, Is.EqualTo(ic.Rows), ic.Id + " 되읽기 불일치");
                }
                finally { Object.DestroyImmediate(t); }
            }
        }

        [Test]
        public void FromTextureRejectsWrongSizes()
        {
            var t = new Texture2D(10, 10); var err = new List<string>();
            try { Assert.IsNull(PixelKit.FromTexture(t, "x", PixelKit.All()[0].Pal, new List<string>(), err)); Assert.That(err.Count, Is.EqualTo(1)); }
            finally { Object.DestroyImmediate(t); }
        }
    }
}
