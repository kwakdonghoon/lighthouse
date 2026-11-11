using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>아이콘 크기: 그림 한 점 = 도트 한 칸의 정수 배.</summary>
    public sealed class DotSize : MonoBehaviour, IDotScaled
    {
        public LayoutElement Elem; public int Px = 8, Mult = 2;
        public void ApplyDot(float dot) { if (Elem != null) { Elem.preferredWidth = Elem.minWidth = Px * dot * Mult; Elem.preferredHeight = Elem.minHeight = Px * dot * Mult; } }
    }
}
