using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Lighthouse.Core;

namespace Lighthouse.UI.Model
{
    public sealed class GateState { public string Type, Id; public int A, B, Right; public List<int> Choices = new List<int>(); }

    public sealed partial class PanelController
    {
        // ----- 상점 상태(원본 gate·shopMsg·adMsg·buying·adBusy) -----
        GateState gate; string shopMsg = "", shopMsgFor = "", buying = "", adBusy = "";
        string[] adMsg;            // [종류, 글]
        public GateState Gate => gate;
        Task<PurchaseResult> payTask; ProductDef payProduct; bool paySkin; long payStart;
        Task<AdResult> adTask; string adTaskKind; long adStart; bool adShown;
        public bool AdShowing => adBusy != "" && adShown;
        public string AdKindShowing => adBusy;
        void SetShopMsg(string text, string id = "") { shopMsg = text; shopMsgFor = id; }

        static string Won(double n) => "₩" + Fmt(n);
        bool ProductOwned(ProductDef p) => p.Grants.All(Owns);
        double Due(ProductDef p) => ShopMath.DueOf(T, p, Owns);

        // ================= 상점 (원본 renderShop) =================
        string AdWhy(string k)
        {
            if (Ctx.AdLeft(k) <= 0) return "done";
            if (k == "home" && Ctx.AdsToday().HomeDay != Ctx.TodayKey()) return "home";
            if (k == "star" && !Ctx.Night) return "night";
            return "";
        }

        List<Block> BuildShop()
        {
            var r = new List<Block>();
            var pv = PreviewBanner(true); if (pv != null) r.Add(pv);
            if (Features.Shop) r.Add(Block.N((Features.PaymentTest ? S_("shop.testNote") + "\n" : "") + S_("shop.note")));
            if (AdsOn())
            {
                bool nad = Owns("noads");
                var kinds = T.AdKinds; int sum = kinds.Sum(k => AdWhy(k.Id) != "" ? 0 : (int)Ctx.AdLeft(k.Id));
                bool open = shopAdOpen || adBusy != "" || adMsg != null || (gate != null && gate.Type == "ad");
                var sec = new Block(BK.Card) { Style = "adsec", Text = nad ? S_("shop.adHeadNoAd") : S_("shop.adHead"), Sub = (sum > 0 ? F_("shop.adLeft", sum) : S_("shop.adNone")) + " · " + (open ? S_("grow.fold") : S_("shop.unfold")), Act = "adtoggle", On = open };
                if (open)
                {
                    sec.Add(Block.N(nad ? S_("shop.adNoteNoAd") : S_("shop.adNote")));
                    foreach (var k in kinds) sec.Add(AdRow(k, nad));
                }
                r.Add(sec);
            }
            if (Features.Shop)
            {
                r.Add(Block.H(S_("shop.prodTitle")));
                foreach (var p in T.Products) r.Add(ProdBlock(p));
                if (shopMsg != "" && !T.Products.Any(p => p.Id == shopMsgFor)) r.Add(Block.N(shopMsg, "status"));
                r.Add(Block.Btn(S_("shop.restore"), "restore:go"));
            }
            return r;
        }

        Block AdRow(AdKindDef A, bool nad)
        {
            int left = (int)Ctx.AdLeft(A.Id); string why = AdWhy(A.Id); bool asking = gate != null && gate.Type == "ad" && gate.Id == A.Id, loading = adBusy == A.Id;
            string sub = loading ? S_("shop.adLoading") : why == "done" ? S_("shop.adTomorrow") : why == "home" ? S_("shop.adHome") : why == "night" ? S_("shop.adNight") : F_("shop.adLeftToday", left);
            bool off = why != "" || adBusy != "" || (gate != null && !asking);
            var row = new Block(BK.Row) { Style = "adrow" + (why == "done" ? " off" : ""), Icon = A.Icon, Text = A.Name, Tag = nad ? null : S_("shop.adTag"), Sub = sub, Extra = A.Desc, Locked = why == "done" };
            if (!asking) row.Add(Block.Btn(why == "done" ? S_("shop.adDone") : S_("shop.adGet"), "ad:" + A.Id, "main", off));
            if (asking) row.Add(GateBlock());
            if (adMsg != null && adMsg[0] == A.Id) row.Add(Block.N(adMsg[1], "status"));
            return row;
        }

