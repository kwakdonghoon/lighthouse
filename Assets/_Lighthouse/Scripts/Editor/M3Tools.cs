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
        static readonly string[] AllStates = { "main", "mail", "dex", "dexsea", "growth", "offer", "deco", "skin", "shop", "settings", "news", "title", "photo", "ad" };
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
                    float dpr = 2f; int devW = (int)(res.DpW * dpr), devH = (int)(res.DpH * dpr);
                    foreach (var st in res.States)
                    {
                        var rig = CaptureOne(T, str, devW, devH, dpr, st, report, bad, ref checks);
                        string file = Path.Combine(outDir, $"m3_{res.Name}_{st}.png");
                        File.WriteAllBytes(file, rig); shots++;
                    }
                }
                report.AppendLine($"캡처 {shots}장, 감사 항목 {checks}건, 위반 {bad.Count}건");
                foreach (var b in bad) report.AppendLine("  위반: " + b);
                File.WriteAllText(Path.Combine(outDir, "audit.txt"), report.ToString(), new UTF8Encoding(false));
                Debug.Log("[M3] Step4 완료\n" + report);
            }
            catch (Exception e) { Debug.LogError("[M3] Step4 실패: " + e); File.WriteAllText(Path.Combine(outDir, "audit_error.txt"), e.ToString()); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        static byte[] CaptureOne(GameTables T, UiStrings str, int devW, int devH, float dprUsed, string state, StringBuilder report, List<string> bad, ref int checks)
        {
            // 상태
            int stage = state == "dexsea" || state == "growth" || state == "offer" || state == "deco" || state == "skin" ? 6 : state == "shop" ? 4 : 3;
            bool night = state == "main" && false;
            var ctx = SampleState.Make(T, Clock(), 11, stage, stage == 6 ? 260 : stage == 4 ? 150 : 70, stage >= 4 ? 4 : 1, night);
            ctx.S.Found["shell"] = Math.Max(1, ctx.S.Found.ContainsKey("shell") ? ctx.S.Found["shell"] : 1);
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

        static void ApplyState(UiRoot ui, GameContext ctx, string state)
        {
            var c = ui.Ctl; var P = ui.Panel;
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
                case "photo": ui.SetPhoto(true); break;
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
                    if (t.preferredWidth > rr.width + 1.5f && !t.enableWordWrapping) bad.Add($"{tag}: 글 폭 넘침 {PathOf(t.transform, ui.transform)}");
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
        static string Short(string s) => s.Length > 24 ? s.Substring(0, 24) + "…" : s;
        static string PathOf(Transform t, Transform root) { var l = new List<string>(); for (var c = t; c != null && c != root; c = c.parent) l.Insert(0, c.name); return string.Join("/", l); }

        [MenuItem("Lighthouse/M3/Open UI scene")] static void OpenScene() { EditorSceneManager.OpenScene(ScenePath); }
    }
}
