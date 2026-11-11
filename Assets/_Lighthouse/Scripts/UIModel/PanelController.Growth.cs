using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.UI.Model
{
    public sealed partial class PanelController
    {
        // ================= 성장 (원본 renderGrowth) =================
        string NormalizeGrow(string v) => v == "offer" || v == "mile" || (v == "log" && LogbookOn()) ? v : "grow";

        List<Block> BuildGrowth()
        {
            var r = new List<Block>();
            bool lb = LogbookOn();
            GrowSub = NormalizeGrow(GrowSub);
            var seg = Block.Seg(lb);
            foreach (var t in lb ? new[] { "grow", "offer", "mile", "log" } : new[] { "grow", "offer", "mile" })
                seg.Add(Block.Btn(S_("grow.sub." + t), "grow:" + t, "btn", false, GrowSub == t));
            r.Add(seg);
            if (GrowSub == "offer") BuildOffer(r);
            else if (GrowSub == "mile") BuildMiles(r);
            else if (GrowSub == "log") BuildLogbook(r);
            else BuildGrowthMain(r);
            return r;
        }

        double LvAt(int n) => Progress.LvAt(T, n);

        /// <summary>갈래 효과 한 줄(원본 TRACK_EFFECT). 표시용 식이라 일부 계수(7%·메테오)는 표에 없고 여기에 둔다(원본 값 그대로).</summary>
        string TrackEffect(string k, int lv)
        {
            switch (k)
            {
                case "lens":
                    {
                        double meteor = (1 + 0.03 * Math.Min(lv, 16)) * (lv >= 14 ? 2.2 : lv >= 9 ? 1.43 : 1);
                        return F_("eff.lens", Math.Round((Ctx.LensMul() - 1) * 100), (Progress.LensShiny(T, lv) * 100).ToString("0.0", CultureInfo.InvariantCulture), meteor.ToString("0.0", CultureInfo.InvariantCulture));
                    }
                case "garden":
                    {
                        int cap = T.LvCap["garden"];
                        return F_("eff.garden", Math.Min(lv, cap), cap) + (lv > cap ? F_("eff.harvest", Math.Round(Ctx.HarvestChance() * 100)) : "");
                    }
                default:
                    {
                        int cap = T.LvCap["port"];
                        return F_("eff.port", 7 * Math.Min(lv, cap), T.Bal.OfflineMaxBaseHours + Math.Min(lv, T.Bal.OfflineMaxPortHoursCap)) + (lv > cap ? F_("eff.regBonus", Math.Round(Ctx.RegularBonus() * 100)) : "");
                    }
            }
        }

        void BuildGrowthMain(List<Block> r)
        {
            int stage = Ctx.Stage; double goal = T.Goal[stage]; int need = T.DexNeed[stage]; int dex = Ctx.Discovered(), rk = Ctx.RankIdx(), tot = Ctx.TotalLv();
            var nr = rk + 1 < T.Ranks.Count ? T.Ranks[rk + 1] : null;
            var rank = new Block(BK.Card) { Style = "rank", Text = T.Ranks[rk].Name, Sub = S_("grow.rank") };
            rank.Add(Block.N(F_("grow.lvSum", tot) + (nr != null ? F_("grow.toNext", nr.Min - tot) : ""), "small"));
            rank.Add(Block.N(F_("grow.shinyChance", (Ctx.ShinyChance() * 100).ToString("0.0", CultureInfo.InvariantCulture)), "small"));
            r.Add(rank);
            var st = new Block(BK.Card) { Style = "stage", Text = T.StageName[stage], Sub = stage + " / " + T.MaxStage + S_("grow.stageUnit"), Tag = S.Build != null ? S_("grow.building") : null };
            if (goal > 0)
            {
                st.Add(Block.Prog(S_("grow.guided"), F_("grow.ofShips", Math.Min(S.Guided, goal), goal), (float)Math.Min(1, S.Guided / goal)));
                if (need > 0) st.Add(Block.Prog(S_("grow.dexNeed"), F_("grow.ofKinds", Math.Min(dex, need), need), (float)Math.Min(1, dex / (double)need), "thin"));
                st.Add(Block.N(F_("grow.nextStage", T.StageName[stage + 1]), "small"));
            }
            else st.Add(Block.N(S_("grow.maxStage"), "small"));
            r.Add(st);
            var role = new Block(BK.Card) { Style = "rolecard", Text = S_("grow.roles"), Sub = roleOpen ? S_("grow.fold") : S_("grow.see"), Act = "rolebox", On = roleOpen };
            if (roleOpen) BuildRoles(role);
            r.Add(role);
            r.Add(Block.H(S_("grow.focusTitle")));
            r.Add(Block.N(S_("grow.focusNote")));
            var seg = Block.Seg();
            seg.Add(Block.Btn(S_("seg.auto"), "focus:auto", "btn", false, S.Focus == "auto" || !T.TrackById.ContainsKey(S.Focus)));
            foreach (var t in T.Tracks) seg.Add(Block.Btn(t.Name, "focus:" + t.Id, "btn", false, S.Focus == t.Id));
            r.Add(seg);
            foreach (var t in T.Tracks)
            {
                int lv = Ctx.LvlOf(t.Id); double pts = GameContext.Get(S.Growth, t.Id), nxt = LvAt(lv + 1), prev = lv > 0 ? LvAt(lv) : 0;
                bool open = trkOpen.TryGetValue(t.Id, out bool o) && o;
                var card = new Block(BK.Row) { Style = "track" + (S.Focus == t.Id ? " focus" : ""), Icon = "track:" + t.Id, Text = t.Name, Tag = "Lv." + lv, Sub = t.Desc, Value = (float)Math.Min(1, (pts - prev) / (nxt - prev)) };
                card.Extra = TrackEffect(t.Id, lv) + " · " + F_("grow.nextLv", nxt - pts);
                var nm = Progress.NextMile(T, t.Id, lv);
                if (nm != null) card.Count = F_("grow.nextReward", nm.Lv, nm.Text);
                card.Add(Block.Btn(open ? S_("grow.fold") : S_("grow.detail"), "trk:" + t.Id, "btn"));
                card.Open = open;
                r.Add(card);
            }
        }

        void BuildRoles(Block parent)
        {
            int tier = Tier();
            foreach (var ro in Str.Roles)
            {
                bool on = tier >= ro.Stage;
                parent.Add(new Block(BK.Row) { Style = "role", Locked = !on, Tag = on ? S_("grow.inUse") : StageFor(ro.Stage) + S_("grow.stageUnit"), Text = ro.Name, Sub = ro.Desc });
            }
            var st = S.Stats;
            double V(string k) => GameContext.Get(st, k);
            var rec = new[] { Tuple.Create("reef", V("reef"), "unit.times"), Tuple.Create("fog", V("fog"), "unit.times"), Tuple.Create("rescued", V("rescued"), "unit.ship"), Tuple.Create("dock", V("dock"), "unit.ship"), Tuple.Create("rain", V("rain"), "unit.nth"), Tuple.Create("drift", V("drift"), "unit.piece"), Tuple.Create("wish", V("wish"), "unit.nth"), Tuple.Create("fished", V("fished"), "unit.nth"), Tuple.Create("fed", V("fed"), "unit.nth"), Tuple.Create("stars", V("stars"), "unit.nth") };
            parent.Add(Block.H(S_("grow.records")));
            var stats = new Block(BK.Stats);
            foreach (var x in rec) stats.Add(new Block(BK.Cell) { Text = S_("stat." + x.Item1), Sub = Fmt(x.Item2) + S_(x.Item3) });
            parent.Add(stats);
        }

        void BuildMiles(List<Block> r)
        {
            r.Add(Block.N(S_("mile.note")));
            foreach (var t in T.Tracks)
            {
                int lv = Ctx.LvlOf(t.Id);
                r.Add(Block.H(t.Name + " · Lv." + lv));
                var nx = Progress.NextMile(T, t.Id, lv);
                foreach (var m in T.Milestones.Where(x => x.K == t.Id))
                    r.Add(new Block(BK.Row) { Style = "role", Locked = m.Lv > lv, Tag = "Lv." + m.Lv, Text = m.Text, Sub = m.Lv <= lv ? S_("mile.open") : F_("mile.more", m.Lv - lv) + (nx == m ? S_("mile.next") : "") });
            }
        }

        void BuildLogbook(List<Block> r)
        {
            double days = Ctx.DaysSinceStart();
            string topK = null; double topN = 0;
            foreach (var g in T.Gifts) { double n = GameContext.Get(S.Totals, g.Id); if (n > 0 && (topK == null || n > topN)) { topK = g.Id; topN = n; } }
            int regs = T.Regulars.Count(x => S.Reg.ContainsKey(x.Id)), ships = T.Ships.Count(x => S.Dex.ContainsKey(x.Id));
            r.Add(new Block(BK.Heading) { Text = S_("log.title"), Icon = "logbook" });
            var c1 = new Block(BK.Card) { Style = "two" };
            c1.Add(new Block(BK.Cell) { Text = S_("log.guided"), Sub = Fmt(S.Guided) + S_("log.ship") });
            c1.Add(new Block(BK.Cell) { Text = S_("log.days"), Sub = Fmt(days) + S_("log.day") });
            r.Add(c1);
            var c2 = new Block(BK.Card) { Text = S_("log.topGift") };
            if (topK != null) { c2.Add(new Block(BK.Row) { Style = "lbg", Icon = topK, Text = T.GiftById[topK].Name, Sub = "×" + Fmt(topN) }); c2.Add(Block.N(S_("log.topNote"), "small")); }
            else c2.Add(Block.N(S_("log.none"), "small"));
            r.Add(c2);
            var c3 = new Block(BK.Card) { Style = "two" };
            c3.Add(new Block(BK.Cell) { Text = S_("log.regs"), Sub = regs + S_("log.person") });
            c3.Add(new Block(BK.Cell) { Text = S_("log.ships"), Sub = ships + S_("log.kind") });
            r.Add(c3);
            var d = LocalTime.Of((long)S.StartAt, Ctx.Clock);
            r.Add(new Block(BK.Card) { Text = S_("log.first"), Sub = F_("fmt.year", d.Year) + " " + FmtDate(S.StartAt) });
            r.Add(Block.H(S_("log.rewards")));
            foreach (var m in T.Milestones.Where(x => x.SkinLayer != null))
            {
                var sk = T.SkinLayerById[m.SkinLayer].Skins[m.SkinId]; bool ok = Ctx.LvlOf(m.K) >= m.Lv;
                r.Add(new Block(BK.Card) { Style = "rw", Locked = !ok, Text = sk.Name, Sub = ok ? S_("mile.open") : F_("log.at", TrackName(m.K), m.Lv) });
            }
            var fr = T.SkinLayerById["ship"].Skins["friends"];
            r.Add(new Block(BK.Card) { Style = "rw", Locked = S.Finale <= 0, Text = fr.Name, Sub = S.Finale > 0 ? S_("mile.open") : S_("log.finale") });
            r.Add(Block.Btn(S_("log.seeDex"), "godex", "tbtn"));
        }

        // ================= 헌정 (원본 renderOffer) =================
        void BuildOffer(List<Block> r)
        {
            string tg = Ctx.OfferTarget(); bool auto = S.AutoOffer;
            var setc = new Block(BK.Card) { Style = "set", Text = S_("offer.auto"), Sub = F_("offer.autoHint", T.Bal.AutoOfferKeep, T.Bal.AutoOfferShinyKeep) };
            var seg = Block.Seg();
            seg.Add(Block.Btn(S_("seg.on"), "autooffer:on", "btn", false, auto)); seg.Add(Block.Btn(S_("seg.off"), "autooffer:off", "btn", false, !auto));
            setc.Add(seg); r.Add(setc);
            r.Add(Block.H(S_("offer.where")));
            var to = Block.Seg(true);
            to.Add(Block.Btn(S_("offer.fit"), "offerto:auto", "btn", false, tg == "auto"));
            foreach (var t in T.Tracks) to.Add(Block.Btn(t.Name, "offerto:" + t.Id, "btn", false, tg == t.Id));
            r.Add(to);
            r.Add(Block.N(S_("offer.fitNote")));
            r.Add(Block.H(S_("offer.mine")));
            int rows = 0;
            foreach (var g in T.Gifts)
            {
                double s = GameContext.Get(S.Shiny, g.Id), n = Ctx.SpareOf(g.Id);
                foreach (var pair in new[] { Tuple.Create(true, s), Tuple.Create(false, n) })
                {
                    bool shiny = pair.Item1; double c = pair.Item2;
                    if (c <= 0) continue;
                    string tr = tg == "auto" ? Ctx.AutoTrack(g.Id) : tg;
                    double pts = Ctx.GiftPts(g.Id, tr, shiny, tg == "auto");
                    string key = shiny ? "offerS" : "offer";
                    var row = new Block(BK.Row) { Style = "offer", Shiny = shiny, Icon = g.Id, Text = (shiny ? "✦ " : "") + g.Name + " ×" + Fmt(c), Sub = F_("offer.pts", TrackName(tr), pts) };
                    row.Add(Block.Btn(S_("offer.one"), key + ":" + g.Id)); row.Add(Block.Btn(S_("offer.all"), key + "all:" + g.Id));
                    r.Add(row); rows++;
                }
            }
            if (rows == 0) r.Add(Block.N(S_("offer.none")));
        }
    }
}
