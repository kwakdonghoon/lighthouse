using System;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>사진 모드 아래쪽 막대(원본 #photobar).</summary>
    public sealed class PhotoBarView : MonoBehaviour
    {
        public RectTransform Box; public TMP_Text Msg; public Button Save, Back;
        public static PhotoBarView Create(Transform parent, UiStrings s)
        {
            var root = Kit.Rect("PhotoBar", parent); Kit.Stretch(root); var p = root.gameObject.AddComponent<PhotoBarView>();
            var f = Kit.PixelBox(root, UiTheme.Hex("#070b22"), UiTheme.Hex("#151e48"), UiTheme.Hex("#3b4b8c"), Color.clear, UiRoot.CurrentDot, "bar"); f.raycastTarget = false; p.Box = f.rectTransform;      // 원본 #photobar 472행: 어두운 남색 막대
            var v = f.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.spacing = 4;
            var dl = f.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(5, 4, 5, 6); dl.ApplyDot(UiRoot.CurrentDot);
            f.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            p.Msg = Kit.Text(f.transform, s.Get("photo.hidden"), 13f, UiTheme.Hex("#cdd6f6"), true, TextAlignmentOptions.Center);
            var row = Kit.HBox(f.transform, "buttons", Vector4.zero, 3, TextAnchor.MiddleCenter, true);
            p.Save = PixelButton(row.transform, s.Get("photo.save"), "save", "#112a17", "#2f6f36", "#6cc060", "#1f4e24");                  // 원본 475행: 초록 단추
            p.Back = PixelButton(row.transform, s.Get("photo.back"), "back", "#14172b", "#3b4b8c", "#6a7ac0", "#222e66");                  // 원본 476행 .sub: 파란 단추
            p.Box.anchorMin = p.Box.anchorMax = new Vector2(0.5f, 0); p.Box.pivot = new Vector2(0.5f, 0);
            root.gameObject.SetActive(false);
            return p;
        }
        static Button PixelButton(Transform parent, string label, string name, string border, string fill, string hi, string lo)
        {
            var img = Kit.PixelBox(parent, UiTheme.Hex(border), UiTheme.Hex(fill), UiTheme.Hex(hi), UiTheme.Hex(lo), UiRoot.CurrentDot, name);
            var btn = img.gameObject.AddComponent<Button>(); btn.targetGraphic = img; btn.transition = Selectable.Transition.None;
            var box = img.gameObject.AddComponent<VerticalLayoutGroup>(); box.childAlignment = TextAnchor.MiddleCenter; box.childControlWidth = box.childControlHeight = true; box.childForceExpandWidth = true; box.childForceExpandHeight = false;
            box.padding = new RectOffset(14, 14, 0, 0);
            Kit.Elem(img.gameObject, minH: 44f, flexW: 1, minW: 100f);
            Kit.Text(img.transform, label, 15f, Color.white, true, TextAlignmentOptions.Center, "label");
            return btn;
        }

        public void Layout(float cw, Insets sa) { Box.sizeDelta = new Vector2(Mathf.Min(cw - 16f - sa.L - sa.R, 320f), 10); Box.anchoredPosition = new Vector2(0, 14 + sa.B); }
    }
}
