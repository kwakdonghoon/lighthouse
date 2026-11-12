using System;
using System.IO;
using Lighthouse.Audio;
using Lighthouse.Core;

namespace Lighthouse.Tests
{
    /// <summary>오디오 시험 공용: 프로젝트 루트·골든 경로·오프라인 렌더 도우미.</summary>
    public static class AudioFixture
    {
        static string root;
        public static string Root
        {
            get
            {
                if (root != null) return root;
                foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                    for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                        if (Directory.Exists(Path.Combine(d.FullName, "Tools", "golden")) && Directory.Exists(Path.Combine(d.FullName, "Assets", "_Lighthouse"))) return root = d.FullName;
                throw new DirectoryNotFoundException("프로젝트 루트를 찾지 못함");
            }
        }
        public static string GoldenDir { get { return Path.Combine(Root, "Tools", "golden", "audio", "out"); } }

        public const int Sr = 44100;
        /// <summary>골든 추출과 같은 씨앗 규칙: 초기화 씨앗 S0, 소리마다 S1+번호.</summary>
        public const int S0 = 20260101, S1 = 5000;
        /// <summary>골든은 소리를 1.0014초(345퀀텀) 뒤에 예약한다(크롬 압축기의 시작 이득 버릇을 피하려고). 같은 만큼 무음을 먼저 렌더한다.</summary>
        public const int Warm = 345 * 128;
        public static void WarmUp(SynthEngine e) { var a = new float[Warm]; var b = new float[Warm]; e.Render(a, b, 0, Warm); }

        public static SynthEngine NewEngine(int seed = S0, int sr = Sr) { return new SynthEngine(sr, new Mulberry32(seed)); }

        /// <summary>효과음 하나를 골든과 같은 조건으로 렌더한다(새 엔진 → 씨앗 바꾸기 → DoSfx → frames 만큼).</summary>
        public static void RenderSfx(string name, int index, int frames, out float[] l, out float[] r)
        {
            var e = NewEngine(); e.DirectorOn = false; WarmUp(e); e.Rng = new Mulberry32(S1 + index); e.DoSfx(name);
            l = new float[frames]; r = new float[frames]; e.Render(l, r, 0, frames);
        }
    }
}
