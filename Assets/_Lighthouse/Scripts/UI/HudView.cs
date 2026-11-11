using System.Linq;
using System;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>위쪽 가운데 표시줄(등대 단계·안내한 배 수·다음 단계 막대·날씨 예보)과 오른쪽 위 메뉴 단추.</summary>
    public sealed class HudView : MonoBehaviour
    {
        public RectTransform Pill, ForecastRect, MenuRect; public TMP_Text Stage, Count, CountUnit, CountTail, Label, Forecast; public Image PillImg, ForecastImg, BarFill, MenuBadge; public Button Menu; public Image Bar;
        string key = "";

        public static HudView Create(Transform parent)
        {
            var root = Kit.Rect("Hud", parent); Kit.Stretch(root);
            var h = root.gameObject.AddComponent<HudView>();
            var col = Kit.VBox(root, "hud column", Vector4.zero, 2, false); var colRt = col.GetComponent<RectTransform>(); col.childAlignment = TextAnchor.UpperCenter; col.childForceExpandWidth = false;
            colRt.anchorMin = colRt.anchorMax = new Vector2(0.5f, 1); colRt.pivot = new Vector2(0.5f, 1); colRt.sizeDelta = new Vector2(190, 10);
            colRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            h.Pill = colRt;
            h.PillImg = Kit.Frame(col.transform, "hud", null, "pill"); h.PillImg.raycastTarget = false;
            Kit.Elem(h.PillImg.gameObject, minW: 160f, prefW: 190f);
            var v = h.PillImg.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var dl = h.PillImg.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(7, 3, 7, 4); dl.SpacingDots = 1; dl.ApplyDot(UiRoot.CurrentDot);
            h.Stage = Kit.Text(h.PillImg.transform, "", 13f, UiTheme.HudSub, false, TextAlignmentOptions.Center, "stage");
            var line = Kit.HBox(h.PillImg.transform, "count line", Vector4.zero, 2, TextAnchor.LowerCenter, false);
            var t1 = Kit.Text(line.transform, "", 15.5f, UiTheme.HudText, false, TextAlignmentOptions.Bottom, "count label");
            h.Count = Kit.Text(line.transform, "0", 21.5f, UiTheme.HudGold, true, TextAlignmentOptions.Bottom, "count"); h.CountUnit = t1;
            h.CountTail = Kit.Text(line.transform, "", 15.5f, UiTheme.HudText, false, TextAlignmentOptions.Bottom, "tail");
            h.Bar = Kit.Solid(h.PillImg.transform, UiTheme.Hex("#2e4a8a"), "bar"); var bl = h.Bar.gameObject.AddComponent<LayoutElement>(); var bdl = h.Bar.gameObject.AddComponent<DotLayout>(); bdl.Elem = bl; bdl.MinHeightDots = 3; bdl.ApplyDot(UiRoot.CurrentDot);
            h.BarFill = Kit.Solid(h.Bar.transform, UiTheme.Gold, "fill"); var fr = h.BarFill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0, 1); fr.offsetMin = fr.offsetMax = Vector2.zero;
            h.Label = Kit.Text(h.PillImg.transform, "", 13f, UiTheme.HudSub, false, TextAlignmentOptions.Top, "label");
            h.ForecastImg = Kit.Frame(col.transform, "hud", null, "forecast"); h.ForecastImg.raycastTarget = false;
            var fv = h.ForecastImg.gameObject.AddComponent<HorizontalLayoutGroup>(); fv.childAlignment = TextAnchor.MiddleCenter; fv.childControlWidth = fv.childControlHeight = true; fv.childForceExpandWidth = false;
            var fdl = h.ForecastImg.gameObject.AddComponent<DotLayout>(); fdl.Group = fv; fdl.PadDots = new Vector4(7, 3, 7, 3); fdl.ApplyDot(UiRoot.CurrentDot);
            h.Forecast = Kit.Text(h.ForecastImg.transform, "", 14f, UiTheme.Hex("#cfe8ff"), true, TextAlignmentOptions.Center, "forecast text");
            h.ForecastRect = h.ForecastImg.rectTransform; h.ForecastImg.gameObject.SetActive(false);

            var mb = Kit.FrameButton(root, "ib", "ib_p", "", 12f, Color.white, null, "menu button", true, 44f);
            UnityEngine.Object.DestroyImmediate(mb.transform.Find("label").gameObject);
            var ic = Kit.IconImage(mb.transform, "menu", 8, 1); ic.GetComponent<LayoutElement>().flexibleWidth = 0;
            h.Menu = mb; h.MenuRect = (RectTransform)mb.transform; mb.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            h.MenuRect.anchorMin = h.MenuRect.anchorMax = new Vector2(1, 1); h.MenuRect.pivot = new Vector2(1, 1); h.MenuRect.sizeDelta = new Vector2(48, 48);
            h.MenuBadge = Kit.Solid(mb.transform, UiTheme.Red, "badge"); var br = h.MenuBadge.rectTransform; br.anchorMin = br.anchorMax = new Vector2(1, 1); br.sizeDelta = new Vector2(12, 12); br.anchoredPosition = new Vector2(-4, -4); h.MenuBadge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true; h.MenuBadge.gameObject.SetActive(false);
            return h;
        }

        public void Layout(Insets sa)
        {
            float top = ScreenLayout.HudTop(sa);
            Pill.anchoredPosition = new Vector2(0, -top);
            MenuRect.anchoredPosition = new Vector2(-(8 + sa.R), -(8 + sa.T));
        }

        public void Apply(HudInfo i, UiStrings s, bool menuBadge)
        {
            string k = i.StageText + "|" + i.Count + "|" + Mathf.RoundToInt(i.Pct) + "|" + i.Label + "|" + i.Forecast + "|" + i.ForecastKind + "|" + menuBadge;
            if (k == key) return; key = k;
            Stage.text = UiText.Display(i.StageText); Count.text = i.Count.ToString("0");
            var unit = s.Get("hud.count").Split(new[] { "{0}" }, StringSplitOptions.None);
            CountUnit.text = UiText.Display(unit[0]); CountTail.text = UiText.Display(unit.Length > 1 ? unit[1] : "");
            BarFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(i.Pct / 100f), 1);
            Label.text = UiText.Display(string.Join(" · ", i.LabelParts.Select(x => x.Replace((char)32, (char)160)))); Label.gameObject.SetActive(!string.IsNullOrEmpty(i.Label));
            bool fc = !string.IsNullOrEmpty(i.Forecast); ForecastImg.gameObject.SetActive(fc);
            if (fc)
            {
                Forecast.text = UiText.Display(i.Forecast);
                string f = i.ForecastKind == "fog" ? "hud_fog" : i.ForecastKind == "rain" ? "hud_rain" : i.ForecastKind == "rainbow" ? "hud_rainbow" : "hud";
                ForecastImg.sprite = UiTheme.Frame(f);
            }
            MenuBadge.gameObject.SetActive(menuBadge);
            LayoutRebuilder.ForceRebuildLayoutImmediate(Pill);
        }
    }
}
