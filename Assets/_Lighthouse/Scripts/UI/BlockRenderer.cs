using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>
    /// 블록 목록(PanelController.Build)을 uGUI 계층으로 그린다. 그리기만 하고 규칙은 모른다 — 입력은 Act 문자열로 onAct 에 돌려준다.
    /// 모든 높이는 레이아웃 그룹이 정하므로(글이 길어지거나 폭이 좁아져도) 칸끼리 겹치지 않는다.
    /// </summary>
    public sealed class BlockRenderer
    {
        readonly Action<string> onAct; readonly UiStrings str; readonly Action<string> onInput; readonly Func<string> inputText;
        const float Body = 14f, Small = 12f, Title = 15f;

        public BlockRenderer(UiStrings s, Action<string> act, Action<string> input = null, Func<string> inputGet = null) { str = s; onAct = act; onInput = input; inputText = inputGet; }

        public void Render(RectTransform content, IList<Block> blocks)
        {
            for (int i = content.childCount - 1; i >= 0; i--) { var c = content.GetChild(i).gameObject; c.SetActive(false); UnityEngine.Object.DestroyImmediate(c); }
            foreach (var b in blocks) Add(content, b);
        }

        static Color Ink => UiTheme.Ink;

        void Add(Transform p, Block b)
        {
            switch (b.Kind)
            {
                case BK.Heading: Heading(p, b); break;
                case BK.Note: Note(p, b); break;
                case BK.Summary: Summary(p, b); break;
                case BK.Progress: Progress(p, b); break;
                case BK.Seg: Seg(p, b); break;
                case BK.Grid: Grid(p, b); break;
                case BK.Row: Row(p, b); break;
                case BK.Letter: Letter(p, b); break;
                case BK.Button: ButtonBlock(p, b); break;
                case BK.ButtonRow: ButtonRow(p, b); break;
                case BK.Empty: Empty(p, b); break;
                case BK.Input: Input(p, b); break;
                case BK.Card: case BK.Pick: case BK.Gate: CardBlock(p, b); break;
                case BK.Stats: Stats(p, b); break;
                case BK.Cell: CellBlock(p, b, false); break;
            }
        }

        // ---------- 글 ----------
        void Heading(Transform p, Block b)
        {
            var row = Kit.HBox(p, "heading", new Vector4(0, 4, 0, 1), 3, TextAnchor.MiddleLeft, true);
            if (!string.IsNullOrEmpty(b.Icon)) Kit.IconImage(row.transform, b.Icon);
            var t = Kit.Text(row.transform, b.Text, 16f, UiTheme.Teal2, true); Kit.Elem(t.gameObject, flexW: 1);
        }

        void Note(Transform p, Block b)
        {
            Color c = b.Style == "need" || b.Style == "status" ? UiTheme.Teal2 : b.Style == "lead" ? Ink : UiTheme.Soft;
            float size = b.Style == "lead" ? 15f : b.Style == "small" || b.Style == "legend" || b.Style == "qnote" ? Small : 13.5f;
            var t = Kit.Text(p, b.Text, size, c, b.Style == "lead" || b.Style == "status" || b.Style == "need", TextAlignmentOptions.TopLeft, "note");
            if (b.Style == "story") { var inner = t; inner.fontSize = 13.5f; inner.color = Ink; }
            if (!string.IsNullOrEmpty(b.Sub) && b.Style == "story") { var s = Kit.Text(p, b.Sub, Small, UiTheme.Soft); s.transform.SetSiblingIndex(t.transform.GetSiblingIndex()); }
            var pad = t.gameObject.AddComponent<LayoutElement>(); pad.minHeight = size * 1.4f;
        }

        // ---------- 막대 ----------
        void Bar(Transform p, float value, bool thin)
        {
            var bg = Kit.Frame(p, "track", null, "bar"); bg.raycastTarget = false;
            var le = bg.gameObject.AddComponent<LayoutElement>(); var dl = bg.gameObject.AddComponent<DotLayout>(); dl.Elem = le; dl.MinHeightDots = thin ? 4 : 6; dl.ApplyDot(UiRoot.CurrentDot);
            var fill = Kit.Solid(bg.transform, UiTheme.Gold, "fill"); var r = fill.rectTransform;
            r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(Mathf.Clamp01(value), 1); r.offsetMin = new Vector2(UiRoot.CurrentDot, UiRoot.CurrentDot); r.offsetMax = new Vector2(Mathf.Clamp01(value) >= 1 ? -UiRoot.CurrentDot : 0, -UiRoot.CurrentDot);
        }

        void Progress(Transform p, Block b)
        {
            var box = Kit.VBox(p, "progress", new Vector4(0, 1, 0, 1), 1);
            var top = Kit.HBox(box.transform, "labels", Vector4.zero, 2, TextAnchor.MiddleLeft, false);
            Kit.Elem(Kit.Text(top.transform, b.Text, 13f, Ink).gameObject, flexW: 1);
            Kit.Text(top.transform, b.Sub, 13f, UiTheme.Teal2, true, TextAlignmentOptions.TopRight);
            Bar(box.transform, b.Value, b.Style == "thin");
        }

        // ---------- 단추 ----------
        void ButtonBlock(Transform p, Block b) { MakeButton(p, b, false); }

        Selectable_ MakeButton(Transform p, Block b, bool inSeg)
        {
            string frame = "btn", pressed = "btn_p"; Color tc = Ink; bool link = b.Style == "tbtn" || b.Style == "txt";
            if (b.Disabled) { frame = "btn_d"; pressed = null; tc = UiTheme.Soft; }
            else if (b.Style == "main") { frame = "main"; pressed = "main_p"; tc = Color.white; }
            else if (b.On) { frame = "gold"; pressed = "gold_p"; tc = UiTheme.OnGold; }
            if (link)
            {
                var r = Kit.Rect("link", p); var img = r.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0); img.raycastTarget = true;
                var btn = r.gameObject.AddComponent<Button>(); btn.targetGraphic = img; btn.transition = Selectable.Transition.None;
                var le = r.gameObject.AddComponent<LayoutElement>(); le.minHeight = 44f;
                var t = Kit.Text(r, b.Text, 14f, UiTheme.Teal2, true, TextAlignmentOptions.Center, "label"); t.fontStyle = FontStyles.Underline; Kit.Stretch(t.rectTransform);
                if (b.Act != null) { string a = b.Act; btn.onClick.AddListener(() => onAct(a)); r.gameObject.AddComponent<ActTag>().Act = a; }
                return new Selectable_ { Btn = btn, Rect = r };
            }
            string label = b.Text; string sub = inSeg ? b.Sub : null;
            var button = Kit.FrameButton(p, frame, pressed, label, inSeg ? 13.5f : 14f, tc, null, "btn:" + (b.Act ?? b.Text), true, 44f, inSeg ? 1.5f : 5f);
            var g = button.GetComponent<Image>();
            if (b.Locked && !b.On) g.color = new Color(1, 1, 1, 0.78f);
            if (!string.IsNullOrEmpty(sub)) Kit.Text(button.transform, sub, 11f, b.On ? UiTheme.OnGold : UiTheme.Soft, false, TextAlignmentOptions.Center, "sub");
            if (b.Disabled) button.interactable = false;
            else if (b.Act != null) { string a = b.Act; button.onClick.AddListener(() => onAct(a)); button.gameObject.AddComponent<ActTag>().Act = a; NavNone(button, a); }
            return new Selectable_ { Btn = button, Rect = (RectTransform)button.transform };
        }
        /// <summary>구역 칩·자리 칸은 화살표를 UiRoot 가 직접 처리한다(원본 3946행: 칩은 ←→ 로 옮기며 고르고, 칸은 격자대로). 자동 이동은 끈다.</summary>
        static void NavNone(Selectable s, string act) { if (act.StartsWith("zone:") || act.StartsWith("slot:") || act.StartsWith("slotlock:")) { var n = s.navigation; n.mode = Navigation.Mode.None; s.navigation = n; } }
        struct Selectable_ { public Button Btn; public RectTransform Rect; }

        void ButtonRow(Transform p, Block b)
        {
            var row = Kit.HBox(p, "buttons", new Vector4(0, 1, 0, 1), 3, TextAnchor.MiddleRight, true);
            foreach (var k in b.Kids ?? new List<Block>())
            {
                var s = MakeButton(row.transform, k, false);
                if (k.Style == "tbtn" || k.Style == "txt") Kit.Elem(s.Rect.gameObject, flexW: 0, minW: 96f); else Kit.Elem(s.Rect.gameObject, flexW: 1, minW: 80f);
            }
        }

        void Seg(Transform p, Block b)
        {
            int n = b.Kids?.Count ?? 0; if (n == 0) return;
            int cols = b.Style == "zones" ? Mathf.Min(n, 5) : b.Style == "dx" ? 5 : b.Style == "wrap" ? (UiRoot.CanvasW < 360f ? 3 : 4) : Mathf.Min(n, 4);
            cols = Mathf.Min(cols, n);
            var grid = Kit.Rect("seg", p); var fc = grid.gameObject.AddComponent<FixedColumnsLayout>(); fc.Columns = cols; fc.SpacingDots = 2; fc.MinCellHeight = 44f; fc.ApplyDot(UiRoot.CurrentDot);
            foreach (var k in b.Kids) MakeButton(grid, k, true);
        }

        // ---------- 요약·카드 ----------
        void Summary(Transform p, Block b)
        {
            string frame = b.Style == "cover" ? "plate_gold" : "plate";
            var card = Kit.Frame(p, frame, null, "summary"); card.raycastTarget = false;
            var box = card.gameObject.AddComponent<VerticalLayoutGroup>(); box.childControlWidth = true; box.childControlHeight = true; box.childForceExpandWidth = true; box.childForceExpandHeight = false;
            var dl = card.gameObject.AddComponent<DotLayout>(); dl.Group = box; dl.PadDots = new Vector4(5, 4, 5, 6); dl.SpacingDots = 1; dl.ApplyDot(UiRoot.CurrentDot);
            if (!string.IsNullOrEmpty(b.Extra)) Kit.Text(card.transform, b.Extra, 14f, UiTheme.GoldTx, true);
            var top = Kit.HBox(card.transform, "top", Vector4.zero, 3, TextAnchor.LowerLeft, false);
            var big = Kit.Text(top.transform, b.Text, 26f, Ink, true); big.textWrappingMode = TextWrappingModes.NoWrap; Kit.Elem(big.gameObject, flexW: 0);
            Kit.Elem(Kit.Text(top.transform, b.Sub, 13f, UiTheme.Soft).gameObject, flexW: 1);
            if (b.Value >= 0) Bar(card.transform, b.Value, false);
            if (b.Kids != null) foreach (var k in b.Kids) Add(card.transform, k);
        }

        void CardBlock(Transform p, Block b)
        {
            string frame = b.Style == "confirm" ? "plate_focus" : b.Style != null && b.Style.StartsWith("stage") ? "plate_gold" : b.Style != null && b.Style.StartsWith("pv") ? "plate_focus" : b.Style == "adsec" ? "plate_ad" : b.Kind == BK.Pick ? "tray" : "plate";
            if (b.Locked) frame = "plate_off";
            var card = Kit.Frame(p, frame, null, b.Kind == BK.Pick ? "pick" : b.Kind == BK.Gate ? "gate" : "card:" + (b.Style ?? "plain")); card.raycastTarget = false;
            var box = card.gameObject.AddComponent<VerticalLayoutGroup>(); box.childControlWidth = true; box.childControlHeight = true; box.childForceExpandWidth = true; box.childForceExpandHeight = false;
            var dl = card.gameObject.AddComponent<DotLayout>(); dl.Group = box; dl.PadDots = new Vector4(5, 4, 5, 6); dl.SpacingDots = 2; dl.ApplyDot(UiRoot.CurrentDot);
            bool clickable = b.Act != null && (b.Style == "rolecard" || b.Style == "adsec");
            if (!string.IsNullOrEmpty(b.Text) || !string.IsNullOrEmpty(b.Sub))
            {
                RectTransform head = card.rectTransform;
                if (clickable)
                {
                    var hb = Kit.Rect("head", card.transform); var img = hb.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0);
                    var btn = hb.gameObject.AddComponent<Button>(); btn.targetGraphic = img; btn.transition = Selectable.Transition.None; string a = b.Act; btn.onClick.AddListener(() => onAct(a)); hb.gameObject.AddComponent<ActTag>().Act = a;
                    var v = hb.gameObject.AddComponent<VerticalLayoutGroup>(); v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
                    Kit.Elem(hb.gameObject, minH: 44f);
                    head = hb;
                }
                var line = Kit.HBox(head, "title", Vector4.zero, 3, TextAnchor.MiddleLeft, false);
                var tt = Kit.Text(line.transform, b.Text, b.Style == "rank" || b.Style == "stage" ? 20f : Title, Ink, true); Kit.Elem(tt.gameObject, flexW: 1);
                if (!string.IsNullOrEmpty(b.Tag)) Chip(line.transform, b.Tag, "#8a5a12");
                if (clickable && !string.IsNullOrEmpty(b.Sub)) { var st = Kit.Text(head, b.Sub, Small, UiTheme.Teal2, true); }
                else if (!string.IsNullOrEmpty(b.Sub)) Kit.Text(card.transform, b.Sub, b.Style == "set" || b.Style == "rank" ? 13f : Small, UiTheme.Soft);
            }
            if (b.Style != null && b.Style.StartsWith("two") && b.Kids != null) { var two = Kit.HBox(card.transform, "two", Vector4.zero, 4, TextAnchor.UpperLeft, true); foreach (var k in b.Kids) CellBlock(two.transform, k, true); return; }
            if (b.Kids != null)
            {
                Transform btnRow = null;
                foreach (var k in b.Kids)
                {
                    bool plainCard = b.Kind != BK.Gate && b.Style != "set" && b.Style != "set more" && b.Style != "rolecard" && !(b.Style ?? "").StartsWith("adsec");
                    if (k.Kind == BK.Button && plainCard)
                    {
                        if (btnRow == null) btnRow = Kit.HBox(card.transform, "buttons", new Vector4(0, 1, 0, 1), 3, TextAnchor.MiddleRight, true).transform;
                        var s = MakeButton(btnRow, k, false); Kit.Elem(s.Rect.gameObject, flexW: k.Style == "tbtn" ? 0 : 1, minW: k.Style == "tbtn" ? 72f : 80f);
                        continue;
                    }
                    btnRow = null;
                    if (b.Kind == BK.Pick && k.Kind == BK.Cell) CellBlock(card.transform, k, true); else Add(card.transform, k);
                }
            }
        }

        void Chip(Transform p, string text, string border)
        {
            var c = Kit.Frame(p, "plate_gold", null, "chip"); c.raycastTarget = false;
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>(); h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childAlignment = TextAnchor.MiddleCenter;
            var dl = c.gameObject.AddComponent<DotLayout>(); dl.Group = h; dl.PadDots = new Vector4(3, 1, 3, 2); dl.ApplyDot(UiRoot.CurrentDot);
            var ct = Kit.Text(c.transform, text, 11f, UiTheme.GoldTx, true, TextAlignmentOptions.Center, "chip"); ct.textWrappingMode = TextWrappingModes.NoWrap; Kit.Elem(ct.gameObject, minW: ct.GetPreferredValues(ct.text).x + 1f); Kit.Elem(c.gameObject, minW: ct.GetPreferredValues(ct.text).x + 7f * UiRoot.CurrentDot, flexW: 0);
        }

        void Stats(Transform p, Block b)
        {
            var grid = Kit.Rect("stats", p); var fc = grid.gameObject.AddComponent<FixedColumnsLayout>(); fc.Columns = 3; fc.SpacingDots = 2; fc.MinCellHeight = 40; fc.ApplyDot(UiRoot.CurrentDot);
            foreach (var k in b.Kids)
            {
                var c = Kit.Frame(grid, "plate", null, "stat"); c.raycastTarget = false;
                var v = c.gameObject.AddComponent<VerticalLayoutGroup>(); v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
                var dl = c.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(3, 2, 3, 4); dl.ApplyDot(UiRoot.CurrentDot);
                Kit.Text(c.transform, k.Sub, 15f, Ink, true, TextAlignmentOptions.Center); Kit.Text(c.transform, k.Text, Small, UiTheme.Soft, false, TextAlignmentOptions.Center);
            }
        }

        // ---------- 격자·칸 ----------
        void Grid(Transform p, Block b)
        {
            int n = b.Kids?.Count ?? 0; if (n == 0) return;
            int cols = b.Style == "slots" ? 4 : b.Style == "mini" ? Mathf.Max(3, ScreenLayout.GridColumns(UiRoot.CanvasW) + 1) : ScreenLayout.GridColumns(UiRoot.CanvasW);
            var grid = Kit.Rect("grid:" + (b.Style ?? "cells"), p); var fc = grid.gameObject.AddComponent<FixedColumnsLayout>(); fc.Columns = cols; fc.SpacingDots = 4; fc.MinCellHeight = b.Style == "slots" ? 64f : 72f; fc.ApplyDot(UiRoot.CurrentDot);
            foreach (var k in b.Kids) CellBlock(grid, k, false);
        }

        void CellBlock(Transform p, Block b, bool listRow)
        {
            bool slot = b.Style != null && b.Style.StartsWith("slot"), item = b.Style != null && b.Style.StartsWith("item"), stat = !slot && !item && b.Act == null && listRow;
            string frame = b.Locked ? "plate_off" : b.Shiny ? "plate_gold" : slot && b.On ? "plate_focus" : slot && b.Style.Contains("full") ? "plate_new" : item && b.On ? "plate_gold" : "plate";
            if (b.Disabled) frame = "plate_off";
            var cell = Kit.Frame(p, frame, null, "cell:" + (b.Act ?? b.Text)); cell.raycastTarget = b.Act != null;
            if (b.Act != null && !b.Disabled)
            {
                var btn = cell.gameObject.AddComponent<Button>(); btn.targetGraphic = cell; btn.transition = Selectable.Transition.None; string a = b.Act; btn.onClick.AddListener(() => onAct(a)); cell.gameObject.AddComponent<ActTag>().Act = a; NavNone(btn, a);
            }
            if (item || (listRow && b.Icon != null))
            {
                var h = cell.gameObject.AddComponent<HorizontalLayoutGroup>(); h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
                var dl = cell.gameObject.AddComponent<DotLayout>(); dl.Group = h; dl.PadDots = new Vector4(4, 3, 4, 5); dl.SpacingDots = 3; dl.MinHeightDots = 0; dl.Elem = Kit.Elem(cell.gameObject, minH: 44f); dl.ApplyDot(UiRoot.CurrentDot);
                if (b.Icon != null) Kit.IconImage(cell.transform, b.Icon, 16, 1, b.Locked);
                var col = Kit.VBox(cell.transform, "text", Vector4.zero, 0); Kit.Elem(col.gameObject, flexW: 1);
                Kit.Text(col.transform, b.Text, 13.5f, b.Locked ? UiTheme.Soft : Ink, !b.Locked);
                if (!string.IsNullOrEmpty(b.Sub)) Kit.Text(col.transform, b.Sub, Small, UiTheme.Soft);
                if (!string.IsNullOrEmpty(b.Count)) Kit.Text(cell.transform, b.Count, Small, UiTheme.Teal2, true, TextAlignmentOptions.TopRight);
                return;
            }
            if (stat) { var v0 = cell.gameObject.AddComponent<VerticalLayoutGroup>(); v0.childControlWidth = v0.childControlHeight = true; v0.childForceExpandWidth = true; v0.childForceExpandHeight = false; var d0 = cell.gameObject.AddComponent<DotLayout>(); d0.Group = v0; d0.PadDots = new Vector4(3, 2, 3, 4); d0.ApplyDot(UiRoot.CurrentDot); Kit.Text(cell.transform, b.Sub, 16f, Ink, true, TextAlignmentOptions.Center); Kit.Text(cell.transform, b.Text, Small, UiTheme.Soft, false, TextAlignmentOptions.Center); return; }
            var v = cell.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var dv = cell.gameObject.AddComponent<DotLayout>(); dv.Group = v; dv.PadDots = new Vector4(2, 2, 2, 4); dv.SpacingDots = 1; dv.ApplyDot(UiRoot.CurrentDot);
            if (slot)
            {
                var top = Kit.HBox(cell.transform, "top", Vector4.zero, 1, TextAnchor.MiddleLeft, false);
                Kit.Text(top.transform, b.Count, 12f, Ink, true); Kit.Elem(Kit.Rect("sp", top.transform).gameObject, flexW: 1); Kit.Text(top.transform, b.Tag, 11f, UiTheme.Soft, true, TextAlignmentOptions.TopRight);
                if (b.Icon != null) { var ic = Kit.IconImage(cell.transform, b.Icon, 16, 1, b.Locked); ic.GetComponent<LayoutElement>().flexibleWidth = 0; }
                else Kit.Text(cell.transform, b.Text == "+" ? "+" : "", 22f, UiTheme.Teal2, true, TextAlignmentOptions.Center);
                if (b.Icon != null) Kit.Text(cell.transform, b.Text, 11f, b.Locked ? UiTheme.Soft : Ink, false, TextAlignmentOptions.Center);
                return;
            }
            if (b.Tag != null || b.Count != null)
            {
                var top = Kit.HBox(cell.transform, "top", Vector4.zero, 1, TextAnchor.MiddleLeft, false);
                if (b.Tag != null) Chip(top.transform, b.Tag, "#14172b"); Kit.Elem(Kit.Rect("sp", top.transform).gameObject, flexW: 1);
                if (b.Count != null) Kit.Text(top.transform, b.Count, 11f, UiTheme.Teal2, true, TextAlignmentOptions.TopRight);
            }
            if (b.Icon != null) { var ic = Kit.IconImage(cell.transform, b.Icon, 8, 2, b.Locked); }
            Kit.Text(cell.transform, b.Text, 12f, b.Locked ? UiTheme.Soft : Ink, true, TextAlignmentOptions.Center);
            if (!string.IsNullOrEmpty(b.Sub)) Kit.Text(cell.transform, b.Sub, 10.5f, UiTheme.Soft, false, TextAlignmentOptions.Center);
        }

        // ---------- 줄 ----------
        void Row(Transform p, Block b)
        {
            string style = b.Style ?? "";
            string frame = b.Locked ? "plate_off" : b.Shiny ? "plate_gold" : style.Contains("focus") ? "plate_focus" : style.StartsWith("adrow") ? "plate_ad" : style.StartsWith("gline") || style.StartsWith("pline") || style.StartsWith("lbg") || style.StartsWith("tiprow") || style.StartsWith("have") ? null : "plate";
            Transform host = p;
            if (frame != null)
            {
                var f = Kit.Frame(p, frame, null, "row:" + style); f.raycastTarget = false; host = f.transform;
                var v = f.gameObject.AddComponent<VerticalLayoutGroup>(); v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
                var dl = f.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(5, 3, 5, 5); dl.SpacingDots = 2; dl.ApplyDot(UiRoot.CurrentDot);
            }
            else { var v = Kit.VBox(p, "row:" + style, new Vector4(0, 1, 0, 1), 1); host = v.transform; }
            var line = Kit.HBox(host, "line", Vector4.zero, 3, TextAnchor.MiddleLeft, false);
            if (!string.IsNullOrEmpty(b.Icon) && !style.StartsWith("diary")) Kit.IconImage(line.transform, b.Icon, 8, 2, b.Locked);
            var col = Kit.VBox(line.transform, "col", Vector4.zero, 0); Kit.Elem(col.gameObject, flexW: 1);
            bool bold = !(style.StartsWith("gline") || style.StartsWith("tiprow"));
            var titleLine = Kit.HBox(col.transform, "t", Vector4.zero, 3, TextAnchor.MiddleLeft, false);
            if (!string.IsNullOrEmpty(b.Tag) && (style.StartsWith("role") || style.StartsWith("tiprow"))) Chip(titleLine.transform, b.Tag, "#1d5a34");
            var nm = Kit.Text(titleLine.transform, b.Text, style.StartsWith("diary") ? 12.5f : 14f, b.Locked ? UiTheme.Soft : Ink, bold); Kit.Elem(nm.gameObject, flexW: 1);
            if (!string.IsNullOrEmpty(b.Tag) && !(style.StartsWith("role") || style.StartsWith("tiprow"))) Chip(titleLine.transform, b.Tag, "#14172b");
            if (!string.IsNullOrEmpty(b.Extra) && style.StartsWith("reg")) Kit.Text(col.transform, b.Extra, 14f, UiTheme.Red, true);
            if (!string.IsNullOrEmpty(b.Sub)) Kit.Text(col.transform, b.Sub, style.StartsWith("diary") ? 13f : Small + 0.5f, style.StartsWith("diary") ? Ink : UiTheme.Soft);
            if (!string.IsNullOrEmpty(b.Extra) && !style.StartsWith("reg")) Kit.Text(col.transform, b.Extra, Small, UiTheme.Soft);
            if (b.Value >= 0) Bar(col.transform, b.Value, true);
            if (!string.IsNullOrEmpty(b.Count) && (b.Open || !style.StartsWith("track"))) Kit.Text(col.transform, b.Count, Small, UiTheme.Teal2, true);
            if (style.StartsWith("diary") && !string.IsNullOrEmpty(b.Icon)) Kit.IconImage(line.transform, b.Icon, 8, 2);
            if (b.Kids != null && b.Kids.Count > 0)
            {
                var btns = Kit.HBox(host, "btns", Vector4.zero, 3, TextAnchor.MiddleRight, true);
                foreach (var k in b.Kids)
                {
                    if (k.Kind == BK.Button) { var s = MakeButton(btns.transform, k, false); Kit.Elem(s.Rect.gameObject, flexW: k.Style == "main" ? 1 : 0, minW: k.Style == "tbtn" ? 72 : 88); }
                    else Add(host, k);
                }
            }
        }

        // ---------- 편지 ----------
        void Letter(Transform p, Block b)
        {
            var card = Kit.Frame(p, b.New ? "plate_new" : "plate", null, "letter"); card.raycastTarget = false;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>(); v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var dl = card.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(4, 3, 4, 5); dl.SpacingDots = 2; dl.ApplyDot(UiRoot.CurrentDot);
            var headRect = Kit.Rect("top", card.transform); var himg = headRect.gameObject.AddComponent<Image>(); himg.color = new Color(0, 0, 0, 0);
            var btn = headRect.gameObject.AddComponent<Button>(); btn.targetGraphic = himg; btn.transition = Selectable.Transition.None; string act = b.Act; btn.onClick.AddListener(() => onAct(act)); headRect.gameObject.AddComponent<ActTag>().Act = act;
            var hv = headRect.gameObject.AddComponent<HorizontalLayoutGroup>(); hv.childAlignment = TextAnchor.MiddleLeft; hv.childControlWidth = hv.childControlHeight = true; hv.childForceExpandWidth = false; hv.childForceExpandHeight = false; hv.spacing = 6;
            Kit.Elem(headRect.gameObject, minH: 44f);
            var seal = Kit.Rect("seal", headRect); var simg = seal.gameObject.AddComponent<Image>(); simg.sprite = UiTheme.Frame("seal"); simg.preserveAspect = true; simg.raycastTarget = false; var sd = seal.gameObject.AddComponent<DotSize>(); sd.Elem = seal.gameObject.AddComponent<LayoutElement>(); sd.Px = simg.sprite.texture != UiTheme.White1.texture ? Mathf.Max(simg.sprite.texture.width, simg.sprite.texture.height) : 6; sd.Mult = 1; sd.ApplyDot(UiRoot.CurrentDot);
            if (!string.IsNullOrEmpty(b.Icon)) Kit.IconImage(headRect, b.Icon);
            var col = Kit.VBox(headRect, "tt", Vector4.zero, 0); Kit.Elem(col.gameObject, flexW: 1);
            var nl = Kit.HBox(col.transform, "nm", Vector4.zero, 3, TextAnchor.MiddleLeft, false);
            if (!string.IsNullOrEmpty(b.Tag)) Chip(nl.transform, b.Tag, "#14172b");
            Kit.Elem(Kit.Text(nl.transform, b.Text, 14f, Ink, true).gameObject, flexW: 1);
            if (!b.Open && !string.IsNullOrEmpty(b.Sub)) Kit.Text(col.transform, b.Sub, 12.5f, UiTheme.Soft);
            if (!string.IsNullOrEmpty(b.Date)) { var dt = Kit.Text(headRect, b.Date, 12f, UiTheme.Soft, false, TextAlignmentOptions.TopRight); dt.textWrappingMode = TextWrappingModes.NoWrap; Kit.Elem(dt.gameObject, minW: 54f, flexW: 0); }
            if (b.Open)
            {
                var body = Kit.VBox(card.transform, "body", new Vector4(2, 2, 2, 2), 2);
                Kit.Text(body.transform, b.Sub, 14f, Ink);
                if (!string.IsNullOrEmpty(b.Extra)) Kit.Text(body.transform, b.Extra, 13f, UiTheme.Soft, false, TextAlignmentOptions.TopRight);
                if (!string.IsNullOrEmpty(b.Count)) { var gl = Kit.HBox(body.transform, "gift", Vector4.zero, 3, TextAnchor.MiddleLeft, false); Kit.IconImage(gl.transform, b.Icon); Kit.Text(gl.transform, b.Count, 13f, UiTheme.Teal2, true); }
                if (b.Value >= 0) Kit.Text(body.transform, str.Get("mail.keep"), 12f, UiTheme.Soft);
            }
        }

        void Empty(Transform p, Block b)
        {
            var v = Kit.VBox(p, "empty", new Vector4(0, 12, 0, 6), 2); v.childAlignment = TextAnchor.UpperCenter;
            var ic = Kit.IconImage(v.transform, b.Icon, 8, 2); ic.GetComponent<LayoutElement>().flexibleWidth = 0;
            v.childForceExpandWidth = false; v.childControlWidth = true;
            Kit.Text(v.transform, b.Text, 15f, Ink, true, TextAlignmentOptions.Center); Kit.Text(v.transform, b.Sub, 13f, UiTheme.Soft, false, TextAlignmentOptions.Center);
        }

        void Input(Transform p, Block b)
        {
            var f = Kit.Frame(p, "field", null, "input"); Kit.Elem(f.gameObject, minH: 72f);
            var viewport = Kit.Rect("text area", f.transform); Kit.Stretch(viewport, UiRoot.CurrentDot * 3, UiRoot.CurrentDot * 3, UiRoot.CurrentDot * 3, UiRoot.CurrentDot * 3); viewport.gameObject.AddComponent<RectMask2D>();
            var ph = Kit.Text(viewport, b.Sub, 13f, UiTheme.Line2, false, TextAlignmentOptions.TopLeft, "placeholder"); Kit.Stretch(ph.rectTransform);
            var tx = Kit.Text(viewport, b.Text, 13f, Ink, false, TextAlignmentOptions.TopLeft, "text"); Kit.Stretch(tx.rectTransform);
            var inp = f.gameObject.AddComponent<TMP_InputField>(); inp.textViewport = viewport; inp.textComponent = tx; inp.placeholder = ph; inp.lineType = TMP_InputField.LineType.MultiLineNewline; inp.text = b.Text ?? "";
            inp.onEndEdit.AddListener(s => onInput?.Invoke(s));
        }
    }
}
