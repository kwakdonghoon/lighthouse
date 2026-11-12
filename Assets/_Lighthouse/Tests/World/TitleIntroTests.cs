using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Lighthouse.Render;
using Lighthouse.Render.Verify;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>
    /// 타이틀 인트로(첫 실행 약 9초, 원본 INTRO 5596·5707~5758행) 시험. 인트로가 끝난 뒤(t ≥ 12초)의 그림은 인트로 없는 장면(골든 28장으로 지켜지는 경로)과 한 점도 다르지 않아야 하고,
    /// 인트로 중의 장면은 시각에 따라 단계적으로 채워지며, 소리 요청은 원본 EV 표대로 한 번씩 나간다.
    /// </summary>
    public sealed class TitleIntroTests
    {
        static readonly int[][] Sizes = { new[] { 640, 1136 }, new[] { 750, 1624 }, new[] { 1536, 2048 } };

        static int Lit(TitleRenderer tr) { int n = 0; foreach (var p in tr.Pixels) if (p != 0) n++; return n; }

        /// <summary>
        /// 동결 HTML(MD5 e36a9779…)의 #titleCv 를 인트로가 켜진 상태(?dev&amp;title&amp;intro&amp;at=0)에서 시각 12개로 그린 프레임(Tools/golden/m4/out/titleI_&lt;가로&gt;x&lt;세로&gt;d&lt;dpr&gt;.bin.gz, 추출 Tools/golden/m2/srv/ext_title_intro.js)과 비교한다.
        /// 화면 3종(320x568·375x812 dpr2, 768x1024 dpr1) × 시각 12개(바닥 내려앉기·블록·탑 올라오기·램프 번쩍·관리인·배 들어옴·글자 내려앉음·끝). 허용은 인트로 없는 골든과 같다(±3, 초과 점 비율 상한).
        /// </summary>
        [Test]
        public void TitleIntroFramesMatchHtmlGolden()
        {
            string Out = Path.Combine(M1Fixture.Root, "Tools", "golden", "m4", "out"); var sprites = TitleSprites.Build(true); var lines = new List<string>(); var bad = new List<string>(); string dump = Environment.GetEnvironmentVariable("TITLE_DUMP");
            foreach (var view in new[] { "320x568d2", "375x812d2", "768x1024d1" })
            {
                var gp = GoldenPack.Load(Path.Combine(Out, "titleI_" + view + ".bin.gz"));
                var meta = File.ReadAllLines(Path.Combine(Out, "titleI_" + view + ".meta")).Select(l => l.Split(';')).ToList();
                var v = meta.First(m => m[0] == "view"); int iw = int.Parse(v[1]), ih = int.Parse(v[2]); double dpr = double.Parse(v[3], CultureInfo.InvariantCulture);
                var tr = new TitleRenderer(sprites, (int)Math.Round(iw * dpr), (int)Math.Round(ih * dpr)) { Intro = true };
                Assert.That(new[] { tr.Lw, tr.Lh, tr.Size.Sx }, Is.EqualTo(new[] { int.Parse(v[4]), int.Parse(v[5]), int.Parse(v[7]) }), view + ": 캔버스 크기·배율이 원본과 같다");
                foreach (var f in meta.Where(m => m[0] == "frame"))
                {
                    double t = double.Parse(f[2], CultureInfo.InvariantCulture); tr.Draw(t);
                    var act = new Bitmap(0, 0, tr.Lw, tr.Lh); Array.Copy(tr.Pixels, act.D, act.D.Length);
                    var r = Cmp.DiffOpaque("titleI", view + "/" + f[1], act, gp.Items[f[1]], TitleGoldenTests.Tol); lines.Add(r.ToString());
                    if (dump != null)
                    {
                        PngLite.Save(Path.Combine(dump, "I_" + view + "_" + f[1] + ".png"), act.D, act.W, act.H, 2); var e = gp.Items[f[1]]; PngLite.Save(Path.Combine(dump, "I_" + view + "_" + f[1] + "_html.png"), e.D, e.W, e.H, 2);
                    }
                    if ((double)r.OverTol / r.Pixels > TitleGoldenTests.MaxOverFraction) bad.Add(r.ToString());
                }
            }
            foreach (var l in lines) TestContext.Out.WriteLine(l);
            Assert.That(bad, Is.Empty);
        }

        [Test]
        public void IntroEndsExactlyLikeTheNoIntroScene()
        {
            var sp = TitleSprites.Build(true);
            foreach (var s in Sizes)
                foreach (double t in new[] { 11.5, 14.2, 20.3, 31.7 })
                {
                    var a = new TitleRenderer(sp, s[0], s[1]); a.Draw(t);
                    var b = new TitleRenderer(sp, s[0], s[1]) { Intro = true }; b.Draw(t);
                    int diff = 0; for (int i = 0; i < a.Pixels.Length; i++) if (a.Pixels[i] != b.Pixels[i]) diff++;
                    Assert.That(diff, Is.EqualTo(0), s[0] + "x" + s[1] + " t=" + t + ": 인트로 뒤 장면이 인트로 없는 장면과 다르다");
                }
        }

        [Test]
        public void PiecesDoNotChangeTheBakedIslandAndFollowTheHtmlDelays()
        {
            var plain = TitleSprites.Build(false); var withP = TitleSprites.Build(true);
            Assert.That(withP.Island.D, Is.EqualTo(plain.Island.D), "조각을 굽는 것이 섬 그림을 바꾸지 않는다");
            Assert.That(withP.Pieces.Count, Is.EqualTo(1 + 100 + 4), "바닥 1 + 칸 100 + 물체 4(집·나무 셋)");
            Assert.That(withP.Pieces.Count(p => p.Floor), Is.EqualTo(1)); Assert.That(withP.Pieces.First(p => p.Floor).Delay, Is.EqualTo(1.0));
            double max = withP.Pieces.Max(p => p.Delay); Assert.That(max, Is.LessThan(4.7), "섬은 4.7초 전에 다 내려앉는다(그 뒤에는 한 장으로 그린다)");
            // 가운데 칸(ring 0.5)이 가장 먼저, 바깥(ring 4.5)이 늦다: 1.3 + (ring-0.5)*0.55 (+해시 0~0.12, 물체는 +0.4)
            var center = withP.Pieces.First(p => p.Tag == "t:0.5:0.5"); Assert.That(center.Delay, Is.InRange(1.3, 1.3 + 0.12));
            var edge = withP.Pieces.First(p => p.Tag == "t:-4.5:-4.5"); Assert.That(edge.Delay, Is.InRange(1.3 + 4 * 0.55, 1.3 + 4 * 0.55 + 0.12));
            var hut = withP.Pieces.First(p => p.Tag == "o:-2.6:2.4"); Assert.That(hut.Delay, Is.InRange(1.3 + 2.1 * 0.55 + 0.4, 1.3 + 2.1 * 0.55 + 0.4 + 0.12));
        }

        [Test]
        public void IntroBuildsUpOverTime()
        {
            var sp = TitleSprites.Build(true); var tr = new TitleRenderer(sp, 750, 1624) { Intro = true };
            // 처음에는 하늘·바다·별뿐이고(섬 조각·탑·글자 없음), 시간이 갈수록 그림이 채워진다
            tr.Draw(0.2); var f0 = (uint[])tr.Pixels.Clone();
            tr.Draw(2.5); var f1 = (uint[])tr.Pixels.Clone();
            tr.Draw(4.6); var f2 = (uint[])tr.Pixels.Clone();
            tr.Draw(5.0); var f3 = (uint[])tr.Pixels.Clone();                                              // 등대가 올라오는 중
            tr.Draw(6.5); var f4 = (uint[])tr.Pixels.Clone();                                              // 불이 켜지는 순간(번쩍)
            tr.Draw(8.0); var f5 = (uint[])tr.Pixels.Clone();                                              // 관리인·빛줄기·배가 들어온다
            tr.Draw(9.5); var f6 = (uint[])tr.Pixels.Clone();                                              // 글자가 내려앉는 중
            tr.Draw(12.0); var f7 = (uint[])tr.Pixels.Clone();
            int D(uint[] a, uint[] b) { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) n++; return n; }
            Assert.That(D(f0, f1), Is.GreaterThan(500), "블록이 내려앉기 시작한다"); Assert.That(D(f1, f2), Is.GreaterThan(500));
            Assert.That(D(f2, f3), Is.GreaterThan(100), "탑이 올라온다"); Assert.That(D(f3, f4), Is.GreaterThan(100));
            Assert.That(D(f4, f5), Is.GreaterThan(500), "빛줄기·관리인·배"); Assert.That(D(f5, f6), Is.GreaterThan(200), "글자"); Assert.That(D(f6, f7), Is.GreaterThan(0));
            // 탑이 아직 없는 때(5.0 이전 같은 위치)와 비교: 글자는 9초 전에는 위쪽 줄에 없다
            int top = tr.LogoTop, h = tr.LogoH, w = tr.Lw; int cnt = 0, same = 0;
            for (int y = top; y < top + h; y++) for (int x = 0; x < w; x++) { cnt++; if (f5[y * w + x] == f0[y * w + x]) same++; }
            Assert.That(same / (double)cnt, Is.GreaterThan(0.6), "글자 자리는 8초까지 거의 하늘 그대로(별·달무리만 다름)");
            Assert.That(Lit(tr), Is.GreaterThan(0));
        }

        /// <summary>TITLE_DUMP 폴더가 정해져 있으면 인트로 장면을 시각별로 PNG 로 저장한다(눈 확인용, 글자 텍스트 제외). 배율은 정수 Sx 배.</summary>
        [Test]
        public void IntroFramesDump()
        {
            string dump = Environment.GetEnvironmentVariable("TITLE_DUMP"); if (dump == null) Assert.Ignore("TITLE_DUMP 없음");
            var sp = TitleSprites.Build(true); var tr = new TitleRenderer(sp, 720, 1280) { Intro = true }; int sx = tr.Size.Sx;
            foreach (double t in new[] { 0.5, 1.8, 2.6, 3.6, 4.6, 5.2, 5.9, 6.5, 7.2, 8.0, 9.3, 10.0, 12.0 })
            {
                tr.Draw(t); var o = new uint[720 * 1280]; int offX = (tr.Lw * sx - 720) / 2;
                for (int y = 0; y < 1280; y++) for (int x = 0; x < 720; x++) o[y * 720 + x] = tr.Pixels[Math.Min(tr.Lh - 1, y / sx) * tr.Lw + Math.Min(tr.Lw - 1, (x + offX) / sx)];
                PngLite.Save(Path.Combine(dump, "intro_t" + t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"), o, 720, 1280, 1);
            }
        }

        [Test]
        public void IntroSoundRequestsFireOncePerEvent()
        {
            var sp = TitleSprites.Build(true); var tr = new TitleRenderer(sp, 640, 1136) { Intro = true }; var got = new List<(double t, string n)>(); double now = 0;
            tr.Sfx += n => got.Add((now, n));
            for (now = 0; now <= 16.0; now += 0.05) tr.Draw(now);
            Assert.That(got.Select(g => g.n).ToArray(), Is.EqualTo(new[] { "stone", "stone", "stone", "stone", "stone", "build", "lamp" }));
            double[] want = { 1.75, 2.3, 2.85, 3.4, 3.95, 5.9, 6.4 };
            for (int i = 0; i < want.Length; i++) Assert.That(got[i].t, Is.InRange(want[i], want[i] + 0.06), got[i].n);
            got.Clear(); var tr2 = new TitleRenderer(sp, 640, 1136); tr2.Sfx += n => got.Add((0, n)); for (now = 0; now <= 16.0; now += 0.05) tr2.Draw(now);
            Assert.That(got.Count, Is.EqualTo(0), "인트로가 아니면 소리 요청이 없다");
        }
    }
}
