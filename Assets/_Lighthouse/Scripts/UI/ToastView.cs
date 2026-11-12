using System;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>알림 토스트(원본 #toast): 가운데 위쪽에 5초.</summary>
    public sealed class ToastView : MonoBehaviour
    {
        public RectTransform Box; public TMP_Text Title, Sub; public CanvasGroup Group;
        public static ToastView Create(Transform parent)
        {
            var root = Kit.Rect("Toast", parent); Kit.Stretch(root); var t = root.gameObject.AddComponent<ToastView>();
            var f = Kit.Frame(root, "pop", null, "toast box"); f.raycastTarget = false; t.Box = f.rectTransform; t.Group = f.gameObject.AddComponent<CanvasGroup>(); t.Group.blocksRaycasts = false; t.Group.interactable = false;
            var v = f.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            var dl = f.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(8, 3, 8, 5); dl.ApplyDot(UiRoot.CurrentDot);
            f.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize; f.gameObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            t.Title = Kit.Text(f.transform, "", 15f, UiTheme.Teal2, true, TextAlignmentOptions.Center, "title"); t.Sub = Kit.Text(f.transform, "", 13f, UiTheme.Soft, false, TextAlignmentOptions.Center, "sub");
            var le = t.Title.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = -1; t.Title.enableWordWrapping = true;
            t.Box.anchorMin = t.Box.anchorMax = new Vector2(0.5f, 1); t.Box.pivot = new Vector2(0.5f, 1);
            t.gameObject.SetActive(false);
            return t;
        }
        public void Layout(float ch) { Box.anchoredPosition = new Vector2(0, -ScreenLayout.ToastTop(ch)); }
        public void Apply(ToastModel m, long now)
        {
            bool vis = m.Visible; gameObject.SetActive(vis); if (!vis) return;
            Title.text = UiText.Display(m.Title); Sub.text = UiText.Display(m.Sub ?? ""); Sub.gameObject.SetActive(!string.IsNullOrEmpty(m.Sub));
            Group.alpha = m.Alpha(now);
            // 원본 #toast: max-width calc(100vw - 16px), word-break keep-all → 긴 제목·부제는 줄을 바꾸고 화면(캔버스) 폭 밖으로 나가지 않는다 (콘텐츠 검토 F-01~F-04)
            float maxW = ScreenLayout.ToastTextMaxW(UiRoot.CanvasW, UiRoot.CurrentDot);
            FitWidth(Title, maxW, 120f); FitWidth(Sub, maxW, 0f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(Box);
        }

        static void FitWidth(TMP_Text t, float maxW, float minW)
        {
            var le = t.GetComponent<LayoutElement>(); if (le == null) le = t.gameObject.AddComponent<LayoutElement>();
            t.enableWordWrapping = true;
            le.preferredWidth = Mathf.Min(maxW, Mathf.Max(minW, t.GetPreferredValues(t.text).x + 1f));     // +1: 부동소수 오차로 한 줄 글이 줄바꿈되지 않게 // 한 줄에 들어가면 한 줄 폭, 넘으면 한도 폭(줄바꿈)
        }
    }
}
