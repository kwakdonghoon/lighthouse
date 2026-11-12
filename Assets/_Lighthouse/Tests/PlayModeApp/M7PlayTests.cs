using System;
using System.Collections;
using System.IO;
using Lighthouse.App;
using Lighthouse.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lighthouse.Tests
{
    /// <summary>
    /// 영역 C PlayMode 시험(에디터 배치, 그래픽 필요): 가져오기 확정 → GameApp.Restart 경로(M4 에서 시험하지 않았던 부분)와 손상 저장 알림.
    /// 시계는 수동(ManualClockMode), 저장 폴더는 임시 폴더. 실기기는 확인하지 않는다.
    /// </summary>
    public sealed class M7PlayTests
    {
        const string Scene = "Main";
        string dir; static readonly long Start = new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        [SetUp] public void Up()
        {
            dir = Path.Combine(Path.GetTempPath(), "lh_m7play_" + Guid.NewGuid().ToString("N"));
            GameApp.ForceW = 1080; GameApp.ForceH = 2340; GameApp.SaveDirOverride = dir; GameApp.ManualClockMode = true; GameApp.TimeMul = 1; GameApp.ManualStartMs = Start;
        }
        [TearDown] public void Down()
        {
            GameApp.ForceW = GameApp.ForceH = 0; GameApp.SaveDirOverride = null; GameApp.ManualClockMode = false; GameApp.TimeMul = 1; GameApp.ManualStartMs = 0;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
        }

        static IEnumerator LoadMain() { yield return SceneManager.LoadSceneAsync(Scene, LoadSceneMode.Single); yield return null; yield return null; }
        JObject ReadSave(string name) => (JObject)Js.Parse(File.ReadAllText(Path.Combine(dir, name)));

        [UnityTest, Timeout(300000)]
        public IEnumerator ImportThenRestartKeepsDeviceOwnedAndAdsAndNothingOverwritesIt()
        {
            yield return LoadMain();
            var app = GameApp.Instance; Assert.NotNull(app);
            var S = app.Ctx.S;
            S.Owned["noads"] = 1; S.Ads.Day = app.Ctx.TodayKey(); S.Ads.Used["home"] = 2; S.Guided = 5; app.Ctx.NotifyChanged(); app.FlushNow();
            var mig = new SaveMigrator(app.T, app.Clock); var sc = new SaveCode(app.T, mig);
            var other = mig.Defaults(); other["stage"] = 4; other["guided"] = 900;
            var parsed = sc.Parse(sc.Export(other)); Assert.IsNull(parsed.Err, parsed.Err);
            var oldCtx = app.Ctx;

            app.Ui.ImportSave(parsed.Data);                                                    // "정말 덮어쓰기" 와 같은 호출
            app.SendMessage("OnApplicationPause", true, SendMessageOptions.DontRequireReceiver);       // 재시작을 기다리는 사이 앱이 가려져도 옛 상태를 쓰면 안 된다
            Assert.AreEqual(900, (int)ReadSave("save.json")["guided"], "가져온 상태가 곧바로 파일에 있다");
            Assert.AreEqual(5, (int)ReadSave("save.pre-import.json")["guided"], "가져오기 전 저장이 한 벌 남는다");

            for (int i = 0; i < 60 && ReferenceEquals(app.Ctx, oldCtx); i++) yield return null;      // Invoke(Restart) 가 다음 프레임에 돈다
            Assert.IsFalse(ReferenceEquals(app.Ctx, oldCtx), "재시작해서 새 GameContext 가 생겼다");
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual("main", app.LoadedFrom); Assert.AreEqual(4, app.Ctx.Stage); Assert.AreEqual(900, (int)app.Ctx.S.Guided);
            Assert.IsNotNull(app.Ctx.S.Owned["noads"], "이 기기의 구매는 남는다"); Assert.AreEqual(2, app.Ctx.S.Ads.Used["home"], "이 기기의 광고 횟수는 남는다");
            Assert.IsNull(app.LastNews, "가져온 직후엔 비운 시간 보상이 없다");
            for (int i = 0; i < 30; i++) yield return null;
            app.FlushNow();
            Assert.AreEqual(900, (int)ReadSave("save.json")["guided"], "그 뒤 저장들도 가져온 상태");
            Assert.IsTrue(app.Ui.TitleOpen, "다시 시작하면 타이틀부터");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FailedImportKeepsTheGameRunning()
        {
            yield return LoadMain();
            var app = GameApp.Instance; var ctx = app.Ctx; double g = ctx.S.Guided; long f0 = app.Frames;
            var bad = new JObject { ["ver"] = 10, ["totals"] = JValue.CreateNull() };                         // 마이그레이션이 거부하는 값
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("가져오기 실패"));
            app.Ui.ImportSave(bad);
            for (int i = 0; i < 20; i++) yield return null;
            Assert.IsTrue(ReferenceEquals(ctx, app.Ctx), "실패하면 재시작하지 않는다"); Assert.AreEqual(g, app.Ctx.S.Guided);
            Assert.Greater(app.Frames, f0 + 10, "게임이 계속 돈다(restarting 이 풀렸다)");
            Assert.IsTrue(app.Ui.ToastState.Visible, "저장하지 못했다는 안내");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CorruptMainStartsFromBackupAndTellsThePlayer()
        {
            // 정상 저장 둘(본·백업)을 만든 뒤 본 파일을 망가뜨리고 켠다
            var T = Lighthouse.Data.DataLoader.Load(new Lighthouse.Data.ResourcesDataSource());
            var clock = new SystemClock(); var mig = new SaveMigrator(T, clock); var store = new SaveStore(new FileStorage(dir), mig, clock);
            var st = store.Load(); st.Seen = true; st.Guided = 41; store.Save(st); st.Guided = 42; store.Save(st);
            File.WriteAllText(Path.Combine(dir, "save.json"), "{ 깨진 파일");
            yield return LoadMain();
            var app = GameApp.Instance;
            Assert.AreEqual("backup", app.LoadedFrom); Assert.AreEqual(41, (int)app.Ctx.S.Guided);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "save.corrupt.json")), "깨진 파일은 따로 남는다");
            app.Ui.Title.Tap.onClick.Invoke();
            for (int i = 0; i < 600 && app.Ui.TitleOpen; i++) yield return null;
            Assert.IsFalse(app.Ui.TitleOpen);
            Assert.IsTrue(app.Ui.ToastState.Visible, "복원 안내가 뜬다"); StringAssert.Contains("읽지 못했어요", app.Ui.ToastState.Title);
            Assert.IsNotNull(app.Store); Assert.AreEqual(LoadNotice.RestoredBackup, app.Store.Notice);
        }
    }
}
