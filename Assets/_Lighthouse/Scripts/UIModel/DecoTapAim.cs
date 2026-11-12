using System;
using System.Collections.Generic;

namespace Lighthouse.UI.Model
{
    /// <summary>
    /// 섬을 직접 눌러 자리 고르기(원본 decoTapAt 3933행)의 겨냥 계산: 누른 점(설계 좌표)에서 각 열린 자리까지의 거리를 화면 단위(CSS px = 캔버스 dp)로 재어
    /// 22 안쪽인 것만 모은다. 자리 기준점은 자리 위치에서 위로 3 올린 곳(sy - 3)이다. 엔진 참조 없는 순수 계산.
    /// </summary>
    public static class DecoTapAim
    {
        public const double Reach = 22;           // 원본: d <= 22 CSS px
        public const double CloseGap = 8;         // 원본: 첫째와 둘째의 거리 차 < 8 이면 고르지 않고 "자리가 가까워요" (PanelController.DecoTapPick)

        /// <param name="x">누른 곳(설계 좌표). <paramref name="slots"/> (id, 자리 x, 자리 y)(설계 좌표). <paramref name="css"/> 설계 1칸 = 화면 단위 몇 개(원본 SC).</param>
        public static List<(double d, string id)> Candidates(double x, double y, IEnumerable<(string id, double sx, double sy)> slots, double css)
        {
            var c = new List<(double d, string id)>();
            foreach (var s in slots)
            {
                double dd = Math.Sqrt((x - s.sx) * (x - s.sx) + (y - (s.sy - 3)) * (y - (s.sy - 3))) * css;
                if (dd <= Reach) c.Add((dd, s.id));
            }
            return c;
        }
    }
}
