using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>레이아웃 그룹의 여백·간격을 "도트 수"로 두고 도트 크기를 곱해 쓴다.</summary>
    public sealed class DotLayout : MonoBehaviour, IDotScaled
    {
        public HorizontalOrVerticalLayoutGroup Group; public Vector4 PadDots; public float SpacingDots, MinHeightDots; public LayoutElement Elem;
        public void ApplyDot(float dot)
        {
            if (Group != null)
            {
                Group.padding = new RectOffset(Mathf.RoundToInt(PadDots.x * dot), Mathf.RoundToInt(PadDots.z * dot), Mathf.RoundToInt(PadDots.y * dot), Mathf.RoundToInt(PadDots.w * dot));
                Group.spacing = SpacingDots * dot;
            }
            if (Elem != null && MinHeightDots > 0) Elem.minHeight = MinHeightDots * dot;
        }
    }
}
