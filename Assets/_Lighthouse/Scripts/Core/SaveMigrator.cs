using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Core
{
    /// <summary>
    /// 저장 마이그레이션(원본 MIGRATIONS 2~10 + migrate + cleanLetters/cleanIsle/cleanChoices, HTML 1491~1617행).
    /// 필드 이름·구조를 바꾸지 않고 JObject 위에서 JS 의 암묵 규칙(형 불일치→기본값, 진리값, typeof)까지 그대로 옮겼다.
    /// 번호 9 는 의도적으로 결번(원본 1490행). 더 새 빌드가 만든 저장은 번호를 낮추지 않는다.
    /// 알려진 차이(의도): totals 가 null 이면 JS 는 TypeError 로 불러오기를 포기하지만 여기서는 기본값으로 바꾼다.
    /// </summary>
    public sealed class SaveMigrator
    {
        readonly GameTables T; readonly IClock clock;
        public SaveMigrator(GameTables tables, IClock clock) { T = tables; this.clock = clock; }

        /// <summary>SAVE_DEFAULTS() — 새 JObject 사본.</summary>
        public JObject Defaults() => (JObject)T.SaveDefaultsJson.DeepClone();

        // ---------- 날짜 도우미 (원본 1485~1487) ----------
        public string TodayKey() => LocalTime.Of(clock.NowMs, clock).DayKey;
        public string DayKeyOf(double ts) => LocalTime.Of((long)ts, clock).DayKey;
        /// <summary>dayNum: 'YYYY-MM-DD' 의 일 번호(UTC 자정 기준 일 수). 형식이 틀리면 NaN.</summary>
        public static double DayNum(JToken k)
        {
            string s = Js.Str(k);
            return s == null ? double.NaN : DayNum(s);
        }
        public static double DayNum(string s)
        {
            if (s == null || s.Length != 10 || s[4] != '-' || s[7] != '-') return double.NaN;
            for (int i = 0; i < 10; i++) if (i != 4 && i != 7 && !(s[i] >= '0' && s[i] <= '9')) return double.NaN;
            int y = int.Parse(s.Substring(0, 4)), m = int.Parse(s.Substring(5, 2)), d = int.Parse(s.Substring(8, 2));
            // JS Date.UTC(y, m-1, d): 월·일이 범위를 넘으면 넘겨 계산한다(2026-02-30 → 3월 2일). 같은 규칙으로 일 수를 센다
            double days = DaysFromCivil(y, 1, 1);
            int mo = m - 1; int yy = y + (int)Math.Floor(mo / 12.0); mo = ((mo % 12) + 12) % 12;
            return DaysFromCivil(yy, mo + 1, 1) + (d - 1);
        }
        static double DaysFromCivil(int y, int m, int d)
        {
            // 1970-01-01 기준 일 수 (Howard Hinnant 알고리즘)
            y -= m <= 2 ? 1 : 0;
            long era = (y >= 0 ? y : y - 399) / 400;
            long yoe = y - era * 400;
            long doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
            long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468;
        }
        /// <summary>adsDayBad: 형식이 틀리거나(NaN) 오늘보다 2일 넘게 미래면 true (D20). 부정 구조를 그대로 둔다.</summary>
        public bool AdsDayBad(JToken day) => !(DayNum(day) <= DayNum(TodayKey()) + 2);

        // ---------- migrate ----------
        public JObject Migrate(JToken d)
        {
            JObject s = d is JObject jo ? jo : new JObject();
            double v;
            if (Js.IsInteger(s["ver"])) v = Js.Num(s["ver"]);
            else if (Js.IsNumber(s["stageV"]) && Js.Num(s["stageV"]) == 2) v = 2;
            else v = s.Count > 0 ? 1 : T.SaveVer;
            for (; v < T.SaveVer; v++) Step(s, (long)v + 1);
            var def = Defaults();
            foreach (var p in def.Properties())
            {
                JToken a = p.Value, b = s[p.Name];
                bool replace = b == null
                    || (a.Type != JTokenType.Null && (Js.TypeOf(a) != Js.TypeOf(b) || (a.Type == JTokenType.Array) != (b.Type == JTokenType.Array)))
                    || (a.Type == JTokenType.Null && b.Type != JTokenType.Null && Js.TypeOf(b) != "object");
                if (replace) s[p.Name] = a.DeepClone();
            }
            // totals 안쪽 칸도 형태가 어긋나면 비운다 (null 이면 JS 는 예외 → 여기서는 기본값)
            if (!(s["totals"] is JObject totals)) { totals = new JObject(); s["totals"] = totals; }
            if (!(totals["gifts"] is JObject)) totals["gifts"] = new JObject();
            foreach (var k in new[] { "startAt", "lastSeen", "finale" })
            {
                double x = Js.ToNumber(s[k]);
                if (!(x >= 0 && x <= T.Bal.MaxT)) s[k] = new JValue(0L);
            }
            if (Js.Truthy(s["build"]))
            {
                JToken b = s["build"];
                double st = Js.ToNumber(Js.Get(b, "startAt")), en = Js.ToNumber(Js.Get(b, "endAt"));
                if (!(st >= 0 && st <= T.Bal.MaxT && en >= 0 && en <= T.Bal.MaxT)) s["build"] = JValue.CreateNull();
            }
            s["letters"] = CleanLetters(s["letters"], s);
            if (s["ads"] is JObject ads && Js.Truthy(ads))
            {
                JToken day = ads["day"];
                bool dayIsEmpty = day != null && day.Type == JTokenType.String && (string)day == "";
                if (!dayIsEmpty && AdsDayBad(day))
                {
                    ads["day"] = TodayKey();
                    ads["used"] = new JObject { ["home"] = 0, ["star"] = 0, ["ship"] = 0 };
                }
            }
            if (!(Js.Num(s["ver"]) > T.SaveVer)) s["ver"] = T.SaveVer;
            CleanChoices(s);
            if (s["gifts"] is JObject gifts) foreach (var p in gifts.Properties().ToList()) { if (!(s["found"] is JObject)) s["found"] = new JObject(); ((JObject)s["found"])[p.Name] = 1; }
            return s;
        }

        // 한 단계 변환 (원본 1493~1532)
        void Step(JObject s, long to)
        {
            switch (to)
            {
                case 2:
                    {
                        // 단계를 3개에서 6개로 늘리기 전의 저장: 옛 1·2·3단계는 새 1·3·5단계
                        s["stageV"] = 2;
                        s["stage"] = Pick4(Js.Truthy(s["stage"]) ? s["stage"] : new JValue(1L));
                        if (Js.Truthy(s["build"]) && s["build"] is JObject b)
                        {
                            long pk = Pick4Or0(b["to"]);
                            b["to"] = pk != 0 ? pk : Js.Num(s["stage"]) + 1;
                        }
                        break;
                    }
                case 3: break;
                case 4:
                    {
                        double g = Js.ToNumber(s["guided"]); if (double.IsNaN(g)) g = 0;
                        var t = new JObject();
                        if (g > 0)
                        {
                            t["h1"] = 1; t["h2"] = 1; t["h4"] = g >= 16 ? 1 : 0;
                            JToken st = Js.Truthy(s["stage"]) ? s["stage"] : new JValue(1L);
                            t["h5"] = Js.ToNumber(st) >= 2 ? 1 : 0;
                            s["firstBack"] = true;
                        }
                        if (s["letters"] is JArray la && la.Count > 0) t["h3"] = 1;
                        foreach (var p in t.Properties().ToList()) if (!Js.Truthy(p.Value)) p.Remove();
                        s["tips"] = t; break;
                    }
                case 5: s["ads"] = new JObject { ["day"] = "", ["used"] = new JObject { ["home"] = 0, ["star"] = 0, ["ship"] = 0 }, ["homeDay"] = "" }; break;
                case 6:
                    {
                        var ts = new List<double>();
                        if (s["letters"] is JArray la)
                            foreach (var l in la)
                            {
                                JToken tt = l is JObject lo ? lo["t"] : null;     // l && l.t (객체가 아니면 undefined)
                                if (Js.IsFinite(tt) && Js.Num(tt) > 0) ts.Add(Js.Num(tt));
                            }
                        s["startAt"] = ts.Count > 0 ? Js.N(ts.Min()) : new JValue(0L); s["anniv"] = new JObject(); s["seasonLetters"] = new JObject(); break;
                    }
                case 7:
                    {
                        JToken g = s["gifts"] is JObject ? s["gifts"] : new JObject(), sh = s["shiny"] is JObject ? s["shiny"] : new JObject();
                        var t = new JObject(); var keys = new List<string>();
                        foreach (var k in Js.Keys(g)) if (!keys.Contains(k)) keys.Add(k);
                        foreach (var k in Js.Keys(sh)) if (!keys.Contains(k)) keys.Add(k);
                        foreach (var k in keys)
                        {
                            double a = Js.ToNumber(Js.Get(g, k)); if (double.IsNaN(a)) a = 0;
                            double b = Js.ToNumber(Js.Get(sh, k)); if (double.IsNaN(b)) b = 0;
                            double n = a + b; if (n > 0) t[k] = Js.N(n);
                        }
                        s["totals"] = new JObject { ["gifts"] = t }; s["finale"] = 0; break;
                    }
                case 8:
                    {
                        double g = Js.ToNumber(s["guided"]); if (double.IsNaN(g)) g = 0;
                        JObject t = s["tips"] is JObject tj ? tj : null;
                        if (t == null) { t = new JObject(); s["tips"] = t; }
                        if (g >= 30 && t["h6"] == null) t["h6"] = 1;
                        if (s["deco"] is JArray deco && deco.Any(Js.Truthy) && t["h7"] == null) t["h7"] = 1;
                        s["diarySeen"] = new JObject(); break;
                    }
                case 10:
                    {
                        bool had = s["deco"] is JArray deco && deco.Any(Js.Truthy);
                        s.Remove("deco");
                        if (!(s["isle"] is JObject)) s["isle"] = NewIsle();
                        if (had) ((JObject)s["isle"])["mig"] = 1;
                        break;
                    }
                default: break;     // 9 는 결번
            }
        }

        static readonly int[] Stage4 = { 1, 1, 3, 5 };
        // [1,1,3,5][x] || 1  (x: 숫자 인덱스 또는 그 문자열 키)
        static JValue Pick4(JToken x) { long r = Pick4Or0(x); return new JValue(r != 0 ? r : 1L); }
        static long Pick4Or0(JToken x)
        {
            double d;
            if (Js.IsNumber(x)) d = Js.Num(x);
            else if (Js.IsString(x)) { string str = (string)x; if (str.Length == 1 && str[0] >= '0' && str[0] <= '3') d = str[0] - '0'; else return 0; }
            else return 0;
            if (d >= 0 && d <= 3 && Math.Floor(d) == d) return Stage4[(int)d];
            return 0;
        }

        public static JObject NewIsle() => new JObject { ["v"] = 1, ["put"] = new JObject(), ["own"] = new JObject(), ["news"] = new JArray(), ["mig"] = 0, ["tipSeen"] = 0 };

        // ---------- 표 키 검증 ----------
        /// <summary>SAFE_ID: 짧은 영숫자만, 객체 기본 이름은 거부 (QA-402).</summary>
        public static string SafeId(JToken v) => SafeId(Js.Str(v));
        public static string SafeId(string s)
        {
            if (s == null || s.Length < 1 || s.Length > 40) return null;
            foreach (char c in s) if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == ':' || c == '-')) return null;
            return Js.InObjectPrototype(s) ? null : s;
        }

        bool Known(string tbl, JToken k)
        {
            string id = SafeId(k); if (id == null) return false;
            switch (tbl)
            {
                case "gift": return T.GiftById.ContainsKey(id);
                case "reg": return T.RegularById.ContainsKey(id);
                case "region": return T.RegionById.ContainsKey(id);
                default: return true;
            }
        }

        bool FiniteTime(JToken v) { if (!Js.IsFinite(v)) return false; double x = Js.Num(v); return x >= 0 && x <= T.Bal.MaxT; }

        /// <summary>cleanLetters: 알려진 칸만, 글은 문자열로 길이 제한, 글이 없는 편지는 버림. s 가 있으면 letterId 도 맞춘다.</summary>
        public JArray CleanLetters(JToken arr, JObject s = null)
        {
            var outArr = new JArray(); var ids = new HashSet<long>(); var need = new List<JObject>(); double maxId = 0;
            if (arr is JArray ja)
                foreach (var item in ja)
                {
                    if (outArr.Count >= T.Bal.LetterLimCount) break;
                    if (!(item is JObject l) || !Js.IsString(l["text"])) continue;
                    JToken gv = l["gift"];
                    JToken gift = JValue.CreateNull();
                    if (Js.IsString(gv) && (string)gv != "") gift = Known("gift", gv) ? new JValue((string)gv) : new JValue("shell");
                    var o = new JObject();
                    o["id"] = l["id"] != null ? l["id"].DeepClone() : null;
                    o["ship"] = Js.Slice(Js.Str(l["ship"]) ?? "", T.Bal.LetterLimName);
                    o["cap"] = Js.Slice(Js.Str(l["cap"]) ?? "", T.Bal.LetterLimName);
                    o["gift"] = gift;
                    o["t"] = FiniteTime(l["t"]) ? l["t"].DeepClone() : Js.N(clock.NowMs);
                    o["read"] = l["read"] != null && l["read"].Type == JTokenType.Boolean && (bool)l["read"];
                    if (Known("reg", l["reg"])) o["reg"] = (string)l["reg"];
                    JToken keep = l["keep"];
                    if ((keep != null && keep.Type == JTokenType.Boolean && (bool)keep) || (Js.IsString(keep) && T.Bal.KeepKinds.Contains((string)keep))) o["keep"] = keep.DeepClone();
                    if (Known("region", l["region"])) o["region"] = (string)l["region"];
                    if (l["shiny"] != null && l["shiny"].Type == JTokenType.Boolean && (bool)l["shiny"]) o["shiny"] = true;
                    o["text"] = Js.Slice((string)l["text"], T.Bal.LetterLimText);
                    o["sign"] = Js.Slice(Js.Str(l["sign"]) ?? "", T.Bal.LetterLimSign);
                    if (Js.IsString(l["nm"]) && (string)l["nm"] != "") o["nm"] = Js.Slice((string)l["nm"], T.Bal.LetterLimName);
                    JToken idt = o["id"];
                    if (Js.IsInteger(idt) && Js.Num(idt) >= 0 && Js.Num(idt) < 1e12 && !ids.Contains((long)Js.Num(idt))) { ids.Add((long)Js.Num(idt)); maxId = Math.Max(maxId, Js.Num(idt)); }
                    else need.Add(o);
                    outArr.Add(o);
                }
            foreach (var o in need) o["id"] = Js.N(++maxId);
            if (s != null && !(Js.IsInteger(s["letterId"]) && Js.Num(s["letterId"]) > maxId)) s["letterId"] = Js.N(maxId + 1);
            return outArr;
        }

        /// <summary>cleanIsle: 꾸미기 저장을 알려진 칸만 남겨 새로 만든다. s(저장 전체)가 있으면 put 을 실제 자리·장식 표와 맞춘다.</summary>
        public JObject CleanIsle(JToken v, JObject s = null)
        {
            JObject o = v is JObject jo ? jo : new JObject();
            var put = new JObject(); var own = new JObject();
            if (o["put"] is JObject po) foreach (var k in Js.Keys(po).Take(40)) { if (SafeId(k) != null && Js.IsString(po[k]) && SafeId(po[k]) != null) put[k] = (string)po[k]; }
            if (o["own"] is JObject ow) foreach (var k in Js.Keys(ow).Take(40)) { if (SafeId(k) != null && Js.Truthy(ow[k])) own[k] = 1; }
            var news = new List<string>();
            if (o["news"] is JArray na) foreach (var x in na) if (Js.IsString(x) && SafeId(x) != null) news.Add((string)x);
            news = news.Take(6).ToList();
            if (s != null)
            {
                foreach (var k in Js.Keys(own)) if (!T.DecorItemById.TryGetValue(k, out var it) || it.Src != "season") own.Remove(k);
                news = news.Where(k => T.DecorItemById.ContainsKey(k)).ToList();
                int st = DecorStageOf(s); int gl = DecorLvOf(s, "garden");
                foreach (var k in Js.Keys(put))
                {
                    T.DecorSlotById.TryGetValue(k, out var sl); T.DecorItemById.TryGetValue((string)put[k], out var im);
                    if (sl == null || im == null || !im.Zones.Contains(sl.Chip) || T.SizeRank[im.Size] > T.SizeRank[sl.Size]) put.Remove(k);
                    else if (sl.Blocked) continue;
                    else if (!DecorRules.SlotOpen(sl, st, gl) || !DecorOwnedJ(im.Id, s, own)) put.Remove(k);
                }
            }
            return new JObject { ["v"] = 1, ["put"] = put, ["own"] = own, ["news"] = new JArray(news), ["mig"] = Js.Truthy(o["mig"]) ? 1 : 0, ["tipSeen"] = Js.Truthy(o["tipSeen"]) ? 1 : 0 };
        }

        /// <summary>decorStageOf(s) = Number.isInteger(s.stage) ? s.stage : 1 (개발용 ?s= 덮어쓰기는 옮기지 않는다).</summary>
        static int DecorStageOf(JObject s) => Js.IsInteger(s["stage"]) ? (int)Js.Num(s["stage"]) : 1;

        /// <summary>decorLv(s, k): 저장 s 의 성장 레벨.</summary>
        int DecorLvOf(JObject s, string k)
        {
            JToken gv = Js.Get(s["growth"], k);
            double p = Js.Truthy(s["growth"]) && Js.IsFinite(gv) ? Js.Num(gv) : 0;
            int n = 0; while (p >= Progress.LvAt(T, n + 1) && n < 200) n++;
            return n;
        }

        /// <summary>decorOwnedS(id, s, own): 보유 판정(JObject 판).</summary>
        bool DecorOwnedJ(string id, JObject s, JObject own)
        {
            if (!T.DecorItemById.TryGetValue(id, out var it)) return false;
            if (it.Src == "free") return true;
            if (it.Src == "earn") return DecorLvOf(s, it.NeedKey) >= it.NeedVal;
            if (it.Src == "story")
            {
                JToken r = Js.Truthy(s["reg"]) && Js.HasOwn(s["reg"], it.NeedKey) ? Js.Get(s["reg"], it.NeedKey) : null;
                if (!Js.Truthy(r)) return false;
                double rv = Js.ToNumber(Js.Get(r, "v")); if (double.IsNaN(rv)) rv = 0;
                int idx = (int)it.NeedVal - 1;
                return idx >= 0 && idx < T.Bal.VisitAt.Length && rv >= T.Bal.VisitAt[idx];
            }
            if (it.Src == "gift")
            {
                JToken tot = s["totals"]; JToken g = Js.Truthy(tot) ? Js.Get(tot, "gifts") : null;
                if (!Js.Truthy(g) || !Js.HasOwn(g, it.NeedKey)) return false;
                return Js.ToNumber(Js.Get(g, it.NeedKey)) >= it.NeedVal;
            }
            if (it.Src == "season")
            {
                JToken o = own != null ? own : (Js.Truthy(s["isle"]) ? Js.Get(s["isle"], "own") : null);
                return Js.Truthy(Js.Get(o, id));
            }
            return false;
        }

        /// <summary>cleanChoices: 표 키로 쓰이는 저장 값을 실제 표와 맞춘다 (QA-501).</summary>
        public void CleanChoices(JObject s)
        {
            string One(JToken v, IEnumerable<string> list, string def) { string x = Js.Str(v); return x != null && list.Contains(x) ? x : def; }
            var seasons = new List<string> { "auto" }; seasons.AddRange(GameTables.SeasonIds);
            s["season"] = One(s["season"], seasons, "auto");
            var tr = new List<string> { "auto" }; tr.AddRange(T.Tracks.Select(x => x.Id));
            s["focus"] = One(s["focus"], tr, "auto"); s["offerTo"] = One(s["offerTo"], tr, "auto");
            s["clock"] = One(s["clock"], new[] { "fast", "real" }, "fast");
            string beam = Js.Str(s["beam"]); s["beam"] = beam != null && T.BeamColors.Any(c => c.Id == beam) ? beam : "gold";
            string region = Js.Str(s["region"]); s["region"] = region != null && T.RegionById.ContainsKey(region) ? region : "home";
            if (s["view"] != null && !(Js.IsString(s["view"]) && GameTables.ViewIds.Contains((string)s["view"]))) s.Remove("view");
            var sk = new JObject();
            if (s["skin"] is JObject skin)
                foreach (var layer in T.SkinLayers)
                    if (skin.ContainsKey(layer.Id) && Js.IsString(skin[layer.Id]) && layer.SkinSet.Contains((string)skin[layer.Id])) sk[layer.Id] = (string)skin[layer.Id];
            s["skin"] = sk;
            s.Remove("deco");
            s["isle"] = CleanIsle(s["isle"], s);
            foreach (var k in new[] { "gifts", "dex", "shiny", "found", "reg", "stats", "tips", "anniv", "seasonLetters", "diarySeen" }) s[k] = Strip(s[k]);
            if (s["totals"] is JObject tot) tot["gifts"] = Strip(tot["gifts"]);
        }

        /// <summary>객체 기본 이름 키는 버린다 (JSON 의 own "__proto__" 포함).</summary>
        public static JObject Strip(JToken m)
        {
            var o = new JObject();
            if (m is JObject jo) foreach (var p in jo.Properties()) if (!Js.InObjectPrototype(p.Name)) o[p.Name] = p.Value.DeepClone();
            return o;
        }
    }
}
