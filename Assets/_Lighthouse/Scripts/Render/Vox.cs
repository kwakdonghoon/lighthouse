using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    public enum Tex { None, Stone, Leaf, Wood }

    /// <summary>JS vmat 의 두 번째 인자 <c>o</c>. null/기본값은 JS 의 undefined 에 해당.</summary>
    public sealed class VmatOpts
    {
        public string T, L, R;
        public string[] Pal;
        public Tex Tx = Tex.None;
        public double? G, Bv;
        public bool Wet;
    }

    /// <summary>재질: 한 색에서 윗면·왼면·오른면 세 톤 (HTML vmat 834행).</summary>
    public sealed class Vmat
    {
        public Col T, L, R;
        public Col[] Pal;
        public Tex Tx;
        public double G, Bv;
        public bool Wet;

        public static Vmat Make(string hex, VmatOpts o = null) => Make(Col.Hex(hex), o);

        public static Vmat Make(Col c, VmatOpts o = null)
        {
            o = o ?? new VmatOpts();
            Col F(double k, double bl) => new Col(JsMath.Clamp255(c.R * k), JsMath.Clamp255(c.G * k), JsMath.Clamp255(c.B * (k + bl)));
            return new Vmat
            {
                T = !string.IsNullOrEmpty(o.T) ? Col.Hex(o.T) : c,
                L = !string.IsNullOrEmpty(o.L) ? Col.Hex(o.L) : F(0.78, 0.04),
                R = !string.IsNullOrEmpty(o.R) ? Col.Hex(o.R) : F(0.56, 0.12),
                Pal = o.Pal != null ? o.Pal.Select(Col.Hex).ToArray() : null,
                Tx = o.Tx,
                G = o.G ?? 0.1,
                Bv = o.Bv ?? 1,
                Wet = o.Wet
            };
        }
    }

    /// <summary>한 장의 굽기에 쓰는 래스터라이저와 장면 수집기 (HTML 842~944행).</summary>
    public static class Raster
    {
        public const double VS = 4, VZ = 4;

        public static (double x, double y) VProj(double a, double b, double c) => ((a - b) * VS, (a + b) * VS / 2 - c * VZ);

        // 면 질감 (2x2 도트 덩어리·균열·판자 이음). x>>1 은 부호 있는 산술 시프트 (JS 와 같다)
        static double Vtx(Tex tx, int x, int y)
        {
            switch (tx)
            {
                case Tex.Stone:
                {
                    double h = JsMath.Hsh(x >> 1, y >> 1), k = JsMath.Hsh(x + 5, y * 3);
                    return (h < 0.2 ? 0.86 : h > 0.88 ? 1.09 : 1) * (k < 0.035 ? 0.72 : 1);
                }
                case Tex.Leaf:
                {
                    double h = JsMath.Hsh((x >> 1) + 9, (y >> 1) + 4);
                    return h < 0.2 ? 0.6 : h > 0.84 ? 1.14 : 1;
                }
                case Tex.Wood:
                    return ((y & 3) == 0 ? 0.8 : 1) * ((((x + (y >> 2) * 3) & 7) == 0) ? 0.86 : 1) * (JsMath.Hsh(x, y + 2) < 0.06 ? 0.92 : 1);
                default:
                    return 1;
            }
        }

        /// <summary>
        /// 볼록 다각형을 도트 중심 규칙으로 채운다 (번짐 없음). alpha &lt; 1 이면 아래 색과 섞는다. (HTML vfill 849행)
        /// pal 이 있으면 도트마다 pal 에서 해시로 색을 고른다. grain 은 알갱이 세기(0 이면 없음).
        /// </summary>
        public static void Vfill(Bitmap B, (double x, double y)[] pts, Col c, double grain, Col[] pal, double alpha, Tex tx)
        {
            double ymin = 1e9, ymax = -1e9;
            foreach (var p in pts) { if (p.y < ymin) ymin = p.y; if (p.y > ymax) ymax = p.y; }
            int n = pts.Length;
            int yEnd = (int)Math.Ceiling(ymax - 0.5);
            for (int y = (int)Math.Ceiling(ymin - 0.5); y < yEnd; y++)
            {
                double yc = y + 0.5, xl = 1e9, xr = -1e9;
                for (int i = 0; i < n; i++)
                {
                    var p = pts[i]; var q = pts[(i + 1) % n];
                    if ((p.y <= yc && yc < q.y) || (q.y <= yc && yc < p.y))
                    {
                        double x = p.x + (yc - p.y) * (q.x - p.x) / (q.y - p.y);
                        if (x < xl) xl = x;
                        if (x > xr) xr = x;
                    }
                }
                if (xl > xr) continue;
                int by = y - B.Ay; if (by < 0 || by >= B.H) continue;
                int xEnd = (int)Math.Ceiling(xr - 0.5);
                for (int x = (int)Math.Ceiling(xl - 0.5); x < xEnd; x++)
                {
                    int bx = x - B.Ax; if (bx < 0 || bx >= B.W) continue;
                    Col col = pal != null ? pal[((int)Math.Floor(JsMath.Hsh(x, y) * pal.Length)) % pal.Length] : c;
                    double f = 1;
                    if (grain != 0) { double g = JsMath.Hsh(y + 977, x - 313); if (g < grain * 0.5) f = 0.9; else if (g > 1 - grain * 0.5) f = 1.09; }
                    if (tx != Tex.None) f *= Vtx(tx, x, y);
                    double r = Math.Min(255, col.R * f), gg = Math.Min(255, col.G * f), bb = Math.Min(255, col.B * f);
                    int o = by * B.W + bx;
                    if (alpha < 1)
                    {
                        uint d = B.D[o], da = d >> 24;
                        if (da != 0)
                        {
                            double k = alpha;
                            B.D[o] = Bitmap.Pack(
                                JsMath.RoundI((d & 255) * (1 - k) + r * k),
                                JsMath.RoundI(((d >> 8) & 255) * (1 - k) + gg * k),
                                JsMath.RoundI(((d >> 16) & 255) * (1 - k) + bb * k), 255);
                        }
                        else B.D[o] = Bitmap.Pack(JsMath.RoundI(r), JsMath.RoundI(gg), JsMath.RoundI(bb), JsMath.RoundI(alpha * 255));
                    }
                    else B.D[o] = Bitmap.Pack(JsMath.RoundI(r), JsMath.RoundI(gg), JsMath.RoundI(bb), 255);
                }
            }
        }
    }

    /// <summary>한 물체(상자)가 차지한 화면 사각형. HTML 의 acc {x0,y0,x1,y1}.</summary>
    public sealed class BBox
    {
        public double X0 = 1e9, Y0 = 1e9, X1 = -1e9, Y1 = -1e9;
    }

    public sealed class VItem
    {
        public double K;
        public Action<Bitmap> F;
        public BBox Bb;
        public string Tag;
    }

    public sealed class Piece
    {
        public string Tag;
        public double K;
        public Bitmap Spr;
    }

    /// <summary>
    /// 장면 수집기 (HTML vnew 878행). 상자를 모아 두었다가 뒤에서 앞으로, 아래에서 위로 칠하고 한 장으로 굽는다.
    /// vnew 클로저의 상태(acc, G, curTag)는 모두 필드 (부록 B-17).
    /// </summary>
    public sealed class VNew
    {
        readonly List<VItem> it = new List<VItem>();
        double x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
        double? G;
        string curTag;
        BBox acc = new BBox();

        public double KeyOf(double x, double y, double z, double w, double d, double h)
            => (G.HasValue ? G.Value * 1000 : (x + w + y + d) * 1000) + z * 10 + (x + y) * 0.01;

        /// <summary>group(a,b): 한 물체로 묶는다(깊이 열쇠가 a+b). group() 은 해제.</summary>
        public void Group(double a, double b) { G = a + b; }
        public void GroupEnd() { G = null; }
        public void Tag(string id) { curTag = id; }

        void Grow(double px, double py)
        {
            if (px < x0) x0 = px; if (px > x1) x1 = px; if (py < y0) y0 = py; if (py > y1) y1 = py;
            if (px < acc.X0) acc.X0 = px; if (px > acc.X1) acc.X1 = px; if (py < acc.Y0) acc.Y0 = py; if (py > acc.Y1) acc.Y1 = py;
        }
        void Grow((double x, double y) p) => Grow(p.x, p.y);

        void Push(double k, Action<Bitmap> f)
        {
            var o = new VItem { K = k, F = f, Bb = acc, Tag = curTag };
            acc = new BBox();
            it.Add(o);
        }

        static (double x, double y) P(double a, double b, double c) => Raster.VProj(a, b, c);
        const double VZ = Raster.VZ;

        public void Box(double x, double y, double z, double w, double d, double h, Vmat m, double? k = null)
        {
            double xe = x + w, ye = y + d, ze = z + h;
            Grow(P(x, y, ze)); Grow(P(xe, y, ze)); Grow(P(xe, ye, ze)); Grow(P(x, ye, ze)); Grow(P(xe, ye, z)); Grow(P(x, ye, z)); Grow(P(xe, y, z));
            Push(k ?? KeyOf(x, y, z, w, d, h), B =>
            {
                Raster.Vfill(B, new[] { P(x, y, ze), P(xe, y, ze), P(xe, ye, ze), P(x, ye, ze) }, m.T, m.G, m.Pal, 1, m.Tx);
                Raster.Vfill(B, new[] { P(x, ye, ze), P(xe, ye, ze), P(xe, ye, z), P(x, ye, z) }, m.L, m.G, null, 1, m.Tx);
                Raster.Vfill(B, new[] { P(xe, y, ze), P(xe, ye, ze), P(xe, ye, z), P(xe, y, z) }, m.R, m.G, null, 1, m.Tx);
                if (m.Bv != 0 && h >= 0.5)                       // 위 모서리에 밝은 1도트 띠 (쌓인 블록 사이 이음매)
                {
                    double e = 1 / VZ;
                    Raster.Vfill(B, new[] { P(x, ye, ze), P(xe, ye, ze), P(xe, ye, ze - e), P(x, ye, ze - e) }, Col.Mul(m.L, 1.16), 0, null, 1, Tex.None);
                    Raster.Vfill(B, new[] { P(xe, y, ze), P(xe, ye, ze), P(xe, ye, ze - e), P(xe, y, ze - e) }, Col.Mul(m.R, 1.2), 0, null, 1, Tex.None);
                }
                if (m.Wet)                                       // 물에 닿은 밑단
                {
                    double e = 1 / VZ;
                    Raster.Vfill(B, new[] { P(x, ye, z + e), P(xe, ye, z + e), P(xe, ye, z), P(x, ye, z) }, Col.Mul(m.L, 0.55), 0, null, 1, Tex.None);
                    Raster.Vfill(B, new[] { P(xe, y, z + e), P(xe, ye, z + e), P(xe, ye, z), P(xe, y, z) }, Col.Mul(m.R, 0.55), 0, null, 1, Tex.None);
                }
            });
        }

        /// <summary>왼면(+b 방향) 위의 사각형 (창·문·무늬). plane = 면의 위치.</summary>
        public void RectL(double a0, double a1, double plane, double z0, double z1, string col, double k, double alpha = 1)
        {
            var pts = new[] { P(a0, plane, z1), P(a1, plane, z1), P(a1, plane, z0), P(a0, plane, z0) };
            foreach (var p in pts) Grow(p);
            var cc = Col.Hex(col);
            Push(k, B => Raster.Vfill(B, pts, cc, 0, null, alpha, Tex.None));
        }

        /// <summary>오른면(+a 방향) 위의 사각형.</summary>
        public void RectR(double b0, double b1, double plane, double z0, double z1, string col, double k, double alpha = 1)
        {
            var pts = new[] { P(plane, b0, z1), P(plane, b1, z1), P(plane, b1, z0), P(plane, b0, z0) };
            foreach (var p in pts) Grow(p);
            var cc = Col.Hex(col);
            Push(k, B => Raster.Vfill(B, pts, cc, 0, null, alpha, Tex.None));
        }

        public void Floor(double a0, double b0, double a1, double b1, double z, string col, double alpha = 1)
        {
            var pts = new[] { P(a0, b0, z), P(a1, b0, z), P(a1, b1, z), P(a0, b1, z) };
            foreach (var p in pts) Grow(p);
            var cc = Col.Hex(col);
            Push(-1e12 + a0 * 10 + b0, B => Raster.Vfill(B, pts, cc, 0, null, alpha, Tex.None));
        }

        public void Dot(double a, double b, double c, string col, double k, double size = 1)
        {
            var p = P(a, b, c); Grow(p); Grow(p.x + size, p.y + size);
            var cc = Col.Hex(col);
            Push(k, B => Raster.Vfill(B, new[] { (p.x, p.y), (p.x + size, p.y), (p.x + size, p.y + size), (p.x, p.y + size) }, cc, 0, null, 1, Tex.None));
        }

        /// <summary>안정 정렬: JS Array.prototype.sort 는 안정 (부록 B-3). LINQ OrderBy 도 안정이다.</summary>
        static List<T> StableSort<T>(List<T> src, Func<T, double> key) => src.OrderBy(key).ToList();

        /// <summary>
        /// tag 가 같은 상자끼리 한 조각으로 따로 구워 깊이 순으로 돌려준다 (HTML bakePieces 891행).
        /// 주의: 그 전에 Bake() 가 it 를 제자리 정렬해 두므로 그룹 등장 순서도 정렬된 순서를 따른다 (같은 순서로 호출할 것).
        /// </summary>
        public List<Piece> BakePieces(bool skipNull)
        {
            const string NullKey = "\u0000null";
            var groups = new Dictionary<string, (List<VItem> items, BBox bb, double[] k)>();
            var order = new List<string>();
            var tagOf = new Dictionary<string, string>();
            foreach (var o in it)
            {
                if (skipNull && o.Tag == null) continue;
                string key = o.Tag ?? NullKey;
                if (!groups.TryGetValue(key, out var gp))
                {
                    gp = (new List<VItem>(), new BBox(), new[] { 1e18 });
                    groups[key] = gp; order.Add(key); tagOf[key] = o.Tag;
                }
                gp.items.Add(o);
                gp.bb.X0 = Math.Min(gp.bb.X0, o.Bb.X0); gp.bb.Y0 = Math.Min(gp.bb.Y0, o.Bb.Y0);
                gp.bb.X1 = Math.Max(gp.bb.X1, o.Bb.X1); gp.bb.Y1 = Math.Max(gp.bb.Y1, o.Bb.Y1);
                gp.k[0] = Math.Min(gp.k[0], o.K);
            }
            var outList = new List<Piece>();
            foreach (var key in order)
            {
                var gp = groups[key];
                int ix0 = (int)Math.Floor(gp.bb.X0) - 1, iy0 = (int)Math.Floor(gp.bb.Y0) - 1;
                int w = (int)Math.Ceiling(gp.bb.X1) + 1 - ix0, h = (int)Math.Ceiling(gp.bb.Y1) + 1 - iy0;
                var B = new Bitmap(ix0, iy0, w, h);
                foreach (var o in StableSort(gp.items, q => q.K)) o.F(B);
                outList.Add(new Piece { Tag = tagOf[key], K = gp.k[0], Spr = B });
            }
            return StableSort(outList, p => p.K);
        }

        /// <summary>한 장으로 굽는다. skipTagged: tag 가 붙은 상자(조각)는 빼고 굽는다 (크기·원점은 그대로).</summary>
        public Bitmap Bake(int pad = 1, bool skipTagged = false)
        {
            int ix0 = (int)Math.Floor(x0) - pad, iy0 = (int)Math.Floor(y0) - pad;
            int w = (int)Math.Ceiling(x1) + pad - ix0, h = (int)Math.Ceiling(y1) + pad - iy0;
            var B = new Bitmap(ix0, iy0, w, h);
            var sorted = StableSort(it, q => q.K);
            it.Clear(); it.AddRange(sorted);                      // JS it.sort 는 제자리 정렬
            foreach (var i in it) if (!(skipTagged && i.Tag != null)) i.F(B);
            return B;
        }
    }

    /// <summary>땅 높이(dz 블록)만큼 올려서 쓰는 보기: 물체를 풀밭 위에 얹을 때 (HTML vshift 946행).</summary>
    public sealed class VShift
    {
        readonly VNew S; readonly double dz;
        public VShift(VNew s, double dz) { S = s; this.dz = dz; }
        public double KeyOf(double x, double y, double z, double w, double d, double h) => S.KeyOf(x, y, z + dz, w, d, h);
        public void Box(double x, double y, double z, double w, double d, double h, Vmat m, double? k = null) => S.Box(x, y, z + dz, w, d, h, m, k);
        public void RectL(double a0, double a1, double p, double z0, double z1, string c, double k, double al = 1) => S.RectL(a0, a1, p, z0 + dz, z1 + dz, c, k, al);
        public void RectR(double b0, double b1, double p, double z0, double z1, string c, double k, double al = 1) => S.RectR(b0, b1, p, z0 + dz, z1 + dz, c, k, al);
        public void Dot(double a, double b, double c, string col, double k, double s = 1) => S.Dot(a, b, c + dz, col, k, s);
    }
}
