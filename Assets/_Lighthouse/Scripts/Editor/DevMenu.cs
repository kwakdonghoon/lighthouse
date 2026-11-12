#if LIGHTHOUSE_DEV
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.App;
using Lighthouse.Core;
using Lighthouse.Data;
using Lighthouse.Dev;
using Lighthouse.UI.Model;
using UnityEditor;
using UnityEngine;

namespace Lighthouse.EditorTools
{
    /// <summary>
    /// 에디터 개발 메뉴(원본 에디터 모드·개발 막대의 에디터판). LIGHTHOUSE_DEV 가 있을 때만 컴파일된다(출시 구성에서는 이 파일 통째로 없다).
    /// Play 중: 배속·시간 건너뛰기·단계 점프·낮밤·날씨. Play 밖: 자체 점검·시뮬레이터(결과를 Tools/c_results 에 쓴다).
    /// </summary>
    public static class DevMenu
    {
        const string Root = "Lighthouse/Dev/";
        static GameApp App => Application.isPlaying ? GameApp.Instance : null;
        static bool Ready() => App != null && App.Ctx != null;

        // ----- 배속 (Play) -----
        static void Mul(float m) { GameApp.DevTimeMul = m; Debug.Log("개발 배속 " + m + "x"); }
        [MenuItem(Root + "Play/Time x1")] static void M1() { Mul(1); }
        [MenuItem(Root + "Play/Time x10")] static void M10() { Mul(10); }
        [MenuItem(Root + "Play/Time x60")] static void M60() { Mul(60); }
        [MenuItem(Root + "Play/Time x600")] static void M600() { Mul(600); }
        [MenuItem(Root + "Play/Time x3600")] static void M3600() { Mul(3600); }

        // ----- 시간 건너뛰기 (Play) -----
        static void Skip(double sec)
        {
            if (!Ready() || GameApp.DevClockRef == null) { Debug.LogWarning("Play 중이고 실시간 시계 모드일 때만 된다"); return; }
            var news = DevCommands.SkipTime(GameApp.DevClockRef, App.Ctx, App.Loop, sec);
            if (news != null && !news.Quiet && App.Ui != null) { App.Ui.Ctl.OpenNews(new OfferNews(news)); App.Ui.Panel.Refresh(false); }
            Debug.Log("시간 +" + sec / 3600 + "시간" + (news == null ? " (보상 없음)" : " (보상 " + news.Count + "척)"));
        }
        [MenuItem(Root + "Play/Skip +1 hour")] static void S1() { Skip(3600); }
        [MenuItem(Root + "Play/Skip +8 hours")] static void S8() { Skip(8 * 3600); }
        [MenuItem(Root + "Play/Skip +1 day")] static void S24() { Skip(24 * 3600); }

        // ----- 단계 점프 (Play) -----
        static void Stage(int s, bool save) { if (!Ready()) return; DevCommands.JumpStage(App.Ctx, s, save); App.DevRebuild(); Debug.Log((save ? "저장 " : "보기 ") + "단계 " + s); }
        [MenuItem(Root + "Play/Stage view 1")] static void V1() { Stage(1, false); }
        [MenuItem(Root + "Play/Stage view 2")] static void V2() { Stage(2, false); }
        [MenuItem(Root + "Play/Stage view 3")] static void V3() { Stage(3, false); }
        [MenuItem(Root + "Play/Stage view 4")] static void V4() { Stage(4, false); }
        [MenuItem(Root + "Play/Stage view 5")] static void V5() { Stage(5, false); }
        [MenuItem(Root + "Play/Stage view 6")] static void V6() { Stage(6, false); }
        [MenuItem(Root + "Play/Stage save 6 (changes the save)")] static void P6() { Stage(6, true); }

        // ----- 낮밤·날씨·선물 (Play) -----
        [MenuItem(Root + "Play/Day")] static void Day() { if (Ready()) DevCommands.SetNight(App.Loop, false); }
        [MenuItem(Root + "Play/Night")] static void Night() { if (Ready()) DevCommands.SetNight(App.Loop, true); }
        [MenuItem(Root + "Play/Clock day-night")] static void Clk() { if (Ready()) DevCommands.SetNight(App.Loop, null); }
        [MenuItem(Root + "Play/Weather rain")] static void Rain() { if (Ready()) DevCommands.StartWeather(App.Loop, "rain"); }
        [MenuItem(Root + "Play/Weather fog")] static void Fog() { if (Ready()) DevCommands.StartWeather(App.Loop, "fog"); }
        [MenuItem(Root + "Play/Gifts +10 each (changes the save)")] static void Gifts() { if (Ready()) DevCommands.GiveGifts(App.Ctx, 10); }

        // ----- 자체 점검·시뮬레이터 (Play 밖에서도) -----
        static string OutDir() { string d = Path.Combine(Application.dataPath, "..", "Tools", "c_results"); Directory.CreateDirectory(d); return d; }

        [MenuItem(Root + "Self-check (14 lines)")]
        public static void SelfCheck()
        {
            var src = new ResourcesDataSource(); var T = DataLoader.Load(src); var str = UiStrings.Parse(src.ReadText("ko/ui.json"));
            var res = DevChecks.RunAll(T, new SystemClock(), str);
            var sb = new StringBuilder(); foreach (var r in res) sb.AppendLine(r.ToString());
            int bad = res.Count(r => !r.Ok); sb.AppendLine(bad == 0 ? "모두 통과" : bad + "건 실패");
            File.WriteAllText(Path.Combine(OutDir(), "selfcheck.txt"), sb.ToString(), new UTF8Encoding(false));
            if (bad == 0) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
        }

        [MenuItem(Root + "Simulate 468/120d seed 1 (Markdown)")]
        public static void Simulate()
        {
            var T = DataLoader.Load(new ResourcesDataSource());
            string text = DevChecks.SimulateText(T, new SystemClock(), 468, 120, 1, 1);
            File.WriteAllText(Path.Combine(OutDir(), "simulate.md"), "```\n" + text + "\n```\n", new UTF8Encoding(false));
            Debug.Log(text);
        }

        /// <summary>배치: -executeMethod Lighthouse.EditorTools.DevMenu.BatchSelfCheck (끝나고 종료 코드 0/1)</summary>
        public static void BatchSelfCheck()
        {
            var src = new ResourcesDataSource(); var T = DataLoader.Load(src); var str = UiStrings.Parse(src.ReadText("ko/ui.json"));
            var res = DevChecks.RunAll(T, new SystemClock(), str);
            var sb = new StringBuilder(); foreach (var r in res) sb.AppendLine(r.ToString());
            File.WriteAllText(Path.Combine(OutDir(), "selfcheck.txt"), sb.ToString(), new UTF8Encoding(false));
            EditorApplication.Exit(res.Any(r => !r.Ok) ? 1 : 0);
        }
    }
}
#endif
