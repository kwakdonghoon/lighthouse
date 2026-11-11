using System.Collections.Generic;
using System.Diagnostics;
using Lighthouse.Core;
using Lighthouse.Render;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.Game
{
    /// <summary>
    /// M0 한 장짜리 화면: 섬 + 풍경 조각 + 탑 A/B 를 굽고(Bitmap) Texture2D 로 올려
    /// 저해상 RenderTexture(W×H) 에 직교 카메라(PPU=1)로 그린 뒤 정수 배율(s)로 화면에 키운다. (설계서 2-3)
    /// 하늘·바다는 makeBg(베이어 디더)가 아직 없어 7색 띠로 대신한 임시 배경이다.
    /// </summary>
    public sealed class M0Preview : MonoBehaviour
    {
        public int Stage = 1;
        public int T1V = 2;
        public Material SpriteMaterial;               // URP 2D Sprite-Unlit (조명 없이 그리는 재질)

        public sealed class Stats
        {
            public double IslandMs, TowerMs, UploadMs;
            public long TextureBytes, RtBytes;
            public int Sprites;
            public LogicalScreen Screen;
        }

        public Stats Last { get; private set; }
        public RenderTexture Rt { get; private set; }
        public Camera WorldCam { get; private set; }
        GameObject root;

        const double CY = 224, HORIZON = 150;
        static readonly string[] SkyD = { "#3f86c8", "#5b9ad4", "#78b0de", "#98c6e8", "#b8d9ee", "#d8ebf2", "#f6efd8" };
        static readonly string[] SeaD = { "#9ccfe4", "#7ebfdc", "#5aa8cf", "#3d92c0", "#2b7bb0", "#1f69a0", "#195a8e" };

        void Start() => Build(UnityEngine.Screen.width, UnityEngine.Screen.height);

        void OnDestroy() => Clear();

        public void Clear()
        {
            if (root != null) { if (Application.isPlaying) Destroy(root); else DestroyImmediate(root); root = null; }
            if (Rt != null) { Rt.Release(); if (Application.isPlaying) Destroy(Rt); else DestroyImmediate(Rt); Rt = null; }
        }

        public Stats Build(int devW, int devH)
        {
            Clear();
            var st = new Stats { Screen = new LogicalScreen(devW, devH) };
            var ls = st.Screen;
            var sw = Stopwatch.StartNew();
            var isl = new IslandBaker(new IslandEnv { Stage = Stage }).Bake();
            st.IslandMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
            var P = TowerParams.ForStage(Stage, T1V);
            var ta = TowerBaker.BakeA(P); var tb = TowerBaker.BakeB(P);
            st.TowerMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

            root = new GameObject("M0World") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            int order = 0;
            double isleY = 10;                                         // ISLE_Y = GROUND = GZ*VZ
            double X = ls.W / 2.0, cy = CY + ls.OY;

            // 임시 배경: 하늘 7띠(맨 위 ~ 수평선), 바다 7띠(수평선 ~ 맨 아래)
            double hor = HORIZON + ls.OY;
            for (int i = 0; i < 7; i++) FillRect(0, hor * i / 7.0, ls.W, hor / 7.0 + 0.001, Col.Hex(SkyD[i]), order++);
            for (int i = 0; i < 7; i++) FillRect(0, hor + (ls.H - hor) * i / 7.0, ls.W, (ls.H - hor) / 7.0 + 0.001, Col.Hex(SeaD[i]), order++);

            Place(isl.Island, X, cy + isleY, ref order, st);                          // 지면·길·테두리·물속 바위
            int pi = 0;
            for (; pi < isl.Pieces.Count && isl.Pieces[pi].V < -2; pi++) Place(isl.Pieces[pi].Spr, X, cy + isleY, ref order, st);      // 탑 뒤(A 패스)
            Place(ta, X, cy, ref order, st);
            Place(tb, X, cy, ref order, st);
            for (; pi < isl.Pieces.Count; pi++) Place(isl.Pieces[pi].Spr, X, cy + isleY, ref order, st);                                // 탑 앞(B 패스)
            st.UploadMs = sw.Elapsed.TotalMilliseconds;

            Rt = new RenderTexture(ls.W, ls.H, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "LogicalRT", useMipMap = false };
            Rt.Create();
            st.RtBytes = (long)ls.W * ls.H * 4;

            var camGo = new GameObject("WorldCam"); camGo.transform.SetParent(root.transform, false);
            WorldCam = camGo.AddComponent<Camera>();
            WorldCam.orthographic = true; WorldCam.orthographicSize = ls.H / 2f;
            WorldCam.transform.position = new Vector3(ls.W / 2f, -ls.H / 2f, -10f);
            WorldCam.clearFlags = CameraClearFlags.SolidColor; WorldCam.backgroundColor = Color.black;
            WorldCam.targetTexture = Rt; WorldCam.depth = -10;

            // 화면: 오버레이 캔버스 위에 RT 를 정수 배율(s)로 (논리 1칸 = 기기 픽셀 s 개), 아래 정렬
            var cvGo = new GameObject("Screen", typeof(RectTransform)); cvGo.transform.SetParent(root.transform, false);
            var cv = cvGo.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay;
            var cs = cvGo.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = 1f;
            var imGo = new GameObject("World", typeof(RectTransform)); imGo.transform.SetParent(cvGo.transform, false);
            var raw = imGo.AddComponent<RawImage>(); raw.texture = Rt; raw.raycastTarget = false;
            var rt = (RectTransform)imGo.transform;
            // 왼쪽 아래 기준 + 정수 기기 픽셀 오프셋: 폭 차이가 홀수여도 반 픽셀 어긋나(번짐) 않도록 floor 한다
            rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(-Mathf.Floor((ls.W * ls.Scale - ls.DevW) / 2f), 0);
            rt.sizeDelta = new Vector2(ls.W * ls.Scale, ls.H * ls.Scale);

            Last = st;
            return st;
        }

        void Place(Bitmap b, double x, double y, ref int order, Stats st)
        {
            var tex = BitmapTexture.ToTexture(b);
            st.TextureBytes += (long)b.W * b.H * 4; st.Sprites++;
            var go = new GameObject("spr"); go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BitmapTexture.ToSprite(tex); sr.sharedMaterial = SpriteMaterial; sr.sortingOrder = order++;
            go.transform.position = new Vector3((float)(x + b.Ax), (float)(-(y + b.Ay)), 0);
        }

        static Texture2D whiteTex;
        void FillRect(double x, double y, double w, double h, Col c, int order)
        {
            if (whiteTex == null)
            {
                whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                whiteTex.SetPixel(0, 0, Color.white); whiteTex.Apply();
            }
            var go = new GameObject("bg"); go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(whiteTex, new Rect(0, 0, 1, 1), new Vector2(0, 1), 1f);
            sr.sharedMaterial = SpriteMaterial; sr.sortingOrder = order;
            sr.color = new Color32((byte)c.R, (byte)c.G, (byte)c.B, 255);
            go.transform.position = new Vector3((float)x, (float)-y, 0);
            go.transform.localScale = new Vector3((float)w, (float)h, 1);
        }
    }
}
