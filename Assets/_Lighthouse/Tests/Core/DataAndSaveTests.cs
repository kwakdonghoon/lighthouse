using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Data;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>G-DATA: 표 개수(18/9/24/8/…)가 추출 매니페스트·부록 A 와 같은지. G-SAVE: 마이그레이션·가져오기·LHS1 이 JS 와 같은지.</summary>
    public class DataAndSaveTests
    {
        [Test]
        public void TableCountsMatchManifestAndAppendixA()
        {
            var T = M1Fixture.Tables; var man = M1Fixture.Golden("data_manifest.json");
            Assert.That(man["mismatch"], Is.Empty, "추출기 기대 개수 불일치");
            var t = (JObject)man["tables"];
            Assert.AreEqual((int)t["GIFTS"], T.Gifts.Count); Assert.AreEqual(18, T.Gifts.Count);
            Assert.AreEqual(9, T.Ships.Count); Assert.AreEqual(24, T.Sea.Count); Assert.AreEqual(8, T.Regulars.Count);
            Assert.AreEqual(6, T.Regulars[0].Story.Length); Assert.IsTrue(T.Regulars.All(r => r.Story.Length == 6));
            Assert.AreEqual(28, T.DecorSlots.Count); Assert.AreEqual(8, T.DecorSlots.Count(s => s.Blocked)); Assert.AreEqual(20, T.DecorSlots.Count(s => !s.Blocked));
            Assert.AreEqual(10, T.DecorItems.Count); Assert.AreEqual(33, T.SkinLayers.Sum(l => l.SkinIds.Count)); Assert.AreEqual(7, T.Products.Count); Assert.AreEqual(3, T.AdKinds.Count);
            Assert.AreEqual(3, T.Tracks.Count); Assert.AreEqual(10, T.Ranks.Count); Assert.AreEqual((int)t["MILESTONES"], T.Milestones.Count); Assert.AreEqual((int)t["DIARY"], T.Diary.Count);
            Assert.AreEqual(5, T.DecorZones.Count); Assert.AreEqual(4, T.SeasonGuests.Count); Assert.AreEqual(4, T.Anniv.Count); Assert.AreEqual(3, T.NatureLetters.Count);
            Assert.AreEqual(12, T.Lint.Branches.Count); Assert.AreEqual((int)t["ICONS"], T.Lint.Icons.Count);
            Assert.AreEqual(10, T.SaveVer); Assert.AreEqual(6, T.MaxStage);
        }

        [Test]
        public void SaveDefaultsHaveExpectedShape()
        {
            var T = M1Fixture.Tables; var d = M1Fixture.Migrator().Defaults();
            Assert.AreEqual(10, (int)d["ver"]); Assert.IsTrue(d["build"].Type == JTokenType.Null);
            var st = GameState.FromJson(d);
            Assert.IsNull(M1Fixture.Diff(st.ToJson(), d), "기본 저장 왕복 불일치");
        }

        [Test]
        public void MigrateMatchesJsGolden()
        {
            var g = M1Fixture.Golden("save_migrate.json"); var mig = M1Fixture.Migrator(); var fails = new List<string>();
            foreach (JObject c in (JArray)g["cases"])
            {
                JObject actual;
                try { actual = mig.Migrate(Js.Parse((string)c["inputText"])); }
                catch (Exception e) { fails.Add(c["name"] + ": 예외 " + e.Message); continue; }
                string diff = M1Fixture.Diff(actual, c["expected"]);
                if (diff != null) fails.Add(c["name"] + " → " + diff);
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
            Assert.GreaterOrEqual(((JArray)g["cases"]).Count, 40);
        }

        [Test]
        public void TypedStateRoundTripKeepsMigratedSaves()
        {
            var g = M1Fixture.Golden("save_migrate.json"); var fails = new List<string>();
            foreach (JObject c in (JArray)g["cases"])
            {
                var exp = (JObject)c["expected"]; if (exp == null || exp["__error"] != null) continue;
                // 값이 이상한 형(문자열 카운트 등)을 일부러 넣은 변조 사례는 타입화 때 다듬어지므로 정상 형만 확인한다
                string name = (string)c["name"]; if (name.StartsWith("t_types") || name == "t_proto_keys" || name == "t_extra_fields" || name == "t_letter_ids") continue;
                string diff = M1Fixture.Diff(GameState.FromJson(exp).ToJson(), exp);
                if (diff != null) fails.Add(name + " → " + diff);
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        [Test]
        public void SanitizeAndParseMatchJsGolden()
        {
            var g = M1Fixture.Golden("save_import.json"); var T = M1Fixture.Tables; var mig = M1Fixture.Migrator(); var sc = new SaveCode(T, mig); var fails = new List<string>();
            foreach (JObject c in (JArray)g["cases"])
            {
                string name = (string)c["name"];
                if (c["sanitized"].Type != JTokenType.Null)
                {
                    string d1 = M1Fixture.Diff(sc.Sanitize((JObject)Js.Parse((string)c["inputText"])), c["sanitized"]);
                    if (d1 != null) fails.Add(name + " sanitize → " + d1);
                }
                var parsed = (JObject)c["parsed"]; var r = sc.Parse((string)c["code"]);
                if (parsed["err"] != null) { if (r.Err != (string)parsed["err"]) fails.Add(name + " err: " + r.Err + " != " + parsed["err"]); }
                else if (parsed["data"] != null) { if (r.Err != null) fails.Add(name + " 예기치 않은 err: " + r.Err); else { string d2 = M1Fixture.Diff(r.Data, parsed["data"]); if (d2 != null) fails.Add(name + " parse → " + d2); } }
            }
            foreach (JObject c in (JArray)g["raw"])
            {
                var parsed = (JObject)c["parsed"]; var r = sc.Parse(c["code"].Type == JTokenType.Null ? null : (string)c["code"]);
                if (parsed["err"] != null) { if (r.Err != (string)parsed["err"]) fails.Add(c["name"] + " err: " + r.Err + " != " + parsed["err"]); }
                else { if (r.Err != null) fails.Add(c["name"] + " 예기치 않은 err: " + r.Err); else { string d2 = M1Fixture.Diff(r.Data, parsed["data"]); if (d2 != null) fails.Add(c["name"] + " parse → " + d2); } }
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
            Assert.GreaterOrEqual(((JArray)g["cases"]).Count, 30);
        }

        [Test]
        public void Lhs1ExportMatchesJsAndRoundTrips()
        {
            var g = M1Fixture.Golden("save_import.json"); var T = M1Fixture.Tables; var sc = new SaveCode(T, M1Fixture.Migrator());
            foreach (JObject e in (JArray)g["export"])
            {
                var rest = (JObject)Js.Parse((string)e["json"]);
                Assert.AreEqual((string)e["json"], Js.Stringify(rest), "JSON.stringify 서식 불일치");
                Assert.AreEqual((string)e["code"], sc.Export(rest));
                var withPaid = (JObject)rest.DeepClone(); withPaid["owned"] = new JObject { ["noads"] = 1 }; withPaid["ads"] = new JObject { ["day"] = "2026-11-11" };
                Assert.AreEqual((string)e["code"], sc.Export(withPaid), "owned·ads 는 코드에 담지 않는다");
                var back = sc.Parse(sc.Export(rest));
                Assert.IsNull(back.Err, back.Err);
            }
            foreach (JObject h in (JArray)g["hashes"]) Assert.AreEqual((string)h["hash"], Js.HashStr((string)h["text"]), "hashStr " + h["text"].ToString().Length);
        }

        [Test]
        public void ImportKeepsDeviceOwnedAndAds()
        {
            // 가져오기는 owned·ads 를 담지 않는다 → 가져온 저장에는 기본값, 호출한 쪽이 이 기기 것을 되살린다(원본 importAct)
            var T = M1Fixture.Tables; var mig = M1Fixture.Migrator(); var sc = new SaveCode(T, mig);
            var s = mig.Defaults(); s["stage"] = 3; s["owned"] = new JObject { ["noads"] = 1 };
            var back = sc.Parse(sc.Export(s)).Data;
            Assert.AreEqual(0, ((JObject)back["owned"]).Count);
            Assert.AreEqual(3, (int)back["stage"]);
        }
    }
}
