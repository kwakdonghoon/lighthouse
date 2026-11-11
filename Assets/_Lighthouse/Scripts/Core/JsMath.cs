using System;

namespace Lighthouse.Core
{
    /// <summary>
    /// JS 의 수 규칙을 C# 에서 그대로 재현하는 헬퍼 (이식 설계서 부록 B 1·2·18번).
    /// 순수 C# — UnityEngine 참조 없음.
    /// </summary>
    public static class JsMath
    {
        /// <summary>
        /// JS Math.round: 가장 가까운 정수, 정확히 .5 는 +무한대 쪽(2.5→3, -0.5→-0, -2.5→-2).
        /// C# Math.Round 는 은행가 반올림이라 쓰지 않는다. Math.Floor(x+0.5) 는 0.49999999999999994 에서 틀리므로
        /// 바닥값과의 차이로 판정한다 (V8 과 동일).
        /// </summary>
        public static double Round(double x)
        {
            double f = Math.Floor(x);
            return (x - f >= 0.5) ? f + 1.0 : f;
        }

        public static int RoundI(double x) => (int)Round(x);

        /// <summary>JS Math.min(255, Math.max(0, Math.round(x))) 에 해당하는 색 채널 고정.</summary>
        public static double Clamp255(double x)
        {
            double r = Round(x);
            return r < 0 ? 0 : (r > 255 ? 255 : r);
        }

        /// <summary>
        /// JS 의 hsh(x, y) 원본 32비트 값 (나누기 전).
        /// JS: h = (x*374761393 + y*668265263)|0; h = Math.imul(h ^ (h>>>13), 1274126177); return (h ^ (h>>>16)) >>> 0
        /// x, y 는 |x|,|y| &lt; 2^23 인 정수라 double 곱·합이 정확하다 (long 으로 같은 값을 얻는다).
        /// </summary>
        public static uint HshRaw(int x, int y)
        {
            long t = (long)x * 374761393L + (long)y * 668265263L;
            uint h = unchecked((uint)(int)t);                 // |0 : 32비트로 자른다
            h = unchecked((h ^ (h >> 13)) * 1274126177u);     // Math.imul + 부호 없는 보기(비트는 같다)
            return h ^ (h >> 16);
        }

        /// <summary>JS 의 hsh(x, y): [0,1) 의 결정적 난수 (격자 해시).</summary>
        public static double Hsh(int x, int y) => HshRaw(x, y) / 4294967296.0;
    }
}
