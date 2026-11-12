using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>
    /// 키보드·게임패드로 옮겨 다닐 때 지금 초점이 있는 단추를 금빛 테두리로 보여 주는 고리(원본 :focus-visible).
    /// 마우스·터치로 눌렀을 때는 보이지 않는다(UiRoot 가 navMode 로 구분). 가는 막대 4개로 그려 9-slice 틀이 필요 없다.
    /// </summary>
    public sealed class FocusRing : MonoBehaviour
    {
        RectTransform box; readonly Image[] bars = new Image[4];
        public static readonly Color Ring = new Color(1f, 0.78f, 0.2f, 1f);

        public static FocusRing Create(Transform parent)
        {
            var root = Kit.Rect("FocusRing", parent); var f = root.gameObject.AddComponent<FocusRing>(); f.box = root;
            root.anchorMin = root.anchorMax = new Vector2(0, 1); root.pivot = new Vector2(0, 1);
            for (int i = 0; i < 4; i++) f.bars[i] = Kit.Solid(root, Ring, "bar" + i);
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            f.gameObject.SetActive(false);
            return f;
        }

        /// <summary>t 가 있으면 그 둘레에 고리를 둔다(없거나 안 보이면 숨김). 캔버스 단위·왼쪽 위 원점.</summary>
        public void Show(RectTransform t, RectTransform canvasRect)
        {
            if (t == null || !t.gameObject.activeInHierarchy || t.rect.width < 1f || t.rect.height < 1f) { if (gameObject.activeSelf) gameObject.SetActive(false); return; }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            transform.SetAsLastSibling();
            var cs = new Vector3[4]; t.GetWorldCorners(cs); var a = canvasRect.InverseTransformPoint(cs[0]); var b = canvasRect.InverseTransformPoint(cs[2]); var cr = canvasRect.rect;
            float pad = 1f, th = Mathf.Max(2f, UiRoot.CurrentDot);
            float l = a.x - cr.xMin - pad, r = b.x - cr.xMin + pad, tp = cr.yMax - b.y - pad, bt = cr.yMax - a.y + pad;
            box.anchoredPosition = new Vector2(l, -tp); box.sizeDelta = new Vector2(r - l, bt - tp);
            Bar(bars[0], 0, 1, 1, 1, 0, -th, 0, 0);       // 위
            Bar(bars[1], 0, 0, 1, 0, 0, 0, 0, th);        // 아래
            Bar(bars[2], 0, 0, 0, 1, 0, 0, th, 0);        // 왼
            Bar(bars[3], 1, 0, 1, 1, -th, 0, 0, 0);       // 오른
        }

        static void Bar(Image img, float ax0, float ay0, float ax1, float ay1, float ox0, float oy0, float ox1, float oy1)
        {
            var r = img.rectTransform; r.anchorMin = new Vector2(ax0, ay0); r.anchorMax = new Vector2(ax1, ay1); r.offsetMin = new Vector2(ox0, oy0); r.offsetMax = new Vector2(ox1, oy1); img.raycastTarget = false;
        }
    }

    /// <summary>단추가 어떤 동작(원본 data-act)을 하는지 붙여 두는 표지. 초점·스크롤 요청이 Act 문자열로 단추를 찾는 데 쓴다.</summary>
    public sealed class ActTag : MonoBehaviour { public string Act; }

    /// <summary>스크롤 본문 안의 한 칸이 보이게 하는 도구(원본 scrollIntoView({block:'nearest'})).</summary>
    public static class ScrollKit
    {
        /// <summary>t 의 위·아래가 뷰포트 안에 들어오게 가장 적게 스크롤한다. t 가 뷰포트보다 크면 위쪽을 맞춘다. top=true 이면 t 의 위가 뷰포트 위(여백 4)에 오게 한다.</summary>
        public static void IntoView(ScrollRect sr, RectTransform t, bool top = false)
        {
            if (sr == null || t == null || sr.content == null || sr.viewport == null) return;
            Canvas.ForceUpdateCanvases();
            var vp = sr.viewport; var cs = new Vector3[4]; t.GetWorldCorners(cs);
            var a = vp.InverseTransformPoint(cs[0]); var b = vp.InverseTransformPoint(cs[2]);            // 뷰포트 지역 좌표(y 위로 커짐): a=왼아래, b=오른위
            float vTop = vp.rect.yMax, vBot = vp.rect.yMin, dy = 0f;
            if (top) dy = (vTop - 4f) - b.y;
            else if (b.y - a.y >= vTop - vBot) dy = vTop - b.y;
            else if (b.y > vTop) dy = vTop - b.y;
            else if (a.y < vBot) dy = vBot - a.y;
            if (Mathf.Abs(dy) < 0.5f) return;
            float max = Mathf.Max(0f, sr.content.rect.height - vp.rect.height);
            var p = sr.content.anchoredPosition; p.y = Mathf.Clamp(p.y + dy, 0f, max); sr.content.anchoredPosition = p;     // dy = 요소를 옮겨야 할 방향(위가 +). 내용(위쪽 피벗)의 anchoredPosition.y 가 오르면 요소가 위로 간다
        }
    }
}
