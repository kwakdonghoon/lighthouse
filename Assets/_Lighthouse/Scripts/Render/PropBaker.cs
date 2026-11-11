using System;

namespace Lighthouse.Render
{
    /// <summary>작은 소품 굽기: 우체통(1244행)·섬 둘레 얕은 물(1259행)·암초(1285행).</summary>
    public static class PropBaker
    {
        const string WoodD = "#6a4526";

        /// <summary>우체통: 깃발이 올라가면(up) 노란 블록이 선다.</summary>
        public static Bitmap Mailbox(bool up)
        {
            var S = new VNew(); S.Group(0, 0);
            S.Box(-0.1, -0.1, 0, 0.2, 0.2, 0.9, Vmat.Make(WoodD, new VmatOpts { Bv = 0 }));
            S.Box(-0.45, -0.3, 0.9, 0.9, 0.6, 0.6, Vmat.Make("#c9403a", new VmatOpts { T = "#e0605a", G = 0.08 }));
            S.Box(-0.35, -0.2, 1.5, 0.7, 0.4, 0.12, Vmat.Make("#a8322c", new VmatOpts { Bv = 0, G = 0 }));
            if (up)
            {
                S.Box(0.45, -0.05, 1.0, 0.1, 0.1, 0.8, Vmat.Make("#dddddd", new VmatOpts { Bv = 0, G = 0 }));
                S.Box(0.5, -0.07, 1.5, 0.4, 0.14, 0.3, Vmat.Make("#ffd36b", new VmatOpts { T = "#fff0a0", Bv = 0, G = 0 }));
            }
            else S.Box(0.45, -0.05, 0.95, 0.45, 0.1, 0.1, Vmat.Make("#dddddd", new VmatOpts { Bv = 0, G = 0 }));
            return S.Bake(1);
        }

        /// <summary>섬 둘레 얕은 물 (마름모 세 겹). N = 섬 한 변의 절반.</summary>
        public static Bitmap Shore(int N)
        {
            var S = new VNew();
            S.Floor(-N - 2.6, -N - 2.6, N + 2.6, N + 2.6, 0, "#5aa0d2", 0.12);
            S.Floor(-N - 1.7, -N - 1.7, N + 1.7, N + 1.7, 0, "#6eb4e1", 0.15);
            S.Floor(-N - 0.9, -N - 0.9, N + 0.9, N + 0.9, 0, "#a0d7f0", 0.2);
            return S.Bake(1);
        }

        /// <summary>암초: 바다에 솟은 돌 블록.</summary>
        public static Bitmap Reef()
        {
            var S = new VNew();
            S.Box(-1.1, -0.4, 0, 0.9, 0.9, 0.6, Vmat.Make("#59608a", new VmatOpts { G = 0.05, Wet = true, Tx = Tex.Stone }));
            S.Box(-0.35, -0.7, 0, 1.2, 1.2, 1.0, Vmat.Make("#6b7090", new VmatOpts { G = 0.05, Wet = true, Tx = Tex.Stone }));
            S.Box(0.55, -0.25, 0, 0.8, 0.8, 0.5, Vmat.Make("#7a7fa0", new VmatOpts { G = 0.05, Wet = true, Tx = Tex.Stone }));
            S.Box(-0.1, -0.45, 1.0, 0.7, 0.7, 0.3, Vmat.Make("#8a90b0", new VmatOpts { G = 0.2 }));
            return S.Bake(1);
        }
    }
}
