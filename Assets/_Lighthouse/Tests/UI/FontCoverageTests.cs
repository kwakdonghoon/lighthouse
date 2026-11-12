using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Lighthouse.Core;
using Lighthouse.UI.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>
    /// 글리프 전수 시험 (콘텐츠 검토 P1-1): "수집 함수(GlyphHarvest)가 모은 글자"가 아니라 **실제 JSON 문자열 전부 + 화면에 나갈 코드 문자열**의 글자가
    /// 폰트 파일(Art/UI/Fonts/*-Subset.ttf)의 cmap 에 있는지 본다. 수집 범위 밖 필드(예: 단골 말풍선 대사 Lines 의 꿈·닌·죠)가 빠져도 여기서 잡힌다.
    /// Unity 없이 dotnet test 로도 돈다(TTF cmap 을 직접 읽는다).
    /// </summary>
    public class FontCoverageTests
    {
        static string FontDir => Path.Combine(M1Fixture.Root, "Assets", "_Lighthouse", "Art", "UI", "Fonts");
        static readonly string[] Weights = { "Regular", "Bold" };

        // ---------- 최소 TTF cmap 읽기 (형식 4·12) ----------
        static ushort U16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
        static uint U32(byte[] b, int o) => ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

        public static HashSet<int> ReadCmap(string ttfPath)
        {
            var b = File.ReadAllBytes(ttfPath); var set = new HashSet<int>();
            int n = U16(b, 4), cmap = -1;
            for (int i = 0; i < n; i++) { int r = 12 + 16 * i; if (Encoding.ASCII.GetString(b, r, 4) == "cmap") cmap = (int)U32(b, r + 8); }
            Assert.GreaterOrEqual(cmap, 0, ttfPath + ": cmap 표 없음");
            int sub = U16(b, cmap + 2);
            for (int i = 0; i < sub; i++)
            {
                int rec = cmap + 4 + 8 * i; int plat = U16(b, rec), off = cmap + (int)U32(b, rec + 4); int fmt = U16(b, off);
                if (fmt == 4)
                {
                    int segX2 = U16(b, off + 6), seg = segX2 / 2, endO = off + 14, startO = endO + segX2 + 2, deltaO = startO + segX2, rangeO = deltaO + segX2;
                    for (int s = 0; s < seg; s++)
                    {
                        int end = U16(b, endO + 2 * s), start = U16(b, startO + 2 * s), delta = U16(b, deltaO + 2 * s), ro = U16(b, rangeO + 2 * s);
                        for (int c = start; c <= end && c < 0xFFFF; c++)
                        {
                            int gid = ro == 0 ? (c + delta) & 0xFFFF : U16(b, rangeO + 2 * s + ro + 2 * (c - start));
                            if (gid != 0 && ro != 0) gid = (gid + delta) & 0xFFFF;
                            if (gid != 0) set.Add(c);
                        }
                    }
                }
                else if (fmt == 12)
                {
                    uint groups = U32(b, off + 12);
                    for (uint g = 0; g < groups; g++)
                    {
                        int go = off + 16 + 12 * (int)g; uint sc = U32(b, go), ec = U32(b, go + 4), sg = U32(b, go + 8);
                        for (uint c = sc; c <= ec; c++) if (sg + (c - sc) != 0) set.Add((int)c);
                    }
                }
            }
            return set;
        }

        // ---------- 검사할 글자 모으기 ----------
        /// <summary>화면에 나가는 글자인지: ASCII 영문자(식별자·키)는 건너뛴다. 한글·숫자·기호는 모두 폰트에 있어야 한다.</summary>
        static bool MustHave(char c) => c >= 0x20 && c != 0x7f && !((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) && c != 160;

        static void Walk(JToken t, string where, Action<string, string> add)
        {
            if (t is JValue v) { if (v.Type == JTokenType.String) add((string)v.Value, where); }
            else if (t is JObject o) foreach (var p in o.Properties()) Walk(p.Value, where + "/" + p.Name, add);
            else if (t is JArray a) for (int i = 0; i < a.Count; i++) Walk(a[i], where + "[" + i + "]", add);
        }

        /// <summary>Resources/Data 의 모든 JSON 글(art.json 은 글이 있는 TIPS·DECO_MSG·ROLES·SEASON_NAME 만, lint/ 는 정규식이라 제외).</summary>
        public static List<(string text, string where)> JsonTexts()
        {
            var res = new List<(string, string)>();
            foreach (var f in Directory.GetFiles(M1Fixture.DataDir, "*.json", SearchOption.AllDirectories))
            {
                string rel = f.Substring(M1Fixture.DataDir.Length + 1).Replace('\\', '/');
                if (rel.StartsWith("lint/")) continue;
                var root = JToken.Parse(File.ReadAllText(f, new UTF8Encoding(false)));
                if (rel == "art.json") { foreach (var k in new[] { "TIPS", "DECO_MSG", "ROLES", "SEASON_NAME" }) if (root[k] != null) Walk(root[k], rel + ":" + k, (s, w) => res.Add((s, w))); continue; }
                Walk(root, rel, (s, w) => { if (!w.Contains("/rows[")) res.Add((s, w)); });      // rows[] = 장식 그림의 점 무늬 줄('*' 등), 글이 아니다
            }
            return res;
        }

        static string StripComments(string src)
        {
            var sb = new StringBuilder();
            foreach (var line in src.Split('\n'))
            {
                int q = 0, cut = -1;
                for (int i = 0; i < line.Length - 1; i++)
                {
                    if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) q++;
                    if (q % 2 == 0 && line[i] == '/' && line[i + 1] == '/') { cut = i; break; }
                }
                sb.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>화면에 나갈 수 있는 코드 문자열 안의 한글(검사 도구·시험 도구·로그 전용 파일은 제외).</summary>
        public static List<(string text, string where)> CodeTexts()
        {
            var res = new List<(string, string)>();
            string scripts = Path.Combine(M1Fixture.Root, "Assets", "_Lighthouse", "Scripts");
            foreach (var sub in new[] { "Core", "UIModel", "UI", "World", "App" })
                foreach (var f in Directory.GetFiles(Path.Combine(scripts, sub), "*.cs", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(f);
                    if (name == "ContentLint.cs" || name == "Simulator.cs" || name == "LoopSim.cs" || name == "SampleState.cs" || name == "M3UiDemo.cs") continue;      // 검사·시뮬·샘플·데모(화면 문구 아님)
                    foreach (Match m in Regex.Matches(StripComments(File.ReadAllText(f, new UTF8Encoding(false))), "\"(?:[^\"\\\\\\n]|\\\\.)*\""))
                        if (!m.Value.Contains("-힣")) res.Add((m.Value, name));
                }
            return res;
        }

        static bool Hangul(char c) => c >= 0xAC00 && c <= 0xD7A3;

        [TestCase("Regular"), TestCase("Bold")]
        public void FontCmapCoversEveryJsonString(string weight)
        {
            var cmap = ReadCmap(Path.Combine(FontDir, "NotoSansKR-" + weight + "-Subset.ttf"));
            var lost = new SortedDictionary<char, string>();
            foreach (var (text, where) in JsonTexts())
                foreach (char c in UiText.Display(text)) if (MustHave(c) && !cmap.Contains(c) && !lost.ContainsKey(c)) lost[c] = where;
            Assert.That(lost, Is.Empty, weight + " 폰트에 없는 글자: " + string.Join(", ", lost.Select(kv => kv.Key + "(" + kv.Value + ")")));
        }

        [TestCase("Regular"), TestCase("Bold")]
        public void FontCmapCoversHangulInCodeStrings(string weight)
        {
            var cmap = ReadCmap(Path.Combine(FontDir, "NotoSansKR-" + weight + "-Subset.ttf"));
            var lost = new SortedDictionary<char, string>();
            foreach (var (text, where) in CodeTexts())
                foreach (char c in UiText.Display(text)) if (Hangul(c) && !cmap.Contains(c) && !lost.ContainsKey(c)) lost[c] = where;
            Assert.That(lost, Is.Empty, weight + " 폰트에 없는 코드 문자열 한글: " + string.Join(", ", lost.Select(kv => kv.Key + "(" + kv.Value + ")")));
        }

        [TestCase("Regular"), TestCase("Bold")]
        public void FontCmapCoversEveryHarvestedGlyph(string weight)
        {
            var cmap = ReadCmap(Path.Combine(FontDir, "NotoSansKR-" + weight + "-Subset.ttf"));
            var set = GlyphHarvest.Collect(M1Fixture.Tables, UiFixture.Strings());
            var lost = set.Where(c => !cmap.Contains(c)).ToList();
            Assert.That(lost, Is.Empty, weight + " 폰트에 없는 수집 글자: " + new string(lost.ToArray()));
        }

        [Test]
        public void HarvestIncludesRegularLinesAndBadgeText()
        {
            var T = M1Fixture.Tables; var set = GlyphHarvest.Collect(T, UiFixture.Strings());
            Assert.That(T.Regulars.Sum(r => r.Lines.Length), Is.GreaterThan(0), "단골 대사가 읽혀야 한다");
            foreach (var r in T.Regulars) foreach (var l in r.Lines) foreach (char c in l) if (c != ' ') Assert.IsTrue(set.Contains(c), "대사 글자 수집 빠짐: " + c + " (" + l + ")");
            foreach (char c in "꿈닌죠N+9") Assert.IsTrue(set.Contains(c), "글자 수집 빠짐: " + c);
        }
    }
}
