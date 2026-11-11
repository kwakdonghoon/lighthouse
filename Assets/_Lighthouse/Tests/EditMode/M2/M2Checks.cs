using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Lighthouse.Render;

namespace Lighthouse.Tests.M2
{
    /// <summary>
    /// M2 렌더 골든 대조 전부. 순수 C# 이라 dotnet 실행기(Tools/golden_runner)와 Unity EditMode 테스트가 같은 코드를 쓴다.
    /// 허용 오차 규칙은 Cmp.Diff 참고: 굽기 산출은 0, 밤 사본의 반투명 가장자리만 ±NightTol.
    /// </summary>
    public static class M2Checks
    {
        public const int NightTol = 1;                 // 밤 틴트: 반투명(그림자) 픽셀에 한해 ±1 (브라우저 GPU premultiplied 경로 차이, 부록 B-15)
        public static string Dir;                       // Tools/golden/m2/out

        static GoldenPack Pack(string f) => GoldenPack.Load(Path.Combine(Dir, f));

        static string[] SplitSp(string s) => s.Length == 0 ? new string[0] : s.Split(' ');

        class IsleMeta { public int N; public double[] V; public double[][] Lights; }

        static Dictionary<string, IsleMeta> Meta(string f)
        {
            var d = new Dictionary<string, IsleMeta>();
            foreach (var line in File.ReadAllLines(Path.Combine(Dir, f)))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(';');
                d[p[0]] = new IsleMeta
                {
                    N = int.Parse(p[1]),
                    V = SplitSp(p[2]).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray(),
                    Lights = p[3].Length == 0 ? new double[0][] : p[3].Split('|').Select(l => l.Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray()).ToArray()
                };
            }
            return d;
        }

        static void IsleSet(List<Result> res, string set, GoldenPack gp, Dictionary<string, IsleMeta> meta, string tag, IslandEnv env)
        {
            var r = new IslandBaker(env).Bake();
            res.Add(Cmp.Diff(set, tag, r.Island, gp.Items[tag]));
            var m = meta[tag];
            if (r.N != m.N) res.Add(new Result { Set = set, Name = tag + "/N", OverTol = 1, Note = $"N {r.N} != {m.N}" });
            if (r.Pieces.Count != m.V.Length) res.Add(new Result { Set = set, Name = tag + "/pieces", OverTol = 1, Note = $"조각 수 {r.Pieces.Count} != {m.V.Length}" });
            else
            {
                for (int i = 0; i < r.Pieces.Count; i++)
                {
                    res.Add(Cmp.Diff(set, tag + "_p" + i, r.Pieces[i].Spr, gp.Items[tag + "_p" + i]));
                    if (r.Pieces[i].V != m.V[i]) res.Add(new Result { Set = set, Name = tag + "_p" + i + "/v", OverTol = 1, Note = $"v {r.Pieces[i].V} != {m.V[i]}" });
                }
            }
            bool lightsOk = r.Lights.Count == m.Lights.Length;
            for (int i = 0; lightsOk && i < r.Lights.Count; i++)
                for (int j = 0; j < 3; j++) if (Math.Abs(r.Lights[i][j] - m.Lights[i][j]) > 1e-9) lightsOk = false;
            if (!lightsOk) res.Add(new Result { Set = set, Name = tag + "/lights", OverTol = 1, Note = "불빛 좌표 다름" });
        }

        /// <summary>섬: 계절 4 × 단계 6 (기본 외형) + 섬·마을 외형.</summary>
        public static List<Result> Islands()
        {
            var res = new List<Result>();
            foreach (var season in Palettes.Seasons)
            {
                var gp = Pack("isle_" + season + ".bin.gz"); var meta = Meta("isle_" + season + ".meta");
                for (int s = 1; s <= 6; s++) IsleSet(res, "isle_" + season, gp, meta, "s" + s, Palettes.IslandEnvFor(season, s));
            }
            {
                var gp = Pack("isle_skins.bin.gz"); var meta = Meta("isle_skins.meta");
                foreach (var (layer, id) in new[] { ("isle", "blossom"), ("isle", "maple"), ("decor", "seaside"), ("decor", "festival"), ("decor", "dev") })
                    foreach (int s in new[] { 3, 6 })
                        IsleSet(res, "isle_skins", gp, meta, layer + "_" + id + "_s" + s,
                            Palettes.IslandEnvFor("summer", s, layer == "isle" ? id : "classic", layer == "decor" ? id : "classic"));
            }
            return res;
        }

