using System.Collections.Generic;
using Lighthouse.Render;
using Lighthouse.Game;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace Lighthouse.UI
{
    /// <summary>색·틀 스프라이트·글꼴·아이콘을 한곳에서 불러온다(원본 CSS 변수 :root 의 값 그대로). 에셋은 Resources/UI 아래.</summary>
    public static class UiTheme
    {
        public static readonly Color Ink = Hex("#171b33"), Soft = Hex("#3f466a"), Teal = Hex("#2f6f36"), Teal2 = Hex("#255a2b"), Red = Hex("#a83228"), Gold = Hex("#e8b84a"), GoldTx = Hex("#7e4a06");
        public static readonly Color Paper = Hex("#c7cadf"), Card = Hex("#e2e4f1"), Line = Hex("#9a9fc2"), Line2 = Hex("#6c7299"), AdBg = Hex("#e6dfbe"), White = Color.white, OnGold = Hex("#1c1408");
        public static readonly Color Dim = new Color(14f / 255, 34f / 255, 60f / 255, 0.42f), DimLight = new Color(14f / 255, 34f / 255, 60f / 255, 0.10f);
        public static readonly Color HudText = Hex("#e6ecff"), HudSub = Hex("#c8d6f4"), HudGold = Hex("#ffe08a");

        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // ----- 틀(9-slice) -----
        sealed class FrameInfo { public Sprite Sprite; }
        static readonly Dictionary<string, Sprite> frames = new Dictionary<string, Sprite>();
        static Sprite whiteSprite;
        public static Sprite White1
        {
            get
            {
                if (whiteSprite != null) return whiteSprite;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "white" };
                t.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) }); t.Apply();
                return whiteSprite = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        /// <summary>틀 이름(card·btn·main·plate…)으로 스프라이트. 없으면 흰 사각형.</summary>
        public static Sprite Frame(string name)
        {
            if (frames.TryGetValue(name, out var s) && s != null) return s;
            s = Resources.Load<Sprite>("UI/Frames/" + name);
            if (s == null) s = White1;
            return frames[name] = s;
        }

        // ----- 글꼴 -----
        static TMP_FontAsset regular, bold;
        public static TMP_FontAsset FontRegular => regular != null ? regular : regular = Resources.Load<TMP_FontAsset>("UI/Fonts/NotoSansKR-Regular SDF");
        public static TMP_FontAsset FontBold => bold != null ? bold : bold = Resources.Load<TMP_FontAsset>("UI/Fonts/NotoSansKR-Bold SDF") ?? FontRegular;

        // ----- 아이콘(Render 의 굽기로 만든 8~16 도트 그림) -----
        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, string> trackIcon = new Dictionary<string, string> { { "lens", "pearl" }, { "garden", "seed" }, { "port", "bottle" } };

        /// <summary>아이콘 id: 선물·아이콘 이름, "decor:id"(장식 16×16), "ship:type"(배 그림), "track:id".</summary>
        public static Sprite Icon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (icons.TryGetValue(id, out var s)) return s;
            Bitmap b;
            try
            {
                if (id.StartsWith("decor:")) b = DecorBaker.Icon(DecorBaker.Item(id.Substring(6)));
                else if (id.StartsWith("ship:")) b = ShipBaker.Sprite(id.Substring(5), 1);
                else if (id.StartsWith("track:")) b = IconBaker.Bake(trackIcon.TryGetValue(id.Substring(6), out var t) ? t : "star");
                else b = IconBaker.Bake(id);
            }
            catch (System.Exception) { b = IconBaker.Bake("unknown"); }
            var tex = BitmapTexture.ToTexture(b, true);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
            return icons[id] = s;
        }

        public static void ClearCaches() { frames.Clear(); icons.Clear(); regular = bold = null; }
    }
}