        Block ProdBlock(ProductDef p)
        {
            bool own = ProductOwned(p), asking = gate != null && gate.Type == "buy" && gate.Id == p.Id; double due = Due(p); bool cut = due < p.Price, now = buying == p.Id;
            var have = p.Grants.Where(Owns).ToList();
            var c = new Block(BK.Card) { Style = "prod" + (own ? " own" : "") + (asking ? " gating" : ""), Text = p.Name, Sub = own ? S_("shop.ownAll") : p.Desc };
            if (!own && have.Count > 0)
            {
                c.Add(new Block(BK.Row) { Style = "have", Icon = "check", Text = F_("shop.haveN", have.Count), Sub = "(" + string.Join(", ", have.Select(g => T.ItemNames.TryGetValue(g, out var nm) ? nm : g)) + ")" });
                c.Add(Block.N(S_("shop.deduct"), "small"));
            }
            if (asking) c.Add(GateBlock());
            else if (own)
            {
                var line = new Block(BK.Row) { Style = "pline", Icon = "check", Text = S_("shop.owned") };
                if (Features.PaymentTest) line.Add(Block.Btn(S_("shop.unbuy"), "unbuy:" + p.Id));
                c.Add(line);
            }
            else
            {
                var line = new Block(BK.Row) { Style = "pline", Text = Won(due), Sub = cut ? Won(p.Price) : null };
                line.Add(Block.Btn(now ? S_("shop.processing") : S_("shop.buy"), "buy:" + p.Id, "main", now || buying != "" || gate != null));
                c.Add(line);
            }
            if (shopMsg != "" && shopMsgFor == p.Id) c.Add(Block.N(shopMsg, "status"));
            return c;
        }

        Block GateBlock()
        {
            var g = new Block(BK.Gate);
            if (gate.Type == "buy")
            {
                var p = T.Products.FirstOrDefault(q => q.Id == gate.Id);
                g.Text = F_("gate.buy", p != null ? " (" + p.Name + " " + Won(Due(p)) + ")" : "");
                g.Add(Block.Btn(S_("gate.ok"), "gate:ok", "main"));
            }
            else
            {
                g.Text = F_("gate.ad", gate.A, gate.B);
                foreach (var c in gate.Choices) g.Add(Block.Btn(c.ToString(CultureInfo.InvariantCulture), "gate:" + c));
            }
            g.Add(Block.Btn(S_("gate.cancel"), "gatex:go", "txt"));
            return g;
        }

        void OpenGate(string type, string id)
        {
            int a = 3 + (int)Math.Floor(Ctx.Rng.NextDouble() * 7), b = 3 + (int)Math.Floor(Ctx.Rng.NextDouble() * 7), right = a * b;
            var set = new List<int> { right };
            while (set.Count < 3)
            {
                int c = right + (Ctx.Rng.NextDouble() < 0.5 ? -1 : 1) * (1 + (int)Math.Floor(Ctx.Rng.NextDouble() * 10));
                if (c >= 9 && !set.Contains(c)) set.Add(c);
            }
            // 보기 순서 섞기(원본 sort(() => random - 0.5) 와 같은 느낌의 단순 섞기)
            for (int i = set.Count - 1; i > 0; i--) { int j = (int)Math.Floor(Ctx.Rng.NextDouble() * (i + 1)); var t = set[i]; set[i] = set[j]; set[j] = t; }
            gate = new GateState { Type = type, Id = id, A = a, B = b, Right = right, Choices = set };
            SetShopMsg(""); adMsg = null; Req = new FocusRequest("gate");                              // 보호자 확인이 열리면 첫 보기로 초점(원본 1870행)
        }

        void BuyAct(string id)
        {
            var p = T.Products.FirstOrDefault(q => q.Id == id);
            if (p != null && !ProductOwned(p) && buying == "") OpenGate("buy", id);
        }

