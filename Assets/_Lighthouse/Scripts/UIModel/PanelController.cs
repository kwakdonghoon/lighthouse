using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Lighthouse.Core;
using Newtonsoft.Json.Linq;

namespace Lighthouse.UI.Model
{
    /// <summary>
    /// 팝업(편지·도감·성장·꾸미기·상점·설정·소식)의 순수 로직. 원본 renderPanel/onAct 에 해당한다.
    /// Core(GameContext)의 상태를 읽어 블록(Build)으로 바꾸고, 블록의 Act 문자열(Act)을 Core 호출로 옮긴다.
    /// Unity 를 모른다(noEngineReferences) — 그림 그리기는 Lighthouse.UI 의 BlockRenderer 소관.
    /// </summary>
    public sealed partial class PanelController
    {
        public readonly GameContext Ctx;
        public readonly UiStrings Str;
        public readonly IUiHost Host;
        public readonly UiFeatures Features;
        readonly IPaymentAdapter pay; readonly IAdAdapter ads; readonly SaveCode code;
        public GameTables T => Ctx.T;
        public GameState S => Ctx.S;

        // ----- 팝업 상태(저장하지 않는다: 원본 panelMode 등) -----
        public PanelMode Mode = PanelMode.None;
        public string GrowSub = "grow", DexSub = "gift", DecoTab = "place", DecoZone = "path";
        public OfferNews News;
        PanelMode lastTab = PanelMode.Growth, settingsFrom = PanelMode.Growth;
        public readonly HashSet<double> OpenLetters = new HashSet<double>();
        readonly HashSet<string> openRegs = new HashSet<string>();
        readonly Dictionary<string, bool> trkOpen = new Dictionary<string, bool>();
        bool roleOpen, tipsOpen, moreOpen, shopAdOpen;
        string dexRg;

        public PanelController(GameContext ctx, UiStrings str, IUiHost host, IPaymentAdapter payment, IAdAdapter adapter, SaveCode saveCode, UiFeatures features = null)
        {
            Ctx = ctx; Str = str; Host = host; pay = payment; ads = adapter; code = saveCode; Features = features ?? new UiFeatures();
        }

        // ================= 조회 도우미 =================
        string S_(string k) => Str.Get(k);
        string F_(string k, params object[] a) => Str.F(k, a);
        static string Fmt(double n) => n.ToString("N0", CultureInfo.InvariantCulture);
        public int Tier() => Ctx.Tier();
        public int StageFor(int tier) => Array.IndexOf(T.TierOf, tier);
        public bool AdsOn() => Features.Ads && Ctx.Stage >= 3;
        public bool ShopTabOn() => Features.Shop || AdsOn();
        public bool LogbookOn() => Ctx.Stage >= T.MaxStage;
        bool Owns(string id) { var v = S.Owned[id]; return v != null && Js.Truthy(v); }
        string SeasonName(string id) => T.Lint.SeasonName.TryGetValue(id, out var n) ? n : id;
        string TrackName(string id) => T.TrackById.TryGetValue(id, out var t) ? t.Name : id;
        public string FixJosa(string s) => GameContext.FixJosa(s);

        /// <summary>원본 fmtDate: 범위 밖 시각은 빈 글.</summary>
        public string FmtDate(double ts)
        {
            if (double.IsNaN(ts) || ts < 0 || ts > T.Bal.MaxT) return "";
            var d = LocalTime.Of((long)ts, Ctx.Clock);
            return F_("fmt.date", d.Month, d.Day);
        }
        string FmtAway(double s) => s >= 3600 ? F_("fmt.hm", Math.Floor(s / 3600), Math.Floor(s % 3600 / 60)) : F_("fmt.m", Math.Max(1, Math.Floor(s / 60)));
        string MailN(int n) => n >= 100 ? "99" : n.ToString(CultureInfo.InvariantCulture);
        string MailMore(int n) => n >= 100 ? S_("mail.more") : "";

