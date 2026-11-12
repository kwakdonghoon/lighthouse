using System;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>
    /// 섬 둘레 블록 물거품(원본 drawShore 1266행)을 시각 t 마다 다시 그리는 작은 층. 가장자리를 따라 깜빡이며 밀려왔다 간다(sin(t·1.6 + i·1.7)).
    /// M2 의 SceneComposer.Foam 은 t=0 한 장으로 구워 움직이지 않았다 — 이 층이 매 프레임 새로 채운다(섬 둘레 상자만큼의 작은 버퍼, 쓰레기 없음).
    /// 색은 straight RGBA(알파 = 깜빡임 세기), Bitmap(Ax, Ay) 규약: 원점 = 캔버스 좌표의 왼쪽 위.
    /// </summary>
    public sealed class FoamLayer
    {
        public int Ax, Ay, W, H; public uint[] D;
        static readonly uint CA = Bitmap.Pack(0xea, 0xf6, 0xfc, 0), CB = Bitmap.Pack(0xb4, 0xdc, 0xf0, 0);

        public FoamLayer(int stage, double cx, double cy)
        {
            int N = Terrain.N(stage); double ext = (N + 0.15 + 0.5) * 2 * Terrain.VS;           // |a−b|·VS 의 최댓값 둘레 + 여유
            Ax = (int)Math.Floor(cx - ext) - 4; Ay = (int)Math.Floor(cy - ext / 2) - 4; W = (int)Math.Ceiling(2 * ext) + 10; H = (int)Math.Ceiling(ext) + 10; D = new uint[W * H];
        }

        void Put(double x, double y, uint rgb, int g8)
        {
            int px = (int)JsMath.Round(x) - Ax, py = (int)JsMath.Round(y) - Ay;
            if (px < 0 || py < 0 || px >= W || py >= H) return;
            D[py * W + px] = (rgb & 0x00FFFFFFu) | ((uint)g8 << 24);
        }

        /// <summary>한 프레임을 채운다. night: dusk≥0.5(원본 NIGHT). 겹치면 뒤의 것이 덮는다.</summary>
        public void Paint(int stage, double cx, double cy, double t, bool night)
        {
            Array.Clear(D, 0, D.Length); int N = Terrain.N(stage), i = 0;
            for (double sv = -N + 0.5; sv <= N - 0.4; sv += 1)
            {
                for (int k = 0; k < 4; k++)
                {
                    double a = k == 0 ? N + 0.15 : k == 1 ? sv : k == 2 ? -N - 0.15 : sv, b = k == 0 ? sv : k == 1 ? N + 0.15 : k == 2 ? sv : -N - 0.15;
                    double ph = Math.Sin(t * 1.6 + i * 1.7), x = cx + (a - b) * Terrain.VS, y = cy + (a + b) * Terrain.VS / 2; i++;
                    if (ph < -0.1) continue;
                    int g8 = (int)JsMath.Round((0.45 + 0.4 * ph) * (night ? 0.7 : 0.95) * 255);
                    double sx = JsMath.Round(x / 2) * 2, sy = JsMath.Round(y);
                    Put(sx - 1, sy, CA, g8); Put(sx, sy, CA, g8);                                // Q(.., 2, 1)
                    if (ph > 0.5) Put(sx + 1, sy + 1, CB, g8);
                }
            }
        }
    }
}
