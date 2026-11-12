using System;

namespace Lighthouse.UI.Model
{
    /// <summary>
    /// 타이틀·첫 실행 안내의 시간표(원본 CSS·타이머 수치 그대로). 엔진 참조 없는 순수 함수라 dotnet 으로 시험한다.
    /// 타이틀: 인트로(첫 실행) 15.6초 · 첫 실행(인트로 없음) 6.4초 · 그 밖 3.6초 뒤 자동으로 닫힘(원본 5603행),
    /// 부제 9.6초·날짜 줄 10.8초에 0.4초 4단계로 나타남(5604행·CSS 5526행), 안내 한 줄은 1.2초 뒤 10초 동안(CSS 513·517행).
    /// </summary>
    public static class IntroTiming
    {
        public const float IntroCloseSec = 15.6f, FirstCloseSec = 6.4f, PlainCloseSec = 3.6f;
        public const float TaglineAt = 9.6f, DayAt = 10.8f, StepLen = 0.4f; public const int StepN = 4;
        public const float HintDelay = 1.2f, HintLife = 10f;

        public static float AutoCloseSec(bool intro, bool first) => intro ? IntroCloseSec : first ? FirstCloseSec : PlainCloseSec;

        /// <summary>CSS transition: opacity 0.4s steps(4): 시작 시각 at 부터 4단계(0, .25, .5, .75, 1)로 오른다.</summary>
        public static float Steps(float t, float at, float len = StepLen, int n = StepN) => (float)(Math.Floor(Math.Max(0, Math.Min(1, (t - at) / len)) * n) / n);

        /// <summary>안내 한 줄(@keyframes hint): 지연 1.2초 → 10초 동안 0%:0, 15%:1, 78%:1, 100%:0 (ease 와 비슷한 부드러운 곡선).</summary>
        public static float HintAlpha(float sec)
        {
            float u = (sec - HintDelay) / HintLife; if (u <= 0f || u >= 1f) return 0f;
            float k = u < 0.15f ? u / 0.15f : u <= 0.78f ? 1f : 1f - (u - 0.78f) / 0.22f; return k * k * (3f - 2f * k);
        }
    }
}
