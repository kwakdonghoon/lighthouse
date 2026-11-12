using System;
using Lighthouse.Render;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>
    /// 타이틀(원본 #title): 별빛 하늘 아래 어두운 바다 위의 블록 섬·줄무늬 등대·배, 맨 위에 금빛 블록 글자 "등대지기".
    /// 그림 전체(하늘·바다·글자·섬·등대·배·빛줄기)는 Render 의 TitleRenderer 가 CPU 로 구운 도트 그림 한 장이고(화면 크기에 맞춘 lw x lh),
    /// 여기서는 포인트 필터 Texture2D 로 올려 정수 배율(Sx)로 늘려 보인다. 위쪽 가운데 맞춤이라 화면보다 조금 크게 나가는 쪽은 잘린다(원본과 같다).
    /// 부제는 글자 바로 아래, "함께한 N번째 날" 줄은 아래에서 7% 위. 누르면 사라진다(UiRoot 의 7단 페이드). 인트로(첫 실행 9초)는 이식하지 않았다.
    /// </summary>
    public sealed class TitleView : MonoBehaviour
    {
        public Image Bg; public TMP_Text Name, Tagline, Day; public Button Tap; public CanvasGroup Group; public RectTransform Block; public RawImage Scene;
        public TitleRenderer Renderer; public Texture2D SceneTex;
        static TitleSprites sprites;
        /// <summary>첫 실행 인트로(약 9초 + 글자 연출, 원본 INTRO)를 보이는 중. 켜진 순간부터의 시각 t 로 글자·날짜 줄이 차례로 나타난다.</summary>
        public bool Intro { get; private set; }
        /// <summary>장면이 내는 소리 요청(블록이 내려앉는 stone·build·lamp). UiRoot 가 Sfx 로 넘긴다.</summary>
        public event Action<string> SfxRequested;
        /// <summary>true 이면 처음 그릴 때부터 섬 조각(인트로용)까지 굽는다(첫 실행이 곧 올 때 한 번만 굽게). 앱이 타이틀을 만들기 전에 정한다.</summary>
        public static bool WantPieces;
        float startAt; bool first; int lastW, lastH; uint[] flip; float scale = 1f, canvasW = 360f, canvasH = 640f;

        public static TitleView Create(Transform parent)
        {
            var root = Kit.Rect("Title", parent); Kit.Stretch(root); var t = root.gameObject.AddComponent<TitleView>();
            t.Bg = Kit.Solid(root, UiTheme.Hex("#050a1c"), "bg"); Kit.Stretch(t.Bg.rectTransform); t.Bg.raycastTarget = true;
            t.Group = root.gameObject.AddComponent<CanvasGroup>();
            t.Tap = t.Bg.gameObject.AddComponent<Button>(); t.Tap.targetGraphic = t.Bg; t.Tap.transition = Selectable.Transition.None;
            t.Block = Kit.Rect("block", root);
            t.Name = Kit.Text(t.Block, "", 1f, new Color(0, 0, 0, 0), true, TextAlignmentOptions.Center, "name");
            t.Tagline = Kit.Text(t.Block, "", 17f, new Color(0.96f, 0.94f, 0.82f, 0.9f), true, TextAlignmentOptions.Top, "tagline");
            t.Day = Kit.Text(root, "", 14f, new Color(0.87f, 0.89f, 0.96f, 0.62f), true, TextAlignmentOptions.Center, "day");
            var dr = t.Day.rectTransform; dr.anchorMin = new Vector2(0, 0); dr.anchorMax = new Vector2(1, 0); dr.pivot = new Vector2(0.5f, 0); dr.anchoredPosition = new Vector2(0, 56); dr.sizeDelta = new Vector2(-32, 30);
            t.EnsureParts();
            return t;
        }

        /// <summary>
        /// 장면 그림 칸·부제 자리를 갖춘다. 코드로 만들 때도, 예전에 저장된 프리팹(Resources/UI/Prefabs/Title·UiRoot: 글자 TMP 와 세로 배치만 있음)을 불러올 때도 같은 모양이 되게 한다
        /// (프리팹은 에디터 도구(M3Tools)로 다시 저장하면 이 단계가 할 일이 없어진다).
        /// </summary>
        void EnsureParts()
        {
            if (Scene == null)
            {
                var sr = Kit.Rect("scene", transform); Scene = sr.gameObject.AddComponent<RawImage>(); Scene.raycastTarget = false; Scene.color = Color.white;
                Kit.Anchor(sr, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(360, 640));
                sr.SetSiblingIndex(Bg != null ? Bg.transform.GetSiblingIndex() + 1 : 0);
            }
            if (Name != null) Name.gameObject.SetActive(false);                                                          // 글자는 그림으로 보인다(스크린 리더용 이름만 남김)
            var v = Block.GetComponent<VerticalLayoutGroup>(); if (v != null) { v.enabled = false; Destroy(v); }
            Kit.Anchor(Block, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(0, 40));
            var tr = Tagline.rectTransform; tr.SetParent(Block, false); Kit.Stretch(tr); Tagline.alignment = TextAlignmentOptions.Top; Tagline.characterSpacing = 4f; Tagline.textWrappingMode = TextWrappingModes.Normal;
            Tagline.color = new Color(0.96f, 0.94f, 0.82f, 0.85f); Tagline.raycastTarget = false; Day.raycastTarget = false;
            Day.color = new Color(0.87f, 0.89f, 0.96f, 0.62f); Day.alignment = TextAlignmentOptions.Center;
            Scene.transform.SetSiblingIndex(Bg != null ? Bg.transform.GetSiblingIndex() + 1 : 0);
        }

        /// <summary>움직임 줄이기(원본 prefers-reduced-motion): 켜져 있으면 인트로 없이 정지 장면 한 장(Draw(-1)). 값은 기기 설정에서 읽은 Core 의 MotionPref.</summary>
        public bool Calm => Lighthouse.Core.MotionPref.Reduce;

        public void Apply(UiStrings s, string dayText, bool firstRun = false, bool intro = false)
        {
            Name.text = UiText.Display(s.Get("title.name")); Tagline.text = UiText.Display(s.Get("title.tagline")); Day.text = UiText.Display(dayText); first = firstRun;
            Intro = intro && !Calm; startAt = Time.realtimeSinceStartup;
            if (lastW > 0) Build(lastW, lastH);
            if (Renderer != null) Renderer.Intro = Intro;
            SetTextAlpha(0f);
        }

        /// <summary>화면 크기(기기 픽셀)·배율(기기 픽셀/캔버스 단위)·안전 영역이 바뀔 때 부른다: 장면 크기·배율과 글자 아래 부제 위치를 다시 정한다.</summary>
        public void Layout(Insets sa, int devW, int devH, float uiScale)
        {
            EnsureParts(); scale = Mathf.Max(0.01f, uiScale); canvasW = devW / scale; canvasH = devH / scale;
            var dr = Day.rectTransform; dr.anchoredPosition = new Vector2(0, 0.07f * canvasH + sa.B);                    // 원본: 아래 여백 7vh + 안전 영역
            Tagline.fontSize = Mathf.Clamp(0.04f * canvasW, 14f, 18f);
            Build(devW, devH);
        }

        void Build(int devW, int devH)
        {
            if (devW <= 0 || devH <= 0) return;
            bool needPieces = Intro || WantPieces;
            if (sprites == null || (needPieces && sprites.Pieces == null)) { sprites = TitleSprites.Build(needPieces); Renderer = null; }       // 인트로는 섬 조각(pieces)도 굽는다
            if (Renderer == null || lastW != devW || lastH != devH)
            {
                lastW = devW; lastH = devH; Renderer = new TitleRenderer(sprites, devW, devH) { Intro = Intro }; Renderer.Sfx += n => SfxRequested?.Invoke(n);
                if (SceneTex != null) Destroy(SceneTex);
                SceneTex = new Texture2D(Renderer.Lw, Renderer.Lh, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "titleScene" };
                flip = new uint[Renderer.Lw * Renderer.Lh]; Scene.texture = SceneTex; Redraw(0);
            }
            float k = Renderer.Size.Sx / scale;                                                                           // 도트 한 칸 = Sx 기기 픽셀 = k 캔버스 단위
            Scene.rectTransform.sizeDelta = new Vector2(Renderer.Lw * k, Renderer.Lh * k);
            Block.anchoredPosition = new Vector2(0, -Renderer.SubtitleTop * k); Block.sizeDelta = new Vector2(0, 40);
        }

        void OnEnable() { startAt = Time.realtimeSinceStartup; }

        /// <summary>켜진 뒤 지난 초(장면 시각).</summary>
        public float Elapsed => Time.realtimeSinceStartup - startAt;
        public float? ForcedT;           // 시험·캡처: 장면 시각을 고정한다

        void Update()
        {
            if (Renderer == null) return;
            float t = ForcedT ?? Elapsed; Redraw(Calm ? -1f : t);
            SetTextAlpha(t);
        }

        /// <summary>부제·날짜 줄의 나타남(원본 CSS 5523~5533): 인트로는 부제 9.6초·날짜 10.8초에 0.4초 4단계로 나타나고, 아니면 처음부터 보인다. 첫 실행 날짜 줄은 2.6초 주기로 깜빡인다.</summary>
        public void SetTextAlpha(float t)
        {
            float ts = Intro ? IntroTiming.Steps(t, IntroTiming.TaglineAt) : 1f, td = Intro ? IntroTiming.Steps(t, IntroTiming.DayAt) : 1f;
            var c1 = Tagline.color; c1.a = 0.85f * ts; Tagline.color = c1;
            float a = 0.62f * td; if (first) a *= Mathf.Lerp(0.45f, 1f, 0.5f - 0.5f * Mathf.Cos(t / 2.6f * Mathf.PI * 2f)); var c = Day.color; c.a = a; Day.color = c;
        }

        /// <summary>자동으로 닫히는 시각(켜진 뒤 초, 원본 5603행): 인트로 15.6 · 첫 실행(인트로 없음) 6.4 · 그 밖 3.6.</summary>
        public float AutoCloseSec => IntroTiming.AutoCloseSec(Intro, first);

        /// <summary>t 초째 장면을 그려 올린다(테스트·캡처도 이 함수로).</summary>
        public void Redraw(float t)
        {
            if (Renderer == null || SceneTex == null) return;
            Renderer.Draw(t); int w = Renderer.Lw, h = Renderer.Lh;
            for (int y = 0; y < h; y++) Array.Copy(Renderer.Pixels, y * w, flip, (h - 1 - y) * w, w);                       // Bitmap 은 위 행부터, Texture2D 는 아래 행부터
            SceneTex.SetPixelData(flip, 0); SceneTex.Apply(false);
        }

        void OnDestroy() { if (SceneTex != null) Destroy(SceneTex); }
    }
}
