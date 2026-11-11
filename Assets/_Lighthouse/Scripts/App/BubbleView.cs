using Lighthouse.UI;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.App
{
    /// <summary>월드 말풍선(원본 #bubble): 배·관리인 머리 위에 따라붙는 짧은 말. 위치는 GameApp 이 매 프레임 캔버스 좌표로 넣는다.</summary>
    public sealed class BubbleView : MonoBehaviour
    {
        public RectTransform Box; public TMP_Text Text; public CanvasGroup Group;

        public static BubbleView Create(Transform parent)
        {
            var root = Kit.Rect("Bubble", parent); Kit.Stretch(root); var b = root.gameObject.AddComponent<BubbleView>();
            var f = Kit.Frame(root, "pop", null, "bubble box"); f.raycastTarget = false; b.Box = f.rectTransform;
            b.Group = f.gameObject.AddComponent<CanvasGroup>(); b.Group.blocksRaycasts = false; b.Group.interactable = false;
            var v = f.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.MiddleCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            var dl = f.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(5, 3, 5, 5); dl.ApplyDot(UiRoot.CurrentDot);
            var fit = f.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            b.Text = Kit.Text(f.transform, "", 14f, UiTheme.Ink, true, TextAlignmentOptions.Center, "text"); b.Text.enableWordWrapping = true;
            b.Box.anchorMin = b.Box.anchorMax = new Vector2(0, 1); b.Box.pivot = new Vector2(0.5f, 0);
            b.gameObject.SetActive(false);
            return b;
        }

        string shown;
        /// <summary>글을 바꾼다(같으면 다시 계산하지 않는다).</summary>
        public void SetText(string text)
        {
            if (shown == text) return; shown = text;
            Text.text = UiText.Display(text);
            var le = Text.GetComponent<LayoutElement>() ?? Text.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = Mathf.Min(UiRoot.CanvasW - 48f, Mathf.Min(260f, Mathf.Max(60f, Text.GetPreferredValues(Text.text).x)));
            LayoutRebuilder.ForceRebuildLayoutImmediate(Box);
        }

        /// <summary>bottom-center 를 (cx, by)(캔버스 단위, 왼쪽 위 원점)로: 화면 가장자리에서는 안쪽으로 밀어 넣는다.</summary>
        public void Place(float cx, float by, float canvasW, float canvasH, float topLimit)
        {
            float half = Box.rect.width / 2f; cx = Mathf.Clamp(cx, half + 8f, canvasW - half - 8f);
            float h = Box.rect.height; by = Mathf.Clamp(by, topLimit + h + 4f, canvasH - 4f);
            Box.anchoredPosition = new Vector2(cx, -by);
        }

        public void Hide() { shown = null; gameObject.SetActive(false); }
    }
}
