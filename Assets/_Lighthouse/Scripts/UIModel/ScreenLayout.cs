using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.UI.Model
{
    public readonly struct Insets
    {
        public readonly float L, T, R, B;
        public Insets(float l, float t, float r, float b) { L = l; T = t; R = r; B = b; }
        public static readonly Insets None = new Insets(0, 0, 0, 0);
    }

    public readonly struct LRect
    {
        public readonly float X, Y, W, H;       // 캔버스 단위, 왼쪽 위 기준
        public LRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float Right => X + W; public float Bottom => Y + H;
        public bool Overlaps(LRect o) => X < o.Right - 0.01f && o.X < Right - 0.01f && Y < o.Bottom - 0.01f && o.Y < Bottom - 0.01f;
        public bool Inside(LRect o) => X >= o.X - 0.01f && Y >= o.Y - 0.01f && Right <= o.Right + 0.01f && Bottom <= o.Bottom + 0.01f;
    }

    /// <summary>
    /// 화면 크기·안전 영역·팝업 카드 크기의 순수 계산 (원본 CSS 의 분기점을 옮김).
    /// 캔버스 배율은 uGUI CanvasScaler(Scale With Screen Size, 기준 360×640, Match 0.5)와 같은 식이다:
    /// scale = 2^((1-m)·log2(w/360) + m·log2(h/640)). 캡처·시험에서 임의 크기를 넣어 쓰려고 컴포넌트 대신 이 식을 직접 쓴다.
    /// </summary>
    public static class ScreenLayout
    {
        public const float RefW = 360f, RefH = 640f, Match = 0.5f, MinTouch = 44f;
        public const float CardMaxW = 440f;                 // 원본 #panel .card width: min(440px, 100%) (설계서의 480 은 추정값이었다)
        public const float SideGap = 10f, TopTall = 64f, TopLow = 10f, LowHeight = 700f;

        /// <summary>density(기기 픽셀 / dp)를 알면 그것이 배율 — 캔버스 단위가 dp 가 되어 원본 CSS 의 320 폭 분기가 그대로 맞는다.
        /// 모르면(0: 에디터·데스크톱) 위 CanvasScaler 식. 폰은 Screen.dpi/160(안드로이드 밀도 기준)을 넣는다.</summary>
        public static float Scale(float devW, float devH, float density = 0f)
            => density > 0f ? density : (float)Math.Pow(2.0, (1 - Match) * Math.Log(devW / RefW, 2.0) + Match * Math.Log(devH / RefH, 2.0));

        /// <summary>캔버스 단위로 본 화면 크기.</summary>
        public static void CanvasSize(float devW, float devH, out float w, out float h, float density = 0f) { float s = Scale(devW, devH, density); w = devW / s; h = devH / s; }

        /// <summary>도트 한 칸을 기기 픽셀 정수 k 개로(원본 setDot: k = max(2, round(2·dpr))). dpr 은 캔버스 배율로 근사한다. 캔버스 단위로 돌려준다.</summary>
        public static float DotUnits(float scale, out int deviceDotPx)
        {
            deviceDotPx = Math.Max(2, (int)Math.Round(2.0 * scale, MidpointRounding.AwayFromZero));
            return deviceDotPx / scale;
        }

        public static int GridColumns(float canvasW) => canvasW < 360f ? 3 : 4;      // 원본 @media (max-width:359px) grid 3열
        public static bool Narrow(float canvasW) => canvasW < 360f;
        public static bool Low(float canvasH) => canvasH <= LowHeight;

        /// <summary>팝업 카드(#panel .card)의 위치와 크기. 안전 영역 안쪽에 둔다.</summary>
        public static LRect PanelCard(float cw, float ch, Insets sa, bool decoScreen, bool peek = false, float peekH = 100f)
        {
            float w = Math.Min(CardMaxW, cw - 2 * SideGap - sa.L - sa.R);
            bool low = Low(ch);
            float top = (low ? TopLow : TopTall) + sa.T;
            float h;
            if (low) h = ch - 20f - sa.T;
            else
            {
                h = decoScreen ? Clamp(380f, 0.62f * ch, 600f) : Clamp(420f, 0.8f * ch, 760f);
                h = Math.Min(h, ch - 84f - sa.T);
            }
            h = Math.Min(h, ch - top - SideGap - sa.B);
            if (peek) h = peekH;
            float x = sa.L + (cw - sa.L - sa.R - w) / 2f;
            return new LRect(x, top, w, Math.Max(120f, h));
        }
        static float Clamp(float lo, float v, float hi) => Math.Max(lo, Math.Min(v, hi));

        /// <summary>토스트 위치: 가로 가운데, 위에서 22%.</summary>
        public static float ToastTop(float ch) => ch * 0.22f;
        /// <summary>토스트 글 최대 폭: 원본 #toast max-width calc(100vw - 16px) 에서 좌우 안쪽 여백(8칸씩, 칸 = dot)을 뺀 값. 이보다 긴 문장은 줄을 바꾼다.</summary>
        public static float ToastTextMaxW(float canvasW, float dot) => Math.Max(80f, canvasW - 16f - 2f * 8f * dot);
        /// <summary>HUD 알약 위쪽: 10 + 안전 영역.</summary>
        public static float HudTop(Insets sa) => 10f + sa.T;
    }

    /// <summary>화면에 올리기 직전의 글자 보정. Noto Sans KR 에 없는 ✦(U+2726)는 있는 ★(U+2605)로 보여 준다(글리프 누락 0 을 위해).</summary>
    public static class UiText
    {
        public static string Display(string s) => s == null ? null : s.Replace('✦', '★');
    }

    /// <summary>탭·메뉴 배지에 쓰는 글자(원본 updateBadges·menuBadge): 안 읽은 편지 수(100 이상은 99+), 도감 새 항목은 N.</summary>
    public static class Badges
    {
        public const string NewText = "N", OverText = "99+";
        public static string Count(int n) => n <= 0 ? "" : n >= 100 ? OverText : n.ToString();
        /// <summary>메뉴 단추: 안 읽은 편지 수, 없으면 도감 새 항목 N, 둘 다 없으면 빈 글.</summary>
        public static string Menu(int unread, bool dexNew) => unread > 0 ? Count(unread) : dexNew ? NewText : "";
    }

    /// <summary>한글 글리프 수집: 데이터·UI 문구 어디에서 쓰든 폰트 아틀라스에 미리 채울 글자 모음 (설계서 R5).</summary>
    public static class GlyphHarvest
    {
        /// <summary>표 전체(선물·배·편지 조각·일지·단골·꾸미기 이름…)와 UI 문구에서 쓰는 모든 글자.</summary>
        public static SortedSet<char> Collect(GameTables T, UiStrings ui)
        {
            var set = new SortedSet<char>();
            void Add(string s) { if (s == null) return; foreach (char c in UiText.Display(s)) if (c >= 0x20 && c != 0x7f) set.Add(c); }
            foreach (var g in T.Gifts) Add(g.Name);
            foreach (var s in T.Ships) Add(s.Name);
            foreach (var s in T.Sea) { Add(s.Name); Add(s.Where); Add(s.Hint); Add(s.Tag); }
            foreach (var s in T.SeaGroups) Add(s.Name);
            foreach (var n in T.NatureLetters) { Add(n.Nm); Add(n.Text); Add(n.Sign); }
            foreach (var s in T.StageName) Add(s); foreach (var s in T.StageRoles) Add(s);
            foreach (var t in T.Tracks) { Add(t.Name); Add(t.Desc); }
            foreach (var m in T.Milestones) Add(m.Text);
            foreach (var r in T.Ranks) Add(r.Name);
            foreach (var d in T.Diary) Add(d.Text);
            foreach (var r in T.Regulars) { Add(r.Ship); Add(r.Cap); if (r.Story != null) foreach (var s in r.Story) Add(s); if (r.Lines != null) foreach (var s in r.Lines) Add(s); }      // Lines = 말풍선 대사(M4 에서 늘어난 필드; 빠뜨리면 꿈·닌·죠 글리프가 비었다)
            foreach (var g in T.RareGuests) { Add(g.Ship); Add(g.Cap); Add(g.Text); Add(g.Hint); foreach (var s in g.Lines) Add(s); }
            foreach (var s in T.SeasonGuests) { Add(s.Cap); Add(s.Text); }
            foreach (var a in T.Anniv) { Add(a.Cap); Add(a.Text); }
            Add(T.Finale.Nm); Add(T.Finale.Sign); Add(T.Finale.Head); Add(T.Finale.None); Add(T.Finale.SomeSuffix); Add(T.Finale.Tail);
            foreach (var kv in T.FinaleVoices) { Add(kv.Key); Add(kv.Value); }
            var tx = T.Texts;
            foreach (var arr in new[] { tx.Open, tx.Day, tx.Night, tx.Mid, tx.Thanks, tx.Gift, tx.Close, tx.Captains, tx.ShipNames }) if (arr != null) foreach (var s in arr) Add(s);
            foreach (var d in new[] { tx.SeasonLetterDay, tx.SeasonLetterNight, tx.SeasonDiaryDay, tx.SeasonDiaryNight }) foreach (var a in d.Values) foreach (var s in a) Add(s);
            foreach (var s in tx.SeaOpen.Values) Add(s);
            foreach (var l in T.SkinLayers) foreach (var s in l.Skins.Values) Add(s.Name);
            foreach (var p in T.Products) { Add(p.Name); Add(p.Desc); }
            foreach (var s in T.ItemNames.Values) Add(s);
            foreach (var a in T.AdKinds) { Add(a.Name); Add(a.Desc); }
            foreach (var r in T.Regions) Add(r.Name);
            foreach (var z in T.DecorZones) { Add(z.Chip); Add(z.Title); }
            foreach (var i in T.DecorItems) { Add(i.Name); Add(i.Who); }
            foreach (var b in T.BeamColors) Add(b.Name);
            foreach (var kv in T.Bal.CondName) Add(kv.Value); foreach (var kv in T.Bal.CondShort) Add(kv.Value);
            foreach (var kv in T.Lint.DecoMsg) Add(kv.Value); foreach (var kv in T.Lint.SeasonName) Add(kv.Value);
            foreach (var kv in T.DecorLetters) Add(kv.Value);
            Add(Badges.NewText); Add(Badges.OverText);        // 탭·메뉴 배지 글자(N, 99+)
            if (ui != null) foreach (var s in ui.AllTexts()) Add(s);
            Add("0123456789 .,:;!?()[]{}<>+-×÷=%/·…~'\"가나다"); Add("▶◀▲▼★♥♡₩←→"); set.Add((char)160); Add("오늘어제그제자정새벽오전낮오후시쯤월일년분초간번째회척개종곳명통점레벨단계");      // 코드가 만드는 글(WhenParts·FmtAway 등)
            return set;
        }
    }
}
