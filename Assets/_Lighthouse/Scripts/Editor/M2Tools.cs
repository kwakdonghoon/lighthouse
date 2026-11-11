using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.Core;
using Lighthouse.Game;
using Lighthouse.Render;
using Lighthouse.Render.Verify;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;

namespace Lighthouse.EditorTools
{
    /// <summary>
    /// M2 실측·캡처·장면 생성 (에디터 기준; 실기기는 미확인). 배치 모드(그래픽 필요, -nographics 금지):
    /// Unity -batchmode -quit -projectPath . -executeMethod Lighthouse.EditorTools.M2Tools.Run -m2Out &lt;dir&gt;
    /// 하는 일: (1) Unity 스프라이트 렌더(GPU) 프레임을 HTML 골든 프레임과 대조 (2) 굽기 시간·메모리 실측 (3) 풀세트·계절·외형·배·아이콘·꾸미기 PNG (4) M2Showcase 씬 생성
    /// </summary>
    public static class M2Tools
    {
        public const string ScenePath = "Assets/_Lighthouse/Scenes/M2Showcase.unity";
        const string MatPath = "Assets/_Lighthouse/Art/Materials/SpriteUnlit.mat";

        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        static double Median(List<double> v) { v.Sort(); return v[v.Count / 2]; }

        [MenuItem("Lighthouse/M2/Run")]
        public static void Run()
        {
            string dir = Arg("-m2Out", Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/m2")));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.processorType} x{SystemInfo.processorCount}, GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), scripting=Mono(Editor), debug={UnityEngine.Debug.isDebugBuild}");
            try { CreateScene(); sb.AppendLine("씬 생성: " + ScenePath); } catch (Exception e) { sb.AppendLine("씬 생성 실패: " + e.Message); }
            try { GpuFrames(sb, dir); } catch (Exception e) { sb.AppendLine("GPU 프레임 대조 실패: " + e); }
            try { ShowcaseCheck(sb, dir); } catch (Exception e) { sb.AppendLine("M2Showcase 점검 실패: " + e); }
            try { Measure(sb); } catch (Exception e) { sb.AppendLine("실측 실패: " + e); }
            try { Sheets(dir, sb); } catch (Exception e) { sb.AppendLine("PNG 생성 실패: " + e); }
            File.WriteAllText(Path.Combine(dir, "m2_measure.txt"), sb.ToString(), new UTF8Encoding(false));
            UnityEngine.Debug.Log("[Lighthouse] M2 완료\n" + sb);
        }

        static Material SpriteMat() => AssetDatabase.LoadAssetAtPath<Material>(MatPath);

