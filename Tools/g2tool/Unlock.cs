using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lighthouse.Core;
using Lighthouse.Render;

namespace Lighthouse.G2Tool
{
    public static partial class Program
    {
        static readonly (string name, double ships, int days, int ve)[] UnlockProfiles =
            { ("A", 468, 120, 1), ("B", 192, 120, 1), ("C", 48, 120, 1), ("Z", 0, 120, 1), ("D", 48, 120, 3) };

        /// <summary>unlock: 이전(1차 10종·자리 20)과 지금(G2': 무료·업적 아이템 21종·자리 28)의 해금 분포. 마크다운 표로 출력.</summary>
        static void Unlock()
        {
            var T = Tables(); var clock = new FixedClock(1762831800000L, -540);
            var legacyItems = new HashSet<string>(DecorLayout.LegacyItemIds); var legacySlots = new HashSet<string>(DecorLayout.LegacySlotIds);
            var buckets = new[] { (1, 1, "1일"), (2, 7, "2~7일"), (8, 14, "8~14일"), (15, 30, "15~30일"), (31, 60, "31~60일"), (61, 120, "61~120일") };
            var cal = new HashSet<string> { "path_petal", "snowman" };
            var sb = new StringBuilder();
            sb.AppendLine("| 프로필 | 구분 | " + string.Join(" | ", buckets.Select(b => b.Item3)) + " | 합계 |");
            sb.AppendLine("|---|---|" + string.Join("|", buckets.Select(b => "---")) + "|---|");
            var dayRows = new List<string>();
            foreach (var p in UnlockProfiles)
            {
                var r = DecorUnlockModel.Run(T, clock, p.ships, p.days, 1, p.ve);
                int Cnt(IEnumerable<KeyValuePair<string, int>> src, int lo, int hi) => src.Count(kv => kv.Value >= lo && kv.Value <= hi);
                var before = r.ItemDay.Where(kv => legacyItems.Contains(kv.Key));
                var after = r.ItemDay.Where(kv => T.DecorItemById[kv.Key].Src != "iap" && !cal.Contains(kv.Key));
                var slotsB = r.SlotDay.Where(kv => legacySlots.Contains(kv.Key)); var slotsA = r.SlotDay;
                string Row(string kind, IEnumerable<KeyValuePair<string, int>> d) { var c = buckets.Select(b => Cnt(d, b.Item1, b.Item2)).ToList(); return "| " + p.name + " | " + kind + " | " + string.Join(" | ", c) + " | " + c.Sum() + " |"; }
                sb.AppendLine(Row("아이템 이전(10종)", before)); sb.AppendLine(Row("아이템 G2'(달력 2종 제외)", after));
                sb.AppendLine(Row("자리 이전(20)", slotsB)); sb.AppendLine(Row("자리 G2'(28)", slotsA));
                var cat = T.DecorItems.Where(i => i.Src != "iap").Select(i => i.Id + "=" + (r.ItemDay.TryGetValue(i.Id, out int dd) ? dd.ToString() : "-"));
                dayRows.Add("[" + p.name + "] " + string.Join(", ", cat));
                dayRows.Add("[" + p.name + "] 자리 " + string.Join(", ", T.DecorSlots.Select(s => s.Id + "=" + (r.SlotDay.TryGetValue(s.Id, out int sd) ? sd.ToString() : "-"))));
                var ob = new StringBuilder("[" + p.name + "] 누적 열린 자리/보유(무료·업적) 아이템: ");
                foreach (int d in new[] { 1, 7, 14, 30, 60, 120 }) { int dd = d; while (!r.OpenSlots.ContainsKey(dd) && dd > 1) dd--; ob.Append(d + "일 " + r.OpenSlots[dd] + "/" + r.OwnedItems[dd] + "  "); }
                dayRows.Add(ob.ToString());
            }
            Console.WriteLine(sb); foreach (var l in dayRows) Console.WriteLine(l);
        }
    }
}
