using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lighthouse.Audio;
using Lighthouse.Core;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>
    /// HTML OfflineAudioContext 렌더(Tools/golden/audio/out, 추출: Tools/golden/audio/srv/harness.js)와 C# 합성 결과의 대조.
    /// 허용 오차(관측 최댓값 → 허용값, 이유는 package-unity-m6.md):
    ///   효과음 30종  relErr(파형 상대 오차) 0.043 → 0.06 / 피크·RMS ±0.4dB / 옥타브 대역 ±0.5dB / 포락선 상관 ≥ 0.998
    ///   음악 구절 4  relErr 0.025 → 0.04 / RMS ±0.1dB
    ///   환경음 장면 4(파도·바람·비·갈매기·귀뚜라미·안개고동·망치 포함; 첫 1초 제외)  relErr 0.29 → 0.35 / RMS ±1.0dB / 옥타브 대역 ±2dB / 포락선 상관 ≥ 0.98
    ///   압축기·울림 보정(같은 순음을 여덟 크기·두 경로로)  ±0.3dB
    /// 골든은 크롬의 오실레이터 버릇(SynthEngine.ChromiumQuirks)을 켠 렌더와 견준다. 게임 재생에서는 이 버릇을 켜지 않는다.
    /// </summary>
    [TestFixture]
    public sealed class AudioGoldenTests
    {
        [SetUp] public void Up() { SynthEngine.ChromiumQuirks = true; Biquad.Legacy = false; }
        [TearDown] public void Down() { SynthEngine.ChromiumQuirks = false; }

        static int SeasonOf(string s) { return s == "spring" ? 0 : s == "summer" ? 1 : s == "autumn" ? 2 : 3; }
        static readonly Dictionary<string, (string season, double dusk, double storm, int wx, double amt, int stage, bool build, int seed)> Cases = new Dictionary<string, (string, double, double, int, double, int, bool, int)>
        {
            { "music_spring_day", ("spring", 0.1, 0, 0, 0, 3, false, 5200) }, { "music_summer_night", ("summer", 0.9, 0, 0, 0, 3, false, 5201) },
            { "music_winter_day_storm", ("winter", 0.1, 0.5, 0, 0, 3, false, 5202) }, { "music_autumn_day", ("autumn", 0.1, 0, 0, 0, 3, false, 5203) },
            { "amb_spring_day", ("spring", 0.1, 0, 0, 0, 3, false, 5300) }, { "amb_summer_night", ("summer", 0.9, 0, 0, 0, 2, false, 5301) },
            { "amb_autumn_rain_build", ("autumn", 0.3, 0, 2, 0.8, 4, true, 5302) }, { "amb_winter_fog", ("winter", 0.2, 0, 1, 0.8, 1, false, 5303) },
        };

        /// <summary>음악 구절·환경음 장면을 골든과 같은 조건으로 렌더한다. 환경음은 앞 1초(압축기 시작 버릇)를 빼고 비교한다.</summary>
        static void RenderLong(string name, int frames, out float[] l, out float[] r, out int skip)
        {
            var c = Cases[name];
            var sc = AudioScene.Default; sc.Season = SeasonOf(c.season); sc.Dusk = c.dusk; sc.Wx = c.wx; sc.WxAmt = c.amt; sc.Stage = c.stage; sc.Building = c.build;
            var e = AudioFixture.NewEngine(); l = new float[frames]; r = new float[frames]; skip = 0;
            if (name.StartsWith("music_"))
            {
                e.DirectorOn = false; AudioFixture.WarmUp(e); e.SetScene(sc); e.SyncScene(); e.Rng = new Mulberry32(c.seed); e.MusicPhraseNow(c.storm);
            }
            else { e.SetScene(sc); e.Rng = new Mulberry32(c.seed); skip = AudioFixture.Sr; }
            e.Render(l, r, 0, frames);
        }

        static GoldenAudio.Result CompareLong(string name)
        {
            var g = GoldenAudio.Load(name, out int n); RenderLong(name, n, out var l, out var r, out int skip);
            int m = n - skip; var gl = new[] { new float[m], new float[m] }; var ml = new float[m]; var mr = new float[m];
            Array.Copy(g[0], skip, gl[0], 0, m); Array.Copy(g[1], skip, gl[1], 0, m); Array.Copy(l, skip, ml, 0, m); Array.Copy(r, skip, mr, 0, m);
            return GoldenAudio.Compare(ml, mr, gl, m);
        }

        static void NeedGolden(string name) { if (!File.Exists(Path.Combine(AudioFixture.GoldenDir, name + ".pcm.gz"))) Assert.Fail("골든 없음: " + name + " (Tools/golden/audio 의 harness 로 추출)"); }

        [Test]
        public void ThirtySfx_MatchHtmlOfflineRender()
        {
            var sb = new StringBuilder(); var bad = new List<string>();
            for (int i = 0; i < SynthEngine.SfxNames.Length; i++)
            {
                string nm = SynthEngine.SfxNames[i]; NeedGolden("sfx_" + nm);
                var g = GoldenAudio.Load("sfx_" + nm, out int n); AudioFixture.RenderSfx(nm, i, n, out var l, out var r);
                var res = GoldenAudio.Compare(l, r, g, n); sb.AppendLine(nm.PadRight(9) + res);
                if (!(res.RelErr <= 0.06 && Math.Abs(res.PeakDb) <= 0.4 && Math.Abs(res.RmsDb) <= 0.4 && res.BandMaxDb <= 0.5 && res.EnvCorr >= 0.998)) bad.Add(nm + " " + res);
            }
            TestContext.WriteLine(sb.ToString());
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [TestCase("music_spring_day"), TestCase("music_summer_night"), TestCase("music_winter_day_storm"), TestCase("music_autumn_day")]
        public void MusicPhrase_MatchesHtml(string name)
        {
            NeedGolden(name); var res = CompareLong(name); TestContext.WriteLine(name + " " + res);
            Assert.LessOrEqual(res.RelErr, 0.04, "파형 오차 " + res); Assert.LessOrEqual(Math.Abs(res.RmsDb), 0.1, "RMS " + res); Assert.GreaterOrEqual(res.EnvCorr, 0.999);
        }

        [TestCase("amb_spring_day"), TestCase("amb_summer_night"), TestCase("amb_autumn_rain_build"), TestCase("amb_winter_fog")]
        public void AmbienceScene_MatchesHtml(string name)
        {
            NeedGolden(name); var res = CompareLong(name); TestContext.WriteLine(name + " " + res);
            Assert.LessOrEqual(res.RelErr, 0.35, "파형 오차 " + res); Assert.LessOrEqual(Math.Abs(res.RmsDb), 1.0, "RMS " + res);
            Assert.LessOrEqual(res.BandMaxDb, 2.0, "대역 " + res); Assert.GreaterOrEqual(res.EnvCorr, 0.98, "포락선 " + res);
        }

        [Test]
        public void CompressorAndReverb_Calibration_WithinPoint3dB()
        {
            string path = Path.Combine(AudioFixture.GoldenDir, "calib.json"); if (!File.Exists(path)) Assert.Fail("calib.json 없음");
            double[] win0 = { 0.05, 0.3, 0.8, 1.5, 2.4 }, win1 = { 0.1, 0.4, 0.9, 1.6, 2.5 };
            var rx = new System.Text.RegularExpressions.Regex(@"""bus"": (true|false),\s*""vol"": ([0-9.]+),\s*""rms"": \[([^\]]*)\]");
            int cnt = 0;
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(File.ReadAllText(path)))
            {
                bool bus = m.Groups[1].Value == "true"; double v = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                var gold = m.Groups[3].Value.Split(','); var e = AudioFixture.NewEngine(); e.DirectorOn = false; AudioFixture.WarmUp(e); e.Rng = new Mulberry32(AudioFixture.S1 + 400);
                e.Tone(440, 2, OscType.Sine, v, 0, 0, bus, 0.015); var l = new float[AudioFixture.Sr * 3]; var r = new float[l.Length]; e.Render(l, r, 0, l.Length);
                for (int w = 0; w < 5; w++)
                {
                    double s = 0; int n = 0; for (int i = (int)(win0[w] * AudioFixture.Sr); i < (int)(win1[w] * AudioFixture.Sr); i++) { s += (double)l[i] * l[i] + (double)r[i] * r[i]; n += 2; }
                    double g = double.Parse(gold[w], System.Globalization.CultureInfo.InvariantCulture); if (g <= 0) continue;
                    double db = 20 * Math.Log10(g / Math.Sqrt(s / n)); Assert.LessOrEqual(Math.Abs(db), 0.3, (bus ? "bus " : "dry ") + v + " 창 " + w + ": " + db.ToString("F2") + "dB"); cnt++;
                }
            }
            Assert.GreaterOrEqual(cnt, 70, "보정 점이 모자람");
        }

        [TestCase("tone_220_0", 220.0, 0.0, false), TestCase("tone_988_0", 988.0, 0.0, false), TestCase("tone_988_0_bus", 988.0, 0.0, true), TestCase("tone_3000_0", 3000.0, 0.0, false),
         TestCase("tone_5335_0", 5335.0, 0.0, false), TestCase("tone_660_0.05", 660.0, 0.05, false), TestCase("tone_2000_0.2", 2000.0, 0.2, false)]
        public void SinglePureTone_MatchesHtml(string name, double f, double when, bool bus)
        {
            NeedGolden(name); var g = GoldenAudio.Load(name, out int n); var e = AudioFixture.NewEngine(); e.DirectorOn = false; AudioFixture.WarmUp(e); e.Rng = new Mulberry32(AudioFixture.S1 + 500);
            e.Tone(f, 0.5, OscType.Sine, 0.05, when, 0, bus, 0.004); var l = new float[n]; var r = new float[n]; e.Render(l, r, 0, n);
            var res = GoldenAudio.Compare(l, r, g, n); Assert.LessOrEqual(res.RelErr, 0.04, name + " " + res);
        }

        [Test]
        public void WithoutQuirks_EnergyBandsAndEnvelopeStillMatch()
        {
            // 게임 재생(버릇 끔)은 골든과 파형이 위상만 다를 수 있다 → 에너지·대역·포락선은 그대로여야 한다
            SynthEngine.ChromiumQuirks = false; var bad = new List<string>();
            foreach (var nm in new[] { "warn", "sos", "tick", "horn", "bell", "mail", "pick", "stone", "wood", "fanfare" })
            {
                int i = Array.IndexOf(SynthEngine.SfxNames, nm); var g = GoldenAudio.Load("sfx_" + nm, out int n); AudioFixture.RenderSfx(nm, i, n, out var l, out var r);
                var res = GoldenAudio.Compare(l, r, g, n);
                if (!(Math.Abs(res.RmsDb) <= 0.5 && res.BandMaxDb <= 0.6 && res.EnvCorr >= 0.995)) bad.Add(nm + " " + res);
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
