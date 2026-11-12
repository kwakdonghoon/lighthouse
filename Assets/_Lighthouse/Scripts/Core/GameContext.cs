using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lighthouse.Core
{
    /// <summary>"때"(밤·새벽·비·안개): 조건부 선물(GD-05)이 열리는 조건.</summary>
    public sealed class Cond { public bool Night, Dawn, Rain, Fog; }

    /// <summary>
    /// 게임 규칙 한 곳 (원본의 전역 save·STAGE·NIGHT·wx·condOv·SIM 과 그것을 읽고 쓰는 함수들).
    /// 순수 C#: Unity 를 모른다. 난수는 IRng, 시각은 IClock 으로만 얻는다.
    /// 저장 대상은 S(GameState), 저장하지 않는 런타임 값(Stage·Night·Wx 등)은 이 클래스의 필드다 (설계서 7-4 의 GameState/RuntimeState 분리).
    /// 이 파일: 진행(레벨·승급)·선물·성장·헌정. 편지·일지·단골은 GameContext.Letters.cs, 배 한 척 보상·오프라인은 GameContext.Offline.cs.
    /// 호출 순서가 곧 난수 소비 순서라 원본 함수의 평가 순서를 그대로 지켰다 (부록 B-5).
    /// </summary>
    public sealed partial class GameContext
    {
        public readonly GameTables T;
        public GameState S;
        public IRng Rng;
        public IClock Clock;

        // ----- 저장하지 않는 런타임 값 -----
        public int Stage = 1;                         // 등대 단계(원본 STAGE; 저장의 stage 와 따로 움직일 수 있다)
        public bool Night;                            // 지금 밤인지(원본 NIGHT)
        public string Wx = "clear";                   // 날씨 종류 clear|fog|rain (원본 wx.kind)
        public string SeasonForce;                    // 개발용 계절 고정(저장하지 않는다)
        public Cond CondOv;                           // 지금의 때를 덮어쓰는 값(오프라인 추첨·시뮬레이터·일지)
        public bool Sim;                              // 시뮬레이터가 도는 동안 true: 알림 대기열·자연 편지·손님 편지를 건너뛴다
        public string GrowthEvent;                    // 마지막 레벨 오름 알림 글
        public bool NeedRebuild;                      // 정원이 오르면 섬을 다시 구워야 함
        public double CycleSec = 600;                 // 하루 주기(빠른 모드)
        public bool TitleOpen;                        // 타이틀이 떠 있으면 seeSea 가 기록하지 않는다 (anyTime 이 아니면)
        public readonly List<string[]> FirstQ = new List<string[]>();     // 처음 만난 것 알림 대기열 [종류, id]
        public readonly Dictionary<string, List<string>> LetterRecent = new Dictionary<string, List<string>>();   // 편지 조각 최근 3개
        public Func<string, bool> RegularOnSea = id => false;             // 지금 바다에 그 단골 배가 있는지(화면 쪽이 채운다)

        // ----- 알림(렌더·UI 가 구독) -----
        public event Action<string, string> FirstMet;          // (종류, id)
        public event Action<Letter> LetterAdded;
        public event Action<string> ShinyFound;                // 빛나는 선물을 알릴 때 (선물 id) — 원본 addGift 의 토스트 자리. Quiet·Sim 에서는 안 불린다
        public event Action<int> LettersArrived;               // 손님·기념일·완결 편지가 도착했을 때(통수) — 원본 deliverGuests·deliverFinale 의 토스트 자리. quiet 에서는 안 불린다
        public event Action<string> GrowthChanged;             // 갈래 id
        public event Action StateChanged;                      // 원본 persist() 지점

        public GameContext(GameTables tables, GameState state, IRng rng, IClock clock)
        {
            T = tables; S = state; Rng = rng; Clock = clock; Stage = state.Stage;
        }

        void Persist() { StateChanged?.Invoke(); }
        /// <summary>M3: 화면(UI) 쪽에서 상태를 바꾼 뒤 저장·갱신을 알리는 공개 창구 (원본 persist() 호출과 같은 뜻).</summary>
        public void NotifyChanged() { Persist(); }
        double Rand() => Rng.NextDouble();

        // ================= 기본 조회 =================
        public static double Get(Dictionary<string, double> d, string k) => d.TryGetValue(k, out double v) ? v : 0;
        static void Inc(Dictionary<string, double> d, string k, double by = 1) { d.TryGetValue(k, out double v); d[k] = v + by; }

        /// <summary>TIER_OF[STAGE] || 1 : 기능 등급.</summary>
        public int Tier() { int t = T.TierAt(Stage); return t != 0 ? t : 1; }

        public string CurRegion() => S.Region != null && T.RegionById.TryGetValue(S.Region, out var r) && (r.Src == "free" || (S.Owned[("region:" + S.Region)] != null && Js.Truthy(S.Owned["region:" + S.Region]))) ? S.Region : "home";

        /// <summary>curSeason: 개발용 고정 → 저장의 계절 → 실제 날짜(3~5 봄, 6~8 여름, 9~11 가을, 12~2 겨울).</summary>
        public string CurSeason()
        {
            if (!string.IsNullOrEmpty(SeasonForce)) return SeasonForce;
            if (!string.IsNullOrEmpty(S.Season) && S.Season != "auto") return S.Season;
            int m = LocalTime.Of(Clock.NowMs, Clock).Month;
            return m >= 3 && m <= 5 ? "spring" : m >= 6 && m <= 8 ? "summer" : m >= 9 && m <= 11 ? "autumn" : "winter";
        }

        /// <summary>seasonKey: 겨울은 이듬해 1~2월까지 같은 해의 겨울.</summary>
        public string SeasonKey()
        {
            var d = LocalTime.Of(Clock.NowMs, Clock); string s = CurSeason();
            return (d.Year - (s == "winter" && d.Month - 1 < 2 ? 1 : 0)).ToString() + ":" + s;
        }

        public string TodayKey() => LocalTime.Of(Clock.NowMs, Clock).DayKey;
        string DayKeyOf(double ts) => LocalTime.Of((long)ts, Clock).DayKey;

        /// <summary>함께한 날: 처음 시작한 날부터 오늘까지의 달력 날짜 차 (연속 접속 일수가 아니다).</summary>
        public double DaysSinceStart() => Math.Max(0, JsMath.Round(SaveMigrator.DayNum(TodayKey()) - SaveMigrator.DayNum(DayKeyOf(S.StartAt))));

        // ================= 레벨·성장 =================
        public int LvlOf(string k) => Progress.LevelOf(T, Get(S.Growth, k));
        public int TotalLv() => LvlOf("lens") + LvlOf("garden") + LvlOf("port");
        public int EffLv(string k) => Math.Min(LvlOf(k), T.LvCap[k]);
        public double ShinyChance() => Progress.ShinyChance(T, TotalLv(), LvlOf("lens"));
        public double HarvestChance() => Progress.HarvestChance(T, LvlOf("garden"));
        public double RegularBonus() => Progress.RegularBonus(T, LvlOf("port"));
        public double OfflineMax() => Progress.OfflineMax(T, LvlOf("port"));
        public double LensMul() => Progress.LensMul(T, LvlOf("lens"));
        public int RankIdx() => Progress.RankIdx(T, TotalLv());

        /// <summary>discovered: 선물 종류 + 배 종류 + 단골 수 (빛나는 선물은 더하지 않는다, QA-208).</summary>
        public int Discovered() => S.Found.Count + S.Dex.Count + S.Reg.Count;

        /// <summary>canUpgrade: 공사 중이 아니고, 안내 수와 도감 발견 수가 다음 단계 조건에 닿았는가.</summary>
        public bool CanUpgrade()
        {
            if (S.Build != null) return false;
            int goal = Stage >= 0 && Stage < T.Goal.Length ? T.Goal[Stage] : 0;
            if (goal == 0) return false;
            return S.Guided >= goal && Discovered() >= (Stage < T.DexNeed.Length ? T.DexNeed[Stage] : 0);
        }

        /// <summary>안내한 배 한 척의 성장 점수를 정한 방향(정하지 않았으면 골고루)에 쌓는다.</summary>
        public void AddGrowth()
        {
            string k = T.TrackById.ContainsKey(S.Focus ?? "") ? S.Focus : T.Tracks[(int)(S.Guided % T.Tracks.Count)].Id;
            BumpGrowth(k, 1);
        }

        /// <summary>성장 점수를 더하고 레벨이 오르면 growthEvent 를 남긴다.</summary>
        public void BumpGrowth(string k, double pts)
        {
            int before = LvlOf(k);
            S.Growth[k] = Get(S.Growth, k) + pts;
            int now = LvlOf(k);
            if (now > before)
            {
                var ms = T.Milestones.Where(m => m.K == k && m.Lv > before && m.Lv <= now).Select(m => m.Text).ToList();
                GrowthEvent = T.TrackById[k].Name + " Lv." + now + (ms.Count > 0 ? " · 새 보상: " + string.Join(", ", ms) : "");
                if (k == "garden" && before < T.LvCap["garden"]) NeedRebuild = true;
                GrowthChanged?.Invoke(k);
            }
        }

        // ================= 배 종류·선물 추첨 =================
        /// <summary>shipTypeAt(r): r 은 0~1 뽑기 값. 오프라인 안내도 같은 분포를 쓴다.</summary>
        public string ShipTypeAt(double r)
        {
            var mix = T.ShipMix[Math.Max(1, Math.Min(T.MaxStage, Stage))];
            double total = 0; foreach (var e in mix) total += e.W;
            double acc = r * total;
            foreach (var e in mix) { acc -= e.W; if (acc <= 0) return e.Id; }
            return mix[0].Id;
        }

        public Cond SampleCond()
        {
            int t = Tier(); var b = T.Bal;
            var c = new Cond();
            c.Night = Rand() < b.CondNight;
            c.Dawn = Rand() < b.CondDawn;
            c.Rain = t >= 3 && CurSeason() != "winter" && Rand() < b.CondRain;
            c.Fog = t >= 2 && Rand() < (t >= 3 ? b.CondFog : b.CondOddsFogTier2);
            return c;
        }

        public bool IsDawnAt(long ms) => Progress.IsDawnAt(S.Clock, ms, Clock, CycleSec);

        /// <summary>condNow: 지금의 때. 겨울의 비는 눈이라 빗방울 구슬은 오지 않는다.</summary>
        public Cond CondNow() => CondOv ?? new Cond { Night = Night, Dawn = IsDawnAt(Clock.NowMs), Rain = Wx == "rain" && CurSeason() != "winter", Fog = Wx == "fog" };

        bool GiftOpen(GiftDef g, Cond c)
        {
            if (g.Season != null) return g.Season == CurSeason();
            bool condOk = g.Cond == null || CondFlag(c, g.Cond);
            return condOk && (g.Tier == 0 || Tier() >= g.Tier);
        }

        static bool CondFlag(Cond c, string k) => k == "night" ? c.Night : k == "dawn" ? c.Dawn : k == "rain" ? c.Rain : k == "fog" && c.Fog;

        /// <summary>rollGift(type, boost): 배 종류별 무게로 선물 하나를 뽑는다(난수 1회). boost: 무게를 높일 조건('fog'·'rain'), 없으면 null.</summary>
        public string RollGift(string type, string boost = null)
        {
            string reg = CurRegion();
            var ks = T.Gifts.Where(g => "home" == reg).ToList();     // 모든 선물은 첫 바다 소속(원본 rg(GIFTS[k]) 가 'home')
            if (ks.Count == 0) ks = T.Gifts.ToList();
            var c = CondNow();
            var ws = new double[ks.Count]; double total = 0;
            for (int i = 0; i < ks.Count; i++)
            {
                var g = ks[i];
                double w;
                if (!GiftOpen(g, c)) w = 0;
                else
                {
                    double basew;
                    if (g.Season != null) basew = T.Bal.GiftSeasonWeight;
                    else
                    {
                        string kind = T.GiftKind.TryGetValue(type, out var gk) ? gk : type;
                        double x = kind == "fish" ? g.WFish : kind == "trade" ? g.WTrade : 0;
                        basew = (g.HasW && (kind == "fish" || kind == "trade") && x != 0) ? x : 1;      // G.w[kind] || 1
                    }
                    w = basew * (boost != null && g.Cond == boost ? T.Bal.WxBoost : 1);
                }
                ws[i] = w; total += w;
            }
            double r = Rand() * total;
            for (int i = 0; i < ks.Count; i++) { r -= ws[i]; if (r <= 0) return ks[i].Id; }
            return ks[0].Id;
        }

        // ================= 선물 =================
        public sealed class AddGiftOpts { public bool? Shiny; public bool NoShiny; public bool Quiet; }

        /// <summary>선물 한 개를 얻는다. 가끔 빛나는 변종으로 온다. 빛나는 선물이면 true.</summary>
        public bool AddGift(string g, AddGiftOpts o = null)
        {
            o = o ?? new AddGiftOpts();
            bool shiny = o.Shiny.HasValue ? o.Shiny.Value : (!o.NoShiny && Rand() < ShinyChance());
            bool firstGift = !S.Found.ContainsKey(g) || S.Found[g] == 0;
            if (shiny) Inc(S.Shiny, g); else Inc(S.Gifts, g);
            S.Found[g] = 1;
            if (firstGift && !o.Quiet && !Sim) QueueFirst("gift", g);
            Inc(S.Totals, g);
            if (shiny && !o.Quiet && !Sim) ShinyFound?.Invoke(g);
            return shiny;
        }

        public void QueueFirst(string kind, string id)
        {
            if (S.Guided < 3 || FirstQ.Count >= 4) return;
            FirstQ.Add(new[] { kind, id });
            FirstMet?.Invoke(kind, id);
        }

        public string AffOf(string k) => T.Affinity.TryGetValue(k, out var a) ? a : T.AffinityDefault;

        /// <summary>giftPts: 기본 1점, 어울리는 곳 2배, 계절 선물 3배, 때 조건 선물 2배, 빛나는 선물 6배 (fit: "알맞은 곳"으로 보낸 것).</summary>
        public double GiftPts(string k, string track, bool shiny, bool fit = false)
        {
            T.GiftById.TryGetValue(k, out var g);
            double a = g != null && g.Season != null ? T.Bal.GiftPtsSeason : g != null && g.Cond != null ? T.Bal.GiftPtsCond : 1;
            return a * (fit || AffOf(k) == track ? T.Bal.GiftPtsFit : 1) * (shiny ? T.Bal.GiftPtsShiny : 1);
        }

        /// <summary>레벨이 가장 낮은 갈래 (같으면 점수가 적은 쪽).</summary>
        public string LowestTrack()
        {
            string a = T.Tracks[0].Id;
            for (int i = 1; i < T.Tracks.Count; i++)
            {
                string b = T.Tracks[i].Id;
                if (LvlOf(b) < LvlOf(a) || (LvlOf(b) == LvlOf(a) && Get(S.Growth, b) < Get(S.Growth, a))) a = b;
            }
            return a;
        }

        public string AutoTrack(string k) => T.GiftById.TryGetValue(k, out var g) && g.Season != null ? LowestTrack() : AffOf(k);
        public double SpareOf(string k) => Get(S.Gifts, k);
        public string OfferTarget() => T.TrackById.ContainsKey(S.OfferTo ?? "") ? S.OfferTo : "auto";

        /// <summary>선물을 바쳐 성장 점수로 바꾼다. 얻은 점수를 돌려준다.</summary>
        public double Offer(string k, string track, double n, bool shiny)
        {
            double have = shiny ? Get(S.Shiny, k) : SpareOf(k);
            n = Math.Min(n, have);
            if (n <= 0) return 0;
            if (shiny) S.Shiny[k] = Get(S.Shiny, k) - n; else S.Gifts[k] = Get(S.Gifts, k) - n;
            double pts = 0;
            bool isSeason = T.GiftById.TryGetValue(k, out var g) && g.Season != null;
            if (track == "auto" && isSeason)
            {
                for (int i = 0; i < n; i++) { string tr = AutoTrack(k); double p = GiftPts(k, tr, shiny, true); pts += p; BumpGrowth(tr, p); }
            }
            else
            {
                string tr = track == "auto" ? AffOf(k) : track;
                pts = GiftPts(k, tr, shiny) * n; BumpGrowth(tr, pts);
            }
            S.Offered += n;
            Persist();
            return pts;
        }

        /// <summary>자동 헌정: 종류마다 3개를 남기고 나머지를 바친다. 빛나는 선물도 바치되 종류마다 1개는 도감용으로 남긴다.</summary>
        public double AutoOfferAll()
        {
            if (!S.AutoOffer) return 0;
            double n = 0;
            foreach (var g in T.Gifts)
            {
                string k = g.Id;
                double spare = SpareOf(k) - T.Bal.AutoOfferKeep; if (spare > 0) { Offer(k, OfferTarget(), spare, false); n += spare; }
                double sh = Get(S.Shiny, k) - T.Bal.AutoOfferShinyKeep; if (sh > 0) { Offer(k, OfferTarget(), sh, true); n += sh; }
            }
            return n;
        }

        // ================= 편지함 =================
        /// <summary>편지를 맨 앞에 넣는다. 한도를 넘으면 오래된 일반 편지부터 지우고, 단골 사연(reg)·보존 표시(keep) 편지는 지우지 않는다.</summary>
        public Letter AddLetter(Letter l)
        {
            S.Letters.Insert(0, l);
            while (S.Letters.Count > T.Bal.LetterMax)
            {
                int i = -1;
                for (int j = S.Letters.Count - 1; j >= 0; j--) if (S.Letters[j].Reg == null && !S.Letters[j].KeepTruthy) { i = j; break; }
                if (i < 0) break;
                S.Letters.RemoveAt(i); S.LettersTrimmed = true;
            }
            LetterAdded?.Invoke(l);
            return l;
        }

        public int UnreadCount() => S.Letters.Count(l => !l.Read);

        // ================= 광고 하루 기록 (한도·날짜만; 광고 SDK 는 IAdAdapter) =================
        /// <summary>adsToday: 날짜가 앞으로 가면 조용히 채워지고, 기기 날짜가 과거로 가면 2일 이내일 때만 초기화하지 않는다 (D15·D20).</summary>
        public AdsState AdsToday()
        {
            var a = S.Ads;
            foreach (var k in T.AdKinds) if (!(a.Used.TryGetValue(k.Id, out double u) && u >= 0)) a.Used[k.Id] = 0;
            string t = TodayKey();
            bool bad = !(SaveMigrator.DayNum(a.Day) <= SaveMigrator.DayNum(t) + 2);
            if (string.CompareOrdinal(t, a.Day) > 0 || bad) { a.Day = t; foreach (var k in T.AdKinds) a.Used[k.Id] = 0; }
            return a;
        }

        public double AdLeft(string kind) => Math.Max(0, T.AdKinds.First(k => k.Id == kind).Limit - AdsToday().Used[kind]);
    }
}
