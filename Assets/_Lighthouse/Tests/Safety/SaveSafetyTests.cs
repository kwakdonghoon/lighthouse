using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>
    /// 저장 원자화·손상 복원 시험(영역 C ①, 순수 C#: dotnet test 와 Unity EditMode 공용).
    /// 실제 파일 시스템(FileStorage)으로: 교체 직전 강제 종료, 부분 쓰기(잘린 파일·빈 파일·딴 내용), 둘 다 손상, 진짜 프로세스 강제 종료를 시험한다.
    /// </summary>
    public sealed class SaveSafetyTests
    {
        string dir;
        [SetUp] public void Up() { dir = Path.Combine(Path.GetTempPath(), "lh_m7_" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void Down() { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { } }

        static SaveStore Store(IStorage io) => new SaveStore(io, M1Fixture.Migrator(), M1Fixture.Clock());
        string P(string n) => Path.Combine(dir, n);

        /// <summary>guided 값만 다른 저장을 n 번째까지 쓴다.</summary>
        static GameState Write(SaveStore s, int g)
        {
            var st = s.Load(); st.Guided = g; st.Gifts["shell"] = g; st.Found["shell"] = 1; s.Save(st); return st;
        }

        // ---------- 교체 직전 강제 종료 ----------
        [Test]
        public void CrashBeforeCommitLeavesPreviousMainIntactAndTempIsCleanedOnNextStart()
        {
            var fs = new FileStorage(dir); var s = Store(fs);
            Write(s, 1); Write(s, 2);
            fs.BeforeCommit = name => { if (name == SaveStore.MainName) throw new IOException("강제 종료 흉내"); };
            Assert.Throws<IOException>(() => Write(s, 3));
            Assert.IsTrue(File.Exists(P("save.json.tmp")), "끊긴 순간의 임시 파일이 남아 있다");
            var fs2 = new FileStorage(dir); var s2 = Store(fs2); var got = s2.Load();
            Assert.AreEqual("main", s2.LoadedFrom); Assert.AreEqual(LoadNotice.None, s2.Notice);
            Assert.AreEqual(2, (int)got.Guided, "본 파일은 옛 본(2) 그대로");
            Assert.IsFalse(File.Exists(P("save.json.tmp")), "다음 시작 때 임시 파일을 치운다");
        }

        [Test]
        public void BackupWriteFailureDoesNotBlockMainAndLeavesOldBackup()
        {
            var fs = new FileStorage(dir); var s = Store(fs); Write(s, 1); Write(s, 2);
            fs.BeforeCommit = name => { if (name == SaveStore.BackupName) throw new IOException("백업 쓰는 중 종료"); };
            Assert.DoesNotThrow(() => Write(s, 3), "백업 실패는 삼키고 본 파일은 쓴다(저장이 막히면 안 된다)");
            fs.BeforeCommit = null;
            var s2 = Store(new FileStorage(dir)); var got = s2.Load();
            Assert.AreEqual("main", s2.LoadedFrom); Assert.AreEqual(3, (int)got.Guided);
            Assert.AreEqual(1, (int)((JObject)Js.Parse(File.ReadAllText(P("save.bak.json"))))["guided"], "백업은 이전 것 그대로(온전)");
        }

        [Test]
        public void ReplaceKeepsExistingFileUntilTheSwapAndLeavesNoTemp()
        {
            var fs = new FileStorage(dir); fs.WriteTextAtomic("a.txt", "one"); fs.WriteTextAtomic("a.txt", "two");
            Assert.AreEqual("two", fs.ReadText("a.txt")); Assert.IsFalse(File.Exists(P("a.txt.tmp")));
            fs.BeforeCommit = n => throw new IOException("x");
            Assert.Throws<IOException>(() => fs.WriteTextAtomic("a.txt", "three"));
            Assert.AreEqual("two", File.ReadAllText(P("a.txt")), "교체 전에 끊기면 옛 내용");
        }

        // ---------- 부분 쓰기·손상 ----------
        [Test]
        public void TruncatedMainAtEveryCutFallsBackToBackup()
        {
            var fs = new FileStorage(dir); var s = Store(fs); Write(s, 5); Write(s, 6);
            string full = File.ReadAllText(P("save.json")); int cuts = 0;
            for (int cut = 0; cut < full.Length; cut += Math.Max(1, full.Length / 60))
            {
                File.WriteAllText(P("save.json"), full.Substring(0, cut));
                var s2 = Store(new FileStorage(dir)); var got = s2.Load();
                Assert.AreEqual("backup", s2.LoadedFrom, "cut=" + cut); Assert.AreEqual(5, (int)got.Guided, "cut=" + cut);
                Assert.AreEqual(LoadNotice.RestoredBackup, s2.Notice, "cut=" + cut); cuts++;
            }
            Assert.Greater(cuts, 30);
            Assert.IsTrue(File.Exists(P("save.corrupt.json")), "손상 본을 따로 남긴다");
        }

        [TestCase("")]
        [TestCase("   \n ")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("123")]
        [TestCase("\"문자열\"")]
        [TestCase("{\"hello\":1}")]
        [TestCase("\0\0\0\0\0\0\0\0")]
        public void UnusableMainContentFallsBackToBackup(string junk)
        {
            var fs = new FileStorage(dir); var s = Store(fs); Write(s, 5); Write(s, 6);
            File.WriteAllText(P("save.json"), junk);
            var s2 = Store(new FileStorage(dir)); var got = s2.Load();
            Assert.AreEqual("backup", s2.LoadedFrom); Assert.AreEqual(5, (int)got.Guided); Assert.AreEqual(LoadNotice.RestoredBackup, s2.Notice);
        }

        [Test]
        public void BothCorruptStartsFreshKeepsRawAndNotifies()
        {
            var fs = new FileStorage(dir); var s = Store(fs); Write(s, 5); Write(s, 6);
            File.WriteAllText(P("save.json"), "{ main 깨짐"); File.WriteAllText(P("save.bak.json"), "backup 깨짐");
            var s2 = Store(new FileStorage(dir)); var got = s2.Load();
            Assert.AreEqual("new", s2.LoadedFrom); Assert.AreEqual(LoadNotice.StartedFresh, s2.Notice); Assert.AreEqual(0, (int)got.Guided);
            Assert.IsTrue(File.Exists(P("save.corrupt.json")));
            StringAssert.Contains("main 깨짐", File.ReadAllText(P("save.corrupt.json")), "처음 본 손상 파일(본)을 남긴다");
            // 새로 시작한 뒤 저장하면 다음 시작은 정상
            Write(s2, 1);
            var s3 = Store(new FileStorage(dir)); s3.Load(); Assert.AreEqual("main", s3.LoadedFrom); Assert.AreEqual(LoadNotice.None, s3.Notice);
        }

        [Test]
        public void MissingMainWithBackupCountsAsRestore()
        {
            var fs = new FileStorage(dir); var s = Store(fs); Write(s, 5); Write(s, 6);
            File.Delete(P("save.json"));
            var s2 = Store(new FileStorage(dir)); var got = s2.Load();
            Assert.AreEqual("backup", s2.LoadedFrom); Assert.AreEqual(LoadNotice.RestoredBackup, s2.Notice); Assert.AreEqual(5, (int)got.Guided);
        }

        [Test]
        public void FreshInstallHasNoNotice()
        {
            var s = Store(new FileStorage(dir)); s.Load();
            Assert.AreEqual("new", s.LoadedFrom); Assert.AreEqual(LoadNotice.None, s.Notice);
        }

        [Test]
        public void CorruptMainIsNeverCopiedOverTheGoodBackup()
        {
            var fs = new FileStorage(dir); var s = Store(fs); Write(s, 5); Write(s, 6);
            File.WriteAllText(P("save.json"), "{ 깨진");
            var s2 = Store(fs); var st = s2.Load();           // backup(5)
            st.Guided = 7; s2.Save(st);                        // 본이 깨져 있으면 백업(5)을 덮지 않는다
            Assert.AreEqual(5, (int)((JObject)Js.Parse(File.ReadAllText(P("save.bak.json"))))["guided"]);
            Assert.AreEqual(7, (int)Store(fs).Load().Guided);
        }

        // ---------- totals:null (HTML 과 같게 불러오기 포기) ----------
        [Test]
        public void TotalsNullGivesUpLoadingLikeHtmlAndFallsBackToBackup()
        {
            var mig = M1Fixture.Migrator();
            var bad = mig.Defaults(); bad["totals"] = JValue.CreateNull();
            Assert.Throws<InvalidDataException>(() => mig.Migrate(bad), "원본 1608행의 TypeError 에 해당");
            var fs = new FileStorage(dir); var s = Store(fs); Write(s, 5); Write(s, 6);
            var tampered = (JObject)Js.Parse(File.ReadAllText(P("save.json"))); tampered["totals"] = JValue.CreateNull();
            File.WriteAllText(P("save.json"), Js.Stringify(tampered));
            var s2 = Store(new FileStorage(dir)); var got = s2.Load();
            Assert.AreEqual("backup", s2.LoadedFrom); Assert.AreEqual(5, (int)got.Guided);
            File.WriteAllText(P("save.bak.json"), Js.Stringify(tampered));
            var s3 = Store(new FileStorage(dir)); s3.Load(); Assert.AreEqual("new", s3.LoadedFrom, "백업도 같으면 원본처럼 새로 시작");
        }

        [Test]
        public void TotalsOfOtherWrongShapesStillGetDefaults()
        {
            var mig = M1Fixture.Migrator();
            foreach (var t in new JToken[] { new JValue("x"), new JValue(5), new JArray(), new JObject(), new JObject { ["gifts"] = JValue.CreateNull() } })
            {
                var d = mig.Defaults(); d["totals"] = t;
                var m = mig.Migrate(d); Assert.IsTrue(m["totals"] is JObject tt && tt["gifts"] is JObject, "totals=" + t);
            }
        }

        // ---------- 진짜 프로세스 강제 종료 ----------
        [Test, Category("Kill")]
        public void KillWhileWritingNeverLosesMain()
        {
            string exe = FindKillWriter();
            if (exe == null) Assert.Ignore("KillWriter 가 빌드돼 있지 않다(Tools/m7dotnet/KillWriter: dotnet build -c Release)");
            Directory.CreateDirectory(dir); var rnd = new Random(20261112); long lastSeenCounter = 0; int kills = 0, mainOk = 0, backupCases = 0;
            int rounds = int.TryParse(Environment.GetEnvironmentVariable("LH_KILL_ROUNDS"), out int rr) ? rr : 25; var notes = new List<string>();
            for (int round = 0; round < rounds; round++)
            {
                var psi = new ProcessStartInfo("dotnet", "\"" + exe + "\" \"" + dir + "\" \"" + M1Fixture.DataDir + "\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                long last = 0;
                using (var p = Process.Start(psi))
                {
                    var swait = Stopwatch.StartNew(); int target = 5 + rnd.Next(60);       // 쓰기 몇 번 뒤(무작위)에 죽인다
                    int seen = 0;
                    while (seen < target && swait.ElapsedMilliseconds < 20000)
                    {
                        string line = p.StandardOutput.ReadLine(); if (line == null) break;
                        if (line.StartsWith("w ")) { seen++; last = long.Parse(line.Substring(2)); }
                    }
                    try { p.Kill(); } catch (Exception) { }
                    p.WaitForExit(); kills++;
                }
                string desc = Describe();
                var s = Store(new FileStorage(dir)); var got = s.Load();
                // 어떤 순간에 죽어도 "새로 시작"(데이터 잃음)은 없어야 한다. 본 파일이 그대로면 최선, 교체 도중 끊겨 본 파일이 비어 있으면(윈도우 ReplaceFile 의 창) 백업(직전 본)으로 이어진다.
                Assert.AreNotEqual("new", s.LoadedFrom, "round " + round + ": 강제 종료 뒤 저장을 잃었다 — 폴더: " + desc);
                if (s.LoadedFrom == "main") { mainOk++; Assert.AreEqual(LoadNotice.None, s.Notice, "round " + round); Assert.GreaterOrEqual((long)got.Guided, last, "round " + round + ": 끝났다고 알린 쓰기까지는 남아 있다"); }
                else { backupCases++; notes.Add("round " + round + " 백업 복원: " + desc); Assert.AreEqual(LoadNotice.RestoredBackup, s.Notice); Assert.GreaterOrEqual((long)got.Guided, last - 1, "round " + round + ": 백업은 직전 본(최대 한 번 뒤처짐)"); }
                Assert.GreaterOrEqual((long)got.Guided, lastSeenCounter, "round " + round + ": 되감기 없음");
                lastSeenCounter = (long)got.Guided;
            }
            TestContext.Out.WriteLine("강제 종료 " + kills + "번: 본 파일 정상 " + mainOk + "번, 백업으로 복원 " + backupCases + "번, 새로 시작 0번, 마지막 guided " + lastSeenCounter);
            foreach (var n in notes) TestContext.Out.WriteLine(n);
        }

        string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in new DirectoryInfo(dir).GetFiles())
            {
                string head = ""; try { string t = File.ReadAllText(f.FullName); head = t.Length > 40 ? "[" + t.Substring(0, 20).Replace("\n", " ") + "…" + t.Substring(t.Length - 20).Replace("\n", " ") + "]" : "[" + t + "]"; } catch (Exception e) { head = "읽기 실패 " + e.Message; }
                sb.Append(f.Name + ":" + f.Length + head + " ");
            }
            return sb.ToString();
        }

        static string FindKillWriter()
        {
            string root = M1Fixture.Root;
            foreach (var cfg in new[] { "Release", "Debug" })
            {
                string p = Path.Combine(root, "Tools", "m7dotnet", "KillWriter", "bin", cfg, "net9.0", "KillWriter.dll");
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}
