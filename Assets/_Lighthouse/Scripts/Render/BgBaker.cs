using System;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>하늘·바다 배경(makeBg 686행)과 구름(bakeCloud 731행). 화면 크기가 바뀔 때만 굽는다.</summary>
    public static class BgBaker
    {
        public const int DesignH = 320, Horizon = 150;
        public static readonly string[] SkyN = { "#040818", "#071029", "#0c1838", "#13234c", "#22365f", "#40507a", "#7d6186" };   // 수평선 쪽이 장밋빛 보라로 따뜻해진다
        public static readonly string[] SkyD = { "#3f86c8", "#5b9ad4", "#78b0de", "#98c6e8", "#b8d9ee", "#d8ebf2", "#f6efd8" };
        public static readonly string[] SeaN = { "#4d5a82", "#36507a", "#27426a", "#1a3358", "#122846", "#0c1e38", "#08162c" };
        public static readonly string[] SeaD = { "#9ccfe4", "#7ebfdc", "#5aa8cf", "#3d92c0", "#2b7bb0", "#1f69a0", "#195a8e" };

        static double[][] Ramp(string[] hex) => hex.Select(h => { var c = Col.Hex(h); return new[] { c.R, c.G, c.B }; }).ToArray();

        static int[] LerpC(double[][] ramp, double f)
        {
            int i = (int)Math.Max(0, Math.Min(ramp.Length - 2, Math.Floor(f)));
            double k = f - i;
            var o = new int[3];
            for (int q = 0; q < 3; q++) o[q] = (int)JsMath.Round(ramp[i][q] + (ramp[i + 1][q] - ramp[i][q]) * k);
            return o;
        }

        /// <summary>w×h 불투명 배경. oy = H - DESIGN_H (논리 화면이 설계 높이보다 큰 만큼). 원점 (0,0).</summary>
        public static Bitmap MakeBg(bool night, int w, int h, int oy)
        {
            var bm = new Bitmap(0, 0, w, h);
            var sky = Ramp(night ? SkyN : SkyD); var sea = Ramp(night ? SeaN : SeaD);
            double hy = Horizon + oy;
            const double BAND = 14;
            for (int y = 0; y < h; y++)
            {
                double[][] ramp; double f;
                if (y < hy) { ramp = sky; f = Math.Pow(y / hy, 1.7) * (ramp.Length - 1); }
                else { double v = (y - hy) / (h - hy); ramp = sea; f = Math.Pow(v, 0.75) * (ramp.Length - 1); }
                f = Math.Max(0, Math.Min(ramp.Length - 1.001, f));
                double step = BAND / (ramp.Length - 1);
                var k = LerpC(ramp, JsMath.Round(f * step) / step);
                uint px = Bitmap.Pack(k[0], k[1], k[2], 255);
                for (int x = 0; x < w; x++) bm.D[y * w + x] = px;
            }
            return bm;
        }

        static void FillRect(Bitmap B, int x, int y, int w, int h, Col c)
        {
            uint px = Bitmap.Pack((int)c.R, (int)c.G, (int)c.B, 255);
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++)
                {
                    int bx = i - B.Ax, by = j - B.Ay;
                    if (bx < 0 || by < 0 || bx >= B.W || by >= B.H) continue;
                    B.D[by * B.W + bx] = px;
                }
        }

        /// <summary>구름 한 장(64×24, 원점 -32,-16).</summary>
        public static Bitmap Cloud(bool night)
        {
            var b = night ? new[] { "#3a4b74", "#4d5f8a", "#6d7fa8" } : new[] { "#c4d2e4", "#eef4fa", "#ffffff" };
            var B = new Bitmap(-32, -16, 64, 24);
            foreach (var r in new[] { new[] { -22, 0, 44, 6 }, new[] { -14, -5, 26, 5 }, new[] { 4, -9, 14, 4 }, new[] { -30, 2, 8, 4 }, new[] { 20, 2, 10, 4 } })
            {
                int x = r[0], y = r[1], w = r[2], h = r[3];
                FillRect(B, x, y + 2, w, h, Col.Hex(b[0])); FillRect(B, x, y, w, h, Col.Hex(b[1])); FillRect(B, x, y, w, 1, Col.Hex(b[2]));
            }
            return B;
        }
    }
}