        // ---------- (0) 씬 ----------
        static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.orthographic = true; cam.cullingMask = 0;
            camGo.transform.position = new Vector3(0, 0, -10);
            var go = new GameObject("M2Showcase");
            go.AddComponent<WorldView>();
            var sc = go.AddComponent<M2Showcase>(); sc.SpriteMaterial = SpriteMat(); sc.AutoCycle = true;
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---------- (1) Unity 스프라이트 렌더(GPU) 프레임 vs HTML 골든 프레임 ----------
        static void GpuFrames(StringBuilder sb, string dir)
        {
            sb.AppendLine("\n[Unity 스프라이트 렌더(GPU, RT ARGB32) vs HTML 골든 프레임 — dusk 0/1 고정, 정적 층]");
            var gp = GoldenPack.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/golden/m2/out/frames.bin.gz")));
            var metaPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/golden/m2/out/frames.meta"));
            var go = new GameObject("M2GpuTest") { hideFlags = HideFlags.HideAndDontSave };
            var view = go.AddComponent<WorldView>(); view.SpriteMaterial = SpriteMat();
            long totalPx = 0, diff = 0, over1 = 0, over2 = 0; int maxD = 0; int frames = 0;
            foreach (var line in File.ReadAllLines(metaPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(';'); int w = int.Parse(p[0]), h = int.Parse(p[1]), st = int.Parse(p[3]), gl = int.Parse(p[4]); string sn = p[2];
                var sp = SceneSprites.Build(w, h, sn, st, gardenLv: gl, slotLv: 32, put: DecorLayout.DevFill());
                var ops = SceneComposer.Ops(sp);
                foreach (int d in new[] { 0, 1 })
                {
                    view.Build(w, h, ops, d);
                    var got = view.Capture();
                    string name = "frame_" + w + "x" + h + "_" + sn + "_s" + st + "_" + (d == 1 ? "n" : "d");
                    var r0 = Cmp.DiffOpaque("gpu", name, got, gp.Items[name], 255);
                    var r1 = Cmp.DiffOpaque("gpu", name, got, gp.Items[name], 1);
                    var r2 = Cmp.DiffOpaque("gpu", name, got, gp.Items[name], 2);
                    totalPx += r0.Pixels; diff += r0.ExactDiff; over1 += r1.OverTol; over2 += r2.OverTol; maxD = Math.Max(maxD, r0.MaxDelta); frames++;
                    sb.AppendLine($"{name}: 다른 픽셀 {r0.ExactDiff}/{r0.Pixels}, 최대 채널 차 {r0.MaxDelta}, ±1 초과 {r1.OverTol}, ±2 초과 {r2.OverTol}" + (r1.OverTol > 0 ? " [" + r1.Note + "]" : ""));
                    if (d == 0 && sn == "summer" && st == 6 && w == 216) File.WriteAllBytes(Path.Combine(dir, "gpu_frame_216x468_summer_s6_d.png"), ToPng(got, 2));
                    if (r1.OverTol > 0) File.WriteAllBytes(Path.Combine(dir, "gpu_over_" + name + ".png"), ToPng(got, 2));
                }
            }
            view.Clear(); UnityEngine.Object.DestroyImmediate(go);
            sb.AppendLine($"합계: 프레임 {frames}장, 전체 {totalPx} 픽셀 중 다른 픽셀 {diff}, 최대 채널 차 {maxD}, ±1 초과 {over1}, ±2 초과 {over2}");
        }

        // ---------- (1b) M2Showcase 컴포넌트가 에디터에서 한 번에 만들어지는지 (플레이 모드 IMGUI 는 미확인) ----------
        static void ShowcaseCheck(StringBuilder sb, string dir)
        {
            var go = new GameObject("M2ShowcaseCheck") { hideFlags = HideFlags.HideAndDontSave };
            var sc = go.AddComponent<M2Showcase>(); sc.SpriteMaterial = SpriteMat(); sc.Dusk = 0.3f;
            sc.Rebuild(1080, 2340);
            var shot = sc.View.Capture();
            sb.AppendLine($"\n[M2Showcase.Rebuild(1080x2340)] 논리 {sc.Screen.W}x{sc.Screen.H} 배율 {sc.Screen.Scale}, 스프라이트 {sc.View.SpriteCount}개, 굽기+업로드 {F(sc.LastBakeMs)} ms, 캡처 {shot.W}x{shot.H}");
            sc.Decor = false; sc.Extras = false; sc.Stage = 3; sc.Season = "winter"; sc.TowerSkin = "snow"; sc.Dusk = 1f; sc.Rebuild(720, 1280);
            sb.AppendLine($"[M2Showcase 다른 설정 720x1280: 논리 {sc.Screen.W}x{sc.Screen.H} 배율 {sc.Screen.Scale}, 스프라이트 {sc.View.SpriteCount}개, {F(sc.LastBakeMs)} ms]");
            File.WriteAllBytes(Path.Combine(dir, "m2_showcase_winter_s3_night_720x1280.png"), ToPng(sc.View.Capture(), 2));
            // 연출: 하늘 장식·바다 물결을 프레임마다 CPU 로 그려 배경 텍스처에 올리는 비용 (60프레임 평균), 시각 고정 캡처
            sc.Decor = true; sc.Extras = true; sc.Stage = 6; sc.Season = "summer"; sc.TowerSkin = "classic"; sc.Animate = true;
            foreach (var (tt, dd) in new[] { (0.0, 0f), (137.3, 1f), (30.0, 0.5f) })
            {
                sc.Dusk = dd; sc.Rebuild(1080, 2340); sc.AnimateAt(tt);
                var times = new List<double>(); var sw2 = Stopwatch.StartNew();
                for (int i = 0; i < 60; i++) { sc.AnimateAt(tt + i / 30.0); times.Add(sc.LastFxMs); }
                sb.AppendLine($"[연출 프레임 dusk={dd}] 배경 합성+하늘+물결+업로드: 평균 {F(times.Average())} ms, 최대 {F(times.Max())} ms (216x468, 에디터 Mono 디버그, 60프레임)");
                sc.AnimateAt(tt);
                File.WriteAllBytes(Path.Combine(dir, $"m2_animate_t{tt}_dusk{dd}.png"), ToPng(sc.View.Capture(), 2));
            }
            sc.View.Clear(); UnityEngine.Object.DestroyImmediate(go);
        }

        // ---------- (2) 굽기 시간·메모리 ----------
        static void Measure(StringBuilder sb)
        {
            sb.AppendLine("\n[굽기 시간 ms: 첫 호출(JIT 포함) / 중앙값(20) — 에디터 Mono 디버그, 실기기 미확인]");
            Func<Action, (double first, double med)> T = a =>
            {
                var sw = Stopwatch.StartNew(); a(); double first = sw.Elapsed.TotalMilliseconds;
                var ts = new List<double>(); for (int i = 0; i < 20; i++) { sw.Restart(); a(); ts.Add(sw.Elapsed.TotalMilliseconds); }
                return (first, Median(ts));
            };
            double sumScene = 0;
            for (int st = 1; st <= 6; st++)
            {
                var env = Palettes.IslandEnvFor("summer", st, gardenLv: 6);
                IslandResult ir = null;
                var tIsland = T(() => ir = new IslandBaker(env).Bake());
                var P = TowerParams.ForStage(st);
                var tTower = T(() => { TowerBaker.BakeA(P); TowerBaker.BakeB(P); TowerBaker.BakeLamp(P, false); TowerBaker.BakeLamp(P, true); });
                var tNight = T(() => { Night.Of(ir.Island); foreach (var pc in ir.Pieces) Night.Of(pc.Spr); Night.Of(TowerBaker.BakeA(P)); Night.Of(TowerBaker.BakeB(P)); });
                var put = DecorLayout.DevFill(); SceneSprites sp = null;
                var tScene = T(() => sp = SceneSprites.Build(216, 468, "summer", st, gardenLv: 6, slotLv: 32, put: put));
                sumScene += tScene.med;
                sb.AppendLine($"단계 {st}: 섬+조각 {F(tIsland.first)}/{F(tIsland.med)}, 탑 A·B·램프2 {F(tTower.first)}/{F(tTower.med)}, 밤 사본(섬·조각·탑) {F(tNight.first)}/{F(tNight.med)}, 장면 전체(배경 낮·밤+물가+장식 풀세트+밤 사본 포함) {F(tScene.first)}/{F(tScene.med)}");
            }
            sb.AppendLine($"장면 전체 6단계 중앙값 합계 {F(sumScene)} ms");
            var tBg = T(() => { BgBaker.MakeBg(false, 216, 468, 148); BgBaker.MakeBg(true, 216, 468, 148); });
            var tBgBig = T(() => { BgBaker.MakeBg(false, 1080, 2340, 2340 - 320); });
            sb.AppendLine($"배경 낮+밤 216x468: {F(tBg.first)}/{F(tBg.med)} ms, 논리 해상도가 아닌 기기 해상도 1080x2340 한 장(참고): {F(tBgBig.first)}/{F(tBgBig.med)} ms");
            var tDecor = T(() => { foreach (var it in DecorBaker.Items) Night.Of(DecorBaker.Bake(it, "summer", "classic")); });
            var tDecor4 = T(() => { foreach (var sn in Palettes.Seasons) foreach (var it in DecorBaker.Items) DecorBaker.Bake(it, sn, "classic"); });
            sb.AppendLine($"꾸미기 아이템 10종 낮+밤: {F(tDecor.first)}/{F(tDecor.med)} ms, 계절 4×10종 낮만: {F(tDecor4.first)}/{F(tDecor4.med)} ms");
            var tShip = T(() => { foreach (var t in ShipBaker.Types) for (int l = 0; l < 3; l++) { var s = ShipBaker.Sprite(t, l); Night.Of(s); } });
            sb.AppendLine($"배 9종×3항로(낮+밤) 27개: {F(tShip.first)}/{F(tShip.med)} ms");
            var tIcon = T(() => { foreach (var id in IconBaker.Ids) IconBaker.Bake(id); });
            sb.AppendLine($"아이콘 68종: {F(tIcon.first)}/{F(tIcon.med)} ms (런타임이 아니라 빌드 때 구워 PNG 로 둘 계획)");
            var tSeason = T(() => { var e = Palettes.IslandEnvFor("autumn", 6, gardenLv: 6); var r = new IslandBaker(e).Bake(); Night.Of(r.Island); foreach (var pc in r.Pieces) Night.Of(pc.Spr); });
            sb.AppendLine($"계절 바뀜(섬 6단계 + 밤 사본만 다시): {F(tSeason.first)}/{F(tSeason.med)} ms");

            sb.AppendLine("\n[메모리·업로드: 장면 한 장(6단계, 꾸미기 풀세트, 구름·배 포함)]");
            var go = new GameObject("M2Measure") { hideFlags = HideFlags.HideAndDontSave };
            var view = go.AddComponent<WorldView>(); view.SpriteMaterial = SpriteMat();
            foreach (var (w, h) in new[] { (216, 468), (180, 320), (206, 446) })
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long heap0 = GC.GetTotalMemory(true); long uni0 = Profiler.GetTotalAllocatedMemoryLong();
                var sw = Stopwatch.StartNew();
                var sp = SceneSprites.Build(w, h, "summer", 6, gardenLv: 6, slotLv: 32, put: DecorLayout.DevFill());
                double bake = sw.Elapsed.TotalMilliseconds; sw.Restart();
                var ops = SceneExtras.OpsWithExtras(sp);
                view.Build(w, h, ops, 0.5);
                double up = sw.Elapsed.TotalMilliseconds;
                long heap1 = GC.GetTotalMemory(false); long uni1 = Profiler.GetTotalAllocatedMemoryLong();
                long bm = ops.Select(o => o.D).Concat(ops.Where(o => o.N != null).Select(o => o.N)).Distinct().Sum(b => (long)b.W * b.H * 4);
                sb.AppendLine($"{w}x{h}: 굽기 {F(bake)} ms, 스프라이트·텍스처 업로드(명령 {ops.Count}개, 스프라이트 {view.SpriteCount}개) {F(up)} ms (첫 호출은 셰이더·JIT 포함); 텍스처 {view.TextureBytes / 1024.0:F1} KB (비트맵 CPU 사본 {bm / 1024.0:F1} KB) + RT {(long)w * h * 4 / 1024.0:F1} KB; 관리 힙 +{(heap1 - heap0) / 1024.0:F0} KB, 유니티 총 할당 +{(uni1 - uni0) / 1024.0:F0} KB");
                GC.KeepAlive(sp);
            }
            view.Clear(); UnityEngine.Object.DestroyImmediate(go);
        }

