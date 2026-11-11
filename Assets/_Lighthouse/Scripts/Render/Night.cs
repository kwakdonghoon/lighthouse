using System;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// 밤 사본 (HTML nightOf 621행): 구워 둔 스프라이트의 불투명한 부분에 푸른 틴트 rgba(12,22,70, NT*k) 를 씌운다
    /// (canvas 'source-atop'). 브라우저 캔버스는 premultiplied 8비트로 저장하므로 같은 정수 단계를 밟는다:
    /// premul = round(c*a/255) → 틴트도 premul 8비트 → 혼합 round → 읽을 때 unpremul round.
    /// 불투명(a=255)·투명 픽셀은 브라우저와 정확히 같다. 반투명 가장자리(그림자)는 브라우저의 GPU 경로와 ±1 어긋날 수 있다(골든으로 실측).
    /// </summary>
    public static class Night
    {
        public const double NT = 0.4;                         // 확정: 중 (진 0.58, 약 0.26)
        const int TR = 12, TG = 22, TB = 70;

        public static Bitmap Of(Bitmap s, double k = 1, double nt = NT)
        {
            int sa = (int)JsMath.Round(nt * k * 255);          // fillStyle 알파의 8비트 값
            int sr = (int)JsMath.Round(TR * sa / 255.0), sg = (int)JsMath.Round(TG * sa / 255.0), sb = (int)JsMath.Round(TB * sa / 255.0);
            var o = new Bitmap(s.Ax, s.Ay, s.W, s.H);
            for (int i = 0; i < s.D.Length; i++)
            {
                uint d = s.D[i]; int a = (int)(d >> 24);
                if (a == 0) continue;
                o.D[i] = Pack(Tint((int)(d & 255), a, sr, sa), Tint((int)((d >> 8) & 255), a, sg, sa), Tint((int)((d >> 16) & 255), a, sb, sa), a);
            }
            return o;
        }

        static int Tint(int c, int a, int src, int sa)
        {
            int p = (int)JsMath.Round(c * a / 255.0);                                   // premul 8비트
            int po = (int)JsMath.Round((src * (double)a + p * (double)(255 - sa)) / 255.0);   // source-atop: src*αd + dst*(1-αs)
            if (a == 255) return po;
            int u = (int)JsMath.Round(po * 255.0 / a);
            return u > 255 ? 255 : u;
        }

        static uint Pack(int r, int g, int b, int a) => Bitmap.Pack(r, g, b, a);
    }
}
