using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lighthouse.Core
{
    /// <summary>방치 일지 한 줄.</summary>
    public sealed class DiaryEntry
    {
        public string At; public string[] Parts; public string Text; public string Gift; public bool Shiny;
        public Newtonsoft.Json.Linq.JObject ToJson() => new Newtonsoft.Json.Linq.JObject
        {
            ["at"] = At, ["parts"] = new Newtonsoft.Json.Linq.JArray(Parts), ["text"] = Text,
            ["gift"] = Gift != null ? (Newtonsoft.Json.Linq.JToken)new Newtonsoft.Json.Linq.JValue(Gift) : Newtonsoft.Json.Linq.JValue.CreateNull(), ["shiny"] = Shiny
        };
    }

    /// <summary>편지 조각 조합의 입력(원본 letterParts/makeLetter 의 o 인자). 비워 두면 지금 상태를 쓴다.</summary>
    public sealed class LetterOpts { public bool? Night; public string Season; public IRng Rnd; public Dictionary<string, List<string>> St; }

    public sealed class DiaryOpts
    {
        public IRng Rnd; public long? Now; public int? Tier; public string Season; public bool? Week; public Dictionary<string, double> Seen; public bool NoGift;
    }

    public sealed partial class GameContext
    {
        // ================= 한국어 조사 (원본 fixJosa 2767행) =================
        static readonly Regex JosaRe = new Regex(@"([가-힣]+)((?:</[a-z]+>)*)(을\(를\)|이\(가\)|은\(는\)|와\(과\)|\{을/를\}|\{이/가\}|\{은/는\}|\{과/와\})", RegexOptions.CultureInvariant);

        /// <summary>"진주을(를)"·"진주{을/를}" → "진주를": 받침 유무로 조사를 고른다. 이름을 &lt;b&gt;…&lt;/b&gt; 로 감싼 경우도 닫는 태그를 건너뛰고 고친다.</summary>
        public static string FixJosa(string str)
            => JosaRe.Replace(str ?? "", m =>
            {
                string w = m.Groups[1].Value, tags = m.Groups[2].Value, j = m.Groups[3].Value;
                bool fin = Progress.HasFinal(w[w.Length - 1]);
                char c = j[0] == '{' ? j[1] : j[0];
                return w + tags + (c == '을' ? (fin ? "을" : "를") : c == '이' ? (fin ? "이" : "가") : c == '은' ? (fin ? "은" : "는") : (fin ? "과" : "와"));
            });

        static string ReplaceFirst(string s, string find, string with) { int i = s.IndexOf(find, StringComparison.Ordinal); return i < 0 ? s : s.Substring(0, i) + with + s.Substring(i + find.Length); }

        // ================= 절차적 편지 =================
        string SeaOpen() => T.Texts.SeaOpen.TryGetValue(CurRegion(), out var s) && !string.IsNullOrEmpty(s) ? s : T.Texts.SeaOpen["home"];

        /// <summary>letterParts: 편지 한 통의 문장 조각들(자리표시 {g}·{sea_open} 가 아직 안 풀린 상태). 같은 조각이 연속 3통 안에 다시 나오지 않게 한다.</summary>
        public List<string> LetterParts(LetterOpts o = null)
        {
            o = o ?? new LetterOpts();
            bool night = o.Night ?? Night; string season = !string.IsNullOrEmpty(o.Season) ? o.Season : CurSeason();
            IRng rnd = o.Rnd ?? Rng; var st = o.St ?? LetterRecent;
            string Pk(string slot, string[] arr)
            {
                if (!st.TryGetValue(slot, out var rc)) { rc = new List<string>(); st[slot] = rc; }
                var c = arr.Where(s => !rc.Contains(s)).ToList();
                string s0 = (c.Count > 0 ? c : arr.ToList())[(int)Math.Floor(rnd.NextDouble() * (c.Count > 0 ? c.Count : arr.Length))];
                rc.Add(s0); while (rc.Count > Math.Min(3, arr.Length - 1)) rc.RemoveAt(0);
                return s0;
            }
            var sa = night ? T.Texts.SeasonLetterNight[season] : T.Texts.SeasonLetterDay[season];
            if (!st.TryGetValue("season", out var sr)) { sr = new List<string>(); st["season"] = sr; }
            var sc = sa.Where(s => !sr.Contains(s)).ToList();
            string sp = (rnd.NextDouble() < 0.5 && sc.Count > 0) ? sc[(int)Math.Floor(rnd.NextDouble() * sc.Count)] : null;
            sr.Add(sp); while (sr.Count > 3) sr.RemoveAt(0);
            var parts = new List<string> { Pk("open", T.Texts.Open), night ? Pk("night", T.Texts.Night) : Pk("day", T.Texts.Day) };
            if (sp != null) parts.Add(sp);
            parts.Add(Pk("mid", T.Texts.Mid)); parts.Add(Pk("thanks", T.Texts.Thanks)); parts.Add(Pk("gift", T.Texts.Gift)); parts.Add(Pk("close", T.Texts.Close));
            return parts;
        }

        bool RegNameClash(string ship, string cap)
            => T.Regulars.Any(r => r.Ship == ship || r.Cap == cap || ship.Contains(r.Ship) || cap.Contains(r.Cap))
               || T.SeasonGuests.Any(g => g.Ship == ship || g.Cap == cap) || T.Anniv.Any(g => g.Ship == ship || g.Cap == cap);

        /// <summary>fillLetter: 자리표시를 풀고 조사를 맞춘다.</summary>
        public string FillLetter(List<string> parts, string gift, bool shiny)
        {
            string nm = (shiny ? "빛나는 " : "") + (gift != null && T.GiftById.TryGetValue(gift, out var g) ? g.Name : "선물");
            return FixJosa(string.Join(" ", parts).Replace("{sea_open}", SeaOpen()).Replace("{g}", nm));
        }

        /// <summary>makeLetter: 이름이 단골과 겹치지 않는 배·선장 + 조각 조합. 편지 번호를 하나 쓴다.</summary>
        public Letter MakeLetter(string gift, bool shiny, LetterOpts o = null)
        {
            o = o ?? new LetterOpts();
            IRng rnd = o.Rnd ?? Rng;
            string ship, cap; int n = 0;
            do
            {
                ship = T.Texts.ShipNames[(int)Math.Floor(rnd.NextDouble() * T.Texts.ShipNames.Length)];
                cap = T.Texts.Captains[(int)Math.Floor(rnd.NextDouble() * T.Texts.Captains.Length)];
            } while (RegNameClash(ship, cap) && ++n < 8);
            var l = new Letter { Id = S.LetterId++, Ship = ship, Cap = cap, Gift = gift, T = Clock.NowMs, Read = false, Region = CurRegion(), Shiny = shiny ? (bool?)true : null };
            l.Text = FillLetter(LetterParts(o), gift, shiny);
            l.Sign = ship + "호 " + cap + " 드림";
            return l;
        }

        // ================= 방치 일지 =================
        /// <summary>whenParts: [날, 때] — 오늘/어제/N월 N일 + 자정·새벽·오전·낮·오후 N시쯤.</summary>
        public string[] WhenParts(long ms)
        {
            var d = LocalTime.Of(ms, Clock); var now = LocalTime.Of(Clock.NowMs, Clock);
            double day = JsMath.Round(SaveMigrator.DayNum(now.DayKey) - SaveMigrator.DayNum(d.DayKey));
            int h = d.Hour;
            return new[] { day <= 0 ? "오늘" : day == 1 ? "어제" : d.Month + "월 " + d.Day + "일", h == 0 ? "자정쯤" : h < 6 ? "새벽 " + h + "시쯤" : h < 12 ? "오전 " + h + "시쯤" : h == 12 ? "낮 12시쯤" : "오후 " + (h - 12) + "시쯤" };
        }

        public string FmtWhen(long ms) => string.Join(" ", WhenParts(ms)).Trim();

        double WPickTotal(List<DiaryDef> pool) { double a = 0; foreach (var e in pool) a += e.W; return a; }

        /// <summary>makeDiary: 비운 시간 동안 있었던 일을 시간 순으로 늘어놓는다. 같은 사건은 한 일지에 한 번만(후보가 바닥나면 그 시각은 건너뛴다).</summary>
        public List<DiaryEntry> MakeDiary(double away, DiaryOpts o = null)
        {
            o = o ?? new DiaryOpts();
            IRng rnd = o.Rnd ?? Rng; long now = o.Now ?? Clock.NowMs; int tr = o.Tier ?? Tier(); string season = !string.IsNullOrEmpty(o.Season) ? o.Season : CurSeason();
            int n = (int)Math.Max(1, Math.Min(T.Bal.DiaryMax, JsMath.Round(away / 3600 * T.Bal.DiaryPerHour)));
            var frRaw = new double[n]; for (int i = 0; i < n; i++) frRaw[i] = rnd.NextDouble();
            var fr = Js.SortAsStrings(frRaw);
            var outList = new List<DiaryEntry>(); var used = new HashSet<DiaryDef>(); var usedText = new HashSet<string>();
            bool week = o.Week ?? (DaysSinceStart() < 7);
            var seenNv = o.Seen ?? S.DiarySeen;
            foreach (double f in fr)
            {
                long ms = (long)Math.Truncate(now - away * 1000 * (1 - f));
                var d = LocalTime.Of(ms, Clock); int hr = d.Hour; bool night = hr >= 19 || hr < 6;
                var all = T.Diary.Where(e => (e.Stage != 0 ? e.Stage : 1) <= tr && !(e.NoWinter && season == "winter") && (e.Any || (night ? e.Night : e.Day))).ToList();
                var fresh = all.Where(e => !used.Contains(e) && (e.Season || !usedText.Contains(e.Text))).ToList();
                var unseen = week ? fresh.Where(e => e.Nv != null && !(seenNv.TryGetValue(e.Nv, out double sv) && sv != 0)).ToList() : new List<DiaryDef>();
                var pool = unseen.Count > 0 ? unseen : fresh;
                if (pool.Count == 0) continue;
                double r = rnd.NextDouble() * WPickTotal(pool); var e0 = pool[pool.Count - 1];
                foreach (var c in pool) { r -= c.W; if (r <= 0) { e0 = c; break; } }
                if (e0.Season)
                {
                    var arr0 = night ? T.Texts.SeasonDiaryNight[season] : T.Texts.SeasonDiaryDay[season];
                    if (!arr0.Any(s => !usedText.Contains(s))) { used.Add(e0); continue; }
                }
                used.Add(e0);
                if (e0.Nv != null && T.SeaById.ContainsKey(e0.Nv) && (o.Rnd == null || o.Seen != null)) SeeSea(e0.Nv, false, true);
                if (e0.Nv != null && (o.Rnd == null || o.Seen != null)) seenNv[e0.Nv] = 1;
                string gift = null; bool shiny = false;
                if (e0.Gift || e0.Shiny)
                {
                    if (!o.NoGift) { var sc = SampleCond(); sc.Night = night; CondOv = sc; }
                    gift = o.NoGift ? T.Gifts[0].Id : RollGift("trade");
                    CondOv = null;
                    shiny = o.NoGift ? e0.Shiny : AddGift(gift, new AddGiftOpts { Quiet = true, Shiny = e0.Shiny ? (bool?)true : null });
                }
                string nm = gift != null ? T.GiftById[gift].Name : "";
                string text;
                if (e0.Season)
                {
                    var arr = night ? T.Texts.SeasonDiaryNight[season] : T.Texts.SeasonDiaryDay[season];
                    var fa = arr.Where(s => !usedText.Contains(s)).ToArray(); var pl = fa.Length > 0 ? fa : arr;
                    text = pl[(int)Math.Floor(rnd.NextDouble() * pl.Length)]; usedText.Add(text);
                }
                else { text = FixJosa(ReplaceFirst(e0.Text, "{g}", shiny ? "빛나는 " + nm : nm)); usedText.Add(e0.Text); }
                outList.Add(new DiaryEntry { At = FmtWhen(ms), Parts = WhenParts(ms), Text = text, Gift = gift, Shiny = shiny });
            }
            return outList;
        }

        // ================= 바다 친구 도감(자연 도감) =================
        double SeaN(string k) { double n = Get(S.Sea, k); return !double.IsNaN(n) && !double.IsInfinity(n) && n > 0 ? Math.Floor(n) : 0; }

        /// <summary>seaGrpCount: [만난 수, 전체 수].</summary>
        public int[] SeaGrpCount(string g)
        {
            var ks = T.Sea.Where(s => s.Grp == g).ToList();
            return new[] { ks.Count(s => SeaN(s.Id) != 0), ks.Count };
        }

        /// <summary>seeSea: 바다 친구를 만났다. bump: 만난 횟수를 센다(돌고래·갈매기·별똥별). anyTime: 일지처럼 타이틀이 떠 있어도 기록.</summary>
        public void SeeSea(string id, bool bump = false, bool anyTime = false)
        {
            if (!T.SeaById.ContainsKey(id) || Sim) return;
            if (SeaN(id) != 0 && !bump) return;
            if (!anyTime && TitleOpen) return;
            bool first = SeaN(id) == 0;
            S.Sea[id] = Math.Min(1e9, SeaN(id) + 1); Persist();
            if (first) { QueueFirst("sea", id); NatureLetters(); }
        }

        bool NatureNeed(NatureLetterDef L)
        {
            if (L.Need == "sky") { var c = SeaGrpCount("sky"); return c[0] >= c[1]; }
            if (L.Need == "sea") { var c = SeaGrpCount("sea"); return c[0] >= c[1]; }
            return T.Sea.All(s => SeaN(s.Id) != 0);
        }

        /// <summary>구역 완성 관찰 편지(하늘·바다·자연 전부). 한 번씩만. 도착한 통수를 돌려준다.</summary>
        public int NatureLetters()
        {
            if (Sim) return 0;
            int n = 0;
            foreach (var L in T.NatureLetters)
            {
                if ((S.SeasonLetters.TryGetValue(L.Key, out double dv) && dv != 0) || !NatureNeed(L)) continue;
                S.SeasonLetters[L.Key] = 1; n++;
                AddLetter(new Letter { Id = S.LetterId++, Ship = "섬", Cap = "관리인", Nm = L.Nm, Gift = null, T = Clock.NowMs, Read = false, Region = "home", Keep = new Newtonsoft.Json.Linq.JValue("nature"), Text = L.Text, Sign = L.Sign });
            }
            if (n > 0) { Persist(); if (!FirstQ.Any(q => q[0] == "letter")) QueueFirst("letter", "nature"); }
            return n;
        }

        /// <summary>도감 머리말 도장: [구역 이름, 표시, 설명] 목록.</summary>
        public List<string[]> NatureStamps()
        {
            var r = new List<string[]>();
            foreach (var g in T.SeaGroups)
            {
                var c = SeaGrpCount(g.Id); int a = c[0], b = c[1];
                if (a >= b) r.Add(new[] { g.Name, "✦✦", g.Name + " 도장 완성" });
                else if (T.StampHalf.TryGetValue(g.Id, out bool half) && half && a * 2 >= b) r.Add(new[] { g.Name, "✦", g.Name + " 도장 절반" });
            }
            return r;
        }

        // ================= 단골 손님 =================
        public RegularDef RegById(string id) => T.RegularById.TryGetValue(id, out var r) ? r : null;

        public double RegVisits(string id) => S.Reg.TryGetValue(id, out var r) ? r.V : 0;

        /// <summary>pickRegular: 가끔 이름과 사연이 있는 단골이 온다. 가중치 = 아직 못 들은 이야기 수 + 1.</summary>
        public RegularDef PickRegular()
        {
            if (Rand() > T.Bal.RegularBase + RegularBonus()) return null;
            var c = T.Regulars.Where(r => (r.Region ?? "home") == CurRegion() && Tier() >= r.Stage && !RegularOnSea(r.Id)).ToList();
            if (c.Count == 0) return null;
            double Wt(RegularDef r) { double v = RegVisits(r.Id); return 1 + T.Bal.VisitAt.Count(n => n > v); }
            double sum = 0; foreach (var r in c) sum += Wt(r);
            double x = Rand() * sum;
            foreach (var r in c) { x -= Wt(r); if (x < 0) return r; }
            return c[c.Count - 1];
        }

        /// <summary>visitRegular: 방문이 쌓이면 이야기 편지와 선물이 도착한다. quiet 는 알림만 줄인다(저장은 부른 쪽이). 편지를 돌려준다.</summary>
        public Letter VisitRegular(string regId, bool quiet)
        {
            var r = RegById(regId);
            if (r == null) return null;
            if (!S.Reg.TryGetValue(r.Id, out var rec)) { rec = new RegRecord { V = 0 }; S.Reg[r.Id] = rec; }
            rec.V++;
            int idx = Array.IndexOf(T.Bal.VisitAt, (int)rec.V);
            if (rec.V != Math.Floor(rec.V)) idx = -1;
            Letter letter = null;
            if (idx >= 0)
            {
                string g = rec.V == T.Bal.GiftAt ? r.Gift : RollGift(r.Type);
                AddGift(g, new AddGiftOpts { NoShiny = true });
                letter = AddLetter(new Letter { Id = S.LetterId++, Ship = r.Ship, Cap = r.Cap, Gift = g, T = Clock.NowMs, Read = false, Reg = r.Id, Region = r.Region ?? "home", Text = r.Story[idx], Sign = r.Ship + "호 " + r.Cap + " 드림" });
            }
            else if (rec.V == Math.Floor(rec.V) && rec.V > T.Bal.VisitAt[T.Bal.VisitAt.Length - 1] && (rec.V - T.Bal.VisitAt[T.Bal.VisitAt.Length - 1]) % GreetEvery == 0)
                letter = GreetingLetter(r);
            if (!quiet) Persist();
            return letter;
        }

        /// <summary>이야기를 다 들은 단골이 이 번째 방문마다 안부 편지를 보낸다(마지막 이야기 이후 10·20·30…번째). 일반 편지로 쌓이고(남겨 두지 않음) 오래되면 지워진다.</summary>
        public const int GreetEvery = 10;

        Letter GreetingLetter(RegularDef r)
        {
            string g = RollGift(r.Type);
            AddGift(g, new AddGiftOpts { NoShiny = true });
            var parts = LetterParts();
            string line = r.Lines != null && r.Lines.Length > 0 ? " " + r.Lines[(int)Math.Floor(Rng.NextDouble() * r.Lines.Length)] : "";
            parts.Insert(1, r.Ship + "호 " + r.Cap + "입니다." + line);
            return AddLetter(new Letter { Id = S.LetterId++, Ship = r.Ship, Cap = r.Cap, Gift = g, T = Clock.NowMs, Read = false, Region = r.Region ?? "home", Text = FillLetter(parts, g, false), Sign = r.Ship + "호 " + r.Cap + " 드림" });
        }

        /// <summary>한 번이라도 만난 단골.</summary>
        public List<string> MetRegulars() => T.Regulars.Where(r => RegVisits(r.Id) > 0).Select(r => r.Id).ToList();

        // ================= 희귀 손님 (Unity 전용 U20) =================
        /// <summary>조건이 맞는 배가 새로 올 때 이 확률로 희귀 손님이 끼어든다. 이미 만난 손님은 훨씬 드물게 다시 온다.</summary>
        public const double RareChance = 0.05, RareRepeatChance = 0.015;

        /// <summary>만난 적 있나: 첫 만남에 남겨 둔 편지(keep = "rare", 보낸 배 이름이 손님)가 곧 기록이다(저장 구조를 늘리지 않는다. keep 종류는 balance.json keepKinds).</summary>
        public bool RareMet(string id)
        {
            if (!T.RareById.TryGetValue(id, out var d)) return false;
            return S.Letters.Any(l => l.Ship == d.Ship && l.Keep != null && l.Keep.Type == Newtonsoft.Json.Linq.JTokenType.String && (string)l.Keep == "rare");
        }

        public bool RareCondOk(string cond)
        {
            foreach (var c in (cond ?? "").Split('+'))
            {
                bool ok = c == "night" ? Night : c == "rain" ? Wx == "rain" : c == "fog" ? Wx == "fog" : c == CurSeason();
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>pickRare: 지금 조건·등급에 맞고 바다에 없는 희귀 손님 중 하나를 확률로 고른다. 규칙 난수를 쓰지 않아 원본 동등 기록을 건드리지 않는다.</summary>
        public RareGuestDef PickRareGuest(Random rnd, Func<string, bool> onSea = null)
        {
            if (Sim || T.RareGuests.Count == 0) return null;
            var c = T.RareGuests.Where(r => Tier() >= r.Stage && RareCondOk(r.Cond) && !(onSea != null && onSea(r.Id))).ToList();
            if (c.Count == 0) return null;
            var d = c[(int)Math.Floor(rnd.NextDouble() * c.Count)];
            return rnd.NextDouble() < (RareMet(d.Id) ? RareRepeatChance : RareChance) ? d : null;
        }

        /// <summary>meetRare: 첫 만남은 남겨 두는 편지 + 빛나는 선물, 다시 만나면 평범한 선물 하나. 첫 만남이면 편지를 돌려준다.</summary>
        public Letter MeetRare(string id, out string gift)
        {
            gift = null; if (!T.RareById.TryGetValue(id, out var d)) return null;
            if (RareMet(id)) { gift = RollGift("trade"); AddGift(gift, new AddGiftOpts { NoShiny = true }); Persist(); return null; }
            gift = d.Gift ?? RollGift("trade");
            AddGift(gift, new AddGiftOpts { Shiny = true });
            var l = AddLetter(new Letter { Id = S.LetterId++, Ship = d.Ship, Cap = d.Cap, Gift = gift, T = Clock.NowMs, Read = false, Region = "home", Shiny = true, Keep = new Newtonsoft.Json.Linq.JValue("rare"), Text = d.Text, Sign = d.Ship + "호 " + d.Cap + " 드림" });
            Persist(); return l;
        }

        // ================= 계절 손님·기념일·완결 편지 =================
        Letter MkGuestLetter(string ship, string cap, string text, bool isSeason, long now, string keep, string gift, bool? shiny)
            => AddLetter(new Letter
            {
                Id = S.LetterId++, Ship = ship, Cap = cap, Gift = gift, T = now, Read = false, Region = "home", Text = text,
                Sign = isSeason ? ship + "호 " + cap + " 드림" : ship + " 드림", Nm = isSeason ? null : ship,
                Keep = new Newtonsoft.Json.Linq.JValue(keep), Shiny = shiny
            });

        /// <summary>deliverGuests: 계절 손님·기념일 편지를 한 번씩 배달한다. 배달한 통수를 돌려준다.</summary>
        public int DeliverGuests(bool quiet)
        {
            int count = 0; long now = Clock.NowMs;
            var sg = T.SeasonGuests.FirstOrDefault(g => g.Season == CurSeason()); string key = SeasonKey();
            if (sg != null && S.Guided >= T.Bal.SeasonGuestMinGuided && !(S.SeasonLetters.TryGetValue(key, out double sv) && sv != 0))
            {
                S.SeasonLetters[key] = 1; MkGuestLetter(sg.Ship, sg.Cap, sg.Text, true, now, "season", null, null); count++;
            }
            double days = Math.Floor((now - S.StartAt) / T.Bal.AnnivDayMs);
            foreach (var a in T.Anniv)
            {
                if (days < a.Day || (S.Anniv.TryGetValue(a.Day.ToString(), out double av) && av != 0)) continue;
                S.Anniv[a.Day.ToString()] = 1;
                string gift = null; bool shiny = false;
                if (a.Gift != null)
                {
                    gift = RollGift("trade");
                    shiny = AddGift(gift, new AddGiftOpts { Quiet = true, Shiny = a.Gift == "shiny" ? (bool?)true : null, NoShiny = a.Gift == "normal" });
                }
                MkGuestLetter(a.Ship, a.Cap, a.Text, false, now, "anniv", gift, shiny); count++;
            }
            if (count > 0) { Persist(); if (!quiet) LettersArrived?.Invoke(count); }
            return count;
        }

        /// <summary>6단계 완결 편지: 6단계에 이른 날 단 한 번. 도착했으면 true.</summary>
        public bool DeliverFinale(bool quiet)
        {
            if (Sim || S.Finale > 0 || Stage < T.MaxStage) return false;
            S.Finale = Clock.NowMs;
            AddLetter(new Letter { Id = S.LetterId++, Ship = "바다의 벗들", Cap = "단골 일동", Nm = T.Finale.Nm, Gift = null, T = S.Finale, Read = false, Region = "home", Keep = new Newtonsoft.Json.Linq.JValue("finale"), Text = FinaleText(MetRegulars()).Trim(), Sign = T.Finale.Sign });
            Persist();
            if (!quiet) LettersArrived?.Invoke(1);
            return true;
        }

        /// <summary>FINALE_LETTER.make(met): 만난 단골의 한마디만 넣고, 나머지는 이름 없는 배들의 말로 둔다.</summary>
        public string FinaleText(List<string> met)
        {
            var own = T.FinaleVoices.Where(v => met.Contains(v.Key)).ToList();
            string ownText = string.Join(" ", own.Select(v => v.Value));
            return T.Finale.Head + (own.Count > 0 ? ownText + (own.Count < T.Finale.SomeUntil ? T.Finale.SomeSuffix : "") : T.Finale.None) + T.Finale.Tail;
        }
    }
}
