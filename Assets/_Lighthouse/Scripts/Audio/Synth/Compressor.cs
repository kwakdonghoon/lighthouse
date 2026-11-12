using System;

namespace Lighthouse.Audio
{
    /// <summary>
    /// DynamicsCompressorNode 근사(원본: threshold -20, knee 24, ratio 3, attack 0.02, release 0.4).
    /// 크롬 구현의 구성 요소를 따른다: ① 6ms 앞선 감지(출력이 6ms 늦는다) ② 정적 곡선(무릎 곡선 + 일정 비율)
    /// ③ 풀레인지 보정이득 (1/곡선(1))^0.6 — 작은 소리도 약 +1.3dB 올라간다 ④ 어택·릴리스 평활.
    /// ④의 적응형 릴리스(다항식 구간)는 단순 1극 평활로 대신한다. 원본의 소리 크기(피크 -23dB 안팎)에서는 ①②③이 거의 전부다.
    /// </summary>
    public sealed class Compressor
    {
        readonly double linThr, kneeThr, ypk, slope, k, makeup, attackCoef, releaseCoef;
        readonly int delay; readonly float[] dl, dr; int wpos; double gain = 1;

        public double Makeup => makeup;

        public Compressor(int sr, double thresholdDb = -20, double kneeDb = 24, double ratio = 3, double attack = 0.02, double release = 0.4)
        {
            linThr = Db2Lin(thresholdDb); slope = 1.0 / ratio; kneeThr = Db2Lin(thresholdDb + kneeDb);
            // KAtSlope: 무릎 끝에서 기울기가 1/ratio 가 되는 k 를 이분(기하평균)으로 찾는다
            double minK = 0.1, maxK = 10000, kk = 5;
            for (int i = 0; i < 15; i++)
            {
                double s = SlopeAt(kneeThr, kk);
                if (s < slope) maxK = kk; else minK = kk;
                kk = Math.Sqrt(minK * maxK);
            }
            k = kk; ypk = Lin2Db(KneeCurve(kneeThr, k)); kneeDbV = thresholdDb + kneeDb;
            makeup = Math.Pow(1.0 / Saturate(1.0), 0.6);
            attackCoef = 1 - Math.Exp(-1.0 / (Math.Max(0.001, attack) * sr / 3));        // 어택: 약 3 시정수로 목표에 닿는다
            releaseCoef = 1 - Math.Exp(-1.0 / (release * sr / 3));
            delay = (int)(0.006 * sr); dl = new float[delay + 1]; dr = new float[delay + 1];
        }
        readonly double kneeDbV;

        static double Db2Lin(double db) { return Math.Pow(10.0, 0.05 * db); }
        static double Lin2Db(double x) { return 20.0 * Math.Log10(x); }
        double KneeCurve(double x, double kk) { return x < linThr ? x : linThr + (1 - Math.Exp(-kk * (x - linThr))) / kk; }
        double SlopeAt(double x, double kk)
        {
            if (x < linThr) return 1;
            double x2 = x * 1.001, xd = Lin2Db(x), x2d = Lin2Db(x2), yd = Lin2Db(KneeCurve(x, kk)), y2d = Lin2Db(KneeCurve(x2, kk));
            return (y2d - yd) / (x2d - xd);
        }
        double Saturate(double x)
        {
            if (x < kneeThr) return KneeCurve(x, k);
            double xd = Lin2Db(x), yd = ypk + slope * (xd - kneeDbV); return Db2Lin(yd);
        }

        public void Process(float[] l, float[] r, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double a = Math.Max(Math.Abs(l[i]), Math.Abs(r[i]));
                double target = a <= 0.0001 ? 1.0 : Saturate(a) / a;
                gain += (target - gain) * (target < gain ? attackCoef : releaseCoef);
                float il = l[i], ir = r[i];
                int rp = wpos + 1; if (rp > delay) rp = 0;                    // 가장 오래된 표본 = 지연된 입력
                float ol = dl[rp], or = dr[rp];
                dl[wpos] = il; dr[wpos] = ir; wpos = rp;
                double g = gain * makeup;
                l[i] = (float)(ol * g); r[i] = (float)(or * g);
            }
        }
    }
}
