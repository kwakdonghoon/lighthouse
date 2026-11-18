using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.Render;
using Lighthouse.UI;
using UnityEditor;
using UnityEngine;

namespace Lighthouse.EditorTools
{
    /// <summary>
    /// 도트 아이콘 작업 도구(Unity 에디터). 아이콘 정의 한 줄 "id;d=#2c3a4a,w=#c8d2e4,...;행|행|..."(8×8·16×16)을 만들고·검사하고·미리 본다.
    /// 정의는 IconBaker(동결 표)와 UiTheme.UnityIcons(Unity 교체 그림)에서 읽고, 교체 그림이 우선한다. 창: Lighthouse/PixelKit.
    /// </summary>
    public static class PixelKit
    {
        public sealed class Icon { public string Id; public Dictionary<char, string> Pal; public string[] Rows; public string Source; }

        public static Icon Parse(string line, string source)
        {
            var p = line.Split(';'); var pal = new Dictionary<char, string>();
            foreach (var kv in p[1].Split(',')) if (kv.Length >= 3) pal[kv[0]] = kv.Substring(2);
            return new Icon { Id = p[0], Pal = pal, Rows = p[2].Split('|'), Source = source };
        }

        public static string Line(string id, Dictionary<char, string> pal, IList<string> rows) => id + ";" + string.Join(",", pal.Select(kv => kv.Key + "=" + kv.Value)) + ";" + string.Join("|", rows);

        public static List<Icon> All()
        {
            var list = new List<Icon>();
            foreach (var id in IconBaker.Ids)
            {
                if (UiTheme.UnityIcons.ContainsKey(id)) continue;
                var d = IconBaker.Def(id); list.Add(new Icon { Id = id, Pal = d.Pal.ToDictionary(kv => kv.Key, kv => kv.Value), Rows = d.Rows, Source = "표" });
            }
            foreach (var kv in UiTheme.UnityIcons) list.Add(Parse(kv.Value, "Unity"));
            return list;
        }

        /// <summary>모양·팔레트 검사. 오류 목록을 돌려주고, notes 가 있으면 참고 사항(안 쓴 글자·외톨이 점·윤곽 비대칭)을 담는다. sym 이면 윤곽 비대칭도 오류.</summary>
        public static List<string> Lint(Icon ic, bool sym = false, List<string> notes = null)
        {
            var err = new List<string>(); int n = ic.Rows.Length;
            if (n != 8 && n != 16) err.Add("줄 수 " + n + " (8 또는 16 이어야 함)");
            for (int y = 0; y < n; y++) if (ic.Rows[y].Length != n) err.Add(y + "번째 줄 길이 " + ic.Rows[y].Length + " ≠ " + n);
            var used = new HashSet<char>(string.Concat(ic.Rows)); used.Remove('.');
            foreach (var c in used.Where(c => !ic.Pal.ContainsKey(c)).OrderBy(c => c)) err.Add("팔레트에 없는 글자 '" + c + "'");
            if (used.Count == 0) err.Add("빈 그림");
            if (notes != null) foreach (var c in ic.Pal.Keys.Where(c => !used.Contains(c)).OrderBy(c => c)) notes.Add("안 쓴 팔레트 글자 '" + c + "'");
            if (ic.Rows.Any(r => r.Length != n)) return err;
            bool M(int x, int y) => ic.Rows[y][x] != '.';
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    if (!M(x, y)) continue;
                    bool nb = (x + 1 < n && M(x + 1, y)) || (x > 0 && M(x - 1, y)) || (y + 1 < n && M(x, y + 1)) || (y > 0 && M(x, y - 1));
                    if (!nb && notes != null) notes.Add("외톨이 점 (" + x + "," + y + ")");
                }
            int lr = 0, ud = 0;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { if (M(x, y) != M(n - 1 - x, y)) lr++; if (M(x, y) != M(x, n - 1 - y)) ud++; }
            lr /= 2; ud /= 2;
            notes?.Add("윤곽 비대칭 좌우 " + lr + "점·상하 " + ud + "점");
            if (sym && (lr > 0 || ud > 0)) err.Add("윤곽이 대칭이 아님: 좌우 " + lr + "점·상하 " + ud + "점");
            return err;
        }