        void GateAct(string v)
        {
            var g = gate; gate = null;
            if (g == null) return;
            bool ok = g.Type == "buy" ? v == "ok" : int.TryParse(v, out int n) && n == g.Right;
            if (g.Type == "ad") { if (ok) StartAd(g.Id); else adMsg = new[] { g.Id, S_("gate.wrong") }; return; }
            var p = T.Products.FirstOrDefault(q => q.Id == g.Id);
            if (p == null) return;
            if (!ok) { SetShopMsg(S_("shop.notBought"), p.Id); return; }
            double due = Due(p); paySkin = p.Grants.Where(x => !Owns(x)).Any(x => x.Contains(':') && !x.StartsWith("beam:", StringComparison.Ordinal));
            payProduct = p; buying = p.Id; payStart = Ctx.Clock.NowMs;
            try { payTask = pay.Purchase(p, (int)due); } catch (Exception) { payTask = Task.FromResult(new PurchaseResult { Ok = false, Reason = "error" }); }
        }

        void FinishBuy(PurchaseResult r)
        {
            var p = payProduct; buying = ""; payTask = null;
            if (p == null) return;
            if (r != null && r.Ok)
            {
                foreach (var g in p.Grants.Where(x => !Owns(x)).ToList()) S.Owned[g] = true;
                Ctx.NotifyChanged(); Host.WorldDirty(); Host.Sfx("pick"); SkinPreview = null;
                SetShopMsg(F_("shop.got", p.Name) + (paySkin ? S_("shop.gotSkin") : ""), p.Id);
            }
            else SetShopMsg(S_("shop.failed"), p.Id);
        }

        void Unbuy(string id)
        {
            var p = T.Products.FirstOrDefault(q => q.Id == id); if (p == null || !Features.PaymentTest) return;
            var keep = new HashSet<string>(T.Products.Where(q => q != p && ProductOwned(q)).SelectMany(q => q.Grants));
            foreach (var g in p.Grants) if (!keep.Contains(g)) S.Owned.Remove(g);
            foreach (var kv in S.Skin.ToList()) if (!SkinUsable(kv.Key, kv.Value)) S.Skin.Remove(kv.Key);
            var bc = T.BeamColors.FirstOrDefault(c => c.Id == S.Beam); if (bc != null && !BeamUsable(bc)) S.Beam = "gold";
            Ctx.NotifyChanged(); Host.WorldDirty();
            SetShopMsg(F_("shop.unbought", p.Name), p.Id);
        }

        void RestoreAct()
        {
            var t = pay.Restore();
            if (t.IsCompleted) { int n = t.Result.Count; SetShopMsg(n > 0 ? F_("shop.restored", n) : S_("shop.restoreNone")); }
        }

        // ================= 광고 (원본 adAct / startAd / finishAd) =================
        void AdAct(string k)
        {
            if (!T.AdKinds.Any(x => x.Id == k) || !AdsOn() || adBusy != "" || AdWhy(k) != "") return;
            if (Owns("noads")) StartAd(k); else OpenGate("ad", k);
        }

        void StartAd(string k)
        {
            if (adBusy != "") return;
            adMsg = null;
            if (Owns("noads")) { FinishAd(k, new AdResult { Ok = true }); return; }
            adBusy = k; adStart = Ctx.Clock.NowMs; adShown = false; adTaskKind = k;
            try { adTask = ads.Show(k); } catch (Exception) { adTask = Task.FromResult(new AdResult { Ok = false, Reason = "error" }); }
        }

        /// <summary>화면이 광고를 실제로 띄웠다고 알린다(불러오기 시간 제한 해제).</summary>
        public void AdShown() { adShown = true; }

