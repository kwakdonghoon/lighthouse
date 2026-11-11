using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Game;
using Lighthouse.Render;
using Lighthouse.World;
using UnityEngine;

namespace Lighthouse.App
{
    /// <summary>
    /// 게임 월드 그리기(M4): 순수 GameLoop 의 상태를 읽어 화면에 올린다.
    ///  - 정적 층(섬·탑·꾸미기·암초): WorldView 의 스프라이트(낮 그림 + 밤 그림 dusk 알파).
    ///  - 배경(하늘·바다 물결): 프레임마다 CPU 로 그려 배경 텍스처 한 장에 올린다(M2 2차).
    ///  - 배·관리인: 스프라이트 풀(항로·v 순서로 정적 층 사이에 끼움).
    ///  - 연출 층(빛줄기·불빛·날씨·해질녘 틴트·물결·선물 팝·관리인 손동작): WorldPainter 가 CPU 로 그린 premultiplied 한 장을 맨 위에 얹는다.
    /// </summary>
    public sealed class GameWorldView : MonoBehaviour
    {
        public Material SpriteMat, PremulMat;
        public WorldView View { get; private set; }
        public SceneSprites Sprites { get; private set; }
        public FitView Fit { get; private set; }
        public int W, H, OY;
        public double LastFrameMs, LastPaintMs, LastBakeMs, LastBgMs;
        public int DevW { get; private set; }

        GameLoop loop; GameContext ctx;
        WorldPainter painter; Texture2D ovTex; uint[] ovBuf; SpriteRenderer ovSr; Sprite ovSprite;
        CpuCanvas bg, bgMix; double mixD = -1; double shownDusk = -1;
        string shipSkin = "classic";
        readonly Dictionary<Ship, ShipVis> ships = new Dictionary<Ship, ShipVis>();
        readonly Dictionary<string, (Bitmap d, Bitmap n)> shipSpr = new Dictionary<string, (Bitmap, Bitmap)>(), shipHelp = new Dictionary<string, (Bitmap, Bitmap)>(), shadows = new Dictionary<string, (Bitmap, Bitmap)>();
        readonly Dictionary<Bitmap, Sprite> spriteCache = new Dictionary<Bitmap, Sprite>();
        readonly List<Texture2D> extraTex = new List<Texture2D>(); readonly List<Ship> goneBuf = new List<Ship>(), orderBuf = new List<Ship>();
        readonly Dictionary<string, Bitmap> keeperSpr = new Dictionary<string, Bitmap>();
        SpriteRenderer keeperSr, keeperShadowSr; Bitmap keeperShadow;
        int indexTowerB = -1, indexTowerA = -1, backOrder, frontBase, keeperBackOrder;

        void Awake() { View = gameObject.GetComponent<WorldView>() ?? gameObject.AddComponent<WorldView>(); }
        void OnDestroy() { DropExtra(); }

        class ShipVis { public SpriteRenderer ShadowD, ShadowN, BodyD, BodyN, HelpD, HelpN, Lit; public GameObject Go; public int Lane; }

