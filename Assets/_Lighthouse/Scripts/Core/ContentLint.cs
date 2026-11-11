using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lighthouse.Core
{
    /// <summary>
    /// 문구·표 정합 검사 (원본 FORBID_RE·DAY_BAN_RE·DIARY_BAN_RE·JOSA_LEFT_RE 와 checkContent·decorSelfCheck, HTML 5347~5418행·3554행).
    /// 에디터·시험 전용(출시 빌드에 정규식 검사를 넣지 않는다 — 설계서 3-3). 패턴은 lint/forbid_ko.json 에서 와서 JS 판과 같은 원문이다.
    /// 알려진 차이: .NET 의 \d 는 유니코드 숫자까지 잡으므로 [0-9] 로 바꿔 JS 와 맞췄다. SHIPS·SHIP_SPRITES(배 도면)의 존재 검사는
    /// 렌더 어셈블리(M2) 소관이라 shipSpriteKeys 를 넘길 때만 한다. 꾸미기 화면이 그려 내는 문구(decorTexts)는 UI 쪽 TextHarvester(M3)가
    /// extraTexts 로 넘기고, 데이터에서 바로 나오는 문구(장식 이름·잠금 조건·자리 이름·DECO_MSG)는 여기서 만든다.
    /// </summary>
    public sealed class ContentLint
    {
        readonly GameTables T;
        readonly Regex forbid, josaLeft, dayBan, diaryBan, natureBan;
        static readonly Regex NvKey = new Regex("^[a-z]{1,12}$", RegexOptions.CultureInvariant), SeasonKey = new Regex("^[0-9]{4}:[a-z]{1,10}$", RegexOptions.CultureInvariant);

        static Regex Make(string p) => new Regex(p.Replace("\\d", "[0-9]"), RegexOptions.CultureInvariant);

        public ContentLint(GameTables tables)
        {
            T = tables;
            forbid = Make(string.Join("|", T.Lint.Branches.Select(b => b.Value)));
            josaLeft = Make(T.Lint.JosaLeft); dayBan = Make(T.Lint.DayBan); diaryBan = Make(T.Lint.DiaryBan); natureBan = Make(T.Lint.NatureTextBan);
        }

        public bool Forbid(string t) => forbid.IsMatch(t);
        public bool JosaLeft(string t) => josaLeft.IsMatch(t);
        public bool DayBan(string t) => dayBan.IsMatch(t);
        public bool DiaryBan(string t) => diaryBan.IsMatch(t);

        /// <summary>어느 갈래가 걸렸는지(사람이 고칠 때 보는 이름). 걸리지 않으면 빈 목록.</summary>
        public List<string> ForbidBranches(string t)
            => T.Lint.Branches.Where(b => Make(b.Value).IsMatch(t)).Select(b => b.Key).ToList();

        // ---------- 꾸미기 문구(데이터에서 바로 나오는 것) ----------
        static string GaI(double n) { string s = JsNumStr(n); return "2459".Contains(s.Substring(s.Length - 1)) ? "가" : "이"; }
        static string JsNumStr(double n) => Js.NumToString(n);

        public string DecorNeedText(string id, bool isLong)
        {
            var it = T.DecorItemById[id]; string[] ORD = { "첫", "두", "세", "네", "다섯", "여섯" };
            if (it.Src == "earn") return isLong ? T.TrackById[it.NeedKey].Name + " Lv." + JsNumStr(it.NeedVal) + GaI(it.NeedVal) + " 되면 만날 수 있어요." : T.TrackById[it.NeedKey].Name + " Lv." + JsNumStr(it.NeedVal);
            if (it.Src == "story") return isLong ? it.Who + "의 " + ORD[(int)it.NeedVal - 1] + " 번째 이야기를 들으면 만날 수 있어요." : it.Who + "의 " + ORD[(int)it.NeedVal - 1] + " 번째 이야기";
            if (it.Src == "season") return isLong ? T.Lint.SeasonName[it.NeedKey] + "을 만나면 계속 가질 수 있어요." : T.Lint.SeasonName[it.NeedKey] + "에 만나요";
            if (it.Src == "gift") return isLong ? T.GiftById[it.NeedKey].Name + "{을/를} " + JsNumStr(it.NeedVal) + "개 받으면 만날 수 있어요." : T.GiftById[it.NeedKey].Name + " " + JsNumStr(it.NeedVal) + "개";
            return "";
        }

        public string SlotNeedText(DecorSlotDef sl, bool isLong)
            => sl.OpenLv != 0 ? (isLong ? "정원 Lv." + sl.OpenLv + GaI(sl.OpenLv) + " 되면 열려요." : "정원 Lv." + sl.OpenLv) : (isLong ? "섬이 " + sl.OpenStage + "단계가 되면 열려요." : "섬 " + sl.OpenStage + "단계");

        public string SlotLabel(DecorSlotDef sl) => T.DecorZones.First(z => z.Id == sl.Chip).Title + " " + sl.Num + "번 자리";

        /// <summary>데이터에서 바로 나오는 꾸미기 문구 모음(원본 decorTexts 의 데이터 부분).</summary>
        public List<string> DecorTexts()
        {
            var o = new List<string>();
            foreach (var it in T.DecorItems) { o.Add(it.Name); if (it.NeedStr != null) { o.Add(GameContext.FixJosa(DecorNeedText(it.Id, false))); o.Add(GameContext.FixJosa(DecorNeedText(it.Id, true))); } }
            foreach (var sl in T.DecorSlots) if (!sl.Blocked) { o.Add(SlotLabel(sl)); if (sl.OpenLv != 0 || sl.OpenStage != 0) { o.Add(SlotNeedText(sl, false)); o.Add(SlotNeedText(sl, true)); } }
            foreach (var m in T.Lint.DecoMsg.Values) o.Add(m);
            return o;
        }

        // ---------- 꾸미기 자체 점검 (원본 decorSelfCheck) ----------
        static double Lum(string hex) { int n = int.Parse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture); return 0.3 * (n >> 16) + 0.59 * ((n >> 8) & 255) + 0.11 * (n & 255); }

        /// <summary>계절별 글자 → 색 표(외형 치환 없음).</summary>
        public Dictionary<string, string> DecorColors(string season)
        {
            var c = new Dictionary<string, string>(T.DecorLetters);
            var pine = T.DecorSeason["pine"][season]; c["P"] = pine[0]; c["p"] = pine[1]; c["q"] = pine[2];
            var leaf = T.DecorSeason["leaf"][season]; c["L"] = leaf[0]; c["l"] = leaf[1]; c["m"] = leaf[2];
            if (season == "winter") { c["F"] = "#f4f8fc"; c["C"] = "#e8f0f8"; c["T"] = "#dfeaf2"; c["1"] = "#c8d4de"; c["2"] = "#e4ecf4"; c["M"] = "#e4ecf4"; }
            return c;
        }

        public List<string> DecorSelfCheck()
        {
            var bad = new List<string>();
            var cap = new Dictionary<string, int[]> { { "S", new[] { 10, 17 } }, { "M", new[] { 14, 17 } }, { "L", new[] { 18, 20 } } };
            foreach (var kv in T.DecorLetters) if (Lum(kv.Value) < 80 && kv.Key != "*") bad.Add("글자 " + kv.Key + " 휘도 " + Lum(kv.Value).ToString("F1", CultureInfo.InvariantCulture) + " < 80");
            foreach (var sn in GameTables.SeasonIds) foreach (var kv in DecorColors(sn)) if (Lum(kv.Value) < 80) bad.Add(sn + " 글자 " + kv.Key + "(" + kv.Value + ") 휘도 " + Lum(kv.Value).ToString("F1", CultureInfo.InvariantCulture) + " < 80");
            foreach (var it in T.DecorItems)
            {
                int w = it.Rows[0].Length, h = it.Rows.Length; bool flat = it.Zones[0] == "path";
                if (it.Rows.Any(r => r.Length != w)) bad.Add(it.Id + ": 행 길이가 다름");
                foreach (var r in it.Rows) foreach (char ch in r) if (ch != '.' && !T.DecorLetters.ContainsKey(ch.ToString())) bad.Add(it.Id + ": 글자표에 없는 글자 " + ch);
                if (!flat && (w > cap[it.Size][0] || h > cap[it.Size][1])) bad.Add(it.Id + ": 크기 상한(" + cap[it.Size][0] + "," + cap[it.Size][1] + ") 초과 " + w + "×" + h);
                if (it.Ax >= w || it.Ay > h) bad.Add(it.Id + ": 앵커가 격자 밖");
                if (string.IsNullOrEmpty(it.Name) || Forbid(it.Name)) bad.Add(it.Id + ": 이름 금지 표현");
            }
            if (T.DecorSlots.Count != T.Bal.DecorSlotCount) bad.Add("자리가 28개가 아님");
            int live = T.DecorSlots.Count(s => !s.Blocked);
            if (live != T.Bal.DecorOpenCount) bad.Add("사용 자리 " + live + " (20여야 함)");
            return bad;
        }

        // ---------- checkContent ----------
        /// <summary>표 정합·금지 표현 점검. 문제 목록(빈 목록 = 위반 0).</summary>
        public List<string> CheckContent(IEnumerable<string> shipSpriteKeys = null, IEnumerable<string> extraTexts = null)
        {
            var bad = new List<string>(); void Add(string m) => bad.Add(m);
            var regs = T.Regions.Select(r => r.Id).ToList(); var icons = T.Lint.Icons;
            foreach (var v in T.Sea)
            {
                string k = v.Id;
                if (!icons.ContainsKey(v.Icon)) Add("바다 친구 " + k + ": ICONS 에 아이콘 " + v.Icon + " 없음");
                if (string.IsNullOrEmpty(v.Name) || string.IsNullOrEmpty(v.Where) || string.IsNullOrEmpty(v.Hint)) Add("바다 친구 " + k + ": 이름·장소·안내 글이 비었음");
                if (SaveMigrator.SafeId(k) == null || !T.SeaGroups.Any(g => g.Id == v.Grp)) Add("바다 친구 " + k + ": id 형식 또는 구역(grp) 이상");
                foreach (var t in new[] { v.Name, v.Where, v.Hint, v.Tag ?? "" }) if (Forbid(t) || JosaLeft(t)) Add("바다 친구 " + k + ": 금지 표현 또는 조사 잔여 — " + t);
            }
            foreach (var e in T.Diary)
            {
                if (e.Nv != null && !T.SeaById.ContainsKey(e.Nv)) Add("일지 이름표 " + e.Nv + ": SEA 에 없음");
                else if (e.Nv != null && !NvKey.IsMatch(e.Nv)) Add("일지 이름표 " + e.Nv + ": diarySeen 키 형식(소문자 12자 이내)이 아님");
            }
            foreach (var L in T.NatureLetters)
            {
                if (!SeasonKey.IsMatch(L.Key)) Add("자연 편지 " + L.Key + ": seasonLetters 키 형식 아님");
                foreach (var t in new[] { L.Text, L.Nm, L.Sign }) if (Forbid(t) || natureBan.IsMatch(t) || t.Length > T.Bal.LetterLimText) Add("자연 편지 " + L.Key + ": 금지 표현·길이 — " + Js.Slice(t, 20));
            }
            var phr = T.SeaGroups.Select(g => g.Name).Concat(new[] { "새 하늘 기록, ", "드문 하늘 기록, ", "새 바다 친구, ", "드문 바다 친구, ", "자연 도장 ", "하늘·날씨 도장 절반", "하늘·날씨 도장 완성", "바다·섬 친구 도장 절반", "바다·섬 친구 도장 완성" });
            foreach (var t in phr) if (Forbid(t)) Add("자연 도감 문구: 금지 표현 — " + t);
            foreach (var g in T.Gifts)
            {
                string k = g.Id;
                if (!icons.ContainsKey(k)) Add("선물 " + k + ": ICONS 에 아이콘 없음");
                if (!T.Affinity.ContainsKey(k)) Add("선물 " + k + ": AFFINITY 에 없음");
                else if (!T.TrackById.ContainsKey(T.Affinity[k])) Add("선물 " + k + ": AFFINITY 갈래 " + T.Affinity[k] + " 가 TRACKS 에 없음");
                if (!regs.Contains("home")) Add("선물 " + k + ": 모르는 바다 home");
                if (g.Season != null && !T.Lint.SeasonName.ContainsKey(g.Season)) Add("선물 " + k + ": 모르는 계절 " + g.Season);
                if (g.Season == null && !(g.HasW && g.WFish >= 0 && g.WTrade >= 0)) Add("선물 " + k + ": w(fish·trade) 없음");
                if (g.Cond != null && !T.Bal.CondName.ContainsKey(g.Cond)) Add("선물 " + k + ": 모르는 조건 " + g.Cond);
                if (g.Tier != 0 && !(g.Tier >= 2 && g.Tier <= 3 && Array.IndexOf(T.TierOf, g.Tier) > 0)) Add("선물 " + k + ": tier " + g.Tier + " 이상");
                if ((g.Season != null ? 1 : 0) + (g.Cond != null ? 1 : 0) + (g.Tier != 0 ? 1 : 0) > 1) Add("선물 " + k + ": season·cond·tier 는 하나만");
                if (string.IsNullOrEmpty(GiftWhen(g)) && (g.Season != null || g.Cond != null || g.Tier != 0)) Add("선물 " + k + ": 도감 조건 문구가 비어 있음");
            }
            foreach (var k in T.Affinity.Keys) if (!T.GiftById.ContainsKey(k)) Add("AFFINITY " + k + ": GIFTS 에 없는 선물");
            foreach (var kv in icons)
            {
                var ic = kv.Value;
                if (!(ic.Rows.Length == 8 || ic.Rows.Length == 16) || ic.Rows.Any(r => r.Length != ic.Rows.Length)) Add("아이콘 " + kv.Key + ": 8×8 또는 16×16 이 아님");
                foreach (var r in ic.Rows) foreach (char ch in r) if (ch != '.' && !ic.PalKeys.Contains(ch.ToString())) Add("아이콘 " + kv.Key + ": 팔레트에 없는 글자 " + ch);
            }
            var shipKeys = shipSpriteKeys != null ? new HashSet<string>(shipSpriteKeys) : null;
            foreach (var s in T.Ships)
            {
                if (shipKeys != null && !shipKeys.Contains(s.Id)) { Add("배 " + s.Id + ": SHIPS 에 없음"); Add("배 " + s.Id + ": SHIP_SPRITES 에 없음"); }
                if (!regs.Contains("home")) Add("배 " + s.Id + ": 모르는 바다 home");
                if (!(s.Stage >= 1 && s.Stage <= T.MaxStage)) Add("배 " + s.Id + ": stage 이상");
            }
            var ids = new HashSet<string>();
            foreach (var r in T.Regulars)
            {
                if (ids.Contains(r.Id)) Add("단골 " + r.Id + ": id 중복"); ids.Add(r.Id);
                if (!T.GiftById.ContainsKey(r.Gift)) Add("단골 " + r.Id + ": gift " + r.Gift + " 가 GIFTS 에 없음");
                if (!T.ShipById.ContainsKey(r.Type)) Add("단골 " + r.Id + ": type " + r.Type + " 가 SHIP_INFO 에 없음");
                if (!regs.Contains(r.Region ?? "home")) Add("단골 " + r.Id + ": 모르는 바다 " + (r.Region ?? "home"));
                if (r.Story == null || r.Story.Length != T.Bal.VisitAt.Length) Add("단골 " + r.Id + ": story 가 " + T.Bal.VisitAt.Length + "편이 아님");
                for (int i = 0; i < (r.Story?.Length ?? 0); i++) if (string.IsNullOrEmpty(r.Story[i]) || Forbid(r.Story[i])) Add("단골 " + r.Id + ": " + (i + 1) + "번째 이야기에 금지 표현 또는 빈 글");
            }
            foreach (var m in T.Milestones) if (m.SkinLayer != null)
                {
                    SkinDef sd = null; if (T.SkinLayerById.TryGetValue(m.SkinLayer, out var layer)) layer.Skins.TryGetValue(m.SkinId, out sd);
                    if (sd == null) Add("이정표 " + m.K + " Lv." + m.Lv + ": 외형 " + m.SkinLayer + "." + m.SkinId + " 가 SKINS 에 없음");
                    else if (sd.Src != "earn" || sd.NeedKey != m.K || sd.NeedLv != m.Lv) Add("이정표 " + m.K + " Lv." + m.Lv + ": 외형 " + m.SkinLayer + "." + m.SkinId + " 가 earn·need 와 어긋남");
                }
            var ctx = new GameContext(T, new GameState(), new Mulberry32(1), new FixedClock(0, 0));
            var sets = new[] { new List<string>(), new List<string> { "swallow" }, new List<string> { "moon", "starroad" }, T.FinaleVoices.Select(v => v.Key).ToList() };
            if (sets.Any(m => { string t = ctx.FinaleText(m); return Forbid(t) || natureBan.IsMatch(t); })) Add("완결 편지: 금지 표현");
            foreach (var t in DecorTexts().Concat(extraTexts ?? new string[0])) if (Forbid(t) || JosaLeft(t)) Add("꾸미기 문구: 금지 표현 또는 조사 잔여 — " + t);
            foreach (var m in DecorSelfCheck()) Add("꾸미기 데이터: " + m);
            return bad;
        }

        /// <summary>도감의 "{조건}에 만나요" 글.</summary>
        public string GiftWhen(GiftDef g)
            => g.Season != null ? (T.Lint.SeasonName.TryGetValue(g.Season, out var sn) ? sn : "") : g.Cond != null ? (T.Bal.CondName.TryGetValue(g.Cond, out var cn) ? cn : "") : g.Tier != 0 ? Array.IndexOf(T.TierOf, g.Tier) + "단계 이후" : "";
    }
}
