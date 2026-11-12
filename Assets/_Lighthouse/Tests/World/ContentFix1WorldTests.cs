using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.UI.Model;
using Lighthouse.World;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>콘텐츠 보완 1차(unity-content-parity P1-2): 처음 발견·빛나는 선물·편지 도착·저장 실패 알림이 원본 시점·문구로 연결됐는지. 순수 C#(dotnet test·EditMode 공용).</summary>
    public sealed class ContentFix1WorldTests
    {
        static GameTables T => M1Fixture.Tables;
        static UiStrings Str => UiStrings.Parse(File.ReadAllText(Path.Combine(M1Fixture.DataDir, "ko", "ui.json"), new System.Text.UTF8Encoding(false)));

        sealed class Rig
        {
            public ManualClock Clock; public GameContext Ctx; public GameLoop Loop; public List<string> Toasts = new List<string>(), Sfx = new List<string>();
            public static Rig Make(long seed, int stage = 1, int guided = 5)
            {
                var clock = new ManualClock(M1Fixture.FixMs, M1Fixture.TzMinutes);
                var st = GameState.FromJson(new SaveMigrator(T, clock).Defaults()); st.Seen = true; st.Stage = stage; st.Guided = guided; st.StartAt = clock.Now;
                var ctx = new GameContext(T, st, new Mulberry32(seed), clock) { Stage = stage };
                var loop = new GameLoop(ctx, Str, new Mulberry32(seed + 1000));
                var r = new Rig { Clock = clock, Ctx = ctx, Loop = loop };
                loop.Toast += (a, b) => r.Toasts.Add(a + " / " + b); loop.Sfx += s => r.Sfx.Add(s);
                loop.SetView(216, 468, 468 - 320);
                return r;
            }
            public void Run(double sec, double dt = 0.1) { int n = (int)(sec / dt); for (int i = 0; i < n; i++) { Clock.AdvanceSec(dt); Loop.Tick(dt); } }
        }

        static int Count(List<string> l, string prefix) => l.Count(s => s.StartsWith(prefix));

        // ---------- 빛나는 선물 ----------
        [Test]
        public void ShinyGiftShowsToastAndLevelSoundUnlessQuietOrSim()
        {
            var r = Rig.Make(1); string g = T.Gifts[0].Id;
            r.Ctx.AddGift(g, new GameContext.AddGiftOpts { Shiny = true });
            Assert.AreEqual(1, Count(r.Toasts, "빛나는 선물을 찾았어요 / 반짝이는 "), string.Join(" | ", r.Toasts));
            StringAssert.Contains(GameContext.FixJosa("반짝이는 " + T.Gifts[0].Name + "{이/가} 왔어요"), r.Toasts[0]);
            CollectionAssert.Contains(r.Sfx, "level");
            r.Toasts.Clear(); r.Sfx.Clear();
            r.Ctx.AddGift(g, new GameContext.AddGiftOpts { Shiny = true, Quiet = true });         // 편지 선물·오프라인·기념일은 조용히
            r.Ctx.AddGift(g, new GameContext.AddGiftOpts { Shiny = false });                         // 빛나지 않으면 알리지 않는다
            r.Ctx.Sim = true; r.Ctx.AddGift(g, new GameContext.AddGiftOpts { Shiny = true }); r.Ctx.Sim = false;      // 시뮬레이터는 알림 대기열을 건너뛴다
            Assert.IsEmpty(r.Toasts); CollectionAssert.DoesNotContain(r.Sfx, "level");
        }

        // ---------- 편지 도착 ----------
        [Test]
        public void FinaleLetterArrivalShowsMailboxToastOnce()
        {
            var r = Rig.Make(2, stage: T.MaxStage);
            Assert.IsTrue(r.Ctx.DeliverFinale(false));
            CollectionAssert.AreEqual(new[] { "우체통에 편지가 도착했어요 / 천천히 읽어 보세요" }, r.Toasts);
            CollectionAssert.Contains(r.Sfx, "mail");
            Assert.Greater(r.Loop.MailAlertUntil, r.Loop.Perf, "우체통 반짝임(4초)이 켜진다");
            var q = Rig.Make(3, stage: T.MaxStage); Assert.IsTrue(q.Ctx.DeliverFinale(true)); Assert.IsEmpty(q.Toasts, "quiet 이면 알리지 않는다");
        }

        [Test]
        public void SeveralGuestLettersAtOnceShowCountInOneToast()
        {
            var r = Rig.Make(4, guided: 10);
            r.Ctx.S.StartAt = r.Clock.Now - (long)(T.Anniv.Max(a => a.Day) + 5) * T.Bal.AnnivDayMs;      // 기념일 편지가 모두 한꺼번에 도착하는 날
            int n = r.Ctx.DeliverGuests(false);
            Assert.GreaterOrEqual(n, 2, "시험 전제: 한꺼번에 2통 이상");
            Assert.AreEqual(1, r.Toasts.Count, "여러 통이 와도 알림은 한 번");
            Assert.AreEqual("우체통에 편지가 도착했어요 / " + n + "통이 와 있어요", r.Toasts[0]);
            r.Toasts.Clear(); Assert.AreEqual(0, r.Ctx.DeliverGuests(false)); Assert.IsEmpty(r.Toasts, "이미 받은 편지는 다시 알리지 않는다");
            var q = Rig.Make(5, guided: 10); q.Ctx.S.StartAt = q.Clock.Now - (long)(T.Anniv.Max(a => a.Day) + 5) * T.Bal.AnnivDayMs;
            q.Ctx.DeliverGuests(true); Assert.IsEmpty(q.Toasts, "quiet(오프라인·시뮬)이면 알리지 않는다");
        }

        // ---------- 처음 만난 것 ----------
        [Test]
        public void FirstMetToastsUseOriginalWordingAndPacing()
        {
            var r = Rig.Make(6, guided: 5);
            string ship = T.Ships[0].Id, gift = T.Gifts[0].Id;
            r.Ctx.QueueFirst("ship", ship); r.Ctx.QueueFirst("gift", gift); r.Ctx.QueueFirst("letter", "nature");
            r.Run(5.5); Assert.IsEmpty(r.Toasts, "켠 지 6초 안에는 기다린다(원본 now - firstLast < 6000)");
            r.Run(1.0);
            Assert.AreEqual("처음 만난 배, " + T.Ships[0].Name + " / 도감에 남겨 뒀어요", r.Toasts.First(t => t.StartsWith("처음 만난 배")));
            CollectionAssert.Contains(r.Sfx, "tick");
            r.Run(5.0); Assert.AreEqual(0, Count(r.Toasts, "처음 받은 선물"), "다음 알림은 6초 뒤");
            r.Run(1.5); Assert.AreEqual("처음 받은 선물, " + T.Gifts[0].Name + " / 도감에 남겨 뒀어요", r.Toasts.First(t => t.StartsWith("처음 받은 선물")));
            r.Run(6.5); CollectionAssert.Contains(r.Toasts, "우체통에 편지가 도착했어요 / 천천히 읽어 보세요");
            Assert.IsEmpty(r.Ctx.FirstQ);
        }

        [Test]
        public void FirstMetSeaWordsFollowGroupAndRarity()
        {
            foreach (var (grp, rare, want) in new[] { ("sky", false, "새 하늘 기록, "), ("sky", true, "드문 하늘 기록, "), ("sea", false, "새 바다 친구, "), ("sea", true, "드문 바다 친구, ") })
            {
                var sd = T.Sea.FirstOrDefault(s => s.Grp == grp && s.Rare == rare); if (sd == null) continue;       // 표에 없는 조합은 건너뜀
                var r = Rig.Make(7, guided: 5); r.Ctx.QueueFirst("sea", sd.Id); r.Run(7);
                Assert.AreEqual(want + sd.Name + " / 도감에 남겨 뒀어요", r.Toasts.FirstOrDefault(t => t.Contains("도감에 남겨")), grp + rare);
            }
        }

        [Test]
        public void FirstMetWaitsWhileUiBusyOrBuildingAndStopsAfterThree()
        {
            var r = Rig.Make(8, guided: 5); bool busy = true; r.Loop.UiBusy = () => busy;
            r.Ctx.QueueFirst("ship", T.Ships[0].Id); r.Run(20); Assert.IsEmpty(r.Toasts, "팝업·타이틀·다른 알림이 떠 있으면 기다린다");
            busy = false; r.Ctx.S.Build = new BuildState { To = 2, StartAt = r.Clock.Now, EndAt = r.Clock.Now + 3600000 }; r.Run(2); Assert.AreEqual(0, Count(r.Toasts, "처음 만난 배"), "공사 중에는 기다린다");
            r.Ctx.S.Build = null; r.Run(0.5); Assert.AreEqual(1, Count(r.Toasts, "처음 만난 배"));

            var q = Rig.Make(9, guided: 5);
            for (int i = 0; i < 4; i++) q.Ctx.QueueFirst("gift", T.Gifts[i].Id);
            q.Run(40); Assert.AreEqual(3, Count(q.Toasts, "처음 받은 선물"), "켠 동안 3번까지");
            Assert.IsEmpty(q.Ctx.FirstQ, "넘치는 알림은 버린다(도감에는 남는다)");
        }

        [Test]
        public void FirstMetIsSilentDuringFirstThreeGuides()
        {
            var r = Rig.Make(10, guided: 2); r.Ctx.QueueFirst("ship", T.Ships[0].Id); Assert.IsEmpty(r.Ctx.FirstQ, "처음 3척 동안은 알리지 않는다"); r.Run(10); Assert.IsEmpty(r.Toasts);
        }

        // ---------- 저장 실패 ----------
        sealed class FailingStorage : IStorage
        {
            readonly Dictionary<string, string> m = new Dictionary<string, string>(); public bool Fail;
            public bool Exists(string n) => m.ContainsKey(n); public string ReadText(string n) => m[n];
            public void WriteTextAtomic(string n, string t) { if (Fail) throw new IOException("디스크가 가득 찼어요"); m[n] = t; }
            public void Delete(string n) { m.Remove(n); }
        }

        [Test]
        public void SaveFailureNoticeIsThrottledAndResetsAfterSuccess()
        {
            var r = Rig.Make(11); var io = new FailingStorage(); var store = new SaveStore(io, new SaveMigrator(T, r.Clock), r.Clock);
            var svc = new SaveService(store, r.Ctx, r.Clock); int fails = 0, notices = 0; svc.SaveFailed += e => fails++; svc.SaveFailedNotice += () => notices++;
            io.Fail = true;
            for (int i = 0; i < 10; i++) { r.Ctx.NotifyChanged(); svc.Tick(2.5); }          // 25초 동안 10번 실패
            Assert.AreEqual(10, fails); Assert.AreEqual(1, notices, "연달아 실패해도 30초에 한 번만 알린다");
            for (int i = 0; i < 4; i++) { r.Ctx.NotifyChanged(); svc.Tick(2.5); }           // 35초째
            Assert.AreEqual(2, notices, "30초가 지나면 다시 알린다");
            io.Fail = false; r.Ctx.NotifyChanged(); svc.Tick(2.5); Assert.Greater(svc.Writes, 0);
            io.Fail = true; r.Ctx.NotifyChanged(); svc.Tick(2.5); Assert.AreEqual(3, notices, "한 번 성공한 뒤의 실패는 곧바로 알린다");
        }

        [Test]
        public void SaveFailedKeysExist()
        {
            var s = Str; Assert.AreEqual("저장하지 못했어요", s.Get("world.saveFailed")); Assert.AreEqual("저장 공간을 확인해 주세요", s.Get("world.saveFailedSub")); Assert.That(s.Missing, Is.Empty);
        }
    }
}
