using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Core
{
    // 저장 대상 상태 = 원본 save 객체의 타입화. 필드 이름은 JSON 키와 같은 뜻(대문자 시작)으로, JObject 왕복이 손실 없이 된다.
    // 모르는 필드(view·wake·lastTab 등 화면 쪽 값, 더 새 빌드의 필드)는 Extra 에 그대로 보존한다 (설계서 4-2).
    // 비저장 런타임 값(날씨·배 목록·관리인 위치·빛 각도·STAGE 와 같은 장면 상태)은 GameContext 쪽에 둔다.

    public sealed class AdsState
    {
        public string Day = "", HomeDay = "";
        public Dictionary<string, double> Used = new Dictionary<string, double> { { "home", 0 }, { "star", 0 }, { "ship", 0 } };
    }

    public sealed class IsleState
    {
        public double V = 1, Mig, TipSeen;
        public Dictionary<string, string> Put = new Dictionary<string, string>();
        public Dictionary<string, double> Own = new Dictionary<string, double>();
        public List<string> News = new List<string>();
    }

    public sealed class BuildState { public double To, StartAt, EndAt; }
    public sealed class RegRecord { public double V; }

    /// <summary>편지 한 통. Shiny 가 null 이면 키가 없고, false 면 false 로 적힌다(기념일 편지가 shiny:false 를 가진다).</summary>
    public sealed class Letter
    {
        public double Id; public string Ship, Cap, Gift; public double T; public bool Read;
        public string Reg; public JToken Keep; public string Region; public bool? Shiny; public string Text, Sign, Nm;

        public bool KeepTruthy => Keep != null && Js.Truthy(Keep);

        public JObject ToJson()
        {
            var o = new JObject { ["id"] = Js.N(Id), ["ship"] = Ship, ["cap"] = Cap, ["gift"] = Gift != null ? (JToken)new JValue(Gift) : JValue.CreateNull(), ["t"] = Js.N(T), ["read"] = Read };
            if (Reg != null) o["reg"] = Reg;
            if (Keep != null) o["keep"] = Keep.DeepClone();
            if (Region != null) o["region"] = Region;
            if (Shiny.HasValue) o["shiny"] = Shiny.Value;
            o["text"] = Text; o["sign"] = Sign;
            if (Nm != null) o["nm"] = Nm;
            return o;
        }

        public static Letter FromJson(JObject o) => new Letter
        {
            Id = Js.Num(o["id"]), Ship = Js.Str(o["ship"]) ?? "", Cap = Js.Str(o["cap"]) ?? "", Gift = Js.Str(o["gift"]), T = Js.Num(o["t"]),
            Read = o["read"] != null && o["read"].Type == JTokenType.Boolean && (bool)o["read"], Reg = Js.Str(o["reg"]), Keep = o["keep"]?.DeepClone(), Region = Js.Str(o["region"]),
            Shiny = o["shiny"] != null && o["shiny"].Type == JTokenType.Boolean ? (bool?)(bool)o["shiny"] : null, Text = Js.Str(o["text"]) ?? "", Sign = Js.Str(o["sign"]) ?? "", Nm = Js.Str(o["nm"])
        };
    }

    public sealed class GameState
    {
        public int Ver;
        public AdsState Ads = new AdsState();
        public double Guided; public bool Seen;
        public Dictionary<string, double> Gifts = new Dictionary<string, double>(), Dex = new Dictionary<string, double>(), Sea = new Dictionary<string, double>(), Found = new Dictionary<string, double>(), Shiny = new Dictionary<string, double>();
        public List<Letter> Letters = new List<Letter>();
        public double LetterId = 1, DexSeen, LastSeen;
        public int Stage = 1;
        public Dictionary<string, double> Growth = new Dictionary<string, double> { { "lens", 0 }, { "garden", 0 }, { "port", 0 } };
        public string Focus = "auto";
        public IsleState Isle = new IsleState();
        public BuildState Build;     // null = 공사 없음
        public Dictionary<string, double> Stats = new Dictionary<string, double>();
        public string Clock = "fast"; public bool Sound = true;
        public Dictionary<string, RegRecord> Reg = new Dictionary<string, RegRecord>();
        public string Season = "auto";
        public double Offered; public string OfferTo = "auto"; public bool AutoOffer = true; public string Beam = "gold";
        public JObject Owned = new JObject();     // 구매 기록: 스토어 영수증이 정본, 저장은 캐시 (설계서 4-2)
        public Dictionary<string, string> Skin = new Dictionary<string, string>();
        public string Region = "home";
        public Dictionary<string, double> Tips = new Dictionary<string, double>();
        public bool TipsOff, FirstBack, LettersTrimmed;
        public double StartAt;
        public Dictionary<string, double> Anniv = new Dictionary<string, double>(), SeasonLetters = new Dictionary<string, double>();
        public double Finale;
        public Dictionary<string, double> Totals = new Dictionary<string, double>();     // totals.gifts
        public Dictionary<string, double> DiarySeen = new Dictionary<string, double>();
        public JObject Extra = new JObject();     // 위에 없는 모든 필드

        static readonly HashSet<string> Known = new HashSet<string>
        {
            "ver","ads","guided","seen","gifts","dex","sea","letters","letterId","dexSeen","lastSeen","stage","growth","focus","isle","build","stats","clock","sound","reg","season","found","shiny",
            "offered","offerTo","autoOffer","beam","owned","skin","region","tips","tipsOff","firstBack","lettersTrimmed","startAt","anniv","seasonLetters","finale","totals","diarySeen"
        };

        // ---------- JObject 왕복 ----------
        static double D(JToken t, double def = 0) { double x = Js.ToNumber(t); return double.IsNaN(x) ? def : x; }
        static bool B(JToken t, bool def = false) => t != null && t.Type == JTokenType.Boolean ? (bool)t : def;
        static Dictionary<string, double> Dic(JToken t)
        {
            var r = new Dictionary<string, double>();
            if (t is JObject o) foreach (var p in o.Properties()) { double x = Js.ToNumber(p.Value); r[p.Name] = double.IsNaN(x) ? 0 : x; }
            return r;
        }
        static JObject Jd(Dictionary<string, double> d) { var o = new JObject(); foreach (var kv in d) o[kv.Key] = Js.N(kv.Value); return o; }

        /// <summary>Migrate 를 거친 저장 JObject 에서 만든다.</summary>
        public static GameState FromJson(JObject s)
        {
            var g = new GameState();
            g.Ver = (int)D(s["ver"]);
            if (s["ads"] is JObject a)
            {
                g.Ads.Day = Js.Str(a["day"]) ?? ""; g.Ads.HomeDay = Js.Str(a["homeDay"]) ?? "";
                g.Ads.Used = Dic(a["used"]);
            }
            g.Guided = D(s["guided"]); g.Seen = B(s["seen"]);
            g.Gifts = Dic(s["gifts"]); g.Dex = Dic(s["dex"]); g.Sea = Dic(s["sea"]); g.Found = Dic(s["found"]); g.Shiny = Dic(s["shiny"]);
            if (s["letters"] is JArray la) foreach (var l in la) if (l is JObject lo) g.Letters.Add(Letter.FromJson(lo));
            g.LetterId = D(s["letterId"], 1); g.DexSeen = D(s["dexSeen"]); g.LastSeen = D(s["lastSeen"]); g.Stage = (int)D(s["stage"], 1);
            g.Growth = Dic(s["growth"]); g.Focus = Js.Str(s["focus"]) ?? "auto";
            if (s["isle"] is JObject i)
            {
                g.Isle.V = D(i["v"], 1); g.Isle.Mig = D(i["mig"]); g.Isle.TipSeen = D(i["tipSeen"]);
                if (i["put"] is JObject put) foreach (var p in put.Properties()) g.Isle.Put[p.Name] = (string)p.Value;
                g.Isle.Own = Dic(i["own"]);
                if (i["news"] is JArray news) foreach (var n in news) g.Isle.News.Add((string)n);
            }
            if (s["build"] is JObject b) g.Build = new BuildState { To = D(b["to"]), StartAt = D(b["startAt"]), EndAt = D(b["endAt"]) };
            g.Stats = Dic(s["stats"]); g.Clock = Js.Str(s["clock"]) ?? "fast"; g.Sound = B(s["sound"], true);
            if (s["reg"] is JObject rg) foreach (var p in rg.Properties()) g.Reg[p.Name] = new RegRecord { V = D(p.Value is JObject ro ? ro["v"] : null) };
            g.Season = Js.Str(s["season"]) ?? "auto"; g.Offered = D(s["offered"]); g.OfferTo = Js.Str(s["offerTo"]) ?? "auto"; g.AutoOffer = B(s["autoOffer"], true); g.Beam = Js.Str(s["beam"]) ?? "gold";
            g.Owned = s["owned"] is JObject ow ? (JObject)ow.DeepClone() : new JObject();
            if (s["skin"] is JObject sk) foreach (var p in sk.Properties()) g.Skin[p.Name] = (string)p.Value;
            g.Region = Js.Str(s["region"]) ?? "home"; g.Tips = Dic(s["tips"]);
            g.TipsOff = B(s["tipsOff"]); g.FirstBack = B(s["firstBack"]); g.LettersTrimmed = B(s["lettersTrimmed"]);
            g.StartAt = D(s["startAt"]); g.Anniv = Dic(s["anniv"]); g.SeasonLetters = Dic(s["seasonLetters"]); g.Finale = D(s["finale"]);
            g.Totals = s["totals"] is JObject tj ? Dic(tj["gifts"]) : new Dictionary<string, double>();
            g.DiarySeen = Dic(s["diarySeen"]);
            foreach (var p in s.Properties()) if (!Known.Contains(p.Name)) g.Extra[p.Name] = p.Value.DeepClone();
            return g;
        }

        public JObject ToJson()
        {
            var s = new JObject();
            s["ver"] = Ver;
            s["ads"] = new JObject { ["day"] = Ads.Day, ["used"] = Jd(Ads.Used), ["homeDay"] = Ads.HomeDay };
            s["guided"] = Js.N(Guided); s["seen"] = Seen; s["gifts"] = Jd(Gifts); s["dex"] = Jd(Dex); s["sea"] = Jd(Sea);
            s["letters"] = new JArray(Letters.Select(l => (JToken)l.ToJson()));
            s["letterId"] = Js.N(LetterId); s["dexSeen"] = Js.N(DexSeen); s["lastSeen"] = Js.N(LastSeen); s["stage"] = Stage; s["growth"] = Jd(Growth); s["focus"] = Focus;
            s["isle"] = new JObject { ["v"] = Js.N(Isle.V), ["put"] = new JObject(Isle.Put.Select(kv => new JProperty(kv.Key, kv.Value))), ["own"] = Jd(Isle.Own), ["news"] = new JArray(Isle.News), ["mig"] = Js.N(Isle.Mig), ["tipSeen"] = Js.N(Isle.TipSeen) };
            s["build"] = Build == null ? JValue.CreateNull() : new JObject { ["to"] = Js.N(Build.To), ["startAt"] = Js.N(Build.StartAt), ["endAt"] = Js.N(Build.EndAt) };
            s["stats"] = Jd(Stats); s["clock"] = Clock; s["sound"] = Sound;
            s["reg"] = new JObject(Reg.Select(kv => new JProperty(kv.Key, new JObject { ["v"] = Js.N(kv.Value.V) })));
            s["season"] = Season; s["found"] = Jd(Found); s["shiny"] = Jd(Shiny); s["offered"] = Js.N(Offered); s["offerTo"] = OfferTo; s["autoOffer"] = AutoOffer; s["beam"] = Beam;
            s["owned"] = Owned.DeepClone(); s["skin"] = new JObject(Skin.Select(kv => new JProperty(kv.Key, kv.Value))); s["region"] = Region; s["tips"] = Jd(Tips);
            s["tipsOff"] = TipsOff; s["firstBack"] = FirstBack; s["lettersTrimmed"] = LettersTrimmed; s["startAt"] = Js.N(StartAt);
            s["anniv"] = Jd(Anniv); s["seasonLetters"] = Jd(SeasonLetters); s["finale"] = Js.N(Finale);
            s["totals"] = new JObject { ["gifts"] = Jd(Totals) }; s["diarySeen"] = Jd(DiarySeen);
            foreach (var p in Extra.Properties()) s[p.Name] = p.Value.DeepClone();
            return s;
        }

        public GameState Clone() => FromJson(ToJson());
    }
}
