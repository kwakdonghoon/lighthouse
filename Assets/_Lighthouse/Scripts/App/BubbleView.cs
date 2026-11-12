using System.Collections.Generic;
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
        public RectTransform Box, Tail; public TMP_Text Text; public CanvasGroup Group; Image tailImg;
        public BubbleSpot LastSpot { get; private set; }

        public static BubbleView Create(Transform parent)
        {
            var root = Kit.Rect("Bubble", parent); Kit.Stretch(root); var b = root.gameObject.AddComponent<BubbleView>();
            var f = Kit.Frame(root, "pop", null, "bubble box"); f.raycastTarget = false; b.Box = f.rectTransform;
            b.Group = f.gameObject.AddComponent<CanvasGroup>(); b.Group.blocksRaycasts = false; b.Group.interactable = false;
            var v = f.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.MiddleCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            var dl = f.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(5, 3, 5, 5); dl.ApplyDot(UiRoot.CurrentDot);
            var fit = f.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            b.Text = Kit.Text(f.transform, "", 14f, UiTheme.Ink, true, TextAlignmentOptions.Center, "text"); b.Text.textWrappingMode = TextWrappingModes.Normal;
            b.Tail = Kit.Rect("tail", b.Box); b.tailImg = b.Tail.gameObject.AddComponent<Image>(); b.tailImg.raycastTarget = false; b.tailImg.preserveAspect = true; b.Tail.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            b.tailImg.sprite = UiTheme.Frame("tail_dn");                                                       // 말풍선 꼬리(원본 ::after 490행): 풍선 바로 아래, 대상 쪽을 가리킨다
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

        /// <summary>
        /// bottom-center 를 (x, by)(캔버스 단위, 왼쪽 위 원점)로: 화면 가장자리에서는 안쪽으로 밀어 넣고(꼬리는 대상을 계속 가리킨다),
        /// 표시 줄·메뉴·안내 카드·토스트(uis)와 겹치면 위·아래로 비킨다. 비킬 자리가 없으면 잠깐 감춘다(원본 5306~5324행, BubbleLayout).
        /// </summary>
        public void Place(float x, float by, float canvasW, float canvasH, IList<UiRectF> uis)
        {
            var sp = BubbleLayout.Resolve(x, by, Box.rect.width, Box.rect.height, canvasW, canvasH, uis); LastSpot = sp;
            Group.alpha = sp.Hidden ? 0f : 1f;                                                                  // 원본 visibility: hidden
            Box.anchoredPosition = new Vector2(sp.Cx, -sp.Bottom);
            float d = UiRoot.CurrentDot; var spr = tailImg.sprite;
            if (spr != null)
            {
                float tw = spr.rect.width * d, th = spr.rect.height * d;
                Tail.anchorMin = Tail.anchorMax = new Vector2(0.5f, 0); Tail.pivot = new Vector2(0.5f, 1); Tail.sizeDelta = new Vector2(tw, th);
                Tail.anchoredPosition = new Vector2(sp.TailOffset, 0);
            }
        }

        public void Hide() { shown = null; gameObject.SetActive(false); }
    }
}
