using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Lighthouse.Audio;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>
    /// 연결 점검(통합 단계): 월드·UI 코드가 요청하는 효과음 이름(Say("x")·Host.Sfx("x")·Sfx("x"))이 모두 소리 엔진(SynthEngine.SfxNames)에 있는지.
    /// 요청 경로는 GameLoop.Sfx → AudioBridge.PlayFromWorld → SfxGate → SynthBehaviour.Sfx(이름) 로 이름을 가리지 않으므로, 엔진에 없는 이름만 조용히 사라진다.
    /// </summary>
    public class SfxCoverageTests
    {
        static readonly Regex Req = new Regex(@"\b(?:Say|Sfx|Play)\(\s*""([a-z]+)""\s*\)");

        [Test]
        public void EverySfxNameRequestedByGameCodeExistsInEngine()
        {
            string scripts = Path.Combine(AudioFixture.Root, "Assets", "_Lighthouse", "Scripts");
            var known = new HashSet<string>(SynthEngine.SfxNames);
            var bad = new SortedDictionary<string, string>(); int seen = 0;
            foreach (var sub in new[] { "World", "UIModel", "UI", "Render", "App" })
                foreach (var f in Directory.GetFiles(Path.Combine(scripts, sub), "*.cs", SearchOption.AllDirectories))
                    foreach (Match m in Req.Matches(File.ReadAllText(f, new UTF8Encoding(false))))
                    {
                        seen++; string n = m.Groups[1].Value;
                        if (!known.Contains(n) && !bad.ContainsKey(n)) bad[n] = Path.GetFileName(f);
                    }
            Assert.Greater(seen, 20, "효과음 요청 호출을 찾지 못함(정규식 점검)");
            Assert.That(bad, Is.Empty, "엔진에 없는 효과음 이름: " + string.Join(", ", bad.Select(kv => kv.Key + "(" + kv.Value + ")")));
        }

        [Test]
        public void FireworkIsRequestedByWorldAndKnownToEngine()
        {
            string src = File.ReadAllText(Path.Combine(AudioFixture.Root, "Assets", "_Lighthouse", "Scripts", "World", "GameLoop.Scenery.cs"), new UTF8Encoding(false));
            Assert.That(src, Does.Contain("Say(\"firework\")"));
            Assert.That(SynthEngine.SfxNames, Does.Contain("firework"));
            var gate = new SfxGate();
            Assert.IsTrue(gate.Allow("firework", 10, true, false), "소리 켜짐: 허용");
            Assert.IsFalse(gate.Allow("firework", 11, false, false), "소리 꺼짐: 불꽃놀이도 조용");
        }
    }
}
