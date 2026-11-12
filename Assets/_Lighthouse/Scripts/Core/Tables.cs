using System;
using System.Collections.Generic;

namespace Lighthouse.Core
{
    // 표(데이터) 정의. 값은 Resources/Data/*.json(추출기 Tools/export_data.mjs 가 동결 HTML 에서 뽑음)에서 Lighthouse.Data.DataLoader 가 채운다.
    // 순서가 의미인 표(선물·바다·배·단골·갈래)는 List 로 두고 id 조회용 사전을 따로 둔다 (부록 B-4).

    public sealed class GiftDef
    {
        public string Id, Name;
        public string Season, Cond;      // 없으면 null
        public int Tier;                 // 없으면 0
        public double WFish, WTrade;
        public bool HasW;
    }

    public sealed class ShipDef { public string Id, Name; public int Stage, St; }
    public sealed class ShipMixEntry { public string Id; public double W; }

    public sealed class SeaDef
    {
        public string Id, Grp = "sea", Name, Icon, Where, Hint, Tag;
        public bool Rare; public int Count;
    }
    public sealed class SeaGroup { public string Id, Name; }
    public sealed class NatureLetterDef { public string Key, Need, Nm, Text, Sign; }

    public sealed class TrackDef { public string Id, Name, Desc; }
    public sealed class MilestoneDef { public string K; public int Lv; public string Text, SkinLayer, SkinId; }
    public sealed class RankDef { public int Min; public string Name; }
    public sealed class BeamColorDef { public string Id, Name, Src, Rgb; public int Lens; }

    public sealed class DiaryDef
    {
        public double W; public bool Day, Night, Any, Gift, Shiny, Season, NoWinter;
        public int Stage;          // 0 이면 1 로 본다(원본 e.stage || 1)
        public string Nv, Text;
    }

    public sealed class RegularDef
    {
        public string Id, Ship, Cap, Type, Gift, Region;
        public int Stage; public string[] Story;
        public double Sp = 1; public string[] Lines = new string[0];       // M4: 배 속도 배율·말풍선 대사 (원본 r.sp, r.lines; 데이터에는 이미 있었다)
    }
    public sealed class SeasonGuestDef { public string Season, Ship, Cap, Text; }
    public sealed class AnnivDef { public int Day; public string Gift, Ship, Cap, Text; }   // Gift: null | "normal" | "shiny"
    public sealed class FinaleDef { public string Nm, Sign, Head, None, SomeSuffix, Tail; public int SomeUntil; }

    public sealed class SkinDef { public string Id, Name, Src, NeedKey; public double NeedLv; public bool HasNeed; }
    public sealed class SkinLayerDef
    {
        public string Id; public List<string> SkinIds = new List<string>(); public HashSet<string> SkinSet = new HashSet<string>();
        public Dictionary<string, SkinDef> Skins = new Dictionary<string, SkinDef>();
    }

    /// <summary>아이콘 도면(검사용): 격자 행과 팔레트 글자.</summary>
    public sealed class IconDef { public string[] Rows; public HashSet<string> PalKeys = new HashSet<string>(); }

    /// <summary>글 검사용 정규식 원문(lint/forbid_ko.json).</summary>
    public sealed class LintData
    {
        public List<KeyValuePair<string, string>> Branches = new List<KeyValuePair<string, string>>();
        public string DayBan, DiaryBan, JosaLeft, NatureTextBan;
        public Dictionary<string, IconDef> Icons = new Dictionary<string, IconDef>();
        public List<string> ExtraTexts = new List<string>();     // 화면 쪽(UI·TMP)이 모아 오는 문구. 빌드 파이프라인이 채운다
        public Dictionary<string, string> DecoMsg = new Dictionary<string, string>();
        public Dictionary<string, string> SeasonName = new Dictionary<string, string>();
    }
    public sealed class ProductDef { public string Id, Name, Desc; public int Price; public string[] Grants; }
    public sealed class AdKindDef { public string Id, Name, Desc, Icon; public int Limit; }
    public sealed class RegionDef { public string Id, Name, Src; }

