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
    /// 타이틀 장면 골든 대조: 동결 HTML(MD5 e36a9779…)의 #titleCv 를 인트로 없는 상태(?nointro)에서 정해진 시각 t 로 그려 읽은 프레임
    /// (Tools/golden/m4/out/title_&lt;가로&gt;x&lt;세로&gt;d&lt;dpr&gt;.bin.gz, 추출 스크립트 Tools/golden/m2/srv/ext_title.js)과 TitleRenderer 결과를 비교한다.
    /// 화면 크기 4종(320x568·375x812 dpr2, 768x1024·1024x768 dpr1) × 시각 7개. 글자 모양은 골든 브라우저의 글꼴에서 뽑은 마스크표(TitleGlyphs.Generated.cs)를 쓴다.
    /// </summary>
    public sealed class TitleGoldenTests
    {
        public static int Tol = 3;                 // 알파·가산 8비트 반올림 차이(채널 단계)
        public const double MaxOverFraction = 0.0005;   // 허용 초과 픽셀 비율 상한(소수 좌표 안개 띠 가장자리 등 브라우저 안티앨리어싱 차이)

        static string Out => Path.Combine(M1Fixture.Root, "Tools", "golden", "m4", "out");
        public static readonly string[] Views = { "320x568d2", "375x812d2", "768x1024d1", "1024x768d1" };

        [Test]
        public void TitleFramesMatchHtmlGolden()
        {
            var sprites = TitleSprites.Build(); var lines = new List<string>(); var bad = new List<string>(); string dump = Environment.GetEnvironmentVariable("TITLE_DUMP");
            foreach (var view in Views)
            {
                var gp = GoldenPack.Load(Path.Combine(Out, "title_" + view + ".bin.gz"));
                var meta = File.ReadAllLines(Path.Combine(Out, "title_" + view + ".meta")).Select(l => l.Split(';')).ToList();
                var v = meta.First(m => m[0] == "view"); int iw = int.Parse(v[1]), ih = int.Parse(v[2]); double dpr = double.Parse(v[3], CultureInfo.InvariantCulture);
                int lw = int.Parse(v[4]), lh = int.Parse(v[5]); int sx = int.Parse(v[7]);
                var tr = new TitleRenderer(sprites, (int)Math.Round(iw * dpr), (int)Math.Round(ih * dpr));
                Assert.That(new[] { tr.Lw, tr.Lh, tr.Size.Sx }, Is.EqualTo(new[] { lw, lh, sx }), view + ": 캔버스 크기·배율이 원본과 같다");
                foreach (var f in meta.Where(m => m[0] == "frame"))
                {
                    double t = double.Parse(f[2], CultureInfo.InvariantCulture); tr.Draw(t);
                    var act = new Bitmap(0, 0, tr.Lw, tr.Lh); Array.Copy(tr.Pixels, act.D, act.D.Length);
                    var r = Cmp.DiffOpaque("title", view + "/" + f[1], act, gp.Items[f[1]], Tol); lines.Add(r.ToString());
                    if (dump != null)
                    {
                        PngLite.Save(Path.Combine(dump, view + "_" + f[1] + ".png"), act.D, act.W, act.H, 2);
                        var e = gp.Items[f[1]]; PngLite.Save(Path.Combine(dump, view + "_" + f[1] + "_html.png"), e.D, e.W, e.H, 2); var df = new uint[act.D.Length]; var pts = new List<string>();      // 차이 그림: 같으면 어둡게, 다르면 빨강(허용 초과는 노랑)
                        for (int i = 0; i < df.Length; i++)
                        {
                            uint a = act.D[i], b = e.D[i]; int dm = 0; for (int s = 0; s < 24; s += 8) dm = Math.Max(dm, Math.Abs((int)((a >> s) & 255) - (int)((b >> s) & 255)));
                            df[i] = dm == 0 ? Bitmap.Pack(((int)(a & 255)) / 4, ((int)((a >> 8) & 255)) / 4, ((int)((a >> 16) & 255)) / 4, 255) : dm > Tol ? Bitmap.Pack(255, 255, 0, 255) : Bitmap.Pack(255, 0, 0, 255);
                            if (dm > Tol && pts.Count < 40) pts.Add((i % act.W) + "," + (i / act.W));
                        }
                        PngLite.Save(Path.Combine(dump, view + "_" + f[1] + "_diff.png"), df, act.W, act.H, 2); TestContext.Out.WriteLine(view + "/" + f[1] + " over: " + string.Join(" ", pts));
                    }
                    if ((double)r.OverTol / r.Pixels > MaxOverFraction) bad.Add(r.ToString());
                }
            }
            foreach (var l in lines) TestContext.Out.WriteLine(l);
            Assert.That(bad, Is.Empty);
        }

        /// <summary>TITLE_DUMP 폴더가 정해져 있으면, 기기 화면 크기별로 장면을 정수 배율로 늘려 위쪽 가운데 맞춤으로 자른 그림(실제 화면에 보이는 모습, 글자 텍스트 제외)을 저장한다.</summary>
        [Test]
        public void TitleDeviceViewsDump()
        {
            string dump = Environment.GetEnvironmentVariable("TITLE_DUMP"); if (dump == null) Assert.Ignore("TITLE_DUMP 없음");
            var sprites = TitleSprites.Build();
            foreach (var s in new[] { new[] { 640, 1136 }, new[] { 720, 1280 }, new[] { 1080, 2340 }, new[] { 1536, 2048 }, new[] { 2048, 1536 }, new[] { 1280, 720 } })
            {
                var tr = new TitleRenderer(sprites, s[0], s[1]); tr.Draw(2.5); int sx = tr.Size.Sx; var o = new uint[s[0] * s[1]]; int offX = (tr.Lw * sx - s[0]) / 2;
                for (int y = 0; y < s[1]; y++) for (int x = 0; x < s[0]; x++) o[y * s[0] + x] = tr.Pixels[(y / sx) * tr.Lw + (x + offX) / sx];
                PngLite.Save(Path.Combine(dump, "view_" + s[0] + "x" + s[1] + ".png"), o, s[0], s[1], 1);
                TestContext.Out.WriteLine(s[0] + "x" + s[1] + ": 장면 " + tr.Lw + "x" + tr.Lh + " x" + sx + ", 글자 폭 " + tr.LogoTotalWidth + "/" + tr.Lw + ", 부제 윗줄 " + tr.SubtitleTop);
            }
        }

        [Test]
        public void TitleSizeFollowsHtmlRule()
        {   // sz() 5612행의 수식 몇 점 (기기 픽셀 기준)
            Assert.That(new TitleSize(750, 1624).Sx, Is.EqualTo(6)); Assert.That(new TitleSize(750, 1624).Lw, Is.EqualTo(125));
            Assert.That(new TitleSize(640, 1136).Sx, Is.EqualTo(4)); Assert.That(new TitleSize(1024, 768).Sx, Is.EqualTo(3));
            Assert.That(new TitleSize(320, 480).Sx, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void TitleLayoutFitsEveryScreen()
        {   // 320 폭 ~ 태블릿 가로·세로: 글자 그림이 화면 안에 들어가고(잘리지 않고) 섬·배가 화면 안에 있다
            var sprites = TitleSprites.Build();
            foreach (var s in new[] { new[] { 320, 480 }, new[] { 360, 640 }, new[] { 412, 915 }, new[] { 750, 1624 }, new[] { 1080, 2400 }, new[] { 768, 1024 }, new[] { 1536, 2048 }, new[] { 1024, 768 }, new[] { 2048, 1536 }, new[] { 1920, 1080 } })
            {
                var tr = new TitleRenderer(sprites, s[0], s[1]);
                Assert.That(tr.LogoTotalWidth, Is.LessThanOrEqualTo(tr.Lw), s[0] + "x" + s[1] + " 글자 폭");
                Assert.That(tr.LogoTop + tr.LogoH, Is.LessThan(tr.Lh * 0.5), s[0] + "x" + s[1] + " 글자는 위쪽 절반 안");
                Assert.That(tr.Lw * tr.Size.Sx, Is.GreaterThanOrEqualTo(s[0]), "가로를 덮는다"); Assert.That(tr.Lh * tr.Size.Sx, Is.GreaterThanOrEqualTo(s[1]), "세로를 덮는다");
                tr.Draw(1.5);
            }
        }
    }
}
