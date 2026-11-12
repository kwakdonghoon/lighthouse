using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Render;
using Lighthouse.Render.Verify;
using Lighthouse.World;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>
    /// A1 월드 연출 골든 대조: 동결 HTML(MD5 e36a9779…)에서 시각·상태를 고정해 뽑은 프레임(Tools/golden/a1/out/fx_a1.bin.gz, 추출 Tools/golden/m2/srv/ext_f.js)과
    /// WorldPainter(A1 연출) 합성을 비교한다. 단색 바탕(배·선원·표식)과 실제 배경(먼 섬·부이·표류물·비계·생물·계절·갈매기·번호 배지) 양쪽.
    /// 허용 오차는 알파·가산의 8비트 반올림 차이(채널 단계, 부록 B-15).
    /// </summary>
    public sealed class A1GoldenTests
    {
        public static int Tol = 3;
        /// <summary>표류물 오크통은 기울어 그려져 가장자리를 브라우저가 안티앨리어싱한다(그림 사각형에 덮이는 비율). 8×8 표본 근사 + 기울임을 1/80 rad 칸으로 어림(층을 다시 굽는 횟수를 줄이려고)해서 가장자리 칸에서만 최대 ±14(실측) 어긋난다.</summary>
        public static int DriftTol = 16;
        const double FP = 123480.5; const long EP0 = 1800000; const int W = 216, H = 468, OY = 148;
        static GoldenPack gp; static string[] meta;
        static GoldenPack Pack() { if (gp == null) { string d = Path.Combine(M1Fixture.Root, "Tools", "golden", "a1", "out"); gp = GoldenPack.Load(Path.Combine(d, "fx_a1.bin.gz")); meta = File.ReadAllLines(Path.Combine(d, "fx_a1.meta")).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray(); } return gp; }

        static CpuCanvas Bg(double d, bool plain)
        {
            var c = new CpuCanvas(W, H);
            if (plain) { var sea = Col.Hex("#2a78ae"); for (int k = 0; k < c.D.Length; k++) c.D[k] = Bitmap.Pack((int)sea.R, (int)sea.G, (int)sea.B, 255); return c; }
            if (d < 0.999) c.Copy(BgBaker.MakeBg(false, W, H, OY));
            if (d > 0.001) { if (d >= 0.999) c.Copy(BgBaker.MakeBg(true, W, H, OY)); else c.Put(BgBaker.MakeBg(true, W, H, OY), 0, 0, d); }
            return c;
        }

        static Bitmap Composite(CpuCanvas bg, WorldPainter p)
        {
            var buf = new uint[bg.D.Length]; p.Merge(buf);
            var o = new Bitmap(0, 0, bg.W, bg.H);
            for (int i = 0; i < buf.Length; i++)
            {
                uint b = bg.D[i], q = buf[i]; int a = (int)(q >> 24);
                int r = Math.Min(255, (int)Math.Round((b & 255) * (255 - a) / 255.0, MidpointRounding.AwayFromZero) + (int)(q & 255));
                int g = Math.Min(255, (int)Math.Round(((b >> 8) & 255) * (255 - a) / 255.0, MidpointRounding.AwayFromZero) + (int)((q >> 8) & 255));
                int bl = Math.Min(255, (int)Math.Round(((b >> 16) & 255) * (255 - a) / 255.0, MidpointRounding.AwayFromZero) + (int)((q >> 16) & 255));
                o.D[i] = Bitmap.Pack(r, g, bl, 255);
            }
            return o;
        }

        sealed class Case { public string Name; public double D; public bool Plain; public Action<GameLoop, WorldPainter> Draw; }

        static GameLoop NewLoop(double dusk, int stage = 6)
        {
            var clk = new ManualClock(EP0, M1Fixture.TzMinutes);
            var st = GameState.FromJson(new SaveMigrator(M1Fixture.Tables, clk).Defaults()); st.Seen = true; st.Stage = stage;
            var ctx = new GameContext(M1Fixture.Tables, st, new Mulberry32(1), clk) { Stage = stage, SeasonForce = "summer", Sim = true };
            var L = new GameLoop(ctx, null, new Mulberry32(2)); L.SetView(W, H, OY); L.T = FP / 1000; L.NightForced = true; L.Dusk = dusk; ctx.Night = dusk >= 0.5; return L;
        }

        static Ship Sh(string type, int lane, double x, int dir, double ph, Action<Ship> o = null) { var s = new Ship { Type = type, Lane = lane, X = x, Dir = dir, Ph = ph, Need = 1, Sp = 5 }; o?.Invoke(s); return s; }
        static List<Ship> Marks() => new List<Ship>
        {
            Sh("fish", 2, 60.3, 1, 1.1), Sh("trade", 1, 150.6, -1, 2.3, s => s.Done = true), Sh("galleon", 0, 100.2, 1, 0.7, s => s.Sos = true), Sh("rowboat", 1, 40.5, -1, 4.2, s => { s.RestT = 2; s.Guide = 0.5; s.Reg = "x"; }),
            Sh("steamer", 2, 170.4, 1, 3.6, s => s.Done = true), Sh("cruise", 0, 30, -1, 5.1, s => s.Guide = 0.3), Sh("schooner", 1, 120, 1, 2.8, s => s.Guide = 0.95), Sh("explorer", 2, 20.5, 1, 0.2, s => { s.Done = true; s.Yo = 5; }), Sh("ferry", 1, 190, -1, 4.9)
        };

        static List<Case> Cases()
        {
            var cs = new List<Case>();
            void Add(string n, double d, Action<GameLoop, WorldPainter> f, bool plain = false) => cs.Add(new Case { Name = n, D = d, Draw = f, Plain = plain });
            // ---- 먼 섬
            Add("far_d0_t1", 0, (L, p) => p.PaintFarIslands(L, 1)); Add("far_d1_on", 1, (L, p) => p.PaintFarIslands(L, 1.0)); Add("far_d1_off", 1, (L, p) => p.PaintFarIslands(L, 3.0));
            // ---- 물가 불빛 + 부이
            void WB(string n, double d, double t, int stage)
            {
                Pack(); int gv = int.Parse(meta.First(l => l.StartsWith("growth;port;") && l.EndsWith(";" + n)).Split(';')[2]);
                cs.Add(new Case { Name = n, D = d, Draw = (L, p) => { L.Ctx.S.Growth["port"] = gv; if (d > 0.4) p.PaintWaterWarmth(L, Math.Min(1, (d - 0.4) / 0.4)); p.PaintBuoys(L, t); } });
                var c = cs[cs.Count - 1]; var draw = c.Draw; c.Draw = (L, p) => { L.Ctx.Stage = stage; L.Ctx.S.Stage = stage; draw(L, p); };
            }
            WB("buoy_s6_p4_d0_t2.3", 0, 2.3, 6); WB("buoy_s6_p12_d1_t5.1", 1, 5.1, 6); WB("buoy_s6_p23_d1_t7.7", 1, 7.7, 6); WB("buoy_s3_p8_d0.7_t3.3", 0.7, 3.3, 3); WB("buoy_s1_p0_d1_t0.4", 1, 0.4, 1);
            // ---- 표류물
            Action<GameLoop> driftSet = L =>
            {
                L.Drift.Add(new DriftItem { X = 60.4, Y = 200.3, Vx = 3, Ph = 1.2 }); L.Drift.Add(new DriftItem { X = 150.7, Y = 290.2, Vx = -3, Ph = 3.7, Take = 0.4 });
                L.Drift.Add(new DriftItem { X = 100, Y = 276.5, Vx = 3, Ph = 5.5, Take = 0.9 }); L.Drift.Add(new DriftItem { X = 30.5, Y = 192, Vx = 3, Ph = 0.3 });
            };
            Add("drift_d0_t4.2", 0, (L, p) => { driftSet(L); p.PaintDrift(L, 4.2); }); Add("drift_d1_t9.7", 1, (L, p) => { driftSet(L); p.PaintDrift(L, 9.7); });
            // ---- 비계
            Add("scaffold_s6_d0_t3.37", 0, (L, p) => { L.Ctx.Stage = 6; p.PaintScaffold(L, 3.37); }); Add("scaffold_s3_d1_t7.9", 1, (L, p) => { L.Ctx.Stage = 3; p.PaintScaffold(L, 7.9); });
            Add("scaffold_s1_d0_t1.9", 0, (L, p) => { L.Ctx.Stage = 1; p.PaintScaffold(L, 1.9); }); Add("scaffold_s5_d0.55_t12.2", 0.55, (L, p) => { L.Ctx.Stage = 5; p.PaintScaffold(L, 12.2); });
            // ---- 생물
            void Life(string n, double d, double t, double garden) => Add(n, d, (L, p) => { L.Ctx.S.Growth["garden"] = garden; p.PaintLife(L, t); });
            Life("life_day_g0_t6.3", 0, 6.3, 0); Life("life_day_g40000_t6.9", 0, 6.9, 40000); Life("life_day_g40000_t2.0", 0, 2.0, 40000);
            Life("life_night_g0_t13.4", 1, 13.4, 0); Life("life_night_g40000_t27.8", 1, 27.8, 40000); Life("life_night_g40000_t30.55", 1, 30.55, 40000);
            // ---- 계절 연출
            Action<GameLoop, string> motes = (L, s) => { for (int i = 0; i < 8; i++) L.Motes.Add(new Mote { X = 20 + i * 24.3, Y = -OY + 30 + i * 41.7, Ph = i * 0.8 + 0.1, S = s }); };
            Add("sfx_spring_d0", 0, (L, p) => { L.Ctx.SeasonForce = "spring"; motes(L, "spring"); p.PaintSeasonFx(L, 8.77); });
            Add("sfx_autumn_d0", 0, (L, p) => { L.Ctx.SeasonForce = "autumn"; motes(L, "autumn"); p.PaintSeasonFx(L, 8.77); });
            Action<GameLoop> wint = L => { L.Ctx.SeasonForce = "winter"; motes(L, "winter"); L.Meteors.Add(new Meteor { X = 80, Y = -OY + 40, Vx = 60, Vy = 30, Age = 0.3, Life = 1.8 }); L.Meteors.Add(new Meteor { X = 160, Y = -OY + 90, Vx = -60, Vy = 30, Age = 1.4, Life = 1.8 }); };
            Add("sfx_winter_d1", 1, (L, p) => { wint(L); p.PaintSeasonFx(L, 8.77); }); Add("sfx_winter_d0.7", 0.7, (L, p) => { wint(L); p.PaintSeasonFx(L, 3.3); });
            Add("sfx_summer_d1", 1, (L, p) =>
            {
                L.Lanterns.Add(new Lantern { X = 90.4, Y = 160.2, Ph = 1.1 }); L.Lanterns.Add(new Lantern { X = 130, Y = 100, Ph = 3.2 }); L.Lanterns.Add(new Lantern { X = 70.7, Y = 40.9, Ph = 5.0 });
                L.Meteors.Add(new Meteor { X = 40, Y = -OY + 20, Vx = -60, Vy = 30, Age = 1.7, Life = 1.8 }); p.PaintSeasonFx(L, 8.77);
            });
            // ---- 갈매기
            Action<GameLoop> gl = L =>
            {
                L.Gulls.Add(new Gull { X = 100.4, Y = 250.2, St = "stand", Face = 1, Pk = 0.1, Ph = 0.5 }); L.Gulls.Add(new Gull { X = 120.2, Y = 252.6, St = "stand", Face = -1, Pk = 0, Ph = 1.5 });
                L.Gulls.Add(new Gull { X = 60.6, Y = 200.1, St = "in", Face = 1, Ph = 2.1 }); L.Gulls.Add(new Gull { X = 150.5, Y = 180.4, St = "out", Face = 1, Ph = 4.4, Life = 0.5 }); L.Gulls.Add(new Gull { X = 30, Y = 100, St = "in", Face = 1, Ph = 3, Delay = 1.2 });
                L.Crumbs.Add(new Crumb { X = 110.3, Y = 245.6 }); L.Crumbs.Add(new Crumb { X = 118.2, Y = 252.1, Land = true, Life = 1 }); L.Crumbs.Add(new Crumb { X = 104.9, Y = 249, Land = true, Life = 2 });
            };
            Add("gulls_d0_t5.3", 0, (L, p) => { gl(L); p.PaintGulls(L, 5.3); }); Add("gulls_d1_t6.1", 1, (L, p) => { gl(L); p.PaintGulls(L, 6.1); });
            // ---- 배: 표식·물보라·선원
            foreach (var (d, t, n) in new[] { (0.0, 3.37, "marks_d0_t3.37"), (1.0, 3.37, "marks_d1_t3.37"), (0.6, 7.1, "marks_d0.6_t7.1"), (1.0, 1.9, "marks_d1_t1.9") })
            { double tt = t; Add(n, d, (L, p) => { L.Ships.AddRange(Marks()); p.PaintShipMarks(L, tt); }, true); }
            foreach (var (d, t, n) in new[] { (0.0, 3.37, "spray_d0_t3.37"), (1.0, 6.2, "spray_d1_t6.2") })
            { double tt = t; Add(n, d, (L, p) => { foreach (var s in Marks()) p.PaintShipBody(L, s, tt); }, true); }
            foreach (var (d, t, n) in new[] { (0.0, 3.37, "sailor_d0_t3.37"), (1.0, 5.5, "sailor_d1_t5.5") })
            { double tt = t; Add(n, d, (L, p) => { foreach (var s in Marks()) p.PaintSailor(s, tt); }, true); }
            // ---- 번호 배지
            Pack();
            foreach (var z in new[] { "front", "path", "shore", "back" })
            {
                string zz = z; if (!meta.Any(l => l == "badgeframe;" + zz + ";2.6")) continue;
                Add("badges_" + z + "_d0_t2.6", 0, (L, p) =>
                {
                    foreach (var l in meta.Where(l => l.StartsWith("marker;" + zz + ";")))
                    {
                        var f = l.Split(';'); string size = f[5];
                        L.Markers.Add(new DecoMarker { X = double.Parse(f[3], CultureInfo.InvariantCulture), Y = double.Parse(f[4], CultureInfo.InvariantCulture), Rx = size == "S" ? 6 : size == "M" ? 9 : 12, Full = f[6] == "1", Sel = f[7] == "1", Num = int.Parse(new string(f[2].Where(char.IsDigit).ToArray())) });
                    }
                    p.PaintDecoMarkers(L, 2.6);
                });
            }
            return cs;
        }

        [Test]
        public void A1FramesMatchHtmlGolden()
        {
            var gpk = Pack(); var results = new List<Result>(); var lines = new List<string>();
            foreach (var c in Cases())
            {
                var L = NewLoop(c.D); var p = new WorldPainter(W, H, OY);
                var bg = Bg(c.D, c.Plain); c.Draw(L, p);
                var r = Cmp.DiffOpaque("a1", c.Name, Composite(bg, p), gpk.Items[c.Name], c.Name.StartsWith("drift_") ? DriftTol : Tol); results.Add(r); lines.Add(r.ToString());
            }
            // 그림자 타원: 암초·배·관리인 (HTML ellipse() 와 같은 줄 채우기)
            foreach (double d in new[] { 0.0, 1.0 })
            {
                var bg = Bg(d, false); string name = "shadows_d" + (d > 0 ? "1" : "0");
                var col = d > 0 ? new[] { 0, 6, 24, 102 } : new[] { 10, 50, 90, 77 };
                void Shd(double cx, double cy, double rx, double ry, int[] c) { var b = KeeperArt.Ellipse(rx, ry, c[0], c[1], c[2], c[3]); bg.Put(b, cx, cy + OY); }
                Shd(45, 281, 11, 3, col); if (d == 0) Shd(175, 291, 11, 3, col);
                foreach (var (type, lane) in new[] { ("fish", 2), ("galleon", 0), ("rowboat", 1) })
                {
                    double ls = Lanes.S[lane], ly = Lanes.Y[lane]; Shd(101, JsMath.Round(ly + 1.8), Lanes.HalfLen(type) * ls * 0.95, 2 * ls * Math.Max(0.5, Terrain.KY / 0.36), col);
                }
                if (d == 0) Shd(60, 220, 4, 1.4, new[] { 0, 0, 0, 71 });
                var o = new Bitmap(0, 0, W, H); Array.Copy(bg.D, o.D, o.D.Length);
                // HTML 쪽 배경은 위 Bg(d) 와 같은 makeBg 그림 위에 그렸으므로 같은 바탕이다
                var r = Cmp.DiffOpaque("a1", name, o, gpk.Items[name], Tol); results.Add(r); lines.Add(r.ToString());
            }
            foreach (var l in lines) TestContext.Out.WriteLine(l);
            TestContext.Out.WriteLine("허용 오차 ±" + Tol + ": 프레임 " + results.Count + "장, 완전 일치 " + results.Count(r => r.ExactDiff == 0) + "장, 허용 초과 " + results.Count(r => !r.Ok) + "장, 최대 채널 차 " + results.Max(r => r.MaxDelta));
            Assert.That(results.Where(r => !r.Ok).Select(r => r.ToString()).ToList(), Is.Empty);
        }

        [Test]
        public void KeeperBodiesMatchHtmlGolden()
        {
            var gpk = Pack(); var lines = new List<string>(); var bad = new List<string>();
            foreach (var id in new[] { "classic", "dusk", "moonlit", "sailor", "dev" })
                foreach (int dir in new[] { 1, -1 })
                    foreach (int fr in new[] { 0, 1 })
                    {
                        string name = "keeper_" + id + "_" + dir + "_" + fr; var b = KeeperArt.Body(id, dir, fr); var o = new Bitmap(0, 0, b.W, b.H); Array.Copy(b.D, o.D, o.D.Length);
                        var r = Cmp.Diff("a1", name, o, gpk.Items[name], 0); lines.Add(r.ToString()); if (!r.Ok) bad.Add(r.ToString());
                    }
            foreach (var l in lines) TestContext.Out.WriteLine(l);
            Assert.That(bad, Is.Empty);
        }
    }
}