        // ================= 굽기 =================
        /// <summary>섬·탑·꾸미기·배경을 새로 굽고 올린다(단계·계절·외형·장식·화면 크기가 바뀔 때).</summary>
        public void Rebuild(GameLoop l, GameContext c, int devW, int devH, FitView.View view, Func<string, string> skinOf, List<(string slot, string item)> put)
        {
            var sw = Stopwatch.StartNew();
            loop = l; ctx = c; DevW = devW; View.SpriteMaterial = SpriteMat;
            Fit = new FitView(devW, devH, view); W = Fit.W; H = Fit.H; OY = Fit.OY;
            loop.SetView(W, H, OY);
            string Safe(string layer, string def, Func<string, bool> valid) { string id = skinOf(layer); return valid(id) ? id : def; }
            string tower = Safe("tower", "classic", id => Palettes.TowerSkins.Any(s => s.Id == id)), isle = Safe("isle", "classic", id => Palettes.IsleSkinSeason.ContainsKey(id));
            string decor = Safe("decor", "classic", id => new[] { "classic", "seaside", "festival", "dev" }.Contains(id));
            shipSkin = Safe("ship", "classic", id => ShipBaker.SkinIds.Contains(id));
            Sprites = SceneSprites.Build(W, H, c.CurSeason(), c.Stage, isle, decor, tower, gardenLv: c.EffLv("garden"), slotLv: c.LvlOf("garden"), put: put);
            var ops = SceneComposer.Ops(Sprites);
            // 암초(2단계부터): 그림은 정적 층, 물보라는 연출 층
            var reefD = PropBaker.Reef(); var reefN = Night.Of(reefD);
            if (c.Tier() >= 2)
            {
                int at = 1;
                foreach (var rf in Lanes.Reefs)
                    ops.Insert(at++, new DrawOp { D = reefD, N = reefN, X = JsMath.Round(W * rf[0]), Y = rf[1] + 1 + OY, Tag = "reef", AlphaD = k => k < 0.999 ? 1 : 0, AlphaN = k => k > 0.001 ? k : 0 });
            }
            ops[0] = new DrawOp { D = Sprites.BgD, N = null, X = 0, Y = 0, Tag = "bg", AlphaD = k => 1, AlphaN = k => 0 };       // 배경은 동적: 낮·밤 섞기를 CPU 가 한다
            bg = new CpuCanvas(W, H); bgMix = null; mixD = -1;
            ships.Clear(); spriteCache.Clear(); DropExtra(); keeperSr = null; keeperShadowSr = null;
            View.DynamicBg = true;
            View.Build(W, H, ops, loop.Dusk); shownDusk = loop.Dusk;
            View.ShowOnScreen(W, H, Fit.Scale, devW);
            indexTowerA = ops.FindIndex(o => o.Tag == "towerA"); indexTowerB = ops.FindIndex(o => o.Tag == "towerB");
            backOrder = View.OrderBefore("shore"); frontBase = View.OrderEnd; keeperBackOrder = View.OrderBefore("towerA");
            // 연출 층(맨 위)
            painter = new WorldPainter(W, H, OY); ovBuf = new uint[W * H];
            ovTex = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "overlay" }; extraTex.Add(ovTex);
            ovSprite = BitmapTexture.ToSprite(ovTex);
            var go = new GameObject("overlay"); go.transform.SetParent(View.Root, false);
            ovSr = go.AddComponent<SpriteRenderer>(); ovSr.sprite = ovSprite; ovSr.sharedMaterial = PremulMat; ovSr.sortingOrder = frontBase + 150; ovSr.color = Color.white;
            go.transform.position = Vector3.zero;
            LastBakeMs = sw.Elapsed.TotalMilliseconds;
        }

        void DropExtra() { foreach (var t in extraTex) if (t != null) Destroy(t); extraTex.Clear(); }

        // ================= 프레임 =================
        public void Frame()
        {
            if (loop == null || Sprites == null) return;
            var sw = Stopwatch.StartNew();
            double dusk = loop.Dusk;
            if (shownDusk != dusk) { View.SetDusk(dusk); shownDusk = dusk; }
            AnimateBg(loop.T, dusk);
            LastBgMs = sw.Elapsed.TotalMilliseconds;
            UpdateShips(dusk);
            UpdateKeeper();
            var sw2 = Stopwatch.StartNew();
            painter.Paint(loop, Sprites); painter.Merge(ovBuf, true);
            ovTex.SetPixelData(ovBuf, 0); ovTex.Apply(false, false);
            LastPaintMs = sw2.Elapsed.TotalMilliseconds;
            LastFrameMs = sw.Elapsed.TotalMilliseconds;
        }

        void AnimateBg(double t, double d)
        {
            if (bgMix == null || mixD != d)
            {
                if (bgMix == null || bgMix.W != bg.W || bgMix.H != bg.H) bgMix = new CpuCanvas(bg.W, bg.H); mixD = d;
                if (d < 0.999) bgMix.Copy(Sprites.BgD); else bgMix.Copy(Sprites.BgN);
                if (d > 0.001 && d < 0.999) bgMix.Put(Sprites.BgN, 0, 0, d);
            }
            bg.CopyFrom(bgMix);
            Fx.DrawSkyDeco(bg, W, OY, t, d); Fx.DrawSea(bg, W, OY, t, d);
            View.UpdateDynamicBg(bg);
        }

        // ================= 스프라이트 도우미 =================
        Sprite SpriteOf(Bitmap b)
        {
            if (spriteCache.TryGetValue(b, out var sp)) return sp;
            var tex = BitmapTexture.ToTexture(b, true); extraTex.Add(tex);
            return spriteCache[b] = BitmapTexture.ToSprite(tex);
        }

        SpriteRenderer MakeSr(Transform parent, string name, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sharedMaterial = m; return sr;
        }

