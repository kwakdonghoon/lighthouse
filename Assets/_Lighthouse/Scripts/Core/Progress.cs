using System;
using System.Collections.Generic;

namespace Lighthouse.Core
{
    /// <summary>
    /// 성장 곡선·레벨·계급·배율 같은 상태 없는 계산 (원본 lvAt/lvlOf/totalLv/lensMul/shinyChance… 1944~1959행).
    /// 값은 GameTables.Bal(balance.json)에서 온다. 모든 수는 double (부록 B-7).
    /// </summary>
    public static class Progress
    {
        static readonly Dictionary<GameTables, double[]> lvCache = new Dictionary<GameTables, double[]>();
        const int CacheLen = 600;

        /// <summary>lvAt(n): 레벨 n 에 필요한 누적 성장 점수 (n ≥ 1). 레벨 5 이후는 레벨마다 약 38% 씩 커진다.</summary>
        public static double LvAt(GameTables T, int n)
        {
            if (n < 1) return double.NaN;                  // 원본은 LV_BASE[-1] → undefined → NaN
            if (n > CacheLen) return Compute(T, n);
            double[] c;
            lock (lvCache)
            {
                if (!lvCache.TryGetValue(T, out c))
                {
                    c = new double[CacheLen + 1];
                    for (int i = 1; i <= CacheLen; i++) c[i] = Compute(T, i);
                    lvCache[T] = c;
                }
            }
            return c[n];
        }

        static double Compute(GameTables T, int n)
        {
            double v = T.LvBase[Math.Min(n, 4) - 1];
            int from = (int)T.Bal.LvGrowthFromLv;
            for (int i = from; i <= n; i++) v += JsMath.Round(T.Bal.LvGrowthBase * Math.Pow(T.Bal.LvGrowthRatio, i - from));
            return v;
        }

        /// <summary>lvlOf: 성장 점수 p 에서의 레벨.</summary>
        public static int LevelOf(GameTables T, double p)
        {
            int n = 0;
            while (p >= LvAt(T, n + 1)) n++;
            return n;
        }

        public static int EffLv(GameTables T, int lv, string track) => Math.Min(lv, T.LvCap[track]);

        /// <summary>계급 번호(원본 rankIdx).</summary>
        public static int RankIdx(GameTables T, int totalLv)
        {
            int i = 0; for (int j = 0; j < T.Ranks.Count; j++) if (totalLv >= T.Ranks[j].Min) i = j;
            return i;
        }

        /// <summary>렌즈 안내 속도 배율 (원본 lensMul).</summary>
        public static double LensMul(GameTables T, int lensLv)
        {
            var b = T.Bal; double L = lensLv;
            return Math.Min(b.LensMulMax, 1 + b.LensMulPerLv * Math.Min(L, T.LvCap["lens"]) + b.LensMulBeyondPerLv * Math.Max(0, L - T.LvCap["lens"]));
        }

        public static double LensShiny(GameTables T, int lensLv) => T.Bal.LensShinyPerLv * Math.Min(lensLv, T.Bal.LensShinyLvCap);

        /// <summary>빛나는 선물 확률 (원본 shinyChance): min(0.25, 0.03 + 0.003×레벨합 + 렌즈).</summary>
        public static double ShinyChance(GameTables T, int totalLv, int lensLv, double decorBonus = 0)
            => Math.Min(T.Bal.ShinyMax, T.Bal.ShinyBase + T.Bal.ShinyPerLv * totalLv + LensShiny(T, lensLv) + decorBonus);

        /// <summary>풍년(정원이 상한을 넘으면 선물이 하나 더) 확률.</summary>
        public static double HarvestChance(GameTables T, int gardenLv) => Math.Min(T.Bal.HarvestMax, T.Bal.HarvestPerLv * Math.Max(0, gardenLv - T.LvCap["garden"]));

        /// <summary>항구가 상한을 넘으면 단골이 더 자주 들른다.</summary>
        public static double RegularBonus(GameTables T, int portLv) => Math.Min(T.Bal.RegularBonusMax, T.Bal.RegularBonusPerLv * Math.Max(0, portLv - T.LvCap["port"]));

        /// <summary>오프라인 인정 최대 시간(초): 8시간 + 항구 레벨마다 1시간(최대 +6).</summary>
        public static double OfflineMax(GameTables T, int portLv) => (T.Bal.OfflineMaxBaseHours + Math.Min(T.Bal.OfflineMaxPortHoursCap, portLv)) * 3600;

        /// <summary>비운 시간(초, 한도 안)에 안내하는 배 수. 1e-9 보정을 그대로 둔다 (부록 B-7).</summary>
        public static double OfflineShips(GameTables T, double awaySec) => Math.Max(1, Math.Floor(awaySec * T.Bal.OfflineRate + T.Bal.OfflineShipEps));

        /// <summary>하루 시간대: 새벽 = 하루 주기의 끝 20%(빠른 모드) 또는 실제 시각 4:00~7:00(실제 시간 모드).</summary>
        public static bool IsDawnAt(string clockMode, long ms, IClock clock, double cycleSec = 600)
        {
            if (clockMode == "real")
            {
                var d = LocalTime.Of(ms, clock); int m = d.Hour * 60 + d.Minute;
                return m >= 240 && m < 420;
            }
            return (ms / 1000.0 % cycleSec) / cycleSec >= 0.8;
        }

        static double Smooth(double t) => t * t * (3 - 2 * t);

        /// <summary>duskAt(ms): 0 = 낮, 1 = 밤 (원본 588~595행).</summary>
        public static double DuskAt(string clockMode, long ms, IClock clock, double cycleSec = 600)
        {
            if (clockMode == "real")
            {
                var d = LocalTime.Of(ms, clock); double m = d.Hour * 60 + d.Minute + d.Second / 60.0;
                return m < 345 ? 1 : m < 375 ? 1 - Smooth((m - 345) / 30) : m < 1125 ? 0 : m < 1155 ? Smooth((m - 1125) / 30) : 1;
            }
            double p = (ms / 1000.0 % cycleSec) / cycleSec;
            return p < 0.4 ? 0 : p < 0.5 ? Smooth((p - 0.4) / 0.1) : p < 0.9 ? 1 : 1 - Smooth((p - 0.9) / 0.1);
        }

        /// <summary>nextMile: 갈래 k 에서 레벨 lv 보다 높은 첫 이정표(없으면 null). T.Milestones 는 갈래별·레벨 오름차순으로 정렬돼 있다.</summary>
        public static MilestoneDef NextMile(GameTables T, string k, int lv)
        {
            foreach (var m in T.Milestones) if (m.K == k && m.Lv > lv) return m;
            return null;
        }

        /// <summary>한국어 조사 고르기(원본 fixJosa 의 받침 판정): 마지막 글자가 받침이 있으면 true.</summary>
        public static bool HasFinal(char c) => (c - 0xAC00) % 28 != 0;
    }
}