        // ================= 열기·닫기 =================
        public PanelMode MenuTarget()
        {
            if (Ctx.UnreadCount() > 0) return PanelMode.Mail;
            if (Ctx.Discovered() > S.DexSeen) return PanelMode.Dex;
            return lastTab;
        }
        static bool Remember(PanelMode m) => m == PanelMode.Mail || m == PanelMode.Dex || m == PanelMode.Growth || m == PanelMode.Deco;
        PanelMode Normalize(PanelMode m) => m == PanelMode.Shop && !ShopTabOn() ? PanelMode.Growth : (m == PanelMode.None || m == PanelMode.Settings || m == PanelMode.News) ? PanelMode.Growth : m;

        public void Open(PanelMode mode)
        {
            mode = mode == PanelMode.News ? mode : Normalize(mode);
            bool same = Mode == mode;
            Mode = mode; Host.Sfx("tick");
            if (Remember(mode)) lastTab = mode;
            if (!same) { gate = null; adMsg = null; decoConfirm = false; decoOpenItem = null; decoNote = ""; }
            if (mode != PanelMode.Deco) { decoSel = null; decoPeek = false; }
            if (mode != PanelMode.Deco && mode != PanelMode.Shop) ClearPreview();
            if (mode == PanelMode.Dex) { S.DexSeen = Ctx.Discovered(); Ctx.NotifyChanged(); }
        }

        public void OpenNews(OfferNews n) { News = n; Mode = PanelMode.News; }

        public void Close()
        {
            Mode = PanelMode.None; decoSel = null; decoPeek = false; decoConfirm = false; decoOpenItem = null; decoNote = ""; decoUndo.Clear(); decoRedo.Clear(); migShow = false;
            gate = null; shopMsg = ""; shopMsgFor = ""; adMsg = null; ClearPreview(); roleOpen = false; tipsOpen = false; moreOpen = false; shopAdOpen = false; impArmed = false; impText = ""; impMsg = "";
        }

        public void OpenSettings()
        {
            if (Mode == PanelMode.None || Mode == PanelMode.News || Mode == PanelMode.Settings) return;
            settingsFrom = Mode; gate = null; adMsg = null; Host.Sfx("tick"); Mode = PanelMode.Settings; Req = new FocusRequest("setback");      // 설정에 들어오면 "돌아가기"에 초점
        }
        public void LeaveSettings()
        {
            if (Mode != PanelMode.Settings) return;
            Host.Sfx("tick"); tipsOpen = false; moreOpen = false; impArmed = false; Mode = Normalize(settingsFrom); Req = new FocusRequest("gear");      // 돌아가면 머리의 톱니에 초점
        }
        public void ToggleSettings() { if (Mode == PanelMode.Settings) LeaveSettings(); else OpenSettings(); }

        // ================= 화면 모델 =================
        public PanelModel Build()
        {
            if (Mode == PanelMode.Shop && !ShopTabOn()) Mode = PanelMode.Growth;
            var m = new PanelModel { Mode = Mode };
            bool news = Mode == PanelMode.News, set = Mode == PanelMode.Settings;
            m.ShowTabs = !news; m.ShowGear = !news; m.GearOn = set;
            m.Light = Mode == PanelMode.Deco || Mode == PanelMode.Shop;
            m.Peek = Mode == PanelMode.Deco && DecoTab == "place" && decoPeek;
            string[] ids = { "mail", "dex", "growth", "deco", "shop" };
            foreach (var id in ids)
            {
                if (id == "shop" && !ShopTabOn()) continue;
                var pm = TabMode(id);
                string bt = id == "mail" ? Badges.Count(Ctx.UnreadCount()) : id == "dex" && Ctx.Discovered() > S.DexSeen ? Badges.NewText : "";      // 원본 updateBadges: 편지 수(99+)·도감 N
                m.Tabs.Add(new TabInfo { Id = id, Label = S_("tab." + id), On = pm == Mode, Badge = bt != "", BadgeText = bt });
            }
            m.Heading = S_("heading." + (news ? "news" : Mode.ToString().ToLowerInvariant()));
            List<Block> body;
            switch (Mode)
            {
                case PanelMode.News: body = BuildNews(); break;
                case PanelMode.Dex: body = BuildDex(); break;
                case PanelMode.Growth: body = BuildGrowth(); break;
                case PanelMode.Deco: body = BuildDeco(); break;
                case PanelMode.Shop: body = BuildShop(); break;
                case PanelMode.Settings: body = BuildSettingsScreen(); break;
                default: body = BuildMail(); break;
            }
            m.Blocks = body;
            m.Live = decoLive;
            foreach (var b in m.Blocks) JosaFix(b);
            return m;
        }

