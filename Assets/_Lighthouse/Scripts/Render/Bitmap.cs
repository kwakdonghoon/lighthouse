using System;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// 굽기 버퍼 겸 구워진 스프라이트. HTML 의 <c>B = { x0, y0, w, h, d: Uint32Array }</c> 와
    /// <c>{ c, ax, ay, w, h }</c> 를 하나로 합친 것이다 (Ax = x0, Ay = y0).
    /// D 의 픽셀은 리틀엔디언 0xAABBGGRR (JS Uint32Array 와 같은 비트 배치, 부록 B-18).
    /// 행 0 이 화면 맨 위 (canvas 와 같은 순서; Texture2D 로 올릴 때 뒤집는다).
    /// </summary>
    public sealed class Bitmap
    {
        public readonly int Ax, Ay, W, H;
        public readonly uint[] D;

        public Bitmap(int ax, int ay, int w, int h)
        {
            Ax = ax; Ay = ay; W = w; H = h;
            D = new uint[w * h];
        }

        public static uint Pack(int r, int g, int b, int a) => unchecked((uint)((a << 24) | (b << 16) | (g << 8) | r));
    }

    /// <summary>색 한 점 (채널은 JS 와 같이 double: vfill 의 곱셈·반올림이 같은 값이 되도록).</summary>
    public readonly struct Col
    {
        public readonly double R, G, B;
        public Col(double r, double g, double b) { R = r; G = g; B = b; }

        /// <summary>JS rgb('#rrggbb') = parseInt 로 읽어 [n>>16, (n>>8)&amp;255, n&amp;255].</summary>
        public static Col Hex(string h)
        {
            int n = Convert.ToInt32(h.Substring(1), 16);
            return new Col(n >> 16, (n >> 8) & 255, n & 255);
        }

        /// <summary>JS hexOf(c): '#' + 채널마다 clamp(round) 한 두 자리 16진.</summary>
        public static string HexOf(Col c)
        {
            return "#" + ((int)JsMath.Clamp255(c.R)).ToString("x2") + ((int)JsMath.Clamp255(c.G)).ToString("x2") + ((int)JsMath.Clamp255(c.B)).ToString("x2");
        }

        /// <summary>JS vmul(c, f): 채널마다 clamp(round(v*f)).</summary>
        public static Col Mul(Col c, double f) => new Col(JsMath.Clamp255(c.R * f), JsMath.Clamp255(c.G * f), JsMath.Clamp255(c.B * f));
    }
}
