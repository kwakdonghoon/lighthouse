using System;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>타이틀: 어두운 바다 위의 금빛 글자. 누르면 사라진다(원본 #title). 블록 글자 그림·별빛 장면은 M4 연결 때(범위 밖).</summary>
    public sealed class TitleView : MonoBehaviour
    {
        public Image Bg; public TMP_Text Name, Tagline, Day; public Button Tap; public CanvasGroup Group; public RectTransform Block;
        public static TitleView Create(Transform parent)
        {
            var root = Kit.Rect("Title", parent); Kit.Stretch(root); var t = root.gameObject.AddComponent<TitleView>();
            t.Bg = Kit.Solid(root, UiTheme.Hex("#050a1c"), "bg"); Kit.Stretch(t.Bg.rectTransform); t.Bg.raycastTarget = true;
            t.Group = root.gameObject.AddComponent<CanvasGroup>();
            t.Tap = t.Bg.gameObject.AddComponent<Button>(); t.Tap.targetGraphic = t.Bg; t.Tap.transition = Selectable.Transition.None;
            t.Block = Kit.Rect("block", root); Kit.Anchor(t.Block, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(0, 220));
            var v = t.Block.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.MiddleCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.spacing = 10; v.padding = new RectOffset(16, 16, 0, 0);
            t.Name = Kit.Text(t.Block, "", 60f, UiTheme.Hex("#ffd866"), true, TextAlignmentOptions.Center, "name"); t.Name.outlineWidth = 0.28f; t.Name.outlineColor = new Color32(58, 42, 8, 255); t.Name.characterSpacing = 8f;
            t.Tagline = Kit.Text(t.Block, "", 17f, new Color(0.96f, 0.94f, 0.82f, 0.9f), false, TextAlignmentOptions.Center, "tagline");
            t.Day = Kit.Text(root, "", 14f, new Color(0.87f, 0.89f, 0.96f, 0.7f), false, TextAlignmentOptions.Center, "day");
            var dr = t.Day.rectTransform; dr.anchorMin = new Vector2(0, 0); dr.anchorMax = new Vector2(1, 0); dr.pivot = new Vector2(0.5f, 0); dr.anchoredPosition = new Vector2(0, 56); dr.sizeDelta = new Vector2(-32, 30);
            return t;
        }
        public void Apply(UiStrings s, string dayText) { Name.text = UiText.Display(s.Get("title.name")); Tagline.text = UiText.Display(s.Get("title.tagline")); Day.text = UiText.Display(dayText); }
        public void Layout(Insets sa) { var dr = Day.rectTransform; dr.anchoredPosition = new Vector2(0, 40 + sa.B); }
    }
}
