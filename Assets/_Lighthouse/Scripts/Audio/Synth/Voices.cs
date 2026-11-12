using System;
using System.Collections.Generic;

namespace Lighthouse.Audio
{
    /// <summary>소리 하나(WebAudio 의 노드 묶음 하나)에 해당하는 목소리. 엔진이 풀에서 꺼내 Init 하고, 끝나면 되돌린다.</summary>
    abstract class Voice
    {
        public long Start, Stop;
        public abstract void Render(SynthEngine e, long blockStart, int count);

        protected static long SampleAt(double t, int sr) { return (long)Math.Ceiling(t * sr - 1e-9); }
        /// <summary>주파수 자동화가 있는 오실레이터의 시작 표본: 크롬 버릇 모드에서는 부동소수 오차까지 그대로 올림한다.</summary>
        /// <summary>크롬 버릇: 시작 퀀텀에서 이벤트(주파수 setValueAtTime) 이전 프레임 수만큼 440Hz 증분이 쓰인다(시작 위치 q 에서 min(q, 128-q) 프레임). q 는 올림 전의 위치로 센다.</summary>
        protected static int QuirkFrames(double t0, int sr, long start)
        {
            if (!SynthEngine.ChromiumQuirks) return 0;
            long r = (long)Math.Round(t0 * sr); if ((start >> 7) != (r >> 7)) return 0;
            int q = (int)(r & (SynthEngine.Quantum - 1)), avail = SynthEngine.Quantum - (int)(start & (SynthEngine.Quantum - 1));
            return Math.Min(q, avail);
        }
        protected static long OscStart(double t, int sr) { return (long)Math.Ceiling(SynthEngine.ChromiumQuirks ? t * sr : t * sr - 1e-9); }
    }

    sealed class Pool<T> where T : class, new()
    {
        readonly Stack<T> free = new Stack<T>();
        public Pool(int prewarm) { for (int i = 0; i < prewarm; i++) free.Push(new T()); }
        public T Get() { return free.Count > 0 ? free.Pop() : new T(); }
        public void Put(T t) { free.Push(t); }
    }

    /// <summary>원본 tone(): 오실레이터 + 지수 포락선. 높이는 약간(±5센트) 흔들린다(난수는 호출한 쪽이 detune 으로 준다).</summary>
    sealed class ToneVoice : Voice
    {
        WebParam freq, gain; double detMul, phase, invSr; float[] table; bool bus; int quirkLeft;
        public void Init(SynthEngine e, double f, double dur, OscType ty, double vol, double when, double slideTo, bool toBus, double atk, double detuneCents)
        {
            int sr = e.Sr; invSr = 1.0 / sr; double t0 = e.Now + when, a = Math.Min(atk, dur * 0.4);
            Start = OscStart(t0, sr); Stop = SampleAt(t0 + dur + 0.05, sr);
            if (freq == null) { freq = new WebParam(sr); gain = new WebParam(sr); }
            freq.Reset(f, Start); freq.SetValueAtTime(f, t0); if (slideTo > 0) freq.ExponentialRampToValueAtTime(slideTo, t0 + dur);
            gain.Reset(1, Start); gain.SetValueAtTime(0.0001, t0); gain.ExponentialRampToValueAtTime(vol, t0 + a); gain.ExponentialRampToValueAtTime(0.0001, t0 + dur);
            detMul = Math.Pow(2.0, detuneCents / 1200.0); phase = 0; bus = toBus;
            quirkLeft = QuirkFrames(t0, sr, Start);
            table = OscTables.Get(ty, Math.Max(f, slideTo) * detMul, sr);
        }
        public override void Render(SynthEngine e, long b0, int count)
        {
            var oL = bus ? e.BusL : e.DryL; var oR = bus ? e.BusR : e.DryR;
            for (int i = 0; i < count; i++)
            {
                long s = b0 + i; if (s < Start) continue; if (s >= Stop) break;
                float y = (float)(OscTables.Read(table, phase) * gain.Next());
                double fr = freq.Next(); if (quirkLeft > 0) { quirkLeft--; fr = 440; }
                phase += fr * detMul * invSr; phase -= Math.Floor(phase);          // 읽은 다음 진행(첫 표본의 위상은 0)
                oL[i] += y; oR[i] += y;
            }
        }
    }

