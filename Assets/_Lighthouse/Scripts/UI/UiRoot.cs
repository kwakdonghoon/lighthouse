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
        public bool TitleOpen => Title != null && Title.gameObject.activeSelf;

        public event Action<string> WorldDirtyRequested, ViewChanged; public event Action<JObject> SaveImported; public event Action PhotoSaveRequested; public event Action<string> SfxRequested;
        /// <summary>M4: 월드 연결 지점. 예보 입력(날씨 연출이 채운다)·우체통 화면 위치(캔버스 단위, 없으면 임시 자리)·타이틀을 눌렀다/닫혔다.</summary>
        public Func<ForecastInput> ForecastSource; public Func<Vector2?> MailAnchor; public event Action TitleTapped, TitleClosed;
        float hudAt; long adShownAt; bool adShownSent; float titleFadeAt = -1f; long clockNow => Ctx != null ? Ctx.Clock.NowMs : 0;

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
            if (Photo != null) { Photo.Back.onClick.AddListener(() => SetPhoto(false)); Photo.Save.onClick.AddListener(() => { PhotoSaveRequested?.Invoke(); Photo.Msg.text = UiText.Display(str.Get("photo.saved")); }); }
            if (Ad != null) { Ad.Ok.onClick.AddListener(() => AdAdapter.Finish(true)); Ad.Cancel.onClick.AddListener(() => AdAdapter.Finish(false, "closed")); }
            Title.Tap.onClick.RemoveAllListeners(); Title.Tap.onClick.AddListener(() => { titleFadeAt = Time.realtimeSinceStartup; TitleTapped?.Invoke(); });
            if (!ManualScreen) Density = Application.isMobilePlatform && Screen.dpi > 0 ? Mathf.Clamp(Screen.dpi / 160f, 1f, 5f) : 0f;
            if (ManualScreen) ApplyScreen(DevW, DevH, Safe); else ApplyScreen(Screen.width, Screen.height, ReadSafe());
            RefreshHud(true);
        }

        public void ShowTitle(bool first)
        {
            Title.Apply(Str, Hud_.TitleDay(Ctx, Str, first), first); Title.gameObject.SetActive(true); Title.Group.alpha = 1f; titleFadeAt = -1f; Ctx.TitleOpen = true; Title.transform.SetAsLastSibling();
        }
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
            if (Ctl != null) { Panel.Refresh(true); RefreshHud(true); }
            Canvas.ForceUpdateCanvases();
        }

        public void SetBackdrop(Texture tex) { Backdrop.texture = tex; Backdrop.color = tex != null ? Color.white : Color.clear; }

        // ================= 매 프레임 =================
        void Update()
        {
            if (Ctx == null) return;
            if (!ManualScreen && (Screen.width != DevW || Screen.height != DevH)) ApplyScreen(Screen.width, Screen.height, ReadSafe());
            if (Ctl.Tick()) Panel.Refresh(true);
            UpdateAd(); UpdateToast(); UpdateTitle();
            if (Time.unscaledTime >= hudAt) { hudAt = Time.unscaledTime + 0.25f; RefreshHud(false); UpdateTips(); }
            if (Input.GetKeyDown(KeyCode.Escape)) OnBack();
        }

        public void OnBack()
        {
            if (PhotoMode) { SetPhoto(false); return; }
            if (Ctl.Mode == PanelMode.None) return;
            if (Ctl.Mode == PanelMode.Settings) Ctl.LeaveSettings(); else if (!Ctl.DecoEsc()) Ctl.Close();
            Panel.Refresh(false);
        }

        void RefreshHud(bool force)
        {
            var info = Hud_.Compute(Ctx, Str, ForecastSource?.Invoke());
            Hud.Apply(info, Str, !PhotoMode && (Ctx.UnreadCount() > 0 || Ctx.Discovered() > Ctx.S.DexSeen));
        }

        void UpdateToast() { if (ToastState.Tick(clockNow)) Toast.Apply(ToastState, clockNow); else if (ToastState.Visible) Toast.Apply(ToastState, clockNow); else if (Toast.gameObject.activeSelf) Toast.gameObject.SetActive(false); }

        void UpdateTitle()
        {
            if (!TitleOpen || titleFadeAt < 0) return;
            float t = (Time.realtimeSinceStartup - titleFadeAt) / 0.7f; Title.Group.alpha = 1f - Mathf.Floor(Mathf.Clamp01(t) * 7f) / 7f;      // steps(7) 와 같은 7단 페이드
            if (t >= 1f) { Title.gameObject.SetActive(false); Ctx.TitleOpen = false; TitleClosed?.Invoke(); }
        }

        void UpdateAd()
        {
            if (Ad == null) return;
            bool show = AdAdapter.Showing; if (show != Ad.gameObject.activeSelf) { Ad.gameObject.SetActive(show); if (show) { adShownAt = DateTime.UtcNow.Ticks; adShownSent = false; Ad.transform.SetAsLastSibling(); } }
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

        // ================= 사진 모드 =================
        public void SetPhoto(bool on)
        {
            PhotoMode = on; if (Photo != null) { Photo.gameObject.SetActive(on); if (on) Photo.Msg.text = UiText.Display(Str.Get("photo.hidden")); }
            Hud.gameObject.SetActive(!on); if (on) { Tip.Hide(); ToastState.Visible = false; Toast.gameObject.SetActive(false); Panel.gameObject.SetActive(false); }
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
