#if LIGHTHOUSE_DEV
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Lighthouse.App;
using Lighthouse.Core;
using Lighthouse.UI.Model;
using UnityEngine;

namespace Lighthouse.Dev
{
    /// <summary>
    /// 개발용 인게임 패널(원본의 ?dev 개발 막대 + 에디터 모드, HTML 560~565행·5771~5964행). IMGUI 로 그려서 프리팹·씬을 건드리지 않는다.
    /// F10(또는 ` 키)·오른쪽 위의 DEV 단추로 열고 닫는다. LIGHTHOUSE_DEV 가 없으면 이 파일은 컴파일에서 빠진다(Lighthouse.Dev 어셈블리 전체가 빠진다).
    /// 기능: 배속·시간 건너뛰기, 단계 점프, 낮밤·계절·날씨 고정, 선물·레벨·안내 수·시작일, 자리 비움 정산, 저장 초기화/즉시 저장/폴더, 시뮬레이터, 자체 점검.
    /// </summary>
    public sealed class DevPanel : MonoBehaviour
    {
        bool open; Vector2 scroll; string log = ""; float scale = 1f;
        string giftN = "10", lvN = "10", guidedN = "100", startDays = "30", awaySec = "14400";
        string simShips = "468", simDays = "120", simSeed = "1", simVisit = "1";
        GameApp App => GameApp.Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<DevPanel>() != null) return;
            var go = new GameObject("LighthouseDev"); DontDestroyOnLoad(go); go.AddComponent<DevPanel>();
        }

        void Update() { if (Input.GetKeyDown(KeyCode.F10) || Input.GetKeyDown(KeyCode.BackQuote)) open = !open; }

        void Log(string s) { log = s + "\n" + log; if (log.Length > 6000) log = log.Substring(0, 6000); }
        static int I(string s, int d) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : d;
        static double D(string s, double d) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : d;

        void OnGUI()
        {
            scale = Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi / 110f : 1.5f, 1f, 3f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float sw = Screen.width / scale, sh = Screen.height / scale;
            if (GUI.Button(new Rect(sw - 52, 4, 48, 24), open ? "닫기" : "DEV")) open = !open;
            if (!open) return;
            var app = App; float w = Mathf.Min(360, sw - 8);
            GUILayout.BeginArea(new Rect(sw - w - 4, 32, w, sh - 40), GUI.skin.box);
            if (app == null || app.Ctx == null) { GUILayout.Label("게임이 아직 시작되지 않았어요."); GUILayout.EndArea(); return; }
            scroll = GUILayout.BeginScrollView(scroll);
            var ctx = app.Ctx; var loop = app.Loop;

            Head("시간");
            GUILayout.BeginHorizontal();
            foreach (var m in new[] { 1f, 10f, 60f, 600f, 3600f }) if (GUILayout.Button(m == GameApp.DevTimeMul ? "[" + m + "x]" : m + "x")) { GameApp.DevTimeMul = m; Log("배속 " + m + "x"); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var h in new[] { 1.0, 8.0, 24.0 }) if (GUILayout.Button("+" + h + "시간")) Skip(h * 3600);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("낮")) { DevCommands.SetNight(loop, false); Log("낮 고정"); }
            if (GUILayout.Button("밤")) { DevCommands.SetNight(loop, true); Log("밤 고정"); }
            if (GUILayout.Button("시계")) { DevCommands.SetNight(loop, null); Log("밤낮 시계 따름"); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var s in new[] { "spring", "summer", "autumn", "winter" }) if (GUILayout.Button(ctx.SeasonForce == s ? "[" + s + "]" : s)) { DevCommands.SetSeason(ctx, s); app.DevRebuild(); }
            if (GUILayout.Button("자동")) { DevCommands.SetSeason(ctx, null); app.DevRebuild(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("빠른 날씨·구조 (?fast)")) { DevCommands.SetFast(loop, loop.WxSlow <= 1); Log("날씨·구조 주기 1/" + loop.WxSlow); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("안개")) DevCommands.StartWeather(loop, "fog");
            if (GUILayout.Button("비")) DevCommands.StartWeather(loop, "rain");
            GUILayout.EndHorizontal();

            Head("등대 단계 (보기만 / 저장까지)");
            GUILayout.BeginHorizontal();
            for (int s = 1; s <= ctx.T.MaxStage; s++) if (GUILayout.Button(ctx.Stage == s ? "[" + s + "]" : s.ToString())) { DevCommands.JumpStage(ctx, s, false); app.DevRebuild(); Log("보기 단계 " + s); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            for (int s = 1; s <= ctx.T.MaxStage; s++) if (GUILayout.Button("저장" + s)) { DevCommands.JumpStage(ctx, s, true); app.DevRebuild(); Log("저장 단계 " + s); }
            GUILayout.EndHorizontal();

            Head("상태 (저장을 바꿔요)");
            Row("선물 +N개", ref giftN, () => { DevCommands.GiveGifts(ctx, I(giftN, 10)); Log("선물 지급"); });
            Row("레벨 N", ref lvN, () => { DevCommands.SetLevel(ctx, I(lvN, 10)); Log("레벨 설정"); app.DevRebuild(); });
            Row("안내한 배 N", ref guidedN, () => { DevCommands.SetGuided(ctx, D(guidedN, 0)); Log("안내 수 설정"); });
            Row("시작한 지 N일", ref startDays, () => { DevCommands.SetStartDays(ctx, D(startDays, 0)); Log("시작일 설정"); });
            Row("자리 비움 N초", ref awaySec, () => { ShowNews(DevCommands.Away(ctx, loop, D(awaySec, 14400))); Log("자리 비움 정산"); });

            Head("저장");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("지금 저장")) { app.FlushNow(); Log("저장함 (" + app.LoadedFrom + ")"); }
            if (GUILayout.Button("폴더 열기")) Application.OpenURL("file:///" + app.SaveDir.Replace('\\', '/'));
            GUILayout.EndHorizontal();
            GUILayout.Label("저장 폴더: " + app.SaveDir);
            if (GUILayout.Button("저장 초기화 (되돌릴 수 없어요)")) { app.Store.Save(DevCommands.FreshState(app.DevMig, app.Clock.NowMs)); app.DevReboot(); }

            Head("시뮬레이터 · 자체 점검");
            Row("배/일", ref simShips, null); Row("일 수", ref simDays, null); Row("seed", ref simSeed, null); Row("방문 간격(일)", ref simVisit, null);
            if (GUILayout.Button("시뮬레이터 실행")) Log(DevChecks.SimulateText(app.T, new SystemClock(), D(simShips, 468), I(simDays, 120), I(simSeed, 1), I(simVisit, 1)));
            if (GUILayout.Button("자체 점검 실행 (몇 초 걸려요)"))
            {
                var res = DevChecks.RunAll(app.T, new SystemClock(), app.DevStr);
                var sb = new StringBuilder(); foreach (var r in res) sb.AppendLine(r.ToString());
                sb.AppendLine(res.Count(r => !r.Ok) == 0 ? "모두 통과" : res.Count(r => !r.Ok) + "건 실패");
                Log(sb.ToString());
            }

            Head("기록");
            GUILayout.TextArea(log, GUILayout.MinHeight(120));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Skip(double sec)
        {
            var app = App; if (GameApp.DevClockRef == null) { Log("개발 시계가 없어요(수동 시계 모드)"); return; }
            ShowNews(DevCommands.SkipTime(GameApp.DevClockRef, app.Ctx, app.Loop, sec)); Log("+" + sec / 3600 + "시간 건너뜀");
        }

        void ShowNews(OfflineResult news)
        {
            var app = App;
            if (news != null && !news.Quiet && app.Ui != null) { app.Ui.Ctl.OpenNews(new OfferNews(news)); app.Ui.Panel.Refresh(false); }
        }

        static void Head(string t) { GUILayout.Space(6); GUILayout.Label("<b>" + t + "</b>", new GUIStyle(GUI.skin.label) { richText = true }); }

        static void Row(string label, ref string value, Action go)
        {
            GUILayout.BeginHorizontal(); GUILayout.Label(label, GUILayout.Width(110)); value = GUILayout.TextField(value, GUILayout.Width(90));
            if (go != null && GUILayout.Button("실행")) go();
            GUILayout.EndHorizontal();
        }
    }
}
#endif
