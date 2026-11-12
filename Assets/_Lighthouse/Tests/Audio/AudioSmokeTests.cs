using System;
using System.Diagnostics;
using System.IO;
using Lighthouse.Audio;
using Lighthouse.Core;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>M6 소리 엔진 기본 시험(순수 C#): 30종 모두 소리가 나고 유한하며 크기가 정상 범위인지, 결정적인지, 설정이 먹는지.</summary>
    [TestFixture]
    public sealed class AudioSmokeTests
    {
        [SetUp] public void Up() { SynthEngine.ChromiumQuirks = false; }

        static float Peak(float[] a, int n) { float p = 0; for (int i = 0; i < n; i++) p = Math.Max(p, Math.Abs(a[i])); return p; }

        [Test]
        public void AllThirtySfx_AreFinite_Audible_AndNotClipping()
        {
            Assert.AreEqual(30, SynthEngine.SfxNames.Length);
            for (int i = 0; i < SynthEngine.SfxNames.Length; i++)
            {
                AudioFixture.RenderSfx(SynthEngine.SfxNames[i], i, AudioFixture.Sr * 4, out var l, out var r);
                float pk = Math.Max(Peak(l, l.Length), Peak(r, r.Length));
                for (int k = 0; k < l.Length; k++) Assert.IsTrue(!float.IsNaN(l[k]) && !float.IsInfinity(l[k]) && !float.IsNaN(r[k]), SynthEngine.SfxNames[i] + " 표본 " + k);
                Assert.Greater(pk, 0.004f, SynthEngine.SfxNames[i] + " 너무 작다 " + pk);
                Assert.Less(pk, 0.6f, SynthEngine.SfxNames[i] + " 너무 크다 " + pk);
            }
        }

        [Test]
        public void SameSeed_GivesBitIdenticalRender()
        {
            AudioFixture.RenderSfx("mail", 6, AudioFixture.Sr, out var a, out var _);
            AudioFixture.RenderSfx("mail", 6, AudioFixture.Sr, out var b, out var _2);
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void SoundOff_MakesSilence_AndFreezesTime()
        {
            var e = AudioFixture.NewEngine(); var s = AudioScene.Default; s.SoundOn = false; e.SetScene(s);
            e.Sfx("fanfare"); var l = new float[8192]; var r = new float[8192]; e.Render(l, r, 0, l.Length);
            Assert.AreEqual(0f, Peak(l, l.Length)); Assert.AreEqual(0.0, e.Now);
            s.SoundOn = true; e.SetScene(s); e.Sfx("ding"); e.Render(l, r, 0, l.Length);
            Assert.Greater(Peak(l, l.Length), 0.001f, "소리를 켜면 요청한 소리가 난다(꺼진 동안 요청은 버려지고, 켠 뒤 요청만 난다)");
        }

        [Test]
        public void AdOpen_Pauses_ThenResumes()
        {
            var e = AudioFixture.NewEngine(); var s = AudioScene.Default; e.SetScene(s);
            e.Sfx("horn"); var l = new float[4096]; var r = new float[4096]; e.Render(l, r, 0, l.Length);
            double t1 = e.Now; s.AdOpen = true; e.SetScene(s); e.Render(l, r, 0, l.Length);
            Assert.AreEqual(0f, Peak(l, l.Length)); Assert.AreEqual(t1, e.Now, 1e-9, "광고 중엔 시간이 멈춘다");
            s.AdOpen = false; e.SetScene(s); e.Render(l, r, 0, l.Length);
            Assert.Greater(Peak(l, l.Length), 0.001f, "광고가 끝나면 이어서 난다");
        }

        [Test]
        public void Ambience_RunsWithoutSfx_AndRainAddsEnergy()
        {
            double Rms(AudioScene sc)
            {
                var e = AudioFixture.NewEngine(); e.SetScene(sc); var l = new float[AudioFixture.Sr * 12]; var r = new float[l.Length]; e.Render(l, r, 0, l.Length);
                double s = 0; for (int i = AudioFixture.Sr * 4; i < l.Length; i++) s += (double)l[i] * l[i]; return Math.Sqrt(s / (l.Length - AudioFixture.Sr * 4));
            }
            var clear = AudioScene.Default; var rain = clear; rain.Wx = 2; rain.WxAmt = 1;
            double c = Rms(clear), q = Rms(rain);
            Assert.Greater(c, 1e-4); Assert.Greater(q, c * 1.3, "비가 오면 환경음이 커진다");
        }

        [Test]
        public void Speed_Report()
        {
            var sw = Stopwatch.StartNew(); var e = AudioFixture.NewEngine(); double init = sw.Elapsed.TotalMilliseconds;
            var s = AudioScene.Default; s.Dusk = 0.1; s.Wx = 2; s.WxAmt = 0.8; e.SetScene(s);
            var l = new float[AudioFixture.Sr * 30]; var r = new float[l.Length]; sw.Restart(); e.Render(l, r, 0, l.Length);
            double ms = sw.Elapsed.TotalMilliseconds; TestContext.WriteLine("엔진 초기화 " + init.ToString("F0") + "ms, 30초 장면 렌더 " + ms.ToString("F0") + "ms (실시간 대비 " + (30000 / ms).ToString("F1") + "배)");
            Assert.Greater(30000 / ms, 3, "실시간의 3배 이상으로 돌아야 한다(모바일 여유)");
        }

        // ---------- 감독 규칙: 낮밤·계절·날씨별 소리 ----------
        static SynthEngine RunScene(AudioScene sc, double seconds, int seed = 99)
        {
            var e = new SynthEngine(AudioFixture.Sr, new Mulberry32(seed)); e.SetScene(sc); var l = new float[(int)(AudioFixture.Sr * seconds)]; var r = new float[l.Length]; e.Render(l, r, 0, l.Length); return e;
        }

        [Test]
        public void Director_Day_HasGullsAndMusic_NoCrickets()
        {
            var sc = AudioScene.Default; sc.Dusk = 0.1; var e = RunScene(sc, 60);
            Assert.GreaterOrEqual(e.CntPhrase, 3, "낮에도 음악이 이어진다"); Assert.GreaterOrEqual(e.CntGull, 2, "낮엔 갈매기"); Assert.AreEqual(0, e.CntCricket, "낮엔 귀뚜라미 없음");
        }

        [Test]
        public void Director_Night_HasCrickets_NoGulls_LowerRoot()
        {
            var sc = AudioScene.Default; sc.Dusk = 0.95; sc.Season = 1; var e = RunScene(sc, 60);
            Assert.GreaterOrEqual(e.CntCricket, 10, "밤엔 귀뚜라미"); Assert.AreEqual(0, e.CntGull, "밤엔 갈매기 없음");
            Assert.AreEqual(1, e.LastKey, "밤 음계(단음계 오음)"); Assert.AreEqual(293.66 * 0.5, e.LastRoot, 1e-9, "밤엔 으뜸음이 한 옥타브 낮다");
        }

        [Test]
        public void Director_SeasonRootsAndScales_FollowOriginalTable()
        {
            double[] roots = { 261.63, 293.66, 246.94, 329.63 }; int[] keysDay = { 0, 0, 3, 2 };            // 봄·여름·가을(쓸쓸한 음)·겨울(투명한 넓은 음)
            for (int sn = 0; sn < 4; sn++)
            {
                var sc = AudioScene.Default; sc.Dusk = 0.1; sc.Season = sn; var e = RunScene(sc, 8);
                Assert.GreaterOrEqual(e.CntPhrase, 1); Assert.AreEqual(keysDay[sn], e.LastKey, "계절 " + sn); Assert.AreEqual(roots[sn], e.LastRoot, 1e-9);
            }
            var w = AudioScene.Default; w.Dusk = 0.95; w.Season = 3; var ew = RunScene(w, 8); Assert.AreEqual(2, ew.LastKey, "겨울 밤도 겨울 음계"); Assert.AreEqual(329.63 * 0.5, ew.LastRoot, 1e-9);
        }

        [Test]
        public void Director_Fog_HasFoghorn_Rain_HasDrops_Building_HasHammer()
        {
            var fog = AudioScene.Default; fog.Dusk = 0.1; fog.Wx = 1; fog.WxAmt = 0.9; var ef = RunScene(fog, 90); Assert.GreaterOrEqual(ef.CntFog, 2, "안개 속 먼 뱃고동 22~34초마다");
            var rain = AudioScene.Default; rain.Wx = 2; rain.WxAmt = 0.9; var er = RunScene(rain, 20); Assert.GreaterOrEqual(er.CntRain, 20, "빗방울");
            var snow = rain; snow.Season = 3; var es = RunScene(snow, 20); Assert.AreEqual(0, es.CntRain, "겨울엔 눈 → 빗방울 소리 없음");
            var b = AudioScene.Default; b.Building = true; var eb = RunScene(b, 20); Assert.GreaterOrEqual(eb.CntHammer, 15, "공사 중 망치 0.8~1.1초마다");
        }

        [Test]
        public void Music_IsSparser_WithRain()
        {
            var clear = AudioScene.Default; clear.Dusk = 0.1; clear.Stage = 3; var storm = clear; storm.Wx = 2; storm.WxAmt = 1;
            Assert.Greater(RunScene(clear, 180).CntPhrase, RunScene(storm, 180).CntPhrase, "비 오면 구절 사이가 길다");
        }

        // ---------- 재생 규칙 ----------
        [Test]
        public void SfxGate_Rules()
        {
            var g = new SfxGate();
            Assert.IsFalse(g.Allow("pick", 1.0, false, false), "소리 꺼짐"); Assert.IsFalse(g.Allow("pick", 1.0, true, true), "광고 중");
            Assert.IsTrue(g.Allow("pick", 1.0, true, false)); Assert.IsFalse(g.Allow("click", 1.05, true, false), "직전 80ms 안엔 click 생략"); Assert.IsTrue(g.Allow("click", 1.09, true, false));
            Assert.IsTrue(g.Allow("click", 1.20, true, false), "click 은 다른 click 을 막지 않는다");
            var t = new SfxGate(); Assert.IsFalse(t.PollEnableTick(false, 0)); Assert.IsFalse(t.PollEnableTick(true, 1.0)); Assert.IsFalse(t.PollEnableTick(true, 1.03)); Assert.IsTrue(t.PollEnableTick(true, 1.06), "켠 뒤 50ms 에 tick 한 번");
            Assert.IsFalse(t.PollEnableTick(true, 1.2), "한 번만"); Assert.IsFalse(t.PollEnableTick(false, 1.3)); Assert.IsFalse(t.PollEnableTick(false, 1.4), "끌 때는 소리 없음");
        }

        [Test]
        public void ThreadSafety_ConcurrentRequestsWhileRendering()
        {
            var e = AudioFixture.NewEngine(); var stop = false; int rendered = 0;
            var th = new System.Threading.Thread(() => { var l = new float[512]; var r = new float[512]; while (!stop) { e.Render(l, r, 0, 512); rendered += 512; } });
            th.Start(); var sc = AudioScene.Default;
            for (int i = 0; i < 300; i++) { e.Sfx(SynthEngine.SfxNames[i % 30]); sc.Dusk = (i % 10) / 10.0; e.SetScene(sc); System.Threading.Thread.Sleep(1); }
            stop = true; th.Join(); Assert.Greater(rendered, 44100, "렌더가 돌았다"); Assert.Less(e.ActiveVoices, 220);
        }

        [Test]
        public void PolyphonyAndVoicePool_NoRunaway()
        {
            var e = AudioFixture.NewEngine(); e.DirectorOn = false; var l = new float[4096]; var r = new float[4096];
            for (int k = 0; k < 40; k++) { e.DoSfx("fanfare"); e.DoSfx("build"); e.DoSfx("mail"); e.Render(l, r, 0, 4096); }
            Assert.LessOrEqual(e.ActiveVoices, 220); for (int k = 0; k < 400; k++) e.Render(l, r, 0, 4096); Assert.AreEqual(0, e.ActiveVoices, "모든 소리는 끝나면 풀로 돌아간다");
        }

        [Test]
        public void SampleRate48k_WorksAndKeepsLevel()
        {
            var a = new SynthEngine(44100, new Mulberry32(1)); var b = new SynthEngine(48000, new Mulberry32(1)); a.DirectorOn = b.DirectorOn = false; a.DoSfx("pick"); b.DoSfx("pick");
            var la = new float[44100 * 2]; var ra = new float[la.Length]; var lb = new float[48000 * 2]; var rb = new float[lb.Length]; a.Render(la, ra, 0, la.Length); b.Render(lb, rb, 0, lb.Length);
            double ea = 0, eb = 0; for (int i = 0; i < la.Length; i++) ea += (double)la[i] * la[i]; for (int i = 0; i < lb.Length; i++) eb += (double)lb[i] * lb[i];
            Assert.AreEqual(1.0, (eb / 48000) / (ea / 44100), 0.1, "샘플레이트가 달라도 소리 크기는 같다");
        }
    }
}
