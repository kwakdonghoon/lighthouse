using System;
using System.Collections.Generic;
using System.Linq;

namespace Lighthouse.Core
{
    /// <summary>꾸미기 해금 진행도 모델의 결과: 아이템·자리가 처음 열린 일차(접속한 날 기준), 일차별 열린 자리 수.</summary>
    public sealed class DecorUnlockResult
    {
        public Dictionary<string, int> ItemDay = new Dictionary<string, int>(), SlotDay = new Dictionary<string, int>();
        public Dictionary<int, int> OpenSlots = new Dictionary<int, int>(), OwnedItems = new Dictionary<int, int>();
        public int Days;

        /// <summary>[from, to] 일차 사이에 처음 열린 아이템 수(해금 경로가 구매(iap)인 것은 센다 안 함: includePaid 로 포함 가능).</summary>
        public int ItemsIn(GameTables T, int from, int to, bool includePaid = false)
            => ItemDay.Count(kv => kv.Value >= from && kv.Value <= to && (includePaid || T.DecorItemById[kv.Key].Src != "iap"));
        public int SlotsIn(int from, int to) => SlotDay.Count(kv => kv.Value >= from && kv.Value <= to);
    }

    /// <summary>
    /// 꾸미기 해금 진행도 모델(G2'): 기존 장기 시뮬레이터(Simulator)에 읽기 전용 훅을 걸어 하루마다 "새로 열린 아이템·자리"를 센다.
    /// 수치 규칙(GOAL·OFFLINE_RATE·DEX_NEED)과 난수 흐름은 건드리지 않는다. 계절 아이템은 시뮬이 정하는 계절(spring→summer→autumn→winter, 91일씩)을
    /// 만난 날 열린다(실제 게임은 달력 계절이라 사람마다 다르고, 설정에서 계절을 고르면 바로 열린다 — 일차로 분산되는 아이템이 아님).
    /// 첫날(1일차)은 시작 상태(아이템 free)도 센다. 팩(iap)은 구매라 해금 일차에 들어가지 않는다.
    /// </summary>
    public static class DecorUnlockModel
    {
        public static DecorUnlockResult Run(GameTables T, IClock clock, double shipsPerDay, int days, long seed, int visitEvery)
        {
            var res = new DecorUnlockResult { Days = days };
            Simulator.Run(T, clock, shipsPerDay, days, seed, visitEvery, (d, ctx) =>
            {
                var S = ctx.S; int gl = ctx.LvlOf("garden");
                if (!string.IsNullOrEmpty(ctx.SeasonForce)) DecorRules.SeasonTick(T, S, ctx.SeasonForce);
                foreach (var it in T.DecorItems)
                    if (!res.ItemDay.ContainsKey(it.Id) && DecorRules.Owned(T, S, it.Id, gl)) res.ItemDay[it.Id] = d;
                int open = 0;
                foreach (var sl in T.DecorSlots)
                    if (DecorRules.SlotOpen(sl, ctx.Stage, gl)) { open++; if (!res.SlotDay.ContainsKey(sl.Id)) res.SlotDay[sl.Id] = d; }
                res.OpenSlots[d] = open; res.OwnedItems[d] = res.ItemDay.Count(kv => T.DecorItemById[kv.Key].Src != "iap");
            });
            return res;
        }
    }
}
