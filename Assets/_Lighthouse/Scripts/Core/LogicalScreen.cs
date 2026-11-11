using System;

namespace Lighthouse.Core
{
    /// <summary>
    /// 논리 해상도 계산 (HTML fit() 2234행). 도트 한 칸을 기기 픽셀의 정수 개로 보여 준다.
    /// 논리 높이 설계값 320, 화면이 길면 위(하늘)부터 잘라 내고 아래 정렬(OY = H - 320).
    /// M0 에서는 '보통' 시점(VIEWS.normal=192)의 정수 배율만 옮겼다. 가깝게/넓게의 반 단계(0.5) 배율은 M2 에서 정한다.
    /// </summary>
    public readonly struct LogicalScreen
    {
        public const int DesignH = 320;
        public const int ViewNormal = 192;

        public readonly int DevW, DevH;
        public readonly int Scale;       // s: 논리 1칸 = 기기 픽셀 s 개
        public readonly int W, H, OY;

        public LogicalScreen(int devW, int devH, int viewW = ViewNormal)
        {
            DevW = devW; DevH = devH;
            int k = Math.Max(1, (int)Math.Floor(Math.Min((double)devW / viewW, (double)devH / (viewW * 1.41)) + 0.3));
            while (k > 1 && (double)devH / k < 225) k--;
            Scale = k;
            W = (int)Math.Ceiling((double)devW / k);
            H = (int)Math.Ceiling((double)devH / k);
            OY = H - DesignH;
        }
    }
}
