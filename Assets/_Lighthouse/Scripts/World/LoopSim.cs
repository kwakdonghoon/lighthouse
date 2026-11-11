using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.World
{
    /// <summary>
    /// 가속 시계 구동 (시험·진단용): 게임 루프(GameLoop)의 같은 경로 — 접속 복귀(ReturnFromAway)·배 한 척 도착(Arrive)·공사 즉시 완료(UpgradeInstant) — 를
    /// Simulator 와 같은 순서·같은 난수 열로 며칠 치 돌린다. 같은 seed 에서 Simulator.Run 과 결과가 같아야 한다(단계 도달일·도감·최종 값).
    /// 실제 게임의 배 추첨·단골 방문·날씨 같은 연출은 규칙 난수와 분리돼 있어(연출 난수 fx) 이 구동에는 끼지 않는다.
    /// </summary>
    public static class LoopSim
    {
        const double OnlinePerMin = 7.8;

        public static SimResult Run(GameTables T, IClock clock, double shipsPerDay = 468, int days = 120, long seed = 1, int visitEvery = 1)
        {
            var rng = new Mulberry32(seed);
            var smig = new SaveMigrator(T, clock);
            var state = GameState.FromJson(smig.Defaults());
            state.Seen = true; state.Stage = 1;
            var ctx = new GameContext(T, state, rng, clock) { Sim = true, Stage = 1 };
            var loop = new GameLoop(ctx, null, new Mulberry32(seed ^ 0x5bd1e995));
            var res = new SimResult { ShipsPerDay = shipsPerDay, Days = days, Seed = seed, VisitEvery = visitEvery };
            string[] seasons = GameTables.SeasonIds;
            void MarkI<K>(Dictionary<K, int> d, K key, int day) { if (!d.ContainsKey(key)) d[key] = day; }
            int nGift = T.Gifts.Count; int[] dexMarks = { 4, 10, 14, 16 }; string[] dexNames = { "발견 4", "발견 10", "발견 14", "발견 16" }; var levels = new[] { 5, 8, 10, 12, 16 };

            void Check(int day)
            {
                loop.UpgradeInstant(st => MarkI(res.Stage, st, day));
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
                if ((d - 1) % visitEvery != 0) continue;
                if (d > 1)
                {
                    var news = loop.ReturnFromAway(visitEvery * 86400.0 - onlineSec);
                    if (news != null) res.OfflineShips += news.Count;
                    Check(d);
                }
                for (int i = 0; i < shipsPerDay; i++)
                {
                    var cond = ctx.SampleCond();
                    string type = ctx.ShipTypeAt(rng.NextDouble());
                    loop.Arrive(type, cond, true);
                    Check(d);
                }
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
