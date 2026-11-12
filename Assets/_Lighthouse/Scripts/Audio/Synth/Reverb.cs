using System;
using Lighthouse.Core;

namespace Lighthouse.Audio
{
    /// <summary>
    /// 소음 바탕·울림 응답 생성(원본 mkNoise / mkIR). 난수는 호출 순서까지 원본과 같게 IRng 로만 받는다(결정적).
    /// 배열은 WebAudio AudioBuffer 처럼 float32 로 둔다.
    /// </summary>
    public static class NoiseMaker
    {
        /// <summary>mkNoise: kind=brown|pink, 6초 스테레오, 이음매 0.6초 겹침, 실효값 0.3.</summary>
        public static float[][] MakeNoise(IRng rng, bool brown, double sec, int sr)
        {
            int n = (int)Math.Floor(sr * sec), x = (int)Math.Floor(sr * 0.6);
            var buf = new float[2][];
            for (int c = 0; c < 2; c++)
            {
                var raw = new float[n + x]; double l = 0, b0 = 0, b1 = 0, b2 = 0;
                for (int i = 0; i < n + x; i++)
                {
                    double w = rng.NextDouble() * 2 - 1;
                    if (brown) { l = (l + 0.02 * w) / 1.02; raw[i] = (float)l; }
                    else { b0 = 0.99765 * b0 + w * 0.099046; b1 = 0.963 * b1 + w * 0.2965164; b2 = 0.57 * b2 + w * 1.0526913; raw[i] = (float)(b0 + b1 + b2 + w * 0.1848); }
                }
                double s = 0; for (int i = 0; i < n; i++) s += (double)raw[i] * raw[i];
                double k = 0.3 / Math.Sqrt(s / n); var d = new float[n];
                for (int i = 0; i < n; i++) d[i] = (float)((i < x ? raw[i] * ((double)i / x) + raw[n + i] * (1 - (double)i / x) : raw[i]) * k);
                buf[c] = d;
            }
            return buf;
        }

        /// <summary>mkIR: 지수로 줄어드는 소음을 점점 어둡게. 앞 18ms 는 비어 있다.</summary>
        public static float[][] MakeIr(IRng rng, double sec, int sr)
        {
            int n = (int)Math.Floor(sr * sec), pre = (int)Math.Floor(sr * 0.018);
            var buf = new float[2][];
            for (int c = 0; c < 2; c++)
            {
                var d = new float[n]; double y = 0;
                for (int i = pre; i < n; i++)
                {
                    double t = (double)(i - pre) / sr, k = 0.55 * Math.Exp(-t * 1.6) + 0.07;
                    y += ((rng.NextDouble() * 2 - 1) - y) * k;
                    d[i] = (float)(y * Math.Exp(-t * 2.3));
                }
                buf[c] = d;
            }
            return buf;
        }

        /// <summary>짧은 터짐 소리용 흰 소음 (2초 모노).</summary>
        public static float[] MakeWhite(IRng rng, int frames)
        {
            var d = new float[frames]; for (int i = 0; i < frames; i++) d[i] = (float)(rng.NextDouble() * 2 - 1); return d;
        }
    }

    /// <summary>
    /// ConvolverNode(normalize=true, 스테레오 응답·스테레오 입력은 좌→좌, 우→우). 지연이 없는 분할 FFT 컨볼루션.
    ///  - 앞쪽 2048표본(= 16개 조각)은 128표본마다 곧바로 계산한다(1단).
    ///  - 나머지는 2048표본 조각 단위로, 직전 블록까지의 입력만으로 계산해 두었다가 더해 준다(2단; 2048 지연 이후의 꼬리라서 정확하다).
    ///  - 입력이 한참 조용하면(응답 길이+4096표본) 계산을 건너뛰고 비운다 → 음악이 없는 동안 CPU 0.
    /// 정규화 배율은 크롬 ConvolverNode 의 calculateNormalizationScale 과 같다(0.00125·44100/sr / RMS).
    /// </summary>
    public sealed class Convolver
    {
        const int Q = SynthEngine.Quantum, F1 = 2 * Q, B2 = 2048, F2 = 2 * B2, P1 = B2 / Q;
        readonly int n, p2, hb1 = Q + 1, hb2 = B2 + 1;
        readonly Fft fft1 = new Fft(F1), fft2 = new Fft(F2);
        readonly float[][] h1re = new float[2][], h1im = new float[2][];          // [ch][p*hb1 + k]
        readonly float[][] h2re = new float[2][], h2im = new float[2][];          // [ch][(p-1)*hb2 + k], p=1..p2-1
        readonly float[][] x1re = new float[2][], x1im = new float[2][];          // 1단 입력 스펙트럼 고리 (P1)
        readonly float[][] x2re = new float[2][], x2im = new float[2][];          // 2단 입력 스펙트럼 고리 (p2-1)
        readonly float[][] hist = new float[2][];                                  // 최근 4096 입력(고리)
        readonly float[][] wTail = new float[2][];                                 // 2단이 지금 블록에 더해 줄 출력 2048
        readonly double[] re1 = new double[F1], im1 = new double[F1], re2 = new double[F2], im2 = new double[F2], accRe = new double[F2], accIm = new double[F2];
        int ring1, ring2, hpos, cnt, idleSamples; bool bypass = true;
        readonly float[][] q1 = new float[2][];                                    // 1단용 직전 입력 128