        /// <summary>줄마다 사각형 한 줄(HTML ellipse())로 채운 그림자 비트맵. 색은 straight rgba.</summary>
        static Bitmap Ellipse(double rx, double ry, int r, int g, int b, int a)
        {
            int n = (int)Math.Max(1, JsMath.Round(ry)), hw = (int)Math.Ceiling(rx) + 2;
            var bm = new Bitmap(-hw, -n, hw * 2, n * 2);
            for (int i = -n; i < n; i++)
            {
                double v = (i + 0.5) / n, w = rx * Math.Sqrt(1 - v * v); int x0 = (int)JsMath.Round(0 - w), ww = (int)JsMath.Round(w * 2);
                for (int x = x0; x < x0 + ww; x++) { int bx = x + hw; if (bx >= 0 && bx < bm.W) bm.D[(i + n) * bm.W + bx] = Bitmap.Pack(r, g, b, a); }
            }
            return bm;
        }

        static void Place(SpriteRenderer sr, Bitmap b, double xt, double y, int dir)
        {
            // dir<0: x_t 를 축으로 좌우 반전(HTML ctx.scale(-1,1)): 위치 = x_t - ax, 스케일 x = -1
            var t = sr.transform;
            double px = dir >= 0 ? xt + b.Ax : xt - b.Ax;
            t.position = new Vector3((float)px, (float)-JsMath.Round(y + b.Ay), 0);
            t.localScale = new Vector3(dir >= 0 ? 1 : -1, 1, 1);
        }

        // ================= 배 =================
        (Bitmap d, Bitmap n) ShipBmp(string type, int lane) { string k = type + lane + shipSkin; if (!shipSpr.TryGetValue(k, out var v)) { var d = ShipBaker.Sprite(type, lane, shipSkin, false); shipSpr[k] = v = (d, Night.Of(d)); } return v; }
        (Bitmap d, Bitmap n) HelpBmp(string type, int lane) { string k = type + lane + shipSkin; if (!shipHelp.TryGetValue(k, out var v)) { var d = ShipBaker.Sprite(type, lane, shipSkin, true); shipHelp[k] = v = (d, Night.Of(d, 0.35)); } return v; }
        (Bitmap d, Bitmap n) ShadowBmp(string type, int lane)
        {
            string k = type + lane; if (shadows.TryGetValue(k, out var v)) return v;
            double L = Lanes.HalfLen(type) * Lanes.S[lane], ry = 2 * Lanes.S[lane] * Math.Max(0.5, Lighthouse.Render.Terrain.KY / 0.36);
            return shadows[k] = (Ellipse(L * 0.95, ry, 10, 50, 90, 77), Ellipse(L * 0.95, ry, 0, 6, 24, 102));
        }

        static double Alpha(bool dayLayer, double dusk, double k = 1) => dayLayer ? (dusk < 0.999 ? k : 0) : (dusk > 0.001 ? k * dusk : 0);

