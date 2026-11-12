using System;
using System.Collections.Generic;
using System.Linq;

namespace Lighthouse.Core
{
    /// <summary>
    /// 꾸미기 자리 규칙(순수 로직; 원본 DECOR-CORE 3219~3330행 중 렌더와 무관한 부분).
    /// 자리 28개(막힘 8·열림 20), 열림 판정, 보유 판정, 놓을 수 있는지 판정. 좌표·가시 기준은 표(decor.json·balance.json)가 갖는다
    /// (뒤 구역 가시 65%·그 밖 80% 기준은 Balance.DecorVisMin/DecorVisMinBack — 픽셀로 재는 decorVisibility 는 래스터라이저가 있는 M2 가 쓴다).
    /// </summary>
    public static class DecorRules
    {
        /// <summary>slotOpenS(sl, stage, gl): 열림 판정. 막힘 자리는 열리지 않는다(저장 값은 보존).</summary>
        public static bool SlotOpen(DecorSlotDef sl, int stage, int gardenLv)
            => !sl.Blocked && stage >= sl.MinStage && (sl.OpenBase != 0 || (sl.OpenStage != 0 && stage >= sl.OpenStage) || (sl.OpenLv != 0 && gardenLv >= sl.OpenLv));

        /// <summary>구역의 자리 목록(막힘은 빠진다, 번호순).</summary>
        public static List<DecorSlotDef> ZoneSlots(GameTables T, string zone)
            => T.DecorSlots.Where(s => s.Chip == zone && !s.Blocked).OrderBy(s => s.Num).ToList();

        public static List<DecorSlotDef> ZoneOpen(GameTables T, string zone, int stage, int gardenLv)
            => ZoneSlots(T, zone).Where(s => SlotOpen(s, stage, gardenLv)).ToList();

        /// <summary>지금 열린 자리 수 (단계·정원 레벨 기준).</summary>
        public static int OpenCount(GameTables T, int stage, int gardenLv) => T.DecorSlots.Count(s => SlotOpen(s, stage, gardenLv));

        /// <summary>decorState: "ok" 놓을 수 있음 · "lock" 잠김 · "big" 자리보다 큼.</summary>
        public static string State(GameTables T, GameState s, string itemId, string slotId, int gardenLv)
        {
            if (!Owned(T, s, itemId, gardenLv)) return "lock";
            if (slotId != null && T.SizeRank[T.DecorItemById[itemId].Size] > T.SizeRank[T.DecorSlotById[slotId].Size]) return "big";
            return "ok";
        }

        /// <summary>장식 보유 판정: 레벨·단골·받은 선물·계절 기록에서 파생(소비 없음, 한 번 열리면 계속).</summary>
        public static bool Owned(GameTables T, GameState s, string id, int gardenLvOverride = -1)
        {
            if (!T.DecorItemById.TryGetValue(id, out var it)) return false;
            switch (it.Src)
            {
                case "free": return true;
                case "earn":
                    {
                        s.Growth.TryGetValue(it.NeedKey, out double p);
                        return Progress.LevelOf(T, FiniteOr0(p)) >= it.NeedVal;
                    }
                case "story":
                    {
                        if (!s.Reg.TryGetValue(it.NeedKey, out var r)) return false;
                        int idx = (int)it.NeedVal - 1;
                        return idx >= 0 && idx < T.Bal.VisitAt.Length && r.V >= T.Bal.VisitAt[idx];
                    }
                case "gift": return s.Totals.TryGetValue(it.NeedKey, out double g) && g >= it.NeedVal;
                case "season": return (s.Isle.Own.TryGetValue(id, out double o) && o != 0) || AltGuided(it, s);       // D44: 두 번째 경로(안내한 배 N척)
                case "guided": return s.Guided >= it.NeedVal;                                                           // G2' 2차 아이템: 누적 안내한 배 수(방치로 찬다)
                case "ad": return s.Guided >= it.NeedVal || (s.Isle.Own.TryGetValue(id, out double oa) && oa != 0);      // D43·U5: 업적 경로(안내한 배 N척). own 은 광고로 받는 경로 자리(광고 SDK 는 M8 뒤)
                case "iap": { var v = s.Owned[it.NeedKey]; return v != null && Js.Truthy(v); }                       // 팩: 구매 기록(스토어 영수증이 정본)
                default: return false;
            }
        }

        /// <summary>두 번째 해금 경로(alt = ['guided', N]): 누적 안내한 배 수가 N 이상.</summary>
        public static bool AltGuided(DecorItemDef it, GameState s) => it.AltStr != null && it.AltStr.Length > 1 && it.AltStr[0] == "guided" && s.Guided >= it.AltNum[1];

        static double FiniteOr0(double p) => double.IsNaN(p) || double.IsInfinity(p) ? 0 : p;

        /// <summary>계절 장식(호박 더미): 그 계절을 한 번 만나면 계속 가진다. 바뀌었으면 true.</summary>
        public static bool SeasonTick(GameTables T, GameState s, string season)
        {
            bool ch = false;
            foreach (var it in T.DecorItems)
                if (it.Src == "season" && it.NeedKey == season && !(s.Isle.Own.TryGetValue(it.Id, out double v) && v != 0)) { s.Isle.Own[it.Id] = 1; ch = true; }
            return ch;
        }

        /// <summary>자리에 놓인 장식 id (없거나 모르는 장식이면 null).</summary>
        public static string PutOf(GameTables T, GameState s, string slotId)
            => s.Isle.Put.TryGetValue(slotId, out var v) && T.DecorItemById.ContainsKey(v) ? v : null;

        /// <summary>그 장식이 지금 열린 자리에 몇 개 놓여 있는지.</summary>
        public static int PlacedCount(GameTables T, GameState s, string itemId, int stage, int gardenLv)
            => s.Isle.Put.Count(kv => kv.Value == itemId && T.DecorSlotById.TryGetValue(kv.Key, out var sl) && SlotOpen(sl, stage, gardenLv));
    }
}
