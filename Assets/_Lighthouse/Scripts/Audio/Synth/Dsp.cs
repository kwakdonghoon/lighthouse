using System;

namespace Lighthouse.Audio
{
    /// <summary>
    /// WebAudio AudioParam 의 자동화(setValueAtTime / linear·exponentialRampToValueAtTime / setTargetAtTime / cancelScheduledValues)를
    /// 한 표본씩 따라가는 구현. 시각은 엔진 시계(초)이고, Next() 는 표본 하나의 값을 주고 시계를 1표본 진행한다.
    /// 지수 램프는 곱셈 한 번, 선형 램프는 덧셈 한 번, setTarget 은 1극 필터로 한 표본을 계산한다(표본마다 pow/exp 를 부르지 않는다).
    /// </summary>
    public sealed class WebParam
    {
        const int Cap = 12;
        readonly double sr;
        readonly int[] type = new int[Cap];                    // 0 set, 1 linear, 2 exp, 3 target
        readonly double[] et = new double[Cap], ev = new double[Cap], etc = new double[Cap];
        int n, idx, mode;                                      // mode: 0 보합, 1 선형, 2 지수, 3 목표로 수렴
        double cur, step, target, sampleT;

        public WebParam(double sampleRate, double init = 1.0) { sr = sampleRate; cur = init; }

        /// <summary>처음 상태로(풀에서 다시 쓸 때). startSample = 이 값이 읽히기 시작하는 엔진 표본 번호.</summary>
        public void Reset(double init, double startSample) { n = 0; idx = 0; mode = 0; cur = init; sampleT = startSample; }

        public double Current => cur;
        public bool Busy => mode != 0 || idx < n;

        public void SetValueAtTime(double v, double t) { Add(0, t, v, 0); }
        public void LinearRampToValueAtTime(double v, double t) { Add(1, t, v, 0); }
        public void ExponentialRampToValueAtTime(double v, double t) { Add(2, t, v, 0); }
        public void SetTargetAtTime(double v, double t, double tc) { Add(3, t, v, tc); }

        /// <summary>지금 값을 곧바로 바꾼다(AudioParam.value = x).</summary>
        public void SetImmediate(double v) { n = 0; idx = 0; mode = 0; cur = v; }

        /// <summary>시각 t 이후의 예약을 지운다(cancelScheduledValues).</summary>
        public void Cancel(double t)
        {
            double ts = t * sr; int k = 0, newIdx = 0;
            for (int i = 0; i < n; i++)
            {
                if (et[i] < ts)
                {
                    if (k != i) { type[k] = type[i]; et[k] = et[i]; ev[k] = ev[i]; etc[k] = etc[i]; }
                    if (i < idx) newIdx++;
                    k++;
                }
            }
            n = k; idx = Math.Min(newIdx, n);
            if ((mode == 1 || mode == 2) && !(idx < n && (type[idx] == 1 || type[idx] == 2))) mode = 0;
        }

        void Add(int ty, double t, double v, double tc)
        {
            if (n == Cap) return;
            double ts = t * sr, rs = Math.Round(ts); if (Math.Abs(ts - rs) < 1e-6) ts = rs;          // 부동소수 오차로 정수 표본 경계를 한 표본 넘기지 않게
            int i = n++;
            while (i > 0 && et[i - 1] > ts) { type[i] = type[i - 1]; et[i] = et[i - 1]; ev[i] = ev[i - 1]; etc[i] = etc[i - 1]; i--; }
            type[i] = ty; et[i] = ts; ev[i] = v; etc[i] = tc;
        }

        public double Next()
        {
            while (idx < n && et[idx] <= sampleT) Activate(idx++);
            double v = cur;
            switch (mode)
            {
                case 1: cur += step; break;
                case 2: cur *= step; break;
                case 3: cur += (target - cur) * step; break;
            }
            sampleT += 1;
            return v;
        }

        void Activate(int i)
        {
            switch (type[i])
            {
                case 0: case 1: case 2: cur = ev[i]; mode = 0; SetupRamp(et[i]); break;
                case 3: mode = 3; target = ev[i]; step = 1.0 - Math.Exp(-1.0 / Math.Max(1e-9, etc[i] * sr)); break;
            }
        }

        void SetupRamp(double tPrev)
        {
            if (idx >= n || (type[idx] != 1 && type[idx] != 2)) return;
            double len = et[idx] - tPrev, v0 = cur, v1 = ev[idx], off = sampleT - tPrev;
            if (len <= 0) { cur = v1; mode = 0; return; }
            if (type[idx] == 1) { step = (v1 - v0) / len; cur = v0 + step * off; mode = 1; }
            else if (v0 > 0 && v1 > 0) { double r = Math.Pow(v1 / v0, 1.0 / len); step = r; cur = v0 * Math.Pow(r, off); mode = 2; }
        }
    }