        static PanelMode TabMode(string id) { switch (id) { case "mail": return PanelMode.Mail; case "dex": return PanelMode.Dex; case "growth": return PanelMode.Growth; case "deco": return PanelMode.Deco; default: return PanelMode.Shop; } }

        void JosaFix(Block b)
        {
            foreach (var d in b.Walk())
            {
                if (d.Text != null) d.Text = FixJosa(d.Text);
                if (d.Sub != null) d.Sub = FixJosa(d.Sub);
                if (d.Extra != null) d.Extra = FixJosa(d.Extra);
            }
        }

        // ================= 입력(원본 onAct) =================
        /// <summary>블록의 Act 한 줄을 Core 호출로 옮긴다. 화면을 다시 만들어야 하면 true(항상 다시 만들어도 된다).</summary>
        public bool Act(string a)
        {
            if (string.IsNullOrEmpty(a)) return false;
            if (a == "setback") { LeaveSettings(); return true; }
            if (a == "photo:go") { Close(); Host.StartPhoto(); return true; }
            if (a == "read") { Open(PanelMode.Mail); return true; }
            if (a == "ok") { Close(); Host.ClosePanelRequested(); return true; }
            if (a == "godex") { Open(PanelMode.Dex); return true; }
            int c = a.IndexOf(':');
            string k = c < 0 ? a : a.Substring(0, c), v = c < 0 ? "" : a.Substring(c + 1);
            switch (k)
            {
                case "tab": Open(TabMode(v)); return true;
                case "gear": ToggleSettings(); return true;
                case "focus": S.Focus = v; Ctx.NotifyChanged(); break;
                case "dtab": DecoTab = v == "skin" ? "skin" : "place"; decoSel = null; decoPeek = false; decoConfirm = false; decoOpenItem = null; decoNote = ""; gate = null; adMsg = null; ClearPreview(); break;
                case "skin": ActSkin(v); break;
                case "skinprev": ClearPreview(); break;
                case "skinshop": Open(PanelMode.Shop); break;
                case "rolebox": roleOpen = !roleOpen; Req = new FocusRequest(a); break;                     // 접이식을 키보드로 눌렀으면 다시 그린 뒤에도 그 단추에 초점(원본 refocus)
                case "buy": BuyAct(v); break;
                case "gate": GateAct(v); break;
                case "gatex": gate = null; break;
                case "ad": AdAct(v); break;
                case "unbuy": Unbuy(v); break;
                case "restore": RestoreAct(); break;
                case "clock": S.Clock = v; Ctx.NotifyChanged(); break;
                case "region": SwitchRegionAct(v); break;
                case "season": S.Season = v; Ctx.NotifyChanged(); Host.WorldDirty(); break;
                case "view": SetExtra("view", v); Ctx.NotifyChanged(); Host.ApplyView(v); break;
                case "wake": SetExtra("wake", v == "on"); Ctx.NotifyChanged(); Host.ApplyWake(v == "on"); break;
                case "readall": foreach (var l in S.Letters) l.Read = true; Ctx.NotifyChanged(); break;
                case "letter": ToggleLetter(v); break;
                case "gomail": GoMail(v); break;
                case "tips": tipsOpen = !tipsOpen; Req = new FocusRequest(a); break;
                case "setmore": moreOpen = !moreOpen; Req = new FocusRequest(a); break;
                case "adtoggle": shopAdOpen = !shopAdOpen; Req = new FocusRequest(a); break;
                case "trk": trkOpen[v] = !(trkOpen.TryGetValue(v, out bool o) && o); Req = new FocusRequest(a); break;
                case "regread": if (T.RegularById.ContainsKey(v)) { if (!openRegs.Remove(v)) openRegs.Add(v); } break;
                case "dex": DexSub = v; break;
                case "dexrg": if (RegionUsable(v)) dexRg = v; break;
                case "grow": GrowSub = v; break;
                case "snd": if ((S.Sound) != (v == "on")) { S.Sound = v == "on"; Ctx.NotifyChanged(); } break;
                case "offer": case "offerall": Ctx.Offer(v, Ctx.OfferTarget(), k == "offer" ? 1 : 9999, false); Host.Sfx("pick"); break;
                case "offerS": case "offerSall": Ctx.Offer(v, Ctx.OfferTarget(), k == "offerS" ? 1 : 9999, true); Host.Sfx("pick"); break;
                case "offerto": S.OfferTo = v; Ctx.NotifyChanged(); break;
                case "autooffer": S.AutoOffer = v == "on"; Ctx.NotifyChanged(); if (S.AutoOffer) Ctx.AutoOfferAll(); break;
                case "beam": { var bc = T.BeamColors.FirstOrDefault(x => x.Id == v); if (bc != null && BeamUsable(bc)) { S.Beam = v; Ctx.NotifyChanged(); Host.WorldDirty(); } break; }
                case "exp": ExportAct(v); break;
                case "imp": ImportAct(); break;
                default:
                    if (!DecoAct(k, v)) return false;
                    break;
            }
            return true;
        }

