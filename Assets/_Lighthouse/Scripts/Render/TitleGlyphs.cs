using System.Collections.Generic;

namespace Lighthouse.Render
{
    /// <summary>타이틀 블록 글자 "등대지기"의 모양 마스크 조회 (표는 TitleGlyphs.Generated.cs). 순수 C#.</summary>
    public static partial class TitleGlyphs
    {
        public const string Text = "등대지기";
        public const int MinS = 16, MaxS = 28;
        static Dictionary<string, bool[]> cache;

        /// <summary>글자 크기 S(16..28)로 그린 ch 의 마스크 격자 (S+2)x(S+2). 위 행부터, 왼쪽부터. 표에 없으면 null.</summary>
        public static bool[] Mask(int s, char ch)
        {
            if (cache == null)
            {
                cache = new Dictionary<string, bool[]>();
                foreach (var line in Table)
                {
                    var p = line.Split(';'); int sz = int.Parse(p[0]) + 2; var m = new bool[sz * sz]; string hx = p[2];
                    for (int i = 0; i < m.Length; i++) m[i] = ((System.Convert.ToInt32(hx[i >> 2].ToString(), 16) >> (3 - (i & 3))) & 1) != 0;
                    cache[p[0] + ";" + p[1]] = m;
                }
            }
            return cache.TryGetValue(s + ";" + ch, out var r) ? r : null;
        }
    }
}