        // ---------- (3) PNG ----------
        public static byte[] ToPng(Bitmap b, int scale = 1)
        {
            var tex = new Texture2D(b.W * scale, b.H * scale, TextureFormat.RGBA32, false);
            var px = new Color32[b.W * scale * b.H * scale];
            for (int y = 0; y < b.H; y++)
                for (int x = 0; x < b.W; x++)
                {
                    uint u = b.D[y * b.W + x]; var c = new Color32((byte)(u & 255), (byte)((u >> 8) & 255), (byte)((u >> 16) & 255), (byte)(u >> 24));
                    for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++) px[(b.H * scale - 1 - (y * scale + dy)) * (b.W * scale) + x * scale + dx] = c;
                }
            tex.SetPixels32(px); var png = tex.EncodeToPNG(); UnityEngine.Object.DestroyImmediate(tex); return png;
        }

        static Bitmap Montage(List<Bitmap> tiles, int cols, int pad, uint bg)
        {
            int tw = tiles.Max(t => t.W), th = tiles.Max(t => t.H), rows = (tiles.Count + cols - 1) / cols;
            var m = new Bitmap(0, 0, cols * (tw + pad) + pad, rows * (th + pad) + pad);
            for (int i = 0; i < m.D.Length; i++) m.D[i] = bg;
            for (int k = 0; k < tiles.Count; k++)
            {
                int ox = pad + (k % cols) * (tw + pad), oy = pad + (k / cols) * (th + pad); var t = tiles[k];
                for (int y = 0; y < t.H; y++) for (int x = 0; x < t.W; x++) m.D[(oy + y) * m.W + ox + x] = t.D[y * t.W + x];
            }
            return m;
        }