        void FinishAd(string k, AdResult r)
        {
            adBusy = ""; adTask = null;
            if (r.Ok)
            {
                var A = T.AdKinds.First(x => x.Id == k); var a = Ctx.AdsToday();
                if (a.Used[k] >= A.Limit) adMsg = new[] { k, S_("shop.adDoneMsg") };
                else
                {
                    a.Used[k]++;
                    string g = Ctx.RollGift("trade"); bool shiny = Ctx.AddGift(g, new GameContext.AddGiftOpts { Quiet = true });
                    Ctx.NotifyChanged(); Host.Sfx("pick");
                    adMsg = new[] { k, FixJosa(F_("shop.adGot", (shiny ? S_("mail.shinyPre") : "") + T.GiftById[g].Name)) };
                }
            }
            else adMsg = new[] { k, r.Reason == "closed" ? S_("shop.adClosed") : S_("shop.adError") };
            if (Mode == PanelMode.Shop) Req = new FocusRequest("ad:" + k);                                 // 결과를 보고도 같은 단추에서 이어 가게(원본 1789행)
            if (Mode != PanelMode.Shop && adMsg != null) { Host.Toast(adMsg[1], null); adMsg = null; }
        }

        /// <summary>매 프레임(또는 시험에서 직접): 끝난 결제·광고를 반영하고 시간 제한을 본다. 화면을 다시 만들어야 하면 true.</summary>
        public bool Tick()
        {
            bool dirty = false; long now = Ctx.Clock.NowMs;
            if (payTask != null)
            {
                if (payTask.IsCompleted) { FinishBuy(payTask.IsFaulted ? null : payTask.Result); dirty = true; }
                else if (now - payStart > 10000) { FinishBuy(null); dirty = true; }
            }
            if (adTask != null)
            {
                if (adTask.IsCompleted) { var r = adTask.IsFaulted ? new AdResult { Ok = false, Reason = "error" } : adTask.Result; var k = adTaskKind; FinishAd(k, r); dirty = true; }
                else if (!adShown && now - adStart > 8000) { try { ads.Abort(); } catch (Exception) { } FinishAd(adTaskKind, new AdResult { Ok = false, Reason = "error" }); dirty = true; }
            }
            return dirty;
        }

        // ================= 설정 (원본 renderSettings) =================
        List<Block> BuildSettingsScreen()
        {
            var r = new List<Block> { Block.Btn(S_("set.back"), "setback", "tbtn") };
            r.AddRange(BuildSettings());
            return r;
        }

        Block SetCard(string title, string hint, Block body)
        {
            var c = new Block(BK.Card) { Style = "set", Text = title, Sub = hint };
            if (body != null) c.Add(body);
            return c;
        }

        Block SegOf(string[][] items, string on)
        {
            var seg = Block.Seg(true);
            foreach (var it in items) seg.Add(new Block(BK.Button) { Text = it[1], Act = it[0], On = on == it[0], Sub = it.Length > 2 ? it[2] : null, Locked = it.Length > 2 && it[2] != null, Style = "btn" });
            return seg;
        }

