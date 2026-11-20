using System;
using System.Collections.Generic;
using System.Linq;

namespace Lighthouse.Render
{
    /// <summary>탑 외형(SKINS.tower) 한 가지: 기본 색 위에 덮어쓸 색(null 이면 덮어쓰지 않음)과 형태 (HTML 1631~1645행).</summary>
    public sealed class TowerSkin
    {
        public string Id, Form, Stripe, Wall, Roof, Trim, Door;
        public TowerSkin(string id, string form = null, string stripe = null, string wall = null, string roof = null, string trim = null, string door = null)
        { Id = id; Form = form; Stripe = stripe; Wall = wall; Roof = roof; Trim = trim; Door = door; }
    }

    /// <summary>색 표 모음: 계절 풀·나무, 섬·마을·탑·배 외형. 모두 HTML 표 그대로 (이 값이 바뀌면 골든이 잡는다).</summary>
    public static class Palettes
    {
        public static readonly string[] Seasons = { "spring", "summer", "autumn", "winter" };

        // SEASON_GRASS (800행)
        public static string[] Grass(string season)
        {
            switch (season)
            {
                case "spring": return new[] { "#2f6a3a", "#3c7d45", "#4f9650", "#66ae5e", "#82c474" };
                case "summer": return new[] { "#2a6236", "#336f3f", "#3d8049", "#4a9255", "#5ca963" };
                case "autumn": return new[] { "#5a5a2a", "#6e6a2c", "#847a32", "#9a8a3a", "#b09a48" };
                case "winter": return new[] { "#7a8c98", "#94a6b2", "#aebcc8", "#c8d4de", "#e4ecf4" };
            }
            throw new ArgumentException(season);
        }

        // TREE_COL (806행): [아래, 왼쪽, 오른쪽, 위, 반짝임]
        public static string[] Tree(string season)
        {
            switch (season)
            {
                case "spring": return new[] { "#e89ab4", "#f4b6c8", "#f4b6c8", "#fbd8e2", "#ffffff" };
                case "summer": return new[] { "#2d6738", "#3a7d46", "#3a7d46", "#4a9455", "#8be096" };
                case "autumn": return new[] { "#b8501e", "#d8742a", "#d8742a", "#e09a30", "#ffd070" };
                case "winter": return new[] { "#5a6a78", "#7a8c9c", "#7a8c9c", "#e4ecf4", "#ffffff" };
            }
            throw new ArgumentException(season);
        }

        /// <summary>섬 풍경 외형(SKINS.isle): 풀·나무 색을 계절과 상관없이 고정. null 이면 계절을 따른다.</summary>
        public static readonly Dictionary<string, string> IsleSkinSeason = new Dictionary<string, string>
        {
            { "classic", null }, { "blossom", "spring" }, { "maple", "autumn" }, { "dev", "autumn" }
        };

        // SKINS.decor 의 pal (1655~1658행). 기본(classic)은 DECOR_PAL (1922행).
        public static DecorPal Decor(string skin)
        {
            var d = new DecorPal();
            switch (skin)
            {
                case "classic": break;
                case "seaside":
                    d.Wall = "#e4efe9"; d.WallS = "#bfd3cc"; d.RoofL = "#2f9aa6"; d.RoofR = "#23737c"; d.RoofE = "#1b5a62";
                    d.Flowers = new[] { "#7fd0e0", "#ffffff", "#a8e0c0", "#f0e08a", "#9ab8f0" }; d.Fence = "#d8ece6"; d.FlagA = "#38b0bc"; d.FlagB = "#2f8e98"; break;
                case "festival":
                    d.Wall = "#f4ead0"; d.WallS = "#d8c9a0"; d.RoofL = "#e0b040"; d.RoofR = "#b88a24"; d.RoofE = "#8a6a1c";
                    d.Flowers = new[] { "#ff9ab0", "#ffe070", "#ffffff", "#ff9a70", "#f0a0e0" }; d.Fence = "#f4ead0"; d.FlagA = "#f6d25a"; d.FlagB = "#e0b040"; break;
                case "dev":
                    d.RoofL = "#e0b040"; d.RoofR = "#b88a24"; d.RoofE = "#8a6a1c"; d.FlagA = "#f6d25a"; d.FlagB = "#e0b040"; break;
                default: throw new ArgumentException(skin);
            }
            return d;
        }