        static Bitmap Frame(string season, int stage, double dusk, string isle = "classic", string decor = "classic", string tower = "classic", bool put = true)
        {
            var sp = SceneSprites.Build(216, 468, season, stage, isle, decor, tower, gardenLv: 6, slotLv: 32, put: put ? DecorLayout.DevFill() : null);
            return SceneComposer.Compose(sp, dusk).ToBitmap();
        }

        static void Sheets(string dir, StringBuilder sb)
        {
            sb.AppendLine("\n[PNG 시트]");
            // 풀세트 한 장 (GPU 경로, 구름·배 포함): 낮 / 밤 / 해질녘 0.5
            var go = new GameObject("M2Shot") { hideFlags = HideFlags.HideAndDontSave };
            var view = go.AddComponent<WorldView>(); view.SpriteMaterial = SpriteMat();
            var sp6 = SceneSprites.Build(216, 468, "summer", 6, gardenLv: 6, slotLv: 32, put: DecorLayout.DevFill());
            var ops6 = SceneExtras.OpsWithExtras(sp6);
            var shots = new List<Bitmap>();
            foreach (double d in new[] { 0.0, 0.5, 1.0 }) { view.Build(216, 468, ops6, d); shots.Add(view.Capture()); }
            File.WriteAllBytes(Path.Combine(dir, "m2_fullset_day_dusk_night.png"), ToPng(Montage(shots, 3, 4, 0xff202020), 2));
            view.Clear(); UnityEngine.Object.DestroyImmediate(go);
            // 계절 4 × 낮·밤 (6단계, 풀세트)
            var seasons = new List<Bitmap>();
            foreach (var d in new[] { 0.0, 1.0 }) foreach (var s in Palettes.Seasons) seasons.Add(Frame(s, 6, d));
            File.WriteAllBytes(Path.Combine(dir, "m2_seasons_day_night.png"), ToPng(Montage(seasons, 4, 4, 0xff202020), 1));
            // 단계 1~6 (여름 낮, 꾸미기 풀세트)
            File.WriteAllBytes(Path.Combine(dir, "m2_stages.png"), ToPng(Montage(Enumerable.Range(1, 6).Select(s => Frame("summer", s, 0)).ToList(), 6, 4, 0xff202020), 1));
            // 외형: 섬(벚꽃·단풍) · 마을(바닷가·축제) · 탑(넓은 램프·뾰족 지붕·달빛)
            var skins = new List<Bitmap>
            {
                Frame("summer", 6, 0, isle: "blossom"), Frame("summer", 6, 0, isle: "maple"), Frame("summer", 6, 0, decor: "seaside"), Frame("summer", 6, 0, decor: "festival"),
                Frame("summer", 6, 0, tower: "widelamp"), Frame("summer", 6, 0, tower: "spire"), Frame("summer", 6, 0, tower: "moonlit"), Frame("summer", 6, 1, tower: "gilded")
            };
            File.WriteAllBytes(Path.Combine(dir, "m2_skins.png"), ToPng(Montage(skins, 4, 4, 0xff202020), 1));
            // 탑 외형 15 (6단계, 탑만, 낮)
            var towers = new List<Bitmap>();
            foreach (var sk in Palettes.TowerSkins)
            {
                var P = TowerParams.ForStage(6, 2, sk); var c = new CpuCanvas(64, 120); c.Copy(BgBaker.MakeBg(false, 64, 120, 120 - 320 + 170));
                var a = TowerBaker.BakeA(P, sk); var b = TowerBaker.BakeB(P); var l = TowerBaker.BakeLamp(P, false);
                c.Put(a, 32, 110); c.Put(l, 32, 110); c.Put(b, 32, 110); towers.Add(c.ToBitmap());
            }
            File.WriteAllBytes(Path.Combine(dir, "m2_tower_skins.png"), ToPng(Montage(towers, 8, 3, 0xff202020), 3));
            // 배 9종 × 항로 3 (왼쪽: 낮, 가운데: 도움받은 색, 오른쪽: 밤)
            var ships = new List<Bitmap>();
            foreach (var t in ShipBaker.Types)
                for (int lane = 0; lane < 3; lane++)
                {
                    var c = new CpuCanvas(76, 46); c.Copy(BgBaker.MakeBg(false, 76, 46, 0));
                    var d = ShipBaker.Sprite(t, lane); var h = ShipBaker.Sprite(t, lane, "classic", true);
                    c.Put(d, 38, 40); ships.Add(c.ToBitmap());
                    var c2 = new CpuCanvas(76, 46); c2.Copy(BgBaker.MakeBg(true, 76, 46, 0)); c2.Put(Night.Of(d), 38, 40); ships.Add(c2.ToBitmap());
                    var c3 = new CpuCanvas(76, 46); c3.Copy(BgBaker.MakeBg(false, 76, 46, 0)); c3.Put(h, 38, 40); ships.Add(c3.ToBitmap());
                }
            File.WriteAllBytes(Path.Combine(dir, "m2_ships.png"), ToPng(Montage(ships, 9, 2, 0xff202020), 2));
            // 꾸미기 10종 × (계절 4) + 마을 외형 2
            var decors = new List<Bitmap>();
            foreach (var it in DecorBaker.Items)
                foreach (var (sn, sk) in new[] { ("spring", "classic"), ("summer", "classic"), ("autumn", "classic"), ("winter", "classic"), ("summer", "seaside"), ("summer", "festival") })
                {
                    var c = new CpuCanvas(40, 36); c.Copy(BgBaker.MakeBg(false, 40, 36, 0)); c.Put(DecorBaker.Bake(it, sn, sk), 20, 28); decors.Add(c.ToBitmap());
                }
            File.WriteAllBytes(Path.Combine(dir, "m2_decor.png"), ToPng(Montage(decors, 6, 2, 0xff202020), 3));
            // 아이콘 68 + 대체
            var icons = new List<Bitmap>();
            foreach (var id in IconBaker.Ids.Concat(new[] { "__nope__" }))
            {
                var ic = IconBaker.Bake(id); var c = new CpuCanvas(16, 16); c.Copy(BgBaker.MakeBg(true, 16, 16, 0 - 150 + 80)); c.Put(ic, (16 - ic.W) / 2, (16 - ic.H) / 2); icons.Add(c.ToBitmap());
            }
            File.WriteAllBytes(Path.Combine(dir, "m2_icons.png"), ToPng(Montage(icons, 12, 2, 0xff202020), 4));
            sb.AppendLine("m2_fullset_day_dusk_night, m2_seasons_day_night, m2_stages, m2_skins, m2_tower_skins, m2_ships, m2_decor, m2_icons .png → " + dir);
        }
    }
}
