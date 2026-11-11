using System;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>첫 실행 안내(원본 #hint): 위에서 34% 쯤의 작은 카드.</summary>
    public sealed class HintView : MonoBehaviour
    {
        public RectTransform Box;
        public static HintView Create(Transform parent, UiStrings s)
        {
            var root = Kit.Rect("Hint", parent); Kit.Stretch(root); var h = root.gameObject.AddComponent<HintView>();
            var f = Kit.Frame(root, "hud", null, "hint box"); f.raycastTarget = false; h.Box = f.rectTransform; f.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var v = f.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var dl = f.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(4, 4, 4, 5); dl.SpacingDots = 1; dl.ApplyDot(UiRoot.CurrentDot);
            Kit.Text(f.transform, s.Get("hint.1"), 15f, UiTheme.Hex("#f2f5ff"), false, TextAlignmentOptions.Center);
            for (int i = 2; i <= 4; i++) Kit.Text(f.transform, s.Get("hint." + i), 13f, UiTheme.Hex("#d3def5"), false, TextAlignmentOptions.Center);
            h.Box.anchorMin = h.Box.anchorMax = new Vector2(0.5f, 1); h.Box.pivot = new Vector2(0.5f, 1);
            root.gameObject.SetActive(false);
            return h;
        }
        public void Layout(float cw, float ch) { Box.sizeDelta = new Vector2(Mathf.Min(cw - 24f, 300f), 10); Box.anchoredPosition = new Vector2(0, -ch * 0.34f); }
    }
}
