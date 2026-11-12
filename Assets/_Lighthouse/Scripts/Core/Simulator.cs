using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Core
{
    /// <summary>simulate() 의 결과 (원본 5496~5529행과 같은 구조).</summary>
    public sealed class SimResult
    {
        public double ShipsPerDay; public int Days, VisitEvery; public long Seed;
        public Dictionary<int, int> Stage = new Dictionary<int, int>();
        public Dictionary<string, int> Ms = new Dictionary<string, int>(), Rank = new Dictionary<string, int>(), Dex = new Dictionary<string, int>();
        public double OfflineShips;
        public Dictionary<string, double> FinalGrowth = new Dictionary<string, double>();
        public int FinalStage; public double FinalGuided; public Dictionary<string, int> FinalLv = new Dictionary<string, int>();
        public int FinalTotal, FinalDiscovered, FinalLetters, FinalShinyKinds;
        public long RngCalls;

        public JObject ToJson()
        {
            JObject Ji<TK>(Dictionary<TK, int> d) { var o = new JObject(); foreach (var kv in d) o[kv.Key.ToString()] = kv.Value; return o; }
            var g = new JObject(); foreach (var kv in FinalGrowth) g[kv.Key] = Js.N(kv.Value);
            return new JObject
            {
                ["shipsPerDay"] = Js.N(ShipsPerDay), ["days"] = Days, ["seed"] = Seed, ["visitEvery"] = VisitEvery,
                ["stage"] = Ji(Stage), ["ms"] = Ji(Ms), ["rank"] = Ji(Rank), ["dex"] = Ji(Dex), ["offlineShips"] = Js.N(OfflineShips),
                ["final"] = new JObject { ["growth"] = g, ["stage"] = FinalStage, ["guided"] = Js.N(FinalGuided), ["lv"] = Ji(FinalLv), ["total"] = FinalTotal, ["discovered"] = FinalDiscovered, ["letters"] = FinalLetters, ["shinyKinds"] = FinalShinyKinds }
            };
        }
    }

    /// <summary>
    /// 장기 진행 시뮬레이터 (원본 simulate, GD-12): 실제 게임 함수(RunOffline·RewardShip·AutoOfferAll·CanUpgrade)를 그대로 불러
    /// 저장·화면 없이 며칠 치를 돌린다. 상태 객체는 새로 만들어 쓰므로 전역 복원이 필요 없다. IRng(Mulberry32)·IClock 주입.
    /// 같은 seed 에서 원본과 같은 결과가 나오도록(seed 일치) 난수 호출 순서를 원본과 맞췄다.
    /// </summary>
    public static class Simulator
    {
        const double OnlinePerMin = 7.8;       // 온라인 안내 속도 가정: 분당 7.8척 (하루 468척 = 1시간)

        public static SimResult Run(GameTables T, IClock clock, double shipsPerDay = 468, int days = 120, long seed = 1, int visitEvery = 1, Action<int, GameContext> dayHook = null)
        {
            var rng = new Mulberry32(seed);
            var smig = new SaveMigrator(T, clock);
            var state = GameState.FromJson(smig.Defaults());
            state.Seen = true; state.Stage = 1;
            var ctx = new GameContext(T, state, rng, clock) { Sim = true, Stage = 1 };
            var res = new SimResult { ShipsPerDay = shipsPerDay, Days = days, Seed = seed, VisitEvery = visitEvery };
            string[] seasons = GameTables.SeasonIds;
            void MarkI<K>(Dictionary<K, int> d, K key, int day) { if (!d.ContainsKey(key)) d[key] = day; }
            int nGift = T.Gifts.Count;
            int[] dexMarks = { 4, 10, 14, 16 };
            string[] dexNames = { "발견 4", "발견 10", "발견 14", "발견 16" };
            var levels = new[] { 5, 8, 10, 12, 16 };

            void Check(int day)
            {
                while (ctx.CanUpgrade()) { ctx.Stage++; state.Stage = ctx.Stage; MarkI(res.Stage, ctx.Stage, day); }
                foreach (var tr in T.Tracks) foreach (int L in levels) if (ctx.LvlOf(tr.Id) >= L) MarkI(res.Ms, tr.Name + " Lv." + L, day);
                for (int i = 0; i < T.Ranks.Count; i++) if (i != 0 && ctx.TotalLv() >= T.Ranks[i].Min) MarkI(res.Rank, i + ". " + T.Ranks[i].Name + "(합 " + T.Ranks[i].Min + ")", day);
                int gf = 0, sh = 0;
                foreach (var g in T.Gifts) { if (state.Found.TryGetValue(g.Id, out double f) && f != 0) gf++; if (state.Shiny.TryGetValue(g.Id, out double s) && s != 0) sh++; }
                int disc = ctx.Discovered();
                for (int i = 0; i < dexMarks.Length; i++) if (disc >= dexMarks[i]) MarkI(res.Dex, dexNames[i], day);
                if (gf >= nGift) MarkI(res.Dex, "선물 " + nGift + "종 모두", day);
                if (state.Dex.Count >= T.Ships.Count) MarkI(res.Dex, "배 모두", day);
                if (sh >= nGift) MarkI(res.Dex, "빛나는 " + nGift + "종 모두", day);
            }

            double onlineSec = shipsPerDay / OnlinePerMin * 60;
            for (int d = 1; d <= days; d++)
            {
                ctx.SeasonForce = seasons[((d - 1) / 91) % 4];
                if ((d - 1) % visitEvery != 0) continue;       // 접속하지 않는 날: 방치 보상은 다음 접속 때 한꺼번에 (한도까지만)
                if (d > 1)
                {
                    var news = ctx.RunOffline(visitEvery * 86400.0 - onlineSec);
                    if (news != null) res.OfflineShips += news.Count;
                    Check(d);
                    dayHook?.Invoke(d, ctx);          // G2': 꾸미기 해금 진행도 기록(난수·상태를 건드리지 않는 읽기 전용 훅)
                }
                for (int i = 0; i < shipsPerDay; i++)
                {
                    ctx.CondOv = ctx.SampleCond();
                    ctx.RewardShip(ctx.ShipTypeAt(rng.NextDouble()), false, true);
                    ctx.CondOv = null;
                    ctx.AutoOfferAll();
                    Check(d);
                }
                dayHook?.Invoke(d, ctx);
            }
            foreach (var kv in state.Growth) res.FinalGrowth[kv.Key] = kv.Value;
            res.FinalStage = ctx.Stage; res.FinalGuided = state.Guided;
            foreach (var tr in T.Tracks) res.FinalLv[tr.Id] = ctx.LvlOf(tr.Id);
            res.FinalTotal = ctx.TotalLv(); res.FinalDiscovered = ctx.Discovered(); res.FinalLetters = state.Letters.Count;
            res.FinalShinyKinds = state.Shiny.Count(kv => kv.Value != 0);
            res.RngCalls = rng.Calls;
            return res;
        }
    }
}
