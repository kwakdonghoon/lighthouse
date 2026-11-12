using System;
using System.Collections.Generic;

namespace Lighthouse.UI.Model
{
    /// <summary>캔버스 단위 사각형(왼쪽 위 원점, y 는 아래로 커진다).</summary>
    public readonly struct UiRectF
    {
        public readonly float L, T, R, B;
        public UiRectF(float l, float t, float r, float b) { L = l; T = t; R = r; B = b; }
        public bool Overlaps(float l, float t, float r, float b) => l < R && r > L && t < B && b > T;
    }

    /// <summary>말풍선 자리 계산 결과: 가운데 x · 아래끝 y(꼬리 끝 쪽) · 꼬리가 풍선 가운데에서 벗어난 거리 · 비킬 곳이 없어 감출지.</summary>
    public readonly struct BubbleSpot
    {
        public readonly float Cx, Bottom, TailOffset; public readonly bool Hidden;
        public BubbleSpot(float cx, float bottom, float tail, bool hidden) { Cx = cx; Bottom = bottom; TailOffset = tail; Hidden = hidden; }
    }

    /// <summary>
    /// 말풍선 자리(원본 frame() 5306~5324행): 화면 가장자리에서는 안쪽으로 밀고(꼬리는 대상을 계속 가리킨다), 표시 줄·메뉴 단추·안내 카드·토스트와 겹치면
    /// 위쪽 칸을 가리면 그 아래로, 아래쪽 칸을 가리면 그 위로 옮긴다(최대 4번). 끝내 비킬 자리가 없으면 겹쳐 보이느니 감춘다. 엔진 참조 없는 순수 계산.
    /// </summary>
    public static class BubbleLayout
    {
        /// <param name="x">대상(배·관리인) 가운데 x. <paramref name="baseY"/> 풍선 아래끝(꼬리 위) y. <paramref name="w"/>·<paramref name="h"/> 풍선 크기. 모두 캔버스 단위.</param>
        public static BubbleSpot Resolve(float x, float baseY, float w, float h, float canvasW, float canvasH, IList<UiRectF> uis)
        {
            float half = w / 2f, cx = Math.Max(Math.Min(x, canvasW - half - 8f), half + 8f), by = baseY;
            for (int k = 0; k < 4; k++)
            {
                float l = cx - half, r = cx + half, t = by - h, b = by; bool hit = false; UiRectF u = default;
                for (int i = 0; i < uis.Count; i++) if (uis[i].Overlaps(l, t, r, b)) { u = uis[i]; hit = true; break; }
                if (!hit) break;
                float up = u.T - 6f, down = u.B + 6f + h, uc = (u.T + u.B) / 2f, bc = (t + b) / 2f;
                by = uc >= bc ? up : down;                                                                                  // 아래쪽 칸을 가리면 그 위로, 위쪽 칸을 가리면 그 아래로
                if (by > canvasH - 4f) by = up; else if (by - h < 4f) by = down;
            }
            by = Math.Min(canvasH - 4f, Math.Max(h + 4f, by));
            bool hidden = false;
            for (int i = 0; i < uis.Count; i++) if (uis[i].Overlaps(cx - half, by - h, cx + half, by)) { hidden = true; break; }
            float ax = Math.Max(-half + 10f, Math.Min(half - 10f, x - cx));
            return new BubbleSpot(cx, by, ax, hidden);
        }
    }
}
