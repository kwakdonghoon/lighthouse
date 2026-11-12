using System;
using Lighthouse.Audio;
using Lighthouse.Core;
using Lighthouse.UI;
using Lighthouse.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Lighthouse.App
{
    /// <summary>
    /// M6: 게임(GameLoop·UiRoot·GameContext) ↔ 소리 엔진(Audio) 연결. GameApp 은 Boot 끝에서 AudioBridge.Attach(this) 한 줄만 부른다.
    ///  - GameLoop.Sfx·UiRoot.SfxRequested 의 소리 요청을 실제로 재생한다(M4 가 기록만 하던 것).
    ///  - 매 프레임 장면(낮밤·계절·날씨·단계·공사·광고·소리 켜기)을 엔진에 알린다 → 파도·바람·비·음악·갈매기·귀뚜라미·안개고동·망치 소리가 원본 규칙대로 변한다.
    ///  - 소리 설정은 저장(GameState.Sound)이 기준이다. 설정 화면·머리줄의 소리 단추는 이미 그 값을 바꾼다(PanelController "snd:on|off") → 여기서 읽기만 한다. 켜는 순간 50ms 뒤 tick.
    ///  - 단추를 누르면 'click'(직전 80ms 안에 다른 소리가 났으면 생략).
    ///  - 배치 모드(시험)에서는 만들지 않는다(LH_AUDIO=1 이면 만든다) → 기존 PlayMode 시험이 소리 장치·CPU 에 영향받지 않는다.
    /// </summary>
    public sealed class AudioBridge : MonoBehaviour
    {
        public static AudioBridge Instance { get; private set; }
        SynthBehaviour synth; GameContext ctx; GameLoop loop; UiRoot ui; readonly SfxGate gate = new SfxGate();
        AudioScene last; bool lastValid;
        /// <summary>시험·진단용: 요청된 소리와 실제 재생된 소리 수.</summary>
        public int Requested, Played;

        public static AudioBridge Attach(GameApp app)
        {
            if (Application.isBatchMode && Environment.GetEnvironmentVariable("LH_AUDIO") != "1") return null;
            if (Instance == null)
            {
                var go = new GameObject("AudioBridge"); DontDestroyOnLoad(go); Instance = go.AddComponent<AudioBridge>();
            }
            Instance.Bind(app.Ctx, app.Loop, app.Ui); return Instance;
        }

        void Bind(GameContext c, GameLoop l, UiRoot u)
        {
            if (loop != null) loop.Sfx -= PlayFromWorld; if (ui != null) ui.SfxRequested -= Play;
            ctx = c; loop = l; ui = u; loop.Sfx += PlayFromWorld; ui.SfxRequested += Play; lastValid = false; fanfareAt = -1;
            if (synth == null) synth = SynthBehaviour.Create(() => new SystemRng());
        }

        void OnDestroy() { if (loop != null) loop.Sfx -= PlayFromWorld; if (ui != null) ui.SfxRequested -= Play; if (Instance == this) Instance = null; }

        bool AdOpen() { return ui != null && ui.Ctl != null && ui.Ctl.AdShowing; }

        double fanfareAt = -1;
        /// <summary>월드(GameLoop) 쪽 요청: 공사 완료 'build' 는 0.52초 뒤 'fanfare' 가 따라온다(원본 3190행 setTimeout). 타이틀 인트로의 build 는 UI 경로라 따라오지 않는다.</summary>
        void PlayFromWorld(string name)
        {
            if (name == "build") fanfareAt = Time.unscaledTimeAsDouble + 0.52;
            Play(name);
        }

        /// <summary>효과음 요청(원본 sfx(name)).</summary>
        public void Play(string name)
        {
            Requested++;
            if (ctx == null || synth == null) return;
            if (!gate.Allow(name, Time.unscaledTimeAsDouble, ctx.S.Sound, AdOpen())) return;
            if (!synth.Ready) return;
            synth.Sfx(name); Played++;
        }

        static int SeasonOf(string s) { return s == "spring" ? 0 : s == "summer" ? 1 : s == "autumn" ? 2 : 3; }

        void Update()
        {
            if (ctx == null || loop == null || synth == null) return;
            var s = new AudioScene
            {
                SoundOn = ctx.S.Sound, AdOpen = AdOpen(), Building = ctx.S.Build != null, Dusk = loop.Dusk, Season = SeasonOf(ctx.CurSeason()),
                Wx = loop.Wx.Kind == "rain" ? 2 : loop.Wx.Kind == "fog" ? 1 : 0, WxAmt = loop.Wx.Amt, Stage = ctx.Stage
            };
            if (!lastValid || !s.Equals2(last)) { synth.SetScene(s); last = s; lastValid = true; }
            if (gate.PollEnableTick(s.SoundOn, Time.unscaledTimeAsDouble)) { synth.Sfx("tick"); Played++; }
            if (fanfareAt >= 0 && Time.unscaledTimeAsDouble >= fanfareAt) { fanfareAt = -1; Play("fanfare"); }
            if (Input.GetMouseButtonUp(0) && PressedButton()) Play("click");
        }

        static readonly System.Collections.Generic.List<RaycastResult> hits = new System.Collections.Generic.List<RaycastResult>();
        static bool PressedButton()
        {
            var es = EventSystem.current; if (es == null) return false;
            var pd = new PointerEventData(es) { position = Input.mousePosition }; hits.Clear(); es.RaycastAll(pd, hits);
            if (hits.Count == 0) return false;
            var b = hits[0].gameObject.GetComponentInParent<Button>();
            return b != null && b.interactable && b.isActiveAndEnabled;
        }
    }

    static class AudioSceneExt
    {
        public static bool Equals2(this AudioScene a, AudioScene b)
        {
            return a.SoundOn == b.SoundOn && a.AdOpen == b.AdOpen && a.Building == b.Building && a.Season == b.Season && a.Wx == b.Wx && a.Stage == b.Stage
                && Math.Abs(a.Dusk - b.Dusk) < 0.01 && Math.Abs(a.WxAmt - b.WxAmt) < 0.01;
        }
    }
}