        /// <summary>섬 굽기 입력을 계절·섬 외형·마을 외형으로 만든다 (HTML isleGrass/treeCol/lookPal 의 규칙).</summary>
        public static IslandEnv IslandEnvFor(string season, int stage, string isleSkin = "classic", string decorSkin = "classic", int gardenLv = 0, int treeLv = 0)
        {
            string fixedSeason = IsleSkinSeason.TryGetValue(isleSkin, out var fs) ? fs : null;
            string cs = fixedSeason ?? season;
            return new IslandEnv { Stage = stage, Grass = Grass(cs), Tree = Tree(cs), Winter = season == "winter", GardenLv = gardenLv, TreeLv = treeLv, Decor = Decor(decorSkin) };
        }

        public static readonly TowerSkin[] TowerSkins =
        {
            new TowerSkin("classic"),
            new TowerSkin("stonetower", form: "stone", roof: "#5a3a28", door: "#4a3220"),
            new TowerSkin("cream", form: "plaster", roof: "#b8472f", door: "#6a4328"),
            new TowerSkin("forest", stripe: "#2f6a3a", wall: "#ece5d0", roof: "#2f6a3a", door: "#5a3a28"),
            new TowerSkin("blossom", stripe: "#e8809c", wall: "#fbeff2", roof: "#d85a7c", trim: "#f4b6c8", door: "#6a4328"),
            new TowerSkin("maple", stripe: "#d8742a", wall: "#f0e4c8", roof: "#b8501e", trim: "#e09a30", door: "#5a3a28"),
            new TowerSkin("snow", stripe: "#8fb8e0", wall: "#f4f8ff", roof: "#7aa0cc", trim: "#d6e6f6", door: "#4a5a78"),
            new TowerSkin("bands", form: "bands", stripe: "#c9403a", wall: "#f4f0e0", roof: "#c9403a", door: "#5a3a28"),
            new TowerSkin("spire", form: "spire", roof: "#3d6aa0", trim: "#d9a93a", door: "#5a3a28"),
            new TowerSkin("moonlit", stripe: "#8a8fd0", wall: "#e8eaf8", roof: "#2f3f7a", trim: "#c8d0f0", door: "#3a3f6a"),
            new TowerSkin("widelamp", form: "wide", stripe: "#c9403a", roof: "#8a5a30", trim: "#d9a93a", door: "#5a3a28"),
            new TowerSkin("gilded", stripe: "#d9a93a", roof: "#e0b040", door: "#5a3a28"),
            new TowerSkin("sea", stripe: "#2f7f86", roof: "#2f7f86", door: "#5a3a28"),
            new TowerSkin("dawn", stripe: "#6a5a9a", roof: "#6a5a9a", door: "#5a3a28"),
            new TowerSkin("dev", stripe: "#2f7f86", roof: "#2f7f86", door: "#5a3a28"),
        };

        public static TowerSkin TowerSkinOf(string id) => TowerSkins.First(s => s.Id == id);

        /// <summary>SKINS.ship 의 pal (1659~1663행): 돛대 색 b·c 를 덮는다.</summary>
        public static Dictionary<char, string> ShipSkin(string id)
        {
            switch (id)
            {
                case "classic": return new Dictionary<char, string>();
                case "coral": return new Dictionary<char, string> { { 'b', "#a8503e" }, { 'c', "#7a3426" } };
                case "friends": return new Dictionary<char, string> { { 'b', "#2f7a5a" }, { 'c', "#1f523c" } };
                case "gilded": return new Dictionary<char, string> { { 'b', "#c8a040" }, { 'c', "#8a6c20" } };
                case "dev": return new Dictionary<char, string> { { 'b', "#3f5a7a" }, { 'c', "#2a3d58" } };
            }
            throw new ArgumentException(id);
        }
    }
}
