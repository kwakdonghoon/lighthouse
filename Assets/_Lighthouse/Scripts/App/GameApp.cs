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
        bool rebuildPending = true; int lastW, lastH, lastDecorRev = -1; long hiddenAtMs; float hintUntil;
        bool pressing, moved; Vector2 pressStart; float pressT; bool booted;
        string lastSkinKey; bool restarting;

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
            SaveDir = !string.IsNullOrEmpty(SaveDirOverride) ? SaveDirOverride : Application.persistentDataPath;
            mig = new SaveMigrator(T, Clock); store = new SaveStore(new FileStorage(SaveDir), mig, Clock);
            var state = store.Load(); LoadedFrom = store.LoadedFrom;
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

            UiRoot.BootStrings = str;
            Ui = UiRoot.Create(null, false); Ui.Canvas.sortingOrder = 10;
            if (ForceW > 0) { Ui.ManualScreen = true; Ui.DevW = ForceW; Ui.DevH = ForceH; Ui.Density = ForceDensity; Ui.Safe = new Insets(0, 24, 0, 16); }
            Ui.Setup(Ctx, str, null, null, new SaveCode(T, mig), null);
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
            pendingNews = LastNews != null && !LastNews.Quiet ? LastNews : null;
            Ui.ShowTitle(first);
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
            if (!Ctx.S.Seen) { Ctx.S.Seen = true; Ctx.NotifyChanged(); Ui.ShowHint(true); hintUntil = Time.unscaledTime + 10f; }
        }

        void OnTitleClosed()
        {
            if (pendingNews != null) { Ui.Ctl.OpenNews(new OfferNews(pendingNews)); Ui.Panel.Refresh(false); pendingNews = null; }
        }

        void OnPhoto()
        {
            try
            {
                int mult = Mathf.Max(2, Mathf.CeilToInt(1080f / World.W));
                var png = World.CapturePng(mult);
                string dir = Path.Combine(SaveDir, "photos"); Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "lighthouse_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                File.WriteAllBytes(path, png); LastPhotoPath = path;
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("사진 저장 실패: " + e.Message); if (Ui.Photo != null) Ui.Photo.Msg.text = UiText.Display(str.Get("photo.failed")); }
        }
        public string LastPhotoPath;

        void OnSaveImported(JObject data)
        {
            try
            {
                var st = GameState.FromJson(mig.Migrate(data)); st.LastSeen = Clock.NowMs;           // 가져온 순간부터 다시 센다 (비운 시간 보상은 없다)
                store.Save(st); restarting = true; Invoke(nameof(Restart), 0f);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("가져오기 실패: " + e.Message); }
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
            var c = new List<(double d, string id)>();
            foreach (var id in Ui.Ctl.DecoOpenSlotIds())
            {
                var sl = DecorBaker.Slots.FirstOrDefault(s => s.Id == id); if (sl == null) continue;
                var p = DecorLayout.Pos(sl, Ctx.Stage, World.W); double dd = Math.Sqrt((d.x - p.x) * (d.x - p.x) + (d.y - (p.y - 3)) * (d.y - (p.y - 3))) * css;
                if (dd <= 22) c.Add((dd, id));
            }
            if (Ui.Ctl.DecoTapPick(c)) Ui.Panel.Refresh(true);
        }

        // ================= 앱 생명주기 =================
        void OnApplicationPause(bool pause)
        {
            if (!booted) return;
            if (pause) { hiddenAtMs = Clock.NowMs; Save.Flush(); return; }
            if (hiddenAtMs == 0) return;
            double away = (Clock.NowMs - hiddenAtMs) / 1000.0; hiddenAtMs = 0;           // 앱을 켜 둔 채 다른 앱에 다녀온 경우에도 보상
            var news = Loop.ReturnFromAway(away); Ctx.S.LastSeen = Clock.NowMs; Save.Flush();
            if (news != null && !news.Quiet) { LastNews = news; Ui.Ctl.OpenNews(new OfferNews(news)); Ui.Panel.Refresh(false); }
        }

        void OnApplicationQuit() { if (booted && Save != null) Save.Flush(); }

        /// <summary>시험: 지금 즉시 저장(앱이 가려질 때와 같다).</summary>
        public void FlushNow() { Save.Flush(); }

        // ================= 매 프레임 =================
        void Update()
        {
            if (!booted) return;
            var sw = Stopwatch.StartNew();
            float rdt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            if (ManualClockMode)
            {
                double sim = rdt * TimeMul; int n = Mathf.Max(1, Mathf.CeilToInt((float)(sim / 0.1))); double step = sim / n;
                var mc = (ManualClock)Clock;
                for (int i = 0; i < n; i++) { mc.AdvanceSec(step); Loop.Tick(step); Save.Tick(step); HandleInput(); }
            }
            else { Loop.Tick(rdt); Save.Tick(rdt); HandleInput(); }
            if (hintUntil > 0 && Time.unscaledTime > hintUntil) { Ui.ShowHint(false); hintUntil = 0; }

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
                if (item != null && T.DecorSlotById.TryGetValue(id, out var sl) && DecorRules.SlotOpen(sl, Ctx.Stage, gl)) put.Add((id, item));
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
                Loop.Markers.Add(new DecoMarker { X = p.x, Y = p.y, Rx = rx, Sel = ctl.DecoSel == id, Full = DecorRules.PutOf(T, Ctx.S, id) != null });
            }
        }

        void UpdateBubble()
        {
            var b = Loop.Bubble; if (b == null) { if (bubble.gameObject.activeSelf) bubble.Hide(); return; }
            double bx, by;
            if (b.Ship != null) { double ls = Lanes.S[b.Ship.Lane]; bx = b.Ship.X; by = Lanes.Y[b.Ship.Lane] - 40 * ls; }
            else { var kp = Loop.KeeperPos(); bx = kp.x; by = kp.y - 19; }
            if (!bubble.gameObject.activeSelf) bubble.gameObject.SetActive(true);
            bubble.SetText(b.Text);
            var cpos = World.ToCanvas(bx, by, Ui.Scale);
            bubble.Place(cpos.x, cpos.y, UiRoot.CanvasW, UiRoot.CanvasH, ScreenLayout.HudTop(Ui.Safe) + 36f);
        }
    }
}
