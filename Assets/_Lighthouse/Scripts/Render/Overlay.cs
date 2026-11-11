using System;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// M4 연출 층: 투명한 바탕에 쌓는 premultiplied 8비트 알파 버퍼 (HTML 의 Q/fillRect/drawImage 중 알파로 얹는 것).
    /// 가산('lighter') 연출은 따로 검은 CpuCanvas(add)에 Fx.DrawGlow/AddRect 로 쌓고, Merge 가 두 층을 한 장(premultiplied RGBA)으로 합친다.
    /// 화면에서는 이 한 장을 'Blend One OneMinusSrcAlpha' 로 맨 위에 얹는다: 장면*(1-A) + P  — 알파로 덮는 일과 더하는 일(알파 0) 모두 한 식이다.
    /// 가산 위에 알파로 덮는 연산(안개·틴트·역할 효과의 붉은 점)은 이미 쌓인 가산을 (1-a)배로 줄여(AddLayer) 원본의 그리는 순서와 같은 결과를 낸다. 그리는 순서가 원본과 다른 곳(배·관리인 스프라이트와 연출의 앞뒤)은 근사다.
    /// </summary>
    public sealed class Overlay
    {
        public readonly int W, H; public readonly uint[] P;
        /// <summary>가산층: 알파로 덮는 연산이 이미 쌓인 가산(빛)을 (1-알파)배로 줄인다 — 원본의 그리는 순서(빛을 먼저 얹고 그 위에 덮음)와 같은 결과.</summary>
        public CpuCanvas AddLayer;
        public Overlay(int w, int h) { W = w; H = h; P = new uint[w * h]; }
        public void Clear() { Array.Clear(P, 0, P.Length); }

        static int Ga8(double ga) => (int)JsMath.Round(ga * 255);

        // 원본: src(straight c, a) 를 globalAlpha 로 줄여 dst(premultiplied) 위에: out = srcPremul + dst*(1-srcAlpha)
        void Blend(int idx, int c0, int c1, int c2, int a, int ga8)
        {
            int sa = (int)JsMath.Round(a * ga8 / 255.0);
            uint d = P[idx]; int da = (int)(d >> 24);
            int r = (int)JsMath.Round(JsMath.Round(c0 * a / 255.0) * ga8 / 255.0) + (int)JsMath.Round((int)(d & 255) * (255 - sa) / 255.0);
            int g = (int)JsMath.Round(JsMath.Round(c1 * a / 255.0) * ga8 / 255.0) + (int)JsMath.Round((int)((d >> 8) & 255) * (255 - sa) / 255.0);
            int b = (int)JsMath.Round(JsMath.Round(c2 * a / 255.0) * ga8 / 255.0) + (int)JsMath.Round((int)((d >> 16) & 255) * (255 - sa) / 255.0);
            int na = sa + (int)JsMath.Round(da * (255 - sa) / 255.0);
            P[idx] = Bitmap.Pack(Math.Min(255, r), Math.Min(255, g), Math.Min(255, b), Math.Min(255, na));
            if (AddLayer != null && sa > 0) { uint ad = AddLayer.D[idx]; if ((ad & 0xFFFFFF) != 0) { int k = 255 - sa; AddLayer.D[idx] = Bitmap.Pack((int)JsMath.Round((int)(ad & 255) * k / 255.0), (int)JsMath.Round((int)((ad >> 8) & 255) * k / 255.0), (int)JsMath.Round((int)((ad >> 16) & 255) * k / 255.0), (int)(ad >> 24)); } }
        }

        /// <summary>Q(x, y, w, h, color): 도트 칸에 맞춘 fillRect(Math.round), globalAlpha 적용.</summary>
        public void Q(double x, double y, double w, double h, Col c, double globalAlpha = 1)
        {
            int x0 = (int)JsMath.Round(x), y0 = (int)JsMath.Round(y), w0 = (int)Math.Max(1, JsMath.Round(w)), h0 = (int)Math.Max(1, JsMath.Round(h)), g8 = Ga8(globalAlpha);
            Rect(x0, y0, w0, h0, c, 255, g8);
        }

        /// <summary>fillStyle 에 알파가 들어 있는 사각형: rgba(c, alpha) 를 globalAlpha 와 함께. 좌표는 정수.</summary>
        public void FillRect(int x0, int y0, int w, int h, Col c, double alpha, double globalAlpha = 1)
            => Rect(x0, y0, w, h, c, Ga8(alpha), Ga8(globalAlpha));

        void Rect(int x0, int y0, int w0, int h0, Col c, int a, int g8)
        {
            for (int j = y0; j < y0 + h0; j++)
            {
                if (j < 0 || j >= H) continue;
                for (int i = x0; i < x0 + w0; i++)
                    if (i >= 0 && i < W) Blend(j * W + i, (int)c.R, (int)c.G, (int)c.B, a, g8);
            }
        }

        /// <summary>drawImage(s.c, round(x+ax), round(y+ay)) (globalAlpha 적용).</summary>
        public void Put(Bitmap s, double x, double y, double globalAlpha = 1)
        {
            int ox = (int)JsMath.Round(x + s.Ax), oy = (int)JsMath.Round(y + s.Ay), g8 = Ga8(globalAlpha);
            for (int j = 0; j < s.H; j++)
            {
                int dy = oy + j; if (dy < 0 || dy >= H) continue;
                for (int i = 0; i < s.W; i++)
                {
                    int dx = ox + i; if (dx < 0 || dx >= W) continue;
                    uint p = s.D[j * s.W + i]; int a = (int)(p >> 24); if (a == 0) continue;
                    Blend(dy * W + dx, (int)(p & 255), (int)((p >> 8) & 255), (int)((p >> 16) & 255), a, g8);
                }
            }
        }

        /// <summary>두 층을 합쳐 premultiplied RGBA(위 행부터) 한 장으로. 가산층(add)은 RGB 만 더한다(알파 불변, 255 상한).</summary>
        public void Merge(CpuCanvas add, uint[] dst, bool flipY = false)
        {
            for (int i = 0; i < P.Length; i++)
            {
                uint p = P[i], a = add.D[i];
                int r = Math.Min(255, (int)(p & 255) + (int)(a & 255)), g = Math.Min(255, (int)((p >> 8) & 255) + (int)((a >> 8) & 255)), b = Math.Min(255, (int)((p >> 16) & 255) + (int)((a >> 16) & 255));
                int o = flipY ? (H - 1 - i / W) * W + i % W : i; dst[o] = Bitmap.Pack(r, g, b, (int)(p >> 24));
            }
        }
    }
}