        List<Block> BuildSettings()
        {
            var r = new List<Block>();
            string cur = string.IsNullOrEmpty(S.Season) ? "auto" : S.Season;
            string view = ExtraStr("view", "normal"); if (view != "near" && view != "wide") view = "normal";
            r.Add(SetCard(S_("set.sound"), S_("set.soundHint"), SegOf(new[] { new[] { "snd:on", S_("seg.on") }, new[] { "snd:off", S_("seg.off") } }, S.Sound ? "snd:on" : "snd:off")));
            r.Add(SetCard(S_("set.view"), S_("set.viewHint"), SegOf(new[] { new[] { "view:near", S_("set.view.near") }, new[] { "view:normal", S_("set.view.normal") }, new[] { "view:wide", S_("set.view.wide") } }, "view:" + view)));
            var regs = RegionsOpen();
            if (regs.Count > 1) r.Add(SetCard(S_("set.region"), S_("set.regionHint"), SegOf(regs.Select(id => new[] { "region:" + id, T.RegionById[id].Name }).ToArray(), "region:" + Ctx.CurRegion())));
            r.Add(SetCard(S_("set.season"), F_("set.seasonHint", SeasonName(Ctx.CurSeason())), SegOf(new[] { new[] { "season:auto", S_("seg.auto") }, new[] { "season:spring", SeasonName("spring") }, new[] { "season:summer", SeasonName("summer") }, new[] { "season:autumn", SeasonName("autumn") }, new[] { "season:winter", SeasonName("winter") } }, "season:" + cur)));
            r.Add(SetCard(S_("set.clock"), F_("set.clockHint", Math.Round(Ctx.CycleSec / 60)), SegOf(new[] { new[] { "clock:fast", S_("set.clock.fast") }, new[] { "clock:real", S_("set.clock.real") } }, S.Clock == "real" ? "clock:real" : "clock:fast")));
            var ph = new Block(BK.ButtonRow); ph.Add(Block.Btn(S_("set.photoBtn"), "photo:go"));
            r.Add(SetCard(S_("set.photo"), S_("set.photoHint"), ph));
            r.Add(new Block(BK.Card) { Style = "set more" }.WithBtn(Block.Btn(moreOpen ? S_("grow.fold") : S_("set.more"), "setmore", "wide", false, moreOpen)));
            if (moreOpen)
            {
                r.Add(SetCard(S_("set.wake"), Host.WakeSupported ? S_("set.wakeHint") : S_("set.wakeNo"), SegOf(new[] { new[] { "wake:on", S_("seg.on") }, new[] { "wake:off", S_("seg.off") } }, ExtraBool("wake") ? "wake:on" : "wake:off")));
                var tip = new Block(BK.Card) { Style = "set", Text = S_("set.tips"), Sub = S_("set.tipsHint") };
                tip.Add(Block.Btn(tipsOpen ? S_("grow.fold") : S_("set.tipsBtn"), "tips", "wide", false, tipsOpen));
                if (tipsOpen) for (int i = 0; i < Str.Tips.Count; i++) tip.Add(new Block(BK.Row) { Style = "tiprow", Tag = (i + 1).ToString(), Text = Str.Tips[i].Text });
                r.Add(tip);
                var sv = new Block(BK.Card) { Style = "set", Text = S_("set.save"), Sub = S_("set.saveHint") };
                var a1 = new Block(BK.ButtonRow); a1.Add(Block.Btn(S_("set.expCode"), "exp:copy")); a1.Add(Block.Btn(S_("set.expFile"), "exp:file")); sv.Add(a1);
                sv.Add(new Block(BK.Input) { Text = impText, Sub = S_("set.impHint"), Act = "impText" });
                var a2 = new Block(BK.ButtonRow); a2.Add(Block.Btn(S_("set.openFile"), "imp:file")); a2.Add(Block.Btn(impArmed ? S_("set.impArmed") : S_("set.imp"), "imp:go", impArmed ? "main" : "btn")); sv.Add(a2);
                if (impMsg != "") sv.Add(Block.N(impMsg, "small"));
                r.Add(sv);
            }
            return r;
        }

        // ----- 저장 옮기기 -----
        string impText = "", impMsg = ""; bool impArmed;
        public string ImpText { get => impText; set { impText = value ?? ""; impArmed = false; } }
        void ExportAct(string v)
        {
            if (code == null) return;
            string c = code.Export(S.ToJson()); impArmed = false;
            if (v == "file")
            {
                var d = LocalTime.Of(Ctx.Clock.NowMs, Ctx.Clock);
                Host.SaveTextFile("lighthouse-save-" + d.Year.ToString("0000") + d.Month.ToString("00") + d.Day.ToString("00") + ".txt", c); impMsg = S_("set.savedFile");
            }
            else { impText = c; Host.CopyText(c); impMsg = S_("set.copied"); }
        }
        public void ImportFileRead(string text) { impText = (text ?? "").Trim(); impArmed = false; impMsg = S_("set.fileRead"); }
        void ImportAct()
        {
            if (code == null) return;
            var res = code.Parse(impText);
            if (res.Err != null) { impMsg = res.Err; impArmed = false; return; }
            if (!impArmed)
            {
                impArmed = true;
                double gifts = 0; foreach (var p in ((Newtonsoft.Json.Linq.JObject)res.Data["gifts"]).Properties()) gifts += Js.Num(p.Value);
                impMsg = F_("set.impConfirm", Js.Num(res.Data["stage"]), gifts); return;
            }
            Host.ImportSave(res.Data);
        }
    }
}
