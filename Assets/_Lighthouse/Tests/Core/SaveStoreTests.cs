using System;
using System.Collections.Generic;
using System.IO;
using Lighthouse.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>저장 파일 층: 본→백업→새 저장 순서, 마이그레이션 경유, 쓰기 왕복, 가져오기 후 상태.</summary>
    public class SaveStoreTests
    {
        sealed class MemStorage : IStorage
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public bool Exists(string n) => Files.ContainsKey(n);
            public string ReadText(string n) => Files[n];
            public void WriteTextAtomic(string n, string t) { Files[n] = t; }
            public void Delete(string n) { Files.Remove(n); }
        }

        static SaveStore Store(IStorage io) => new SaveStore(io, M1Fixture.Migrator(), M1Fixture.Clock());

        [Test]
        public void FreshStartCreatesDefaultsWithStartAt()
        {
            var s = Store(new MemStorage()); var st = s.Load();
            Assert.AreEqual("new", s.LoadedFrom); Assert.AreEqual(10, st.Ver); Assert.AreEqual(M1Fixture.FixMs, st.StartAt);
        }

        [Test]
        public void SaveLoadRoundTripKeepsEverythingAndBackupFollows()
        {
            var io = new MemStorage(); var s = Store(io); var st = s.Load();
            st.Guided = 77; st.Gifts["shell"] = 3; st.Found["shell"] = 1; st.Growth["lens"] = 50; st.Extra["lastTab"] = "mail";
            s.Save(st); Assert.IsFalse(io.Exists(SaveStore.BackupName), "첫 저장에는 백업이 없다");
            st.Guided = 78; s.Save(st); Assert.IsTrue(io.Exists(SaveStore.BackupName));
            var back = Store(io).Load();
            Assert.IsNull(M1Fixture.Diff(back.ToJson(), st.ToJson()));
            Assert.AreEqual(77, (int)((JObject)Js.Parse(io.Files[SaveStore.BackupName]))["guided"], "백업은 직전 본");
        }

        [Test]
        public void CorruptMainFallsBackToBackupThenToNew()
        {
            var io = new MemStorage(); var s = Store(io); var st = s.Load(); st.Guided = 5; s.Save(st); st.Guided = 6; s.Save(st);
            io.Files[SaveStore.MainName] = "{ 깨진 파일";
            var s2 = Store(io); var got = s2.Load(); Assert.AreEqual("backup", s2.LoadedFrom); Assert.AreEqual(5, (int)got.Guided);
            io.Files[SaveStore.BackupName] = "###";
            var s3 = Store(io); var got3 = s3.Load(); Assert.AreEqual("new", s3.LoadedFrom); Assert.AreEqual(0, (int)got3.Guided);
        }

        [Test]
        public void OldVersionFileIsMigratedOnLoad()
        {
            var io = new MemStorage(); io.Files[SaveStore.MainName] = "{\"stageV\":2,\"stage\":3,\"guided\":20,\"gifts\":{\"pearl\":2}}";
            var s = Store(io); var st = s.Load();
            Assert.AreEqual("main", s.LoadedFrom); Assert.AreEqual(10, st.Ver); Assert.AreEqual(3, st.Stage); Assert.AreEqual(2, (int)st.Totals["pearl"]);
        }

        [Test]
        public void FileStorageWritesAtomicallyToDisk()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lh_m1_" + Guid.NewGuid().ToString("N"));
            try
            {
                var fs = new FileStorage(dir); var s = Store(fs); var st = s.Load(); st.Guided = 9; s.Save(st); st.Guided = 10; s.Save(st);
                Assert.IsTrue(File.Exists(Path.Combine(dir, "save.json"))); Assert.IsTrue(File.Exists(Path.Combine(dir, "save.bak.json"))); Assert.IsFalse(File.Exists(Path.Combine(dir, "save.json.tmp")));
                Assert.AreEqual(10, (int)Store(new FileStorage(dir)).Load().Guided);
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Test]
        public void ImportedSaveGetsDeviceOwnedAndNoOfflineReward()
        {
            // 원본 importAct: 가져온 저장에 이 기기의 owned·ads 를 되살리고 lastSeen=지금 (가져오는 순간부터 다시 센다)
            var T = M1Fixture.Tables; var mig = M1Fixture.Migrator(); var sc = new SaveCode(T, mig);
            var device = GameState.FromJson(mig.Defaults()); device.Owned["noads"] = 1; device.Ads.Day = "2026-11-11"; device.Ads.Used["home"] = 1;
            var other = mig.Defaults(); other["stage"] = 4; other["guided"] = 900;
            var imported = GameState.FromJson(sc.Parse(sc.Export(other)).Data);
            imported.Owned = (JObject)device.Owned.DeepClone(); imported.Ads = device.Ads; imported.LastSeen = M1Fixture.FixMs;
            Assert.AreEqual(4, imported.Stage); Assert.AreEqual(1, (int)Js.Num(imported.Owned["noads"])); Assert.AreEqual(1, imported.Ads.Used["home"]);
            var ctx = new GameContext(T, imported, new Mulberry32(1), M1Fixture.Clock());
            Assert.IsNull(ctx.RunOffline((M1Fixture.FixMs - imported.LastSeen) / 1000.0), "가져온 직후 오프라인 보상 없음");
        }
    }
}
