using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Render;

namespace Lighthouse.G2Tool
{
    public static partial class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Dictionary<string, int> MinStages = new Dictionary<string, int> { { "F2", 4 }, { "F3", 4 }, { "B1", 4 }, { "B4", 4 }, { "B5", int.Parse(Environment.GetEnvironmentVariable("MINB5") ?? "1") } };
        static double cap; static bool Quiet; static List<DecorSlot> LastChosen;
        static void Log(string m) { if (!Quiet) Console.WriteLine(m); }
        static bool InZone(string chip, double a, double b) => chip == "back" ? a + b <= -0.5 : chip == "front" ? a + b >= 0.5 : true;
        static double Dbl(string s) => double.Parse(s, Inv);

        static DecorSlot Cand(DecorSlot proto, double a, double b)
            => new DecorSlot { Id = proto.Id, Chip = proto.Chip, Size = proto.Size, OpenKind = proto.OpenKind, OpenVal = proto.OpenVal, MinStage = proto.MinStage, Ka = 0, Ca = a, Kb = 0, Cb = b };

        /// <summary>search [step] [margin] [ids...]: 막힌 자리를 차례로 놓는다(그리디). 후보 격자 전체를 겹침 → 가시율 순으로 거른다.</summary>
        static void Search(string[] a)
        {
            cap = 0.10; double step = a.Length > 1 ? Dbl(a[1]) : 0.5, margin = a.Length > 2 ? Dbl(a[2]) : 0.02;
            var order = a.Length > 3 ? a.Skip(3).Where(x => x != "debug" && x != "write").ToList() : new List<string> { "B2", "B1", "B6", "B4", "B5", "F7", "F3", "F2" };
            var set = DecorBaker.Slots.Where(s => !s.Blocked).ToList(); var items = DecorBaker.Items;
            var ctx = Enumerable.Range(1, 6).Select(st => DecorCheck.StageCtx.Build(st, set, items, false)).ToArray();
            var chosen = new List<DecorSlot>(); LastChosen = chosen;
            foreach (var id in order)
            {
                var proto = DecorBaker.Slot(id); if (proto.MinStage == 1 && MinStages.TryGetValue(id, out int ms)) proto.MinStage = ms; int s0 = Math.Max(proto.MinStage, proto.OpenKind == "stage" ? proto.OpenVal : 1);
                var found = new List<(double score, double ca, double cb, double[] per)>();
                for (double ca = -9; ca <= 9; ca += step)
                    for (double cb = -9; cb <= 9; cb += step)
                    {
                        if (!InZone(proto.Chip, ca, cb)) continue;
                        var c = Cand(proto, ca, cb); var t = new List<DecorSlot>(set) { c };
                        if (DecorCheck.SlotCheck(t).Violations.Count > 0) continue;
                        double score = 9; var per = new double[7]; bool ok = true;
                        for (int st = s0; st <= 6 && ok; st++)
                        {
                            double w = ctx[st - 1].Worst(c, new[] { ca, cb }, out _); per[st] = w; double m = w - DecorCheck.Threshold(c);
                            if (m < margin) ok = false; score = Math.Min(score, m);
                        }
                        if (ok) found.Add((Math.Min(score, cap) * 100 - Math.Sqrt((ca - proto.Ca) * (ca - proto.Ca) + (cb - proto.Cb) * (cb - proto.Cb)), ca, cb, per));
                    }
                if (found.Count == 0 && a.Contains("debug"))
                {
                    var diag = new List<(double sc, double ca, double cb, string s)>();
                    for (double ca = -9; ca <= 9; ca += step)
                        for (double cb = -9; cb <= 9; cb += step)
                        {
                            var c = Cand(proto, ca, cb); var t = new List<DecorSlot>(set) { c };
                            if (DecorCheck.SlotCheck(t).Violations.Count > 0) continue;
                            double sc = 9; string w = "";
                            for (int st = s0; st <= 6; st++) { double ww = ctx[st - 1].Worst(c, new[] { ca, cb }, out var wi); if (ww - DecorCheck.Threshold(c) < sc) { sc = ww - DecorCheck.Threshold(c); w = st + ":" + wi + " " + (ww * 100).ToString("F0"); } }
                            diag.Add((sc, ca, cb, w));
                        }
                    diag.Sort((x, y) => y.sc.CompareTo(x.sc));
                    Log("  겹침 통과 후보 " + diag.Count + " 개, 최고 5:"); foreach (var d in diag.Take(5)) Log("    (" + d.ca + "," + d.cb + ") 여유 " + d.sc.ToString("F2") + " " + d.s);
                }
                found.Sort((x, y) => y.score.CompareTo(x.score));
                Log(id + " 후보 " + found.Count + " (겹침 0, 자기 가시 기준+" + margin + ")");
                bool placed = false;
                foreach (var f in found.Take(400))
                {
                    var c = Cand(proto, f.ca, f.cb); var t = new List<DecorSlot>(set) { c };
                    var vis = DecorCheck.Visibility(t, items); if (vis.Bad.Count > 0) continue;
                    foreach (var cx in ctx) cx.SetSlots(t, false);
                    set.Add(c); chosen.Add(c); placed = true;
                    Log("  선택 " + id + " (" + f.ca + "," + f.cb + ") 점수 " + f.score.ToString("F3") + " 단계별 " + string.Join(" ", f.per.Skip(s0).Select(x => (x * 100).ToString("F0"))) + " | 전체 최소 " + (vis.Min * 100).ToString("F1") + " " + vis.MinAt);
                    break;
                }
                if (!placed) { Log("  배치 못함: " + id); foreach (var f in found.Take(5)) Log("    (" + f.ca + "," + f.cb + ") " + f.score.ToString("F3")); }
            }
            Log("-- 결과 --");
            foreach (var c in chosen) Log(Json(c));
            if (a.Contains("write"))
            {
                string path = System.IO.Path.Combine(Root(), "Tools", "g2_slots.json");
                var j = System.IO.File.Exists(path) ? Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path)) : new Newtonsoft.Json.Linq.JObject();
                foreach (var c in chosen) j[c.Id] = Newtonsoft.Json.Linq.JObject.Parse(Json(c).Substring(Json(c).IndexOf(':') + 1).TrimEnd(','));
                System.IO.File.WriteAllText(path, j.ToString(Newtonsoft.Json.Formatting.Indented) + (char)10);
                Log("wrote " + path);
            }
        }

        /// <summary>searchrand n seed: 무작위 순서 n 번의 그리디 중 새 8자리 최소 여유가 가장 큰 순서를 찾는다.</summary>
        static void SearchRand(string[] a)
        {
            int n = int.Parse(a[1]); var rnd = new Random(int.Parse(a[2])); var ids = new List<string> { "B5", "B4", "B1", "B6", "B2", "F7", "F3", "F2" };
            double best = -9; string bestOrder = ""; Quiet = true;
            for (int k = 0; k < n; k++)
            {
                var ord = ids.OrderBy(x => rnd.Next()).ToList();
                Search(new[] { "search", "0.25", "0.03" }.Concat(ord).ToArray());
                if (LastChosen.Count < 8) continue;
                var vis = DecorCheck.Visibility(DecorBaker.Slots.Where(s => !s.Blocked).Concat(LastChosen).ToList(), DecorBaker.Items);
                double m = 9; foreach (var c in LastChosen) for (int st = 1; st <= 6; st++) if (vis.Rows[st].TryGetValue(c.Id, out var w)) m = Math.Min(m, w - DecorCheck.Threshold(c));
                Console.WriteLine(string.Join(" ", ord) + " -> 최소 여유 " + m.ToString("F3") + " 전체최소 " + vis.Min.ToString("F3"));
                if (m > best) { best = m; bestOrder = string.Join(" ", ord); }
            }
            Console.WriteLine("best " + best.ToString("F3") + " " + bestOrder);
        }

        static string Json(DecorSlot c)
        {
            string body = "{\"ab\": [" + c.Ca.ToString(Inv) + ", " + c.Cb.ToString(Inv) + "], \"blocked\": 0";
            if (c.OpenKind == "base" && c.MinStage > 1) body += ", \"open\": {\"stage\": " + c.MinStage + "}";
            else if (c.MinStage > 1) body += ", \"minStage\": " + c.MinStage;
            return "\"" + c.Id + "\": " + body + "},";
        }
    }
}
