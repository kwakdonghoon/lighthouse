using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Core
{
    /// <summary>parseSaveCode 의 결과: Err 가 있으면 쓸 수 없는 코드, 아니면 Data.</summary>
    public sealed class SaveCodeResult { public string Err; public JObject Data; }

    /// <summary>
    /// 저장 코드 내보내기·가져오기 (원본 saveCode / sanitizeImport / parseSaveCode, HTML 3684~3735행).
    /// LHS1.&lt;base64(UTF-8 JSON)&gt;.&lt;FNV-1a 36진&gt; — 확인값일 뿐 서명이 아니라 위조 가능하다. 그래서 sanitizeImport 가
    /// 허용 목록 방식으로 새로 만든다. owned(구매)·ads(광고 횟수)는 코드에 담지 않고 가져와도 이 기기 것을 유지한다.
    /// </summary>
    public sealed class SaveCode
    {
        readonly GameTables T; readonly SaveMigrator mig;
        public SaveCode(GameTables tables, SaveMigrator migrator) { T = tables; mig = migrator; }

        // ---------- 내보내기 ----------
        /// <summary>saveCode(): 저장에서 owned·ads 를 빼고 JSON → UTF-8 → base64 → LHS1 코드.</summary>
        public string Export(JObject save)
        {
            var rest = new JObject();
            foreach (var p in save.Properties()) if (p.Name != "owned" && p.Name != "ads") rest[p.Name] = p.Value.DeepClone();
            string body = Convert.ToBase64String(new UTF8Encoding(false).GetBytes(Js.Stringify(rest)));
            return "LHS1." + body + "." + Js.HashStr(body);
        }

        // ---------- 가져오기 ----------
        static readonly Regex CodeRe = new Regex(@"^LHS1\.([A-Za-z0-9+/=]+)\.([a-z0-9]+)$", RegexOptions.CultureInvariant);

        /// <summary>deviceOwned: 이 기기의 구매 기록(코드에는 안 담긴다). 주면 구매가 필요한 장식(팩)을 놓은 자리를 이 기기 기준으로 검증한다.</summary>
        public SaveCodeResult Parse(string text, JObject deviceOwned = null)
        {
            string t = Regex.Replace(text ?? "", @"\s+", "");
            var m = CodeRe.Match(t);
            if (!m.Success) return Fail("저장 코드가 아니에요. 복사한 코드 전체를 붙여 넣어 주세요.");
            if (Js.HashStr(m.Groups[1].Value) != m.Groups[2].Value) return Fail("코드가 중간에 잘렸거나 바뀌었어요. 다시 복사해 주세요.");
            JToken d;
            try
            {
                byte[] bytes = Atob(m.Groups[1].Value);
                d = Js.Parse(new UTF8Encoding(false, true).GetString(bytes));
            }
            catch (Exception) { return Fail("코드를 읽을 수 없어요."); }
            JObject o = d as JObject;
            if (o == null || !(o["gifts"] is JObject) || !(o["growth"] is JObject) || !(o["dex"] is JObject) || !Js.IsInteger(o["stage"]) || Js.Num(o["stage"]) < 1 || Js.Num(o["stage"]) > T.MaxStage
                || !(o["letters"] is JArray) || !(o["deco"] is JArray || o["isle"] is JObject))
                return Fail("이 게임의 저장 코드가 아니에요.");
            var san = Sanitize(o); if (deviceOwned != null) san["owned"] = deviceOwned.DeepClone();
            return new SaveCodeResult { Data = mig.Migrate(san) };
        }

        static SaveCodeResult Fail(string e) => new SaveCodeResult { Err = e };

        /// <summary>JS atob: 패딩이 없어도 받는다(길이 % 4 == 1 만 거부).</summary>
        static byte[] Atob(string s)
        {
            int pad = 0; while (s.Length > 0 && s[s.Length - 1] == '=' && pad < 2) { s = s.Substring(0, s.Length - 1); pad++; }
            if (s.IndexOf('=') >= 0) throw new FormatException();
            if (s.Length % 4 == 1) throw new FormatException();
            if (pad > 0 && (s.Length + pad) % 4 != 0) throw new FormatException();
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            return Convert.FromBase64String(s);
        }

        // ---------- sanitizeImport ----------
        static double NumC(JToken v, double lo, double hi, double def) { if (!Js.IsFinite(v)) return def; return Math.Min(hi, Math.Max(lo, Js.Num(v))); }
        static double IntC(JToken v, double lo, double hi, double def) { if (!Js.IsFinite(v)) return def; return Math.Min(hi, Math.Max(lo, Math.Floor(Js.Num(v)))); }
        static bool BoolC(JToken v, bool def) => v != null && v.Type == JTokenType.Boolean ? (bool)v : def;
        static string One(JToken v, IEnumerable<string> list, string def) { string x = Js.Str(v); return x != null && list.Contains(x) ? x : def; }
        static JToken Cnt(JToken v) => Js.IsFinite(v) && Js.Num(v) > 0 ? Js.N(Math.Min(1e9, Math.Floor(Js.Num(v)))) : null;
        static JToken Flag(JToken v) => Js.Truthy(v) ? new JValue(1L) : null;
        static JObject MapOf(JToken m, Func<string, bool> keyOk, Func<JToken, JToken> val)
        {
            var o = new JObject();
            if (m is JObject jo)
                foreach (var k in Js.Keys(jo).Take(200))
                    if (!Js.InObjectPrototype(k) && keyOk(k)) { var x = val(jo[k]); if (x != null) o[k] = x; }
            return o;
        }
        static readonly Regex RxStats = new Regex(@"^[A-Za-z0-9_]{1,30}$"), RxTips = new Regex(@"^[A-Za-z0-9_]{1,20}$"), RxAnniv = new Regex(@"^[0-9]{1,5}$"), RxSeasonLetters = new Regex(@"^[0-9]{4}:[a-z]{1,10}$"), RxDiary = new Regex(@"^[a-z]{1,12}$");
        // JS 정규식 ^…$ 는 끝 줄바꿈을 허용하지 않는다(.NET 의 $ 는 허용) → 직접 확인
        static bool Rx(Regex r, string k) => r.IsMatch(k) && !k.EndsWith("\n");

        /// <summary>sanitizeImport(d): 알려진 칸·형식·범위·길이만 남겨 새로 만든다.</summary>
        public JObject Sanitize(JObject d)
        {
            var s = new JObject();
            JToken dv = d["ver"];
            if (Js.IsInteger(dv)) s["ver"] = Js.N(IntC(dv, 1, 99, T.SaveVer));
            if (Js.IsNumber(d["stageV"]) && Js.Num(d["stageV"]) == 2) s["stageV"] = 2;
            s["guided"] = Js.N(IntC(d["guided"], 0, 1e9, 0)); s["seen"] = BoolC(d["seen"], false);
            Func<string, bool> gk = k => T.GiftById.ContainsKey(k);
            s["gifts"] = MapOf(d["gifts"], gk, Cnt); s["dex"] = MapOf(d["dex"], k => T.ShipById.ContainsKey(k), Cnt); s["sea"] = MapOf(d["sea"], k => T.SeaById.ContainsKey(k), Cnt);
            s["shiny"] = MapOf(d["shiny"], gk, Cnt); s["found"] = MapOf(d["found"], gk, Flag);
            s["letters"] = mig.CleanLetters(d["letters"]); s["letterId"] = Js.N(IntC(d["letterId"], 1, 1e12, 1)); s["dexSeen"] = Js.N(IntC(d["dexSeen"], 0, 1e6, 0));
            s["lastSeen"] = Js.N(NumC(d["lastSeen"], 0, T.Bal.MaxT, 0)); s["stage"] = Js.N(IntC(d["stage"], 1, T.MaxStage, 1));
            var growth = new JObject(); foreach (var tr in T.Tracks) growth[tr.Id] = Js.N(NumC(d["growth"] is JObject gj ? gj[tr.Id] : new JValue(0L), 0, 1e12, 0)); s["growth"] = growth;
            var trIds = new List<string> { "auto" }; trIds.AddRange(T.Tracks.Select(x => x.Id));
            s["focus"] = One(d["focus"], trIds, "auto");
            s["isle"] = mig.CleanIsle(d["isle"]);
            if (!(Js.IsInteger(d["ver"]) && Js.Num(d["ver"]) >= 10) && d["deco"] is JArray deco)
            {
                var arr = new JArray();
                foreach (var k in deco.Take(24)) arr.Add(Js.IsString(k) && gk((string)k) ? (JToken)new JValue((string)k) : JValue.CreateNull());
                s["deco"] = arr;
            }
            if (d["build"] is JObject b && Js.IsFinite(b["to"]))
                s["build"] = new JObject { ["to"] = Js.N(IntC(b["to"], 2, T.MaxStage, T.MaxStage)), ["startAt"] = Js.N(NumC(b["startAt"], 0, T.Bal.MaxT, 0)), ["endAt"] = Js.N(NumC(b["endAt"], 0, T.Bal.MaxT, 0)) };
            else s["build"] = JValue.CreateNull();
            s["stats"] = MapOf(d["stats"], k => Rx(RxStats, k), Cnt);
            s["clock"] = One(d["clock"], new[] { "fast", "real" }, "fast"); s["sound"] = BoolC(d["sound"], true);
            if (d["wake"] != null && d["wake"].Type == JTokenType.Boolean) s["wake"] = (bool)d["wake"];
            s["reg"] = MapOf(d["reg"], k => T.RegularById.ContainsKey(k), v => new JObject { ["v"] = Js.N(IntC(v is JObject vo ? vo["v"] : new JValue(0L), 0, 999, 0)) });
            var seasons = new List<string> { "auto" }; seasons.AddRange(GameTables.SeasonIds);
            s["season"] = One(d["season"], seasons, "auto");
            if (Js.IsString(d["view"]) && GameTables.ViewIds.Contains((string)d["view"])) s["view"] = (string)d["view"];
            s["offered"] = Js.N(NumC(d["offered"], 0, 1e12, 0)); s["offerTo"] = One(d["offerTo"], trIds, "auto"); s["autoOffer"] = BoolC(d["autoOffer"], true);
            string beam = Js.Str(d["beam"]); s["beam"] = beam != null && T.BeamColors.Any(c => c.Id == beam) ? beam : "gold";
            var skin = new JObject();
            if (d["skin"] is JObject sk) foreach (var layer in T.SkinLayers) if (Js.IsString(sk[layer.Id]) && layer.SkinSet.Contains((string)sk[layer.Id])) skin[layer.Id] = (string)sk[layer.Id];
            s["skin"] = skin;
            string region = Js.Str(d["region"]); s["region"] = region != null && T.RegionById.ContainsKey(region) ? region : "home";
            s["tips"] = MapOf(d["tips"], k => Rx(RxTips, k), Flag); s["tipsOff"] = BoolC(d["tipsOff"], false); s["firstBack"] = BoolC(d["firstBack"], false); s["lettersTrimmed"] = BoolC(d["lettersTrimmed"], false);
            s["startAt"] = Js.N(NumC(d["startAt"], 0, T.Bal.MaxT, 0));
            s["anniv"] = MapOf(d["anniv"], k => Rx(RxAnniv, k), Flag); s["seasonLetters"] = MapOf(d["seasonLetters"], k => Rx(RxSeasonLetters, k), Flag);
            s["finale"] = Js.N(NumC(d["finale"], 0, T.Bal.MaxT, 0));
            s["diarySeen"] = MapOf(d["diarySeen"], k => Rx(RxDiary, k), Flag);
            s["totals"] = new JObject { ["gifts"] = MapOf(d["totals"] is JObject tj ? tj["gifts"] : null, gk, Cnt) };
            if (d.ContainsKey("lastTab")) { string lt = Js.Str(d["lastTab"]); s["lastTab"] = lt == "mail" || lt == "dex" || lt == "growth" || lt == "deco" ? lt : "growth"; }
            if (d.ContainsKey("decoTab")) { string dt = Js.Str(d["decoTab"]); s["decoTab"] = dt == "skin" || dt == "shop" ? "skin" : "place"; }
            if (d.ContainsKey("growSub")) s["growSub"] = One(d["growSub"], new[] { "grow", "offer", "mile", "log" }, "grow");
            return s;
        }
    }
}