        public Convolver(float[][] ir, int sampleRate)
        {
            n = ir[0].Length; p2 = (n + B2 - 1) / B2;
            double sum = 0; for (int c = 0; c < 2; c++) for (int i = 0; i < n; i++) sum += (double)ir[c][i] * ir[c][i];
            double power = Math.Sqrt(sum / (2.0 * n)); if (double.IsNaN(power) || double.IsInfinity(power) || power < 0.000125) power = 0.000125;
            double scale = 1.0 / power * 0.00125 * (44100.0 / sampleRate);
            for (int c = 0; c < 2; c++)
            {
                h1re[c] = new float[P1 * hb1]; h1im[c] = new float[P1 * hb1];
                for (int p = 0; p < P1; p++) Spec(ir[c], p * Q, Q, scale, fft1, re1, im1, h1re[c], h1im[c], p * hb1, hb1);
                h2re[c] = new float[(p2 - 1) * hb2]; h2im[c] = new float[(p2 - 1) * hb2];
                for (int p = 1; p < p2; p++) Spec(ir[c], p * B2, B2, scale, fft2, re2, im2, h2re[c], h2im[c], (p - 1) * hb2, hb2);
                x1re[c] = new float[P1 * hb1]; x1im[c] = new float[P1 * hb1];
                x2re[c] = new float[(p2 - 1) * hb2]; x2im[c] = new float[(p2 - 1) * hb2];
                hist[c] = new float[F2]; wTail[c] = new float[B2]; q1[c] = new float[Q];
            }
        }

        static void Spec(float[] src, int off, int len, double scale, Fft f, double[] re, double[] im, float[] outRe, float[] outIm, int o, int bins)
        {
            Array.Clear(re, 0, re.Length); Array.Clear(im, 0, im.Length);
            for (int i = 0; i < len && off + i < src.Length; i++) re[i] = src[off + i] * scale;
            f.Forward(re, im);
            for (int k = 0; k < bins; k++) { outRe[o + k] = (float)re[k]; outIm[o + k] = (float)im[k]; }
        }

        void Reset()
        {
            for (int c = 0; c < 2; c++)
            {
                Array.Clear(x1re[c], 0, x1re[c].Length); Array.Clear(x1im[c], 0, x1im[c].Length);
                Array.Clear(x2re[c], 0, x2re[c].Length); Array.Clear(x2im[c], 0, x2im[c].Length);
                Array.Clear(hist[c], 0, F2); Array.Clear(wTail[c], 0, B2); Array.Clear(q1[c], 0, Q);
            }
            ring1 = ring2 = hpos = cnt = 0;
        }