    /// <summary>선형식 한 성분: value = k·N + c (섬 크기 N), 또는 하늘 자리의 반올림 식.</summary>
    public sealed class LinComp { public double K, C; public bool Round, Neg; }
    public sealed class DecorSlotDef
    {
        public string Id, Chip, Size;
        public int OpenBase, OpenStage, OpenLv;     // 0 이면 없음
        public int MinStage = 1;                    // G2': 정원 레벨로 여는 자리도 이 섬 단계 이상이어야 열린다(작은 섬에는 자리 좌표가 안 들어가서). 1 이면 조건 없음
        public bool Blocked;
        public double[] Ab;                          // 고정 좌표(없으면 null)
        public LinComp[] AbLin;                      // N 의 함수 좌표(없으면 null)
        public LinComp[] XyFn;                       // 하늘 자리 식(없으면 null)
        public int Num;                              // 자리 번호(P1 → 1)
        /// <summary>단계 N(섬 한 변의 절반)의 [a,b] 블록 좌표.</summary>
        public double[] AbAt(double n)
        {
            if (AbLin != null) return new[] { AbLin[0].K * n + AbLin[0].C, AbLin[1].K * n + AbLin[1].C };
            return Ab;
        }
        /// <summary>하늘 자리의 [x,y] (R = 섬 반지름).</summary>
        public double[] XyAt(double r)
        {
            if (XyFn == null) return null;
            double Comp(LinComp c)
            {
                double v = c.Round ? JsMath.Round(c.K * r) : c.K * r;
                return (c.Neg ? -v : v) + c.C;
            }
            return new[] { Comp(XyFn[0]), Comp(XyFn[1]) };
        }
    }

    public sealed class DecorItemDef
    {
        public string Id, Phase, Name, Size, Src, Who; public bool Draft; public string[] AltStr; public double[] AltNum;   // Draft: 시안 격자(D42). Alt: 두 번째 해금 경로(D44)
        public string[] Zones; public string[] NeedStr; public double[] NeedNum;   // need: ['garden',3] / ['swallow',3] / ['shell',20] / ['autumn']
        public int Ax, Ay; public bool Ss, Sk; public string[] Rows;
        public string NeedKey => NeedStr != null && NeedStr.Length > 0 ? NeedStr[0] : null;
        public double NeedVal => NeedNum != null && NeedNum.Length > 1 ? NeedNum[1] : 0;
    }

    public sealed class DecorZoneDef { public string Id, Chip, Title; }

    /// <summary>밸런스 상수(balance.json). 밸런스 변경은 이 JSON 만 고친다.</summary>
    public sealed class Balance
    {
        public double CondNight, CondDawn, CondRain, CondFog;
        public Dictionary<string, string> CondName = new Dictionary<string, string>(), CondShort = new Dictionary<string, string>();
        public double WxBoost;
        public int LetterMax; public double LetterChance;
        public int LetterLimText, LetterLimName, LetterLimSign, LetterLimCount;
        public string[] KeepKinds;
        public double OfflineRate, OfflineRegPerHour, MaxT;
        public int[] VisitAt; public int GiftAt;
        public double OfflineMaxBaseHours, OfflineMaxPortHoursCap, OfflineMinSec, OfflineDiaryMinSec, OfflineHomeMinSec, OfflineFirstBackMinSec, OfflineQuietSec, OfflineRegLongSec, OfflineRegMaxP;
        public double OfflineShipEps, DiaryMax, DiaryPerHour;
        public double ShinyBase, ShinyPerLv, ShinyMax, LensShinyPerLv, LensShinyLvCap;
        public double HarvestPerLv, HarvestMax, RegularBonusPerLv, RegularBonusMax, RegularBase;
        public double LensMulPerLv, LensMulBeyondPerLv, LensMulMax;
        public double CondOddsFogTier2, GiftSeasonWeight;
        public double GiftPtsSeason, GiftPtsCond, GiftPtsFit, GiftPtsShiny, AutoOfferKeep, AutoOfferShinyKeep;
        public double LvGrowthBase, LvGrowthRatio, LvGrowthFromLv;
        public double SeasonGuestMinGuided, AnnivDayMs; public string FirstBackRegular;
        public double ItemValueNoAds, ItemValueBeam, ItemValueSkin;
        public double DecorVisMin, DecorVisMinBack; public int DecorSlotCount, DecorOpenCount, DecorBlockedCount;
    }

    /// <summary>글(편지 조각 등).</summary>
    public sealed class LetterTexts
    {
        public string[] Open, Day, Night, Mid, Thanks, Gift, Close, Captains, ShipNames;
        public Dictionary<string, string[]> SeasonLetterDay = new Dictionary<string, string[]>(), SeasonLetterNight = new Dictionary<string, string[]>();
        public Dictionary<string, string[]> SeasonDiaryDay = new Dictionary<string, string[]>(), SeasonDiaryNight = new Dictionary<string, string[]>();
        public Dictionary<string, string> SeaOpen = new Dictionary<string, string>();
    }

