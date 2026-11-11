using System.Collections.Generic;

namespace Lighthouse.UI.Model
{
    /// <summary>
    /// 화면 한 장을 "블록 목록"으로 나타낸 순수 자료. 팝업 본문(편지·도감·성장·꾸미기·상점·설정·소식)은 Core 상태를 읽어
    /// 이 블록들로 만들어지고, uGUI 쪽(BlockRenderer)은 블록을 그리기만 한다. 입력은 블록의 Act 문자열(원본 data-act)로 컨트롤러에 돌아온다.
    /// 그래서 화면 문구·개수·잠금 표시는 Unity 없이 dotnet 으로 시험된다.
    /// </summary>
    public enum BK
    {
        Heading,     // 소제목 (h3)
        Note,        // 안내 문단. Style: lead | qnote | need | status | legend | small
        Summary,     // 요약 카드: Text(큰 글) Sub(작은 글) Value(막대, -1 이면 없음) Kids(덧붙임 줄)
        Progress,    // 막대 한 줄: Text(왼쪽) Sub(오른쪽) Value(0~1) Style: thin
        Seg,         // 분절 단추 줄: Kids = Button(On, Locked, Sub=잠금 꼬리표). Style: wrap
        Grid,        // 칸 격자: Kids = Cell
        Cell,        // 도감 칸·자리 칸: Icon Text Sub Tag Count Locked Shiny On Act Style(slot|lockd|full)
        Row,         // 아이콘 + 글 + 오른쪽 단추들: Icon Text Sub Kids(Button) Style(offer|ad|role|track|prod|ship|diary|gline|...)
        Letter,      // 편지 한 통: Text(보낸 이) Sub(미리 보기 또는 본문) Tag(보존 칩) Date New Open Icon Act Extra(두고 간 선물 줄) Kids(없음)
        Button,      // 단추: Text Act Style(main|btn|tbtn|wide|txt) Disabled On
        ButtonRow,   // 단추 가로 묶음: Kids = Button
        Empty,       // 빈 상태: Icon Text Sub
        Input,       // 글 칸(저장 코드): Text(현재 값) Sub(안내 문구) Act
        Card,        // 제목 있는 묶음: Text(제목) Sub(설명) Kids. Style: set | stage | rank | gate | pv | confirm
        Stats,       // 기록 칸들: Kids = Cell(Text 이름, Sub 값)
        Pick,        // 장식 고르기 줄: Kids = Cell
        Gate,        // 보호자 확인: Text(질문) Kids = Button(보기들) + 그만두기
    }

    public sealed class Block
    {
        public BK Kind;
        public string Text, Sub, Extra, Icon, Act, Tag, Style, Date;
        public bool On, Disabled, Locked, Shiny, New, Open;
        public float Value = -1f;
        public string Count;
        public List<Block> Kids;

        public Block(BK kind) { Kind = kind; }

        public Block Add(Block b) { (Kids ?? (Kids = new List<Block>())).Add(b); return b; }
        public IEnumerable<Block> Walk()
        {
            yield return this;
            if (Kids != null) foreach (var k in Kids) foreach (var d in k.Walk()) yield return d;
        }

        // 만들기 도우미
        public static Block H(string t) => new Block(BK.Heading) { Text = t };
        public static Block N(string t, string style = null) => new Block(BK.Note) { Text = t, Style = style };
        public static Block Btn(string t, string act, string style = "btn", bool disabled = false, bool on = false) => new Block(BK.Button) { Text = t, Act = act, Style = style, Disabled = disabled, On = on };
        public static Block Seg(bool wrap = false) => new Block(BK.Seg) { Style = wrap ? "wrap" : null };
        public static Block Prog(string l, string r, float v, string style = null) => new Block(BK.Progress) { Text = l, Sub = r, Value = v, Style = style };
    }

    /// <summary>팝업 위쪽 탭 한 칸.</summary>
    public sealed class TabInfo { public string Id, Label; public bool On, Badge; }

    /// <summary>팝업 한 장의 화면 모델: 머리글·탭·본문 블록.</summary>
    public sealed class PanelModel
    {
        public PanelMode Mode;
        public string Heading;
        public bool ShowTabs, GearOn, ShowGear, Light, Peek;
        public List<TabInfo> Tabs = new List<TabInfo>();
        public List<Block> Blocks = new List<Block>();
        public string Live;      // 스크린 리더 안내(놓고 치운 결과)
    }

    public enum PanelMode { None, Mail, Dex, Growth, Deco, Shop, Settings, News }
}
