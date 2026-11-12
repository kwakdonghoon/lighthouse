using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.Render
{
    /// <summary>decorSlotCheck 의 결과(원본 HTML 3444행).</summary>
    public sealed class SlotCheckResult
    {
        public List<string> Violations = new List<string>(), FrontTouch = new List<string>();
        public int Count;
        public double MinPairDots = double.MaxValue, MinPairNonPathDots = double.MaxValue;
        public Dictionary<int, int> SlotsAt = new Dictionary<int, int>();
    }

    /// <summary>decorVisibility 의 결과(원본 HTML 3494행).</summary>
    public sealed class VisResult
    {
        public double Min = 1; public string MinAt;
        public List<string> Bad = new List<string>();
        public Dictionary<int, Dictionary<string, double>> Rows = new Dictionary<int, Dictionary<string, double>>();
        public Dictionary<int, Dictionary<string, string>> Worst = new Dictionary<int, Dictionary<string, string>>();
    }

    /// <summary>
    /// 꾸미기 자리 개발 검사 2종(HTML decorSlotCheck·decorVisibility 의 포팅 — G2'). 자리 목록을 인자로 받아
    /// 현재 데이터뿐 아니라 후보 좌표 시험에도 쓴다. 가시율은 실제 굽기 그림(섬 조각·탑·우체통·장식 몸통)의 알파 마스크로 잰다.
    /// 기본 풍경 좌표는 IslandBaker 의 조각 목록을 그대로 쓰므로(HTML 은 옮겨 적은 표) 풍경을 바꾸면 자동으로 따라간다.
    /// </summary>
    public static class DecorCheck
    {
        public const double VisMin = 0.8, VisMinBack = 0.65;
        static readonly int[] TierOf = { 0, 1, 1, 2, 2, 3, 3 };

        /// <summary>관리인 정지 자리(World/GameLoop.Keeper.cs 의 Stops 와 같은 표 — 시험이 둘의 일치를 본다).</summary>
        public static readonly (string act, int stage, double a, double b, bool noflag)[] KeeperStops =
        {
            ("polish", 1, -0.5, 2.0, false), ("mail", 2, 3.5, 2.6, false), ("pass", 5, -1e9, 0.5, false), ("fish", 5, 1e9, 0.5, false), ("pass", 5, -1e9, 0.5, false), ("well", 4, 3.5, -1.0, false),
            ("anchor", 6, 3.6, -2.2, false), ("pass", 4, 0, -3.2, false), ("sit", 4, -4.0, -0.5, false), ("pass", 4, -3.6, 0.8, true), ("flag", 5, -3.6, 1.5, false),
            ("water", 3, 2.9, 4.7, false), ("gild", 6, 2.7, 3.4, false), ("feed", 5, -0.5, 1e8, false), ("enter", 3, -3.9, 4.9, false), ("sweep", 1, -1.5, 4.9, false)
        };

        public static (double a, double b) StopAB(int N, (string act, int stage, double a, double b, bool noflag) s)
        {
            double a = s.a == -1e9 ? N - 1.5 : s.a == 1e9 ? N + 2.6 : s.a, c = s.b == 1e8 ? N - 1.5 : s.b;
            if (s.act == "feed") { a = -0.5; c = N - 1.5; }
            return (a, c);
        }

        /// <summary>기본 풍경 발자국 [이름, a, b, 반지름(칸)] — HTML decorScenery. 정원 레벨로 열리는 나무는 최악(모두 있음)으로 본다.</summary>
        public static List<(string nm, double a, double b, double r)> Scenery(int st, int N)
        {
            int t = TierOf[st]; var o = new List<(string, double, double, double)>();
            foreach (var p in new[] { new[] { -4.5, -1.5 }, new[] { -2.5, -4.5 } }) o.Add(("나무", p[0], p[1], 1.0));
            if (t >= 2) o.Add(("나무", 4.5, -2.5, 1.0));
            o.Add(("나무", 0.5, -4.5, 1.0)); o.Add(("나무", 4.5, 2.5, 1.0));
            o.Add(("바위", N - 0.5, -N + 2.5, 0.6)); o.Add(("바위", -N + 1.5, -N + 0.5, 0.5)); o.Add(("바위", -N + 0.5, 1.5, 0.6)); o.Add(("바위", N - 2.5, N - 0.5, 0.5));
            o.Add(("우체통", 3.5, 1.5, 0.45));
            if (t >= 2) o.Add(("집", -3.5, 3.5, 1.4));
            if (st >= 2) { o.Add(("보트", -2.6, N - 0.8, 1.0)); o.Add(("표지판", 3.5, 2.5, 0.5)); }
            if (st >= 3) o.Add(("꽃밭", 1.5, 4.7, 1.1));
            if (st >= 4) { o.Add(("벤치", -4.0, -0.5, 0.8)); o.Add(("우물", 3.5, -2.0, 0.85)); }
            if (st >= 5) { o.Add(("깃대", -4.5, 1.5, 0.4)); o.Add(("상자", N - 2.1, 2.4, 0.8)); }
            if (st >= 6) { o.Add(("기념 닻", 3.6, -3.2, 0.9)); o.Add(("금빛 기둥", -2.4, 3.3, 0.4)); o.Add(("금빛 기둥", 3.4, 3.4, 0.4)); o.Add(("금빛 기둥", -0.5, N - 1.6, 0.4)); }
            return o;
        }

        /// <summary>하늘 자리의 섬 중심 기준 가로 좌표(DecorLayout.Pos 와 같은 식).</summary>
        public static double SkyX(DecorSlot s, double R) => s.Id == "SK1" ? -JsMath.Round(R * 0.62) : JsMath.Round(R * 0.70);

        public static double Rad(DecorSlot s) => s.Chip == "path" ? 0.4 : s.Size == "S" ? 0.45 : s.Size == "M" ? 0.8 : 1.3;

        static double DSeg(double a, double b, double x1, double y1, double x2, double y2)
            => Math.Sqrt(Math.Pow(a - Math.Max(Math.Min(x1, x2), Math.Min(Math.Max(x1, x2), a)), 2) + Math.Pow(b - Math.Max(Math.Min(y1, y2), Math.Min(Math.Max(y1, y2), b)), 2));
        static double DRect(double a, double b, double x1, double y1, double x2, double y2)
            => Math.Sqrt(Math.Pow(a - Math.Max(x1, Math.Min(x2, a)), 2) + Math.Pow(b - Math.Max(y1, Math.Min(y2, b)), 2));
        static string F2(double v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>단계 st 에서 자리 하나가 검사에 들어가는가(막힘 제외 옵션, 단계 열림 기준).</summary>
        static bool InStage(DecorSlot s, int st, bool includeBlocked) => (includeBlocked || !s.Blocked) && st >= s.MinStage && !(s.OpenKind == "stage" && st < s.OpenVal);

        /// <summary>
        /// decorSlotCheck: 6단계 각각에서 자리들이 풍경·탑·길·관리인 정지 자리·서로와 겹치지 않는지(아트 2-4 의 5항 + 안쪽 경계·부두).
        /// G2' 추가: 앞쪽 맞닿음 예외는 자리 중심이 풍경 발자국 반지름 밖일 때만(집 한가운데를 통과시키던 구멍 막음).
        /// </summary>
        public static SlotCheckResult SlotCheck(IList<DecorSlot> slots, bool includeBlocked = false)
        {
            var res = new SlotCheckResult();
            for (int st = 1; st <= 6; st++)
            {
                int N = Terrain.N(st); var sc = Scenery(st, N); double th = st <= 3 ? 1.25 : 2.0;
                var bad = new List<string>(); var pos = new Dictionary<string, double[]>();
                var ids = slots.Where(s => !s.Xy && InStage(s, st, includeBlocked)).ToList();
                var sky = slots.Where(s => s.Xy && InStage(s, st, includeBlocked)).ToList();
                var segs = new List<double[]> { new[] { -0.5, 1, -0.5, N } };
                if (TierOf[st] >= 2) segs.Add(new[] { -2, 3.5, -0.9, 3.5 });
                segs.Add(new[] { 1, 0.5, TierOf[st] >= 3 ? N : 4.1, 0.5 });
                foreach (var sl in ids)
                {
                    var ab = sl.AB(N); double a = ab[0], b = ab[1], r = Rad(sl); bool path = sl.Chip == "path", shore = sl.Chip == "shore"; pos[sl.Id] = ab;
                    if (!shore && !path && !(Math.Abs(a) < N - 1 && Math.Abs(b) < N - 1)) bad.Add(sl.Id + ": 안쪽 풀밭 밖");
                    foreach (var (nm, sa, sb, sr) in sc)
                    {
                        double d = Math.Sqrt((a - sa) * (a - sa) + (b - sb) * (b - sb));
                        if (d < r + sr + 0.1)
                        {
                            string msg = st + "단계 " + sl.Id + "~" + nm + "(" + sa + "," + sb + ") 간격 " + F2(d) + " < " + F2(r + sr + 0.1);
                            if (a + b > sa + sb + 0.01 && d >= sr) res.FrontTouch.Add(msg + " (자리가 풍경 앞쪽: 깊이 순서는 맞고 풍경 아랫부분만 가림)");
                            else bad.Add(msg + (d < sr ? " [풍경 발자국 안]" : ""));
                        }
                    }
                    double sq = DRect(a, b, -th, -th, th, th);
                    if (path ? sq <= 0 : sq < r) bad.Add(sl.Id + "~탑 밑동(±" + th + ") " + (path ? "안쪽(탑에 가려짐)" : "간격 " + F2(sq) + " < " + r));
                    if (!path) { double d = segs.Min(s => DSeg(a, b, s[0], s[1], s[2], s[3])); if (d < 0.5 + 0.6 * r) bad.Add(sl.Id + "~길 간격 " + F2(d) + " < " + F2(0.5 + 0.6 * r)); }
                    double rl = Math.Min(r, 0.49);
                    bool onLand = new[] { new[] { 0.0, 0.0 }, new[] { rl, rl }, new[] { -rl, -rl }, new[] { rl, -rl }, new[] { -rl, rl } }.All(dd => { var (u, v) = Terrain.Blk2Uv(a + dd[0], b + dd[1]); return Terrain.TerrH(st, u, v) >= 0; });
                    if (!onLand) bad.Add(st + "단계 " + sl.Id + ": 발자국이 바다 위로 나감");
                    if (!path)
                        foreach (var k in KeeperStops)
                        {
                            if (st < k.stage || (k.noflag && st >= 5)) continue;
                            var (ka, kb) = StopAB(N, k); double d = Math.Sqrt((a - ka) * (a - ka) + (b - kb) * (b - kb));
                            if (d < r + 0.5) bad.Add(sl.Id + "~관리인 정지 자리 " + k.act + "(" + ka.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "," + kb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ") 간격 " + F2(d) + " < " + F2(r + 0.5));
                        }
                    if (shore)
                    {
                        if (DRect(a, b, -2.1, N - 1, 1.1, N) < r + 0.1) bad.Add(sl.Id + "~모래 해변");
                        if (TierOf[st] >= 3 && DRect(a, b, N - 1, -1.1, N + 4, 2.1) < r + 0.1) bad.Add(sl.Id + "~부두");
                    }
                }
                for (int i = 0; i < ids.Count; i++)
                    for (int j = i + 1; j < ids.Count; j++)
                    {
                        var A = ids[i]; var B = ids[j]; var dA = pos[A.Id]; var dB = pos[B.Id];
                        double d = Math.Sqrt(Math.Pow(dA[0] - dB[0], 2) + Math.Pow(dA[1] - dB[1], 2)); bool same = A.Chip == B.Chip, pp = same && A.Chip == "path";
                        double need = Rad(A) + Rad(B) + (same && !pp ? 0.2 : 0.1);
                        if (d < need) bad.Add(A.Id + "~" + B.Id + " 간격 " + F2(d) + " < " + F2(need));
                        double du = ((dA[0] - dA[1]) - (dB[0] - dB[1])) * 4, dv = ((dA[0] + dA[1]) - (dB[0] + dB[1])) * 2, dots = Math.Sqrt(du * du + dv * dv);
                        if (same) { res.MinPairDots = Math.Min(res.MinPairDots, dots); if (!pp) res.MinPairNonPathDots = Math.Min(res.MinPairNonPathDots, dots); }
                    }
                double R = 8.0 * N;
                foreach (var sl in sky) { double x = SkyX(sl, R); if (Math.Abs(x) < 16 + 5 + 2) bad.Add(sl.Id + ": 탑·램프 글로우와 가로 간격 " + Math.Abs(x) + " < 23"); }
                if (sky.Count == 2) { double x1 = SkyX(sky[0], R), x2 = SkyX(sky[1], R); if (Math.Sqrt((x1 - x2) * (x1 - x2) + 18 * 18) < 12) bad.Add("SK1~SK2 간격"); }
                res.SlotsAt[st] = ids.Count + sky.Count; res.Count += ids.Count + sky.Count;
                foreach (var m in bad) res.Violations.Add(st + "단계 " + m);
            }
            return res;
        }

        // ================= 가시율 =================
        const int GW = 300, GH = 300, OX = 150, OYY = 230;

        internal sealed class Entry
        {
            public string Id, Item; public double V, Rank; public bool A, Sky, Flat; public int Px, Py, W, H; public bool[] Mask;
        }

        /// <summary>한 단계의 마스크 묶음(탑·풍경 깊이·우체통). 같은 단계의 후보를 여러 번 잴 때 재사용한다.</summary>
        public sealed class StageCtx
        {
            public int Stage, N; internal bool[] Tower, Mail; internal float[] Sv;
            internal List<DecorSlot> Slots; internal IList<DecorItem> Items; internal List<Entry> Fill;
            internal Dictionary<string, Bitmap> Bodies = new Dictionary<string, Bitmap>();
            const double MailV = (3.5 + 1.5) * 4;

            static void Stamp(bool[] m, Bitmap s, double x, double y, int thr)
            {
                int ox = (int)JsMath.Round(x + s.Ax), oy = (int)JsMath.Round(y + s.Ay);
                for (int j = 0; j < s.H; j++) { int yy = oy + j; if (yy < 0 || yy >= GH) continue; for (int i = 0; i < s.W; i++) { int xx = ox + i; if (xx < 0 || xx >= GW) continue; if ((int)(s.D[j * s.W + i] >> 24) > thr) m[yy * GW + xx] = true; } }
            }

            public static StageCtx Build(int st, IList<DecorSlot> slots, IList<DecorItem> items, bool includeBlocked)
            {
                var c = new StageCtx { Stage = st, N = Terrain.N(st), Items = items };
                var sk = Palettes.TowerSkinOf("classic"); var P = TowerParams.ForStage(st, 2, sk);
                c.Tower = new bool[GW * GH];
                Stamp(c.Tower, TowerBaker.BakeA(P, sk), OX, OYY, 127); Stamp(c.Tower, TowerBaker.BakeLamp(P, false), OX, OYY, 127); Stamp(c.Tower, TowerBaker.BakeB(P), OX, OYY, 127);
                var isl = new IslandBaker(Palettes.IslandEnvFor("summer", st, "classic", "classic", 6)).Bake();
                c.Sv = new float[GW * GH]; for (int i = 0; i < c.Sv.Length; i++) c.Sv[i] = -1e9f;
                foreach (var p in isl.Pieces)
                {
                    var m = new bool[GW * GH]; Stamp(m, p.Spr, OX, OYY + Terrain.IsleY, 127);
                    for (int i = 0; i < m.Length; i++) if (m[i] && p.V > c.Sv[i]) c.Sv[i] = (float)p.V;
                }
                c.Mail = new bool[GW * GH];
                var (mu, mv) = Terrain.Blk2Uv(3.5, 1.5);
                Stamp(c.Mail, PropBaker.Mailbox(false), OX + JsMath.Round(mu), OYY + (JsMath.Round(Terrain.IsleY + mv * Terrain.KY - Terrain.TerrH(st, mu, mv))), 127);
                c.SetSlots(slots, includeBlocked);
                return c;
            }

            /// <summary>이 단계에서 채워 보는 자리 목록을 바꾼다(탑·풍경 마스크는 그대로 — 후보 좌표 탐색이 빠르도록).</summary>
            public void SetSlots(IList<DecorSlot> slots, bool includeBlocked)
            {
                Slots = slots.Where(s => InStage(s, Stage, includeBlocked)).ToList();
                Fill = new List<Entry>();
                foreach (var sl in Slots)
                {
                    var al = Allowed(sl);
                    if (al.Count == 0) continue;
                    Fill.Add(Place(sl, al.OrderByDescending(k => k.H).First(), null));       // 안정 정렬(내림차순, 동률은 목록 순서)
                }
            }

            public List<DecorItem> Allowed(DecorSlot sl) => Items.Where(k => k.Zones.Contains(sl.Chip) && DecorBaker.SizeRank(k.Size) <= DecorBaker.SizeRank(sl.Size)).ToList();

            Bitmap Body(DecorItem it) { if (!Bodies.TryGetValue(it.Id, out var b)) Bodies[it.Id] = b = DecorBaker.Body(it, "summer", "classic", false); return b; }

            /// <summary>자리에 아이템을 놓은 항목. ovr: 블록 좌표를 직접 지정(후보 시험).</summary>
            internal Entry Place(DecorSlot sl, DecorItem it, double[] ovr)
            {
                double x, y; double[] ab = null;
                if (sl.Xy) { var p = DecorLayout.Pos(sl, Stage, 360); x = p.x - 180; y = p.y - Terrain.CY; }
                else
                {
                    ab = ovr ?? sl.AB(N); var (u, v) = Terrain.Blk2Uv(ab[0], ab[1]);
                    x = JsMath.Round(u); y = JsMath.Round(Terrain.IsleY + v * Terrain.KY - Terrain.TerrH(Stage, u, v));
                }
                var body = Body(it); double vv = ab != null ? (ab[0] + ab[1]) * 4 : 0; bool flat = sl.Chip == "path", sky = sl.Xy, A = flat || vv < -2;
                var e = new Entry { Id = sl.Id, Item = it.Id, V = vv, A = A, Sky = sky, Flat = flat, Rank = (sky ? 2e6 : A ? 0 : 1e6) + vv, Px = (int)(OX + x - it.Ax), Py = (int)(OYY + y - it.Ay), W = body.W, H = body.H, Mask = new bool[body.W * body.H] };
                for (int i = 0; i < e.Mask.Length; i++) e.Mask[i] = (body.D[i] >> 24) > 0;
                return e;
            }

            internal double VisOf(Entry e, ICollection<string> selfIds)
            {
                int tot = 0, hid = 0;
                for (int y = Math.Max(0, e.Py); y < Math.Min(GH, e.Py + e.H); y++)
                    for (int x = Math.Max(0, e.Px); x < Math.Min(GW, e.Px + e.W); x++)
                    {
                        if (!e.Mask[(y - e.Py) * e.W + (x - e.Px)]) continue;
                        tot++; int i = y * GW + x; bool h = false;
                        if (e.A && Tower[i]) h = true;
                        else if (e.A && Mail[i]) h = true;
                        else if (!e.Sky && Sv[i] > e.V + 0.1) h = true;
                        else if (!e.Sky && !e.A && Mail[i] && e.V < MailV) h = true;
                        if (!h)
                            foreach (var o in Fill)
                                if (!selfIds.Contains(o.Id) && !(e.Flat && o.Flat) && o.Rank > e.Rank && o.Sky == e.Sky && x >= o.Px && y >= o.Py && x < o.Px + o.W && y < o.Py + o.H && o.Mask[(y - o.Py) * o.W + (x - o.Px)]) { h = true; break; }
                        if (h) hid++;
                    }
                return tot > 0 ? 1.0 - (double)hid / tot : 1.0;
            }

            /// <summary>자리(또는 후보 좌표)의 최악 가시율: 허용 아이템 전부 중 최소. worstItem 은 그 아이템.</summary>
            public double Worst(DecorSlot sl, double[] ovr, out string worstItem)
            {
                double worst = 1; worstItem = "";
                foreach (var k in Allowed(sl)) { double r = VisOf(Place(sl, k, ovr), new[] { sl.Id }); if (r < worst) { worst = r; worstItem = k.Id; } }
                return worst;
            }
        }

        public static double Threshold(DecorSlot s) => s.Chip == "back" ? VisMinBack : VisMin;

        /// <summary>decorVisibility: 6단계 × 자리 × 놓을 수 있는 아이템 전부의 최악 가시율. 기준 미달은 Bad 에.</summary>
        public static VisResult Visibility(IList<DecorSlot> slots, IList<DecorItem> items, bool includeBlocked = false)
        {
            var res = new VisResult();
            for (int st = 1; st <= 6; st++)
            {
                var c = StageCtx.Build(st, slots, items, includeBlocked);
                var rows = new Dictionary<string, double>(); var wk = new Dictionary<string, string>();
                foreach (var sl in c.Slots)
                {
                    double w = c.Worst(sl, null, out var item); rows[sl.Id] = Math.Round(w, 3); wk[sl.Id] = item;
                    if (w < res.Min) { res.Min = w; res.MinAt = st + "단계 " + sl.Id + "(" + item + ")"; }
                    if (w < Threshold(sl)) res.Bad.Add(st + "단계 " + sl.Id + " 가시 " + Math.Round(w * 100) + "% (" + item + ")");
                }
                res.Rows[st] = rows; res.Worst[st] = wk;
            }
            return res;
        }
    }
}
