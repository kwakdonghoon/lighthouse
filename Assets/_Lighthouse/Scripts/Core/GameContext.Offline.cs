using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Core
{
    public sealed class ExtraGift { public string Gift; public bool Shiny; }

    /// <summary>안내를 마친 배 한 척의 보상 결과.</summary>
    public sealed class RewardResult
    {
        public string Gift; public Letter Letter; public bool Shiny; public ExtraGift Extra; public int Guests;
    }

    public sealed class GrowthChange { public string Name; public int From, To; }

    /// <summary>runOffline 의 결과(소식 화면의 재료).</summary>
    public sealed class OfflineResult
    {
        public int Count; public Dictionary<string, double> Gifts = new Dictionary<string, double>(), Shiny = new Dictionary<string, double>();
        public int Letters; public double Away; public List<DiaryEntry> Diary = new List<DiaryEntry>(); public string Home;
        public List<GrowthChange> Growth = new List<GrowthChange>(); public double? First; public int RegVisits, RegStories; public bool Quiet, Capped; public double MaxH;

        public JObject ToJson()
        {
            JObject Jd(Dictionary<string, double> d) { var o = new JObject(); foreach (var kv in d) o[kv.Key] = Js.N(kv.Value); return o; }
            return new JObject
            {
                ["count"] = Count, ["gifts"] = Jd(Gifts), ["shiny"] = Jd(Shiny), ["letters"] = Letters, ["away"] = Js.N(Away),
                ["diary"] = new JArray(Diary.Select(d => (JToken)d.ToJson())), ["home"] = Home != null ? (JToken)new JValue(Home) : JValue.CreateNull(),
                ["growth"] = new JArray(Growth.Select(g => (JToken)new JObject { ["name"] = g.Name, ["from"] = g.From, ["to"] = g.To })),
                ["first"] = First.HasValue ? (JToken)Js.N(First.Value) : JValue.CreateNull(), ["regVisits"] = RegVisits, ["regStories"] = RegStories,
                ["quiet"] = Quiet, ["capped"] = Capped, ["maxH"] = Js.N(MaxH)
            };
        }
    }

    public sealed partial class GameContext
    {
        /// <summary>안내를 마친 배 한 척의 보상: 선물, 도감, (가끔) 편지. 난수 소비 순서는 원본 rewardShip 그대로.</summary>
        public RewardResult RewardShip(string type, bool noLetter, bool quiet)
        {
            bool first = S.Guided == 0; string gift = RollGift(type);
            bool newShip = !(S.Dex.TryGetValue(type, out double dv) && dv != 0);
            S.Guided++; Inc(S.Dex, type);
            if (newShip && !quiet && !Sim) QueueFirst("ship", type);
            bool shiny = AddGift(gift, new AddGiftOpts { Quiet = quiet });
            ExtraGift extra = null;
            if (Rand() < HarvestChance()) { string g2 = RollGift(type); extra = new ExtraGift { Gift = g2, Shiny = AddGift(g2, new AddGiftOpts { Quiet = quiet }) }; }
            Letter letter = null;
            if (!noLetter && (first || Rand() < T.Bal.LetterChance)) { letter = MakeLetter(gift, shiny); AddLetter(letter); }
            AddGrowth();
            int guests = Sim ? 0 : DeliverGuests(quiet);
            if (!quiet) AutoOfferAll();
            Persist();
            return new RewardResult { Gift = gift, Letter = letter, Shiny = shiny, Extra = extra, Guests = guests };
        }

        /// <summary>방치 중 단골 방문: 지금 바다·지금 등급에 나올 수 있는 단골만, 이야기를 다 들은 단골은 건너뛴다. (방문 수, 이야기 수).</summary>
        public int[] OfflineRegulars(double away, string skipId, Dictionary<string, double> gifts)
        {
            var b = T.Bal;
            double m = away / 3600 * b.OfflineRegPerHour; int trials = away >= b.OfflineRegLongSec ? 2 : 1; double p = Math.Min(b.OfflineRegMaxP, m / trials); int last = b.VisitAt[b.VisitAt.Length - 1];
            int visits = 0, stories = 0;
            foreach (var r in T.Regulars)
            {
                if ((r.Region ?? "home") != CurRegion() || Tier() < r.Stage || r.Id == skipId || RegVisits(r.Id) >= last) continue;
                for (int i = 0; i < trials; i++)
                {
                    if (!(Rand() < p)) continue;
                    visits++;
                    var l = VisitRegular(r.Id, true);
                    if (l != null) { stories++; if (gifts != null && l.Gift != null) Inc(gifts, l.Gift); }
                }
            }
            return new[] { visits, stories };
        }

        /// <summary>runOffline: 접속하지 않은 동안의 보상. 90초 미만이면 null. 소식 화면은 Quiet(5분 미만)이면 띄우지 않는다.</summary>
        public OfflineResult RunOffline(double awaySec)
        {
            var b = T.Bal;
            double maxSec = OfflineMax(), away = Math.Min(awaySec, maxSec);
            if (away < b.OfflineMinSec) return null;
            int n = (int)Progress.OfflineShips(T, away);
            var gifts = new Dictionary<string, double>(); var shiny = new Dictionary<string, double>(); var before = new Dictionary<string, int>();
            foreach (var tr in T.Tracks) before[tr.Id] = LvlOf(tr.Id);
            int letters = 0;
            for (int i = 0; i < n; i++)
            {
                CondOv = SampleCond();
                string type = ShipTypeAt(Rand());
                var r = RewardShip(type, letters >= 4, true);
                var m = r.Shiny ? shiny : gifts;
                Inc(m, r.Gift); if (r.Letter != null) letters++; letters += r.Guests;
                if (r.Extra != null) Inc(r.Extra.Shiny ? shiny : gifts, r.Extra.Gift);
            }
            CondOv = null;
            var diary = away >= b.OfflineDiaryMinSec ? MakeDiary(away) : new List<DiaryEntry>();
            foreach (var d in diary) if (d.Gift != null) Inc(d.Shiny ? shiny : gifts, d.Gift);
            string home = null;
            if (away >= b.OfflineHomeMinSec) { home = RollGift("trade"); AddGift(home, new AddGiftOpts { Shiny = true, Quiet = true }); Inc(shiny, home); AdsToday().HomeDay = TodayKey(); }
            double? first = null;
            if (!S.FirstBack && awaySec >= b.OfflineFirstBackMinSec)
            {
                S.FirstBack = true;
                var r = RegById(b.FirstBackRegular);
                if (!S.Reg.TryGetValue(r.Id, out var rec)) { rec = new RegRecord { V = 0 }; S.Reg[r.Id] = rec; }
                if (rec.V == 0)
                {
                    rec.V = 1;
                    string g = RollGift(r.Type); AddGift(g, new AddGiftOpts { NoShiny = true, Quiet = true }); Inc(gifts, g);
                    first = AddLetter(new Letter { Id = S.LetterId++, Ship = r.Ship, Cap = r.Cap, Gift = g, T = Clock.NowMs, Read = false, Reg = r.Id, Keep = new JValue(true), Region = r.Region ?? "home", Text = r.Story[0], Sign = r.Ship + "호 " + r.Cap + " 드림" }).Id; letters++;
                }
            }
            int regVisits = 0, regStories = 0;
            if (!Sim) { var rv = OfflineRegulars(away, first.HasValue ? b.FirstBackRegular : null, gifts); regVisits = rv[0]; regStories = rv[1]; letters += rv[1]; }
            AutoOfferAll();
            var growth = new List<GrowthChange>();
            foreach (var tr in T.Tracks) { int to = LvlOf(tr.Id); if (to > before[tr.Id]) growth.Add(new GrowthChange { Name = tr.Name, From = before[tr.Id], To = to }); }
            Persist();
            return new OfflineResult
            {
                Count = n, Gifts = gifts, Shiny = shiny, Letters = letters, Away = away, Diary = diary, Home = home, Growth = growth, First = first, RegVisits = regVisits, RegStories = regStories,
                Quiet = awaySec < b.OfflineQuietSec, Capped = awaySec > maxSec, MaxH = JsMath.Round(maxSec / 3600)
            };
        }
    }
}
