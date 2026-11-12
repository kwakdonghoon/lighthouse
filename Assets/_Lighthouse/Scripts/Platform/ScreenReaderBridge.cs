using System;
using System.Collections.Generic;
using Lighthouse.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Lighthouse.Platform
{
    /// <summary>
    /// 스크린 리더(Android TalkBack · iOS VoiceOver) 다리. Unity 6 의 UnityEngine.Accessibility(AccessibilityHierarchy)에 화면의 단추·글을 접근성 노드로 올린다.
    /// 원본(HTML)은 aria-label 59곳으로 단추·탭·상태를 읽게 했다 — 같은 일을 uGUI 위에서 한다:
    ///  - 켜진 uGUI Button → 노드(역할 button, 누르면 그 단추를 누른 것과 같다). 라벨은 ① A11y.Labels(원본 aria-label: 아이콘만 있는 단추 — 설정·소리·닫기·메뉴 …) ② 안쪽 글 ③ 오브젝트 이름.
    ///  - 단추 안이 아닌 켜진 TMP 글 → 노드(역할 text).
    ///  - 스크린 리더가 꺼져 있으면 아무것도 하지 않는다(0.4초마다 켜졌는지만 본다 — 비용 없음). 켜져 있으면 0.4초마다 바뀐 것만 다시 만든다.
    /// 한계(실기기 미확인): 노드 좌표의 원점은 왼쪽 위로 가정했다. 열린 팝업 뒤의 HUD 단추도 함께 읽힌다(화면 단위 격리는 UI 쪽 표식이 생기면).
    /// </summary>
    public sealed class ScreenReaderBridge : MonoBehaviour
    {
        Canvas canvas; float next; string lastSig; bool active;
        public int NodeCount { get; private set; }

        
        public void Bind(Canvas c) { canvas = c; lastSig = null; }

        public static bool ScreenReaderOn { get { try { return AssistiveSupport.isScreenReaderEnabled; } catch (Exception) { return false; } } }

        void Update()
        {
            if (canvas == null) return;
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.4f;
            if (!ScreenReaderOn)
            {
                if (active) { try { AssistiveSupport.activeHierarchy = null; } catch (Exception) { } active = false; lastSig = null; NodeCount = 0; }
                return;
            }
            Rebuild();
        }

        /// <summary>지금 화면의 읽을거리(시험이 직접 부른다).</summary>
        public List<A11yItem> Collect()
        {
            var items = new List<A11yItem>();
            if (canvas == null) return items;
            var buttons = canvas.GetComponentsInChildren<Button>(false);
            var inButton = new HashSet<TMP_Text>();
            foreach (var b in buttons)
            {
                if (!b.interactable || Hidden(b.gameObject)) continue;
                string label = null;
                string key = A11y.KeyOfName(b.gameObject.name);
                if (key.Length > 0) label = A11y.LabelFor(key);
                var texts = b.GetComponentsInChildren<TMP_Text>(false);
                foreach (var t in texts) inButton.Add(t);
                if (string.IsNullOrEmpty(label))
                {
                    var parts = new List<string>(); foreach (var t in texts) { string c = A11y.Clean(t.text); if (c.Length > 0) parts.Add(c); }
                    label = parts.Count > 0 ? string.Join(", ", parts) : b.gameObject.name;
                }
                if (!Frame(b.transform as RectTransform, out var r)) continue;
                items.Add(new A11yItem { Label = label, Role = "button", X = r.x, Y = r.y, W = r.width, H = r.height, Tag = b.gameObject });
            }
            foreach (var t in canvas.GetComponentsInChildren<TMP_Text>(false))
            {
                if (inButton.Contains(t) || Hidden(t.gameObject)) continue;
                string c = A11y.Clean(t.text); if (c.Length == 0) continue;
                if (!Frame(t.rectTransform, out var r)) continue;
                items.Add(new A11yItem { Label = c, Role = "text", X = r.x, Y = r.y, W = r.width, H = r.height, Tag = t.gameObject });
            }
            return A11y.Order(items);
        }

        void Rebuild()
        {
            var items = Collect(); string sig = A11y.Signature(items);
            if (sig == lastSig && active) return;
            lastSig = sig;
            try
            {
                var h = new AccessibilityHierarchy();
                foreach (var it in items)
                {
                    var node = h.AddNode(it.Label);
                    node.frame = new Rect(it.X, it.Y, it.W, it.H);
                    if (Enum.TryParse<AccessibilityRole>(it.Role == "button" ? "Button" : "StaticText", out var role)) node.role = role;
                    if (it.Role == "button")
                    {
                        var go = it.Tag as GameObject;
                        node.invoked += () => { if (go == null || !go.activeInHierarchy) return false; ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler); return true; };
                    }
                }
                AssistiveSupport.activeHierarchy = h; active = true; NodeCount = items.Count;
            }
            catch (Exception e) { Debug.LogWarning("접근성 노드 만들기 실패: " + e.Message); active = false; }
        }

        static bool Hidden(GameObject go)
        {
            foreach (var g in go.GetComponentsInParent<CanvasGroup>(false)) if (g.alpha < 0.01f) return true;
            return false;
        }

        /// <summary>RectTransform → 화면 픽셀 사각형(원점 왼쪽 위). 화면 밖이거나 크기가 없으면 false.</summary>
        bool Frame(RectTransform rt, out Rect r)
        {
            r = default;
            if (rt == null) return false;
            var corners = new Vector3[4]; rt.GetWorldCorners(corners);
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var c in corners) { Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, c); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            if (maxX < 0 || maxY < 0 || minX > Screen.width || minY > Screen.height) return false;
            r = new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
            return r.width > 1 && r.height > 1;
        }

        void OnDisable() { if (active) { try { AssistiveSupport.activeHierarchy = null; } catch (Exception) { } active = false; } }
    }
}
