using System;
using System.Collections.Generic;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>
    /// 팝업(편지·도감·성장·꾸미기·상점·설정·소식) 한 장. 뼈대(딤·카드·리본·머리 단추·탭·스크롤)는 Build 가 만들어 프리팹으로 저장하고,
    /// 본문은 PanelController.Build() 블록을 BlockRenderer 로 그린다. 규칙은 모르고 입력만 컨트롤러로 넘긴다.
    /// </summary>
    public sealed class PanelView : MonoBehaviour
    {
        public RectTransform CardRect, Content, TabsRow, ViewportRect;
        public Image Dim, CardImg;
        /// <summary>M4: 꾸미기 놓기에서 팝업 바깥(섬 쪽) 탭 — 섬 위 자리 고르기로 쓴다(원본 5.2970행). 화면 좌표(픽셀).</summary>
        public event System.Action<Vector2> DecoDimTapped;
        public TMP_Text Heading;
        public Button Gear, Sound, Close, DimButton;
        public ScrollRect Scroll;
        public List<Button> TabButtons = new List<Button>();
        public List<GameObject> TabBadges = new List<GameObject>();
        public RectTransform RibbonRow, HeaderButtons;
        public PanelController Ctl; public UiStrings Str; PanelMode lastMode = PanelMode.None; string lastSub;
        BlockRenderer renderer;
        public PanelModel Model { get; private set; }
        public bool IsOpen => Ctl != null && Ctl.Mode != PanelMode.None;
        public event Action Changed;

        static readonly string[] TabIds = { "mail", "dex", "growth", "deco", "shop" };
        public static string[] TabIdList => TabIds;
        static readonly string[] TabIcons = { "mail", "book", "star", "starfish", "bag" };

        // ---------- 뼈대 ----------
        public static PanelView Create(Transform parent)
        {
            var root = Kit.Rect("PopupPanel", parent); Kit.Stretch(root);
            var pv = root.gameObject.AddComponent<PanelView>();
            pv.Dim = Kit.Solid(root, UiTheme.Dim, "dim"); Kit.Stretch(pv.Dim.rectTransform); pv.Dim.raycastTarget = true;
            pv.DimButton = pv.Dim.gameObject.AddComponent<Button>(); pv.DimButton.transition = Selectable.Transition.None; pv.DimButton.targetGraphic = pv.Dim;

            pv.CardImg = Kit.Frame(root, "card", null, "card"); pv.CardRect = pv.CardImg.rectTransform;
            var v = pv.CardImg.gameObject.AddComponent<VerticalLayoutGroup>(); v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var dl = pv.CardImg.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(4, 4, 4, 6); dl.SpacingDots = 1; dl.ApplyDot(UiRoot.CurrentDot);

            // 리본(제목 + 줄무늬 막대)
            pv.RibbonRow = Kit.HBox(pv.CardRect, "ribbon", new Vector4(4, 2, 0, 1), 3, TextAnchor.MiddleLeft, false).GetComponent<RectTransform>();
            Kit.Elem(pv.RibbonRow.gameObject, minH: 44f);
            var stripe = Kit.Rect("stripe", pv.RibbonRow); var simg = stripe.gameObject.AddComponent<Image>(); simg.sprite = UiTheme.Frame("stripe"); simg.preserveAspect = true; simg.raycastTarget = false;
            var sd = stripe.gameObject.AddComponent<DotSize>(); sd.Elem = stripe.gameObject.AddComponent<LayoutElement>(); sd.Px = 5; sd.Mult = 2; sd.ApplyDot(UiRoot.CurrentDot);
            pv.Heading = Kit.Text(pv.RibbonRow, "", 21f, UiTheme.Teal2, true, TextAlignmentOptions.MidlineLeft, "heading");
            Kit.Elem(pv.Heading.gameObject, flexW: 1);
            var rpad = Kit.Rect("reserve", pv.RibbonRow); Kit.Elem(rpad.gameObject, minW: 3 * 44f + 8f);        // 오른쪽 머리 단추 3개 자리(겹침 방지)

            // 탭 줄
            pv.TabsRow = Kit.HBox(pv.CardRect, "tabs", new Vector4(2, 0, 2, 2), 3, TextAnchor.MiddleCenter, true).GetComponent<RectTransform>();
            pv.TabsRow.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            foreach (var id in TabIds)
            {
                var b = Kit.FrameButton(pv.TabsRow, "main", "main_p", "", 12.5f, Color.white, null, "tab:" + id, true, 50f, 1f);
                var lab = b.transform.Find("label").GetComponent<TMP_Text>(); lab.fontSize = 12.5f; lab.textWrappingMode = TextWrappingModes.NoWrap;
                // 아이콘은 라벨 앞에
                var ic = Kit.IconImage(b.transform, TabIcons[Array.IndexOf(TabIds, id)]); ic.transform.SetSiblingIndex(0);
                var ds = ic.GetComponent<DotSize>(); ds.Mult = 1; ds.ApplyDot(UiRoot.CurrentDot);
                Kit.Elem(b.gameObject, flexW: 1, minW: 40f, minH: 52f, prefH: 52f);
                var badge = Kit.Solid(b.transform, UiTheme.Red, "badge"); var br = badge.rectTransform;
                br.anchorMin = br.anchorMax = new Vector2(1, 1); br.pivot = new Vector2(0.5f, 0.5f); br.sizeDelta = new Vector2(12, 12); br.anchoredPosition = new Vector2(-6, -6);
                badge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true; badge.gameObject.SetActive(false);
                pv.TabButtons.Add(b); pv.TabBadges.Add(badge.gameObject);
            }

            // 스크롤 본문
            var scrollRt = Kit.Rect("scroll", pv.CardRect); Kit.Elem(scrollRt.gameObject, minH: 120f, flexH: 1, flexW: 1);
            pv.Scroll = scrollRt.gameObject.AddComponent<ScrollRect>(); pv.Scroll.horizontal = false; pv.Scroll.vertical = true; pv.Scroll.movementType = ScrollRect.MovementType.Clamped; pv.Scroll.scrollSensitivity = 30f;
            var scImg = scrollRt.gameObject.AddComponent<Image>(); scImg.color = new Color(0, 0, 0, 0);
            pv.ViewportRect = Kit.Rect("viewport", scrollRt); Kit.Stretch(pv.ViewportRect); pv.ViewportRect.gameObject.AddComponent<RectMask2D>();
            var vpImg = pv.ViewportRect.gameObject.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0);
            pv.Content = Kit.Rect("content", pv.ViewportRect); pv.Content.anchorMin = new Vector2(0, 1); pv.Content.anchorMax = new Vector2(1, 1); pv.Content.pivot = new Vector2(0.5f, 1); pv.Content.offsetMin = pv.Content.offsetMax = Vector2.zero;
            var cv = pv.Content.gameObject.AddComponent<VerticalLayoutGroup>(); cv.childControlWidth = cv.childControlHeight = true; cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
            var cdl = pv.Content.gameObject.AddComponent<DotLayout>(); cdl.Group = cv; cdl.PadDots = new Vector4(2, 2, 2, 8); cdl.SpacingDots = 3; cdl.ApplyDot(UiRoot.CurrentDot);
            var fit = pv.Content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            pv.Scroll.viewport = pv.ViewportRect; pv.Scroll.content = pv.Content;

            // 머리 단추(톱니·소리·닫기): 카드 오른쪽 위에 겹쳐 둔다(리본이 그 폭을 비워 둔다)
            pv.HeaderButtons = Kit.HBox(pv.CardRect, "head buttons", Vector4.zero, 1, TextAnchor.UpperRight, false).GetComponent<RectTransform>();
            pv.HeaderButtons.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            pv.Gear = HeadButton(pv.HeaderButtons, "gear", "gear"); pv.Sound = HeadButton(pv.HeaderButtons, "sound", "sound"); pv.Close = HeadButton(pv.HeaderButtons, "close", "close");
            return pv;
        }

        static Button HeadButton(Transform p, string name, string icon)
        {
            var b = Kit.FrameButton(p, "hbtn", "hbtn_p", "", 12f, UiTheme.Ink, null, "hb:" + name, true, 44f);
            Kit.Elem(b.gameObject, minW: 44f, prefW: 44f, minH: 44f, prefH: 44f);
            UnityEngine.Object.DestroyImmediate(b.transform.Find("label").gameObject);
            var ic = Kit.IconImage(b.transform, icon, 8, 1); ic.GetComponent<LayoutElement>().flexibleWidth = 0;
            return b;
        }

        // ---------- 연결 ----------
        public void Bind(PanelController ctl, UiStrings str)
        {
            Ctl = ctl; Str = str;
            renderer = new BlockRenderer(str, a => { ctl.Act(a); Refresh(true); }, s => ctl.ImpText = s);
            for (int i = 0; i < TabButtons.Count; i++) { string id = TabIds[i]; int k = i; TabButtons[i].onClick.RemoveAllListeners(); TabButtons[i].onClick.AddListener(() => { ctl.Act("tab:" + id); Refresh(false); }); }
            Gear.onClick.RemoveAllListeners(); Gear.onClick.AddListener(() => { ctl.Act("gear"); Refresh(false); });
            Close.onClick.RemoveAllListeners(); Close.onClick.AddListener(() => { ctl.Close(); Refresh(false); });
            Sound.onClick.RemoveAllListeners(); Sound.onClick.AddListener(() => { ctl.Act("snd:" + (ctl.S.Sound ? "off" : "on")); Refresh(true); });
            DimButton.onClick.RemoveAllListeners(); DimButton.onClick.AddListener(() => { if (ctl.Mode == PanelMode.Deco && ctl.DecoTab == "place") { DecoDimTapped?.Invoke(Input.mousePosition); return; } ctl.Close(); Refresh(false); });
            Refresh(false);
        }

        /// <summary>컨트롤러 상태를 다시 읽어 그린다.</summary>
        public void Refresh(bool keepScroll)
        {
            if (Ctl == null) return;
            gameObject.SetActive(Ctl.Mode != PanelMode.None);
            if (Ctl.Mode == PanelMode.None) { Changed?.Invoke(); return; }
            var m = Ctl.Build(); Model = m;
            float scrollPos = Scroll.verticalNormalizedPosition; string sub = SubKey(m);
            bool sameScreen = m.Mode == lastMode && sub == lastSub;
            Heading.text = UiText.Display(m.Heading);
            TabsRow.gameObject.SetActive(m.ShowTabs && !(m.Mode == PanelMode.Settings && ScreenLayout.Low(UiRoot.CanvasH)) && !m.Peek);
            HeaderButtons.gameObject.SetActive(true);
            Gear.gameObject.SetActive(m.ShowGear); Sound.gameObject.SetActive(true);
            Gear.GetComponent<Image>().sprite = UiTheme.Frame(m.GearOn ? "hbtn_on" : "hbtn");
            Sound.transform.GetChild(0).GetComponent<UiIcon>().Set(Ctl.S.Sound ? "sound" : "mute");
            foreach (var t in m.Tabs)
            {
                int i = Array.IndexOf(TabIds, t.Id); if (i < 0) continue;
                TabButtons[i].gameObject.SetActive(true);
                var img = TabButtons[i].GetComponent<Image>(); img.sprite = UiTheme.Frame(t.On ? "gold" : "main");
                var st = TabButtons[i].spriteState; st.pressedSprite = UiTheme.Frame(t.On ? "gold_p" : "main_p"); st.highlightedSprite = img.sprite; st.selectedSprite = img.sprite; TabButtons[i].spriteState = st;
                var lab = TabButtons[i].transform.Find("label").GetComponent<TMP_Text>(); lab.text = UiText.Display(t.Label); lab.color = t.On ? UiTheme.OnGold : Color.white;
                Kit.SetBadge(TabBadges[i], t.Badge ? (string.IsNullOrEmpty(t.BadgeText) ? Badges.NewText : t.BadgeText) : "");
            }
            for (int i = 0; i < TabIds.Length; i++) if (!m.Tabs.Exists(t => t.Id == TabIds[i])) TabButtons[i].gameObject.SetActive(false);
            Dim.color = m.Light ? UiTheme.DimLight : UiTheme.Dim;
            renderer.Render(Content, m.Blocks);
            LayoutRebuilder.ForceRebuildLayoutImmediate(CardRect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(Content);
            Scroll.verticalNormalizedPosition = sameScreen && keepScroll ? Mathf.Clamp01(scrollPos) : 1f;
            lastMode = m.Mode; lastSub = sub;
            ApplyLayoutRect();
            ApplyReq(Ctl.TakeReq());
            Changed?.Invoke();
        }

        // ---------- 스크롤·초점 요청(원본 decoAfter 3914행·refocus 4046행) ----------
        ActTag FindTag(string act)
        {
            if (string.IsNullOrEmpty(act)) return null;
            foreach (var t in Content.GetComponentsInChildren<ActTag>(false)) if (t.Act == act) return t;
            return null;
        }
        static Transform FindNamed(Transform root, string name)
        {
            foreach (Transform c in root) { if (c.name == name) return c; var d = FindNamed(c, name); if (d != null) return d; }
            return null;
        }
        Selectable ResolveFocus(string f)
        {
            if (f == "gear") return Gear;
            if (f == "zone.on") { var z = FindTag("zone:" + Ctl.DecoZone); return z != null ? z.GetComponent<Selectable>() : null; }
            if (f == "ph" || f == "gate") { var box = FindNamed(Content, f == "ph" ? "pick" : "gate"); return box != null ? box.GetComponentInChildren<Selectable>(false) : null; }
            var tag = FindTag(f); return tag != null ? tag.GetComponent<Selectable>() : null;
        }
        /// <summary>마지막으로 그린 뒤의 요청을 적용한다: 스크롤(구역 칩 줄을 위로 · 고르기 시트가 보이게 · 바뀐 자리 칸이 보이게)과 초점. 단추가 없으면(사라졌으면) 조용히 넘어간다.</summary>
        public void ApplyReq(FocusRequest r)
        {
            if (r == null || Scroll == null) return;
            Canvas.ForceUpdateCanvases();
            if (r.Scroll == "zones") { var z = FindTag("zone:" + Ctl.DecoZone); if (z != null && z.transform.parent != null) ScrollKit.IntoView(Scroll, (RectTransform)z.transform.parent, true); }
            var sel = ResolveFocus(r.Focus);
            if (sel != null && UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(sel.gameObject);
            if (r.Scroll == "ph") { var box = FindNamed(Content, "pick"); if (box != null) ScrollKit.IntoView(Scroll, (RectTransform)box); }
            if (!string.IsNullOrEmpty(r.Near)) { var c = FindTag("slot:" + r.Near); if (c != null) ScrollKit.IntoView(Scroll, (RectTransform)c.transform); }
        }

        string SubKey(PanelModel m) => m.Mode + "|" + Ctl.GrowSub + "|" + Ctl.DexSub + "|" + Ctl.DecoTab + "|" + Ctl.DecoZone;

        // ---------- 화면 크기 ----------
        Insets safe = Insets.None;
        public void Layout(Insets sa) { safe = sa; ApplyLayoutRect(); }
        void ApplyLayoutRect()
        {
            if (CardRect == null) return;
            var m = Model; bool deco = m != null && m.Mode == PanelMode.Deco;
            var r = ScreenLayout.PanelCard(UiRoot.CanvasW, UiRoot.CanvasH, safe, deco, m != null && m.Peek, 110f);
            CardRect.anchorMin = CardRect.anchorMax = new Vector2(0, 1); CardRect.pivot = new Vector2(0, 1);
            CardRect.anchoredPosition = new Vector2(r.X, -r.Y); CardRect.sizeDelta = new Vector2(r.W, r.H);
            // 머리 단추: 카드 오른쪽 위
            var hb = HeaderButtons; hb.anchorMin = new Vector2(1, 1); hb.anchorMax = new Vector2(1, 1); hb.pivot = new Vector2(1, 1);
            float d = UiRoot.CurrentDot; hb.anchoredPosition = new Vector2(-4 * d, -3 * d); hb.sizeDelta = new Vector2(3 * 44f + 2f, 44f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(CardRect);
        }

        /// <summary>시험·감사용: 지금 보이는 상호작용 요소(단추)의 화면 사각형(캔버스 단위).</summary>
        public List<(string name, Rect rect)> InteractiveRects(RectTransform canvas)
        {
            var list = new List<(string, Rect)>(); var corners = new Vector3[4];
            foreach (var b in GetComponentsInChildren<Button>(false))
            {
                if (!b.isActiveAndEnabled || !b.interactable) continue;
                var rt = (RectTransform)b.transform; rt.GetWorldCorners(corners);
                var a = canvas.InverseTransformPoint(corners[0]); var c = canvas.InverseTransformPoint(corners[2]);
                list.Add((b.name, Rect.MinMaxRect(a.x, a.y, c.x, c.y)));
            }
            return list;
        }
    }
}
