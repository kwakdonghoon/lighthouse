using System;
using System.Collections.Generic;
using System.IO;
using Lighthouse.Core;
using Lighthouse.UI.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>
    /// UI 의 뿌리: 캔버스·화면 크기(CanvasScaler 와 같은 식)·안전 영역·도트 크기, 그리고 화면들(HUD·팝업·토스트·안내·타이틀·사진 모드·테스트 광고).
    /// Core 상태를 읽어 보여 주고 입력을 PanelController 로 넘기는 얇은 계층이다. 월드(WorldView)는 모르고, 바뀐 일은 이벤트로 알린다.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour, IUiHost
    {
        public static float CurrentDot = 2.25f, CanvasW = 360f, CanvasH = 640f;
        public const string PrefabPath = "UI/Prefabs/UiRoot";

        public Canvas Canvas; public RectTransform CanvasRect; public RawImage Backdrop;
        public HudView Hud; public PanelView Panel; public ToastView Toast; public TipView Tip; public TitleView Title; public HintView Hint; public PhotoBarView Photo; public AdLayerView Ad;

        public GameContext Ctx { get; private set; } public UiStrings Str { get; private set; } public PanelController Ctl { get; private set; }
        public TipEngine Tips { get; private set; } public ToastModel ToastState { get; } = new ToastModel();
        public TestAdAdapter AdAdapter { get; private set; }
        public bool ManualScreen;           // 캡처·시험: 화면 크기를 직접 넣는다
        public Insets Safe = Insets.None; public float Scale = 1f; public int DevW = 360, DevH = 640; public float Density;      // 기기 픽셀 / dp (0 이면 CanvasScaler 식)
        public bool PhotoMode { get; private set; }
        Rect lastSafeRect;
        public RegionFogView Fog;
        public FocusRing Ring; bool navMode; GameObject lastSel;      // 키보드·게임패드 초점 고리
        public bool TitleHold;           // 시험·캡처: 자동으로 닫지 않는다 (원본 ?at=)
        public bool TitleOpen => Title != null && Title.gameObject.activeSelf;

        public event Action<string> WorldDirtyRequested, ViewChanged; public event Action<JObject> SaveImported; public event Action PhotoSaveRequested; public event Action<string> SfxRequested;
        /// <summary>M4: 월드 연결 지점. 예보 입력(날씨 연출이 채운다)·우체통 화면 위치(캔버스 단위, 없으면 임시 자리)·타이틀을 눌렀다/닫혔다.</summary>
        public Func<ForecastInput> ForecastSource; public Func<Vector2?> MailAnchor; public event Action TitleTapped, TitleClosed;
        float hudAt; float titleShownAt; bool titlePanelWasOpen; long adShownAt; bool adShownSent; float titleFadeAt = -1f; long clockNow => Ctx != null ? Ctx.Clock.NowMs : 0;

        // ================= 만들기 =================
        /// <summary>프리팹(Resources/UI/Prefabs/UiRoot)이 있으면 그것을, 없으면 코드로 같은 계층을 만든다.</summary>
        public static UiRoot Create(Camera cam = null, bool forceBuild = false)
        {
            if (!forceBuild)
            {
                var pf = Resources.Load<GameObject>(PrefabPath);
                if (pf != null)
                {
                    var inst = Instantiate(pf); inst.name = "UiRoot"; var r0 = inst.GetComponent<UiRoot>();
                    if (r0 != null && r0.Panel != null) { r0.PrepareCanvas(cam); return r0; }
                    DestroyImmediate(inst);
                }
            }
            var go = new GameObject("UiRoot", typeof(RectTransform)); go.layer = 5;
            var root = go.AddComponent<UiRoot>();
            root.Canvas = go.AddComponent<Canvas>(); go.AddComponent<GraphicRaycaster>();
            root.CanvasRect = (RectTransform)go.transform;
            root.PrepareCanvas(cam);
            root.BuildHierarchy(go.transform);
            return root;
        }

        public void PrepareCanvas(Camera cam)
        {
            Canvas = Canvas != null ? Canvas : GetComponent<Canvas>(); CanvasRect = (RectTransform)transform;
            if (cam != null) { Canvas.renderMode = RenderMode.ScreenSpaceCamera; Canvas.worldCamera = cam; Canvas.planeDistance = 1f; }
            else Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = GetComponent<CanvasScaler>(); if (scaler != null) scaler.enabled = false;      // 같은 식을 ApplyScreen 이 직접 쓴다
            if (FindFirstObjectByType<EventSystem>() == null && Application.isPlaying) { var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); }
        }

        public void BuildHierarchy(Transform root)
        {
            var strings = BootStrings;
            var bd = Kit.Rect("backdrop", root); Kit.Stretch(bd); Backdrop = bd.gameObject.AddComponent<RawImage>(); Backdrop.raycastTarget = false; Backdrop.color = Color.clear;
            Hud = HudView.Create(root); Panel = PanelView.Create(root); Toast = ToastView.Create(root); Tip = TipView.Create(root);
            Hint = strings != null ? HintView.Create(root, strings) : null; Photo = strings != null ? PhotoBarView.Create(root, strings) : null; Ad = strings != null ? AdLayerView.Create(root, strings) : null;
            Title = TitleView.Create(root); Title.gameObject.SetActive(false);
        }
        public static UiStrings BootStrings;        // 프리팹을 만들 때만 쓰는 문구(실행 중에는 Setup 의 문구)

        // ================= 연결 =================
        public void Setup(GameContext ctx, UiStrings str, IPaymentAdapter payment = null, TestAdAdapter ads = null, SaveCode code = null, UiFeatures features = null)
        {
            Ctx = ctx; Str = str; AdAdapter = ads ?? new TestAdAdapter();
            Ctl = new PanelController(ctx, str, this, payment ?? new TestPaymentAdapter(), AdAdapter, code, features);
            Tips = new TipEngine(ctx, str);
            Panel.Bind(Ctl, str);
            Hud.Menu.onClick.RemoveAllListeners(); Hud.Menu.onClick.AddListener(() => { Ctl.Open(Ctl.MenuTarget()); Panel.Refresh(false); });
            Tip.Body.onClick.RemoveAllListeners(); Tip.Body.onClick.AddListener(() => { Tips.Hide(clockNow); Tip.Hide(); });
            Tip.Off.onClick.RemoveAllListeners(); Tip.Off.onClick.AddListener(() => { Tips.TurnOff(); Tip.Hide(); });
            if (Photo != null) { Photo.Back.onClick.AddListener(() => SetPhoto(false)); Photo.Save.onClick.AddListener(() => { if (PhotoSaveRequested != null) PhotoSaveRequested(); else SetPhotoMessage("photo.saved"); }); }      // 저장 결과 글은 저장한 쪽(GameApp)이 SetPhotoMessage 로 알린다
            if (Ad != null) { Ad.Ok.onClick.AddListener(() => AdAdapter.Finish(true)); Ad.Cancel.onClick.AddListener(() => AdAdapter.Finish(false, "closed")); }
            if (Ring == null) Ring = FocusRing.Create(transform);
            Ctl.ScreenH = CanvasH;
            Title.Tap.onClick.RemoveAllListeners(); Title.Tap.onClick.AddListener(CloseTitle);
            Title.SfxRequested += Sfx;      // 인트로 효과음(stone x5·build·lamp)은 UiRoot.Sfx 로(AudioBridge 가 받아 재생)
            if (!ManualScreen) Density = Application.isMobilePlatform && Screen.dpi > 0 ? Mathf.Clamp(Screen.dpi / 160f, 1f, 5f) : 0f;
            if (ManualScreen) ApplyScreen(DevW, DevH, Safe); else ApplyScreen(Screen.width, Screen.height, ReadSafe());
            RefreshHud(true);
        }

        /// <summary>타이틀을 연다. first = 첫 실행(원본: 안내한 배 0 이고 편지 없음). intro = 약 9초 인트로(첫 실행이고 움직임 줄이기가 아닐 때, 시험·캡처는 강제로 켤 수 있다).</summary>
        public void ShowTitle(bool first, bool? intro = null)
        {
            Title.Apply(Str, Hud_.TitleDay(Ctx, Str, first), first, intro ?? first); Title.gameObject.SetActive(true); Title.Group.alpha = 1f; titleFadeAt = -1f; Ctx.TitleOpen = true; Title.transform.SetAsLastSibling();
            titleShownAt = Time.realtimeSinceStartup; titlePanelWasOpen = Ctl != null && Ctl.Mode != PanelMode.None;
        }

        /// <summary>타이틀을 닫기 시작한다(누름·키·자동·창 열림 어느 쪽이든 한 번만). 7단 페이드 뒤 TitleClosed.</summary>
        public void CloseTitle()
        {
            if (!TitleOpen || titleFadeAt >= 0) return;
            titleFadeAt = Time.realtimeSinceStartup; TitleTapped?.Invoke();
        }
        /// <summary>첫 실행 안내를 흘려 보낸다(1.2초 뒤 나타나 10초 동안). 타이틀 아래에서 이미 지나가기 시작한다(원본과 같다).</summary>
        public void PlayHint() { if (Hint != null) { Hint.Layout(CanvasW, CanvasH); Hint.Play(); LayoutRebuilder.ForceRebuildLayoutImmediate(Hint.Box); } }
        public void ShowHint(bool on) { if (Hint != null) { Hint.gameObject.SetActive(on); Hint.Layout(CanvasW, CanvasH); LayoutRebuilder.ForceRebuildLayoutImmediate(Hint.Box); } }

        // ================= 화면 크기 =================
        Insets ReadSafe()
        {
            var sa = Screen.safeArea; float s = ScreenLayout.Scale(Screen.width, Screen.height, Density);
            return new Insets(sa.xMin / s, (Screen.height - sa.yMax) / s, (Screen.width - sa.xMax) / s, sa.yMin / s);
        }

        /// <summary>기기 픽셀 크기와 안전 영역(캔버스 단위)을 받아 배율·도트·레이아웃을 다시 계산한다.</summary>
        public void ApplyScreen(int devW, int devH, Insets safe)
        {
            DevW = devW; DevH = devH; Safe = safe;
            Scale = ScreenLayout.Scale(devW, devH, Density); Canvas.scaleFactor = Scale;
            ScreenLayout.CanvasSize(devW, devH, out CanvasW, out CanvasH, Density);
            CurrentDot = ScreenLayout.DotUnits(Scale, out _);
            foreach (var m in GetComponentsInChildren<MonoBehaviour>(true)) if (m is IDotScaled d) d.ApplyDot(CurrentDot);
            foreach (var ic in GetComponentsInChildren<UiIcon>(true)) ic.Apply();
            Hud.Layout(safe); Panel.Layout(safe); Toast.Layout(CanvasH); if (Hint != null) Hint.Layout(CanvasW, CanvasH); if (Photo != null) Photo.Layout(CanvasW, safe); Title.Layout(safe, devW, devH, Scale);
            if (Ctl != null) { Ctl.ScreenH = CanvasH; Panel.Refresh(true); RefreshHud(true); }
            Canvas.ForceUpdateCanvases();
        }

        public void SetBackdrop(Texture tex) { Backdrop.texture = tex; Backdrop.color = tex != null ? Color.white : Color.clear; }

        // ================= 매 프레임 =================
        void Update()
        {
            if (Ctx == null) return;
            if (!ManualScreen && (Screen.width != DevW || Screen.height != DevH || Screen.safeArea != lastSafeRect)) { lastSafeRect = Screen.safeArea; ApplyScreen(Screen.width, Screen.height, ReadSafe()); }      // 안전 영역(노치·제스처 막대)은 크기가 같아도 바뀔 수 있다
            if (Ctl.Tick()) Panel.Refresh(true);
            UpdateAd(); UpdateToast(); UpdateTitle();
            if (Time.unscaledTime >= hudAt) { hudAt = Time.unscaledTime + 0.25f; RefreshHud(false); UpdateTips(); }
            UpdateInput();
        }

        // ================= 키보드·게임패드·뒤로 가기 =================
        void UpdateInput()
        {
            // 안드로이드 뒤로 가기: 열린 것(사진 모드·팝업·광고·타이틀)이 있으면 앱을 나가지 않고 그것만 닫는다(원본 popstate 2975행). 없으면 평소처럼 앱을 나간다.
            Input.backButtonLeavesApp = !(PhotoMode || Ctl.Mode != PanelMode.None || (AdAdapter != null && AdAdapter.Showing) || TitleOpen);
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1)) OnBack();          // 취소 = Esc · 뒤로 가기 · 게임패드 B
            HandleTab(); HandleDecoKeys(); UpdateFocus(); KeepInputAboveKeyboard();
        }

        readonly Vector3[] kbCorners = new Vector3[4];
        /// <summary>폰의 화상 키보드가 올라와 저장 코드 칸을 가리면, 칸이 키보드 위에 오도록 팝업 본문을 밀어 올린다(원본: 브라우저가 입력 칸을 자동으로 보이게 한다). 실기기 미확인.</summary>
        void KeepInputAboveKeyboard()
        {
            if (Panel == null || !Panel.IsOpen || Panel.Scroll == null || !TouchScreenKeyboard.visible) return;
            var es = EventSystem.current; var go = es != null ? es.currentSelectedGameObject : null; var inp = go != null ? go.GetComponent<TMPro.TMP_InputField>() : null; if (inp == null) return;
            float kb = TouchScreenKeyboard.area.height; if (kb <= 0f) return;
            ((RectTransform)inp.transform).GetWorldCorners(kbCorners); float bottomPx = kbCorners[0].y;      // 오버레이 캔버스의 세계 좌표 = 화면 픽셀(아래가 0)
            float need = (kb + 12f * Scale - bottomPx) / Mathf.Max(0.01f, Scale); if (need < 1f) return;
            var content = Panel.Scroll.content; float max = Mathf.Max(0f, content.rect.height - Panel.Scroll.viewport.rect.height);
            var p = content.anchoredPosition; p.y = Mathf.Clamp(p.y + need, 0f, max); content.anchoredPosition = p;
        }

        /// <summary>초점이 돌아다닐 수 있는 범위(원본의 포커스 가두기): 광고 화면 > 사진 막대 > 팝업 > 표시 줄. 타이틀 중에는 없다.</summary>
        Transform FocusScope() => AdAdapter != null && AdAdapter.Showing && Ad != null ? Ad.transform : PhotoMode && Photo != null ? Photo.transform : Ctl.Mode != PanelMode.None ? Panel.transform : TitleOpen ? null : Hud.transform;

        static bool Usable(Selectable s) => s != null && s.IsInteractable() && s.gameObject.activeInHierarchy && s.gameObject.name != "dim";

        GameObject FocusFallback()
        {
            if (AdAdapter != null && AdAdapter.Showing && Ad != null) return Ad.Ok.interactable ? Ad.Ok.gameObject : Ad.Cancel.gameObject;
            if (PhotoMode && Photo != null) return Photo.Save.gameObject;
            if (Ctl.Mode != PanelMode.None)
            {
                for (int i = 0; i < Panel.TabButtons.Count; i++) if (Panel.Model != null && Panel.Model.Tabs.Exists(t => t.On && System.Array.IndexOf(PanelView.TabIdList, t.Id) == i) && Usable(Panel.TabButtons[i])) return Panel.TabButtons[i].gameObject;
                return Panel.Close.gameObject;
            }
            return Hud.Menu.gameObject;
        }

        /// <summary>Tab / Shift+Tab: 범위 안의 단추를 차례로(광고 화면은 [끝내고 받기]와 [그만두기]를 번갈아, 원본 1815행).</summary>
        void HandleTab()
        {
            if (!Input.GetKeyDown(KeyCode.Tab)) return;
            var scope = FocusScope(); var es = EventSystem.current; if (scope == null || es == null) return;
            bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var list = new List<Selectable>(); foreach (var s in scope.GetComponentsInChildren<Selectable>(false)) if (Usable(s)) list.Add(s);
            if (list.Count == 0) return;
            var cur = es.currentSelectedGameObject != null ? es.currentSelectedGameObject.GetComponent<Selectable>() : null; int i = list.IndexOf(cur);
            i = i < 0 ? (back ? list.Count - 1 : 0) : (i + (back ? -1 : 1) + list.Count) % list.Count;
            es.SetSelectedGameObject(list[i].gameObject);
        }

        /// <summary>꾸미기 놓기의 키: Ctrl/Cmd+Z 되돌리기(+Shift 다시), 구역 칩 ←→·Home·End 로 옮기며 바로 고르기, 자리 격자 화살표로 칸 옮기기(원본 3946~3961행).</summary>
        void HandleDecoKeys()
        {
            if (Ctl.Mode != PanelMode.Deco || Ctl.DecoTab != "place" || PhotoMode || (AdAdapter != null && AdAdapter.Showing)) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (ctrl && Input.GetKeyDown(KeyCode.Z)) { bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); Ctl.Act(shift ? "redo" : "undo"); Panel.Refresh(true); return; }
            var es = EventSystem.current; var go = es != null ? es.currentSelectedGameObject : null; var tag = go != null ? go.GetComponent<ActTag>() : null; if (tag == null || tag.Act == null) return;
            bool right = Input.GetKeyDown(KeyCode.RightArrow), left = Input.GetKeyDown(KeyCode.LeftArrow), home = Input.GetKeyDown(KeyCode.Home), end = Input.GetKeyDown(KeyCode.End), up = Input.GetKeyDown(KeyCode.UpArrow), down = Input.GetKeyDown(KeyCode.DownArrow);
            if (tag.Act.StartsWith("zone:") && (right || left || home || end))
            {
                var zs = new List<ActTag>(); foreach (var t in tag.transform.parent.GetComponentsInChildren<ActTag>(false)) if (t.Act != null && t.Act.StartsWith("zone:")) zs.Add(t);
                int i = zs.IndexOf(tag), n = home ? 0 : end ? zs.Count - 1 : (i + (right ? 1 : -1) + zs.Count) % zs.Count;
                Ctl.Act(zs[n].Act); Panel.Refresh(true);
            }
            else if ((tag.Act.StartsWith("slot:") || tag.Act.StartsWith("slotlock:")) && (right || left || up || down))
            {
                var gs = new List<ActTag>(); foreach (var t in tag.transform.parent.GetComponentsInChildren<ActTag>(false)) if (t.Act != null && (t.Act.StartsWith("slot:") || t.Act.StartsWith("slotlock:"))) gs.Add(t);
                var fc = tag.transform.parent.GetComponent<FixedColumnsLayout>(); int cols = fc != null ? Mathf.Max(1, fc.Columns) : 4;
                int i = gs.IndexOf(tag), j = i + (right ? 1 : left ? -1 : down ? cols : -cols);
                if (j >= 0 && j < gs.Count) es.SetSelectedGameObject(gs[j].gameObject);
            }
        }

        /// <summary>마우스·터치 말고 키·게임패드로 움직이는 중이면 초점 고리를 보이고, 초점을 잃었으면 범위 안의 알맞은 단추로 되돌린다(원본 :focus-visible).</summary>
        void UpdateFocus()
        {
            var es = EventSystem.current; if (es == null || Ring == null) return;
            bool pointer = Input.GetMouseButtonDown(0) || Input.touchCount > 0;
            bool keyNav = Input.anyKeyDown && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1) && !Input.GetMouseButtonDown(2);
            bool axisNav = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.5f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.5f;
            if (pointer) navMode = false; else if (keyNav || axisNav) navMode = true;
            var sel = es.currentSelectedGameObject; var scope = FocusScope();
            if (navMode && scope != null && (sel == null || !sel.activeInHierarchy || !sel.transform.IsChildOf(scope))) { var f = FocusFallback(); if (f != null) { es.SetSelectedGameObject(f); sel = f; } }
            if (sel != lastSel) { lastSel = sel; if (navMode && sel != null && Panel != null && sel.transform.IsChildOf(Panel.Content)) ScrollKit.IntoView(Panel.Scroll, (RectTransform)sel.transform); }
            Ring.Show(navMode && scope != null && sel != null ? sel.transform as RectTransform : null, CanvasRect);
        }

        public void OnBack()
        {
            if (AdAdapter != null && AdAdapter.Showing) { AdAdapter.Finish(false, "closed"); return; }          // 광고 화면: 뒤로 가기·Esc = 그만두기(원본 1814행)
            if (TitleOpen) return;
            if (PhotoMode) { SetPhoto(false); return; }
            if (Ctl.Mode == PanelMode.None) return;
            if (Ctl.Mode == PanelMode.Settings) Ctl.LeaveSettings(); else if (!Ctl.DecoEsc()) Ctl.Close();
            Panel.Refresh(false);
        }

        void RefreshHud(bool force)
        {
            var info = Hud_.Compute(Ctx, Str, ForecastSource?.Invoke());
            Hud.Apply(info, Str, PhotoMode ? "" : Badges.Menu(Ctx.UnreadCount(), Ctx.Discovered() > Ctx.S.DexSeen));
        }

        /// <summary>사진 막대의 글(원본 photomsg): 키는 ui.json 의 photo.* (hidden·saved·shared·notSaved·failed).</summary>
        public void SetPhotoMessage(string key) { if (Photo != null) Photo.Msg.text = UiText.Display(Str.Get(key)); }

        void UpdateToast() { if (PhotoMode) { ToastState.Tick(clockNow); if (Toast.gameObject.activeSelf) Toast.gameObject.SetActive(false); return; }      // 사진 모드에서는 토스트가 뜨지 않는다(원본 body.photo #toast)
            if (ToastState.Tick(clockNow)) Toast.Apply(ToastState, clockNow); else if (ToastState.Visible) Toast.Apply(ToastState, clockNow); else if (Toast.gameObject.activeSelf) Toast.gameObject.SetActive(false); }

        void UpdateTitle()
        {
            if (TitleOpen && titleFadeAt < 0)
            {
                // 원본 5602~5605행: 누름·키·자동(인트로 15.6초 · 첫 실행 6.4초 · 그 밖 3.6초)·창이 열리면 닫힌다
                bool key = Input.anyKeyDown || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
                bool auto = !TitleHold && Time.realtimeSinceStartup - titleShownAt >= Title.AutoCloseSec;
                bool opened = Ctl != null && Ctl.Mode != PanelMode.None && !titlePanelWasOpen;
                if (key || auto || opened) CloseTitle();
            }
            if (!TitleOpen || titleFadeAt < 0) return;
            float t = (Time.realtimeSinceStartup - titleFadeAt) / 0.7f; Title.Group.alpha = 1f - Mathf.Floor(Mathf.Clamp01(t) * 7f) / 7f;      // steps(7) 와 같은 7단 페이드
            if (t >= 1f) { Title.gameObject.SetActive(false); Ctx.TitleOpen = false; TitleClosed?.Invoke(); }
        }

        void UpdateAd()
        {
            if (Ad == null) return;
            bool show = AdAdapter.Showing; if (show != Ad.gameObject.activeSelf) { Ad.gameObject.SetActive(show); if (show) { adShownAt = DateTime.UtcNow.Ticks; adShownSent = false; Ad.transform.SetAsLastSibling(); if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(Ad.Cancel.gameObject); } }
            if (!show) return;
            if (!adShownSent) { Ctl.AdShown(); adShownSent = true; }
            double sec = (DateTime.UtcNow.Ticks - adShownAt) / 1e7; int left = Mathf.Max(0, 3 - (int)sec); Ad.SetReady(left <= 0, left, Str);
        }

        void UpdateTips()
        {
            if (Tips == null || PhotoMode) { Tip?.Hide(); return; }
            long now = clockNow; var env = new TipEnv { TitleOpen = TitleOpen, PanelOpen = Ctl.Mode != PanelMode.None, PanelMode = Ctl.Mode, DecoTab = Ctl.DecoTab, Building = Ctx.S.Build != null, CanUpgrade = false, ToastVisible = ToastState.Visible };
            var shown = Tips.Update(now, env, out bool hid);
            if (hid) Tip.Hide();
            if (shown != null) PlaceTip(shown, now);
        }

        void PlaceTip(TipInfo t, long now)
        {
            float cx = CanvasW / 2f, top = Mathf.Max(CanvasH * 0.18f, ScreenLayout.HudTop(Safe) + 130f); bool up = false;
            if (t.At == "menu") { var r = Hud.MenuRect; cx = CanvasW - 8 - Safe.R - 24; top = 8 + Safe.T + 48 + 12; up = true; top = Mathf.Max(top, ScreenLayout.HudTop(Safe) + 120f); }
            else if (t.At == "mail") { var ma = MailAnchor?.Invoke(); if (ma.HasValue) { cx = ma.Value.x; top = ma.Value.y; } else { cx = CanvasW * 0.62f; top = CanvasH * 0.66f; } up = true; }          // 우체통 위치는 월드(M4)가 알려 준다
            else if (t.At == "panel")
            {
                var card = Panel.CardRect; float bottom = -card.anchoredPosition.y + card.sizeDelta.y;
                if (bottom + 12 + 90 + 8 <= CanvasH) { top = bottom + 12; up = true; }
                else { Tips.Defer(t, now); return; }
            }
            Tip.Show(t, Str, cx, top, up, CanvasW);
        }

        // ================= 말풍선이 피할 화면 요소 =================
        readonly Vector3[] corners = new Vector3[4];
        /// <summary>말풍선이 비켜 가야 할 보이는 요소(표시 줄·메뉴 단추·안내 카드·토스트)의 사각형을 캔버스 단위(왼쪽 위 원점)로 담는다(원본 5312행의 uis).</summary>
        public void CollectAvoidRects(List<UiRectF> list)
        {
            list.Clear();
            void Add(Component c, bool on)
            {
                if (c == null || !on || !c.gameObject.activeInHierarchy) return;
                var rt = (RectTransform)c.transform; if (rt.rect.width <= 0.5f || rt.rect.height <= 0.5f) return;
                rt.GetWorldCorners(corners); var a = CanvasRect.InverseTransformPoint(corners[0]); var b = CanvasRect.InverseTransformPoint(corners[2]); var cr = CanvasRect.rect;
                list.Add(new UiRectF(a.x - cr.xMin, cr.yMax - b.y, b.x - cr.xMin, cr.yMax - a.y));
            }
            if (Hud != null) { Add(Hud.Pill, !PhotoMode); Add(Hud.MenuRect, !PhotoMode); }
            if (Tip != null) Add(Tip.Box, Tip.gameObject.activeSelf);
            if (Toast != null) Add(Toast.Box, ToastState.Visible);
        }

        // ================= 사진 모드 =================
        public void SetPhoto(bool on)
        {
            PhotoMode = on; if (Photo != null) { Photo.gameObject.SetActive(on); if (on) { Photo.Msg.text = UiText.Display(Str.Get("photo.hidden")); if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(Photo.Save.gameObject); } }      // 원본 3161행: 저장하기 단추에 초점
            Hud.gameObject.SetActive(!on); if (on) { Tip.Hide(); ToastState.Visible = false; Toast.gameObject.SetActive(false); Panel.gameObject.SetActive(false); }
        }

        /// <summary>바다 전환(원본 switchRegion): 어두운 물안개가 덮이는 동안 바다를 바꾸고(팝업 닫기 포함) 걷히면 도착을 알린다.</summary>
        public void SwitchRegion(string id, string name, Action commit, Action arrive)
        {
            if (Fog == null) Fog = RegionFogView.Create(transform);
            Fog.Play(name, Str.Get("region.sailing"), Lighthouse.Core.MotionPref.Reduce, () => { commit(); Panel.Refresh(false); }, arrive);
        }

        // ================= IUiHost =================
        public void Sfx(string name) { SfxRequested?.Invoke(name); }
        public void WorldDirty() { WorldDirtyRequested?.Invoke("dirty"); }
        public void StartPhoto() { SetPhoto(true); }
        public void ApplyView(string view) { ViewChanged?.Invoke(view); }
        public void ApplyWake(bool on) { Screen.sleepTimeout = on ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting; }
        public bool WakeSupported => Application.isMobilePlatform || Application.isEditor;
        void IUiHost.Toast(string title, string sub) { ToastState.Show(title, sub, clockNow); Toast.Apply(ToastState, clockNow); }
        public void CopyText(string text) { GUIUtility.systemCopyBuffer = text; }
        public void SaveTextFile(string name, string text) { try { File.WriteAllText(Path.Combine(Application.persistentDataPath, name), text); } catch (Exception) { } }
        public void ImportSave(JObject data) { SaveImported?.Invoke(data); }
        public void ClosePanelRequested() { Panel.Refresh(false); }

        /// <summary>코드에서 토스트를 띄운다.</summary>
        public void ShowToast(string title, string sub = null) { ((IUiHost)this).Toast(title, sub); }
    }

    /// <summary>모호한 이름 피하기용 별칭.</summary>
    static class Hud_
    {
        public static HudInfo Compute(GameContext c, UiStrings s, ForecastInput fc = null) => Hud.Compute(c, s, fc);
        public static string TitleDay(GameContext c, UiStrings s, bool first) => Hud.TitleDay(c, s, first);
    }
}
