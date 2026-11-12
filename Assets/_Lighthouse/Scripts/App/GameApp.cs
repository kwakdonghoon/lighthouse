using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Data;
using Lighthouse.Render;
using Lighthouse.UI;
using Lighthouse.UI.Model;
using Lighthouse.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Lighthouse.App
{
    /// <summary>
    /// 메인 씬의 뿌리(M4): 데이터·저장·시계를 만들고 GameContext(규칙)·GameLoop(시간 진행)·월드 그림(GameWorldView)·UI(UiRoot)를 한 루프로 잇는다.
    /// 시작: 저장 읽기(본 → 백업 → 새로) → 접속하지 않은 동안의 보상(RunOffline) → 타이틀. 매 프레임: 입력 → Loop.Tick → 자동 저장 → 월드 그리기.
    /// 앱이 가려지면(OnApplicationPause) 저장하고 돌아오면 비운 시간만큼 보상, 끝나면 저장. 저장 위치는 persistentDataPath(시험은 -lhSaveDir 로 바꾼다).
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        // ----- 시험·진단용 훅(정적: 씬 로드 전에 시험이 채운다) -----
        public static string SaveDirOverride;           // 저장 폴더를 바꾼다(없으면 persistentDataPath)
        public static bool ManualClockMode;             // true 이면 시계를 손으로 돌린다: 프레임마다 dt*TimeMul 만큼만 흐른다(가속 시험)
        public static long ManualStartMs;               // 수동 시계의 시작 시각(0 이면 실제 지금)
        public static float TimeMul = 1;                // 수동 시계일 때 시간 배속(0.1 초 단위로 나눠 Tick)
        public static int ForceW, ForceH; public static float ForceDensity = 2.75f;      // 시험: 에디터 배치의 640x480 대신 폰 크기(기기 픽셀)로 레이아웃한다
        static int ScreenW => ForceW > 0 ? ForceW : Screen.width; static int ScreenH => ForceH > 0 ? ForceH : Screen.height;
        public static GameApp Instance { get; private set; }

        public Material SpriteMat, PremulMat;
        public GameContext Ctx { get; private set; } public GameLoop Loop { get; private set; } public GameWorldView World { get; private set; } public UiRoot Ui { get; private set; }
        public SaveService Save { get; private set; } public GameTables T { get; private set; } public IClock Clock { get; private set; } public string SaveDir { get; private set; }
        public OfflineResult LastNews { get; private set; } public string LoadedFrom { get; private set; }
        public FitView.View ViewMode { get; private set; } = FitView.View.Normal;
        public double UpdateMs, LastUpdateMs; public long Frames;
        public readonly List<double> FrameMs = new List<double>();

        UiStrings str; SaveStore store; SaveMigrator mig; BubbleView bubble; OfflineResult pendingNews;
        bool rebuildPending = true; int lastW, lastH, lastDecorRev = -1; long hiddenAtMs; string lastSeasonSeen;
        bool pressing, moved; Vector2 pressStart; float pressT; bool booted;
        string lastSkinKey; bool restarting; LoadNotice pendingNotice; Lighthouse.Platform.ScreenReaderBridge a11y;
        public ITextTransfer Transfer { get; private set; }
        public SaveStore Store => store;
#if LIGHTHOUSE_DEV
        // ----- 개발용 훅 (Lighthouse.Dev 의 DevPanel 이 쓴다. LIGHTHOUSE_DEV 가 없으면 컴파일에서 빠진다) -----
        public static DevClock DevClockRef;
        public static float DevTimeMul = 1;             // 1 이면 실제 속도. 2 이상이면 시계와 루프를 함께 빠르게 돌린다
        public bool DevSuppressSave;                    // true 이면 자동 저장을 멈춘다(시뮬레이터가 상태를 빌려 쓰는 동안)
        public UiStrings DevStr => str;
        public SaveMigrator DevMig => mig;
        public void DevRebuild() { rebuildPending = true; }
        public void DevReboot() { Restart(); }
#endif

        void Awake()
        {
            Instance = this;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-lhSaveDir") SaveDirOverride = args[i + 1];
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            if (SpriteMat == null) SpriteMat = Resources.Load<Material>("Materials/SpriteUnlit");
            if (PremulMat == null) PremulMat = Resources.Load<Material>("Materials/LhPremul");
        }

        void Start() { Boot(); }

        // ================= 시작 =================
        void Boot()
        {
            var src = new ResourcesDataSource();
            T = DataLoader.Load(src); str = UiStrings.Parse(src.ReadText("ko/ui.json"));
            Clock = ManualClockMode ? (IClock)new ManualClock(ManualStartMs > 0 ? ManualStartMs : new SystemClock().NowMs, new SystemClock().OffsetMinutes(new SystemClock().NowMs)) : new SystemClock();
#if LIGHTHOUSE_DEV
            if (!ManualClockMode) { DevClockRef = new DevClock(new SystemClock(), DevClockRef != null ? DevClockRef.ExtraMs : 0); Clock = DevClockRef; }      // 개발용: 배속·시간 건너뛰기(출시 빌드에는 이 줄이 없다)
#endif
            SaveDir = !string.IsNullOrEmpty(SaveDirOverride) ? SaveDirOverride : Application.persistentDataPath;
            mig = new SaveMigrator(T, Clock); store = new SaveStore(new FileStorage(SaveDir), mig, Clock);
            var state = store.Load(); LoadedFrom = store.LoadedFrom; pendingNotice = store.Notice;
            bool first = LoadedFrom == "new";
            Ctx = new GameContext(T, state, ManualClockMode ? (IRng)new Mulberry32(12345) : new SystemRng(), Clock);
            long now = Clock.NowMs;
            LastNews = state.LastSeen > 0 ? Ctx.RunOffline((now - state.LastSeen) / 1000.0) : null;         // 접속하지 않은 동안의 보상
            state.LastSeen = now;
            Loop = new GameLoop(Ctx, str, ManualClockMode ? (IRng)new Mulberry32(777) : new SystemRng());
            Save = new SaveService(store, Ctx, Clock); Save.SaveFailed += e => UnityEngine.Debug.LogWarning("저장 실패: " + e.Message);
            Save.SaveFailedNotice += () => { if (Ui != null) Ui.ShowToast(str.Get("world.saveFailed"), str.Get("world.saveFailedSub")); };      // 플레이어에게도 알린다(30초에 한 번)
            Save.Flush();
            ApplyExtras();

            Lighthouse.UI.TitleView.WantPieces = Ctx.S.Guided == 0 && Ctx.S.Letters.Count == 0 && !Lighthouse.Core.MotionPref.Reduce;      // 첫 실행이면 인트로용 섬 조각을 처음부터 함께 굽는다
            UiRoot.BootStrings = str;
            Ui = UiRoot.Create(null, false); Ui.Canvas.sortingOrder = 10;
            if (ForceW > 0) { Ui.ManualScreen = true; Ui.DevW = ForceW; Ui.DevH = ForceH; Ui.Density = ForceDensity; Ui.Safe = new Insets(0, 24, 0, 16); }
            Ui.Setup(Ctx, str, null, null, new SaveCode(T, mig), null);
            Transfer = new Lighthouse.Platform.PlatformTextTransfer(Path.Combine(SaveDir, "exports")); Ui.Transfer = Transfer;     // 코드 복사·파일 내보내기·파일 열기(플랫폼별)
            Lighthouse.Platform.PlatformAccess.Init();                                                                              // 움직임 줄이기 OS 설정, 스크린 리더 다리
            if (a11y == null) a11y = gameObject.AddComponent<Lighthouse.Platform.ScreenReaderBridge>();
            a11y.Bind(Ui.Canvas);
            Ui.ForecastSource = () => Loop.Forecast();
            bubble = BubbleView.Create(Ui.transform);
            Ui.WorldDirtyRequested += _ => rebuildPending = true;
            Ui.ViewChanged += v => { ViewMode = ParseView(v); rebuildPending = true; };
            Ui.SaveImported += OnSaveImported;
            Ui.PhotoSaveRequested += OnPhoto;
            Ui.Panel.DecoDimTapped += OnDecoTap;
            Ui.TitleTapped += OnTitleTapped; Ui.TitleClosed += OnTitleClosed;
            ViewMode = ParseView(Ctl_ExtraView());

            var worldGo = new GameObject("World"); World = worldGo.AddComponent<GameWorldView>(); World.SpriteMat = SpriteMat; World.PremulMat = PremulMat;
            Ui.MailAnchor = () => { var mp = Loop.MailboxPos(); return new Vector2?(World.ToCanvas(mp.x, mp.y - 10, Ui.Scale)); };
            Loop.Rebuild += _ => rebuildPending = true;
            Loop.Toast += (a, b) => Ui.ShowToast(a, b);
            Loop.UiBusy = () => Ui.Ctl.Mode != PanelMode.None || Ui.TitleOpen || Ui.PhotoMode || Ui.ToastState.Visible || Ui.Tips.Current != null;      // 처음 만난 것 알림이 기다리는 때
            Loop.OpenMail += () => { Ui.Ctl.Open(PanelMode.Mail); Ui.Panel.Refresh(false); };
            Loop.Boot();
            AudioBridge.Attach(this);                                                     // M6: 소리 요청·장면을 소리 엔진에 잇는다(배치 모드에서는 만들지 않음)
            pendingNews = LastNews != null && !LastNews.Quiet ? LastNews : null;
            bool firstRun = Ctx.S.Guided == 0 && Ctx.S.Letters.Count == 0;                    // 원본 5595행: 안내한 배도 편지도 없으면 첫 실행(약 9초 인트로)
            Ui.ShowTitle(firstRun);
            if (!Ctx.S.Seen) { Ctx.S.Seen = true; Ctx.NotifyChanged(); Ui.PlayHint(); }          // 원본 5575행: 첫 실행에만 안내 한 줄(1.2초 뒤 나타나 10초 동안)
            booted = true;
        }

        string Ctl_ExtraView() => Ui.Ctl.ExtraStr("view", "normal");
        static FitView.View ParseView(string v) => v == "near" ? FitView.View.Near : v == "wide" ? FitView.View.Wide : FitView.View.Normal;

        void ApplyExtras()
        {
            bool wake = Ctx.S.Extra["wake"] != null && Ctx.S.Extra["wake"].Type == JTokenType.Boolean && (bool)Ctx.S.Extra["wake"];
            Screen.sleepTimeout = wake ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
        }

        // ================= 이벤트 =================
        void OnTitleTapped()
        {
            // 첫 실행 안내(hint)는 시작할 때부터 흐른다(원본: 타이틀 아래에서 이미 지나가기 시작). 여기서 할 일은 없다.
        }

        void OnTitleClosed()
        {
            if (pendingNotice != LoadNotice.None)
            {
                // 저장 파일이 깨져 있었다는 안내(원본에는 없는 문구: 이식 중 새로 쓴 것)
                if (pendingNotice == LoadNotice.RestoredBackup) Ui.ShowToast(str.Get("world.saveRestored"), str.Get("world.saveRestoredSub"));
                else Ui.ShowToast(str.Get("world.saveFresh"), str.Get("world.saveFreshSub"));
                pendingNotice = LoadNotice.None;
            }
            if (pendingNews != null) { Ui.Ctl.OpenNews(new OfferNews(pendingNews)); Ui.Panel.Refresh(false); pendingNews = null; }
        }

        void OnPhoto()
        {
            PhotoOutcome res;
            try
            {
                var png = World.CapturePng(PhotoSink.Factor(World.W));                                 // 원본 3168행: 화면 도트 그림을 정수 배율(1080/폭, 1~8)로 키워 번지지 않게
                res = PhotoSink.Save(png, PhotoSink.FileName(DateTime.Now), SaveDir); LastPhotoPath = PhotoSink.LastPath;
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("사진 저장 실패: " + e.Message); res = PhotoOutcome.Failed; }
            LastPhotoOutcome = res;
            Ui.SetPhotoMessage(res == PhotoOutcome.Saved ? "photo.saved" : res == PhotoOutcome.Shared ? "photo.shared" : res == PhotoOutcome.Cancelled ? "photo.notSaved" : "photo.failed");
        }
        public PhotoOutcome LastPhotoOutcome;
        public string LastPhotoPath;

        void OnSaveImported(JObject data)
        {
            if (restarting) return;
            try
            {
                // 원본 importAct: 이 기기의 구매(owned)·광고 횟수(ads)는 그대로, lastSeen=지금 (SaveImport.Apply)
                var st = SaveImport.Apply(Ctx.S, data, mig, Clock.NowMs);
                restarting = true;                                  // 이 순간부터 자동 저장·일시정지 저장이 옛 상태로 덮어쓰지 못한다 (Update/Pause/Quit 이 이 값을 본다)
                store.KeepBeforeImport(); store.Save(st);
                Invoke(nameof(Restart), 0f);
            }
            catch (Exception e)
            {
                restarting = false; UnityEngine.Debug.LogWarning("가져오기 실패: " + e.Message);
                if (Ui != null) Ui.ShowToast(str.Get("world.saveFailed"), str.Get("world.saveFailedSub"));
            }
        }

        /// <summary>저장 파일을 다시 읽어 처음부터 다시 시작(가져오기 뒤).</summary>
        public void Restart()
        {
            if (Ui != null) Destroy(Ui.gameObject); if (World != null) { World.View.Clear(); Destroy(World.gameObject); }
            if (bubble != null) Destroy(bubble.gameObject);
            booted = false; rebuildPending = true; lastDecorRev = -1; restarting = false; Boot();
        }

        void OnDecoTap(Vector2 px)
        {
            if (World == null || !Ui.Ctl.DecoPlaceOpen) return;
            var d = World.ToDesign(px); double css = World.Fit.Scale / Ui.Scale;          // 설계 1칸 = 캔버스 단위(dp) 몇 개
            var slots = new List<(string id, double sx, double sy)>();
            foreach (var id in Ui.Ctl.DecoOpenSlotIds())
            {
                var sl = DecorBaker.Slots.FirstOrDefault(s => s.Id == id); if (sl == null) continue;
                var p = DecorLayout.Pos(sl, Ctx.Stage, World.W); slots.Add((id, p.x, p.y));
            }
            var c = DecoTapAim.Candidates(d.x, d.y, slots, css);                                    // 22 안쪽의 자리만 (원본 decoTapAt)
            if (Ui.Ctl.DecoTapPick(c)) Ui.Panel.Refresh(true);
        }

        // ================= 앱 생명주기 =================
        void OnApplicationPause(bool pause)
        {
            if (!booted || restarting) return;           // 가져오기 직후 재시작을 기다리는 동안 옛 상태로 저장하지 않는다
            if (pause) { hiddenAtMs = Clock.NowMs; Save.Flush(); return; }
            if (hiddenAtMs == 0) return;
            double away = (Clock.NowMs - hiddenAtMs) / 1000.0; hiddenAtMs = 0;           // 앱을 켜 둔 채 다른 앱에 다녀온 경우에도 보상
            var news = Loop.ReturnFromAway(away); Ctx.S.LastSeen = Clock.NowMs; Save.Flush();
            if (news != null && !news.Quiet) { LastNews = news; Ui.Ctl.OpenNews(new OfferNews(news)); Ui.Panel.Refresh(false); }
        }

        void OnApplicationQuit() { if (booted && !restarting && Save != null) Save.Flush(); }

        /// <summary>시험: 지금 즉시 저장(앱이 가려질 때와 같다).</summary>
        public void FlushNow() { Save.Flush(); }

        // ================= 매 프레임 =================
        void Update()
        {
            if (!booted || restarting) return;
            var sw = Stopwatch.StartNew();
            float rdt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
#if LIGHTHOUSE_DEV
            if (!ManualClockMode && DevTimeMul > 1.001f && DevClockRef != null)
            {
                // 개발용 배속: 시계를 (배속-1)×dt 만큼 더 보내고 루프를 배속×dt 로 0.1초씩 나눠 돌린다 (ManualClockMode 와 같은 방식)
                double sim = rdt * DevTimeMul; int n = Mathf.Max(1, Mathf.CeilToInt((float)(sim / 0.1))); double step = sim / n;
                DevClockRef.AddExtraSec(rdt * (DevTimeMul - 1));
                for (int i = 0; i < n; i++) { Loop.Tick(step); if (!DevSuppressSave) Save.Tick(step); HandleInput(); }
            }
            else
#endif
            if (ManualClockMode)
            {
                double sim = rdt * TimeMul; int n = Mathf.Max(1, Mathf.CeilToInt((float)(sim / 0.1))); double step = sim / n;
                var mc = (ManualClock)Clock;
                for (int i = 0; i < n; i++) { mc.AdvanceSec(step); Loop.Tick(step); Save.Tick(step); HandleInput(); }
            }
            else { Loop.Tick(rdt); Save.Tick(rdt); HandleInput(); }

            { string sn = Ctx.CurSeason(); if (sn != lastSeasonSeen) { lastSeasonSeen = sn; if (DecorRules.SeasonTick(T, Ctx.S, sn)) { Ctx.NotifyChanged(); rebuildPending = true; } } }      // G2': 계절 장식(호박 더미·꽃잎 길·눈사람)은 그 계절을 만난 순간 영구 해금(원본 decorSeasonTick)
            // 월드 다시 굽기: 화면 크기·단계·계절·외형·장식이 바뀌었을 때
            if (ScreenW != lastW || ScreenH != lastH) rebuildPending = true;
            if (Ui.Ctl.DecorRev != lastDecorRev) { if (lastDecorRev >= 0) rebuildPending = true; lastDecorRev = Ui.Ctl.DecorRev; }
            if (rebuildPending) { RebuildWorld(); rebuildPending = false; }
            UpdateMarkers(); UpdateBubble();
            World.Frame();
            LastUpdateMs = sw.Elapsed.TotalMilliseconds; UpdateMs += LastUpdateMs; Frames++;
            if (FrameMs.Count < 20000) FrameMs.Add(Time.unscaledDeltaTime * 1000.0);
        }

        void RebuildWorld()
        {
            lastW = ScreenW; lastH = ScreenH;
            var put = new List<(string slot, string item)>(); int gl = Ctx.LvlOf("garden");
            foreach (var id in Ctx.S.Isle.Put.Keys)
            {
                var item = DecorRules.PutOf(T, Ctx.S, id);
                if (item != null && T.DecorSlotById.TryGetValue(id, out var sl) && DecorRules.SlotOpen(sl, Ctx.Stage, gl) && DecorRules.Owned(T, Ctx.S, item, gl)) put.Add((id, item));      // G2': 구매·해금이 없는 장식은 그리지 않는다(팩 환불·복원 전)
            }
            World.Rebuild(Loop, Ctx, lastW, lastH, ViewMode, layer => Ui.Ctl.SkinId(layer), put);
        }

        // ================= 입력 (원본 pointerdown/move/up 5160행) =================
        void HandleInput()
        {
            if (World == null || World.Sprites == null) return;
            bool over = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Input.GetMouseButtonDown(0) && !over && !Ui.PhotoMode)
            {
                pressing = true; moved = false; pressStart = Input.mousePosition; pressT = Time.unscaledTime;
                var d = World.ToDesign(pressStart); Loop.PointerDown(d.x, d.y);
            }
            if (pressing && Input.GetMouseButton(0))
            {
                var p = (Vector2)Input.mousePosition; if (!moved && (p - pressStart).magnitude > 8f * Ui.Scale) moved = true;
                var d = World.ToDesign(p); Loop.PointerMove(d.x, d.y, moved);
            }
            if (pressing && Input.GetMouseButtonUp(0))
            {
                pressing = false; Loop.PointerUp(!moved && Time.unscaledTime - pressT < 0.35f);
            }
        }

        // ================= 꾸미기 자리 표시·말풍선 =================
        void UpdateMarkers()
        {
            Loop.Markers.Clear();
            var ctl = Ui.Ctl; if (!ctl.DecoPlaceOpen) return;
            foreach (var id in ctl.DecoOpenSlotIds())
            {
                var sl = DecorBaker.Slots.FirstOrDefault(s => s.Id == id); if (sl == null) continue;
                var p = DecorLayout.Pos(sl, Ctx.Stage, World.W); double rx = sl.Size == "S" ? 6 : sl.Size == "M" ? 9 : 12;
                Loop.Markers.Add(new DecoMarker { X = p.x, Y = p.y, Rx = rx, Sel = ctl.DecoSel == id, Full = DecorRules.PutOf(T, Ctx.S, id) != null, Num = WorldPainter.SlotNum(id) });
            }
        }

        readonly List<UiRectF> avoid = new List<UiRectF>();
        void UpdateBubble()
        {
            var b = Loop.Bubble; if (b == null || Ui.PhotoMode) {          // 사진 모드에서는 말풍선도 숨긴다(원본 body.photo #bubble 471행)
                if (bubble.gameObject.activeSelf) bubble.Hide(); return; }
            double bx, by;
            if (b.Ship != null) { double ls = Lanes.S[b.Ship.Lane]; bx = b.Ship.X; by = Lanes.Y[b.Ship.Lane] - 40 * ls; }
            else { var kp = Loop.KeeperPos(); bx = kp.x; by = kp.y - 19; }
            if (!bubble.gameObject.activeSelf) bubble.gameObject.SetActive(true);
            bubble.SetText(b.Text);
            var cpos = World.ToCanvas(bx, by, Ui.Scale);
            Ui.CollectAvoidRects(avoid); bubble.Place(cpos.x, cpos.y, UiRoot.CanvasW, UiRoot.CanvasH, avoid);
        }
    }
}