        static Color32 Hex(string h) { h = h.TrimStart('#'); return new Color32(Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16), 255); }

        /// <summary>정의를 격자 한 칸 = 한 점 텍스처로 그린다(zoom=1 이면 원본 크기, 투명 칸은 bg 색).</summary>
        public static Texture2D Render(Icon ic, int zoom, Color bg)
        {
            int n = ic.Rows.Length; var t = new Texture2D(n * zoom, n * zoom, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * zoom * n * zoom];
            for (int y = 0; y < n; y++) for (int x = 0; x < ic.Rows[y].Length && x < n; x++)
                {
                    Color32 c = ic.Pal.TryGetValue(ic.Rows[y][x], out var hx) ? Hex(hx) : (Color32)bg;
                    for (int dy = 0; dy < zoom; dy++) for (int dx = 0; dx < zoom; dx++) px[((n - 1 - y) * zoom + dy) * n * zoom + x * zoom + dx] = c;
                }
            t.SetPixels32(px); t.Apply(); return t;
        }

        /// <summary>윤곽(X/.)만 쓴 반쪽(quad 이면 1/4)을 대칭으로 펴고, 바깥 테두리 d · 안쪽 명암 h(왼쪽 위)/w/s(오른쪽 아래)로 칠한다. 구멍(hole)은 가운데 2×2 를 d 로.</summary>
        public static string Mirror(string id, Dictionary<char, string> pal, IEnumerable<string> half, bool quad, bool hole, List<string> errors)
        {
            var q = half.Select(l => l.Trim()).Where(l => l.Length > 0).ToList(); List<string> m;
            if (quad) { var top = q.Select(r => r + new string(r.Reverse().ToArray())).ToList(); m = top.Concat(Enumerable.Reverse(top)).ToList(); }
            else m = q.Select(r => r + new string(r.Reverse().ToArray())).ToList();
            int n = m.Count;
            if ((n != 8 && n != 16) || m.Any(r => r.Length != n)) { errors.Add("펼친 크기가 8×8 또는 16×16 이 아님: " + (m.Count > 0 ? m[0].Length : 0) + "×" + n); return null; }
            float c = (n - 1) / 2f; var rows = new List<string>();
            bool X(int x, int y) => x >= 0 && y >= 0 && x < n && y < n && m[y][x] == 'X';
            for (int y = 0; y < n; y++)
            {
                var sb = new StringBuilder();
                for (int x = 0; x < n; x++)
                {
                    if (!X(x, y)) { sb.Append('.'); continue; }
                    bool edge = !X(x + 1, y) || !X(x - 1, y) || !X(x, y + 1) || !X(x, y - 1);
                    if (edge || (hole && Mathf.Abs(x - c) <= 0.6f && Mathf.Abs(y - c) <= 0.6f)) { sb.Append('d'); continue; }
                    float t = (x - c) + (y - c); sb.Append(t < -n * 3f / 16f ? 'h' : t < n * 2.5f / 16f ? 'w' : 's');
                }
                rows.Add(sb.ToString());
            }
            return Line(id, pal, rows);
        }

        /// <summary>8×8·16×16 PNG(텍스처, 읽을 수 있어야 함)를 정의 한 줄로. 팔레트는 빌려 온 것(like)이고, 가까운 글자로 바꾼다(먼 색은 warn).</summary>
        public static string FromTexture(Texture2D t, string id, Dictionary<char, string> pal, List<string> warn, List<string> errors)
        {
            int n = t.width;
            if (t.width != t.height || (n != 8 && n != 16)) { errors.Add("8×8 또는 16×16 정사각형만 변환합니다(받은 크기 " + t.width + "×" + t.height + "). 큰 그림은 먼저 격자에 맞춰 1:1 로 줄이세요."); return null; }
            var prgb = pal.ToDictionary(kv => kv.Key, kv => Hex(kv.Value)); var rows = new List<string>(); int far = 0;
            for (int y = 0; y < n; y++)
            {
                var sb = new StringBuilder();
                for (int x = 0; x < n; x++)
                {
                    var p = t.GetPixel(x, n - 1 - y);
                    if (p.a < 0.5f) { sb.Append('.'); continue; }
                    char best = '.'; int bd = int.MaxValue; var pc = (Color32)p;
                    foreach (var kv in prgb) { int d = Sq(kv.Value.r - pc.r) + Sq(kv.Value.g - pc.g) + Sq(kv.Value.b - pc.b); if (d < bd) { bd = d; best = kv.Key; } }
                    if (bd > 40 * 40 * 3) far++;
                    sb.Append(best);
                }
                rows.Add(sb.ToString());
            }
            if (far > 0) warn.Add("팔레트와 먼 색 " + far + "점을 가장 가까운 글자로 바꿨습니다(눈으로 확인).");
            return Line(id, pal, rows);
        }
        static int Sq(int v) => v * v;

        /// <summary>프로젝트 안 PNG 에셋을 import 설정과 상관없이 읽는다.</summary>
        public static Texture2D ReadPng(Texture2D asset)
        {
            var path = AssetDatabase.GetAssetPath(asset); if (string.IsNullOrEmpty(path)) return asset;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(File.ReadAllBytes(path)); return t;
        }
    }

    /// <summary>Lighthouse/PixelKit 창: 아이콘 미리보기·검사, 반쪽 윤곽 → 대칭 아이콘, PNG → 정의 한 줄.</summary>
    public sealed class PixelKitWindow : EditorWindow
    {
        List<PixelKit.Icon> icons; string[] names; int sel, like;
        int zoom = 16; string mirrorText = "......XX\n......XX\n..XXX.XX\n..XXXXXX\n..XXXXXX\n...XXXXX\nXXXXXXXX\nXXXXXXXX"; bool quad = true, hole = true;
        string newId = "new_icon", outLine = "", outInfo = ""; Texture2D png; Vector2 scroll, mscroll;
        Texture2D prev; string lintText = "";

        [MenuItem("Lighthouse/PixelKit")] static void Open() { GetWindow<PixelKitWindow>("PixelKit"); }

        void OnEnable() { Reload(); }
        void OnDisable() { if (prev != null) DestroyImmediate(prev); }
        void Reload() { icons = PixelKit.All(); names = icons.Select(i => i.Id + (i.Source == "Unity" ? "  (Unity)" : "")).ToArray(); sel = Mathf.Clamp(sel, 0, icons.Count - 1); like = Mathf.Max(0, icons.FindIndex(i => i.Id == "gear")); Refresh(); }

        void Refresh()
        {
            if (prev != null) DestroyImmediate(prev);
            var ic = icons[sel]; prev = PixelKit.Render(ic, zoom, new Color(0.78f, 0.79f, 0.87f));
            var notes = new List<string>(); var err = PixelKit.Lint(ic, false, notes);
            lintText = (err.Count == 0 ? "오류 없음" : "오류 " + err.Count + "건\n" + string.Join("\n", err)) + (notes.Count > 0 ? "\n— 참고 —\n" + string.Join("\n", notes) : "");
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("아이콘 미리보기·검사", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            sel = EditorGUILayout.Popup("아이콘", sel, names); zoom = EditorGUILayout.IntSlider("확대", zoom, 4, 24);
            if (EditorGUI.EndChangeCheck()) Refresh();
            if (prev != null) { var r = GUILayoutUtility.GetRect(prev.width, prev.height, GUILayout.ExpandWidth(false)); EditorGUI.DrawPreviewTexture(r, prev); }
            EditorGUILayout.HelpBox(lintText, MessageType.None);
            if (GUILayout.Button("정의 한 줄 복사")) { var ic = icons[sel]; EditorGUIUtility.systemCopyBuffer = PixelKit.Line(ic.Id, ic.Pal, ic.Rows); }
            if (GUILayout.Button("전체 검사(모든 아이콘)")) { int bad = 0; foreach (var ic in icons) foreach (var e in PixelKit.Lint(ic)) { bad++; Debug.LogWarning("[PixelKit] " + ic.Id + ": " + e); } Debug.Log("[PixelKit] 검사 " + icons.Count + "개, 오류 " + bad + "건"); }

            EditorGUILayout.Space(10); EditorGUILayout.LabelField("새 아이콘 만들기", EditorStyles.boldLabel);
            newId = EditorGUILayout.TextField("새 id", newId); like = EditorGUILayout.Popup("팔레트 빌릴 아이콘", like, names);
            EditorGUILayout.LabelField("① 반쪽 윤곽(X = 채움, . = 투명)");
            mscroll = EditorGUILayout.BeginScrollView(mscroll, GUILayout.Height(110)); mirrorText = EditorGUILayout.TextArea(mirrorText); EditorGUILayout.EndScrollView();
            quad = EditorGUILayout.Toggle("1/4 만 그림(상하도 대칭)", quad); hole = EditorGUILayout.Toggle("가운데 구멍(2×2)", hole);
            if (GUILayout.Button("대칭 아이콘 만들기"))
            {
                var err = new List<string>(); outLine = PixelKit.Mirror(newId, icons[like].Pal, mirrorText.Split('\n'), quad, hole, err);
                outInfo = outLine == null ? string.Join("\n", err) : "만들었습니다. 복사해서 UiTheme.UnityIcons 에 붙이세요.";
            }
            EditorGUILayout.LabelField("② 또는 PNG(8×8·16×16, 읽기/쓰기 허용 불필요)");
            png = (Texture2D)EditorGUILayout.ObjectField("PNG", png, typeof(Texture2D), false);
            using (new EditorGUI.DisabledScope(png == null))
                if (GUILayout.Button("PNG → 정의 한 줄"))
                {
                    var warn = new List<string>(); var err = new List<string>(); var tex = PixelKit.ReadPng(png);
                    outLine = PixelKit.FromTexture(tex, newId, icons[like].Pal, warn, err); if (tex != png) DestroyImmediate(tex);
                    outInfo = outLine == null ? string.Join("\n", err) : string.Join("\n", warn.DefaultIfEmpty("변환했습니다."));
                }
            if (!string.IsNullOrEmpty(outInfo)) EditorGUILayout.HelpBox(outInfo, outLine == null ? MessageType.Error : MessageType.Info);
            if (!string.IsNullOrEmpty(outLine))
            {
                EditorGUILayout.SelectableLabel(outLine, EditorStyles.textArea, GUILayout.Height(60));
                if (GUILayout.Button("복사")) EditorGUIUtility.systemCopyBuffer = outLine;
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
