using System;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>
    /// 바다를 옮길 때 화면을 덮는 어두운 물안개(원본 switchRegion 1903행): 700ms 동안 덮이고(움직임 줄이기면 0) → 250ms 뒤 바다를 바꾸고 팝업을 닫고 → 500ms 머문 뒤 → 700ms 동안 걷히면
    /// "X에 도착했어요" 토스트. 시간표는 순수 계산(RegionFadeTiming)이라 dotnet 으로 시험하고, 여기서는 그림만 그린다.
    /// </summary>
    public sealed class RegionFogView : MonoBehaviour
    {
        public CanvasGroup Group; public TMP_Text Line1, Line2; RawImage bg;
        float startAt; RegionFadeTiming timing; bool committed, done; Action commit, arrive; Func<float> clock;

        public static RegionFogView Create(Transform parent)
        {
            var root = Kit.Rect("RegionFog", parent); Kit.Stretch(root); var f = root.gameObject.AddComponent<RegionFogView>();
            f.Group = root.gameObject.AddComponent<CanvasGroup>(); f.Group.blocksRaycasts = true; f.Group.interactable = false; f.Group.alpha = 0f;
            var go = Kit.Rect("bg", root); Kit.Stretch(go); f.bg = go.gameObject.AddComponent<RawImage>(); f.bg.raycastTarget = true; f.bg.texture = Gradient();
            var box = Kit.VBox(root, "text", Vector4.zero, 1, true); var br = (RectTransform)box.transform; br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f); br.pivot = new Vector2(0.5f, 0.5f); br.sizeDelta = new Vector2(320, 80);
            box.childAlignment = TextAnchor.MiddleCenter;
            f.Line1 = Kit.Text(box.transform, "", 14f, new Color(0.9f, 0.94f, 1f, 0.7f), false, TextAlignmentOptions.Center, "line1");
            f.Line2 = Kit.Text(box.transform, "", 22f, new Color(0.9f, 0.94f, 1f, 1f), false, TextAlignmentOptions.Center, "line2");
            root.gameObject.SetActive(false);
            return f;
        }

        /// <summary>radial-gradient(ellipse at 50% 55%, #2a4468 0%, #10203c 70%, #0a1428 100%)</summary>
        static Texture2D Gradient()
        {
            int w = 64, h = 64; var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "regionFog" };
            Color a = UiTheme.Hex("#2a4468"), b = UiTheme.Hex("#10203c"), c = UiTheme.Hex("#0a1428"); var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w - 0.5f, dy = (y + 0.5f) / h - 0.45f;                                    // 아래쪽이 y=0 이라 55% 지점이 0.45
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 0.7071f;                                                  // farthest-corner 에 가깝게 1 = 모서리
                    px[y * w + x] = r < 0.7f ? Color.Lerp(a, b, r / 0.7f) : Color.Lerp(b, c, Mathf.Clamp01((r - 0.7f) / 0.3f));
                }
            t.SetPixels(px); t.Apply(false); return t;
        }

        public bool Busy => gameObject.activeSelf && !done;

        /// <summary>시작한다. commit = 안개가 다 덮였을 때 할 일(바다 바꾸기·팝업 닫기), arrive = 걷힌 뒤 할 일(도착 토스트).</summary>
        public void Play(string regionName, string sailingText, bool calm, Action commitAction, Action arriveAction, Func<float> now = null)
        {
            Line1.text = UiText.Display(sailingText); Line2.text = UiText.Display(regionName);
            timing = new RegionFadeTiming(calm); commit = commitAction; arrive = arriveAction; clock = now ?? (() => Time.realtimeSinceStartup);
            startAt = clock(); committed = false; done = false; Group.alpha = 0f; gameObject.SetActive(true); transform.SetAsLastSibling();
        }

        void Update()
        {
            if (done) return;
            float t = clock() - startAt;
            Group.alpha = timing.Alpha(t);
            if (!committed && t >= timing.CommitAt) { committed = true; commit?.Invoke(); }
            if (t >= timing.EndAt) { done = true; gameObject.SetActive(false); arrive?.Invoke(); }
        }
    }
}