    /// <summary>전체 표 묶음. 읽기 전용으로 취급한다.</summary>
    public sealed class GameTables
    {
        // 선물·배·바다
        public List<GiftDef> Gifts = new List<GiftDef>(); public Dictionary<string, GiftDef> GiftById = new Dictionary<string, GiftDef>();
        public Dictionary<string, string> Affinity = new Dictionary<string, string>(); public string AffinityDefault = "garden";
        public List<ShipDef> Ships = new List<ShipDef>(); public Dictionary<string, ShipDef> ShipById = new Dictionary<string, ShipDef>();
        public List<List<ShipMixEntry>> ShipMix = new List<List<ShipMixEntry>>();     // 인덱스 = 단계(0 은 빈 목록)
        public HashSet<string> BigShips = new HashSet<string>(); public Dictionary<string, string> GiftKind = new Dictionary<string, string>();
        public List<SeaDef> Sea = new List<SeaDef>(); public Dictionary<string, SeaDef> SeaById = new Dictionary<string, SeaDef>();
        public List<SeaGroup> SeaGroups = new List<SeaGroup>(); public Dictionary<string, bool> StampHalf = new Dictionary<string, bool>();
        public List<NatureLetterDef> NatureLetters = new List<NatureLetterDef>();

        // 진행
        public int SaveVer, MaxStage; public int[] IsleNs, TierOf, Goal, DexNeed, BuildSec; public string[] StageName, StageRoles;
        public List<TrackDef> Tracks = new List<TrackDef>(); public Dictionary<string, TrackDef> TrackById = new Dictionary<string, TrackDef>();
        public double[] LvBase; public Dictionary<string, int> LvCap = new Dictionary<string, int>();
        public List<RankDef> Ranks = new List<RankDef>(); public List<MilestoneDef> Milestones = new List<MilestoneDef>(); public List<BeamColorDef> BeamColors = new List<BeamColorDef>();
        public Balance Bal = new Balance();

        // 글
        public LetterTexts Texts = new LetterTexts();
        public List<DiaryDef> Diary = new List<DiaryDef>();
        public List<RegularDef> Regulars = new List<RegularDef>(); public Dictionary<string, RegularDef> RegularById = new Dictionary<string, RegularDef>();
        public List<SeasonGuestDef> SeasonGuests = new List<SeasonGuestDef>(); public List<AnnivDef> Anniv = new List<AnnivDef>();
        public List<KeyValuePair<string, string>> FinaleVoices = new List<KeyValuePair<string, string>>(); public FinaleDef Finale = new FinaleDef();

        // 외형·상품·광고·바다 구역
        public List<SkinLayerDef> SkinLayers = new List<SkinLayerDef>(); public Dictionary<string, SkinLayerDef> SkinLayerById = new Dictionary<string, SkinLayerDef>();
        public List<ProductDef> Products = new List<ProductDef>(); public Dictionary<string, string> ItemNames = new Dictionary<string, string>(); public int PriceStep = 10;
        public List<AdKindDef> AdKinds = new List<AdKindDef>(); public List<RegionDef> Regions = new List<RegionDef>(); public Dictionary<string, RegionDef> RegionById = new Dictionary<string, RegionDef>();

        // 꾸미기
        public Dictionary<string, int> SizeRank = new Dictionary<string, int>(); public List<DecorZoneDef> DecorZones = new List<DecorZoneDef>();
        public List<DecorSlotDef> DecorSlots = new List<DecorSlotDef>(); public Dictionary<string, DecorSlotDef> DecorSlotById = new Dictionary<string, DecorSlotDef>();
        public List<DecorItemDef> DecorItems = new List<DecorItemDef>(); public Dictionary<string, DecorItemDef> DecorItemById = new Dictionary<string, DecorItemDef>();
        public Dictionary<string, string> DecorLetters = new Dictionary<string, string>();
        public Dictionary<string, Dictionary<string, string[]>> DecorSeason = new Dictionary<string, Dictionary<string, string[]>>();     // 재질 → 계절 → [윗, 왼, 오른]

        public LintData Lint = new LintData();

        // 저장 기본값 원본(save 기본 JSON). SaveDefaults 가 이것을 복제해 쓴다
        public Newtonsoft.Json.Linq.JObject SaveDefaultsJson;

        // 보조 이름 목록
        public static readonly string[] SeasonIds = { "spring", "summer", "autumn", "winter" };
        public static readonly string[] ViewIds = { "near", "normal", "wide" };

        public bool IsStageNameKnown(int s) => s >= 1 && s <= MaxStage;
        public int TierAt(int stage) => stage >= 0 && stage < TierOf.Length ? TierOf[stage] : 0;
    }
}