        void UpdateShips(double dusk)
        {
            goneBuf.Clear(); foreach (var k in ships.Keys) if (!loop.Ships.Contains(k)) goneBuf.Add(k); var gone = goneBuf;
            foreach (var s in gone) { Destroy(ships[s].Go); ships.Remove(s); }
            var parent = View.Root; double t = loop.T; bool night = loop.Ctx.Night;
            orderBuf.Clear(); orderBuf.AddRange(loop.Ships); orderBuf.Sort((a, b) => { int c = Lanes.Y[a.Lane].CompareTo(Lanes.Y[b.Lane]); return c != 0 ? c : a.Id.CompareTo(b.Id); }); var ordered = orderBuf;
            int back = 0, front = 0;
            foreach (var sh in ordered)
            {
                if (!ships.TryGetValue(sh, out var v))
                {
                    v = new ShipVis { Lane = sh.Lane, Go = new GameObject("ship" + sh.Id) }; v.Go.transform.SetParent(parent, false);
                    var t0 = v.Go.transform;
                    v.ShadowD = MakeSr(t0, "shadowD", SpriteMat); v.ShadowN = MakeSr(t0, "shadowN", SpriteMat);
                    v.BodyD = MakeSr(t0, "bodyD", SpriteMat); v.BodyN = MakeSr(t0, "bodyN", SpriteMat); v.HelpD = MakeSr(t0, "helpD", SpriteMat); v.HelpN = MakeSr(t0, "helpN", SpriteMat); v.Lit = MakeSr(t0, "lit", SpriteMat);
                    ships[sh] = v;
                }
                double ly = Lanes.Y[sh.Lane] + sh.Yo, ls = Lanes.S[sh.Lane]; bool isBack = Lanes.Y[sh.Lane] < Lighthouse.Render.Terrain.CY - 20;
                int baseOrder = isBack ? backOrder + (back++) * 10 : frontBase + (front++) * 10;
                var sb = ShipBmp(sh.Type, sh.Lane); var hb = HelpBmp(sh.Type, sh.Lane); var sd = ShadowBmp(sh.Type, sh.Lane);
                double xt = JsMath.Round(sh.X), bob = JsMath.Round(Math.Sin(t * 1.1 + sh.Ph) * 0.8 * ls), y = ly + bob + OY;
                // 그림자: 반전 없이 (x_t + 1, 항로 y + 1.8) 둘레
                SetSr(v.ShadowD, sd.d, baseOrder + 0, Alpha(true, dusk)); SetSr(v.ShadowN, sd.n, baseOrder + 1, Alpha(false, dusk));
                PlaceShadow(v.ShadowD, sd.d, xt + 1, JsMath.Round(ly + 1.8) + OY); PlaceShadow(v.ShadowN, sd.n, xt + 1, JsMath.Round(ly + 1.8) + OY);
                SetSr(v.BodyD, sb.d, baseOrder + 2, Alpha(true, dusk)); SetSr(v.BodyN, sb.n, baseOrder + 3, Alpha(false, dusk));
                Place(v.BodyD, sb.d, xt, y, sh.Dir); Place(v.BodyN, sb.n, xt, y, sh.Dir);
                double hk = sh.Done ? Math.Min(1, 0.3 + sh.DoneAge / 1.2) : Math.Min(0.85, sh.Guide / (sh.Need != 0 ? sh.Need : 1));       // 안내가 쌓일수록 돛이 금빛으로 물든다
                bool help = hk > 0.02;
                SetSr(v.HelpD, hb.d, baseOrder + 4, help ? Alpha(true, dusk, hk) : 0); SetSr(v.HelpN, hb.n, baseOrder + 5, help ? Alpha(false, dusk, hk) : 0);
                if (help) { Place(v.HelpD, hb.d, xt, y, sh.Dir); Place(v.HelpN, hb.n, xt, y, sh.Dir); }
                if (night) { double target = loop.BeamHits(loop.BeamTh, sh.X, Lanes.Y[sh.Lane]); sh.Lit += (target - sh.Lit) * 0.12; }      // 밤에는 빛줄기가 닿은 배가 밝아진다
                SetSr(v.Lit, sb.d, baseOrder + 6, night && sh.Lit > 0.02 ? sh.Lit : 0);
                if (night && sh.Lit > 0.02) Place(v.Lit, sb.d, xt, y, sh.Dir);
            }
        }

        void SetSr(SpriteRenderer sr, Bitmap b, int order, double alpha)
        {
            sr.sprite = SpriteOf(b); sr.sortingOrder = order; sr.enabled = alpha > 0; sr.color = new Color(1, 1, 1, (float)alpha);
        }

        static void PlaceShadow(SpriteRenderer sr, Bitmap b, double cx, double cy) { sr.transform.position = new Vector3((float)(cx + b.Ax), (float)-(cy + b.Ay), 0); }

        // ================= 관리인 =================
        Bitmap KeeperBmp(int dir, int frame)
        {
            string k = dir + ":" + frame; if (keeperSpr.TryGetValue(k, out var b)) return b;
            string[] head = { "..cccc..", ".cccccc.", "..ssss..", "..ssse..", "..ssss..", ".bbbbbb.", "BbbbbbbB", "BbbbbbbB", "sbbbbbbs", ".bbbbbb.", "..pppp.." };
            string[][] legs = { new[] { "..pp.pp.", "..pp.pp.", "..dd.dd." }, new[] { "..pppp..", "..pppp..", "..dddd.." } };
            var rows = head.Concat(legs[frame]).Select(r => dir > 0 ? r : new string(r.Reverse().ToArray())).ToArray();
            var pal = new Dictionary<char, string> { { 'c', "#3d5a8a" }, { 's', "#f0c8a0" }, { 'b', "#e8b84a" }, { 'p', "#2c3a58" }, { 'd', "#20140c" }, { 'e', "#20140c" } };
            var bc = Col.Hex("#e8b84a"); pal['B'] = string.Format("#{0:x2}{1:x2}{2:x2}", (int)JsMath.Round(bc.R * 0.78), (int)JsMath.Round(bc.G * 0.78), (int)JsMath.Round(bc.B * 0.78));
            return keeperSpr[k] = ShipBaker.FromDesign(new ShipDesign { Ox = 4, Oy = 14, Rows = rows }, pal);
        }

