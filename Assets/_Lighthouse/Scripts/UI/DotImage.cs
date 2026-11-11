using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>9-slice 틀 이미지: 틀 그림의 1도트 = 화면의 도트 한 칸(기기 픽셀 정수)이 되도록 pixelsPerUnitMultiplier 를 맞춘다.</summary>
    public sealed class DotImage : MonoBehaviour, IDotScaled
    {
        public Image Img;
        public void ApplyDot(float dot) { if (Img != null) { Img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.01f, dot); Img.SetAllDirty(); } }
    }
}
