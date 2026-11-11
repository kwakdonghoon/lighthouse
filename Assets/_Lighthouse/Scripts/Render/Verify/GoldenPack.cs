using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Lighthouse.Render;

namespace Lighthouse.Render.Verify
{
    /// <summary>M2 골든 묶음(Tools/golden/m2/out/*.bin.gz) 읽기. 형식은 Tools/golden/m2/srv/gx.js 의 Pack 참고. 순수 C# (dotnet 실행기와 Unity 테스트가 같이 쓴다).</summary>
    public sealed class GoldenPack
    {
        public sealed class Entry { public string Name; public int Ax, Ay, W, H; public uint[] D; }

        public readonly Dictionary<string, Entry> Items = new Dictionary<string, Entry>();
        public readonly List<string> Names = new List<string>();

        public static GoldenPack Load(string path)
        {
            var gp = new GoldenPack();
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                var b = ms.ToArray(); int o = 0;
                int I32() { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
                int n = I32();
                for (int k = 0; k < n; k++)
                {
                    int nl = I32(); string name = Encoding.UTF8.GetString(b, o, nl); o += nl;
                    var e = new Entry { Name = name, Ax = I32(), Ay = I32(), W = I32(), H = I32() };
                    e.D = new uint[e.W * e.H];
                    for (int i = 0; i < e.D.Length; i++) e.D[i] = BitConverter.ToUInt32(b, o + i * 4);
                    o += e.W * e.H * 4;
                    gp.Items[name] = e; gp.Names.Add(name);
                }
            }
            return gp;
        }

        public static Bitmap ToBitmap(Entry e) { var bm = new Bitmap(e.Ax, e.Ay, e.W, e.H); Array.Copy(e.D, bm.D, e.D.Length); return bm; }
    }

    /// <summary>비교 결과 한 줄.</summary>
    public sealed class Result
    {
        public string Set, Name;
        public long Pixels, ExactDiff, OverTol;   // ExactDiff: 값이 한 단계라도 다른 픽셀, OverTol: 허용 오차를 넘은 픽셀(알파가 다르거나 불투명 픽셀이 다르면 허용 안 함)
        public int MaxDelta;
        public string Note;
        public bool Ok => OverTol == 0 && Note == null;
        public override string ToString() => string.Format("{0} {1}/{2}: px={3} diff={4} overTol={5} maxD={6}{7}", Ok ? "OK  " : "FAIL", Set, Name, Pixels, ExactDiff, OverTol, MaxDelta, Note != null ? " " + Note : "");
    }

    public static class Cmp
    {
        /// <summary>
        /// tol = 0 이면 완전 일치. tol &gt; 0 이면 0&lt;알파&lt;255 인 반투명 픽셀의 RGB 채널만 ±tol 허용(알파·불투명·투명 픽셀은 항상 완전 일치).
        /// </summary>
        public static Result Diff(string set, string name, Bitmap act, GoldenPack.Entry exp, int tol = 0)
        {
            var r = new Result { Set = set, Name = name };
            if (exp == null) { r.Note = "골든 항목 없음"; r.OverTol = 1; return r; }
            r.Pixels = exp.D.Length;
            if (act.Ax != exp.Ax || act.Ay != exp.Ay || act.W != exp.W || act.H != exp.H)
            { r.Note = string.Format("크기/원점 다름 기대 ax={0} ay={1} {2}x{3} 실제 ax={4} ay={5} {6}x{7}", exp.Ax, exp.Ay, exp.W, exp.H, act.Ax, act.Ay, act.W, act.H); r.OverTol = 1; return r; }
            for (int i = 0; i < exp.D.Length; i++)
            {
                uint e = exp.D[i], a = act.D[i];
                if (e == a) continue;
                r.ExactDiff++;
                uint ea = e >> 24, aa = a >> 24; bool semi = ea > 0 && ea < 255;
                int dm = 0;
                for (int s = 0; s < 24; s += 8) dm = Math.Max(dm, Math.Abs((int)((e >> s) & 255) - (int)((a >> s) & 255)));
                if (ea != aa || !semi || dm > tol) { r.OverTol++; if (r.OverTol == 1) r.Note = string.Format("첫 차이 ({0},{1}) 기대 {2:x8} 실제 {3:x8}", i % exp.W, i / exp.W, e, a); }
                if (dm > r.MaxDelta) r.MaxDelta = dm;
            }
            if (r.OverTol == 0) r.Note = null;
            return r;
        }

        /// <summary>
        /// 화면 합성 프레임(전부 불투명) 비교. 채널 차이가 tol 이하면 허용. 값이 다른 픽셀 수(ExactDiff)와 허용 초과 수(OverTol), 최대 차이(MaxDelta)를 센다.
        /// </summary>
        public static Result DiffOpaque(string set, string name, Bitmap act, GoldenPack.Entry exp, int tol)
        {
            var r = new Result { Set = set, Name = name, Pixels = exp.D.Length };
            if (act.W != exp.W || act.H != exp.H) { r.Note = "크기 다름"; r.OverTol = 1; return r; }
            for (int i = 0; i < exp.D.Length; i++)
            {
                uint e = exp.D[i], a = act.D[i];
                if (e == a) continue;
                r.ExactDiff++;
                int dm = 0;
                for (int s = 0; s < 24; s += 8) dm = Math.Max(dm, Math.Abs((int)((e >> s) & 255) - (int)((a >> s) & 255)));
                if (dm > r.MaxDelta) r.MaxDelta = dm;
                if (dm > tol) { r.OverTol++; if (r.OverTol == 1) r.Note = string.Format("첫 허용 초과 ({0},{1}) 기대 {2:x8} 실제 {3:x8}", i % exp.W, i / exp.W, e, a); }
            }
            if (r.OverTol == 0) r.Note = null;
            return r;
        }
    }
}
