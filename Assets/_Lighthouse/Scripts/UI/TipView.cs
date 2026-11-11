using System;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>온보딩 안내 카드(원본 #tip): 말풍선 모양 카드 + "안내 끄기".</summary>
    public sealed class TipView : MonoBehaviour
    {
        public RectTransform Box, Tail; public TMP_Text Text; public Image Icon, TailImg; public Button Body, Off;
        public static TipView Create(Transform parent)
        {
            var root = Kit.Rect("Tip", parent); Kit.Stretch(root); var t = root.gameObject.AddComponent<TipView>();
            var f = Kit.Frame(root, "pop", null, "tip box"); f.raycastTarget = false; t.Box = f.rectTransform;
            var v = f.gameObject.AddComponent<VerticalLayoutGroup>(); v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var dl = f.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(3, 2, 3, 4); dl.ApplyDot(UiRoot.CurrentDot);
            f.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var bodyRect = Kit.Rect("body", f.transform); var bi = bodyRect.gameObject.AddComponent<Image>(); bi.color = new Color(0, 0, 0, 0); t.Body = bodyRect.gameObject.AddComponent<Button>(); t.Body.targetGraphic = bi; t.Body.transition = Selectable.Transition.None;
            var hb = bodyRect.gameObject.AddComponent<HorizontalLayoutGroup>(); hb.childAlignment = TextAnchor.MiddleLeft; hb.childControlWidth = hb.childControlHeight = true; hb.childForceExpandWidth = false; hb.spacing = 8; hb.padding = new RectOffset(8, 8, 6, 2);
            Kit.Elem(bodyRect.gameObject, minH: 44f);
            t.Icon = Kit.IconImage(bodyRect, "star"); t.Text = Kit.Text(bodyRect, "", 14.5f, UiTheme.Ink, true, TextAlignmentOptions.MidlineLeft, "text"); Kit.Elem(t.Text.gameObject, flexW: 1);
            t.Off = Kit.FrameButton(f.transform, "pop", null, "", 13f, UiTheme.Soft, null, "off", false, 44f);
            var offImg = t.Off.GetComponent<Image>(); offImg.color = new Color(1, 1, 1, 0); var ol = Kit.LabelOf(t.Off); ol.fontStyle = FontStyles.Underline; Kit.Elem(t.Off.gameObject, minW: 96f, minH: 44f, flexW: 0);
            var vg = t.Off.GetComponent<VerticalLayoutGroup>(); vg.childAlignment = TextAnchor.MiddleRight;
            t.Tail = Kit.Rect("tail", t.Box); t.TailImg = t.Tail.gameObject.AddComponent<Image>(); t.TailImg.raycastTarget = false; t.TailImg.preserveAspect = true; t.Tail.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            t.Box.anchorMin = t.Box.anchorMax = new Vector2(0, 1); t.Box.pivot = new Vector2(0, 1);
            t.gameObject.SetActive(false);
            return t;
        }

        public void Show(TipInfo tip, UiStrings s, float cx, float top, bool up, float canvasW)
        {
            gameObject.SetActive(true);
            Text.text = UiText.Display(tip.Text); Icon.sprite = UiTheme.Icon(tip.Icon); Kit.LabelOf(Off).text = UiText.Display(s.Get("tip.off"));
            float w = Mathf.Min(300f, canvasW * 0.84f);
            Box.sizeDelta = new Vector2(w, 10); Text.rectTransform.sizeDelta = new Vector2(w - 90, 0);
            LayoutRebuilder.ForceRebuildLayoutImmediate(Box);
            float left = Mathf.Clamp(cx - w / 2, 8, canvasW - w - 8);
            Box.anchoredPosition = new Vector2(left, -top);
            TailImg.sprite = UiTheme.Frame(up ? "tail_up" : "tail_dn"); var d = UiRoot.CurrentDot; var sp = TailImg.sprite;
            float tw = sp.rect.width * d, th = sp.rect.height * d;
            Tail.anchorMin = Tail.anchorMax = new Vector2(0, up ? 1 : 0); Tail.pivot = new Vector2(0.5f, up ? 0 : 1); Tail.sizeDelta = new Vector2(tw, th);
            Tail.anchoredPosition = new Vector2(Mathf.Clamp(cx - left, 20, w - 20), 0);
        }
        public void Hide() { gameObject.SetActive(false); }
    }
}