        /// <summary>Q 표본 한 덩어리. in/out 은 [좌, 우] (out 은 덮어쓴다).</summary>
        public void Process(float[] inL, float[] inR, float[] outL, float[] outR)
        {
            double e = 0; for (int i = 0; i < Q; i++) e += (double)inL[i] * inL[i] + (double)inR[i] * inR[i];
            bool silent = e < 1e-14;
            if (bypass) { if (silent) { Array.Clear(outL, 0, Q); Array.Clear(outR, 0, Q); return; } Reset(); bypass = false; idleSamples = 0; }
            if (silent) { idleSamples += Q; if (idleSamples > n + 2 * F2) { bypass = true; Array.Clear(outL, 0, Q); Array.Clear(outR, 0, Q); return; } }
            else idleSamples = 0;

            // 2단: 2048 경계마다 새 블록의 꼬리를 계산한다
            if ((cnt & (B2 - 1)) == 0)
            {
                bool push = cnt > 0 && p2 > 1;
                if (push) ring2 = (ring2 + (p2 - 1) - 1) % (p2 - 1);                // 새것이 ring2 에 오도록 한 칸 뒤로
                for (int c = 0; c < 2; c++)
                {
                    Array.Clear(accRe, 0, F2); Array.Clear(accIm, 0, F2);
                    if (push)
                    {
                        for (int i = 0; i < F2; i++) { re2[i] = hist[c][(hpos + i) & (F2 - 1)]; im2[i] = 0; }
                        fft2.Forward(re2, im2);
                        int o = ring2 * hb2; for (int k = 0; k < hb2; k++) { x2re[c][o + k] = (float)re2[k]; x2im[c][o + k] = (float)im2[k]; }
                        var xr = x2re[c]; var xi = x2im[c]; var hr = h2re[c]; var hi = h2im[c];
                        for (int p = 1; p < p2; p++)
                        {
                            int xo = ((ring2 + p - 1) % (p2 - 1)) * hb2, ho = (p - 1) * hb2;       // X_{m-p}: p=1 이 가장 새것
                            for (int k = 0; k < hb2; k++)
                            {
                                double a = xr[xo + k], b = xi[xo + k], cc = hr[ho + k], d = hi[ho + k];
                                accRe[k] += a * cc - b * d; accIm[k] += a * d + b * cc;
                            }
                        }
                        for (int k = 1; k < B2; k++) { accRe[F2 - k] = accRe[k]; accIm[F2 - k] = -accIm[k]; }
                        fft2.Inverse(accRe, accIm);
                    }
                    for (int i = 0; i < B2; i++) wTail[c][i] = (float)accRe[B2 + i];
                }
            }

            for (int c = 0; c < 2; c++)
            {
                var src = c == 0 ? inL : inR; var dst = c == 0 ? outL : outR;
                // 1단: [직전 128, 이번 128] 의 스펙트럼을 고리에 넣고 16조각 합
                for (int i = 0; i < Q; i++) { re1[i] = q1[c][i]; im1[i] = 0; re1[Q + i] = src[i]; im1[Q + i] = 0; }
                fft1.Forward(re1, im1);
                int r1 = (ring1 + P1 - 1) % P1; int xo0 = r1 * hb1;
                for (int k = 0; k < hb1; k++) { x1re[c][xo0 + k] = (float)re1[k]; x1im[c][xo0 + k] = (float)im1[k]; }
                Array.Clear(accRe, 0, F1); Array.Clear(accIm, 0, F1);
                for (int p = 0; p < P1; p++)
                {
                    int xo = ((r1 + p) % P1) * hb1, ho = p * hb1; var xr = x1re[c]; var xi = x1im[c]; var hr = h1re[c]; var hi = h1im[c];
                    for (int k = 0; k < hb1; k++)
                    {
                        double a = xr[xo + k], b = xi[xo + k], cc = hr[ho + k], d = hi[ho + k];
                        accRe[k] += a * cc - b * d; accIm[k] += a * d + b * cc;
                    }
                }
                for (int k = 1; k < Q; k++) { accRe[F1 - k] = accRe[k]; accIm[F1 - k] = -accIm[k]; }
                fft1.Inverse(accRe, accIm);
                int tail0 = cnt & (B2 - 1);
                for (int i = 0; i < Q; i++) dst[i] = (float)accRe[Q + i] + wTail[c][tail0 + i];
                Array.Copy(src, q1[c], Q);
            }
            // 입력 기록(2단용 4096 고리): hist 의 쓰기 위치 = 지금까지 받은 표본 수
            for (int c = 0; c < 2; c++)
            {
                var src = c == 0 ? inL : inR; var h = hist[c];
                for (int i = 0; i < Q; i++) h[(cnt + i) & (F2 - 1)] = src[i];
            }
            ring1 = (ring1 + P1 - 1) % P1;
            cnt += Q; hpos = cnt & (F2 - 1);
        }
    }
}
