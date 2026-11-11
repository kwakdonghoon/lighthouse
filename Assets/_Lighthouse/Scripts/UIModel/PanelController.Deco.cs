using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.UI.Model
{
    public sealed partial class PanelController
    {
        // ----- 꾸미기 상태(원본 decoSel 등) -----
        string decoSel, decoOpenItem, decoNote = "", decoLive = "";
        bool decoPeek, decoConfirm, migShow;
        readonly List<List<string[]>> decoUndo = new List<List<string[]>>(), decoRedo = new List<List<string[]>>();     // 한 동작 = [자리, 이전, 이후] 목록
        public string DecoSel => decoSel;
        public bool DecoPeek => decoPeek;
        public int UndoCount => decoUndo.Count;

        /// <summary>미리 입기(D21): 구매하지 않은 외형을 임시로 입혀 본다. 저장하지 않는다.</summary>
        public string[] SkinPreview;        // [층, id]
        public int DecorRev;                // 장식이 바뀔 때마다 오른다(화면이 섬을 다시 굽는 신호)
        public void ClearPreview() { if (SkinPreview != null) { SkinPreview = null; Host.WorldDirty(); } }

        // ================= 규칙 도우미 =================
        int GardenLv => Ctx.LvlOf("garden");
        bool SlotNowOpen(string id) => T.DecorSlotById.TryGetValue(id, out var sl) && DecorRules.SlotOpen(sl, Ctx.Stage, GardenLv);
        List<string> ZoneOpenIds(string z) => DecorRules.ZoneOpen(T, z, Ctx.Stage, GardenLv).Select(s => s.Id).ToList();
        List<string> ZoneSlotIds(string z) => DecorRules.ZoneSlots(T, z).Select(s => s.Id).ToList();
        string PutOf(string slot) => DecorRules.PutOf(T, S, slot);
        IEnumerable<DecorItemDef> Catalog() => T.DecorItems.Where(i => i.Phase == "GL");
        string ZoneLong(string z) => T.DecorZones.First(q => q.Id == z).Title;
        string ZoneChip(string z) => T.DecorZones.First(q => q.Id == z).Chip;
        string SlotLabel(string id) => F_("deco.slotLabel", ZoneLong(T.DecorSlotById[id].Chip), T.DecorSlotById[id].Num);
        string SizeWord(string size) => S_("size." + size);
        string GaI(double n) => "2459".Contains(((long)n % 10).ToString()) ? S_("josa.ga") : S_("josa.i");

        string DecorNeedText(string id, bool lng)
        {
            var it = T.DecorItemById[id];
            string[] ord = S_("deco.ord").Split((char)44);
            switch (it.Src)
            {
                case "earn": return lng ? F_("deco.needEarnL", TrackName(it.NeedKey), it.NeedVal, GaI(it.NeedVal)) : F_("deco.needEarn", TrackName(it.NeedKey), it.NeedVal);
                case "story": return lng ? F_("deco.needStoryL", it.Who, ord[(int)it.NeedVal - 1]) : F_("deco.needStory", it.Who, ord[(int)it.NeedVal - 1]);
                case "season": return lng ? F_("deco.needSeasonL", SeasonName(it.NeedKey)) : F_("deco.needSeason", SeasonName(it.NeedKey));
                case "gift": return lng ? F_("deco.needGiftL", T.GiftById[it.NeedKey].Name, it.NeedVal) : F_("deco.needGift", T.GiftById[it.NeedKey].Name, it.NeedVal);
            }
            return "";
        }

        string DecorSlotNeed(string id, bool lng)
        {
            var sl = T.DecorSlotById[id];
            if (sl.OpenLv != 0) return lng ? F_("deco.slotNeedLvL", sl.OpenLv, GaI(sl.OpenLv)) : F_("deco.slotNeedLv", sl.OpenLv);
            return lng ? F_("deco.slotNeedStageL", sl.OpenStage) : F_("deco.slotNeedStage", sl.OpenStage);
        }

        int PlacedTotal() => S.Isle.Put.Keys.Count(id => T.DecorSlotById.ContainsKey(id) && PutOf(id) != null && SlotNowOpen(id));

        // ----- 한 동작(원본 decoDo/decoApply) -----
        void DecoApply(List<string[]> changes, int dir)
        {
            foreach (var c in changes) { string to = dir > 0 ? c[2] : c[1]; if (to != null) S.Isle.Put[c[0]] = to; else S.Isle.Put.Remove(c[0]); }
            DecorRev++; Ctx.NotifyChanged(); Host.WorldDirty();
        }
        bool DecoDo(List<string[]> changes)
        {
            changes = changes.Where(c => c[1] != c[2]).ToList();
            if (changes.Count == 0) return false;
            DecoApply(changes, 1); decoUndo.Add(changes); if (decoUndo.Count > 200) decoUndo.RemoveAt(0); decoRedo.Clear();
            return true;
        }

        /// <summary>어울리게 놓아 줘요: 지금 구역의 빈 열린 자리만, 큰 자리부터. 무작위 없음.</summary>
        public sealed class AutoPlan { public bool Can; public string Reason; public List<string[]> Changes; }
        public AutoPlan DecoAutoPlan()
        {
            string z = DecoZone; var empties = ZoneOpenIds(z).Where(id => PutOf(id) == null).ToList();
            if (empties.Count == 0) return new AutoPlan { Reason = T.Lint.DecoMsg["noSlot"] };
            var cands = Catalog().Where(i => i.Zones.Contains(z) && DecorRules.Owned(T, S, i.Id, GardenLv)).ToList();
            var counts = new Dictionary<string, int>(); var changes = new List<string[]>();
            foreach (var id in S.Isle.Put.Keys.ToList()) { var it = PutOf(id); if (it != null && T.DecorSlotById.ContainsKey(id) && SlotNowOpen(id)) counts[it] = (counts.TryGetValue(it, out int c0) ? c0 : 0) + 1; }
            foreach (var id in empties.OrderByDescending(i => T.SizeRank[T.DecorSlotById[i].Size]).ThenBy(i => T.DecorSlotById[i].Num))
            {
                int sr = T.SizeRank[T.DecorSlotById[id].Size];
                var fit = cands.Where(k => T.SizeRank[k.Size] <= sr).ToList();
                if (fit.Count == 0) continue;
                int top = fit.Max(k => T.SizeRank[k.Size]); var pool = fit.Where(k => T.SizeRank[k.Size] == top).ToList();
                var best = pool[0];
                foreach (var k in pool) if ((counts.TryGetValue(k.Id, out int a) ? a : 0) < (counts.TryGetValue(best.Id, out int b) ? b : 0)) best = k;
                counts[best.Id] = (counts.TryGetValue(best.Id, out int cc) ? cc : 0) + 1;
                changes.Add(new[] { id, null, best.Id });
            }
            return changes.Count > 0 ? new AutoPlan { Can = true, Changes = changes } : new AutoPlan { Reason = T.Lint.DecoMsg["noItem"] };
        }

        // ================= 입력(원본 decoAct) =================
        bool DecoAct(string k, string v)
        {
            string[] keys = { "zone", "slot", "slotlock", "put", "clear", "undo", "redo", "auto", "clearall", "peek", "decoopen", "phclose" };
            if (!keys.Contains(k)) return false;
            if (Mode != PanelMode.Deco || DecoTab != "place") return true;
            decoNote = ""; decoLive = "";
            if (k != "decoopen") decoOpenItem = null;
            if (k != "clearall") decoConfirm = false;
            switch (k)
            {
                case "zone":
                    if (!T.DecorZones.Any(z => z.Id == v) || ZoneOpenIds(v).Count == 0) return true;
                    DecoZone = v; decoSel = null; Host.Sfx("tick"); break;
                case "slot":
                    if (!T.DecorSlotById.ContainsKey(v) || !SlotNowOpen(v)) return true;
                    decoSel = decoSel == v ? null : v; Host.Sfx("tick"); break;
                case "slotlock":
                    if (T.DecorSlotById.TryGetValue(v, out var sl) && !SlotNowOpen(v) && !sl.Blocked) { decoNote = DecorSlotNeed(v, true); Host.Sfx("tick"); }
                    break;
                case "put":
                    {
                        string id = decoSel;
                        if (id == null || !T.DecorItemById.TryGetValue(v, out var item) || !item.Zones.Contains(T.DecorSlotById[id].Chip) || DecorRules.State(T, S, v, id, GardenLv) != "ok") return true;
                        if (DecoDo(new List<string[]> { new[] { id, PutOf(id), v } })) { Host.Sfx("pick"); decoLive = FixJosa(F_("deco.placed", item.Name)); }
                        var list = ZoneOpenIds(T.DecorSlotById[id].Chip); string next = list.Skip(list.IndexOf(id) + 1).FirstOrDefault(s => PutOf(s) == null);
                        decoSel = next;
                        break;
                    }
                case "clear":
                    {
                        string id = decoSel, cur = id != null ? PutOf(id) : null;
                        if (cur != null && DecoDo(new List<string[]> { new[] { id, cur, null } })) { Host.Sfx("tick"); decoLive = FixJosa(F_("deco.cleared", T.DecorItemById[cur].Name)); }
                        break;
                    }
                case "phclose": decoSel = null; break;
                case "undo":
                case "redo":
                    {
                        var from = k == "undo" ? decoUndo : decoRedo; var to = k == "undo" ? decoRedo : decoUndo;
                        if (from.Count > 0)
                        {
                            var c = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
                            DecoApply(c, k == "undo" ? -1 : 1); to.Add(c); Host.Sfx("tick");
                            decoLive = k == "undo" ? T.Lint.DecoMsg["undo"] : T.Lint.DecoMsg["redo"];
                        }
                        break;
                    }
                case "auto":
                    {
                        var au = DecoAutoPlan();
                        if (au.Can && DecoDo(au.Changes)) { decoNote = F_("deco.auto", au.Changes.Count); Host.Sfx("pick"); decoLive = decoNote; }
                        break;
                    }
                case "clearall":
                    decoConfirm = false;
                    if (v == "ask") { if (PlacedTotal() > 0) decoConfirm = true; }
                    else if (v == "yes")
                    {
                        var ch = S.Isle.Put.Keys.Where(id => T.DecorSlotById.ContainsKey(id) && PutOf(id) != null && SlotNowOpen(id)).Select(id => new[] { id, S.Isle.Put[id], (string)null }).ToList();
                        if (ch.Count > 0 && DecoDo(ch)) { Host.Sfx("tick"); decoLive = T.Lint.DecoMsg["all"]; }
                        decoSel = null;
                    }
                    break;
                case "peek": decoPeek = v == "on"; Host.Sfx("tick"); break;
                case "decoopen":
                    if (DecorRules.State(T, S, v, decoSel ?? "F1", GardenLv) == "lock" || !DecorRules.Owned(T, S, v, GardenLv)) decoOpenItem = decoOpenItem == v ? null : v;
                    break;
            }
            return true;
        }

        /// <summary>Esc/뒤로: 확인 → 섬 보기 → 고른 자리 순으로 풀고, 풀 것이 없으면 false(팝업 닫기는 호출한 쪽).</summary>
        public bool DecoEsc()
        {
            if (Mode != PanelMode.Deco || DecoTab != "place") return false;
            if (decoConfirm) { Act("clearall:no"); return true; }
            if (decoPeek) { Act("peek:off"); return true; }
            if (decoSel != null) { Act("phclose"); return true; }
            return false;
        }

        // ================= 꾸미기 화면 (원본 renderDeco/renderPlace/renderPicks/renderSkins) =================
        List<Block> BuildDeco() => DecoTab == "place" ? BuildPlace() : BuildSkins();

        Block DecoSeg()
        {
            var seg = Block.Seg(); seg.Style = "deco";
            seg.Add(Block.Btn(S_("deco.tab.place"), "dtab:place", "btn", false, DecoTab == "place"));
            seg.Add(Block.Btn(S_("deco.tab.skin"), "dtab:skin", "btn", false, DecoTab == "skin"));
            return seg;
        }

        List<Block> BuildPlace()
        {
            var r = new List<Block>();
            var zs = T.DecorZones.Where(z => ZoneOpenIds(z.Id).Count > 0).ToList();
            if (!zs.Any(z => z.Id == DecoZone)) DecoZone = zs[0].Id;
            if (decoSel != null && !(SlotNowOpen(decoSel) && T.DecorSlotById[decoSel].Chip == DecoZone)) decoSel = null;
            string sel = decoSel;
            if (decoPeek)
            {
                r.Add(Block.Btn(S_("deco.peekBack"), "peek:off", "main"));
                r.Add(Block.N(T.Lint.DecoMsg["peek"] + (sel != null ? " " + F_("deco.peekSel", SlotLabel(sel)) : ""), "status"));
                return r;
            }
            if (S.Isle.Mig != 0 && !migShow) { migShow = true; S.Isle.Mig = 0; S.Tips["h8"] = 1; Ctx.NotifyChanged(); }
            r.Add(DecoSeg());
            if (migShow) r.Add(Block.N(S_("deco.migNote"), "status"));
            r.Add(Block.N(S_("deco.intro")));
            var zones = new Block(BK.Seg) { Style = "zones" };
            foreach (var z in zs)
            {
                var o = ZoneOpenIds(z.Id); int n = o.Count(id => PutOf(id) != null);
                zones.Add(new Block(BK.Button) { Text = z.Chip, Sub = n + "/" + o.Count, Act = "zone:" + z.Id, On = z.Id == DecoZone, Style = "zone" });
            }
            r.Add(zones);
            var open = ZoneOpenIds(DecoZone);
            var head = new Block(BK.Card) { Style = "zhead", Text = F_("deco.zhead", ZoneLong(DecoZone), open.Count) };
            head.Add(Block.Btn(S_("deco.undo"), "undo", "btn", decoUndo.Count == 0)); head.Add(Block.Btn(S_("deco.redo"), "redo", "btn", decoRedo.Count == 0));
            r.Add(head);
            var grid = new Block(BK.Grid) { Style = "slots" };
            foreach (var id in open)
            {
                string it = PutOf(id); var sl = T.DecorSlotById[id];
                grid.Add(new Block(BK.Cell) { Style = "slot" + (it != null ? " full" : ""), Act = "slot:" + id, On = id == sel, Tag = sl.Size, Count = sl.Num.ToString(), Icon = it != null ? "decor:" + it : null, Text = it != null ? T.DecorItemById[it].Name : "+" });
            }
            var locked = ZoneSlotIds(DecoZone).Where(id => !SlotNowOpen(id)).ToList();
            if (locked.Count > 0)
            {
                double Gap(string id) { var s = T.DecorSlotById[id]; return s.OpenLv != 0 ? s.OpenLv - GardenLv : s.OpenStage - Ctx.Stage; }
                var best = locked.OrderBy(Gap).ThenBy(i => T.DecorSlotById[i].Num).First(); var bs = T.DecorSlotById[best];
                bool Same(string id) { var s = T.DecorSlotById[id]; return s.OpenLv == bs.OpenLv && s.OpenStage == bs.OpenStage && s.OpenBase == bs.OpenBase; }
                foreach (var id in locked.Where(Same).Take(2))
                    grid.Add(new Block(BK.Cell) { Style = "slot lockd", Act = "slotlock:" + id, Locked = true, Tag = T.DecorSlotById[id].Size, Count = T.DecorSlotById[id].Num.ToString(), Icon = "lock", Text = DecorSlotNeed(id, false) });
            }
            r.Add(grid);
            r.Add(Block.N(S_("deco.legend"), "legend"));
            var au = DecoAutoPlan();
            var tools = new Block(BK.ButtonRow);
            tools.Add(Block.Btn(S_("deco.auto.btn"), "auto", "main", !au.Can)); tools.Add(Block.Btn(S_("deco.peek"), "peek:on", "btn"));
            r.Add(tools);
            string line = decoNote != "" ? decoNote : (au.Can ? "" : au.Reason);
            if (!string.IsNullOrEmpty(line)) r.Add(Block.N(line, "status"));
            if (sel != null) r.Add(BuildPicks(sel)); else r.Add(Block.N(S_("deco.pickHint")));
            int total = PlacedTotal();
            if (decoConfirm && total > 0)
            {
                var cf = new Block(BK.Card) { Style = "confirm", Text = F_("deco.confirm", total), Sub = S_("deco.confirmSub") };
                cf.Add(Block.Btn(S_("deco.clearYes"), "clearall:yes", "main")); cf.Add(Block.Btn(S_("deco.clearNo"), "clearall:no"));
                r.Add(cf);
            }
            var tail = new Block(BK.ButtonRow) { Style = "tail" };
            if (total > 0 && !decoConfirm) tail.Add(Block.Btn(S_("deco.clearAll"), "clearall:ask", "tbtn"));
            tail.Add(Block.Btn(S_("deco.photo"), "photo:go", "tbtn"));
            r.Add(tail);
            return r;
        }

        Block BuildPicks(string sel)
        {
            var sl = T.DecorSlotById[sel]; string cur = PutOf(sel);
            var all = Catalog().Where(i => i.Zones.Contains(sl.Chip)).ToList();
            int Rank(string st) => st == "ok" ? 0 : st == "lock" ? 1 : 2;
            var list = all.Select((it, i) => new { it, st = DecorRules.State(T, S, it.Id, sel, GardenLv), i }).OrderBy(x => Rank(x.st)).ThenBy(x => x.i).ToList();
            var sheet = new Block(BK.Pick) { Text = F_("deco.pickHead", SlotLabel(sel), SizeWord(sl.Size)) };
            var head = new Block(BK.ButtonRow);
            if (cur != null) head.Add(Block.Btn(S_("deco.clear"), "clear"));
            head.Add(Block.Btn(S_("deco.close"), "phclose"));
            sheet.Add(head);
            if (all.Count == 0) sheet.Add(Block.N(S_("deco.noZoneItem")));
            else if (!list.Any(x => x.st == "ok")) sheet.Add(Block.N(S_("deco.noneNow")));
            foreach (var x in list)
            {
                var it = x.it; int n = DecorRules.PlacedCount(T, S, it.Id, Ctx.Stage, GardenLv); bool here = cur == it.Id;
                if (x.st == "lock")
                {
                    sheet.Add(new Block(BK.Cell) { Style = "item lock", Act = "decoopen:" + it.Id, Icon = "decor:" + it.Id, Locked = true, Text = DecorNeedText(it.Id, false), On = decoOpenItem == it.Id });
                    continue;
                }
                if (x.st == "big") { sheet.Add(new Block(BK.Cell) { Style = "item big", Icon = "decor:" + it.Id, Disabled = true, Text = it.Name, Sub = F_("deco.bigSlot", SizeWord(it.Size)) }); continue; }
                string sub = here ? S_("deco.here") : n > 0 ? F_("deco.placedN", n) : F_("deco.sizeItem", SizeWord(it.Size));
                sheet.Add(new Block(BK.Cell) { Style = "item" + (here ? " cur" : ""), Act = "put:" + it.Id, Icon = "decor:" + it.Id, Text = it.Name, Sub = sub, On = here, Count = n > 0 ? "×" + n : null });
            }
            if (decoOpenItem != null && list.Any(x => x.it.Id == decoOpenItem && x.st == "lock")) sheet.Add(Block.N(DecorNeedText(decoOpenItem, true), "need"));
            return sheet;
        }

        // ----- 외형 -----
        bool SkinUsable(string layer, string id)
        {
            if (!T.SkinLayerById.TryGetValue(layer, out var ly) || !ly.Skins.TryGetValue(id ?? "", out var s)) return false;
            return s.Src == "free" || (s.Src == "earn" && Ctx.LvlOf(s.NeedKey) >= s.NeedLv) || (s.Src == "story" && S.Finale > 0) || Owns(layer + ":" + id);
        }
        bool SkinShown(SkinDef s) => s.Src == "free" || s.Src == "earn" || (s.Src == "story" && S.Finale > 0) || (s.Src == "iap" && Features.Shop);
        public string SkinId(string layer)
        {
            if (SkinPreview != null && SkinPreview[0] == layer) return SkinPreview[1];
            return S.Skin.TryGetValue(layer, out var id) && SkinUsable(layer, id) ? id : "classic";
        }
        bool BeamUsable(BeamColorDef c) => c.Src == "iap" ? Owns("beam:" + c.Id) : Ctx.LvlOf("lens") >= c.Lens;
        bool BeamShown(BeamColorDef c) => c.Src != "iap" || Features.Shop;

        void ActSkin(string v)
        {
            int d = v.IndexOf('.'); if (d < 0) return;
            string layer = v.Substring(0, d), id = v.Substring(d + 1);
            if (SkinUsable(layer, id)) { S.Skin[layer] = id; Ctx.NotifyChanged(); SkinPreview = null; Host.Sfx("pick"); Host.WorldDirty(); }
            else if (T.SkinLayerById.TryGetValue(layer, out var ly) && ly.Skins.TryGetValue(id, out var sk) && sk.Src == "iap") { SkinPreview = new[] { layer, id }; Host.Sfx("tick"); Host.WorldDirty(); }
        }

        Block PreviewBanner(bool inShop)
        {
            if (SkinPreview == null) return null;
            var s = T.SkinLayerById[SkinPreview[0]].Skins[SkinPreview[1]];
            var b = new Block(BK.Card) { Style = "pv", Text = S_("pv.title"), Sub = s.Name };
            b.Add(Block.Btn(S_("pv.undo"), "skinprev:off"));
            if (!inShop) b.Add(Block.Btn(S_("pv.goShop"), "skinshop:go", "main"));
            return b;
        }

        List<Block> BuildSkins()
        {
            var r = new List<Block> { DecoSeg() };
            var pv = PreviewBanner(false); if (pv != null) r.Add(pv);
            r.Add(Block.N(S_("skin.note") + (Features.Shop ? S_("skin.noteShop") : "")));
            foreach (var layer in T.SkinLayers)
            {
                var list = layer.SkinIds.Select(id => layer.Skins[id]).Where(SkinShown).ToList();
                if (list.Count < 2) continue;
                r.Add(Block.H(S_("layer." + layer.Id)));
                if (layer.Id == "decor") r.Add(Block.N(S_("skin.decorNote")));
                var seg = Block.Seg(true);
                foreach (var s in list)
                {
                    bool ok = SkinUsable(layer.Id, s.Id), pvOn = SkinPreview != null && SkinPreview[0] == layer.Id && SkinPreview[1] == s.Id;
                    string lk = ok ? "" : pvOn ? S_("skin.previewing") : s.Src == "earn" ? TrackName(s.NeedKey) + " Lv." + s.NeedLv : s.Src == "story" ? S_("skin.story") : S_("skin.buy");
                    seg.Add(new Block(BK.Button) { Text = s.Name, Sub = lk != "" ? lk : null, Locked = !ok, On = SkinId(layer.Id) == s.Id, Act = "skin:" + layer.Id + "." + s.Id, Style = "btn" });
                }
                r.Add(seg);
            }
            r.Add(Block.H(S_("beam.title"))); r.Add(Block.N(S_("beam.note")));
            var bcur = T.BeamColors.FirstOrDefault(c => c.Id == S.Beam && BeamUsable(c)) ?? T.BeamColors[0];
            var bs = Block.Seg(true);
            foreach (var c in T.BeamColors.Where(BeamShown))
                bs.Add(new Block(BK.Button) { Text = c.Name, Sub = BeamUsable(c) ? null : c.Src == "iap" ? S_("skin.buy") : S_("beam.lens") + " Lv." + c.Lens, Locked = !BeamUsable(c), On = bcur.Id == c.Id, Act = "beam:" + c.Id, Style = "btn" });
            r.Add(bs);
            if (Features.Shop && T.Products.Any(p => !ProductOwned(p))) r.Add(Block.Btn(S_("skin.moreShop"), "skinshop:go", "tbtn"));
            return r;
        }
    }
}