    public enum BqType { Lowpass, Highpass, Bandpass }

    /// <summary>
    /// BiquadFilterNode. 로우패스·하이패스의 Q 는 dB(WebAudio 규칙), 밴드패스의 Q 는 보통 Q.
    /// 계수식은 크롬(Blink)의 것을 따른다: Legacy=true 이면 d = sqrt((4 - sqrt(16 - 16/g²))/2) 꼴(구 WebKit), false 이면 스펙의 쿡북 식.
    /// </summary>
    public sealed class Biquad
    {
        public static bool Legacy = false;
        double b0 = 1, b1, b2, a1, a2, z1, z2;

        public void Reset() { z1 = z2 = 0; }

        public void Set(BqType t, double freq, double q, double sr)
        {
            double nyq = sr * 0.5, c = freq / nyq;
            if (c < 0) c = 0; if (c > 1) c = 1;
            if (t != BqType.Bandpass && (c == 1 || c == 0))
            {
                // 컷오프 1: 통과(lowpass) / 차단(highpass), 0: 차단(lowpass) / 통과(highpass)
                bool pass = (t == BqType.Lowpass) == (c == 1);
                b0 = pass ? 1 : 0; b1 = b2 = a1 = a2 = 0; return;
            }
            double w0 = Math.PI * c, cs = Math.Cos(w0), sn = Math.Sin(w0);
            if (t == BqType.Bandpass)
            {
                if (c <= 0 || c >= 1) { b0 = b1 = b2 = a1 = a2 = 0; return; }
                if (q <= 0) { b0 = 1; b1 = b2 = a1 = a2 = 0; return; }
                double al = sn / (2 * q), ai = 1 / (1 + al);
                b0 = al * ai; b1 = 0; b2 = -al * ai; a1 = -2 * cs * ai; a2 = (1 - al) * ai; return;
            }
            if (Legacy)
            {
                double res = Math.Max(0.0, q), g = Math.Pow(10.0, 0.05 * res);
                double d = Math.Sqrt((4 - Math.Sqrt(16 - 16 / (g * g))) / 2);
                double s2 = 0.5 * d * sn, beta = 0.5 * (1 - s2) / (1 + s2), gamma = (0.5 + beta) * cs;
                if (t == BqType.Lowpass)
                {
                    double alpha = 0.25 * (0.5 + beta - gamma);
                    b0 = 2 * alpha; b1 = 4 * alpha; b2 = 2 * alpha;
                }
                else
                {
                    double alpha = 0.25 * (0.5 + beta + gamma);
                    b0 = 2 * alpha; b1 = -4 * alpha; b2 = 2 * alpha;
                }
                a1 = 2 * -gamma; a2 = 2 * beta; return;
            }
            {
                double qlin = Math.Pow(10.0, q / 20.0), al = sn / (2 * qlin), ai = 1 / (1 + al);
                if (t == BqType.Lowpass) { b0 = (1 - cs) / 2 * ai; b1 = (1 - cs) * ai; b2 = b0; }
                else { b0 = (1 + cs) / 2 * ai; b1 = -(1 + cs) * ai; b2 = b0; }
                a1 = -2 * cs * ai; a2 = (1 - al) * ai;
            }
        }

