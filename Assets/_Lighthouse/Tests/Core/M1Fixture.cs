using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Data;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Tests
{
    /// <summary>M1 시험 공용: 프로젝트 루트 찾기, 표 로드, 골든 읽기, JSON 차이 찾기. dotnet test 와 Unity EditMode 양쪽에서 같다.</summary>
    public static class M1Fixture
    {
        static string root; static GameTables tables; static JObject funcGolden;

        /// <summary>Tools/golden/m1 이 있는 프로젝트 루트(현재 폴더·실행 파일 위치에서 위로 올라가며 찾는다).</summary>
        public static string Root
        {
            get
            {
                if (root != null) return root;
                foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                    for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                        if (Directory.Exists(Path.Combine(d.FullName, "Tools", "golden", "m1")) && Directory.Exists(Path.Combine(d.FullName, "Assets", "_Lighthouse", "Resources", "Data"))) return root = d.FullName;
                throw new DirectoryNotFoundException("프로젝트 루트(Tools/golden/m1)를 찾지 못함");
            }
        }

        public static string DataDir => Path.Combine(Root, "Assets", "_Lighthouse", "Resources", "Data");
        public static GameTables Tables => tables ?? (tables = DataLoader.Load(new FolderDataSource(DataDir)));

        public static JObject Golden(string name) => (JObject)Js.Parse(File.ReadAllText(Path.Combine(Root, "Tools", "golden", "m1", name), new System.Text.UTF8Encoding(false)));

        static JObject FuncGolden => funcGolden ?? (funcGolden = Golden("func.json"));
        /// <summary>골든이 쓴 고정 시각(2026-11-11 03:30 UTC)과 시간대 오프셋(한국 +540).</summary>
        public static long FixMs => (long)Js.Num(FuncGolden["meta"]["fix"]);
        public static int TzMinutes => -(int)Js.Num(FuncGolden["meta"]["tzOffsetMinutes"]);      // JS getTimezoneOffset 은 부호 반대
        public static IClock Clock() => new FixedClock(FixMs, TzMinutes);
        public static SaveMigrator Migrator() => new SaveMigrator(Tables, Clock());

        /// <summary>두 JSON 의 첫 차이 위치와 값(같으면 null). 객체 키 순서는 무시, 수는 값으로 비교한다.</summary>
        public static string Diff(JToken actual, JToken expected, string path = "$")
        {
            if (actual == null || expected == null) return actual == expected ? null : path + ": " + Show(actual) + " != " + Show(expected);
            if (Js.IsNumber(actual) && Js.IsNumber(expected)) return Js.Num(actual) == Js.Num(expected) || (double.IsNaN(Js.Num(actual)) && double.IsNaN(Js.Num(expected))) ? null : path + ": " + Show(actual) + " != " + Show(expected);
            if (actual.Type != expected.Type) return path + ": 형 " + actual.Type + "(" + Show(actual) + ") != " + expected.Type + "(" + Show(expected) + ")";
            switch (actual.Type)
            {
                case JTokenType.Object:
                    {
                        var a = (JObject)actual; var e = (JObject)expected;
                        foreach (var p in e.Properties()) { if (!a.ContainsKey(p.Name)) return path + "." + p.Name + ": 없음(기대 " + Show(p.Value) + ")"; var r = Diff(a[p.Name], p.Value, path + "." + p.Name); if (r != null) return r; }
                        foreach (var p in a.Properties()) if (!e.ContainsKey(p.Name)) return path + "." + p.Name + ": 더 있음(" + Show(p.Value) + ")";
                        return null;
                    }
                case JTokenType.Array:
                    {
                        var a = (JArray)actual; var e = (JArray)expected;
                        if (a.Count != e.Count) return path + ": 길이 " + a.Count + " != " + e.Count;
                        for (int i = 0; i < a.Count; i++) { var r = Diff(a[i], e[i], path + "[" + i + "]"); if (r != null) return r; }
                        return null;
                    }
                default: return JToken.DeepEquals(actual, expected) ? null : path + ": " + Show(actual) + " != " + Show(expected);
            }
        }

        static string Show(JToken t) { if (t == null) return "undefined"; string s = Js.Stringify(t); return s.Length > 160 ? s.Substring(0, 160) + "…" : s; }
    }
}
