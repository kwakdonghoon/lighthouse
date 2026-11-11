using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.Core;
using Lighthouse.Render;
using NUnit.Framework;
using UnityEngine;

namespace Lighthouse.Tests
{
    /// <summary>HTML 골든(Tools/golden)과의 픽셀·수치 비교. 차이 0 이 목표 (이식 설계서 M0 완료 기준).</summary>
    public static class GoldenIO
    {
        public static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/golden"));

        public struct Meta { public int Ax, Ay, W, H; }

        static Dictionary<string, Meta> index;
        public static Meta MetaOf(string name)
        {
            if (index == null)
            {
                index = new Dictionary<string, Meta>();
                foreach (var line in File.ReadAllLines(Path.Combine(Dir, "index.csv")))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var p = line.Split(',');
                    index[p[0]] = new Meta { Ax = int.Parse(p[1]), Ay = int.Parse(p[2]), W = int.Parse(p[3]), H = int.Parse(p[4]) };
                }
            }
            return index[name];
        }

        public static uint[] ReadRgba(string name)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Dir, name + ".rgba"));
            var d = new uint[bytes.Length / 4];
            for (int i = 0; i < d.Length; i++) d[i] = BitConverter.ToUInt32(bytes, i * 4);     // 리틀엔디언 R,G,B,A = 0xAABBGGRR
            return d;
        }

        public static double[][] ReadCsv(string file)
        {
            return File.ReadAllLines(Path.Combine(Dir, file)).Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        }

        /// <summary>같으면 null, 다르면 사람이 읽을 차이 설명(차이 개수·경계 상자·앞 5개).</summary>
        public static string Diff(string name, Bitmap act)
        {
            var m = MetaOf(name);
            var exp = ReadRgba(name);
            var sb = new StringBuilder();
            if (act.Ax != m.Ax || act.Ay != m.Ay || act.W != m.W || act.H != m.H)
                sb.Append($"[{name}] 크기/원점 다름: 기대 ax={m.Ax} ay={m.Ay} {m.W}x{m.H}, 실제 ax={act.Ax} ay={act.Ay} {act.W}x{act.H}. ");
            if (act.D.Length != exp.Length) { sb.Append($"픽셀 수 다름 {exp.Length} vs {act.D.Length}"); return sb.ToString(); }
            int n = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1; var first = new List<string>();
            for (int i = 0; i < exp.Length; i++)
            {
                if (exp[i] == act.D[i]) continue;
                n++; int x = i % m.W, y = i / m.W;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                if (first.Count < 5) first.Add($"({x},{y}) 기대 {exp[i]:x8} 실제 {act.D[i]:x8}");
            }
            if (n == 0 && sb.Length == 0) return null;
            if (n > 0) sb.Append($"[{name}] 다른 픽셀 {n}/{exp.Length}, 범위 x{x0}..{x1} y{y0}..{y1}: {string.Join("; ", first)}");
            return sb.ToString();
        }

        public static void DumpPng(string path, Bitmap b)
        {
            var tex = new Texture2D(b.W, b.H, TextureFormat.RGBA32, false);
            var px = new Color32[b.D.Length];
            for (int y = 0; y < b.H; y++)
                for (int x = 0; x < b.W; x++)
                {
                    uint u = b.D[y * b.W + x];
                    px[(b.H - 1 - y) * b.W + x] = new Color32((byte)(u & 255), (byte)((u >> 8) & 255), (byte)((u >> 16) & 255), (byte)(u >> 24));
                }
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }

    public class JsMathTests
    {
        [Test]
        public void Round_FollowsJsHalfUp()
        {
            Assert.AreEqual(3.0, JsMath.Round(2.5));
            Assert.AreEqual(2.0, JsMath.Round(1.5));
            Assert.AreEqual(0.0, JsMath.Round(-0.5));           // JS: -0 (비교는 0 과 같다)
            Assert.AreEqual(-2.0, JsMath.Round(-2.5));
            Assert.AreEqual(0.0, JsMath.Round(0.49999999999999994));
            Assert.AreEqual(1.0, JsMath.Round(0.5));
            Assert.AreEqual(-3.0, JsMath.Round(-2.6));
            Assert.AreNotEqual(Math.Round(2.5), JsMath.Round(2.5), "C# Math.Round(2.5)=2 (은행가)와 달라야 한다");
        }

        [Test]
        public void Hsh_MatchesGoldenGrid()
        {
            var bytes = File.ReadAllBytes(Path.Combine(GoldenIO.Dir, "hsh_grid.u32"));
            const int x0 = -128, n = 384;
            Assert.AreEqual(n * n * 4, bytes.Length);
            int bad = 0; string first = null;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    uint exp = BitConverter.ToUInt32(bytes, (y * n + x) * 4);
                    // 골든은 Math.round(hsh*2^32): hsh 는 uint/2^32 이므로 정확히 uint 와 같다
                    uint act = JsMath.HshRaw(x0 + x, x0 + y);
                    if (exp != act) { bad++; if (first == null) first = $"({x0 + x},{x0 + y}) 기대 {exp} 실제 {act}"; }
                }
            Assert.AreEqual(0, bad, "hsh 불일치 " + first);
        }
    }

    public class BakeGoldenTests
    {
        static string OutDir
        {
            get
            {
                var d = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/golden_diff"));
                Directory.CreateDirectory(d); return d;
            }
        }

        static void Check(string name, Bitmap act)
        {
            string diff = GoldenIO.Diff(name, act);
            if (diff != null) { GoldenIO.DumpPng(Path.Combine(OutDir, name + "_actual.png"), act); File.AppendAllText(Path.Combine(OutDir, "diff.txt"), diff + "\n"); }
            Assert.IsNull(diff, diff);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void Island_MatchesHtmlGolden(int stage)
        {
            var r = new IslandBaker(new IslandEnv { Stage = stage }).Bake();
            Check($"island_s{stage}", r.Island);
            var pv = GoldenIO.ReadCsv("pieces_v.csv").Where(row => (int)row[0] == stage).OrderBy(row => row[1]).ToArray();
            Assert.AreEqual(pv.Length, r.Pieces.Count, $"s{stage} 풍경 조각 개수");
            var fails = new List<string>();
            for (int i = 0; i < r.Pieces.Count; i++)
            {
                if (Math.Abs(pv[i][2] - r.Pieces[i].V) > 1e-9) fails.Add($"조각{i} v 기대 {pv[i][2]} 실제 {r.Pieces[i].V}");
                string d = GoldenIO.Diff($"island_s{stage}_piece{i}", r.Pieces[i].Spr);
                if (d != null) { fails.Add(d); GoldenIO.DumpPng(Path.Combine(OutDir, $"island_s{stage}_piece{i}_actual.png"), r.Pieces[i].Spr); }
            }
            var lights = GoldenIO.ReadCsv("lights.csv").Where(row => (int)row[0] == stage).OrderBy(row => row[1]).ToArray();
            if (lights.Length != r.Lights.Count) fails.Add($"불빛 개수 기대 {lights.Length} 실제 {r.Lights.Count}");
            else for (int i = 0; i < lights.Length; i++)
                    for (int c = 0; c < 3; c++)
                        if (Math.Abs(lights[i][2 + c] - r.Lights[i][c]) > 1e-9) fails.Add($"불빛{i}[{c}] 기대 {lights[i][2 + c]} 실제 {r.Lights[i][c]}");
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        [TestCase(1, 0, "tower_s1_v0")] [TestCase(1, 1, "tower_s1_v1")] [TestCase(1, 2, "tower_s1_v2")]
        [TestCase(2, 2, "tower_s2")] [TestCase(3, 2, "tower_s3")] [TestCase(4, 2, "tower_s4")] [TestCase(5, 2, "tower_s5")] [TestCase(6, 2, "tower_s6")]
        public void TowerAB_MatchesHtmlGolden(int stage, int t1v, string tag)
        {
            var P = TowerParams.ForStage(stage, t1v);
            Check(tag + "_A", TowerBaker.BakeA(P));
            Check(tag + "_B", TowerBaker.BakeB(P));
        }

        [Test]
        public void Bake_IsDeterministic()
        {
            var a = new IslandBaker(new IslandEnv { Stage = 6 }).Bake();
            var b = new IslandBaker(new IslandEnv { Stage = 6 }).Bake();
            CollectionAssert.AreEqual(a.Island.D, b.Island.D);
        }
    }
}
