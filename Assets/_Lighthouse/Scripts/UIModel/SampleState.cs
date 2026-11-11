using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.UI.Model
{
    /// <summary>
    /// 화면 시험·캡처용 가짜 진행 상태. 실제 Core 규칙(RewardShip·성장·단골 방문)을 씨앗 고정 난수로 돌려 만든다 — 손으로 쓴 값이 아니라
    /// 실제 게임이 만들 수 있는 상태만 나온다.
    /// </summary>
    public static class SampleState
    {
        public static GameContext Make(GameTables T, IClock clock, long seed, int stage, int ships, int regularVisits = 0, bool night = false, string season = null)
        {
            var mig = new SaveMigrator(T, clock);
            var st = GameState.FromJson(mig.Defaults());
            st.Stage = stage; st.Seen = true; st.StartAt = clock.NowMs - 20L * 86400000L;
            var rng = new Mulberry32(seed);
            var c = new GameContext(T, st, rng, clock) { Stage = stage, Night = night, SeasonForce = season, Sim = false };
            c.Sim = true;      // 자연 도감·손님 편지 대기열을 건너뛰고 상태만 만든다
            for (int i = 0; i < ships; i++)
            {
                string type = c.ShipTypeAt(rng.NextDouble());
                c.RewardShip(type, false, true);
                c.AddGrowth(); st.Guided++;
            }
            c.Sim = false;
            if (regularVisits > 0)
            {
                foreach (var r in T.Regulars.Where(x => x.Stage <= c.Tier()).Take(3))
                    for (int v = 0; v < regularVisits; v++) c.VisitRegular(r.Id, true);
            }
            if (stage >= T.MaxStage) st.Finale = 1;
            foreach (var s in T.Sea.Take(6)) st.Sea[s.Id] = 1;
            st.DexSeen = Math.Max(0, c.Discovered() - 2);
            c.FirstQ.Clear();
            return c;
        }

    }

}
