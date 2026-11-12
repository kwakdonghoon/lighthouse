using System;

namespace Lighthouse.Core
{
    /// <summary>
    /// 움직임 줄이기 (원본 prefers-reduced-motion: 안내·팝업·토스트 애니메이션 끔, 타이틀 전환 끔, 첫 안개 연출 끔, 선택 표시 흔들림 0 — HTML 478·538·1906·3414행).
    /// Platform 이 OS 설정을 읽어 채우고, 월드·UI 가 이 값을 읽는다. 기본은 꺼짐.
    /// </summary>
    public static class MotionPref
    {
        public static bool Reduce;
    }
}
