using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Lighthouse.Render;
using Lighthouse.Render.Verify;

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


        /// <summary>배: 도면 글자 격자(생성 로직) + 스프라이트 외형 5 × 종 9 × 항로 3 × (깃발 뺀 것/도움받은 것) + 밤 사본.</summary>
        public static List<Result> Ships()
        {
            var res = new List<Result>(); var gp = Pack("ships.bin.gz");
            int rowsBad = 0, rowsN = 0;
            foreach (var line in File.ReadAllLines(Path.Combine(Dir, "ship_rows.txt")))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(';'); rowsN++;
                var d = ShipBaker.Design(p[0], int.Parse(p[1]));
                if (d.Ox != int.Parse(p[2]) || d.Oy != int.Parse(p[3]) || string.Join("|", d.Rows) != p[4]) { rowsBad++; res.Add(new Result { Set = "ships", Name = "rows_" + p[0] + "_" + p[1], OverTol = 1, Note = "도면 글자 격자 다름" }); }
            }
            res.Add(new Result { Set = "ships", Name = "rows(" + rowsN + ")", Pixels = rowsN, OverTol = rowsBad });
            foreach (var skin in ShipBaker.SkinIds)
                foreach (var t in ShipBaker.Types)
                    for (int lane = 0; lane < 3; lane++)
                    {
                        string tag = "ship_" + skin + "_" + t + "_" + lane;
                        var d = ShipBaker.Sprite(t, lane, skin, false); var h = ShipBaker.Sprite(t, lane, skin, true);
                        res.Add(Cmp.Diff("ships", tag + "_d", d, gp.Items[tag + "_d"])); res.Add(Cmp.Diff("ships", tag + "_h", h, gp.Items[tag + "_h"]));
                        if (skin == "classic")
                        {
                            res.Add(Cmp.Diff("ships", tag + "_n", Night.Of(d), gp.Items[tag + "_n"], NightTol));
                            res.Add(Cmp.Diff("ships", tag + "_hn", Night.Of(h, 0.35), gp.Items[tag + "_hn"], NightTol));
                        }
                    }
            return res;
        }

        /// <summary>아이콘 68종 + 대체 아이콘.</summary>
        public static List<Result> Icons()
        {
            var res = new List<Result>(); var gp = Pack("icons.bin.gz");
            foreach (var id in IconBaker.Ids) res.Add(Cmp.Diff("icons", "icon_" + id, IconBaker.Bake(id), gp.Items["icon_" + id]));
            res.Add(Cmp.Diff("icons", "icon_unknown", IconBaker.Bake("__nope__"), gp.Items["icon_unknown"]));
            res.Add(new Result { Set = "icons", Name = "count", Pixels = IconBaker.Ids.Count, OverTol = IconBaker.Ids.Count == 68 ? 0 : 1, Note = IconBaker.Ids.Count == 68 ? null : "아이콘 수 68 아님" });
            return res;
        }

        /// <summary>꾸미기: 굽기(아이템 × 계절 × 마을 외형, 낮·밤)·목록 아이콘·자리 좌표(28×6)·장면 목록(A/B 두 패스 순서).</summary>
        public static List<Result> Decor()
        {
            var res = new List<Result>(); var gp = Pack("decor.bin.gz");
            foreach (var it in DecorBaker.Items)
                foreach (var sn in it.Ss ? Palettes.Seasons : new[] { "summer" })
                    foreach (var sk in it.Sk ? new[] { "classic", "seaside", "festival" } : new[] { "classic" })
                    {
                        string tag = it.Id + "_" + sn + "_" + sk; var b = DecorBaker.Bake(it, sn, sk);
                        res.Add(Cmp.Diff("decor", "decor_" + tag, b, gp.Items["decor_" + tag]));
                        res.Add(Cmp.Diff("decor", "decorN_" + tag, Night.Of(b), gp.Items["decorN_" + tag], NightTol));
                    }
            foreach (var it in DecorBaker.Items) res.Add(Cmp.Diff("decor", "decoicon_" + it.Id, DecorBaker.Icon(it), gp.Items["decoicon_" + it.Id]));
            // 자리 좌표
            int posBad = 0, posN = 0;
            foreach (var line in File.ReadAllLines(Path.Combine(Dir, "decor_pos.txt")))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(';'); posN++; int st = int.Parse(p[0]); var sl = DecorBaker.Slot(p[1]);
                var (x, y) = DecorLayout.Pos(sl, st, 180); double v = DecorLayout.V(sl, st);
                if (x != int.Parse(p[2]) || y != int.Parse(p[3]) || Math.Abs(v - double.Parse(p[4], CultureInfo.InvariantCulture)) > 1e-9)
                { posBad++; res.Add(new Result { Set = "decor", Name = "pos_" + p[0] + "_" + p[1], OverTol = 1, Note = string.Format("기대 {0},{1},{2} 실제 {3},{4},{5}", p[2], p[3], p[4], x, y, v) }); }
            }
            res.Add(new Result { Set = "decor", Name = "pos(" + posN + ")", Pixels = posN, OverTol = posBad });
            // 장면 목록
            var want = File.ReadAllLines(Path.Combine(Dir, "scene_lists.txt")).Where(l => !string.IsNullOrWhiteSpace(l)).GroupBy(l => l.Split(';')[0]).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var kv in want)
            {
                var parts = kv.Key.Split('_'); string season = parts[1]; int st = int.Parse(parts[2].Substring(1)); int w = int.Parse(parts[0].Split('x')[0]);
                var ir = new IslandBaker(Palettes.IslandEnvFor(season, st, "classic", "classic", 6)).Bake();
                var L = SceneLists.Build(st, 32, DecorLayout.DevFill(), ir.Pieces.Select(p => p.V).ToList(), w);
                var got = new List<string>();
                foreach (var (pass, list) in new[] { ("A", L.A), ("B", L.B), ("S", L.S) })
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        got.Add(string.Join(";", kv.Key, pass, i, e.K, Num(e.V), e.X, e.K == 'd' ? e.Y : 0, e.K == 'd' ? e.ItemId : e.K == 'p' ? e.PieceIndex.ToString() : "m"));
                    }
                bool same = got.Count == kv.Value.Count; int first = -1;
                for (int i = 0; same && i < got.Count; i++) if (got[i] != kv.Value[i]) { same = false; first = i; }
                res.Add(new Result { Set = "decor", Name = "lists_" + kv.Key, Pixels = kv.Value.Count, OverTol = same ? 0 : 1, Note = same ? null : (first >= 0 ? string.Format("항목 {0}: 기대 {1} 실제 {2}", first, kv.Value[first], got[first]) : string.Format("개수 {0} != {1}", got.Count, kv.Value.Count)) });
            }
            return res;
        }

        static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);


        public static int FrameTol = 1;                // 장면 합성 허용 오차(채널 단계). 실측 뒤 정한다

        /// <summary>장면 합성: 낮·밤 프레임 (배경·물가·섬·조각·꾸미기 풀세트·탑·램프·우체통). 같은 순서로 CPU 합성한 결과를 HTML 캔버스 읽기값과 비교.</summary>
        public static List<Result> Frames()
        {
            var res = new List<Result>(); var gp = Pack("frames.bin.gz");
            foreach (var line in File.ReadAllLines(Path.Combine(Dir, "frames.meta")))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(';'); int w = int.Parse(p[0]), h = int.Parse(p[1]), st = int.Parse(p[3]), gl = int.Parse(p[4]); string sn = p[2];
                var sp = SceneSprites.Build(w, h, sn, st, gardenLv: gl, slotLv: 32, put: DecorLayout.DevFill());
                foreach (var d in new[] { 0, 1 })
                {
                    string name = "frame_" + w + "x" + h + "_" + sn + "_s" + st + "_" + (d == 1 ? "n" : "d");
                    res.Add(Cmp.DiffOpaque("frames", name, SceneComposer.Compose(sp, d).ToBitmap(), gp.Items[name], FrameTol));
                }
            }
            return res;
        }


        public static int FxTol = 2;                   // 연출(하늘·물결·번짐) 합성 허용 오차(채널 단계). 실측 뒤 정한다

        /// <summary>연출: 하늘 장식+바다 물결(t·dusk 고정 6프레임), 불빛 번짐 6프레임, fit() 배율 표(반 단계 포함).</summary>
        public static List<Result> Fx()
        {
            var res = new List<Result>(); var gp = Pack("fx.bin.gz");
            var inv = CultureInfo.InvariantCulture;
            foreach (var (t, d) in new[] { (0.0, 0.0), (12.5, 0.0), (250.0, 1.0), (137.3, 1.0), (30.0, 0.5), (102.5, 1.0) })
            {
                var c = new CpuCanvas(216, 468);
                if (d < 0.999) c.Copy(BgBaker.MakeBg(false, 216, 468, 148));
                if (d > 0.001) { if (d >= 0.999) c.Copy(BgBaker.MakeBg(true, 216, 468, 148)); else c.Put(BgBaker.MakeBg(true, 216, 468, 148), 0, 0, d); }
                Lighthouse.Render.Fx.DrawSkyDeco(c, 216, 148, t, d); Lighthouse.Render.Fx.DrawSea(c, 216, 148, t, d);
                string name = "sky_" + t.ToString("R", inv) + "_" + d.ToString("R", inv);
                res.Add(Cmp.DiffOpaque("fx_sky", name, c.ToBitmap(), gp.Items[name], FxTol));
            }
            foreach (var line in File.ReadAllLines(Path.Combine(Dir, "fx.meta")))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(';'); int st = int.Parse(p[1]); double t = double.Parse(p[2], inv), d = double.Parse(p[3], inv);
                var sp = SceneSprites.Build(216, 468, "summer", st);
                var c = new CpuCanvas(216, 468); c.Copy(sp.BgN);
                Lighthouse.Render.Fx.DrawLights(c, sp, t, d);
                res.Add(Cmp.DiffOpaque("fx_glow", p[0], c.ToBitmap(), gp.Items[p[0]], FxTol));
            }
            int bad = 0, n = 0;
            foreach (var line in File.ReadAllLines(Path.Combine(Dir, "fit.txt")))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(';'); n++;
                double dpr = double.Parse(p[2], inv); int devW = (int)Math.Round(int.Parse(p[0]) * dpr * 1.0), devH = (int)Math.Round(int.Parse(p[1]) * dpr);
                // HTML: devW = innerWidth*dpr (실수 그대로). 정수 반올림이 영향을 주는지 보려고 실수 입력 경로도 쓴다
                var v = p[3] == "near" ? FitView.View.Near : p[3] == "wide" ? FitView.View.Wide : FitView.View.Normal;
                var f = new FitView(int.Parse(p[0]) * dpr, int.Parse(p[1]) * dpr, v);
                if (Math.Abs(f.Scale - double.Parse(p[4], inv)) > 1e-5 || f.W != int.Parse(p[5]) || f.H != int.Parse(p[6]) || f.OY != int.Parse(p[7]))
                { bad++; res.Add(new Result { Set = "fx_fit", Name = string.Join("_", p.Take(4)), OverTol = 1, Note = string.Format("기대 s={0} {1}x{2} OY={3} 실제 s={4} {5}x{6} OY={7}", p[4], p[5], p[6], p[7], f.Scale, f.W, f.H, f.OY) }); }
            }
            res.Add(new Result { Set = "fx_fit", Name = "fit(" + n + ")", Pixels = n, OverTol = bad });
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
            if (On("ships")) all.AddRange(Ships());
            if (On("icons")) all.AddRange(Icons());
            if (On("decor")) all.AddRange(Decor());
            if (On("frames")) all.AddRange(Frames());
            if (On("fx")) all.AddRange(Fx());
            return all;
        }
    }
}
