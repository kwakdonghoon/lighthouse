using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Lighthouse.Audio;

namespace Lighthouse.Tests
{
    /// <summary>HTML OfflineAudioContext 렌더(골든)를 읽고 C# 렌더와 견주는 도구.</summary>
    public static class GoldenAudio
    {
        public sealed class Item { public string Name, Kind; public int Frames; public double Peak, Rms; }

        public static float[][] Load(string name, out int frames)
        {
            string path = Path.Combine(AudioFixture.GoldenDir, name + ".pcm.gz");
            byte[] raw; using (var fs = File.OpenRead(path)) using (var gz = new GZipStream(fs, CompressionMode.Decompress)) using (var ms = new MemoryStream()) { gz.CopyTo(ms); raw = ms.ToArray(); }
            frames = raw.Length / 4; var l = new float[frames]; var r = new float[frames];
            for (int i = 0; i < frames; i++) { l[i] = BitConverter.ToInt16(raw, i * 4) / 32767f; r[i] = BitConverter.ToInt16(raw, i * 4 + 2) / 32767f; }
            return new[] { l, r };
        }

        /// <summary>매니페스트(간이 파서: name/frames/peak/rms 만 뽑는다).</summary>
        public static List<Item> Manifest()
        {
            var list = new List<Item>();
            foreach (var f in Directory.GetFiles(AudioFixture.GoldenDir, "manifest*.json"))
            {
                string t = File.ReadAllText(f, new UTF8Encoding(false)); int p = 0;
                while ((p = t.IndexOf("\"name\":", p, StringComparison.Ordinal)) >= 0)
                {
                    var it = new Item(); it.Name = Str(t, p, "name"); it.Frames = (int)Num(t, p, "frames"); it.Peak = Num(t, p, "peak"); it.Rms = Num(t, p, "rms");
                    int kp = t.IndexOf("\"kind\":", p, StringComparison.Ordinal); int nx = t.IndexOf("\"name\":", p + 7, StringComparison.Ordinal);
                    if (kp >= 0 && (nx < 0 || kp < nx)) it.Kind = Str(t, kp - 1, "kind");
                    list.Add(it); p += 7;
                }
            }
            return list;
        }
        static string Str(string t, int from, string key) { int k = t.IndexOf("\"" + key + "\":", from, StringComparison.Ordinal) + key.Length + 3; int a = t.IndexOf('"', k) + 1, b = t.IndexOf('"', a); return t.Substring(a, b - a); }
        static double Num(string t, int from, string key) { int k = t.IndexOf("\"" + key + "\":", from, StringComparison.Ordinal) + key.Length + 3; int e = k; while (e < t.Length && "-+.0123456789eE ".IndexOf(t[e]) >= 0) e++; return double.Parse(t.Substring(k, e - k).Trim(), System.Globalization.CultureInfo.InvariantCulture); }

        public sealed class Result
        {
            public double RelErr, PeakDb, RmsDb, BandMaxDb, EnvCorr, LagRelErr, GainDb, ScaledErr; public int BestLag;
            public override string ToString() { return string.Format("relErr {0:F4} (lag {1}: {2:F4})  peak {3:+0.00;-0.00}dB  rms {4:+0.00;-0.00}dB  band {5:F2}dB  env {6:F4}  lsqGain {7:+0.00;-0.00}dB scaledErr {8:F4}", RelErr, BestLag, LagRelErr, PeakDb, RmsDb, BandMaxDb, EnvCorr, GainDb, ScaledErr); }
        }

        static double Energy(float[] a, int n) { double s = 0; for (int i = 0; i < n; i++) s += (double)a[i] * a[i]; return s; }

