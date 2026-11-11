using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.App;
using Lighthouse.Core;
using Lighthouse.UI.Model;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lighthouse.Tests
{
    /// <summary>
    /// M4 플레이 모드 스모크(에디터 배치, 그래픽 필요): 메인 씬을 실제로 돌려 예외 0 · 프레임 비용 실측 · 자동 저장 → 재시작 복원 · 오프라인 복귀를 확인한다.
    /// 시계는 수동(ManualClockMode)이라 배속으로 돌릴 수 있고, 저장 폴더는 임시 폴더다. 실기기·터치는 확인하지 않는다.
    /// </summary>
    public sealed class M4PlayTests
    {
        const string Scene = "Assets/_Lighthouse/Scenes/Main.unity";
        string dir; static readonly long Start = new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        readonly StringBuilder report = new StringBuilder();

        [SetUp] public void Up()
        {
            dir = Path.Combine(Path.GetTempPath(), "lh_m4play_" + Guid.NewGuid().ToString("N"));
            GameApp.ForceW = 1080; GameApp.ForceH = 2340; GameApp.SaveDirOverride = dir; GameApp.ManualClockMode = true; GameApp.TimeMul = 1; GameApp.ManualStartMs = Start;
        }

        [TearDown] public void Down()
        {
            GameApp.ForceW = GameApp.ForceH = 0; GameApp.SaveDirOverride = null; GameApp.ManualClockMode = false; GameApp.TimeMul = 1; GameApp.ManualStartMs = 0;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
            string outDir = Path.Combine(Application.dataPath, "..", "Tools", "m4_results"); Directory.CreateDirectory(outDir);
            File.AppendAllText(Path.Combine(outDir, "smoke.txt"), report.ToString(), new UTF8Encoding(false)); report.Clear();
        }

        static IEnumerator LoadMain()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null; yield return null;
        }

        static string StateJson(GameState s, bool dropSeen = true)
        {
            var o = s.ToJson(); if (dropSeen) o.Remove("lastSeen"); return Js.Stringify(o);
        }

        static void Stats(string label, List<double> ms, StringBuilder sb)
        {
            if (ms.Count == 0) return;
            var s = ms.OrderBy(x => x).ToList(); double avg = s.Average(), p95 = s[(int)(s.Count * 0.95)], mx = s.Last();
            sb.AppendLine(label + ": n=" + ms.Count + " 평균 " + avg.ToString("F2") + " ms, p95 " + p95.ToString("F2") + " ms, 최대 " + mx.ToString("F2") + " ms");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator FirstRunPlaySaveRestartRestore()
        {
            report.AppendLine("=== 첫 실행 → 플레이 → 저장 → 재시작 복원 (" + DateTime.Now.ToString("s") + ") ===");
            // ---------- 1. 첫 실행 ----------
            yield return LoadMain();
            var app = GameApp.Instance; Assert.NotNull(app, "GameApp 이 씬에서 살아야 한다");
            Assert.AreEqual("new", app.LoadedFrom, "저장이 없으면 새로 시작");
            Assert.IsTrue(app.Ui.TitleOpen, "타이틀이 떠 있다");
            yield return null; yield return null;
            Assert.NotNull(app.World.Sprites, "월드가 구워졌다");
            report.AppendLine("논리 화면 " + app.World.W + "x" + app.World.H + " (배율 " + app.World.Fit.Scale + ", 기기 " + Screen.width + "x" + Screen.height + "), 굽기+업로드 " + app.World.LastBakeMs.ToString("F1") + " ms");
            app.Ui.Title.Tap.onClick.Invoke();                                     // 화면을 눌러 시작
            Assert.IsTrue(app.Ctx.S.Seen, "시작하면 seen 기록");
            for (int i = 0; i < 600 && app.Ui.TitleOpen; i++) yield return null;
            Assert.IsFalse(app.Ui.TitleOpen, "타이틀이 닫힌다");

            // ---------- 2. 등속(1배) 프레임 비용 ----------
            var wall = new List<double>(); var upd = new List<double>(); var paint = new List<double>(); var bgm = new List<double>();
            for (int i = 0; i < 240; i++) { yield return null; wall.Add(Time.unscaledDeltaTime * 1000); upd.Add(app.LastUpdateMs); paint.Add(app.World.LastPaintMs); bgm.Add(app.World.LastBgMs); }
            Stats("프레임 간격(에디터 배치, 1배속, 밤 아닐 때 포함)", wall.Skip(10).ToList(), report); Stats("GameApp.Update 전체", upd.Skip(10).ToList(), report); Stats("연출 층 그리기+업로드", paint.Skip(10).ToList(), report); Stats("배경(하늘·바다) 그리기+업로드", bgm.Skip(10).ToList(), report);

            // ---------- 3. 배속으로 플레이: 낮·밤 한 바퀴 이상(하루 주기 600초) ----------
            GameApp.TimeMul = 40f; int toasts = 0; var sfx = new List<string>(); app.Loop.Sfx += s => sfx.Add(s); app.Loop.Toast += (a, b) => toasts++;
            var nightWall = new List<double>(); bool sawNight = false, sawDay = false;
            for (int i = 0; i < 900; i++)
            {
                yield return null;
                if (app.Loop.Dusk > 0.9) { sawNight = true; if (i % 3 == 0) nightWall.Add(app.LastUpdateMs); }
                if (app.Loop.Dusk < 0.1) sawDay = true;
            }
            Stats("GameApp.Update 밤(빛줄기·불빛) 구간, 40배속(틱 여러 번 포함)", nightWall, report);
            report.AppendLine("배속 플레이: 안내 " + app.Ctx.S.Guided + "척, 단계 " + app.Ctx.Stage + ", 도감 " + app.Ctx.Discovered() + ", 편지 " + app.Ctx.S.Letters.Count + ", 토스트 " + toasts + ", 배 " + app.Loop.Ships.Count + ", 소리 요청 " + sfx.Count + "건(" + string.Join(",", sfx.Distinct().Take(8)) + ")");
            Assert.IsTrue(sawDay && sawNight, "낮과 밤을 모두 지났다");
            Assert.Greater(app.Ctx.S.Guided, 20, "배가 안내돼야 한다");
            Assert.Greater(app.Save.Writes, 3, "자동 저장이 돌았다");
            // 밤을 1배속으로: 빛줄기·불빛이 가장 무거운 구간의 프레임 비용
            GameApp.TimeMul = 40f; for (int i = 0; i < 2000 && app.Loop.Dusk < 0.99; i++) yield return null;
            GameApp.TimeMul = 1f; var nUpd = new List<double>(); var nPaint = new List<double>(); var nWall = new List<double>(); app.Loop.PointerDown(180, 262); app.Loop.PointerMove(180, 262, true); int gc0 = GC.CollectionCount(0); long mem0 = GC.GetTotalMemory(false);     // 손가락으로 빛을 끌어 비춘다
            for (int i = 0; i < 240; i++) { yield return null; if (i > 10) { nUpd.Add(app.LastUpdateMs); nPaint.Add(app.World.LastPaintMs); nWall.Add(Time.unscaledDeltaTime * 1000); } }
            Stats("밤·빛줄기 조준·1배속 GameApp.Update 전체", nUpd, report); Stats("밤·빛줄기 조준·1배속 연출 층 그리기+업로드", nPaint, report); Stats("밤·빛줄기 조준·1배속 프레임 간격", nWall, report);
            report.AppendLine("밤 240프레임 동안 GC(세대 0) " + (GC.CollectionCount(0) - gc0) + "회, 관리 힙 변화 " + ((GC.GetTotalMemory(false) - mem0) / 1024) + " KB"); app.Loop.PointerUp(false);
            // 월드 한 장 캡처(연출 포함): 밤이 아닐 수도 있으니 지금 장면 그대로
            string capDir = Path.Combine(Application.dataPath, "..", "Tools", "m4_results", "m4"); Directory.CreateDirectory(capDir);
            File.WriteAllBytes(Path.Combine(capDir, "play_world_a.png"), app.World.CapturePng(2));

            // ---------- 4. 저장 → 재시작 복원 ----------
            app.FlushNow();
            string before = StateJson(app.Ctx.S); long lastSeen = (long)app.Ctx.S.LastSeen; double guided = app.Ctx.S.Guided; int stage = app.Ctx.Stage; int letters = app.Ctx.S.Letters.Count;
            Assert.That(File.Exists(Path.Combine(dir, "save.json")), "저장 파일");
            report.AppendLine("저장 파일 " + new FileInfo(Path.Combine(dir, "save.json")).Length + " 바이트, 백업 " + File.Exists(Path.Combine(dir, "save.bak.json")));
            GameApp.ManualStartMs = lastSeen + 1000;                              // 1초 뒤에 다시 켠다(비운 시간 보상 없음)
            yield return LoadMain();
            var app2 = GameApp.Instance; yield return null; yield return null;
            Assert.AreEqual("main", app2.LoadedFrom, "저장 파일에서 읽는다");
            Assert.IsNull(app2.LastNews, "1초 뒤 복귀는 소식 없음");
            Assert.AreEqual(before, StateJson(app2.Ctx.S), "재시작하면 저장한 상태 그대로(lastSeen 만 다르다)");
            Assert.AreEqual(stage, app2.Ctx.Stage); Assert.AreEqual(guided, app2.Ctx.S.Guided);
            Assert.IsTrue(app2.Ui.TitleOpen, "다시 켜면 타이틀: 함께한 날 문구");
            report.AppendLine("재시작 복원: 상태 JSON 동일, 단계 " + stage + ", 안내 " + guided + ", 편지 " + letters);

            // ---------- 5. 오프라인 복귀(5시간 뒤) ----------
            app2.FlushNow(); long ls2 = (long)app2.Ctx.S.LastSeen; GameApp.ManualStartMs = ls2 + 5L * 3600 * 1000;
            yield return LoadMain();
            var app3 = GameApp.Instance; yield return null; yield return null;
            Assert.NotNull(app3.LastNews, "5시간 비웠으면 소식이 있다");
            Assert.Greater(app3.Ctx.S.Guided, guided, "방치 보상으로 안내 수가 늘었다");
            app3.Ui.Title.Tap.onClick.Invoke(); for (int i = 0; i < 600 && app3.Ui.Ctl.Mode != PanelMode.News; i++) yield return null;
            Assert.AreEqual(PanelMode.News, app3.Ui.Ctl.Mode, "타이틀이 닫히면 그동안의 소식 화면");
            report.AppendLine("오프라인 복귀(5시간): 배 " + app3.LastNews.Count + "척 안내, 안내 " + guided + " → " + app3.Ctx.S.Guided + ", 소식 화면 열림");
            report.AppendLine("끝: 예외 로그 없음(LogAssert 가 있으면 실패 처리)\n");
        }

        /// <summary>
        /// 타이틀 그림: 메인 씬을 켜면 타이틀이 Visible 이고 별빛 장면 텍스처(포인트 필터, 정수 배율)가 올라가 있으며 화면을 덮고,
        /// 시간이 지나면 그림이 움직이고(빛줄기·물결), 여러 화면 비율에서도 잘리지 않게 놓인다. 장면 PNG 와 화면 전체 PNG 를 Tools/m4_results/title/ 에 남긴다.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator TitleShowsSceneImage()
        {
            report.AppendLine("=== 타이틀 그림 ===");
            string outDir = Path.Combine(Application.dataPath, "..", "Tools", "m4_results", "title"); Directory.CreateDirectory(outDir);
            foreach (var sz in new[] { new[] { 640, 1136 }, new[] { 1080, 2340 }, new[] { 1536, 2048 }, new[] { 2048, 1536 } })
            {
                GameApp.ForceW = sz[0]; GameApp.ForceH = sz[1];
                yield return LoadMain(); var app = GameApp.Instance; yield return null; yield return null;
                Assert.IsTrue(app.Ui.TitleOpen, "타이틀이 떠 있다 " + sz[0] + "x" + sz[1]);
                var tv = app.Ui.Title; Assert.IsTrue(tv.gameObject.activeInHierarchy, "타이틀 Visible"); Assert.AreEqual(1f, tv.Group.alpha, 0.001f);
                Assert.NotNull(tv.Scene, "장면 칸"); Assert.NotNull(tv.Scene.texture, "그림 텍스처가 null 이 아니다"); Assert.IsTrue(tv.Scene.enabled);
                var tex = tv.Scene.texture as Texture2D; Assert.NotNull(tex);
                Assert.AreEqual(FilterMode.Point, tex.filterMode, "포인트 필터"); Assert.AreEqual(tv.Renderer.Lw, tex.width); Assert.AreEqual(tv.Renderer.Lh, tex.height);
                Assert.GreaterOrEqual(tv.Renderer.Size.Sx, 2, "정수 배율"); Assert.IsFalse(tv.Name.gameObject.activeSelf, "예전 글자 텍스트는 숨겨져 있다");
                var rt = tv.Scene.rectTransform; float sc = app.Ui.Scale;
                Assert.GreaterOrEqual(rt.sizeDelta.x * sc + 0.5f, sz[0], "그림이 가로를 덮는다"); Assert.GreaterOrEqual(rt.sizeDelta.y * sc + 0.5f, sz[1], "그림이 세로를 덮는다");
                Assert.AreEqual(tv.Renderer.Lw * tv.Renderer.Size.Sx, rt.sizeDelta.x * sc, 0.6f, "정수 배율로 늘어난다");
                Assert.LessOrEqual(tv.Renderer.LogoTotalWidth, tv.Renderer.Lw, "글자가 잘리지 않는다");
                var colors = new HashSet<uint>(tv.Renderer.Pixels); Assert.Greater(colors.Count, 200, "단색이 아니라 그림이다 (색 " + colors.Count + "가지)");
                uint[] before = (uint[])tv.Renderer.Pixels.Clone(); for (int i = 0; i < 30; i++) yield return null;
                int moved = 0; for (int i = 0; i < before.Length; i++) if (before[i] != tv.Renderer.Pixels[i]) moved++;
                Assert.Greater(moved, 50, "시간이 지나면 그림이 움직인다(글자 내려앉기·별 반짝임·빛줄기)");
                string tag = sz[0] + "x" + sz[1];
                Lighthouse.Render.Verify.PngLite.Save(Path.Combine(outDir, "play_scene_" + tag + ".png"), tv.Renderer.Pixels, tv.Renderer.Lw, tv.Renderer.Lh, 2);
                report.AppendLine("타이틀 " + tag + ": 그림 " + tv.Renderer.Lw + "x" + tv.Renderer.Lh + " 도트 x" + tv.Renderer.Size.Sx + ", 색 " + colors.Count + "가지, 30프레임 뒤 바뀐 점 " + moved + ", 부제 " + tv.Tagline.text + " / " + tv.Day.text);
                yield return CaptureUi(app, Path.Combine(outDir, "play_title_" + tag + ".png"), sz[0], sz[1]);
            }
            GameApp.ForceW = GameApp.ForceH = 0;
        }

        /// <summary>UI 전체(글자 포함)를 기기 크기 그림으로 한 장. 오버레이 캔버스를 잠깐 카메라 캔버스로 바꿔 RenderTexture 에 그린다. 실패해도 시험은 실패시키지 않는다.</summary>
        static IEnumerator CaptureUi(GameApp app, string path, int w, int h)
        {
            yield return new WaitForEndOfFrame();
            GameObject cg = null; RenderTexture rt = null;
            try
            {
                cg = new GameObject("titleCapCam"); var cam = cg.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.cullingMask = 1 << 5;
                rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
                var canvas = app.Ui.Canvas; var mode = canvas.renderMode; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases(); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev;
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex)); UnityEngine.Object.Destroy(tex); canvas.renderMode = mode;
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("타이틀 전체 캡처 실패(무시): " + e.Message); }
            finally { if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); } if (cg != null) UnityEngine.Object.Destroy(cg); }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator TapsAndInputReachWorld()
        {
            report.AppendLine("=== 입력·말풍선·승급 연출 ===");
            yield return LoadMain(); var app = GameApp.Instance; yield return null; yield return null;
            app.Ui.Title.Tap.onClick.Invoke(); for (int i = 0; i < 600 && app.Ui.TitleOpen; i++) yield return null;
            var L = app.Loop;
            // 배 탭 → 말풍선(월드 연동: UI 말풍선 켜짐)
            var sh = L.Ships.FirstOrDefault(); Assert.NotNull(sh, "배가 있다");
            L.TapAt(sh.X, Lanes_Y(sh) - 8); yield return null; yield return null;
            Assert.NotNull(L.Bubble, "탭하면 말풍선 정보");
            var bub = app.Ui.GetComponentInChildren<BubbleView>(true); Assert.IsTrue(bub.gameObject.activeSelf, "UI 말풍선이 켜진다");
            report.AppendLine("말풍선: \"" + L.Bubble.Text + "\" 캔버스 위치 " + bub.Box.anchoredPosition);
            // 등대 탭 → 램프 번쩍
            L.TapAt(app.World.W / 2f, 190); Assert.Greater(L.Flash, 0.5);
            // 우체통 탭 → 편지함
            var mp = L.MailboxPos(); L.TapAt(mp.x, mp.y - 7); yield return null; Assert.AreEqual(PanelMode.Mail, app.Ui.Ctl.Mode, "우체통 탭 → 편지함");
            app.Ui.Ctl.Close(); app.Ui.Panel.Refresh(false);
            // 승급 연출: 조건을 채워 공사 → 끝
            app.Ctx.S.Guided = 100000; foreach (var g in app.Ctx.T.Gifts) app.Ctx.S.Found[g.Id] = 1; foreach (var s in app.Ctx.T.Ships) app.Ctx.S.Dex[s.Id] = 1;
            GameApp.TimeMul = 1; for (int i = 0; i < 10; i++) yield return null;
            Assert.IsNotNull(app.Ctx.S.Build, "공사 시작");
            app.Ctx.S.Build.EndAt = app.Clock.NowMs + 500; for (int i = 0; i < 90; i++) yield return null;
            Assert.GreaterOrEqual(app.Ctx.Stage, 2, "승급"); Assert.Greater(app.Loop.Sparks.Count + app.Loop.Birds.Count, 0, "승급 연출(불꽃·갈매기)");
            report.AppendLine("승급: 단계 " + app.Ctx.Stage + ", 불꽃 " + app.Loop.Sparks.Count + ", 갈매기 " + app.Loop.Birds.Count);
            // 사진 모드 → 저장
            app.Ui.SetPhoto(true); yield return null; app.Ui.PhotoSaveRequested += () => { }; var photoBefore = app.LastPhotoPath;
            app.Ui.Photo.Save.onClick.Invoke(); yield return null;
            Assert.NotNull(app.LastPhotoPath, "사진 파일 경로"); Assert.That(File.Exists(app.LastPhotoPath), "사진 PNG 가 저장됐다");
            report.AppendLine("사진: " + app.LastPhotoPath + " (" + new FileInfo(app.LastPhotoPath).Length + " 바이트)");
            app.Ui.SetPhoto(false);
            // 계절 전환: 강제 계절 바꾸면 월드 다시 굽기
            int bakes = 0; app.Loop.Rebuild += _ => bakes++; app.Ctx.SeasonForce = "winter"; yield return null; yield return null; yield return null;
            Assert.Greater(bakes, 0, "계절이 바뀌면 월드를 다시 굽는다");
            string capDir = Path.Combine(Application.dataPath, "..", "Tools", "m4_results", "m4"); Directory.CreateDirectory(capDir);
            File.WriteAllBytes(Path.Combine(capDir, "play_world_winter.png"), app.World.CapturePng(2));
            report.AppendLine("계절 전환 확인(겨울), 월드 캡처 저장\n");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator WorldCapturesLookRight()
        {
            report.AppendLine("=== 월드 캡처(상태별) ===");
            yield return LoadMain(); var app = GameApp.Instance; yield return null; yield return null;
            app.Ui.Title.Tap.onClick.Invoke(); for (int i = 0; i < 600 && app.Ui.TitleOpen; i++) yield return null;
            var L = app.Loop; string capDir = Path.Combine(Application.dataPath, "..", "Tools", "m4_results", "m4"); Directory.CreateDirectory(capDir);
            // 6단계 + 꾸미기 풀세트로
            app.Ctx.Stage = 6; app.Ctx.S.Stage = 6; L.SetKeeperSpots(); app.Ctx.S.Growth["garden"] = 5000; app.Ctx.S.Growth["lens"] = 3000; app.Ctx.S.Growth["port"] = 3000;
            foreach (var sl in Lighthouse.Render.DecorLayout.DevFill()) app.Ctx.S.Isle.Put[sl.slot] = sl.item;
            app.Ui.WorldDirty(); for (int i = 0; i < 6; i++) yield return null;
            // 배를 여러 척 띄워 둔다
            foreach (var t in new[] { "cruise", "schooner", "trade", "ferry", "steamer", "fish" }) { var s = L.SpawnShip(true); s.Type = t; }
            GameApp.TimeMul = 1;
            void Cap(string name, double dusk, string wx, double amt, string season = null, bool aim = false)
            {
                L.NightForced = true; L.Dusk = dusk; app.Ctx.Night = dusk >= 0.5; L.Wx.Kind = wx; L.Wx.Amt = amt; app.Ctx.Wx = wx; app.Ctx.SeasonForce = season;
                L.Wx.T0 = app.Clock.NowMs; L.Wx.Until = app.Clock.NowMs + 3600000; L.Wx.Next = app.Clock.NowMs + 7200000;
                if (aim) { L.PointerDown(190, 270); L.PointerMove(190, 270, true); } else L.PointerUp(false);
            }
            var cases = new (string name, double dusk, string wx, double amt, string season, bool aim)[]
            {
                ("day_summer", 0, "clear", 0, "summer", false), ("dusk_evening", 0.55, "clear", 0, "summer", false), ("night_beam_aim", 1, "clear", 0, "summer", true), ("night_fog", 1, "fog", 1, "autumn", false),
                ("day_rain", 0, "rain", 1, "summer", false), ("day_snow_winter", 0, "rain", 1, "winter", false), ("day_fog", 0, "fog", 1, "autumn", false)
            };
            foreach (var c in cases)
            {
                Cap(c.name, c.dusk, c.wx, c.amt, c.season, c.aim);
                for (int i = 0; i < 40; i++) yield return null;
                File.WriteAllBytes(Path.Combine(capDir, "world_" + c.name + ".png"), app.World.CapturePng(2));
            }
            L.PointerUp(false); report.AppendLine("캡처 " + cases.Length + "장: " + string.Join(", ", cases.Select(c => c.name)) + " (논리 " + app.World.W + "x" + app.World.H + " 정수 ×2)\n");
        }

        sealed class ManualClockAccess { }
        static double Lanes_Y(Lighthouse.World.Ship s) => Lighthouse.World.Lanes.Y[s.Lane];
    }
}
