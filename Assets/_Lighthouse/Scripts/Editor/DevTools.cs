using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Lighthouse.EditorTools
{
    /// <summary>
    /// 개발 도구 심볼(LIGHTHOUSE_DEV) 관리와 "출시 빌드에서 개발 코드가 완전히 빠졌는지" 확인 (영역 C ④).
    /// 이 프로젝트는 M0 설정(ProjectSetup)이 세 플랫폼에 LIGHTHOUSE_DEV 를 넣어 둔다 — 출시 빌드 전에 빼야 한다:
    ///  - 메뉴 Lighthouse/Dev/Symbol: turn OFF (release) / turn ON (dev)
    ///  - 개발 빌드가 아닌(Development Build 끔) 빌드에 심볼이 남아 있으면 ReleaseGuard 가 빌드를 막는다.
    ///  - 배치: -executeMethod Lighthouse.EditorTools.DevTools.BatchSymbolOff / BatchSymbolOn,
    ///          Lighthouse.EditorTools.DevTools.BatchVerifyReleasePlayer -lhOut &lt;폴더&gt; (심볼을 뺀 윈도우 플레이어를 실제로 빌드해 개발 코드가 없는지 바이너리를 훑는다).
    /// 개발 메뉴(배속·단계 점프·자체 점검·시뮬레이터)는 DevMenu.cs 에 있고 LIGHTHOUSE_DEV 안에서만 컴파일된다.
    /// </summary>
    public static class DevTools
    {
        public const string Sym = "LIGHTHOUSE_DEV";
        static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone };

        [MenuItem("Lighthouse/Dev/Symbol: turn OFF (release configuration)")] public static void Off() { SetAll(false); }
        [MenuItem("Lighthouse/Dev/Symbol: turn ON (dev configuration)")] public static void On() { SetAll(true); }
        [MenuItem("Lighthouse/Dev/Symbol: status")]
        public static void Status() { Debug.Log("LIGHTHOUSE_DEV: " + string.Join(", ", Targets.Select(t => t.TargetName + "=" + Has(t)))); }

        public static bool Has(NamedBuildTarget t) => PlayerSettings.GetScriptingDefineSymbols(t).Split(';').Contains(Sym);

        public static void SetAll(bool on)
        {
            foreach (var t in Targets)
            {
                var list = PlayerSettings.GetScriptingDefineSymbols(t).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                list.RemoveAll(s => s == Sym); if (on) list.Add(Sym);
                PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", list));
            }
            AssetDatabase.SaveAssets();
            Debug.Log("LIGHTHOUSE_DEV 를 " + (on ? "켰다" : "껐다") + " (Android·iOS·Standalone). 다시 컴파일된다.");
        }

        public static void BatchSymbolOff() { SetAll(false); }
        public static void BatchSymbolOn() { SetAll(true); }

        /// <summary>
        /// 심볼을 뺀 상태로 윈도우 플레이어(Mono)를 빌드하고, 결과 폴더의 관리 DLL 들에서 개발 코드의 흔적을 찾는다.
        /// 인자: -lhOut 출력 폴더. 결과는 폴더의 dev_exclusion.txt 에 적고, 흔적이 있으면 종료 코드 3.
        /// 호출 전에 BatchSymbolOff 로 심볼을 빼 둔 상태여야 한다(이 함수가 직접 확인).
        /// </summary>
        public static void BatchVerifyReleasePlayer()
        {
            string outDir = Arg("-lhOut") ?? Path.Combine(Application.dataPath, "..", "Tools", "c_results", "release_player");
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder();
            if (Targets.Any(Has)) { sb.AppendLine("실패: LIGHTHOUSE_DEV 가 아직 켜져 있다"); File.WriteAllText(Path.Combine(outDir, "dev_exclusion.txt"), sb.ToString()); EditorApplication.Exit(2); return; }
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var opts = new BuildPlayerOptions { scenes = scenes, locationPathName = Path.Combine(outDir, "Lighthouse.exe"), target = BuildTarget.StandaloneWindows64, options = BuildOptions.None };
            var rep = BuildPipeline.BuildPlayer(opts);
            sb.AppendLine("빌드 결과: " + rep.summary.result + " (오류 " + rep.summary.totalErrors + ", 경고 " + rep.summary.totalWarnings + "), 개발 빌드=" + ((rep.summary.options & BuildOptions.Development) != 0));
            if (rep.summary.result != BuildResult.Succeeded) { File.WriteAllText(Path.Combine(outDir, "dev_exclusion.txt"), sb.ToString()); EditorApplication.Exit(1); return; }

            string managed = Path.Combine(outDir, "Lighthouse_Data", "Managed");
            string[] needles = { "DevClock", "DevTimeMul", "DevPanel", "DevCommands", "DevChecks", "DevRebuild", "DevReboot", "DevSuppressSave", "DevMeteorSoon", "DevFirework" };
            var hits = new System.Collections.Generic.List<string>();
            var dlls = Directory.GetFiles(managed, "Lighthouse*.dll");
            sb.AppendLine("훑은 DLL: " + string.Join(", ", dlls.Select(Path.GetFileName)));
            if (dlls.Any(d => Path.GetFileName(d) == "Lighthouse.Dev.dll")) hits.Add("Lighthouse.Dev.dll 이 들어 있다");
            foreach (var d in dlls)
            {
                string text = Encoding.ASCII.GetString(File.ReadAllBytes(d));
                foreach (var n in needles) if (text.Contains(n)) hits.Add(Path.GetFileName(d) + " 에 '" + n + "'");
            }
            sb.AppendLine(hits.Count == 0 ? "개발 코드 흔적 0 — 완전히 제외됨" : "흔적 " + hits.Count + "건:\n  " + string.Join("\n  ", hits));
            File.WriteAllText(Path.Combine(outDir, "dev_exclusion.txt"), sb.ToString());
            EditorApplication.Exit(hits.Count == 0 ? 0 : 3);
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }
    }

    /// <summary>개발 빌드(Development Build)가 아닌데 LIGHTHOUSE_DEV 가 켜진 채 빌드하려 하면 막는다(출시 빌드에 개발 도구·치트가 들어가는 사고 방지).</summary>
    public sealed class ReleaseGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            bool devBuild = (report.summary.options & BuildOptions.Development) != 0;
            var target = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(report.summary.platform));
            if (!devBuild && DevTools.Has(target))
                throw new BuildFailedException("LIGHTHOUSE_DEV 가 켜진 채로는 출시(개발 빌드 아님) 빌드를 만들 수 없어요. 메뉴 Lighthouse/Dev/Symbol: turn OFF 를 먼저 하세요.");
        }
    }
}
