using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>도트 한 칸의 크기(캔버스 단위)에 맞춰 다시 계산해야 하는 컴포넌트가 구현한다 (설계서 2-4 DotScale).</summary>
    public interface IDotScaled { void ApplyDot(float dot); }

    /// <summary>화면 만들기 도우미(코드로 UI 를 쌓는다. 프리팹 저장 때도 같은 코드를 쓴다).</summary>
    public static class Kit
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform r, float l = 0, float t = 0, float rr = 0, float b = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rr, -t);
        }

        public static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            r.anchorMin = min; r.anchorMax = max; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
        }

        /// <summary>틀 이미지(9-slice). dot 이 곧 도트 크기.</summary>
        public static Image Frame(Transform parent, string frame, Color? tint = null, string name = null)
        {
            var r = Rect(name ?? "frame:" + frame, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Frame(frame); img.type = Image.Type.Sliced; img.color = tint ?? Color.white; img.raycastTarget = true;
            img.pixelsPerUnitMultiplier = 1f / UiRoot.CurrentDot;
            var d = r.gameObject.AddComponent<DotImage>(); d.Img = img;
            return img;
        }

        public static Image Solid(Transform parent, Color c, string name = "solid")
        {
            var r = Rect(name, parent); var img = r.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.White1; img.color = c; img.raycastTarget = false; return img;
        }

        /// <summary>배지(붉은 알약)에 글자를 넣는다. 빈 글이면 숨기고, 있으면 글자 폭에 맞춰 늘인다(원본 min-width 11칸·높이 11칸). 글자 칸은 처음 쓸 때 만든다(옛 프리팹에도 통한다).</summary>
        public static void SetBadge(GameObject badge, string text)
        {
            bool on = !string.IsNullOrEmpty(text); badge.SetActive(on); if (!on) return;
            var t = badge.transform.Find("t") != null ? badge.transform.Find("t").GetComponent<TMP_Text>() : null;
            if (t == null) { t = Text(badge.transform, "", 10f, Color.white, true, TextAlignmentOptions.Center, "t"); t.enableWordWrapping = false; Stretch(t.rectTransform); }
            t.text = text; var rt = (RectTransform)badge.transform;
            rt.sizeDelta = new Vector2(Mathf.Max(14f, t.GetPreferredValues(text).x + 6f), 14f);
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, string name = "text")
        {
            var r = Rect(name, parent); var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = bold ? UiTheme.FontBold : UiTheme.FontRegular; t.fontSize = size; t.color = color; t.alignment = align;
            t.enableWordWrapping = true; t.overflowMode = TextOverflowModes.Overflow; t.raycastTarget = false; t.richText = false; t.extraPadding = false;
            t.lineSpacing = 0f; t.margin = Vector4.zero;
            t.text = Lighthouse.UI.Model.UiText.Display(text ?? "");
            return t;
        }

        public static VerticalLayoutGroup VBox(Transform parent, string name, Vector4 padDots, float spacingDots, bool expandW = true, bool fit = false)
        {
            var r = Rect(name, parent); var g = r.gameObject.AddComponent<VerticalLayoutGroup>();
            g.childAlignment = TextAnchor.UpperLeft; g.childControlWidth = true; g.childControlHeight = true; g.childForceExpandWidth = expandW; g.childForceExpandHeight = false;
            var d = r.gameObject.AddComponent<DotLayout>(); d.Group = g; d.PadDots = padDots; d.SpacingDots = spacingDots; d.ApplyDot(UiRoot.CurrentDot);
            if (fit) { var f = r.gameObject.AddComponent<ContentSizeFitter>(); f.verticalFit = ContentSizeFitter.FitMode.PreferredSize; }
            return g;
        }

        public static HorizontalLayoutGroup HBox(Transform parent, string name, Vector4 padDots, float spacingDots, TextAnchor align = TextAnchor.MiddleLeft, bool expandW = false)
        {
            var r = Rect(name, parent); var g = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.childAlignment = align; g.childControlWidth = true; g.childControlHeight = true; g.childForceExpandWidth = expandW; g.childForceExpandHeight = false;
            var d = r.gameObject.AddComponent<DotLayout>(); d.Group = g; d.PadDots = padDots; d.SpacingDots = spacingDots; d.ApplyDot(UiRoot.CurrentDot);
            return g;
        }

        public static LayoutElement Elem(GameObject go, float minW = -1, float minH = -1, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1)
        {
            var e = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (minW >= 0) e.minWidth = minW; if (minH >= 0) e.minHeight = minH; if (prefW >= 0) e.preferredWidth = prefW; if (prefH >= 0) e.preferredHeight = prefH;
            if (flexW >= 0) e.flexibleWidth = flexW; if (flexH >= 0) e.flexibleHeight = flexH;
            return e;
        }

        public static Image IconImage(Transform parent, string iconId, float px = 8, int mult = 2, bool dim = false)
        {
            var sp = UiTheme.Icon(iconId);
            var r = Rect("icon:" + iconId, parent); var img = r.gameObject.AddComponent<Image>();
            img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false; img.color = dim ? new Color(1, 1, 1, 0.35f) : Color.white;
            r.gameObject.AddComponent<UiIcon>().Id = iconId;
            var e = r.gameObject.AddComponent<LayoutElement>(); var ds = r.gameObject.AddComponent<DotSize>(); ds.Elem = e; ds.Px = sp != null ? Mathf.Max(sp.texture.width, sp.texture.height) : (int)px; ds.Mult = ds.Px <= 10 ? 2 : 1; ds.ApplyDot(UiRoot.CurrentDot);
            return img;
        }

        /// <summary>눌리는 단추: 틀 + 글자. pressed 틀이 있으면 누를 때 바뀐다.</summary>
        public static Button FrameButton(Transform parent, string frame, string pressed, string label, float fontSize, Color textColor, UnityAction onClick, string name = "button", bool bold = true, float minH = 44f, float padX = 5f)
        {
            var img = Frame(parent, frame, null, name);
            var btn = img.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
            if (!string.IsNullOrEmpty(pressed))
            {
                btn.transition = Selectable.Transition.SpriteSwap;
                var st = new SpriteState { pressedSprite = UiTheme.Frame(pressed), highlightedSprite = img.sprite, selectedSprite = img.sprite }; btn.spriteState = st;
            }
            else btn.transition = Selectable.Transition.None;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var box = img.gameObject.AddComponent<VerticalLayoutGroup>();
            box.childAlignment = TextAnchor.MiddleCenter; box.childControlWidth = true; box.childControlHeight = true; box.childForceExpandWidth = true; box.childForceExpandHeight = false;
            var d = img.gameObject.AddComponent<DotLayout>(); d.Group = box; d.PadDots = new Vector4(padX, 3, padX, 5); d.SpacingDots = 0; d.ApplyDot(UiRoot.CurrentDot);
            var le = img.gameObject.AddComponent<LayoutElement>(); le.minHeight = minH;
            var t = Text(img.transform, label, fontSize, textColor, bold, TextAlignmentOptions.Center, "label");
            return btn;
        }

        public static TMP_Text LabelOf(Button b) => b.transform.Find("label").GetComponent<TMP_Text>();
    }
}