    /// <summary>원본 nz(): 흰 소음(고정 2초 버퍼를 되풀이) → 필터(높이가 미끄러질 수 있음) → 지수 포락선.</summary>
    sealed class NoiseVoice : Voice
    {
        WebParam fp, gain; Biquad bq = new Biquad(); BqType type; double q, pos; bool bus, slide; float[] buf; int sr;
        public void Init(SynthEngine e, double dur, double vol, BqType ty, double freq, double qv, double when, double toFreq, bool toBus, double offsetSec)
        {
            sr = e.Sr; double t0 = e.Now + when;
            Start = SampleAt(t0, sr); Stop = SampleAt(t0 + dur + 0.05, sr);
            if (fp == null) { fp = new WebParam(sr); gain = new WebParam(sr); }
            fp.Reset(freq, Start); fp.SetValueAtTime(freq, t0); slide = toFreq > 0; if (slide) fp.ExponentialRampToValueAtTime(toFreq, t0 + dur);
            gain.Reset(1, Start); gain.SetValueAtTime(0.0001, t0); gain.ExponentialRampToValueAtTime(vol, t0 + 0.008); gain.ExponentialRampToValueAtTime(0.0001, t0 + dur);
            type = ty; q = qv; bus = toBus; buf = e.White; pos = Math.Round(offsetSec * sr); pos -= Math.Floor(pos / buf.Length) * buf.Length;
            bq.Reset(); bq.Set(type, freq, q, sr);
        }
        public override void Render(SynthEngine e, long b0, int count)
        {
            var oL = bus ? e.BusL : e.DryL; var oR = bus ? e.BusR : e.DryR; int len = buf.Length;
            for (int i = 0; i < count; i++)
            {
                long s = b0 + i; if (s < Start) continue; if (s >= Stop) break;
                double f = fp.Next(); if (slide && ((s - Start) & 15) == 0) bq.Set(type, f, q, sr);
                int i0 = (int)pos; double fr = pos - i0; int i1 = i0 + 1; if (i1 >= len) i1 = 0;
                double x = buf[i0] + (buf[i1] - buf[i0]) * fr;
                pos += 1; if (pos >= len) pos -= len;
                float y = (float)(bq.Process(x) * gain.Next());
                oL[i] += y; oR[i] += y;
            }
        }
    }

