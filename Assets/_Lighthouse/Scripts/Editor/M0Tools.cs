using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.Game;
using Lighthouse.Render;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;

namespace Lighthouse.EditorTools
{
    /// <summary>
    /// M0 실측·스냅샷 (에디터 기준; 실기기는 미확인). 배치 모드:
    /// Unity -batchmode -quit -projectPath . -executeMethod Lighthouse.EditorTools.M0Tools.Measure -m0Out &lt;dir&gt;
    /// (스냅샷은 그래픽이 필요하므로 -nographics 를 쓰지 않는다)
    /// </summary>
    public static class M0Tools
    {
        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        static double Median(List<double> v) { v.Sort(); return v[v.Count / 2]; }

        [MenuItem("Lighthouse/M0/Measure")]
        public static void Measure()
        {
            string dir = Arg("-m0Out", Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/m0")));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.processorType} x{SystemInfo.processorCount}, scripting=Mono(Editor), debug={UnityEngine.Debug.isDebugBuild}");

            // 1) 굽기 시간: 첫 호출(JIT 포함) + 20회 중앙값
            sb.AppendLine("\n[bake 시간 ms: 첫 호출 / 중앙값(20) / 최소 / 최대]");
            double sumIsland = 0, sumTower = 0;
            for (int s = 1; s <= 6; s++)
            {
                var sw = Stopwatch.StartNew();
                new IslandBaker(new IslandEnv { Stage = s }).Bake();
                double first = sw.Elapsed.TotalMilliseconds;
                var ts = new List<double>();
                for (int i = 0; i < 20; i++) { sw.Restart(); new IslandBaker(new IslandEnv { Stage = s }).Bake(); ts.Add(sw.Elapsed.TotalMilliseconds); }
                double med = Median(ts); sumIsland += med;
                sb.AppendLine($"island stage{s}: {first:F2} / {med:F2} / {ts.Min():F2} / {ts.Max():F2}");
            }
            for (int s = 1; s <= 6; s++)
            {
                var P = TowerParams.ForStage(s);
                var sw = Stopwatch.StartNew(); TowerBaker.BakeA(P); TowerBaker.BakeB(P); double first = sw.Elapsed.TotalMilliseconds;
                var ts = new List<double>();
                for (int i = 0; i < 20; i++) { sw.Restart(); TowerBaker.BakeA(P); TowerBaker.BakeB(P); ts.Add(sw.Elapsed.TotalMilliseconds); }
                double med = Median(ts); sumTower += med;
                sb.AppendLine($"tower  stage{s} (A+B): {first:F2} / {med:F2} / {ts.Min():F2} / {ts.Max():F2}");
            }
            sb.AppendLine($"섬 6단계 중앙값 합계 {sumIsland:F2} ms, 탑 A+B 6단계 합계 {sumTower:F2} ms");

            // 2) 관리 메모리 할당(한 번 굽기)과 텍스처 바이트
            sb.AppendLine("\n[메모리: 한 번 굽기의 관리 힙 할당(대략) / 구운 비트맵·텍스처 바이트]");
            for (int s = 1; s <= 6; s++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetTotalMemory(true);
                long alloc0 = Profiler.GetMonoUsedSizeLong();
                var r = new IslandBaker(new IslandEnv { Stage = s }).Bake();
                var P = TowerParams.ForStage(s); var a = TowerBaker.BakeA(P); var b = TowerBaker.BakeB(P);
                long after = GC.GetTotalMemory(false);
                long bytes = (long)r.Island.W * r.Island.H * 4 + r.Pieces.Sum(p => (long)p.Spr.W * p.Spr.H * 4) + (long)a.W * a.H * 4 + (long)b.W * b.H * 4;
                sb.AppendLine($"stage{s}: 힙 +{(after - before) / 1024.0:F1} KB (조각 {r.Pieces.Count}개 포함), 텍스처 합계 {bytes / 1024.0:F1} KB (섬 {r.Island.W}x{r.Island.H}, 탑A {a.W}x{a.H}, 탑B {b.W}x{b.H})");
                GC.KeepAlive(r); GC.KeepAlive(a); GC.KeepAlive(b);
            }

            // 3) 씬 한 장: 대표 해상도에서 RT 에 그리고 PNG 로 저장 + 업로드 시간 + 텍스처 메모리
            sb.AppendLine("\n[씬 M0Preview: 해상도별 논리 크기/배율, 업로드 시간, 텍스처 메모리]");
            EditorSceneManager.OpenScene(ProjectSetup.ScenePath);
            var pv = UnityEngine.Object.FindFirstObjectByType<M0Preview>();
            foreach (var (w, h) in new[] { (1080, 2340), (720, 1280), (1440, 3120) })
            {
                foreach (int stage in new[] { 1, 6 })
                {
                    pv.Stage = stage;
                    long tmem0 = Profiler.GetTotalAllocatedMemoryLong();
                    var st = pv.Build(w, h);
                    pv.WorldCam.Render();
                    SavePng(pv.Rt, Path.Combine(dir, $"scene_s{stage}_{w}x{h}.png"));
                    long tmem1 = Profiler.GetTotalAllocatedMemoryLong();
                    sb.AppendLine($"{w}x{h} stage{stage}: 논리 {st.Screen.W}x{st.Screen.H} 배율 {st.Screen.Scale} OY={st.Screen.OY}; 섬 굽기 {st.IslandMs:F2} ms, 탑 {st.TowerMs:F2} ms, 업로드(스프라이트 {st.Sprites}개) {st.UploadMs:F2} ms; 텍스처 {st.TextureBytes / 1024.0:F1} KB + RT {st.RtBytes / 1024.0:F1} KB; 총 할당 변화 {(tmem1 - tmem0) / 1024.0:F0} KB");
                }
            }
            pv.Clear();
            File.WriteAllText(Path.Combine(dir, "m0_measure.txt"), sb.ToString(), new UTF8Encoding(false));
            UnityEngine.Debug.Log("[Lighthouse] M0 측정 완료\n" + sb);
        }

        static void SavePng(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
