using System;
using System.IO;
using System.Linq;
using Lighthouse.Platform;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

namespace Lighthouse.Tests
{
    /// <summary>
    /// 출시 구성에서 개발 코드가 빠졌는지(영역 C ④)와 플랫폼 층의 순수 부분 시험. Unity EditMode 전용(어셈블리·클립보드를 본다).
    /// LIGHTHOUSE_DEV 가 켜져 있으면 반대로 "개발 코드가 있다"를 확인한다(양성 대조). 실제 플레이어 빌드 검사는 DevTools.BatchVerifyReleasePlayer.
    /// </summary>
    public sealed class ReleaseAndPlatformTests
    {
        static System.Reflection.Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);

        [Test]
        public void DevCodeIsPresentOnlyWhenSymbolIsDefined()
        {
#if LIGHTHOUSE_DEV
            Assert.IsNotNull(Asm("Lighthouse.Dev"), "개발 구성: 개발 어셈블리가 있다");
            Assert.IsNotNull(Asm("Lighthouse.Core").GetType("Lighthouse.Core.DevClock"));
            Assert.IsNotNull(Asm("Lighthouse.App").GetType("Lighthouse.App.GameApp").GetField("DevTimeMul"));
#else
            Assert.IsNull(Asm("Lighthouse.Dev"), "출시 구성: 개발 어셈블리가 로드되지 않는다");
            Assert.IsNull(Asm("Lighthouse.Core").GetType("Lighthouse.Core.DevClock"), "출시 구성: DevClock 이 없다");
            var app = Asm("Lighthouse.App");
            if (app != null)
            {
                var t = app.GetType("Lighthouse.App.GameApp");
                Assert.IsNull(t.GetField("DevTimeMul"), "출시 구성: 배속 훅이 없다");
                Assert.IsNull(t.GetMethod("DevReboot"), "출시 구성: 개발 재시작 훅이 없다");
                Assert.IsNull(t.GetField("DevClockRef"));
            }
            var player = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(a => a.name).ToList();
            CollectionAssert.DoesNotContain(player, "Lighthouse.Dev", "플레이어 어셈블리 목록에도 없다");
#endif
        }

        [Test]
        public void DevAssemblyDefinitionIsGatedBySymbol()
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("Lighthouse.Dev t:AssemblyDefinitionAsset");
            var path = guids.Select(UnityEditor.AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith("/Dev/Lighthouse.Dev.asmdef"));
            Assert.IsNotNull(path, "Lighthouse.Dev.asmdef 를 찾지 못했다");
            string json = File.ReadAllText(path);
            StringAssert.Contains("\"defineConstraints\": [\"LIGHTHOUSE_DEV\"]", json, "개발 어셈블리는 LIGHTHOUSE_DEV 가 있을 때만 컴파일된다");
            StringAssert.Contains("\"autoReferenced\": false", json);
        }

        // ----- 플랫폼: 파일 열기 후보 찾기(순수 부분) -----
        string dir;
        [SetUp] public void Up() { dir = Path.Combine(Path.GetTempPath(), "lh_plat_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); }
        [TearDown] public void Down() { try { Directory.Delete(dir, true); } catch (Exception) { } }

        [Test]
        public void FindNewestCodePrefersNewestValidFileThenClipboard()
        {
            var t = new PlatformTextTransfer(dir);
            File.WriteAllText(Path.Combine(dir, "old.txt"), "LHS1.AAAA.zz"); File.SetLastWriteTimeUtc(Path.Combine(dir, "old.txt"), DateTime.UtcNow.AddHours(-2));
            File.WriteAllText(Path.Combine(dir, "newer.txt"), "  LHS1.BBBB.yy \n"); File.SetLastWriteTimeUtc(Path.Combine(dir, "newer.txt"), DateTime.UtcNow.AddHours(-1));
            File.WriteAllText(Path.Combine(dir, "newest-but-not-code.txt"), "그냥 메모");
            Assert.AreEqual("LHS1.BBBB.yy", t.FindNewestCode(), "코드가 아닌 가장 새 파일은 건너뛰고 가장 새 코드 파일");
            File.Delete(Path.Combine(dir, "old.txt")); File.Delete(Path.Combine(dir, "newer.txt"));
            string keep = null; try { keep = GUIUtility.systemCopyBuffer; GUIUtility.systemCopyBuffer = "LHS1.CCCC.xx"; } catch (Exception) { Assert.Ignore("이 환경에서는 클립보드를 쓸 수 없다"); }
            if (GUIUtility.systemCopyBuffer != "LHS1.CCCC.xx") { GUIUtility.systemCopyBuffer = keep ?? ""; Assert.Ignore("배치 모드(-nographics)에서는 클립보드가 저장되지 않는다"); }
            try { Assert.AreEqual("LHS1.CCCC.xx", t.FindNewestCode(), "파일이 없으면 클립보드의 코드"); GUIUtility.systemCopyBuffer = "코드 아님"; Assert.IsNull(t.FindNewestCode()); }
            finally { GUIUtility.systemCopyBuffer = keep ?? ""; }
        }

        [Test]
        public void OversizedFileIsIgnored()
        {
            var t = new PlatformTextTransfer(dir);
            File.WriteAllText(Path.Combine(dir, "big.txt"), "LHS1." + new string('A', PlatformTextTransfer.MaxReadBytes + 10) + ".zz");
            string keep = null; try { keep = GUIUtility.systemCopyBuffer; GUIUtility.systemCopyBuffer = ""; } catch (Exception) { Assert.Ignore("클립보드 사용 불가"); }
            try { Assert.IsNull(t.FindNewestCode(), "2 MB 넘는 파일은 읽지 않는다"); } finally { GUIUtility.systemCopyBuffer = keep ?? ""; }
        }

        [Test]
        public void LooksLikeCodeRules()
        {
            Assert.IsTrue(PlatformTextTransfer.LooksLikeCode("LHS1.x.y")); Assert.IsTrue(PlatformTextTransfer.LooksLikeCode("  \nLHS1.x.y"));
            Assert.IsFalse(PlatformTextTransfer.LooksLikeCode("lhs1.x.y")); Assert.IsFalse(PlatformTextTransfer.LooksLikeCode(null)); Assert.IsFalse(PlatformTextTransfer.LooksLikeCode(""));
        }
    }
}
