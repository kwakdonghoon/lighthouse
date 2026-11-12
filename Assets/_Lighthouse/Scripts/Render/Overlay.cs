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
        /// <summary>이번 프레임에 그린 줄 범위(위 행부터). Merge 가 그 줄만 합치고 올린다 — 비어 있으면 MinY &gt; MaxY.</summary>
        public int MinY = int.MaxValue, MaxY = -1;
        int lastLo = int.MaxValue, lastHi = -1;                // 지난 Merge 가 쓴 줄 범위(같은 dst 를 다시 쓸 때 지난 줄을 지워야 한다)
        public void Clear() { Array.Clear(P, 0, P.Length); MinY = int.MaxValue; MaxY = -1; }
        void Touch(int y0, int y1) { if (y0 < MinY) MinY = y0; if (y1 > MaxY) MaxY = y1; }

        static int Ga8(double ga) => (int)JsMath.Round(ga * 255);

        /// <summary>JS Math.round(n / 255) 를 정수만으로: n 이 정수이면 .5 로 끝나는 경우가 없어 (n + 127) / 255 와 같다.</summary>
        static int R255(int n) => (n + 127) / 255;

        // 원본: src(straight c, a) 를 globalAlpha 로 줄여 dst(premultiplied) 위에: out = srcPremul + dst*(1-srcAlpha)  (정수 연산 — 예전 JsMath.Round 식과 같은 값)
        void Blend(int idx, int c0, int c1, int c2, int a, int ga8)
            => BlendPre(idx, R255(R255(c0 * a) * ga8), R255(R255(c1 * a) * ga8), R255(R255(c2 * a) * ga8), R255(a * ga8));

        void BlendPre(int idx, int s0, int s1, int s2, int sa)
        {
            uint d = P[idx]; int inv = 255 - sa;
            int r = s0 + R255((int)(d & 255) * inv), g = s1 + R255((int)((d >> 8) & 255) * inv), b = s2 + R255((int)((d >> 16) & 255) * inv), na = sa + R255((int)(d >> 24) * inv);
            P[idx] = Bitmap.Pack(Math.Min(255, r), Math.Min(255, g), Math.Min(255, b), Math.Min(255, na));
            if (AddLayer != null && sa > 0) { uint ad = AddLayer.D[idx]; if ((ad & 0xFFFFFF) != 0) AddLayer.D[idx] = Bitmap.Pack(R255((int)(ad & 255) * inv), R255((int)((ad >> 8) & 255) * inv), R255((int)((ad >> 16) & 255) * inv), (int)(ad >> 24)); }
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
            int ya = Math.Max(0, y0), yb = Math.Min(H, y0 + h0), xa = Math.Max(0, x0), xb = Math.Min(W, x0 + w0);
            if (ya >= yb || xa >= xb) return;
            Touch(ya, yb - 1);
            int c0 = (int)c.R, c1 = (int)c.G, c2 = (int)c.B, sa = R255(a * g8), s0 = R255(R255(c0 * a) * g8), s1 = R255(R255(c1 * a) * g8), s2 = R255(R255(c2 * a) * g8);      // 사각형 안에서 모두 같은 값
            for (int j = ya; j < yb; j++)
            {
                int row = j * W;
                for (int i = xa; i < xb; i++) BlendPre(row + i, s0, s1, s2, sa);
            }
        }

        /// <summary>drawImage(s.c, round(x+ax), round(y+ay)) (globalAlpha 적용).</summary>
        public void Put(Bitmap s, double x, double y, double globalAlpha = 1)
        {
            int ox = (int)JsMath.Round(x + s.Ax), oy = (int)JsMath.Round(y + s.Ay), g8 = Ga8(globalAlpha);
            Touch(Math.Max(0, oy), Math.Min(H - 1, oy + s.H - 1));
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

        /// <summary>
        /// translate(tx, ty) → rotate(ang) → drawImage(s, ox, oy) (imageSmoothing 끔 = 최근접 표본, 원본 표류물 오크통). 도착 칸의 가운데 점을 거꾸로 돌려 원본 픽셀을 고른다.
        /// 스프라이트는 왼쪽 위가 원점인 작은 그림(Ax·Ay 는 쓰지 않는다).
        /// </summary>
        public void PutRotated(Bitmap s, double tx, double ty, double ang, double ox, double oy, double globalAlpha = 1)
        {
            double c = Math.Cos(ang), sn = Math.Sin(ang); int g8 = Ga8(globalAlpha);
            double rad = Math.Sqrt((double)(s.W * s.W + s.H * s.H)) + 2;
            int x0 = (int)Math.Floor(tx - rad), x1 = (int)Math.Ceiling(tx + rad), y0 = (int)Math.Floor(ty - rad), y1 = (int)Math.Ceiling(ty + rad);
            Touch(Math.Max(0, y0), Math.Min(H - 1, y1));
            for (int py = y0; py <= y1; py++)
            {
                if (py < 0 || py >= H) continue;
                for (int px = x0; px <= x1; px++)
                {
                    if (px < 0 || px >= W) continue;
                    double dx = px + 0.5 - tx, dy = py + 0.5 - ty;
                    double u = c * dx + sn * dy - ox, v = -sn * dx + c * dy - oy;          // 회전의 역변환
                    // 브라우저는 회전한 그림의 가장자리를 안티앨리어싱한다(표본은 최근접): 칸이 그림 사각형에 덮이는 비율을 4×4 점으로 센다
                    int inside = 0, total = 64;
                    if (u >= 1 && v >= 1 && u < s.W - 1 && v < s.H - 1) inside = total;                  // 안쪽 칸은 그대로
                    else
                        for (int sy = 0; sy < 8; sy++)
                            for (int sx = 0; sx < 8; sx++)
                            {
                                double ddx = dx + (sx - 3.5) / 8, ddy = dy + (sy - 3.5) / 8;
                                double su = c * ddx + sn * ddy - ox, sv = -sn * ddx + c * ddy - oy;
                                if (su >= 0 && sv >= 0 && su < s.W && sv < s.H) inside++;
                            }
                    if (inside == 0) continue;
                    int iu = Math.Max(0, Math.Min(s.W - 1, (int)Math.Floor(u))), iv = Math.Max(0, Math.Min(s.H - 1, (int)Math.Floor(v)));
                    uint p = s.D[iv * s.W + iu]; int a = (int)(p >> 24); if (a == 0) continue;
                    a = (int)JsMath.Round(a * inside / (double)total); if (a == 0) continue;
                    Blend(py * W + px, (int)(p & 255), (int)((p >> 8) & 255), (int)((p >> 16) & 255), a, g8);
                }
            }
        }

        /// <summary>두 층을 합쳐 premultiplied RGBA(위 행부터) 한 장으로. 가산층(add)은 RGB 만 더한다(알파 불변, 255 상한).</summary>
        public void Merge(CpuCanvas add, uint[] dst, bool flipY = false)
        {
            // 그린 줄(이번 + 지난번 Merge 가 쓴 줄)만 합친다 — dst 는 늘 같은 버퍼여야 한다(처음에는 0). 줄 범위를 모르는 쪽은(MinY &gt; MaxY 인데 그린 경우는 없다) 없다.
            int lo = Math.Min(MinY, add.MinY), hi = Math.Max(MaxY, add.MaxY);
            int wlo = Math.Min(lo, lastLo), whi = Math.Max(hi, lastHi);
            lastLo = lo; lastHi = hi;
            if (wlo < 0) wlo = 0; if (whi >= H) whi = H - 1;
            for (int y = wlo; y <= whi; y++)
            {
                int src = y * W, o = (flipY ? H - 1 - y : y) * W;
                for (int x = 0; x < W; x++)
                {
                    uint p = P[src + x], a = add.D[src + x];
                    if ((p & 0xFF000000u) == 0 && (a & 0x00FFFFFFu) == 0 && (p & 0x00FFFFFFu) == 0) { dst[o + x] = 0; continue; }
                    int r = Math.Min(255, (int)(p & 255) + (int)(a & 255)), g = Math.Min(255, (int)((p >> 8) & 255) + (int)((a >> 8) & 255)), b = Math.Min(255, (int)((p >> 16) & 255) + (int)((a >> 16) & 255));
                    dst[o + x] = Bitmap.Pack(r, g, b, (int)(p >> 24));
                }
            }
        }
    }
}
