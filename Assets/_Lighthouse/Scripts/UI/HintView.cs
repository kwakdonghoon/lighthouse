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
        public RectTransform Box; CanvasGroup grp; float playAt = -1f;
        public const float Delay = IntroTiming.HintDelay, Life = IntroTiming.HintLife;           // 원본 CSS animation: hint 10s ease 1.2s both
        public static HintView Create(Transform parent, UiStrings s)
        {
            var root = Kit.Rect("Hint", parent); Kit.Stretch(root); var h = root.gameObject.AddComponent<HintView>();
            var f = Kit.Frame(root, "hud", null, "hint box"); f.raycastTarget = false; h.Box = f.rectTransform; f.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var v = f.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var dl = f.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(4, 4, 4, 5); dl.SpacingDots = 1; dl.ApplyDot(UiRoot.CurrentDot);
            Kit.Text(f.transform, s.Get("hint.1"), 15f, UiTheme.Hex("#f2f5ff"), false, TextAlignmentOptions.Center);
            for (int i = 2; i <= 4; i++) Kit.Text(f.transform, s.Get("hint." + i), 13f, UiTheme.Hex("#d3def5"), false, TextAlignmentOptions.Center);
            h.grp = f.gameObject.AddComponent<CanvasGroup>(); h.grp.blocksRaycasts = false; h.grp.interactable = false;
            h.Box.anchorMin = h.Box.anchorMax = new Vector2(0.5f, 1); h.Box.pivot = new Vector2(0.5f, 1);
            root.gameObject.SetActive(false);
            return h;
        }
        /// <summary>첫 실행 안내 한 줄의 흐름(원본 @keyframes hint 5·517행): 1.2초 기다렸다가 10초 동안 0% 투명 → 15% 불투명 → 78% 불투명 → 100% 투명(ease).</summary>
        public static float AlphaAt(float sec) => IntroTiming.HintAlpha(sec);
        /// <summary>안내를 시작한다(지금부터 11.2초 동안 보이는 구간이 흐른다).</summary>
        public void Play() { playAt = Time.unscaledTime; gameObject.SetActive(true); Tick(); }
        void Tick() { if (playAt < 0) return; float t = Time.unscaledTime - playAt; if (grp == null && Box != null) grp = Box.GetComponent<CanvasGroup>() ?? Box.gameObject.AddComponent<CanvasGroup>(); if (grp != null) { grp.blocksRaycasts = false; grp.alpha = AlphaAt(t); } if (t > Delay + Life) { playAt = -1f; gameObject.SetActive(false); } }
        void Update() { Tick(); }
        void OnDisable() { playAt = -1f; if (grp != null) grp.alpha = 1f; }

        public void Layout(float cw, float ch) { Box.sizeDelta = new Vector2(Mathf.Min(cw - 24f, 300f), 10); Box.anchoredPosition = new Vector2(0, -ch * 0.34f); }
    }
}