        // ----- 저장 보조값(화면 쪽 값: Extra 에 보존) -----
        public string ExtraStr(string key, string def) => S.Extra[key] != null && S.Extra[key].Type == JTokenType.String ? (string)S.Extra[key] : def;
        public bool ExtraBool(string key) => S.Extra[key] != null && S.Extra[key].Type == JTokenType.Boolean && (bool)S.Extra[key];
        void SetExtra(string key, object v) { S.Extra[key] = JToken.FromObject(v); }

        // ================= 편지 =================
        void ToggleLetter(string v)
        {
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double id)) return;
            var l = S.Letters.FirstOrDefault(x => x.Id == id);
            if (!OpenLetters.Remove(id)) OpenLetters.Add(id);
            if (l != null && !l.Read) { l.Read = true; Ctx.NotifyChanged(); }
        }
        void GoMail(string v)
        {
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double id))
            {
                var l = S.Letters.FirstOrDefault(x => x.Id == id);
                if (l != null) { OpenLetters.Add(id); if (!l.Read) { l.Read = true; Ctx.NotifyChanged(); } }
            }
            Open(PanelMode.Mail);
        }

        // ================= 바다(지역) =================
        bool RegionUsable(string id) => id != null && T.RegionById.TryGetValue(id, out var r) && (r.Src == "free" || Owns("region:" + id));
        List<string> RegionsOpen() => T.Regions.Where(r => RegionUsable(r.Id)).Select(r => r.Id).ToList();
        string DexRegion() => RegionUsable(dexRg) ? dexRg : Ctx.CurRegion();
        bool InDexRegion(string region) => RegionsOpen().Count < 2 || (string.IsNullOrEmpty(region) ? "home" : region) == DexRegion();
    }

    /// <summary>소식 화면(원본 newsData): OfflineResult 를 화면용으로 한 겹 감싼 것.</summary>
    public sealed class OfferNews
    {
        public OfflineResult R;
        public OfferNews(OfflineResult r) { R = r; }
    }
}
