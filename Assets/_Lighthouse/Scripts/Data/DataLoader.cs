using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Data
{
    /// <summary>표 JSON 을 읽어 오는 곳(에디터·플레이어는 Resources, 시험은 폴더).</summary>
    public interface IDataSource
    {
        /// <summary>상대 경로(예: "gifts.json", "ko/letters.json")의 글. 없으면 예외.</summary>
        string ReadText(string relPath);
    }

    /// <summary>폴더에서 읽는다(시험·도구용).</summary>
    public sealed class FolderDataSource : IDataSource
    {
        readonly string dir;
        public FolderDataSource(string dir) { this.dir = dir; }
        public string ReadText(string relPath) => File.ReadAllText(Path.Combine(dir, relPath.Replace('/', Path.DirectorySeparatorChar)), new System.Text.UTF8Encoding(false));
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>Resources/Data/ 아래 TextAsset 에서 읽는다.</summary>
    public sealed class ResourcesDataSource : IDataSource
    {
        public string ReadText(string relPath)
        {
            string name = "Data/" + (relPath.EndsWith(".json") ? relPath.Substring(0, relPath.Length - 5) : relPath);
            var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>(name);
            if (ta == null) throw new FileNotFoundException("Resources/" + name + ".json 없음");
            return ta.text;
        }
    }
#endif

    /// <summary>JSON 표 → GameTables. 리플렉션 없이 손으로 옮긴다(IL2CPP 코드 깎기에 안전).</summary>
    public static class DataLoader
    {
        public static GameTables Load(IDataSource src)
        {
            var T = new GameTables();
            JObject Read(string p) => (JObject)Js.Parse(src.ReadText(p));

            // ----- 선물 -----
            var g = Read("gifts.json");
            foreach (JObject o in (JArray)g["gifts"])
            {
                var d = new GiftDef { Id = S(o["id"]), Name = S(o["name"]), Season = S(o["season"]), Cond = S(o["cond"]), Tier = o["tier"] != null ? (int)o["tier"] : 0 };
                if (o["w"] is JObject w) { d.HasW = true; d.WFish = D(w["fish"]); d.WTrade = D(w["trade"]); }
                T.Gifts.Add(d); T.GiftById[d.Id] = d;
            }
            foreach (var p in ((JObject)g["affinity"]).Properties()) T.Affinity[p.Name] = (string)p.Value;
            T.AffinityDefault = S(g["affinityDefault"]);

            // ----- 배 -----
            var sh = Read("ships.json");
            foreach (JObject o in (JArray)sh["ships"]) { var d = new ShipDef { Id = S(o["id"]), Name = S(o["name"]), Stage = (int)o["stage"], St = (int)o["st"] }; T.Ships.Add(d); T.ShipById[d.Id] = d; }
            foreach (var m in (JArray)sh["mix"])
            {
                var l = new List<ShipMixEntry>();
                if (m is JArray ma) foreach (JObject e in ma) l.Add(new ShipMixEntry { Id = S(e["id"]), W = D(e["w"]) });
                T.ShipMix.Add(l);
            }
            foreach (var b in (JArray)sh["big"]) T.BigShips.Add((string)b);
            foreach (var p in ((JObject)sh["giftKind"]).Properties()) T.GiftKind[p.Name] = (string)p.Value;

            // ----- 바다 친구·자연 도감 -----
            var sea = Read("sea.json");
            foreach (JObject o in (JArray)sea["sea"])
            {
                var d = new SeaDef { Id = S(o["id"]), Grp = S(o["grp"]) ?? "sea", Name = S(o["name"]), Icon = S(o["icon"]), Where = S(o["where"]), Hint = S(o["hint"]), Tag = S(o["tag"]), Rare = o["rare"] != null && Js.Truthy(o["rare"]), Count = o["count"] != null ? (int)o["count"] : 0 };
                T.Sea.Add(d); T.SeaById[d.Id] = d;
            }
            foreach (JObject o in (JArray)sea["groups"]) T.SeaGroups.Add(new SeaGroup { Id = S(o["id"]), Name = S(o["name"]) });
            foreach (var p in ((JObject)sea["stampHalf"]).Properties()) T.StampHalf[p.Name] = (bool)p.Value;
            foreach (JObject o in (JArray)sea["natureLetters"]) T.NatureLetters.Add(new NatureLetterDef { Key = S(o["key"]), Need = S(o["need"]), Nm = S(o["nm"]), Text = S(o["text"]), Sign = S(o["sign"]) });

            // ----- 진행 -----
            var pr = Read("progression.json");
            T.SaveVer = (int)pr["saveVer"]; T.MaxStage = (int)pr["maxStage"];
            T.IsleNs = Ints(pr["isleNs"]); T.TierOf = Ints(pr["tierOf"]); T.Goal = Ints(pr["goal"]); T.DexNeed = Ints(pr["dexNeed"]); T.BuildSec = Ints(pr["buildSec"]);
            T.StageName = Strs(pr["stageName"]); T.StageRoles = Strs(pr["stageRoles"]);
            foreach (JObject o in (JArray)pr["tracks"]) { var d = new TrackDef { Id = S(o["id"]), Name = S(o["name"]), Desc = S(o["desc"]) }; T.Tracks.Add(d); T.TrackById[d.Id] = d; }
            T.LvBase = ((JArray)pr["lvBase"]).Select(x => (double)x).ToArray();
            foreach (var p in ((JObject)pr["lvCap"]).Properties()) T.LvCap[p.Name] = (int)p.Value;
            foreach (JObject o in (JArray)pr["ranks"]) T.Ranks.Add(new RankDef { Min = (int)o["min"], Name = S(o["name"]) });
            foreach (JObject o in (JArray)pr["milestones"])
            {
                var d = new MilestoneDef { K = S(o["k"]), Lv = (int)o["lv"], Text = S(o["text"]) };
                if (o["skin"] is JArray sk) { d.SkinLayer = (string)sk[0]; d.SkinId = (string)sk[1]; }
                T.Milestones.Add(d);
            }
            foreach (JObject o in (JArray)pr["beamColors"]) T.BeamColors.Add(new BeamColorDef { Id = S(o["id"]), Name = S(o["name"]), Src = S(o["src"]), Rgb = S(o["rgb"]), Lens = o["lens"] != null ? (int)o["lens"] : 0 });
            T.SaveDefaultsJson = (JObject)pr["saveDefaults"];

            // ----- 밸런스 -----
            LoadBalance(T.Bal, Read("balance.json"));

            // ----- 글 -----
            var lt = Read("ko/letters.json"); var X = T.Texts;
            X.Open = Strs(lt["open"]); X.Day = Strs(lt["day"]); X.Night = Strs(lt["night"]); X.Mid = Strs(lt["mid"]); X.Thanks = Strs(lt["thanks"]); X.Gift = Strs(lt["gift"]); X.Close = Strs(lt["close"]);
            X.Captains = Strs(lt["captains"]); X.ShipNames = Strs(lt["shipNames"]);
            foreach (var p in ((JObject)lt["seasonLetter"]).Properties()) { X.SeasonLetterDay[p.Name] = Strs(p.Value["day"]); X.SeasonLetterNight[p.Name] = Strs(p.Value["night"]); }
            foreach (var p in ((JObject)lt["seasonDiary"]).Properties()) { X.SeasonDiaryDay[p.Name] = Strs(p.Value["day"]); X.SeasonDiaryNight[p.Name] = Strs(p.Value["night"]); }
            foreach (var p in ((JObject)lt["seaOpen"]).Properties()) X.SeaOpen[p.Name] = (string)p.Value;

            var di = Read("ko/diary.json");
            foreach (JObject o in (JArray)di["entries"])
                T.Diary.Add(new DiaryDef { W = D(o["w"]), Day = B(o["day"]), Night = B(o["night"]), Any = B(o["any"]), Gift = B(o["gift"]), Shiny = B(o["shiny"]), Season = B(o["season"]), NoWinter = B(o["noWinter"]), Stage = o["stage"] != null ? (int)o["stage"] : 0, Nv = S(o["nv"]), Text = S(o["text"]) });

            var rg = Read("ko/regulars.json");
            foreach (JObject o in (JArray)rg["regulars"]) { var d = new RegularDef { Id = S(o["id"]), Ship = S(o["ship"]), Cap = S(o["cap"]), Type = S(o["type"]), Gift = S(o["gift"]), Region = S(o["region"]), Stage = (int)o["stage"], Story = Strs(o["story"]), Sp = o["sp"] != null ? D(o["sp"]) : 1, Lines = o["lines"] != null ? Strs(o["lines"]) : new string[0] }; T.Regulars.Add(d); T.RegularById[d.Id] = d; }
            foreach (JObject o in (JArray)rg["seasonGuests"]) T.SeasonGuests.Add(new SeasonGuestDef { Season = S(o["season"]), Ship = S(o["ship"]), Cap = S(o["cap"]), Text = S(o["text"]) });
            foreach (JObject o in (JArray)rg["annivLetters"]) T.Anniv.Add(new AnnivDef { Day = (int)o["day"], Gift = S(o["gift"]), Ship = S(o["ship"]), Cap = S(o["cap"]), Text = S(o["text"]) });
            foreach (JObject o in (JArray)rg["finaleVoices"]) T.FinaleVoices.Add(new KeyValuePair<string, string>(S(o["id"]), S(o["text"])));
            var fl = (JObject)rg["finale"]; T.Finale = new FinaleDef { Nm = S(fl["nm"]), Sign = S(fl["sign"]), Head = S(fl["head"]), None = S(fl["none"]), SomeSuffix = S(fl["someSuffix"]), SomeUntil = (int)fl["someUntil"], Tail = S(fl["tail"]) };

            // ----- 외형·상품·광고·바다 -----
            var sk2 = Read("skins.json");
            foreach (JObject o in (JArray)sk2["layers"])
            {
                var l = new SkinLayerDef { Id = S(o["id"]) };
                foreach (JObject s in (JArray)o["skins"])
                {
                    var sd = new SkinDef { Id = S(s["id"]), Name = S(s["name"]), Src = S(s["src"]) };
                    if (s["need"] is JArray nd && nd.Count >= 2) { sd.HasNeed = true; sd.NeedKey = (string)nd[0]; sd.NeedLv = Js.Num(nd[1]); }
                    l.SkinIds.Add(sd.Id); l.SkinSet.Add(sd.Id); l.Skins[sd.Id] = sd;
                }
                T.SkinLayers.Add(l); T.SkinLayerById[l.Id] = l;
            }
            foreach (JObject o in (JArray)sk2["products"]) T.Products.Add(new ProductDef { Id = S(o["id"]), Name = S(o["name"]), Desc = S(o["desc"]), Price = (int)o["price"], Grants = Strs(o["grants"]) });
            foreach (var p in ((JObject)sk2["itemNames"]).Properties()) T.ItemNames[p.Name] = (string)p.Value;
            T.PriceStep = (int)sk2["priceStep"];
            foreach (JObject o in (JArray)sk2["adKinds"]) T.AdKinds.Add(new AdKindDef { Id = S(o["id"]), Name = S(o["name"]), Desc = S(o["desc"]), Icon = S(o["icon"]), Limit = (int)o["limit"] });
            foreach (JObject o in (JArray)sk2["regions"]) { var d = new RegionDef { Id = S(o["id"]), Name = S(o["name"]), Src = S(o["src"]) }; T.Regions.Add(d); T.RegionById[d.Id] = d; }

            // ----- 꾸미기 -----
            var dc = Read("decor.json");
            foreach (var p in ((JObject)dc["sizeRank"]).Properties()) T.SizeRank[p.Name] = (int)p.Value;
            foreach (JObject o in (JArray)dc["zones"]) T.DecorZones.Add(new DecorZoneDef { Id = S(o["id"]), Chip = S(o["chip"]), Title = S(o["title"]) });
            foreach (var p in ((JObject)dc["letters"]).Properties()) T.DecorLetters[p.Name] = (string)p.Value;
            foreach (var p in ((JObject)dc["season"]).Properties()) { var m = new Dictionary<string, string[]>(); foreach (var q in ((JObject)p.Value).Properties()) m[q.Name] = Strs(q.Value); T.DecorSeason[p.Name] = m; }
            foreach (JObject o in (JArray)dc["slots"])
            {
                var op = (JObject)o["open"];
                var d = new DecorSlotDef { Id = S(o["id"]), Chip = S(o["chip"]), Size = S(o["size"]), Blocked = o["blocked"] != null && (int)o["blocked"] != 0, OpenBase = op["base"] != null ? (int)op["base"] : 0, OpenStage = op["stage"] != null ? (int)op["stage"] : 0, OpenLv = op["lv"] != null ? (int)op["lv"] : 0, MinStage = o["minStage"] != null ? (int)o["minStage"] : 1 };
                if (o["ab"] is JArray ab) d.Ab = ab.Select(x => (double)x).ToArray();
                if (o["abLin"] is JArray al) d.AbLin = al.Select(x => new LinComp { K = D(x["k"]), C = D(x["c"]) }).ToArray();
                if (o["xyFn"] is JArray xf) d.XyFn = xf.Select(x => new LinComp { K = D(x["k"]), C = D(x["c"]), Round = B(x["round"]), Neg = B(x["neg"]) }).ToArray();
                d.Num = int.Parse(new string(d.Id.Where(char.IsDigit).ToArray()));
                T.DecorSlots.Add(d); T.DecorSlotById[d.Id] = d;
            }
            foreach (JObject o in (JArray)dc["items"])
            {
                var d = new DecorItemDef { Id = S(o["id"]), Phase = S(o["phase"]), Name = S(o["name"]), Size = S(o["size"]), Src = S(o["src"]), Who = S(o["who"]), Zones = Strs(o["zones"]), Ax = (int)o["ax"], Ay = (int)o["ay"], Ss = B(o["ss"]), Sk = B(o["sk"]), Rows = Strs(o["rows"]) };
                if (o["need"] is JArray nd)
                {
                    d.NeedStr = nd.Select(x => x.Type == JTokenType.String ? (string)x : null).ToArray();
                    d.NeedNum = nd.Select(x => x.Type == JTokenType.String ? double.NaN : Js.Num(x)).ToArray();
                }
                d.Draft = B(o["draft"]);
                if (o["alt"] is JArray alt) { d.AltStr = alt.Select(x => x.Type == JTokenType.String ? (string)x : null).ToArray(); d.AltNum = alt.Select(x => x.Type == JTokenType.String ? double.NaN : Js.Num(x)).ToArray(); }
                T.DecorItems.Add(d); T.DecorItemById[d.Id] = d;
            }

            // ----- 글 검사 데이터 -----
            var lint = Read("lint/forbid_ko.json"); var L = T.Lint;
            foreach (JObject o in (JArray)lint["branches"]) L.Branches.Add(new KeyValuePair<string, string>(S(o["name"]), S(o["pattern"])));
            L.DayBan = S(lint["dayBan"]); L.DiaryBan = S(lint["diaryBan"]); L.JosaLeft = S(lint["josaLeft"]); L.NatureTextBan = S(lint["natureTextBan"]);
            var art = Read("art.json");
            foreach (var p in ((JObject)art["ICONS"]).Properties())
            {
                var ic = new IconDef { Rows = Strs(p.Value["rows"]) };
                foreach (var q in ((JObject)p.Value["pal"]).Properties()) ic.PalKeys.Add(q.Name);
                L.Icons[p.Name] = ic;
            }
            foreach (var p in ((JObject)art["SEASON_NAME"]).Properties()) L.SeasonName[p.Name] = (string)p.Value;
            if (art["DECO_MSG"] is JObject dm) foreach (var p in dm.Properties()) if (p.Value.Type == JTokenType.String) L.DecoMsg[p.Name] = (string)p.Value;
            return T;
        }

        static void LoadBalance(Balance b, JObject j)
        {
            var co = (JObject)j["condOdds"]; b.CondNight = D(co["night"]); b.CondDawn = D(co["dawn"]); b.CondRain = D(co["rain"]); b.CondFog = D(co["fog"]);
            foreach (var p in ((JObject)j["condName"]).Properties()) b.CondName[p.Name] = (string)p.Value;
            foreach (var p in ((JObject)j["condShort"]).Properties()) b.CondShort[p.Name] = (string)p.Value;
            b.WxBoost = D(j["wxBoost"]); b.LetterMax = (int)j["letterMax"]; b.LetterChance = D(j["letterChance"]);
            var ll = (JObject)j["letterLim"]; b.LetterLimText = (int)ll["text"]; b.LetterLimName = (int)ll["name"]; b.LetterLimSign = (int)ll["sign"]; b.LetterLimCount = (int)ll["count"];
            b.KeepKinds = Strs(j["keepKinds"]); b.OfflineRate = D(j["offlineRate"]); b.OfflineRegPerHour = D(j["offlineRegPerHour"]); b.MaxT = D(j["maxT"]);
            b.VisitAt = Ints(j["visitAt"]); b.GiftAt = (int)j["giftAt"];
            b.OfflineMaxBaseHours = D(j["offlineMaxBaseHours"]); b.OfflineMaxPortHoursCap = D(j["offlineMaxPortHoursCap"]); b.OfflineMinSec = D(j["offlineMinSec"]); b.OfflineDiaryMinSec = D(j["offlineDiaryMinSec"]);
            b.OfflineHomeMinSec = D(j["offlineHomeMinSec"]); b.OfflineFirstBackMinSec = D(j["offlineFirstBackMinSec"]); b.OfflineQuietSec = D(j["offlineQuietSec"]); b.OfflineRegLongSec = D(j["offlineRegLongSec"]); b.OfflineRegMaxP = D(j["offlineRegMaxP"]);
            b.OfflineShipEps = D(j["offlineShipEps"]); b.DiaryMax = D(j["diaryMax"]); b.DiaryPerHour = D(j["diaryPerHour"]);
            b.ShinyBase = D(j["shinyBase"]); b.ShinyPerLv = D(j["shinyPerLv"]); b.ShinyMax = D(j["shinyMax"]); b.LensShinyPerLv = D(j["lensShinyPerLv"]); b.LensShinyLvCap = D(j["lensShinyLvCap"]);
            b.HarvestPerLv = D(j["harvestPerLv"]); b.HarvestMax = D(j["harvestMax"]); b.RegularBonusPerLv = D(j["regularBonusPerLv"]); b.RegularBonusMax = D(j["regularBonusMax"]); b.RegularBase = D(j["regularBase"]);
            b.LensMulPerLv = D(j["lensMulPerLv"]); b.LensMulBeyondPerLv = D(j["lensMulBeyondPerLv"]); b.LensMulMax = D(j["lensMulMax"]);
            b.CondOddsFogTier2 = D(j["condOddsFogTier2"]); b.GiftSeasonWeight = D(j["giftSeasonWeight"]);
            b.GiftPtsSeason = D(j["giftPtsSeason"]); b.GiftPtsCond = D(j["giftPtsCond"]); b.GiftPtsFit = D(j["giftPtsFit"]); b.GiftPtsShiny = D(j["giftPtsShiny"]); b.AutoOfferKeep = D(j["autoOfferKeep"]); b.AutoOfferShinyKeep = D(j["autoOfferShinyKeep"]);
            b.LvGrowthBase = D(j["lvGrowthBase"]); b.LvGrowthRatio = D(j["lvGrowthRatio"]); b.LvGrowthFromLv = D(j["lvGrowthFromLv"]);
            b.SeasonGuestMinGuided = D(j["seasonGuestMinGuided"]); b.AnnivDayMs = D(j["annivDayMs"]); b.FirstBackRegular = S(j["firstBackRegular"]);
            b.ItemValueNoAds = D(j["itemValueNoAds"]); b.ItemValueBeam = D(j["itemValueBeam"]); b.ItemValueSkin = D(j["itemValueSkin"]);
            b.DecorVisMin = D(j["decorVisMin"]); b.DecorVisMinBack = D(j["decorVisMinBack"]); b.DecorSlotCount = (int)j["decorSlotCount"]; b.DecorOpenCount = (int)j["decorOpenCount"]; b.DecorBlockedCount = (int)j["decorBlockedCount"];
        }

        static string S(JToken t) => t == null || t.Type == JTokenType.Null ? null : (string)t;
        static double D(JToken t) => Js.Num(t);
        static bool B(JToken t) => t != null && Js.Truthy(t);
        static int[] Ints(JToken t) => ((JArray)t).Select(x => (int)Js.Num(x)).ToArray();
        static string[] Strs(JToken t) => ((JArray)t).Select(x => (string)x).ToArray();
    }
}
