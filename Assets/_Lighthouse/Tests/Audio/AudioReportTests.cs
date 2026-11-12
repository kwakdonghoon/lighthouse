using System;
using System.Diagnostics;
using System.IO;
using Lighthouse.Audio;
using Lighthouse.Core;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>용량·로딩·CPU 실측 보고와 사람 귀 점검용 WAV 내보내기(문서 package-unity-m6.md 의 수치 근거).</summary>
    [TestFixture]
    public sealed class AudioReportTests
    {
        [SetUp] public void Up() { SynthEngine.ChromiumQuirks = false; }

        [Test]
        public void Footprint_InitTime_Memory_And_CpuPerScene()
        {
            long m0 = GC.GetTotalMemory(true); var sw = Stopwatch.StartNew(); var e = new SynthEngine(44100, new Mulberry32(1)); double initMs = sw.Elapsed.TotalMilliseconds;
            long m1 = GC.GetTotalMemory(true);
            var l = new float[44100 * 30]; var r = new float[l.Length]; long m2 = GC.GetTotalMemory(true);
            var sc = AudioScene.Default; sc.Dusk = 0.1; sc.Wx = 2; sc.WxAmt = 0.8; e.SetScene(sc);
            sw.Restart(); e.Render(l, r, 0, l.Length); double ms = sw.Elapsed.TotalMilliseconds;
            // 효과음 30종을 한꺼번에 렌더(최악: 모든 소리가 겹침) 시간
            var e2 = new SynthEngine(44100, new Mulberry32(2)); e2.DirectorOn = false; var a = new float[44100 * 6]; var b = new float[a.Length];
            foreach (var n in SynthEngine.SfxNames) e2.DoSfx(n); sw.Restart(); e2.Render(a, b, 0, a.Length); double msAll = sw.Elapsed.TotalMilliseconds;
            string txt = "엔진 초기화 " + initMs.ToString("F0") + "ms, 엔진 메모리 " + ((m1 - m0) / 1048576.0).ToString("F1") + "MB (+고정 퀀텀 버퍼 외 렌더 버퍼는 호출자 몫), 30초 비 오는 낮 장면 렌더 " + ms.ToString("F0") + "ms = 실시간의 " + (30000 / ms).ToString("F1") + "배, 효과음 30종 동시 6초 렌더 " + msAll.ToString("F0") + "ms";
            TestContext.WriteLine("FOOTPRINT " + txt);
            Assert.Less(initMs, 3000); Assert.Less((m1 - m0) / 1048576.0, 40);
        }

        [Test]
        public void BakedWavSize_IfWeShippedAssetsInstead()
        {
            // 30종 효과음을 44.1kHz 스테레오 16비트 WAV 로 구웠을 때 크기(= 결정 근거 표의 '굽기' 열)
            long bytes = 0; for (int i = 0; i < SynthEngine.SfxNames.Length; i++)
            {
                var e = AudioFixture.NewEngine(); e.DirectorOn = false; e.Rng = new Mulberry32(AudioFixture.S1 + i); e.DoSfx(SynthEngine.SfxNames[i]); var l = new float[44100 * 7]; var r = new float[l.Length]; e.Render(l, r, 0, l.Length);
                int last = 0; for (int k = 0; k < l.Length; k++) if (Math.Abs(l[k]) > 3e-4f || Math.Abs(r[k]) > 3e-4f) last = k; bytes += (long)(last + 1) * 4;
            }
            TestContext.WriteLine("BAKED sfx30 stereo16 total " + (bytes / 1048576.0).ToString("F2") + "MB (mono 이면 절반, Vorbis 약 1/8)");
            Assert.Greater(bytes, 1000000);
        }

        [Test, Explicit("사람 귀 점검용 WAV 를 LH_WAV_DIR(없으면 임시 폴더)에 쓴다")]
        public void ExportWavs()
        {
            string dir = Environment.GetEnvironmentVariable("LH_WAV_DIR") ?? Path.Combine(Path.GetTempPath(), "lh_wav"); Directory.CreateDirectory(dir);
            for (int i = 0; i < SynthEngine.SfxNames.Length; i++)
            {
                var e = AudioFixture.NewEngine(); e.DirectorOn = false; e.Rng = new Mulberry32(AudioFixture.S1 + i); e.DoSfx(SynthEngine.SfxNames[i]); var l = new float[44100 * 6]; var r = new float[l.Length]; e.Render(l, r, 0, l.Length);
                WavIo.Write(Path.Combine(dir, "sfx_" + SynthEngine.SfxNames[i] + ".wav"), l, r, l.Length, 44100);
            }
            foreach (var (nm, season, dusk, wx) in new[] { ("scene_spring_day", 0, 0.1, 0), ("scene_summer_night", 1, 0.9, 0), ("scene_autumn_rain", 2, 0.3, 2), ("scene_winter_fog", 3, 0.2, 1) })
            {
                var e = new SynthEngine(44100, new Mulberry32(7)); var sc = AudioScene.Default; sc.Season = season; sc.Dusk = dusk; sc.Wx = wx; sc.WxAmt = 0.8; sc.Stage = 3; e.SetScene(sc);
                var l = new float[44100 * 60]; var r = new float[l.Length]; e.Render(l, r, 0, l.Length); WavIo.Write(Path.Combine(dir, nm + ".wav"), l, r, l.Length, 44100);
            }
            TestContext.WriteLine("WAV 저장: " + dir);
        }
    }
}
