using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>레이아웃과 상관없이 칸 수가 정해진 격자: 폭을 균등하게 나누고, 칸 높이는 줄 안에서 가장 큰 자식에 맞춘다(자식이 줄바꿈하는 글이어도 겹치지 않는다).</summary>
    public sealed class FixedColumnsLayout : LayoutGroup, IDotScaled
    {
        public int Columns = 3; public float Spacing = 8f, SpacingDots = 0f, MinCellHeight = 44f;
        float[] rowH = new float[0];
        public void ApplyDot(float dot) { if (SpacingDots > 0) { Spacing = SpacingDots * dot; SetDirty(); } }
        int Rows => (rectChildren.Count + Columns - 1) / Mathf.Max(1, Columns);
        float CellW(float total) => (total - padding.horizontal - Spacing * (Columns - 1)) / Mathf.Max(1, Columns);

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            float min = 0; foreach (var c in rectChildren) min = Mathf.Max(min, LayoutUtility.GetMinWidth(c));
            SetLayoutInputForAxis(padding.horizontal + min * Columns + Spacing * (Columns - 1), padding.horizontal + 100 * Columns, -1, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            int n = rectChildren.Count, rows = Rows; rowH = new float[rows];
            float cw = CellW(rectTransform.rect.width);
            for (int i = 0; i < n; i++)
            {
                int r = i / Columns; var c = rectChildren[i];
                c.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0, cw));
                rowH[r] = Mathf.Max(rowH[r], Mathf.Max(MinCellHeight, LayoutUtility.GetPreferredHeight(c)));
            }
            float total = padding.vertical; for (int r = 0; r < rows; r++) total += rowH[r] + (r > 0 ? Spacing : 0);
            SetLayoutInputForAxis(total, total, -1, 1);
        }

        public override void SetLayoutHorizontal() { Place(0); }
        public override void SetLayoutVertical() { Place(1); }

        void Place(int axis)
        {
            float cw = CellW(rectTransform.rect.width);
            for (int i = 0; i < rectChildren.Count; i++)
            {
                int r = i / Columns, col = i % Columns; var c = rectChildren[i];
                if (axis == 0) SetChildAlongAxis(c, 0, padding.left + col * (cw + Spacing), cw);
                else
                {
                    float y = padding.top; for (int k = 0; k < r; k++) y += rowH[k] + Spacing;
                    SetChildAlongAxis(c, 1, y, r < rowH.Length ? rowH[r] : MinCellHeight);
                }
            }
        }
    }
}
