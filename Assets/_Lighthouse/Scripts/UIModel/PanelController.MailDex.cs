using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.UI.Model
{
    public sealed partial class PanelController
    {
        // ================= 편지함 (원본 renderMail / renderLetter) =================
        string[] KeepTag(Letter l)
        {
            // [글자, 접근 이름, 아이콘, 색 종류]
            if (l.Reg != null) return new[] { "reg", "chipReg" };
            string k = l.Keep != null && l.Keep.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)l.Keep : null;
            switch (k)
            {
                case "home": return new[] { "home", "adMail" };
                case "up": return new[] { "up", "chipUp" };
                case "finale": return new[] { "finale", "star" };
                case "season": return new[] { "season", null };
                case "anniv": return new[] { "anniv", null };
                case "nature": return new[] { "nature", null };
            }
            return null;
        }

        string LetterName(Letter l)
            => !string.IsNullOrEmpty(l.Nm) ? l.Nm : !string.IsNullOrEmpty(l.Ship) ? l.Ship + S_("mail.shipSuffix") + (!string.IsNullOrEmpty(l.Cap) ? " · " + l.Cap : "") : !string.IsNullOrEmpty(l.Cap) ? l.Cap : S_("mail.unknownShip");

        static string LetterPreview(Letter l)
        {
            string t = (l.Text ?? "").Replace("<br>", " ");
            return t.Length > 34 ? t.Substring(0, 34) + "…" : t;
        }

        List<Block> BuildMail()
        {
            var r = new List<Block>();
            var letters = S.Letters.Where(l => l != null && l.Text != null).ToList();
            if (letters.Count == 0)
            {
                r.Add(new Block(BK.Empty) { Icon = "mail8", Text = S_("mail.empty"), Sub = S_("mail.emptySub") });
                return r;
            }
            int un = letters.Count(l => !l.Read);
            var sum = new Block(BK.Summary) { Text = MailN(letters.Count), Sub = S_("mail.unit") + MailMore(letters.Count) + (un > 0 ? " · " + S_("mail.unread") + " " + MailN(un) + S_("mail.unit") + MailMore(un) : " · " + S_("mail.allRead")) };
            if (un > 0) sum.Add(Block.Btn(S_("mail.readAll"), "readall"));
            r.Add(sum);
            foreach (var l in letters)
            {
                try { r.Add(LetterBlock(l)); }
                catch (Exception) { r.Add(new Block(BK.Letter) { Text = S_("mail.broken"), Icon = "mail", Act = "letter:" + l.Id.ToString("0", CultureInfo.InvariantCulture), New = false, Open = false }); }      // 한 통을 그리다 예외가 나도 그 편지만 대체 줄로(원본 QA-402)
            }
            if (S.LettersTrimmed) r.Add(Block.N(S_("mail.trimmed"), "qnote"));
            return r;
        }

        Block LetterBlock(Letter l)
        {
            bool open = OpenLetters.Contains(l.Id);
            T.GiftById.TryGetValue(l.Gift ?? "", out var g);
            var tag = KeepTag(l);
            var b = new Block(BK.Letter)
            {
                Text = LetterName(l), Act = "letter:" + l.Id.ToString("0", CultureInfo.InvariantCulture), New = !l.Read, Open = open,
                Date = FmtDate(l.T), Icon = g != null ? l.Gift : "mail", Tag = tag != null ? S_("tag." + tag[0]) : null,
                Style = tag != null ? tag[0] : null
            };
            if (open)
            {
                b.Sub = (l.Text ?? "").Replace("<br>", "\n");
                if (!string.IsNullOrEmpty(l.Sign)) b.Extra = l.Sign;
                if (g != null) b.Count = (l.Shiny == true ? S_("mail.shinyPre") : "") + g.Name + S_("mail.gift");
                if (l.Reg != null || l.KeepTruthy) b.Date = (b.Date ?? "") ;
                b.Value = (l.Reg != null || l.KeepTruthy) ? 1f : -1f;      // 1 이면 "이 편지는 남겨 둬요." 줄
            }
            else b.Sub = LetterPreview(l);
            return b;
        }

        // ================= 도감 (원본 renderDex) =================
        List<Block> BuildDex()
        {
            var r = new List<Block>();
            var gk = T.Gifts.Where(g => InDexRegion(null)).ToList();      // 선물에는 지역 키가 없어 home 으로 센다
            var sk = T.Ships.Where(sh => InDexRegion(null)).ToList();
            var regs = T.Regulars.Where(rg => InDexRegion(rg.Region)).ToList();
            bool Found(string k) => S.Found.ContainsKey(k);
            int gotG = gk.Count(g => Found(g.Id)), gotH = gk.Count(g => S.Shiny.ContainsKey(g.Id)), gotR = regs.Count(x => S.Reg.ContainsKey(x.Id)), gotS = sk.Count(x => S.Dex.ContainsKey(x.Id));
            int tot = gk.Count * 2 + regs.Count + sk.Count, got = gotG + gotH + gotR + gotS, pct = (int)JsMath.Round((double)got / tot * 100);
            bool cover = S.Finale > 0;
            int lay = cover ? Math.Max(0, Math.Min(5, (int)Math.Floor((Ctx.TotalLv() - T.Ranks[T.Ranks.Count - 1].Min) / 100.0))) : 0;
            int need = T.DexNeed[Ctx.Stage], dx = Ctx.Discovered();
            var stamps = Ctx.NatureStamps();
            var sum = new Block(BK.Summary) { Style = cover ? "cover" : null, Text = pct + "%", Sub = F_("dex.sum", got, tot), Value = pct / 100f, Count = lay > 0 ? lay.ToString(CultureInfo.InvariantCulture) : null };
            if (cover) sum.Extra = S_("dex.cover") + (lay > 0 ? " " + new string('✦', lay) : "");
            if (need > 0) sum.Add(Block.N(F_("dex.need", Math.Min(dx, need), need), "small"));
            if (stamps.Count > 0) sum.Add(Block.N(S_("dex.stamp") + " " + string.Join(" · ", stamps.Select(s => s[0] + " " + s[1])), "small"));
            r.Add(sum);

            var open = RegionsOpen();
            if (open.Count > 1)
            {
                var seg = Block.Seg(true);
                foreach (var id in open) seg.Add(Block.Btn(T.RegionById[id].Name, "dexrg:" + id, "btn", false, DexRegion() == id));
                r.Add(seg);
            }
            var chips = Block.Seg(true); chips.Style = "dx";
            var seaTot = T.Sea.Count; int seaGot = T.Sea.Count(x => SeaN(x.Id) > 0);
            foreach (var c in new[] { Tuple.Create("gift", gotG, gk.Count), Tuple.Create("shine", gotH, gk.Count), Tuple.Create("reg", gotR, regs.Count), Tuple.Create("ship", gotS, sk.Count), Tuple.Create("sea", seaGot, seaTot) })
                chips.Add(new Block(BK.Button) { Text = S_("dex.chip." + c.Item1), Sub = c.Item2 + "/" + c.Item3, Act = "dex:" + c.Item1, On = DexSub == c.Item1, Style = "btn" });
            r.Add(chips);

            if (DexSub == "gift")
            {
                var grid = new Block(BK.Grid);
                foreach (var g in gk)
                {
                    bool own = Found(g.Id); double n = GameContext.Get(S.Gifts, g.Id), s = GameContext.Get(S.Shiny, g.Id);
                    string when = GiftWhen(g);
                    var cell = new Block(BK.Cell) { Icon = g.Id, Text = own ? g.Name : "???", Locked = !own, Count = own ? "×" + n : null };
                    if (!string.IsNullOrEmpty(when)) cell.Tag = g.Season != null ? SeasonName(g.Season) : g.Cond != null ? CondShort(g.Cond) : StageFor(g.Tier) + S_("dex.stageSuffix");
                    var subs = new List<string>();
                    if (s > 0) subs.Add("✦ " + s);
                    if (!own && !string.IsNullOrEmpty(when)) subs.Add(F_("dex.meetAt", when));
                    cell.Sub = subs.Count > 0 ? string.Join("\n", subs) : null;
                    cell.Shiny = false;
                    grid.Add(cell);
                }
                r.Add(grid);
            }
            else if (DexSub == "shine")
            {
                r.Add(Block.N(S_("dex.shineNote")));
                var grid = new Block(BK.Grid);
                foreach (var g in gk)
                {
                    double s = GameContext.Get(S.Shiny, g.Id);
                    grid.Add(new Block(BK.Cell) { Icon = g.Id, Text = s > 0 ? g.Name : "???", Locked = s <= 0, Shiny = true, Count = s > 0 ? "×" + s : null });
                }
                r.Add(grid);
            }
            else if (DexSub == "reg") BuildRegulars(r);
            else if (DexSub == "sea")
            {
                r.Add(Block.N(S_("dex.seaNote")));
                foreach (var grp in T.SeaGroups)
                {
                    var c = Ctx.SeaGrpCount(grp.Id);
                    r.Add(Block.H(grp.Name + " " + c[0] + "/" + c[1]));
                    var grid = new Block(BK.Grid);
                    foreach (var v in T.Sea.Where(x => x.Grp == grp.Id))
                    {
                        double n = SeaN(v.Id);
                        grid.Add(new Block(BK.Cell) { Icon = v.Icon, Text = n > 0 ? v.Name : "???", Locked = n <= 0, Shiny = v.Rare, Tag = v.Tag, Count = n > 0 && v.Count != 0 ? "×" + n : null, Sub = n > 0 ? v.Where : v.Hint });
                    }
                    r.Add(grid);
                }
            }
            else
            {
                foreach (var sh in T.Ships.Where(x => InDexRegion(null)))
                {
                    double n = GameContext.Get(S.Dex, sh.Id);
                    int st = sh.St != 0 ? sh.St : StageFor(sh.Stage);
                    if (n > 0) r.Add(new Block(BK.Row) { Style = "ship", Icon = "ship:" + sh.Id, Text = sh.Name, Sub = F_("dex.guidedTimes", n) });
                    else r.Add(new Block(BK.Row) { Style = "ship", Locked = true, Icon = "ship:" + sh.Id, Text = "???", Sub = st > Ctx.Stage ? st + S_("dex.stageMeet") : S_("dex.notYet") });
                }
            }
            return r;
        }

        double SeaN(string k) { double n = GameContext.Get(S.Sea, k); return !double.IsNaN(n) && !double.IsInfinity(n) && n > 0 ? Math.Floor(n) : 0; }
        string CondShort(string c) => T.Bal.CondShort.TryGetValue(c, out var s) ? s : c;
        string GiftWhen(GiftDef g)
        {
            if (g.Season != null) return SeasonName(g.Season);
            if (g.Cond != null) return T.Bal.CondName.TryGetValue(g.Cond, out var n) ? n : g.Cond;
            if (g.Tier != 0) return StageFor(g.Tier) + S_("dex.stageAfter");
            return "";
        }

        void BuildRegulars(List<Block> r)
        {
            var visit = T.Bal.VisitAt;
            foreach (var rg in T.Regulars.Where(x => InDexRegion(x.Region)))
            {
                double v = Ctx.RegVisits(rg.Id);
                if (v > 0)
                {
                    int hearts = visit.Count(n => v >= n); int? next = visit.Where(n => n > v).Select(n => (int?)n).FirstOrDefault();
                    int prev = visit.Where(n => n <= v).DefaultIfEmpty(0).Last();
                    bool open = hearts > 0 && openRegs.Contains(rg.Id);
                    var row = new Block(BK.Row) { Style = "reg", Icon = "ship:" + rg.Type, Text = rg.Ship + S_("mail.shipSuffix") + " · " + rg.Cap, Extra = new string('♥', hearts) + new string('♡', visit.Length - hearts) };
                    row.Sub = F_("dex.visits", v) + " · " + (next.HasValue ? F_("dex.nextStory", next.Value - v) : S_("dex.allStories"));
                    if (next.HasValue) row.Value = (float)Math.Min(1, (v - prev) / (double)(next.Value - prev));
                    if (hearts > 0) row.Add(Block.Btn(open ? S_("dex.foldStory") : S_("dex.readStory"), "regread:" + rg.Id, "tbtn"));
                    r.Add(row);
                    if (open)
                        for (int i = 0; i < hearts && i < rg.Story.Length; i++)
                            r.Add(new Block(BK.Note) { Style = "story", Text = rg.Story[i].Replace("<br>", "\n"), Sub = F_("dex.nthVisit", visit[i]) });
                }
                else r.Add(new Block(BK.Row) { Style = "ship", Locked = true, Icon = null, Text = "???", Sub = rg.Stage > Tier() ? StageFor(rg.Stage) + S_("dex.stageMeet") : S_("dex.notYet") });
            }
        }

        // ================= 그동안의 소식 (원본 renderNews) =================
        List<Block> BuildNews()
        {
            var r = new List<Block>(); var n = News?.R ?? new OfflineResult();
            r.Add(Block.N(n.Capped ? F_("news.leadCapped", n.MaxH != 0 ? n.MaxH : Math.Round(Ctx.OfflineMax() / 3600), n.Count) : F_("news.lead", FmtAway(n.Away), n.Count), "lead"));
            if (n.First.HasValue)
            {
                var l = S.Letters.FirstOrDefault(x => x.Id == n.First.Value);
                if (l != null) { var lb = LetterBlock(l); lb.Act = "gomail:" + l.Id.ToString("0", CultureInfo.InvariantCulture); lb.Style = "fcard"; lb.Open = false; lb.Sub = LetterPreview(l); r.Add(lb); r.Add(Block.N(S_("news.firstReg"), "small")); }
            }
            if (!string.IsNullOrEmpty(n.Home) && T.GiftById.TryGetValue(n.Home, out var hg)) r.Add(Block.N(F_("news.home", hg.Name), "lead"));
            if (n.RegStories > 0) r.Add(Block.N(S_("news.regStories")));
            double tg = n.Gifts.Values.Sum(), ts = n.Shiny.Values.Sum();
            r.Add(Block.N(F_("news.gifts", tg + ts, ts) + (S.AutoOffer ? S_("news.autoOffer") : "")));
            var grid = new Block(BK.Grid); grid.Style = "mini";
            foreach (var kv in n.Gifts) grid.Add(new Block(BK.Cell) { Icon = kv.Key, Text = T.GiftById.TryGetValue(kv.Key, out var gg) ? gg.Name : kv.Key, Count = "×" + kv.Value });
            foreach (var kv in n.Shiny) grid.Add(new Block(BK.Cell) { Icon = kv.Key, Text = T.GiftById.TryGetValue(kv.Key, out var gg2) ? gg2.Name : kv.Key, Count = "✦" + kv.Value, Shiny = true });
            r.Add(grid);
            r.AddRange(BuildNow());
            if (n.Growth.Count > 0)
            {
                r.Add(Block.H(S_("news.grew")));
                foreach (var g in n.Growth) r.Add(new Block(BK.Row) { Style = "gline", Text = g.Name, Sub = "Lv." + g.From + " → Lv." + g.To });
            }
            if (n.Diary.Count > 0)
            {
                r.Add(Block.H(S_("news.diary")));
                foreach (var d in n.Diary) r.Add(new Block(BK.Row) { Style = "diary", Icon = d.Gift, Text = d.Parts != null ? d.Parts[0] + " " + d.Parts[1] : d.At, Sub = d.Text });
            }
            var acts = new Block(BK.ButtonRow) { Style = "sticky" };
            if (n.Letters > 0) acts.Add(Block.Btn(S_("news.readLetters"), "read", "main"));
            acts.Add(Block.Btn(S_("news.ok"), "ok", n.Letters > 0 ? "btn" : "main"));
            r.Add(acts);
            return r;
        }

        List<Block> BuildNow()
        {
            var r = new List<Block>();
            int un = Ctx.UnreadCount(); int nw = (int)Math.Max(0, Ctx.Discovered() - S.DexSeen);
            var visit = T.Bal.VisitAt; (string name, int n)? next = null;
            foreach (var rg in T.Regulars)
            {
                double v = Ctx.RegVisits(rg.Id);
                int? nx = v > 0 ? visit.Where(m => m > v).Select(m => (int?)m).FirstOrDefault() : null;
                if (nx.HasValue && (!next.HasValue || nx.Value - v < next.Value.n)) next = (rg.Cap, (int)(nx.Value - v));
            }
            bool metAny = T.Regulars.Any(rg => Ctx.RegVisits(rg.Id) > 0), allDone = T.Regulars.All(rg => Ctx.RegVisits(rg.Id) >= visit[visit.Length - 1]);
            r.Add(Block.H(S_("now.title")));
            r.Add(new Block(BK.Row) { Style = "gline", Text = un > 0 ? F_("now.unread", MailN(un), MailMore(un)) : S_("mail.allRead") }.WithBtn(un > 0 ? Block.Btn(S_("now.read"), "read", "tbtn") : null));
            r.Add(new Block(BK.Row) { Style = "gline", Text = nw > 0 ? F_("now.newDex", nw) : S_("now.noDex") }.WithBtn(nw > 0 ? Block.Btn(S_("now.see"), "godex", "tbtn") : null));
            r.Add(new Block(BK.Row) { Style = "gline", Text = next.HasValue ? F_("now.nextStory", next.Value.name, next.Value.n) : allDone ? S_("now.storiesDone") : metAny ? S_("now.storiesDone2") : S_("now.noReg") });
            return r;
        }
    }

    static class BlockExt
    {
        public static Block WithBtn(this Block b, Block btn) { if (btn != null) b.Add(btn); return b; }
    }
}
