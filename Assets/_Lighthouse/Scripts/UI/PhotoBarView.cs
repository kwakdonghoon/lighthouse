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
            var f = Kit.Frame(root, "pop", UiTheme.Hex("#cdd6f6"), "bar"); f.raycastTarget = false; p.Box = f.rectTransform;
            var v = f.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.spacing = 4;
            var dl = f.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(5, 4, 5, 6); dl.ApplyDot(UiRoot.CurrentDot);
            f.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            p.Msg = Kit.Text(f.transform, s.Get("photo.hidden"), 13f, UiTheme.Ink, true, TextAlignmentOptions.Center);
            var row = Kit.HBox(f.transform, "buttons", Vector4.zero, 3, TextAnchor.MiddleCenter, true);
            p.Save = Kit.FrameButton(row.transform, "main", "main_p", s.Get("photo.save"), 15f, Color.white, null, "save", true, 44f); Kit.Elem(p.Save.gameObject, flexW: 1, minW: 100f);
            p.Back = Kit.FrameButton(row.transform, "btn", "btn_p", s.Get("photo.back"), 15f, UiTheme.Ink, null, "back", true, 44f); Kit.Elem(p.Back.gameObject, flexW: 1, minW: 100f);
            p.Box.anchorMin = p.Box.anchorMax = new Vector2(0.5f, 0); p.Box.pivot = new Vector2(0.5f, 0);
            root.gameObject.SetActive(false);
            return p;
        }
        public void Layout(float cw, Insets sa) { Box.sizeDelta = new Vector2(Mathf.Min(cw - 16f - sa.L - sa.R, 320f), 10); Box.anchoredPosition = new Vector2(0, 14 + sa.B); }
    }
}