    /// <summary>원본 piano(): 순음 5개(배음마다 줄어드는 속도 다름) → 점점 어두워지는 로우패스 → 스테레오 위치 → 울림 버스. (톡 소리는 NoiseVoice 가 따로)</summary>
    sealed class PianoVoice : Voice
    {
        static readonly double[,] Parts = { { 1, 1, 1, -3 }, { 1, 0.55, 0.9, 4 }, { 2, 0.22, 0.45, 0 }, { 3.01, 0.07, 0.22, 0 }, { 5.03, 0.025, 0.1, 0 } };
        WebParam lpFreq; readonly WebParam[] g = new WebParam[5]; readonly double[] ph = new double[5], inc = new double[5]; readonly long[] stopAt = new long[5];
        readonly Biquad lp = new Biquad(); double gl, gr, invSr; int sr; bool slideLp;
        public PianoVoice() { }
        public void Init(SynthEngine e, double f, double dur, double vol, double when, double pan)
        {
            sr = e.Sr; invSr = 1.0 / sr; double t0 = e.Now + Math.Max(0, when) + 0.01, v = vol * 0.62;
            Start = SampleAt(t0, sr);
            if (lpFreq == null) { lpFreq = new WebParam(sr); for (int i = 0; i < 5; i++) g[i] = new WebParam(sr); }
            lpFreq.Reset(Math.Min(6500, f * 6 + 500), Start); lpFreq.SetValueAtTime(Math.Min(6500, f * 6 + 500), t0); lpFreq.ExponentialRampToValueAtTime(Math.Max(500, f * 1.8), t0 + dur * 0.7);
            lp.Reset(); lp.Set(BqType.Lowpass, Math.Min(6500, f * 6 + 500), 0.4, sr);
            double x = (pan + 1) / 2; gl = Math.Cos(x * Math.PI / 2); gr = Math.Sin(x * Math.PI / 2);
            long last = Start;
            for (int k = 0; k < 5; k++)
            {
                double r = Parts[k, 0], a = Parts[k, 1], dm = Parts[k, 2], ct = Parts[k, 3];
                double end = t0 + dur * dm, mid = t0 + Math.Min(0.45, dur * dm * 0.35);
                g[k].Reset(1, Start); g[k].SetValueAtTime(0.0001, t0); g[k].ExponentialRampToValueAtTime(v * a, t0 + 0.006);
                g[k].ExponentialRampToValueAtTime(v * a * 0.28, mid); g[k].ExponentialRampToValueAtTime(0.0001, end);
                inc[k] = f * r * Math.Pow(2.0, ct / 1200.0) * invSr; ph[k] = 0; stopAt[k] = SampleAt(end + 0.05, sr);
                if (stopAt[k] > last) last = stopAt[k];
            }
            Stop = last + (long)(0.03 * sr);
        }
        public override void Render(SynthEngine e, long b0, int count)
        {
            var sine = OscTables.Get(OscType.Sine, 0, sr);
            for (int i = 0; i < count; i++)
            {
                long s = b0 + i; if (s < Start) continue; if (s >= Stop) break;
                double sum = 0;
                for (int k = 0; k < 5; k++)
                {
                    double gv = g[k].Next();
                    if (s < stopAt[k]) { sum += OscTables.Read(sine, ph[k]) * gv; ph[k] += inc[k]; ph[k] -= Math.Floor(ph[k]); }
                }
                double fcut = lpFreq.Next(); if (((s - Start) & 15) == 0) lp.Set(BqType.Lowpass, fcut, 0.4, sr);
                double y = lp.Process(sum);
                e.BusL[i] += (float)(y * gl); e.BusR[i] += (float)(y * gr);
            }
        }
    }

    /// <summary>원본 pad(): 어긋난 톱니파 둘 + 5도·옥타브 순음 → 어두운 로우패스 → 천천히 커졌다 사라지는 선형 포락선 → 울림 버스.</summary>
    sealed class PadVoice : Voice
    {
        static readonly OscType[] Ty = { OscType.Sawtooth, OscType.Sawtooth, OscType.Sine, OscType.Sine };
        static readonly double[] Ratio = { 1, 1, 1.5, 2 }, Cents = { -7, 6, 0, -3 }, Amp = { 0.5, 0.5, 0.35, 0.3 };
        readonly double[] ph = new double[4], inc = new double[4]; readonly float[][] tab = new float[4][];
        WebParam gain; readonly Biquad lp = new Biquad();
        public void Init(SynthEngine e, double f, double dur, double vol)
        {
            int sr = e.Sr; double t0 = e.Now + 0.02;
            Start = SampleAt(t0, sr); Stop = SampleAt(t0 + dur + 0.1, sr);
            if (gain == null) gain = new WebParam(sr);
            gain.Reset(1, Start); gain.SetValueAtTime(0.0001, t0); gain.LinearRampToValueAtTime(vol, t0 + dur * 0.4); gain.LinearRampToValueAtTime(0.0001, t0 + dur);
            lp.Reset(); lp.Set(BqType.Lowpass, 650, 0.3, sr);
            for (int k = 0; k < 4; k++) { double ff = f * Ratio[k] * Math.Pow(2.0, Cents[k] / 1200.0); inc[k] = ff / sr; ph[k] = 0; tab[k] = OscTables.Get(Ty[k], ff, sr); }
        }
        public override void Render(SynthEngine e, long b0, int count)
        {
            for (int i = 0; i < count; i++)
            {
                long s = b0 + i; if (s < Start) continue; if (s >= Stop) break;
                double sum = 0;
                for (int k = 0; k < 4; k++) { sum += OscTables.Read(tab[k], ph[k]) * Amp[k]; ph[k] += inc[k]; ph[k] -= Math.Floor(ph[k]); }
                float y = (float)(lp.Process(sum) * gain.Next());
                e.BusL[i] += y; e.BusR[i] += y;
            }
        }
    }