        public double Process(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    public enum OscType { Sine, Triangle, Sawtooth }

    /// <summary>
    /// 오실레이터 파형표(WebAudio PeriodicWave 대응): 4096점, 대역 제한(나이퀴스트 아래 배음만), 선형 보간.
    /// 사인·톱니는 스펙의 푸리에 계수(톱니 b_n=(-1)^(n+1)·2/(πn), 삼각 b_n=8·sin(πn/2)/(πn)²)를 쓰고,
    /// 톱니의 크기는 크롬처럼 가장 풍부한 표(2047 배음)의 최댓값이 1 이 되도록 한 번 정한 배율로 맞춘다.
    /// </summary>
    public static class OscTables
    {
        public const int Size = 4096;
        static readonly int[] Ladder = { 1, 2, 3, 4, 5, 6, 8, 10, 13, 16, 20, 26, 32, 40, 52, 64, 80, 104, 128, 160, 208, 256, 320, 416, 512 };
        static float[] sine; static float[][] saw, tri; static readonly object gate = new object();
        static bool ready;

        public static void Ensure()
        {
            if (ready) return;
            lock (gate)
            {
                if (ready) return;
                var st = new double[Size];
                for (int i = 0; i < Size; i++) st[i] = Math.Sin(2 * Math.PI * i / Size);
                sine = new float[Size + 1]; for (int i = 0; i < Size; i++) sine[i] = (float)st[i]; sine[Size] = sine[0];
                double sawScale = 1.0 / PeakOf(true, 2047, st);
                saw = new float[Ladder.Length][]; tri = new float[Ladder.Length][];
                for (int l = 0; l < Ladder.Length; l++) { saw[l] = Build(true, Ladder[l], st, sawScale); tri[l] = Build(false, Ladder[l], st, 1.0); }
                ready = true;
            }
        }

        static double Coef(bool sawtooth, int k)
        {
            if (sawtooth) return ((k & 1) == 1 ? 1.0 : -1.0) * 2.0 / (Math.PI * k);
            if ((k & 1) == 0) return 0;
            double s = (((k - 1) / 2) & 1) == 0 ? 1.0 : -1.0;
            return 8.0 * s / (Math.PI * k * Math.PI * k);
        }

        static double PeakOf(bool sawtooth, int partials, double[] st)
        {
            double peak = 0;
            for (int i = 0; i < Size / 2; i++)                     // 톱니·삼각의 최댓값은 앞 절반에 있다
            {
                double s = 0;
                for (int k = 1; k <= partials; k++) { double c = Coef(sawtooth, k); if (c != 0) s += c * st[(int)(((long)k * i) & (Size - 1))]; }
                if (Math.Abs(s) > peak) peak = Math.Abs(s);
            }
            return peak;
        }

        static float[] Build(bool sawtooth, int partials, double[] st, double scale)
        {
            var t = new float[Size + 1];
            for (int i = 0; i < Size; i++)
            {
                double s = 0;
                for (int k = 1; k <= partials; k++) { double c = Coef(sawtooth, k); if (c != 0) s += c * st[(int)(((long)k * i) & (Size - 1))]; }
                t[i] = (float)(s * scale);
            }
            t[Size] = t[0]; return t;
        }

        /// <summary>maxFreq 로 울릴 때 나이퀴스트를 넘지 않는 가장 풍부한 표.</summary>
        public static float[] Get(OscType ty, double maxFreq, double sr)
        {
            Ensure();
            if (ty == OscType.Sine) return sine;
            int want = (int)Math.Max(1, Math.Floor(sr * 0.5 / Math.Max(1.0, maxFreq))), l = 0;
            for (int i = 0; i < Ladder.Length; i++) if (Ladder[i] <= want) l = i;
            return ty == OscType.Sawtooth ? saw[l] : tri[l];
        }

        public static float Read(float[] t, double phase)
        {
            double pos = phase * Size; int i = (int)pos; double fr = pos - i;
            return (float)(t[i] + (t[i + 1] - t[i]) * fr);
        }
    }

    /// <summary>복소 FFT(2의 거듭제곱, 제자리). Inverse 는 1/N 로 나눈다.</summary>
    public sealed class Fft
    {
        public readonly int N; readonly double[] cs, sn; readonly int[] rev;
        public Fft(int n)
        {
            N = n; cs = new double[n / 2]; sn = new double[n / 2]; rev = new int[n];
            for (int i = 0; i < n / 2; i++) { cs[i] = Math.Cos(2 * Math.PI * i / n); sn[i] = Math.Sin(2 * Math.PI * i / n); }
            int bits = 0; while ((1 << bits) < n) bits++;
            for (int i = 0; i < n; i++) { int r = 0, x = i; for (int b = 0; b < bits; b++) { r = (r << 1) | (x & 1); x >>= 1; } rev[i] = r; }
        }

        public void Forward(double[] re, double[] im) { Run(re, im, -1); }
        public void Inverse(double[] re, double[] im) { Run(re, im, 1); double s = 1.0 / N; for (int i = 0; i < N; i++) { re[i] *= s; im[i] *= s; } }

        void Run(double[] re, double[] im, int dir)
        {
            for (int i = 0; i < N; i++) { int j = rev[i]; if (j > i) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; } }
            for (int len = 2; len <= N; len <<= 1)
            {
                int half = len >> 1, stride = N / len;
                for (int s = 0; s < N; s += len)
                    for (int k = 0; k < half; k++)
                    {
                        double wr = cs[k * stride], wi = dir * sn[k * stride];
                        int a = s + k, b = a + half;
                        double xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                    }
            }
        }
    }
}