        /// <summary>탑 외형 15 × 단계 6: 뒤(A)·앞(B)·램프 낮·밤.</summary>
        public static List<Result> Towers()
        {
            var res = new List<Result>(); var gp = Pack("tower_skins.bin.gz");
            foreach (var sk in Palettes.TowerSkins)
                for (int s = 1; s <= 6; s++)
                {
                    var P = TowerParams.ForStage(s, 2, sk); string tag = "tw_" + sk.Id + "_s" + s;
                    res.Add(Cmp.Diff("tower", tag + "_A", TowerBaker.BakeA(P, sk), gp.Items[tag + "_A"]));
                    res.Add(Cmp.Diff("tower", tag + "_B", TowerBaker.BakeB(P), gp.Items[tag + "_B"]));
                    res.Add(Cmp.Diff("tower", tag + "_lampD", TowerBaker.BakeLamp(P, false), gp.Items[tag + "_lampD"]));
                    res.Add(Cmp.Diff("tower", tag + "_lampN", TowerBaker.BakeLamp(P, true), gp.Items[tag + "_lampN"]));
                }
            return res;
        }


        /// <summary>밤 사본·우체통·암초·물가.</summary>
        public static List<Result> Nights()
        {
            var res = new List<Result>(); var gp = Pack("night.bin.gz");
            void D(string name, Bitmap b, int tol = 0) => res.Add(Cmp.Diff("night", name, b, gp.Items[name], tol));
            for (int s = 1; s <= 6; s++)
            {
                var env = Palettes.IslandEnvFor("summer", s); var ir = new IslandBaker(env).Bake();
                D("night_isle_s" + s, Night.Of(ir.Island), NightTol);
                if (s == 6)
                {
                    for (int i = 0; i < ir.Pieces.Count; i++) D("night_isle_s6_p" + i, Night.Of(ir.Pieces[i].Spr), NightTol);
                    D("night_nt0.58_isle_s6", Night.Of(ir.Island, 1, 0.58), NightTol);
                    D("night_nt0.26_isle_s6", Night.Of(ir.Island, 1, 0.26), NightTol);
                    D("night_k035_isle_s6", Night.Of(ir.Island, 0.35), NightTol);
                }
                var P = TowerParams.ForStage(s);
                D("night_tw_s" + s + "_A", Night.Of(TowerBaker.BakeA(P)), NightTol); D("night_tw_s" + s + "_B", Night.Of(TowerBaker.BakeB(P)), NightTol);
            }
            foreach (bool up in new[] { true, false })
            {
                string t = up ? "up" : "dn"; var m = PropBaker.Mailbox(up);
                D("mail_" + t, m); D("night_mail_" + t, Night.Of(m), NightTol);
            }
            var reef = PropBaker.Reef(); D("reef", reef); D("night_reef", Night.Of(reef), NightTol);
            foreach (int st in new[] { 1, 3, 6 }) D("shore_s" + st, PropBaker.Shore(new[] { 0, 6, 6, 7, 8, 8, 9 }[st]));
            return res;
        }

        /// <summary>배경(해상도 4종 × 낮·밤)과 구름.</summary>
        public static List<Result> Backgrounds()
        {
            var res = new List<Result>(); var gp = Pack("bg.bin.gz");
            foreach (var (w, h) in new[] { (180, 320), (216, 468), (206, 446), (192, 256) })
                foreach (bool n in new[] { false, true })
                {
                    string name = "bg_" + w + "x" + h + "_" + (n ? "n" : "d");
                    res.Add(Cmp.Diff("bg", name, BgBaker.MakeBg(n, w, h, h - BgBaker.DesignH), gp.Items[name]));
                }
            foreach (bool n in new[] { false, true }) res.Add(Cmp.Diff("bg", "cloud_" + (n ? "n" : "d"), BgBaker.Cloud(n), gp.Items["cloud_" + (n ? "n" : "d")]));
            return res;
        }

        public static List<Result> RunAll(params string[] only)
        {
            var all = new List<Result>();
            bool On(string n) => only == null || only.Length == 0 || only.Contains(n);
            if (On("islands")) all.AddRange(Islands());
            if (On("towers")) all.AddRange(Towers());
            if (On("night")) all.AddRange(Nights());
            if (On("bg")) all.AddRange(Backgrounds());
            return all;
        }
    }
}
