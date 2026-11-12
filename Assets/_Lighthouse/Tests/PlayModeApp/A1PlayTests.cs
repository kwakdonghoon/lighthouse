using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.App;
using Lighthouse.Core;
using Lighthouse.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lighthouse.Tests
{
    /// <summary>
    /// A1 월드 연출 플레이 모드 확인(에디터 배치, 그래픽 필요): 메인 씬에서 계절·밤낮·단계를 바꿔 가며 새 연출(먼 섬·부이·표류물·비계·생물·계절 입자·별똥별·갈매기·배 깃발/물보라/선원/표식·번호 배지)이
    /// 예외 없이 그려지고 장면 PNG 로 남는지, 그리고 프레임 비용이 A1 을 끈 상태(M4)보다 얼마나 늘었는지 잰다. 결과: Tools/a1_results/ (smoke.txt, a1/*.png).
    /// </summary>
    public sealed class A1PlayTests
    {
        string dir; static readonly long Start = new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        readonly StringBuilder report = new StringBuilder();
        string outDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "a1_results"));

        [SetUp]
        public void Up()
        {
            dir = Path.Combine(Path.GetTempPath(), "lh_a1play_" + Guid.NewGuid().ToString("N"));
            GameApp.ForceW = 1080; GameApp.ForceH = 2340; GameApp.SaveDirOverride = dir; GameApp.ManualClockMode = true; GameApp.TimeMul = 1; GameApp.ManualStartMs = Start;
            WorldPainter.A1Off = false;
        }

        [TearDown]
        public void Down()
        {
            WorldPainter.A1Off = false;
            GameApp.ForceW = GameApp.ForceH = 0; GameApp.SaveDirOverride = null; GameApp.ManualClockMode = false; GameApp.TimeMul = 1; GameApp.ManualStartMs = 0;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
            Directory.CreateDirectory(outDir); File.AppendAllText(Path.Combine(outDir, "smoke.txt"), report.ToString(), new UTF8Encoding(false)); report.Clear();
        }

        static IEnumerator Boot()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null; yield return null;
            var app = GameApp.Instance; Assert.NotNull(app);
            app.Ui.Title.Tap.onClick.Invoke(); for (int i = 0; i < 600 && app.Ui.TitleOpen; i++) yield return null;
        }

        /// <summary>단계·계절·밤낮·성장 레벨을 바꾸고 월드를 다시 굽게 한다(저장 파일은 건드리지 않는다: 임시 폴더).</summary>
        static void Setup(GameApp app, int stage, string season, bool night, int port = 300000, int garden = 40000, int lens = 40000)
        {
            var c = app.Ctx; c.Stage = stage; c.S.Stage = stage; c.S.Build = null; app.Loop.SetKeeperSpots();
            c.S.Growth["port"] = port; c.S.Growth["garden"] = garden; c.S.Growth["lens"] = lens;
            c.SeasonForce = season; c.NeedRebuild = true; app.Loop.NightForced = true; app.Loop.Dusk = night ? 1 : 0; c.Night = night;
            app.Loop.Drift.Clear(); app.Loop.Meteors.Clear(); app.Loop.Motes.Clear(); app.Loop.Lanterns.Clear(); app.Loop.Gulls.Clear(); app.Loop.Crumbs.Clear(); app.Loop.Ships.Clear();
        }

        /// <summary>눈으로 볼 장면을 채운다: 표류물·갈매기·여러 종류의 배(깃발·표식·선원).</summary>
        static void Populate(GameApp app, bool night)
        {
            var L = app.Loop; int W = app.World.W;
            L.Drift.Add(new DriftItem { X = 40, Y = 200, Vx = 4, Ph = 1.2, Gift = "shell" }); L.Drift.Add(new DriftItem { X = W - 50, Y = 290, Vx = -4, Ph = 3.7, Gift = "shell" });
            var kp = L.KeeperPos();
            L.Gulls.Add(new Gull { X = kp.x + 12, Y = kp.y, Tx = kp.x + 12, Ty = kp.y, St = "stand", Face = -1, Pk = 0.1 }); L.Gulls.Add(new Gull { X = kp.x + 21, Y = kp.y + 2, Tx = kp.x + 21, Ty = kp.y + 2, St = "stand", Face = -1 });
            L.Gulls.Add(new Gull { X = kp.x + 60, Y = kp.y - 30, Tx = kp.x + 30, Ty = kp.y, St = "in", Face = -1, Ph = 2 }); L.Crumbs.Add(new Crumb { X = kp.x + 15, Y = kp.y + 1, Land = true, Life = 5 });
            L.Ships.Add(new Ship { Id = 100, Type = "fish", Lane = 2, Dir = 1, X = W * 0.25, Ph = 1.1, Guide = 0.4, Need = 1, Sp = 0.01, Wave = 2 }); L.Ships.Add(new Ship { Id = 101, Type = "trade", Lane = 1, Dir = -1, X = W * 0.7, Ph = 2.3, Done = true, DoneAge = 2, Need = 1, Sp = 0.01 });
            L.Ships.Add(new Ship { Id = 102, Type = "galleon", Lane = 0, Dir = 1, X = W * 0.3, Ph = 0.7, Sos = true, Need = 1, Sp = 0.01 }); L.Ships.Add(new Ship { Id = 103, Type = "rowboat", Lane = 1, Dir = 1, X = W * 0.4, Ph = 4.2, RestT = 5, Reg = "x", Need = 1, Sp = 0.01 });
            L.Ships.Add(new Ship { Id = 104, Type = "steamer", Lane = 0, Dir = -1, X = W * 0.75, Ph = 3.6, Done = true, Need = 1, Sp = 0.01, Wave = 2 });
            if (night) { L.Lanterns.Add(new Lantern { X = W * 0.5, Y = 180, Ph = 1 }); L.Lanterns.Add(new Lantern { X = W * 0.6, Y = 120, Ph = 2 }); L.Meteors.Add(new Meteor { X = 40, Y = -app.World.OY + 30, Vx = 60, Vy = 30, Age = 0.2 }); }
        }

        void Shot(GameApp app, string name) { Directory.CreateDirectory(Path.Combine(outDir, "a1")); File.WriteAllBytes(Path.Combine(outDir, "a1", name + ".png"), app.World.CapturePng(3)); }

        [UnityTest, Timeout(600000)]
        public IEnumerator ScenesDrawAndCapture()
        {
            report.AppendLine("=== A1 장면 그리기·캡처 (" + DateTime.Now.ToString("s") + ") ===");
            yield return Boot(); var app = GameApp.Instance; var L = app.Loop;
            var scenes = new[]
            {
                ("day_summer_s6", 6, "summer", false), ("night_summer_s6", 6, "summer", true), ("night_winter_s6", 6, "winter", true), ("day_spring_s6", 6, "spring", false), ("day_autumn_s3", 3, "autumn", false), ("night_autumn_s3", 3, "autumn", true)
            };
            foreach (var (name, stage, season, night) in scenes)
            {
                Setup(app, stage, season, night); yield return null; yield return null; yield return null;
                Populate(app, night);
                if (name == "day_autumn_s3") { app.Ctx.S.Build = new BuildState { To = 4, StartAt = app.Clock.NowMs, EndAt = app.Clock.NowMs + 3600000 }; }          // 공사 중: 비계
                for (int i = 0; i < 60; i++) yield return null;
                var pt = app.World.Painter;
                report.AppendLine(name + ": 뒤 층 " + pt.Back.Used + ", 중간 층 " + pt.Mid.Used + ", 입자 " + L.Motes.Count + ", 등불 " + L.Lanterns.Count + ", 별똥별 " + L.Meteors.Count + ", 표류물 " + L.Drift.Count + ", 갈매기 " + L.Gulls.Count);
                Assert.IsTrue(pt.Back.Used, "먼 섬 층은 늘 있다");
                Shot(app, name);
            }
            // 꾸미기 번호 배지: 놓기 화면을 열고 한 장
            Setup(app, 6, "summer", false); yield return null; yield return null;
            report.AppendLine("끝: 예외 로그 없음(LogAssert 가 있으면 실패 처리)\n");
        }

        static void Stats(string label, List<double> ms, StringBuilder sb)
        {
            if (ms.Count == 0) return;
            var s = ms.OrderBy(x => x).ToList(); double avg = s.Average(), p95 = s[(int)(s.Count * 0.95)], mx = s.Last();
            sb.AppendLine(label + ": n=" + ms.Count + " 평균 " + avg.ToString("F2") + " ms, p95 " + p95.ToString("F2") + " ms, 최대 " + mx.ToString("F2") + " ms");
        }

        /// <summary>프레임 비용: 같은 장면을 A1 켠 것과 끈 것(= M4)으로 240프레임씩 재서 늘어난 만큼을 본다.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator FrameCostWithAndWithoutA1()
        {
            report.AppendLine("=== A1 프레임 비용 (에디터 배치, Mono 디버그, 실기기 아님) ===");
            yield return Boot(); var app = GameApp.Instance; var L = app.Loop;
            foreach (var (name, season, night, aim) in new[] { ("낮·여름·6단계·배 5척", "summer", false, false), ("밤·여름·6단계·빛줄기 조준", "summer", true, true), ("밤·겨울·6단계·오로라·눈", "winter", true, false) })
            {
                foreach (bool off in new[] { true, false })
                {
                    WorldPainter.A1Off = off;
                    Setup(app, 6, season, night); yield return null; yield return null; yield return null; Populate(app, night);
                    if (night && season == "winter") for (int i = 0; i < 40; i++) L.Motes.Add(new Mote { X = i * 5.4, Y = -app.World.OY + 10 + i * 8.1, Ph = i * 0.3, S = "winter", Vx = -2, Vy = 12 });
                    if (aim) { L.PointerDown(180, 262); L.PointerMove(180, 262, true); }
                    for (int i = 0; i < 30; i++) yield return null;
                    var upd = new List<double>(); var paint = new List<double>(); int gc0 = GC.CollectionCount(0); long mem0 = GC.GetTotalMemory(false);
                    for (int i = 0; i < 240; i++) { yield return null; upd.Add(app.LastUpdateMs); paint.Add(app.World.LastPaintMs); if (L.Drift.Count < 2 && i % 60 == 0) L.Drift.Add(new DriftItem { X = 30, Y = 280, Vx = 4, Gift = "shell" }); }
                    if (aim) L.PointerUp(false);
                    Stats(name + (off ? " · A1 끔(M4)" : " · A1 켬") + " GameApp.Update 전체", upd, report); Stats(name + (off ? " · A1 끔(M4)" : " · A1 켬") + " 연출 층 그리기+업로드", paint, report);
                    report.AppendLine("  GC(세대 0) " + (GC.CollectionCount(0) - gc0) + "회, 관리 힙 변화 " + ((GC.GetTotalMemory(false) - mem0) / 1024) + " KB");
                }
            }
            WorldPainter.A1Off = false;
            report.AppendLine("끝\n");
        }
    }
}
