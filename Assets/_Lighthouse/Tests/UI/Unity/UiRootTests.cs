using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.UI;
using Lighthouse.UI.Model;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.Tests
{
    /// <summary>M3: Unity 쪽 UI(프리팹·TMP·레이아웃) 시험. EditMode(-nographics)에서 돈다 — 그림은 그리지 않고 레이아웃·글리프·사각형만 본다.</summary>
    public class UiRootTests
    {
        static UiRoot Make(int dpW, int dpH, int stage, out UiFixture.Rig rig)
        {
            rig = UiFixture.Make(stage, stage == 6 ? 260 : stage == 1 ? 0 : 100, stage >= 4 ? 3 : 0);
            UiRoot.BootStrings = rig.Str;
            var ui = UiRoot.Create(null, false);
            ui.ManualScreen = true; ui.DevW = dpW * 2; ui.DevH = dpH * 2; ui.Density = 2f;
            ui.Safe = new Insets(0, dpW <= 800 ? 24f : 0f, 0, dpW <= 800 ? 16f : 0f);
            ui.Setup(rig.Ctx, rig.Str, rig.Pay, rig.Ads, null, null);
            return ui;
        }

        [Test]
        public void PrefabExistsAndLoads()
        {
            var pf = Resources.Load<GameObject>(UiRoot.PrefabPath);
            Assert.IsNotNull(pf, "UiRoot 프리팹이 Resources/UI/Prefabs 에 있어야 한다(M3Tools.Step3_Prefabs)");
            foreach (var n in new[] { "Hud", "PopupPanel", "Toast", "Tip", "Title", "Hint", "PhotoBar", "AdLayer" }) Assert.IsNotNull(Resources.Load<GameObject>("UI/Prefabs/" + n), n + " 프리팹");
            var ui = Make(360, 640, 3, out _);
            try { Assert.IsNotNull(ui.Panel); Assert.IsNotNull(ui.Hud); Assert.IsNotNull(ui.Tip.Off); Assert.IsNotNull(ui.Ad); Assert.IsNotNull(ui.Photo); }
            finally { Object.DestroyImmediate(ui.gameObject); }
        }

        [Test]
        public void FontsCoverEveryHarvestedGlyph()
        {
            var T = M1Fixture.Tables; var str = UiFixture.Strings();
            var set = GlyphHarvest.Collect(T, str);
            foreach (var f in new[] { UiTheme.FontRegular, UiTheme.FontBold })
            {
                Assert.IsNotNull(f, "한글 글꼴 에셋이 Resources/UI/Fonts 에 있어야 한다");
                var missing = set.Where(c => !f.HasCharacter(c, true, true)).ToList();
                Assert.That(missing, Is.Empty, f.name + " 에 없는 글자: " + new string(missing.ToArray()));
            }
        }

        [TestCase(320, 568), TestCase(360, 640), TestCase(393, 852), TestCase(768, 1024), TestCase(1024, 768)]
        public void EveryScreenFitsWithoutHorizontalOverflowOrSmallTouchTargets(int dpW, int dpH)
        {
            var ui = Make(dpW, dpH, 6, out var rig);
            try
            {
                var screens = new List<System.Action>
                {
                    () => ui.Ctl.Open(PanelMode.Mail), () => ui.Ctl.Open(PanelMode.Dex), () => { ui.Ctl.Open(PanelMode.Dex); ui.Ctl.Act("dex:sea"); },
                    () => { ui.Ctl.Open(PanelMode.Growth); ui.Ctl.Act("rolebox"); }, () => { ui.Ctl.Open(PanelMode.Growth); ui.Ctl.Act("grow:offer"); },
                    () => { ui.Ctl.Open(PanelMode.Deco); ui.Ctl.Act("zone:front"); }, () => { ui.Ctl.Open(PanelMode.Deco); ui.Ctl.Act("dtab:skin"); },
                    () => { ui.Ctl.Open(PanelMode.Shop); ui.Ctl.Act("adtoggle"); }, () => { ui.Ctl.Open(PanelMode.Growth); ui.Ctl.Act("gear"); ui.Ctl.Act("setmore"); },
                };
                int n = 0;
                foreach (var open in screens)
                {
                    ui.Ctl.Close(); open(); ui.Panel.Refresh(false); Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(ui.CanvasRect);
                    var canvas = ui.CanvasRect; var vp = ui.Panel.ViewportRect; var corners = new Vector3[4];
                    Rect R(RectTransform r) { r.GetWorldCorners(corners); var a = canvas.InverseTransformPoint(corners[0]); var b = canvas.InverseTransformPoint(corners[2]); return Rect.MinMaxRect(a.x, a.y, b.x, b.y); }
                    var vpr = R(vp);
                    foreach (var g in ui.Panel.Content.GetComponentsInChildren<Graphic>(false))
                    {
                        var rr = R(g.rectTransform);
                        if (rr.width > 0.5f) Assert.IsTrue(rr.xMin >= vpr.xMin - 0.6f && rr.xMax <= vpr.xMax + 0.6f, $"{dpW}x{dpH} 화면 {n}: 가로 넘침 {g.name} {rr.xMin}..{rr.xMax} / {vpr.xMin}..{vpr.xMax}");
                    }
                    foreach (var t in ui.Panel.Content.GetComponentsInChildren<TMP_Text>(false))
                    {
                        if (string.IsNullOrEmpty(t.text)) continue; t.ForceMeshUpdate();
                        Assert.That(t.preferredHeight, Is.LessThanOrEqualTo(R(t.rectTransform).height + 1.5f), $"{dpW}x{dpH} 화면 {n}: 글이 칸보다 큼 '{t.text}'");
                    }
                    foreach (var b in ui.Panel.GetComponentsInChildren<Button>(false))
                    {
                        if (!b.interactable || b.name == "dim" || b.name.StartsWith("cell:")) continue;
                        var rr = R((RectTransform)b.transform);
                        if (b.transform.IsChildOf(ui.Panel.Content) && (rr.yMax < vpr.yMin || rr.yMin > vpr.yMax)) continue;
                        Assert.That(Mathf.Min(rr.width, rr.height), Is.GreaterThanOrEqualTo(ScreenLayout.MinTouch - 0.6f), $"{dpW}x{dpH} 화면 {n}: 터치 크기 {b.name}");
                    }
                    var cr = ui.Panel.CardRect; var sa = ui.Safe; float cx0 = cr.anchoredPosition.x, cy0 = -cr.anchoredPosition.y;
                    Assert.IsTrue(cx0 >= sa.L - 0.5f && cx0 + cr.sizeDelta.x <= UiRoot.CanvasW - sa.R + 0.5f, $"{dpW}x{dpH}: 카드 가로가 안전 영역 밖");
                    Assert.IsTrue(cy0 >= sa.T - 0.5f && cy0 + cr.sizeDelta.y <= UiRoot.CanvasH - sa.B + 0.5f, $"{dpW}x{dpH}: 카드 세로가 안전 영역 밖");
                    n++;
                }
                Assert.That(n, Is.EqualTo(screens.Count));
            }
            finally { Object.DestroyImmediate(ui.gameObject); }
        }

        [Test]
        public void ViewLayerRoutesInputToCoreOnly()
        {
            var ui = Make(360, 640, 4, out var rig);
            try
            {
                ui.Ctl.Open(PanelMode.Growth); ui.Panel.Refresh(false);
                var focusLens = ui.Panel.GetComponentsInChildren<Button>(false).First(b => b.name == "btn:focus:lens");
                focusLens.onClick.Invoke();
                Assert.That(rig.Ctx.S.Focus, Is.EqualTo("lens"), "단추 → 컨트롤러 → Core 상태");
                ui.Panel.GetComponentsInChildren<Button>(false).First(b => b.name == "tab:mail").onClick.Invoke();
                Assert.That(ui.Ctl.Mode, Is.EqualTo(PanelMode.Mail));
                ui.Panel.Close.onClick.Invoke(); Assert.That(ui.Ctl.Mode, Is.EqualTo(PanelMode.None)); Assert.IsFalse(ui.Panel.gameObject.activeSelf);
                ui.ShowToast("진주을(를) 받았어요", "부제"); Assert.IsTrue(ui.ToastState.Visible); Assert.That(ui.ToastState.Title, Is.EqualTo("진주를 받았어요"));
            }
            finally { Object.DestroyImmediate(ui.gameObject); }
        }
    }
}