        public static Result Compare(float[] l, float[] r, float[][] g, int frames)
        {
            var res = new Result();
            double num = 0, den = 0, pk = 0, gpk = 0;
            for (int i = 0; i < frames; i++)
            {
                double dl = l[i] - g[0][i], dr = r[i] - g[1][i]; num += dl * dl + dr * dr; den += (double)g[0][i] * g[0][i] + (double)g[1][i] * g[1][i];
                pk = Math.Max(pk, Math.Max(Math.Abs(l[i]), Math.Abs(r[i]))); gpk = Math.Max(gpk, Math.Max(Math.Abs(g[0][i]), Math.Abs(g[1][i])));
            }
            res.RelErr = Math.Sqrt(num / Math.Max(den, 1e-30));
            { double gm = 0, mm = 0; for (int i = 0; i < frames; i++) { gm += (double)g[0][i] * l[i] + (double)g[1][i] * r[i]; mm += (double)l[i] * l[i] + (double)r[i] * r[i]; }
              double a = gm / Math.Max(mm, 1e-30); res.GainDb = 20 * Math.Log10(Math.Max(Math.Abs(a), 1e-9)); double se = 0; for (int i = 0; i < frames; i++) { double dl = a * l[i] - g[0][i], dr = a * r[i] - g[1][i]; se += dl * dl + dr * dr; } res.ScaledErr = Math.Sqrt(se / Math.Max(den, 1e-30)); }
            res.PeakDb = 20 * Math.Log10(Math.Max(pk, 1e-9) / Math.Max(gpk, 1e-9));
            res.RmsDb = 10 * Math.Log10(Math.Max(Energy(l, frames) + Energy(r, frames), 1e-30) / Math.Max(den, 1e-30));
            // 가장 잘 맞는 어긋남(±200표본) — 지연 차이를 따로 보려는 진단
            double best = double.MaxValue; int bl = 0;
            for (int lag = -200; lag <= 200; lag += 1)
            {
                double s = 0; int c = 0;
                for (int i = Math.Max(0, lag); i < Math.Min(frames, frames + lag); i++) { double dl = l[i] - g[0][i - lag]; s += dl * dl; c++; }
                if (c > 0 && s < best) { best = s; bl = lag; }
            }
            res.BestLag = bl; res.LagRelErr = Math.Sqrt(best / Math.Max(Energy(g[0], frames), 1e-30));
            res.BandMaxDb = BandDiff(l, g[0], frames);
            res.EnvCorr = EnvCorr(l, g[0], frames);
            return res;
        }

        /// <summary>옥타브 대역(63Hz~16kHz)별 에너지 차이의 최댓값(dB). 골든 최대 대역보다 40dB 이상 작은 대역은 무시한다.</summary>
        public static double BandDiff(float[] a, float[] b, int frames)
        {
            int n = 1; while (n < frames) n <<= 1; var f = new Fft(n);
            double[] A = Spec(a, frames, f, n), B = Spec(b, frames, f, n);
            double[] ea = new double[8], eb = new double[8]; double sr = AudioFixture.Sr;
            for (int k = 1; k < n / 2; k++)
            {
                double hz = k * sr / n; int band = (int)Math.Floor(Math.Log(hz / 44.0, 2)); if (band < 0 || band > 7) continue;   // 44~88,...,5.6k~11k : 8 옥타브
                ea[band] += A[k]; eb[band] += B[k];
            }
            double mx = 0; foreach (var v in eb) mx = Math.Max(mx, v);
            double worst = 0;
            for (int i = 0; i < 8; i++) if (eb[i] > mx * 1e-4) worst = Math.Max(worst, Math.Abs(10 * Math.Log10(Math.Max(ea[i], 1e-30) / eb[i])));
            return worst;
        }
        static double[] Spec(float[] a, int frames, Fft f, int n)
        {
            var re = new double[n]; var im = new double[n]; for (int i = 0; i < frames; i++) re[i] = a[i];
            f.Forward(re, im); var p = new double[n / 2]; for (int k = 0; k < n / 2; k++) p[k] = re[k] * re[k] + im[k] * im[k]; return p;
        }

        /// <summary>10ms 구간 RMS 포락선의 상관계수(소리의 시간 모양이 같은가).</summary>
        public static double EnvCorr(float[] a, float[] b, int frames)
        {
            int w = 441, m = frames / w; if (m < 2) return 1;
            var x = new double[m]; var y = new double[m];
            for (int k = 0; k < m; k++) { double sa = 0, sb = 0; for (int i = k * w; i < (k + 1) * w; i++) { sa += (double)a[i] * a[i]; sb += (double)b[i] * b[i]; } x[k] = Math.Sqrt(sa / w); y[k] = Math.Sqrt(sb / w); }
            double mx = 0, my = 0; for (int k = 0; k < m; k++) { mx += x[k]; my += y[k]; } mx /= m; my /= m;
            double sxy = 0, sxx = 0, syy = 0; for (int k = 0; k < m; k++) { sxy += (x[k] - mx) * (y[k] - my); sxx += (x[k] - mx) * (x[k] - mx); syy += (y[k] - my) * (y[k] - my); }
            return sxy / Math.Sqrt(Math.Max(sxx * syy, 1e-30));
        }
    }
}
