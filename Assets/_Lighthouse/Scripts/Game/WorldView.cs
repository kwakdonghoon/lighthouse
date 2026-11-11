using System.Collections.Generic;
using Lighthouse.Core;
using Lighthouse.Render;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.Game
{
    /// <summary>
    /// M2 월드 렌더: 장면의 그리기 명령(SceneComposer.Ops)을 SpriteRenderer(포인트 필터, PPU=1)로 올려 저해상 RenderTexture(W×H)에 직교 카메라로 그린다.
    /// 낮·밤은 HTML putDN 과 같이 낮 그림 위에 밤 그림을 dusk 알파로 얹는다(별도 셰이더 없이 스프라이트 색 알파). 정렬은 목록 순서 = sortingOrder.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        public Material SpriteMaterial;
        public RenderTexture Rt { get; private set; }
        public Camera WorldCam { get; private set; }
        public long TextureBytes { get; private set; }
        public int SpriteCount { get; private set; }

        GameObject root;
        readonly List<(SpriteRenderer d, SpriteRenderer n, DrawOp op)> layers = new List<(SpriteRenderer, SpriteRenderer, DrawOp)>();
        readonly Dictionary<Bitmap, Sprite> sprites = new Dictionary<Bitmap, Sprite>();
        readonly List<Texture2D> textures = new List<Texture2D>();

        void OnDestroy() => Clear();

        public void Clear()
        {
            layers.Clear(); sprites.Clear();
            foreach (var t in textures) if (t != null) { if (Application.isPlaying) Destroy(t); else DestroyImmediate(t); }
            textures.Clear(); TextureBytes = 0; SpriteCount = 0;
            if (root != null) { if (Application.isPlaying) Destroy(root); else DestroyImmediate(root); root = null; }
            if (Rt != null) { Rt.Release(); if (Application.isPlaying) Destroy(Rt); else DestroyImmediate(Rt); Rt = null; }
        }

        Sprite SpriteOf(Bitmap b)
        {
            if (sprites.TryGetValue(b, out var sp)) return sp;
            var tex = BitmapTexture.ToTexture(b); textures.Add(tex);
            TextureBytes += (long)b.W * b.H * 4;
            return sprites[b] = BitmapTexture.ToSprite(tex);
        }

        SpriteRenderer MakeRenderer(Bitmap b, DrawOp op, int order)
        {
            var go = new GameObject("spr:" + op.Tag); go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteOf(b); sr.sharedMaterial = SpriteMaterial; sr.sortingOrder = order;
            // HTML put(): drawImage(round(x + ax), round(y + ay)) — 위로 갈수록 y 가 줄어드는 유니티 좌표로 (y 부호 반전)
            go.transform.position = new Vector3((float)JsMath.Round(op.X + b.Ax), (float)-JsMath.Round(op.Y + b.Ay), 0);
            SpriteCount++;
            return sr;
        }

        /// <summary>논리 크기 W×H 의 RT 를 만들고 명령 목록을 올린다. 화면에 키워 보이는 일은 ShowOnScreen.</summary>
        public void Build(int W, int H, IList<DrawOp> ops, double dusk)
        {
            Clear();
            root = new GameObject("WorldView") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            int order = 0;
            foreach (var op in ops)
            {
                var d = MakeRenderer(op.D, op, order++);
                var n = op.N != null ? MakeRenderer(op.N, op, order++) : null;
                layers.Add((d, n, op));
            }
            SetDusk(dusk);

            Rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "LogicalRT", useMipMap = false };
            Rt.Create();
            var camGo = new GameObject("WorldCam"); camGo.transform.SetParent(root.transform, false);
            WorldCam = camGo.AddComponent<Camera>();
            WorldCam.orthographic = true; WorldCam.orthographicSize = H / 2f;
            WorldCam.transform.position = new Vector3(W / 2f, -H / 2f, -10f);
            WorldCam.clearFlags = CameraClearFlags.SolidColor; WorldCam.backgroundColor = Color.black;
            WorldCam.targetTexture = Rt; WorldCam.depth = -10;
        }

        /// <summary>dusk(0 낮 ~ 1 밤)에 맞춰 낮·밤 층의 투명도를 바꾼다 (굽기는 다시 하지 않는다).</summary>
        public void SetDusk(double dusk)
        {
            foreach (var (d, n, op) in layers)
            {
                double ad = op.AlphaD(dusk);
                d.enabled = ad > 0; d.color = new Color(1, 1, 1, (float)ad);
                if (n != null) { double an = op.AlphaN(dusk); n.enabled = an > 0; n.color = new Color(1, 1, 1, (float)an); }
            }
        }

        /// <summary>한 번 그려 ARGB 픽셀(위 행부터)을 돌려준다 — 골든 대조용.</summary>
        public Bitmap Capture()
        {
            WorldCam.Render(); WorldCam.Render();                          // 첫 렌더는 셰이더·2D 렌더러 준비로 일부 층이 빠질 수 있어 한 번 더 (배치 캡처에서 실측)
            var prev = RenderTexture.active; RenderTexture.active = Rt;
            var tex = new Texture2D(Rt.width, Rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Rt.width, Rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            var bm = new Bitmap(0, 0, Rt.width, Rt.height);
            for (int y = 0; y < bm.H; y++)
                for (int x = 0; x < bm.W; x++)
                {
                    var c = px[(bm.H - 1 - y) * bm.W + x];
                    bm.D[y * bm.W + x] = Bitmap.Pack(c.r, c.g, c.b, c.a);
                }
            DestroyImmediate(tex);
            return bm;
        }

        /// <summary>화면: 오버레이 캔버스에 RT 를 정수 배율로 (왼쪽 아래 기준, 정수 픽셀 오프셋).</summary>
        public void ShowOnScreen(LogicalScreen ls)
        {
            var cvGo = new GameObject("Screen", typeof(RectTransform)); cvGo.transform.SetParent(root.transform, false);
            var cv = cvGo.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay;
            var cs = cvGo.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = 1f;
            var imGo = new GameObject("World", typeof(RectTransform)); imGo.transform.SetParent(cvGo.transform, false);
            var raw = imGo.AddComponent<RawImage>(); raw.texture = Rt; raw.raycastTarget = false;
            var rt = (RectTransform)imGo.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(-Mathf.Floor((ls.W * ls.Scale - ls.DevW) / 2f), 0);
            rt.sizeDelta = new Vector2(ls.W * ls.Scale, ls.H * ls.Scale);
        }
    }
}
