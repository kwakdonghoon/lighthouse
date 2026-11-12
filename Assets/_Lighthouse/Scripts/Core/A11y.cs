using System;
using System.Collections.Generic;
using System.Text;

namespace Lighthouse.Core
{
    /// <summary>스크린 리더가 읽을 한 칸(순수 데이터). 좌표는 화면 픽셀, 원점은 왼쪽 위.</summary>
    public sealed class A11yItem
    {
        public string Label;
        public string Role = "button";            // button | text
        public float X, Y, W, H;
        public object Tag;                        // 누르면 부를 대상(Unity 쪽이 GameObject 를 넣는다)
    }

    /// <summary>
    /// 접근성 라벨 규칙(순수 C#, dotnet 시험 가능). 원본(HTML)의 aria-label 59곳에 해당하는 읽을거리를 Unity 의 화면 요소에서 만든다.
    /// 실제 노드 등록은 Platform 의 ScreenReaderBridge(UnityEngine.Accessibility)가 한다.
    /// </summary>
    public static class A11y
    {
        /// <summary>
        /// 아이콘만 있는 단추의 읽을 이름 — 원본의 aria-label 그대로(HTML 556행 등: 설정 열기·소리 끄기·닫기·돌아가기·사진 찍기 …).
        /// 화면에 그려지는 글이 아니라 스크린 리더만 읽으므로 폰트 부분집합과 무관하다(그래서 ui.json 이 아니라 여기에 둔다).
        /// 열쇠 = GameObject 이름의 콜론 뒤(KeyOfName). menu 의 글은 폰트 부분집합에 없는 글자를 포함해 문자 코드(0xBA54 0xB274)로 적었다(글리프 시험이 코드 문자열을 훑기 때문).
        /// </summary>
        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "gear", "설정 열기" }, { "sound", "소리 끄기" }, { "close", "닫기" }, { "menu", new string(new[] { (char)0xBA54, (char)0xB274 }) }, { "back", "돌아가기" }, { "photo", "사진 찍기" },
            { "mail", "편지함" }, { "dex", "도감" }, { "growth", "성장" }, { "deco", "꾸미기" }, { "shop", "상점" },
        };
        public static string LabelFor(string key) => key != null && Labels.TryGetValue(key, out var v) ? v : null;

        /// <summary>TMP 리치 텍스트 태그(&lt;color=…&gt;, &lt;sprite …&gt; 등)와 줄바꿈·겹공백을 걷어 읽을 글만 남긴다.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length); bool tag = false, sp = true;
            foreach (char c in s)
            {
                if (c == '<') { tag = true; continue; }
                if (tag) { if (c == '>') tag = false; continue; }
                if (c == ' ' || c == '​' || char.IsWhiteSpace(c) || char.IsControl(c)) { if (!sp) { sb.Append(' '); sp = true; } continue; }
                sb.Append(c); sp = false;
            }
            return sb.ToString().Trim();
        }

        /// <summary>GameObject 이름 "hb:gear" → "gear" (콜론 앞은 버린다). 라벨 표(ui.json 의 a11y.gear)를 찾는 열쇠.</summary>
        public static string KeyOfName(string goName)
        {
            if (string.IsNullOrEmpty(goName)) return "";
            int i = goName.LastIndexOf(':'); string n = i >= 0 ? goName.Substring(i + 1) : goName;
            return n.Trim().ToLowerInvariant().Replace(' ', '_');
        }

        /// <summary>읽는 순서: 위에서 아래로, 같은 줄(세로 겹침이 높이의 절반 이상)이면 왼쪽에서 오른쪽으로. 라벨이 빈 칸은 뺀다.</summary>
        public static List<A11yItem> Order(IEnumerable<A11yItem> items)
        {
            var l = new List<A11yItem>();
            foreach (var it in items) if (it != null && !string.IsNullOrWhiteSpace(it.Label) && it.W > 0 && it.H > 0) l.Add(it);
            l.Sort((a, b) =>
            {
                float overlap = Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y);
                bool sameRow = overlap >= 0.5f * Math.Min(a.H, b.H);
                int c = sameRow ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y);
                return c != 0 ? c : string.CompareOrdinal(a.Label, b.Label);
            });
            return l;
        }

        /// <summary>바뀌었는지 보는 짧은 표시(라벨·역할·위치). 같으면 노드를 다시 만들지 않는다.</summary>
        public static string Signature(IList<A11yItem> items)
        {
            var sb = new StringBuilder();
            foreach (var it in items) sb.Append(it.Role).Append('|').Append(it.Label).Append('|').Append((int)it.X).Append(',').Append((int)it.Y).Append(',').Append((int)it.W).Append(',').Append((int)it.H).Append(';');
            return sb.ToString();
        }
    }
}
