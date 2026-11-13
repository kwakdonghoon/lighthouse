using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.Core;
using Lighthouse.Data;
using Lighthouse.Game;
using Lighthouse.Render;
using Lighthouse.UI;
using Lighthouse.UI.Model;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Lighthouse.EditorTools
{
    /// <summary>도트 UI 틀 PNG(Resources/UI/Frames)를 9-slice 스프라이트로 가져온다(포인트 필터·압축 없음·테두리는 frames.json).</summary>
    public sealed class FrameImporter : AssetPostprocessor
    {
        const string Dir = "Assets/_Lighthouse/Resources/UI/Frames/";
        static Dictionary<string, int[]> slices;
        static void Load()
        {
            if (slices != null) return; slices = new Dictionary<string, int[]>();
            var p = Dir + "frames.json"; if (!File.Exists(p)) return;
            var o = JObject.Parse(File.ReadAllText(p));
            foreach (var pr in o.Properties()) if (pr.Value["slice"] is JArray a) slices[pr.Name] = a.Select(x => (int)x).ToArray();      // [위, 오른쪽, 아래, 왼쪽]
        }
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Dir) || !assetPath.EndsWith(".png")) return;
            Load();
            var ti = (TextureImporter)assetImporter; string name = Path.GetFileNameWithoutExtension(assetPath);
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.spritePixelsPerUnit = 100f; ti.npotScale = TextureImporterNPOTScale.None; ti.wrapMode = TextureWrapMode.Clamp;
            if (slices.TryGetValue(name, out var s)) ti.spriteBorder = new Vector4(s[3], s[2], s[1], s[0]);      // Unity: (왼쪽, 아래, 오른쪽, 위)
            var ts = ti.GetDefaultPlatformTextureSettings(); ts.maxTextureSize = 256; ti.SetPlatformTextureSettings(ts);
        }
    }

    /// <summary>
    /// M3 도구(에디터 배치): ① TMP 필수 자원 가져오기 ② 한글 폰트 에셋 만들기·글리프 누락 검사 ③ 화면 프리팹·씬 만들기 ④ 해상도 매트릭스 캡처·겹침/잘림/터치 크기 감사.
    /// 각 단계는 도메인 다시 불러오기가 필요해 따로 실행한다(Tools/unity_run_m3.ps1).
    /// </summary>
    public static class M3Tools
    {
        const string FrameDir = "Assets/_Lighthouse/Resources/UI/Frames";
        const string FontSrcDir = "Assets/_Lighthouse/Art/UI/Fonts";
        const string FontOutDir = "Assets/_Lighthouse/Resources/UI/Fonts";
        const string PrefabDir = "Assets/_Lighthouse/Resources/UI/Prefabs";
        public const string ScenePath = "Assets/_Lighthouse/Scenes/M3UI.unity";
        const string MatPath = "Assets/_Lighthouse/Art/Materials/SpriteUnlit.mat";

        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }
        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        // ---------- ① TMP 필수 자원 + 틀 가져오기 ----------
        public static void Step1_Prepare()
        {
            try
            {
                if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
                {
                    string pkg = Directory.GetDirectories(Path.Combine(ProjectRoot, "Library", "PackageCache"), "com.unity.ugui@*").First();
                    // 패키지 가져오기는 편집기 갱신 루프에서 끝난다: 끝났다는 알림을 받은 뒤에 나머지를 하고 종료한다
                    AssetDatabase.importPackageCompleted += n => { Debug.Log("[M3] TMP 필수 자원 가져옴: " + n); Step1_Rest(); };
                    AssetDatabase.importPackageFailed += (n, e) => { Debug.LogError("[M3] TMP 가져오기 실패: " + e); EditorApplication.Exit(1); };
                    AssetDatabase.ImportPackage(Path.Combine(pkg, "Package Resources", "TMP Essential Resources.unitypackage"), false);
                    return;
                }
            }
            catch (Exception e) { Debug.LogError("[M3] Step1 실패: " + e); EditorApplication.Exit(1); return; }
            Step1_Rest();
        }

        static void Step1_Rest()
        {
            try
            {
                AssetDatabase.Refresh();
                foreach (var f in Directory.GetFiles(FrameDir, "*.png")) AssetDatabase.ImportAsset(f.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
                foreach (var f in Directory.GetFiles(FontSrcDir, "*.ttf")) AssetDatabase.ImportAsset(f.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
                AssetDatabase.SaveAssets();
                Debug.Log("[M3] Step1 완료: TMP settings=" + File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset") + " frames=" + Directory.GetFiles(FrameDir, "*.png").Length);
            }
            catch (Exception e) { Debug.LogError("[M3] Step1 실패: " + e); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        // ---------- ② 폰트 ----------
        public static void Step2_Fonts()
        {
            var sb = new StringBuilder();
            try
            {
                Directory.CreateDirectory(FontOutDir);
                string glyphPath = Path.Combine(ProjectRoot, "Tools", "m3_results", "ui_glyphs.txt");
                string glyphs = File.ReadAllText(glyphPath, new UTF8Encoding(false)).Replace("\r", "").Replace("\n", "");
                foreach (var w in new[] { "Regular", "Bold" })
                {
                    var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontSrcDir}/NotoSansKR-{w}-Subset.ttf");
                    string outPath = $"{FontOutDir}/NotoSansKR-{w} SDF.asset";
                    if (File.Exists(outPath)) AssetDatabase.DeleteAsset(outPath);
                    var fa = TMP_FontAsset.CreateFontAsset(font, 40, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                    fa.name = $"NotoSansKR-{w} SDF";
                    AssetDatabase.CreateAsset(fa, outPath);
                    if (fa.material != null) { fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
                    for (int i = 0; i < fa.atlasTextures.Length; i++) if (fa.atlasTextures[i] != null) { fa.atlasTextures[i].name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTextures[i], fa); }
                    bool ok = fa.TryAddCharacters(glyphs, out string missing);
                    int have = glyphs.Count(c => fa.HasCharacter(c));
                    sb.AppendLine($"{fa.name}: 요청 {glyphs.Length}자, 아틀라스에 {have}자, 누락 {(missing ?? "").Length}자 [{missing}], atlas {fa.atlasTextures.Length}장 {fa.atlasWidth}x{fa.atlasHeight}");
                    EditorUtility.SetDirty(fa); AssetDatabase.SaveAssets();
                }
                var reg = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontOutDir}/NotoSansKR-Regular SDF.asset");
                var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset");
                if (settings != null && reg != null)
                {
                    var so = new SerializedObject(settings); so.FindProperty("m_defaultFontAsset").objectReferenceValue = reg; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings);
                    sb.AppendLine("TMP Settings 기본 글꼴 = " + reg.name);
                    // 한글을 낱말 중간(음절)에서 끊지 않는다(원본 CSS word-break: keep-all)
                    var hp = so.FindProperty("m_UseModernHangulLineBreakingRules");
                    if (hp != null) { hp.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); sb.AppendLine("TMP 현대 한글 줄바꿈 규칙 = 켬"); }
                    else sb.AppendLine("경고: m_UseModernHangulLineBreakingRules 속성을 찾지 못함");
                }
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(Path.Combine(ProjectRoot, "Tools", "m3_results", "m3"));
                File.WriteAllText(Path.Combine(ProjectRoot, "Tools", "m3_results", "m3", "fonts.txt"), sb.ToString(), new UTF8Encoding(false));
                Debug.Log("[M3] Step2 완료\n" + sb);
            }
            catch (Exception e) { Debug.LogError("[M3] Step2 실패: " + e); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        // ---------- 공용 ----------
        static GameTables Tables() => DataLoader.Load(new FolderDataSource(Path.Combine(Application.dataPath, "_Lighthouse", "Resources", "Data")));
        static UiStrings Strings() => UiStrings.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "_Lighthouse", "Resources", "Data", "ko", "ui.json"), new UTF8Encoding(false)));
        static IClock Clock() => new FixedClock(new DateTimeOffset(2026, 11, 11, 3, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), 540);

        // ---------- ③ 프리팹·씬 ----------
        public static void Step3_Prefabs()
        {
            try
            {
                Directory.CreateDirectory(PrefabDir);
                UiRoot.BootStrings = Strings();
                var root = UiRoot.Create(null, true);
                string[] names = { "Hud", "PopupPanel", "Toast", "Tip", "Title", "Hint", "PhotoBar", "AdLayer" };
                foreach (var n in names)
                {
                    var t = root.transform.Find(n); if (t == null) continue;
                    t.gameObject.SetActive(true);
                    PrefabUtility.SaveAsPrefabAssetAndConnect(t.gameObject, $"{PrefabDir}/{n}.prefab", InteractionMode.AutomatedAction);
                    if (n != "PopupPanel" && n != "Hud") t.gameObject.SetActive(false);
                }
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, $"{PrefabDir}/UiRoot.prefab");
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                UiRoot.BootStrings = null;

                // 씬: 카메라 + 데모 호스트(플레이 모드에서 샘플 상태로 UI 를 띄운다)
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" }; var cam = camGo.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.035f, 0.07f); cam.orthographic = true;
                var host = new GameObject("M3UiDemo"); host.AddComponent<M3UiDemo>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[M3] Step3 완료: 프리팹 " + (names.Length + 1) + "개, 씬 " + ScenePath);
            }
            catch (Exception e) { Debug.LogError("[M3] Step3 실패: " + e); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        // ---------- ④ 캡처·감사 ----------
        sealed class Res { public string Name; public int DpW, DpH; public string[] States; }
        static readonly string[] AllStates = { "main", "mail", "dex", "dexsea", "growth", "offer", "deco", "skin", "shop", "settings", "news", "title", "photo", "ad", "tip" };
        static readonly string[] SomeStates = { "main", "dex", "deco", "shop", "settings", "title" };

        public static void Step4_Capture()
        {
            var report = new StringBuilder(); var bad = new List<string>(); int shots = 0, checks = 0;
            string outDir = Arg("-m3Out", Path.Combine(ProjectRoot, "Tools", "m3_results", "m3")); Directory.CreateDirectory(outDir);
            try
            {
                var T = Tables(); var str = Strings(); UiRoot.BootStrings = str;
                string only = Arg("-m3Res", "");
                var resolutions = new[]
                {
                    new Res { Name = "320x568", DpW = 320, DpH = 568, States = AllStates }, new Res { Name = "360x640", DpW = 360, DpH = 640, States = SomeStates },
                    new Res { Name = "393x852", DpW = 393, DpH = 852, States = AllStates }, new Res { Name = "tablet768x1024", DpW = 768, DpH = 1024, States = AllStates },
                    new Res { Name = "tabletland1024x768", DpW = 1024, DpH = 768, States = SomeStates }
                }.Where(r => only == "" || only.Split(',').Contains(r.Name)).ToList();
                var fonts = new[] { UiTheme.FontRegular, UiTheme.FontBold };
                if (fonts[0] == null) throw new Exception("글꼴 에셋 없음(Step2 먼저)");
                foreach (var res in resolutions)
                {
                    float dpr = float.Parse(Arg("-m3Dpr", "2"), System.Globalization.CultureInfo.InvariantCulture); int devW = (int)(res.DpW * dpr), devH = (int)(res.DpH * dpr);
                    foreach (var st in res.States.Where(x => Arg("-m3States", "") == "" || Arg("-m3States", "").Split(',').Contains(x)))
                    {
                        var rig = CaptureOne(T, str, devW, devH, dpr, st, report, bad, ref checks);
                        string file = Path.Combine(outDir, $"m3_{res.Name}_{st}.png");
                        File.WriteAllBytes(file, rig); shots++;
                    }
                }
                report.AppendLine($"캡처 {shots}장, 감사 항목 {checks}건, 위반 {bad.Count}건");
                foreach (var b in bad) report.AppendLine("  위반: " + b);
                File.WriteAllText(Path.Combine(outDir, "audit.txt"), report.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(outDir, "center_log.tsv"), string.Join("\n", CenterLog), new UTF8Encoding(false)); CenterLog.Clear();
                Debug.Log("[M3] Step4 완료\n" + report);
            }
            catch (Exception e) { Debug.LogError("[M3] Step4 실패: " + e); File.WriteAllText(Path.Combine(outDir, "audit_error.txt"), e.ToString()); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        public static GameTables TablesPublic() => Tables(); public static UiStrings StringsPublic() => Strings();
        public static byte[] CaptureOnePublic(GameTables T, UiStrings str, int devW, int devH, float dprUsed, string state, StringBuilder report, List<string> bad, ref int checks) => CaptureOne(T, str, devW, devH, dprUsed, state, report, bad, ref checks);

        static byte[] CaptureOne(GameTables T, UiStrings str, int devW, int devH, float dprUsed, string state, StringBuilder report, List<string> bad, ref int checks)
        {
            // 상태
            int stage = state.StartsWith("a2_") ? 4 : state == "dexsea" || state == "growth" || state == "offer" || state == "deco" || state == "skin" ? 6 : state == "shop" ? 4 : 3;
            bool night = state == "main" && false;
            var ctx = SampleState.Make(T, Clock(), 11, stage, stage == 6 ? 260 : stage == 4 ? 150 : 70, stage >= 4 ? 4 : 1, night);
            ctx.S.Found["shell"] = Math.Max(1, ctx.S.Found.ContainsKey("shell") ? ctx.S.Found["shell"] : 1);
            if (state.StartsWith("cf_badges")) PrepBadges(ctx, state);
            // 월드 배경: M2 장면을 같은 크기로 굽는다(없는 것처럼 어둡게 가려질 것이므로 단계만 맞춘다)
            Texture2D world = null;
            var wgo = new GameObject("world"); var sc = wgo.AddComponent<M2Showcase>(); sc.SpriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatPath); sc.Stage = stage; sc.Season = "summer"; sc.Decor = stage >= 4; sc.Extras = true; sc.Animate = false; sc.Dusk = state == "main" ? 0.0f : 0.0f;
            sc.Rebuild(devW, devH); sc.AnimateAt(0);
            var wb = sc.View.Capture(); world = new Texture2D(wb.W, wb.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[wb.W * wb.H];
            for (int y = 0; y < wb.H; y++) for (int x = 0; x < wb.W; x++) { uint v = wb.D[y * wb.W + x]; px[(wb.H - 1 - y) * wb.W + x] = new Color32((byte)(v & 255), (byte)((v >> 8) & 255), (byte)((v >> 16) & 255), 255); }
            world.SetPixels32(px); world.Apply();
            var ls = sc.Screen;

            // UI
            var camGo = new GameObject("uicam"); var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.035f, 0.07f, 1f); cam.cullingMask = 1 << 5; cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
            var rt = new RenderTexture(devW, devH, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point }; rt.Create(); cam.targetTexture = rt;
            var ui = UiRoot.Create(cam, false);
            ui.ManualScreen = true; ui.DevW = devW; ui.DevH = devH; ui.Density = dprUsed;
            var host = ui; var code = new SaveCode(T, new SaveMigrator(T, Clock()));
            var safe = new Insets(0, devW <= 800 ? 24f : 0f, 0, devW <= 800 ? 16f : 0f);        // 폰: 위 24(상태 막대·노치) 아래 16(제스처 막대) 캔버스 단위
            ui.Safe = safe;
            ui.Setup(ctx, str, null, null, code, null);
            ui.ApplyScreen(devW, devH, safe);
            // 월드 배경을 정수 배율로 (WorldView.ShowOnScreen 과 같은 배치)
            var bgR = ui.Backdrop.rectTransform; ui.SetBackdrop(world); bgR.anchorMin = bgR.anchorMax = Vector2.zero; bgR.pivot = Vector2.zero;
            bgR.sizeDelta = new Vector2(ls.W * ls.Scale / ui.Scale, ls.H * ls.Scale / ui.Scale); bgR.anchoredPosition = new Vector2(-Mathf.Floor((ls.W * ls.Scale - ls.DevW) / 2f) / ui.Scale, 0);
            ApplyState(ui, ctx, state);
            Canvas.ForceUpdateCanvases(); foreach (var t in ui.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate();
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.CanvasRect); Canvas.ForceUpdateCanvases();
            Audit(ui, state, devW + "x" + devH, bad, ref checks);
            cam.Render(); cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(devW, devH, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, devW, devH), 0, 0); tex.Apply(); RenderTexture.active = prev;
            var png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(world); cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(ui.gameObject); UnityEngine.Object.DestroyImmediate(camGo); sc.View.Clear(); UnityEngine.Object.DestroyImmediate(wgo);
            return png;
        }

        /// <summary>콘텐츠 보완 1차 확인용 캡처(Step5): 320·393 폭에서 타이틀·긴 토스트·배지 숫자·말풍선(꿈·닌·죠)·둬.</summary>
        public static void Step5_ContentCapture()
        {
            string outDir = Arg("-m3Out", Path.Combine(ProjectRoot, "Tools", "m4_results", "content_fix1")); Directory.CreateDirectory(outDir);
            var report = new StringBuilder(); var bad = new List<string>(); int checks = 0, shots = 0;
            try
            {
                var T = Tables(); var str = Strings(); UiRoot.BootStrings = str;
                var states = new[] { "title", "cf_toast_fog", "cf_toast_sos", "cf_toast_first", "cf_badges_hud99", "cf_badges_n", "cf_badges_99", "cf_badges_7", "cf_bubbles" };
                foreach (var (name, w, h) in new[] { ("320x568", 320, 568), ("393x852", 393, 852) })
                    foreach (var st in states)
                    {
                        var png = CaptureOne(T, str, w * 2, h * 2, 2f, st, report, bad, ref checks);
                        File.WriteAllBytes(Path.Combine(outDir, $"cf1_{name}_{st}.png"), png); shots++;
                    }
                report.AppendLine($"캡처 {shots}장, 감사 항목 {checks}건, 위반 {bad.Count}건"); foreach (var b in bad) report.AppendLine("  위반: " + b);
                File.WriteAllText(Path.Combine(outDir, "audit.txt"), report.ToString(), new UTF8Encoding(false));
                Debug.Log("[M3] Step5 완료\n" + report);
            }
            catch (Exception e) { Debug.LogError("[M3] Step5 실패: " + e); File.WriteAllText(Path.Combine(outDir, "audit_error.txt"), e.ToString()); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        /// <summary>고정 시계에서는 토스트 투명도가 0(막 나타나는 순간)이라, 1초 지난 것처럼 만들어 보이게 한다.</summary>
        static void CfToast(UiRoot ui, string title, string sub) { ui.ShowToast(title, sub); ui.ToastState.ShownAt -= 1000; ui.Toast.Apply(ui.ToastState, ui.ToastState.ShownAt + 1000); }

        static void PrepBadges(GameContext ctx, string state)
        {
            ctx.S.Letters.Clear(); int n = state == "cf_badges_99" || state == "cf_badges_hud99" ? 150 : state == "cf_badges_7" ? 7 : 0;
            for (int i = 0; i < n; i++) ctx.S.Letters.Add(new Letter { Id = 8000 + i, Ship = "x", Cap = "y", T = ctx.Clock.NowMs, Read = false, Region = "home", Text = "t" + i, Sign = "s" });
            ctx.S.DexSeen = 0;      // 도감 새 항목 N
        }

        static void ApplyState(UiRoot ui, GameContext ctx, string state)
        {
            var c = ui.Ctl; var P = ui.Panel;
            if (state.StartsWith("a2_")) { A2Tools.ApplyState(ui, ctx, state); return; }
            switch (state)
            {
                case "main":
                    ui.ShowHint(true); ui.ShowToast("진주를 받았어요", "선물이 도감에 들어갔어요");
                    ctx.S.Guided = 12; break;
                case "mail": c.Open(PanelMode.Mail); var first = ctx.S.Letters.FirstOrDefault(); if (first != null) c.Act("letter:" + first.Id.ToString("0")); break;
                case "dex": c.Open(PanelMode.Dex); break;
                case "dexsea": c.Open(PanelMode.Dex); c.Act("dex:sea"); break;
                case "growth": c.Open(PanelMode.Growth); c.Act("rolebox"); c.Act("trk:lens"); break;
                case "offer": c.Open(PanelMode.Growth); c.Act("grow:offer"); break;
                case "deco": c.Open(PanelMode.Deco); c.Act("zone:front"); { var o = DecorRules.ZoneOpen(ctx.T, "front", ctx.Stage, ctx.LvlOf("garden")); if (o.Count > 0) { c.Act("slot:" + o[0].Id); c.Act("put:flower_planter"); if (o.Count > 1) c.Act("slot:" + o[1].Id); } } break;
                case "skin": c.Open(PanelMode.Deco); c.Act("dtab:skin"); break;
                case "shop": c.Open(PanelMode.Shop); c.Act("adtoggle"); c.Act("buy:moonpack"); break;
                case "settings": c.Open(PanelMode.Growth); c.Act("gear"); c.Act("setmore"); c.Act("tips"); break;
                case "news": c.OpenNews(new OfferNews(ctx.RunOffline(3 * 3600 + 5))); break;
                case "title": ui.ShowTitle(false); break;
                case "cf_toast_fog": CfToast(ui, ui.Str.Get("world.fogEnd"), ui.Str.F("world.bonusSub", ui.Str.F("world.fogEndSub", 12), "안개 조각")); break;      // 콘텐츠 검토 F-01: 가장 긴 토스트(원본 실측 390px)
                case "cf_toast_sos": CfToast(ui, ui.Str.Get("world.sos"), ui.Str.Get("world.sosSub")); break;
                case "cf_toast_first": CfToast(ui, ui.Str.F("toast.firstSea", "작은 게"), ui.Str.Get("toast.firstSub")); break;
                case "cf_badges_99": case "cf_badges_7": c.Open(PanelMode.Growth); break;
                case "cf_badges_hud99": case "cf_badges_n": break;
                case "cf_bubbles":
                    {
                        int k = 0;
                        foreach (var ch in new[] { '꿈', '닌', '죠' })
                        {
                            var line = ctx.T.Regulars.SelectMany(r => r.Lines).FirstOrDefault(l => l.IndexOf(ch) >= 0); if (line == null) continue;
                            var b = Lighthouse.App.BubbleView.Create(ui.transform); b.gameObject.SetActive(true); b.SetText(line); b.Place(UiRoot.CanvasW / 2f, 140f + 90f * k, UiRoot.CanvasW, UiRoot.CanvasH, new System.Collections.Generic.List<UiRectF>()); k++;
                        }
                        CfToast(ui, ui.Str.Get("world.sos"), ui.Str.Get("world.sosSub"));      // '둬' 가 들어 있는 문장
                        break;
                    }
                case "photo": ui.SetPhoto(true); break;
                case "tip": ui.Tip.Show(new TipInfo { Id = "t", Icon = "star", Text = "등대를 눌러 오늘의 첫 불빛을 켜 보세요" }, ui.Str, UiRoot.CanvasW / 2f, 220f, true, UiRoot.CanvasW); break;
                case "ad": c.Open(PanelMode.Shop); ui.AdAdapter.Show("ship"); ui.Ad.gameObject.SetActive(true); ui.Ad.SetReady(false, 3, ui.Str); break;
            }
            P.Refresh(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(P.CardRect);
        }

        // ---------- 감사: 겹침·잘림·터치 크기·안전 영역·글리프 ----------
        static void Audit(UiRoot ui, string state, string resName, List<string> bad, ref int checks)
        {
            string tag = $"{resName} {state}";
            var canvas = ui.CanvasRect; var corners = new Vector3[4];
            Rect R(RectTransform r) { r.GetWorldCorners(corners); var a = canvas.InverseTransformPoint(corners[0]); var b = canvas.InverseTransformPoint(corners[2]); return Rect.MinMaxRect(a.x, a.y, b.x, b.y); }
            var screen = new Rect(-ui.CanvasRect.rect.width / 2, -ui.CanvasRect.rect.height / 2, ui.CanvasRect.rect.width, ui.CanvasRect.rect.height);
            // 팝업 카드가 안전 영역 안
            if (ui.Panel.gameObject.activeInHierarchy)
            {
                checks++; var card = R(ui.Panel.CardRect);
                var safeRect = Rect.MinMaxRect(screen.xMin + ui.Safe.L, screen.yMin + ui.Safe.B, screen.xMax - ui.Safe.R, screen.yMax - ui.Safe.T);
                if (card.xMin < safeRect.xMin - 0.5f || card.xMax > safeRect.xMax + 0.5f || card.yMin < safeRect.yMin - 0.5f || card.yMax > safeRect.yMax + 0.5f) bad.Add($"{tag}: 팝업 카드가 안전 영역 밖 {card} / {safeRect}");
                var vp = R(ui.Panel.ViewportRect);
                // 가로 잘림: 본문 안의 모든 보이는 그래픽이 뷰포트 폭 안
                foreach (var g in ui.Panel.Content.GetComponentsInChildren<Graphic>(false))
                {
                    if (!g.isActiveAndEnabled) continue; checks++;
                    var rr = R(g.rectTransform);
                    if (rr.width > 0.5f && (rr.xMin < vp.xMin - 0.6f || rr.xMax > vp.xMax + 0.6f)) bad.Add($"{tag}: 가로 넘침 {PathOf(g.transform, ui.transform)} {rr.xMin:F1}..{rr.xMax:F1} vs {vp.xMin:F1}..{vp.xMax:F1}");
                }
                // 글이 자기 칸 높이를 넘는지(겹침의 원인)
                foreach (var t in ui.Panel.Content.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue; checks++;
                    t.ForceMeshUpdate(); var rr = R(t.rectTransform);
                    if (t.preferredHeight > rr.height + 1.5f) bad.Add($"{tag}: 글이 칸보다 큼 {PathOf(t.transform, ui.transform)} 글 {t.preferredHeight:F1} 칸 {rr.height:F1} '{Short(t.text)}'");
                    if (t.preferredWidth > rr.width + 1.5f && t.textWrappingMode == TextWrappingModes.NoWrap) bad.Add($"{tag}: 글 폭 넘침 {PathOf(t.transform, ui.transform)}");
                }
                // 보이는 부분의 터치 크기: 뷰포트 안에 보이는 단추 중 44 단위 미만
                foreach (var b in ui.Panel.GetComponentsInChildren<Button>(false))
                {
                    if (!b.isActiveAndEnabled || !b.interactable) continue; var rr = R((RectTransform)b.transform); checks++;
                    bool inScroll = b.transform.IsChildOf(ui.Panel.Content);
                    if (inScroll && (rr.yMax < vp.yMin || rr.yMin > vp.yMax)) continue;       // 스크롤 밖 단추는 지금 보이지 않는다
                    if (Mathf.Min(rr.width, rr.height) < ScreenLayout.MinTouch - 0.6f && !b.name.StartsWith("cell:") && !b.name.StartsWith("hb:")) bad.Add($"{tag}: 터치 크기 {Mathf.Min(rr.width, rr.height):F1} < 44 {PathOf(b.transform, ui.transform)}");
                    if (b.name.StartsWith("hb:") && Mathf.Min(rr.width, rr.height) < ScreenLayout.MinTouch - 0.6f) bad.Add($"{tag}: 머리 단추 터치 크기 {Mathf.Min(rr.width, rr.height):F1}");
                }
                // 같은 줄 단추끼리 겹침(부모-자식 제외)
                var btns = ui.Panel.GetComponentsInChildren<Button>(false).Where(b => b.isActiveAndEnabled && b.name != "dim").Select(b => (b, R((RectTransform)b.transform))).ToList();
                for (int i = 0; i < btns.Count; i++)
                    for (int j = i + 1; j < btns.Count; j++)
                    {
                        if (btns[i].b.transform.IsChildOf(btns[j].b.transform) || btns[j].b.transform.IsChildOf(btns[i].b.transform)) continue;
                        var a = btns[i].Item2; var c = btns[j].Item2; checks++;
                        // 스크롤 영역 밖으로 잘려 안 보이는 것과 머리 단추-탭 겹침은 제외하고, 둘 다 보이는 영역에서만
                        bool aIn = !btns[i].b.transform.IsChildOf(ui.Panel.Content) || a.Overlaps(vp), cIn = !btns[j].b.transform.IsChildOf(ui.Panel.Content) || c.Overlaps(vp);
                        if (!aIn || !cIn) continue;
                        var inter = Rect.MinMaxRect(Mathf.Max(a.xMin, c.xMin), Mathf.Max(a.yMin, c.yMin), Mathf.Min(a.xMax, c.xMax), Mathf.Min(a.yMax, c.yMax));
                        if (inter.width > 1f && inter.height > 1f) bad.Add($"{tag}: 단추 겹침 {PathOf(btns[i].b.transform, ui.transform)} × {PathOf(btns[j].b.transform, ui.transform)}");
                    }
            }
            CenterAudit(ui, tag, bad, ref checks);
            foreach (var t in ui.GetComponentsInChildren<TMP_Text>(false)) if (t.name == "chip" && t.isActiveAndEnabled) { var rr = R(t.rectTransform); checks++; if (rr.height > t.fontSize * 1.7f) bad.Add($"{tag}: 칩 높이가 글자보다 훨씬 큼(옆 줄 높이로 늘어남) {PathOf(t.transform, ui.transform)} '{t.text}' 칸 {rr.height:F0} 글자 {t.fontSize:F0}"); }
            // HUD 가 안전 영역 위쪽을 침범하지 않는다
            if (ui.Hud.gameObject.activeInHierarchy)
            {
                checks++; var pill = R(ui.Hud.Pill); if (pill.yMax > screen.yMax - ui.Safe.T + 0.5f) bad.Add($"{tag}: HUD 가 위 안전 영역 침범 {pill.yMax:F1} > {(screen.yMax - ui.Safe.T):F1}");
                if (pill.xMin < screen.xMin + ui.Safe.L - 0.5f || pill.xMax > screen.xMax - ui.Safe.R + 0.5f) bad.Add($"{tag}: HUD 가로 넘침");
                var menu = R(ui.Hud.MenuRect); checks++; if (menu.Overlaps(pill) && !ui.Panel.gameObject.activeInHierarchy) bad.Add($"{tag}: 메뉴 단추와 HUD 겹침 {menu} {pill}");
                if (Mathf.Min(menu.width, menu.height) < ScreenLayout.MinTouch - 0.6f) bad.Add($"{tag}: 메뉴 단추 터치 크기");
            }
            // 글리프: 화면의 모든 글자가 폰트에 있는가
            foreach (var t in ui.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue; checks++;
                foreach (char ch in t.text) if (!char.IsWhiteSpace(ch) && !t.font.HasCharacter(ch, true, true)) bad.Add($"{tag}: 글리프 없음 U+{(int)ch:X4} '{ch}' in {PathOf(t.transform, ui.transform)}");
            }
            // 토스트·안내·광고가 화면 안
            foreach (var rt in new RectTransform[] { ui.Toast.Box, ui.Tip.Box })
            {
                if (!rt.gameObject.activeInHierarchy) continue; checks++; var rr = R(rt);
                if (rr.xMin < screen.xMin - 0.5f || rr.xMax > screen.xMax + 0.5f || rr.yMin < screen.yMin - 0.5f || rr.yMax > screen.yMax + 0.5f) bad.Add($"{tag}: {rt.name} 가 화면 밖 {rr}");
            }
        }
        /// <summary>단추 안 글·아이콘이 단추 면 가운데에 오는지 전수 확인: 보이는 모든 단추의 내용 묶음 상자 중심 ↔ 단추 중심(그림자 띠 보정). 오차는 도트 단위로 기록.</summary>
        static readonly string[] FaceFrames = { "main", "main_p", "gold", "gold_p", "btn", "btn_p", "btn_d", "hbtn", "hbtn_p", "hbtn_on", "hbtn_on_p", "ib", "ib_p", "hud", "field" };
        public static readonly List<string> CenterLog = new List<string>();
        /// <summary>EditMode 회귀 시험이 부르는 진입점(그림 없이 사각형만 본다).</summary>
        public static int CenterAuditPublic(UiRoot ui, string tag, List<string> bad) { int c = 0; CenterAudit(ui, tag, bad, ref c); return c; }
        static void CenterAudit(UiRoot ui, string tag, List<string> bad, ref int checks)
        {
            var canvas = ui.CanvasRect; var corners = new Vector3[4]; float dot = Mathf.Max(0.5f, UiRoot.CurrentDot);
            Rect R(RectTransform r) { r.GetWorldCorners(corners); var a = canvas.InverseTransformPoint(corners[0]); var b = canvas.InverseTransformPoint(corners[2]); return Rect.MinMaxRect(a.x, a.y, b.x, b.y); }
            Rect vp = ui.Panel.gameObject.activeInHierarchy ? R(ui.Panel.ViewportRect) : default;
            foreach (var b in ui.GetComponentsInChildren<Button>(false))
            {
                if (!b.isActiveAndEnabled || b.name == "dim") continue;
                var fi = b.GetComponent<Image>(); string fn = fi != null && fi.sprite != null ? fi.sprite.name : "";
                bool face = System.Array.IndexOf(FaceFrames, fn) >= 0;      // 면이 있는 단추 틀. 그 밖(편지 줄·글자 링크·카드 머리줄·칸 접시)은 아래 '넘침·가운데' 약식 검사만 한다
                var brt = (RectTransform)b.transform; var br = R(brt);
                if (ui.Panel.gameObject.activeInHierarchy && b.transform.IsChildOf(ui.Panel.Content) && (br.yMax < vp.yMin || br.yMin > vp.yMax || br.yMax > vp.yMax + 0.5f || br.yMin < vp.yMin - 0.5f)) continue;       // 보이지 않거나 잘려 있는 단추
                bool any = false; float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue; bool centered = false; int items = 0;
                foreach (var t in b.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (!t.isActiveAndEnabled || string.IsNullOrWhiteSpace(t.text) || t.transform.IsChildOf(brt) && InBadge(t.transform, brt)) continue;
                    t.ForceMeshUpdate(); var bb = t.textBounds; if (bb.size.x <= 0.01f) continue;
                    var p0 = canvas.InverseTransformPoint(t.transform.TransformPoint(bb.min)); var p1 = canvas.InverseTransformPoint(t.transform.TransformPoint(bb.max));
                    x0 = Mathf.Min(x0, p0.x); x1 = Mathf.Max(x1, p1.x); y0 = Mathf.Min(y0, p0.y); y1 = Mathf.Max(y1, p1.y); any = true; items++;
                    if ((t.alignment & TextAlignmentOptions.Center) == TextAlignmentOptions.Center || (t.alignment & TextAlignmentOptions.Midline) != 0 && (t.alignment & TextAlignmentOptions.Left) == 0 && (t.alignment & TextAlignmentOptions.Right) == 0) centered = true;
                }
                foreach (var g in b.GetComponentsInChildren<Image>(false))
                {
                    if (g.transform == b.transform || !g.isActiveAndEnabled || !g.name.StartsWith("icon:") || InBadge(g.transform, brt)) continue;
                    var ir = R(g.rectTransform); if (ir.width < 0.5f) continue;
                    { float side = Mathf.Min(ir.width, ir.height); ir = new Rect(ir.center.x - side / 2f, ir.center.y - side / 2f, side, side); }      // preserveAspect: 실제 그려지는 정사각 그림 범위
                    x0 = Mathf.Min(x0, ir.xMin); x1 = Mathf.Max(x1, ir.xMax); y0 = Mathf.Min(y0, ir.yMin); y1 = Mathf.Max(y1, ir.yMax); any = true; items++;
                }
                if (!any || br.width < 20f) continue; checks++;
                if (!face)
                {
                    // 약식: 내용이 단추 사각형 안에 들어오는가(글자 링크는 가운데 정렬이면 좌우 1칸·상하 1.5칸 이내도 확인)
                    float fdx = ((x0 + x1) / 2f - br.center.x) / dot, fdy = ((y0 + y1) / 2f - br.center.y) / dot;
                    bool inside = x0 >= br.xMin - 0.6f && x1 <= br.xMax + 0.6f && y0 >= br.yMin - 0.6f && y1 <= br.yMax + 0.6f;
                    bool plainLink = fn == "" && centered && items == 1;
                    bool ctrOk = !plainLink || (Mathf.Abs(fdx) <= 1.0f && Mathf.Abs(fdy) <= 1.5f);
                    CenterLog.Add($"{tag}\tFIT {PathOf(b.transform, ui.transform)}\tdx={fdx:F1}\tdy={fdy:F1}\titems={items}\tbtn={br.width:F0}x{br.height:F0}\tcontent={(x1 - x0):F0}x{(y1 - y0):F0}\tframe={fn}");
                    if (!inside) bad.Add($"{tag}: 단추 내용이 사각형을 넘음 {PathOf(b.transform, ui.transform)} 내용 {(x1 - x0):F0}x{(y1 - y0):F0} 단추 {br.width:F0}x{br.height:F0}");
                    if (!ctrOk) bad.Add($"{tag}: 글자 링크가 가운데가 아님 {PathOf(b.transform, ui.transform)} dx={fdx:F1}칸 dy={fdy:F1}칸");
                    continue;
                }
                // 틀 그림의 위·아래 테두리 두께(도트): 아래 테두리는 그림자 띠를 포함 → 내용 기준 중심은 (아래-위)/2 칸 위
                float bTop = 3f, bBot = 5f, bSide = 3f; var fimg = b.GetComponent<Image>(); if (fimg != null && fimg.sprite != null && fimg.type == Image.Type.Sliced) { var bd = fimg.sprite.border; bSide = bd.x; bBot = bd.y; bTop = bd.w; }
                float expect = (bBot - bTop) / 2f;
                float cx = (x0 + x1) / 2f - br.center.x, cy = (y0 + y1) / 2f - br.center.y;      // cy>0 이면 내용이 위쪽
                float dxDots = cx / dot, dyDots = cy / dot - expect;
                bool horizOk = !centered || items > 2 || Mathf.Abs(dxDots) <= 1.0f;
                bool vertOk = items > 2 || Mathf.Abs(dyDots) <= 2.2f;      // 정수 아닌 배율에서는 레이아웃이 반 칸 단위로 어긋나 보일 수 있다(실제 화면 픽셀 기준은 Tools/ui_center_pixel_audit.py 가 1칸 이내로 검사)
                // 내용이 단추 안쪽 면(테두리·그림자 제외)을 넘는가
                float innerW = br.width - 2 * 2f * dot, innerH = br.height - (2f + 3f) * dot;      // 눈에 보이는 테두리 2칸 + 아래 그림자 3칸만 뺀 면
                bool fit = (x1 - x0) <= innerW + 0.6f && (y1 - y0) <= innerH + 0.6f;
                if (!fit && items == 1 && !b.GetComponentsInChildren<TMP_Text>(false).Any(t => t.isActiveAndEnabled && !string.IsNullOrWhiteSpace(t.text))) fit = (x1 - x0) <= br.width && (y1 - y0) <= br.height - 2f * dot;      // 아이콘만 있는 단추는 틀 안에 들어오면 된다
                string pxr = "";
                { var cv = ui.GetComponentInParent<Canvas>(); var cam = cv != null ? cv.worldCamera : null; if (cam != null) { brt.GetWorldCorners(corners); var s0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]); var s2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]); pxr = $"{s0.x:F1},{cam.pixelHeight - s2.y:F1},{s2.x:F1},{cam.pixelHeight - s0.y:F1}"; } }
                CenterLog.Add($"{tag}\t{PathOf(b.transform, ui.transform)}\tdx={dxDots:F1}\tdy={dyDots:F1}\titems={items}\tbtn={br.width:F0}x{br.height:F0}\tcontent={(x1 - x0):F0}x{(y1 - y0):F0}\tinner={innerW:F0}x{innerH:F0}\tpx={pxr}\tdot={dot:F2}\tchk={(brt.Find("badge:check") != null ? 1 : 0)}");
                if (!horizOk || !vertOk) bad.Add($"{tag}: 단추 내용이 가운데가 아님 {PathOf(b.transform, ui.transform)} dx={dxDots:F1}칸 dy={dyDots:F1}칸 (단추 {br.width:F0}x{br.height:F0})");
                if (!fit) bad.Add($"{tag}: 단추 내용이 면을 넘음 {PathOf(b.transform, ui.transform)} 내용 {(x1 - x0):F0}x{(y1 - y0):F0} 면 {innerW:F0}x{innerH:F0}");
            }
        }
        static bool InBadge(Transform t, Transform stop) { for (var c = t; c != null && c != stop; c = c.parent) if (c.name == "badge" || c.name.StartsWith("badge")) return true; return false; }
        static string Short(string s) => s.Length > 24 ? s.Substring(0, 24) + "…" : s;
        static string PathOf(Transform t, Transform root) { var l = new List<string>(); for (var c = t; c != null && c != root; c = c.parent) l.Insert(0, c.name); return string.Join("/", l); }

        [MenuItem("Lighthouse/M3/Open UI scene")] static void OpenScene() { EditorSceneManager.OpenScene(ScenePath); }
    }
}
