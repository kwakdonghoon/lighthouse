using System;
using Lighthouse.Core;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Lighthouse.Platform
{
    /// <summary>
    /// OS 접근성 설정을 읽는다. 원본(HTML)에 있는 접근성 선택은 prefers-reduced-motion(움직임 줄이기) 뿐이다 —
    /// 글자 크기·대비 선택은 원본에 없어서 새로 만들지 않았다(출시 체크리스트의 "접근성 확장" 항목).
    ///  - Android: 개발자/접근성의 "애니메이션 제거"(animator_duration_scale == 0).
    ///  - iOS: UIAccessibilityIsReduceMotionEnabled (Plugins/iOS/LhPlatform.mm).
    ///  - 그 밖: 꺼짐.
    /// 읽은 값은 Core 의 MotionPref.Reduce 에 넣는다(월드·UI 가 읽는다). 실기기 미확인.
    /// </summary>
    public static class PlatformAccess
    {
        public static void Init()
        {
            try { MotionPref.Reduce = ReadReduceMotion(); }
            catch (Exception e) { Debug.LogWarning("움직임 줄이기 설정 읽기 실패: " + e.Message); MotionPref.Reduce = false; }
        }

        static bool ReadReduceMotion()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var global = new AndroidJavaClass("android.provider.Settings$Global"))
                return global.CallStatic<float>("getFloat", resolver, "animator_duration_scale", 1f) == 0f;
#elif UNITY_IOS && !UNITY_EDITOR
            return LhIsReduceMotion() != 0;
#else
            return false;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int LhIsReduceMotion();
#endif
    }
}
