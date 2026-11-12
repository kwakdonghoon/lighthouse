using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.Core;
using Lighthouse.UI;
using Lighthouse.UI.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.EditorTools
{
    /// <summary>
    /// A2 캡처(에디터 배치): 타이틀 인트로 장면(시각별)·말풍선 회피·사진 모드 틀·초점 고리·바다 전환 안개·꾸미기 섬 탭 고르기·첫 실행 안내를 같은 UI 코드로 그려 PNG 로 저장한다.
    /// 실행: Tools/unity_run_a2.ps1 (또는 -executeMethod Lighthouse.EditorTools.A2Tools.Capture -a2Out 폴더).
    /// </summary>
    public static class A2Tools
    {
        static string Arg(string name, string def) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return def; }

        public static void Capture()
        {
            string outDir = Arg("-a2Out", Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Tools", "a2_results", "a2")); Directory.CreateDirectory(outDir);
            var report = new StringBuilder(); var bad = new List<string>(); int checks = 0, shots = 0;
            try
            {
                var T = M3Tools.TablesPublic(); var str = M3Tools.StringsPublic(); UiRoot.BootStrings = str;
                var jobs = new List<(string name, int w, int h)>();
                foreach (var (n, w, h) in new[] { ("320x568", 320, 568), ("393x852", 393, 852) })
                {
                    foreach (var st in new[] { "a2_title_t0.5", "a2_title_t3.0", "a2_title_t5.4", "a2_title_t7.0", "a2_title_t9.8", "a2_title_t10.5", "a2_title_t13", "a2_title_plain", "a2_photo", "a2_photo_shared", "a2_hint", "a2_focus", "a2_region_fog", "a2_deco_near", "a2_deco_picked", "a2_bubble_avoid" })
                        jobs.Add((n + "_" + st, w, h));
                }
                foreach (var (name, w, h) in jobs)
                {
                    string st = name.Substring(name.IndexOf('_') + 1);
                    var png = M3Tools.CaptureOnePublic(T, str, w * 2, h * 2, 2f, st, report, bad, ref checks);
                    File.WriteAllBytes(Path.Combine(outDir, "a2_" + name + ".png"), png); shots++;
                }
                report.AppendLine($"캡처 {shots}장, 감사 항목 {checks}건, 위반 {bad.Count}건"); foreach (var b in bad) report.AppendLine("  위반: " + b);
                File.WriteAllText(Path.Combine(outDir, "audit.txt"), report.ToString(), new UTF8Encoding(false));
                Debug.Log("[A2] 캡처 완료\n" + report);
            }
            catch (Exception e) { Debug.LogError("[A2] 캡처 실패: " + e); File.WriteAllText(Path.Combine(outDir, "audit_error.txt"), e.ToString()); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }

        /// <summary>M3Tools.ApplyState 가 a2_ 로 시작하는 상태를 넘겨 준다.</summary>
        public static void ApplyState(UiRoot ui, GameContext ctx, string state)
        {
            var c = ui.Ctl; var P = ui.Panel;
            if (state.StartsWith("a2_title_t"))
            {
                bool plain = state == "a2_title_plain"; float t = plain ? 4f : float.Parse(state.Substring("a2_title_t".Length), System.Globalization.CultureInfo.InvariantCulture);
                ui.TitleHold = true; ui.ShowTitle(true, !plain); ui.Title.ForcedT = t; ui.Title.Redraw(t); ui.Title.SetTextAlpha(t); return;
            }
            if (state == "a2_title_plain") { ui.TitleHold = true; ui.ShowTitle(true, false); ui.Title.ForcedT = 4f; ui.Title.Redraw(4f); ui.Title.SetTextAlpha(4f); return; }
            switch (state)
            {
                case "a2_photo": ui.SetPhoto(true); break;
                case "a2_photo_shared": ui.SetPhoto(true); ui.SetPhotoMessage("photo.shared"); break;
                case "a2_hint": ui.PlayHint(); ui.Hint.gameObject.SetActive(true); { var g = ui.Hint.Box.GetComponent<CanvasGroup>(); if (g != null) g.alpha = IntroTiming.HintAlpha(4f); } break;
                case "a2_focus":
                    c.Open(PanelMode.Deco); c.Act("zone:front"); P.Refresh(false); Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(P.CardRect); LayoutRebuilder.ForceRebuildLayoutImmediate(P.Content);
                    { var t = P.GetComponentsInChildren<ActTag>(false).FirstOrDefault(x => x.Act != null && x.Act.StartsWith("slot:")); if (ui.Ring != null && t != null) { UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(t.gameObject); Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(ui.CanvasRect); ui.Ring.Show((RectTransform)t.transform, ui.CanvasRect); } }
                    return;
                case "a2_region_fog":
                    { var fog = RegionFogView.Create(ui.transform); float now = 0f; fog.Play("먼바다", ui.Str.Get("region.sailing"), false, () => { }, () => { }, () => now); now = 0.95f; Canvas.ForceUpdateCanvases(); fog.Group.alpha = 1f; fog.Line1.ForceMeshUpdate(); fog.Line2.ForceMeshUpdate(); }
                    break;
                case "a2_deco_near":
                    c.Open(PanelMode.Deco); c.Act("zone:front"); { var ids = c.DecoOpenSlotIds(); c.DecoTapPick(new List<(double, string)> { (12, ids[1]), (7, ids[0]) }); }
                    break;
                case "a2_deco_picked":
                    c.Open(PanelMode.Deco); c.Act("zone:front"); { var ids = c.DecoOpenSlotIds(); c.DecoTapPick(new List<(double, string)> { (9, ids[0]) }); }
                    break;
                case "a2_bubble_avoid":
                    {
                        // 표시 줄 바로 아래(원래 자리가 표시 줄과 겹침) → 아래로 비킨다. 둘째는 화면 오른쪽 끝에서 안쪽으로 밀리고 꼬리는 대상(배)을 가리킨다.
                        var uis = new List<UiRectF>(); ui.CollectAvoidRects(uis);
                        var b1 = Lighthouse.App.BubbleView.Create(ui.transform); b1.gameObject.SetActive(true); b1.SetText("덕분에 무사히 지나가요"); b1.Place(UiRoot.CanvasW * 0.5f, ScreenLayout.HudTop(ui.Safe) + 40f, UiRoot.CanvasW, UiRoot.CanvasH, uis);
                        var b2 = Lighthouse.App.BubbleView.Create(ui.transform); b2.gameObject.SetActive(true); b2.SetText("바람이 시원해요"); b2.Place(UiRoot.CanvasW - 6f, UiRoot.CanvasH * 0.55f, UiRoot.CanvasW, UiRoot.CanvasH, uis);
                        ui.ShowToast("진주를 받았어요", "선물이 도감에 들어갔어요"); ui.ToastState.ShownAt -= 1000; ui.Toast.Apply(ui.ToastState, ui.ToastState.ShownAt + 1000);
                        var b3 = Lighthouse.App.BubbleView.Create(ui.transform); b3.gameObject.SetActive(true); b3.SetText("고마워요!"); ui.CollectAvoidRects(uis); b3.Place(UiRoot.CanvasW * 0.5f, UiRoot.CanvasH * 0.22f + 30f, UiRoot.CanvasW, UiRoot.CanvasH, uis);      // 토스트 위에서 비킨다
                        Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(ui.CanvasRect);
                        b1.Place(UiRoot.CanvasW * 0.5f, ScreenLayout.HudTop(ui.Safe) + 40f, UiRoot.CanvasW, UiRoot.CanvasH, uis); b2.Place(UiRoot.CanvasW - 6f, UiRoot.CanvasH * 0.55f, UiRoot.CanvasW, UiRoot.CanvasH, uis); b3.Place(UiRoot.CanvasW * 0.5f, UiRoot.CanvasH * 0.22f + 30f, UiRoot.CanvasW, UiRoot.CanvasH, uis);
                        break;
                    }
            }
            P.Refresh(false);
        }
    }
}
