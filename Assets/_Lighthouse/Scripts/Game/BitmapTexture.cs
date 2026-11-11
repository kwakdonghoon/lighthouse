using Lighthouse.Render;
using UnityEngine;

namespace Lighthouse.Game
{
    /// <summary>순수 C# Bitmap 을 Texture2D/Sprite 로 올리는 얇은 Unity 래퍼 (설계서 2-3: RGBA32, 밉맵 없음, Point, Clamp).</summary>
    public static class BitmapTexture
    {
        public static Texture2D ToTexture(Bitmap b, bool makeNoLongerReadable = true)
        {
            var tex = new Texture2D(b.W, b.H, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "baked"
            };
            // Bitmap 은 위 행부터(canvas 순서), Texture2D 는 아래 행부터라 행을 뒤집어 넣는다
            var flipped = new uint[b.D.Length];
            for (int y = 0; y < b.H; y++)
                System.Array.Copy(b.D, y * b.W, flipped, (b.H - 1 - y) * b.W, b.W);
            tex.SetPixelData(flipped, 0);
            tex.Apply(false, makeNoLongerReadable);
            return tex;
        }

        /// <summary>왼쪽 위 모서리를 피벗으로 한 스프라이트(PPU=1): 위치 = 캔버스 좌표의 왼쪽 위.</summary>
        public static Sprite ToSprite(Texture2D tex)
            => Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 1), 1f, 0, SpriteMeshType.FullRect);
    }
}
