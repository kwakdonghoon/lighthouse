namespace Lighthouse.UI.Model
{
    /// <summary>
    /// 다시 그린 뒤 화면(PanelView)이 한 번 읽는 "스크롤·초점 요청"(원본 decoReq 3915행·refocus 3993행). 순수 자료다.
    /// Focus: 초점을 줄 단추의 Act 문자열(원본 data-act). 특수 값 — "ph"(고르기 시트), "zone.on"(지금 구역 칩), "gear"(머리의 톱니), "gate"(보호자 확인 첫 보기).
    /// Scroll: "zones"(구역 칩 줄이 맨 위에 오게) · "ph"(고르기 시트가 보이게). Near: 그 자리 칸이 보이는 곳까지만 스크롤.
    /// </summary>
    public sealed class FocusRequest
    {
        public string Scroll, Focus, Near;
        public FocusRequest(string focus = null, string scroll = null, string near = null) { Focus = focus; Scroll = scroll; Near = near; }
    }

    public sealed partial class PanelController
    {
        /// <summary>다음에 그릴 때 적용할 요청(한 번 쓰면 비운다).</summary>
        public FocusRequest Req;
        public FocusRequest TakeReq() { var r = Req; Req = null; return r; }
        /// <summary>캔버스 높이(dp). 낮은 화면 규칙(원본 innerHeight &lt;= 700)에 쓴다. UiRoot 가 화면 크기가 바뀔 때 채운다.</summary>
        public float ScreenH = 800f;
        public bool LowScreen => ScreenLayout.Low(ScreenH);
    }
}