        void UpdateKeeper()
        {
            if (keeperSr == null)
            {
                keeperSr = MakeSr(View.Root, "keeper", SpriteMat); keeperShadowSr = MakeSr(View.Root, "keeperShadow", SpriteMat);
                keeperShadow = Ellipse(4, 1.4, 0, 0, 0, 71);
            }
            var k = loop.Keeper; bool hidden = k.Act == "enter" && !k.Walking && k.WaveT <= 0;
            keeperSr.enabled = keeperShadowSr.enabled = !hidden; if (hidden) return;
            var kp = loop.KeeperPos(); bool sit = k.Act == "sit" && !k.Walking && k.WaveT <= 0; double y0 = kp.y, y = sit ? y0 + 3 : y0;
            int frame = k.Walking ? (int)(Math.Floor(loop.T * 5) % 2) : 0;
            var bm = KeeperBmp(k.Dir, frame);
            keeperSr.sprite = SpriteOf(bm); keeperSr.color = Color.white;
            keeperSr.transform.position = new Vector3((float)(kp.x - 4), (float)-(y - 14 + OY), 0); keeperSr.transform.localScale = Vector3.one;
            keeperShadowSr.sprite = SpriteOf(keeperShadow); keeperShadowSr.color = Color.white;
            keeperShadowSr.transform.position = new Vector3((float)(kp.x + keeperShadow.Ax), (float)-(y0 + OY + keeperShadow.Ay), 0);
            // 정렬: 탑 뒤(v < -8)면 탑 앞에서, 아니면 B 패스에서 v 순서에 맞는 자리(원본 drawScene 의 kfn)
            int order;
            if (k.V < -8) order = keeperBackOrder; else order = KeeperFrontOrder(k.V);
            keeperShadowSr.sortingOrder = order; keeperSr.sortingOrder = order + 1;
        }

        int KeeperFrontOrder(double kv)
        {
            var B = Sprites.Lists.B; int first = indexTowerB + 1;
            for (int i = 0; i < B.Count; i++)
            {
                var e = B[i];
                if (!e.Flat && (e.K == 'p' ? e.V - 3 : e.V) > kv) return View.OrderOfIndex(first + i) - 50;
            }
            return View.OrderOfIndex(first + B.Count) - 50;           // 마지막 B 항목 뒤(= S 패스 앞)
        }

        // ================= 좌표 변환 =================
        /// <summary>화면 픽셀(왼쪽 아래 원점) → 설계 좌표.</summary>
        public Vector2 ToDesign(Vector2 px)
        {
            double scale = Fit.Scale, offX = Math.Floor((W * scale - DevW) / 2.0);
            double x = (px.x + offX) / scale, y = (DevH - px.y) / scale - OY;        // 화면 아래가 RT 아래
            return new Vector2((float)x, (float)y);
        }
        public int DevH => (int)Fit.DevH;

        /// <summary>설계 좌표 → UI 캔버스 단위(왼쪽 위 원점).</summary>
        public Vector2 ToCanvas(double x, double y, float uiScale)
        {
            double scale = Fit.Scale, offX = Math.Floor((W * scale - DevW) / 2.0);
            double px = x * scale - offX, pyTop = (y + OY) * scale - (H * scale - DevH);
            return new Vector2((float)(px / uiScale), (float)(pyTop / uiScale));
        }

        /// <summary>월드 한 장(연출 포함, UI 제외)을 정수 배율로 키운 PNG 바이트.</summary>
        public byte[] CapturePng(int mult)
        {
            var big = new RenderTexture(W * mult, H * mult, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            View.WorldCam.Render();
            Graphics.Blit(View.Rt, big);
            var prev = RenderTexture.active; RenderTexture.active = big;
            var tex = new Texture2D(big.width, big.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, big.width, big.height), 0, 0); tex.Apply();
            RenderTexture.active = prev; big.Release(); Destroy(big);
            var png = ImageConversion.EncodeToPNG(tex); Destroy(tex); return png;
        }
    }
}