    /// <summary>원본 gullCall()의 울음 하나: 톱니파(떨림 LFO 로 높이가 흔들림) → 입 모양 밴드패스 → 포락선 → 울림 버스.</summary>
    sealed class GullVoice : Voice
    {
        WebParam freq, gain; readonly Biquad bp = new Biquad(); double ph, lph, lfoInc, lfoAmp, invSr; float[] saw; int quirkLeft;
        public void Init(SynthEngine e, double when, double d, double p, double lfoHz, double vol)
        {
            int sr = e.Sr; invSr = 1.0 / sr; double t0 = e.Now + when;
            Start = OscStart(t0, sr); Stop = SampleAt(t0 + d + 0.05, sr);
            if (freq == null) { freq = new WebParam(sr); gain = new WebParam(sr); }
            freq.Reset(p * 0.8, Start); freq.SetValueAtTime(p * 0.8, t0); freq.LinearRampToValueAtTime(p * 1.35, t0 + d * 0.28); freq.ExponentialRampToValueAtTime(p * 0.7, t0 + d);
            gain.Reset(1, Start); gain.SetValueAtTime(0.0001, t0); gain.ExponentialRampToValueAtTime(vol, t0 + 0.05); gain.ExponentialRampToValueAtTime(0.0001, t0 + d);
            bp.Reset(); bp.Set(BqType.Bandpass, 2300, 1.1, sr);
            lfoInc = lfoHz * invSr; lfoAmp = p * 0.05; ph = 0; lph = 0;
            quirkLeft = QuirkFrames(t0, sr, Start);
            saw = OscTables.Get(OscType.Sawtooth, p * 1.35 + lfoAmp, sr);
        }
        public override void Render(SynthEngine e, long b0, int count)
        {
            var sine = OscTables.Get(OscType.Sine, 0, 0);
            for (int i = 0; i < count; i++)
            {
                long s = b0 + i; if (s < Start) continue; if (s >= Stop) break;
                double lfo = OscTables.Read(sine, lph) * lfoAmp; lph += lfoInc; lph -= Math.Floor(lph);
                float y = (float)(bp.Process(OscTables.Read(saw, ph)) * gain.Next());
                double fr = freq.Next() + lfo; if (quirkLeft > 0) { quirkLeft--; fr = 440; }
                ph += fr * invSr; ph -= Math.Floor(ph);
                e.BusL[i] += y; e.BusR[i] += y;
            }
        }
    }

    /// <summary>환경음 한 겹(원본 layer()): 6초 스테레오 소음 되풀이 → 필터 → 물결 세기(s) → 전체 크기(g).</summary>
    sealed class AmbLayer
    {
        public readonly WebParam S, G, F; public readonly double Base; readonly BqType type; readonly double q; readonly float[][] buf; double pos; readonly Biquad bl = new Biquad(), br = new Biquad(); readonly int sr; int c16;
        public AmbLayer(int sr, float[][] buf, BqType type, double freq, double q, double baseGain, double offsetSec)
        {
            this.sr = sr; this.buf = buf; this.type = type; this.q = q; Base = baseGain;
            S = new WebParam(sr, baseGain); G = new WebParam(sr, 0); F = new WebParam(sr, freq);
            pos = offsetSec * sr; int len = buf[0].Length; pos -= Math.Floor(pos / len) * len;
            bl.Set(type, freq, q, sr); br.Set(type, freq, q, sr);
        }
        public void Render(SynthEngine e, int count)
        {
            int len = buf[0].Length; var a = buf[0]; var b = buf[1];
            for (int i = 0; i < count; i++)
            {
                double f = F.Next(); if ((c16++ & 15) == 0 && F.Busy) { bl.Set(type, f, q, sr); br.Set(type, f, q, sr); }
                int i0 = (int)pos; double fr = pos - i0; int i1 = i0 + 1; if (i1 >= len) i1 = 0;
                double xl = a[i0] + (a[i1] - a[i0]) * fr, xr = b[i0] + (b[i1] - b[i0]) * fr;
                pos += 1; if (pos >= len) pos -= len;
                double k = S.Next() * G.Next();
                e.DryL[i] += (float)(bl.Process(xl) * k); e.DryR[i] += (float)(br.Process(xr) * k);
            }
        }
    }
}
