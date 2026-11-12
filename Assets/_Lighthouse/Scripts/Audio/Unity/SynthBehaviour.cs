using System;
using System.Threading.Tasks;
using Lighthouse.Core;
using UnityEngine;

namespace Lighthouse.Audio
{
    /// <summary>
    /// Unity 오디오 출력 어댑터: 순수 C# SynthEngine 을 OnAudioFilterRead 로 스트리밍한다(에셋·AudioClip 없음 → APK 0바이트, 메모리 약 10MB).
    /// ① 엔진 만들기(소음·울림 응답 약 150만 난수 + FFT 준비)는 백그라운드 스레드에서 한다 → 첫 프레임이 멈추지 않는다. 끝나면 재생을 시작한다.
    /// ② 오디오 스레드는 OnAudioFilterRead 한 곳, 게임은 Engine.Sfx/SetScene 만 부른다(잠금 짧음).
    /// ③ 앱이 가려지면(전화·알림·홈) Paused → 시간까지 멈추고 AudioListener.pause 로 플랫폼 오디오도 멈춘다.
    /// ④ 출력 장치가 바뀌어 샘플레이트가 달라지면(블루투스·이어폰) 새 엔진을 같은 방식으로 다시 만든다(높이가 틀어지지 않게).
    /// 무음 모드(iOS 무음 스위치): Unity 기본 세션 카테고리가 무음 스위치를 따른다 → 별도 코드 없음(실기기 확인 필요).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SynthBehaviour : MonoBehaviour
    {
        volatile SynthEngine engine;
        AudioSource src; AudioClip carrier; Task<SynthEngine> building; int building_sr; bool paused;
        public bool Ready { get { return engine != null; } }
        public SynthEngine Engine { get { return engine; } }
        public int SampleRate { get { return engine != null ? engine.Sr : 0; } }
        public double BuildMs { get; private set; }
        public static SynthBehaviour Instance { get; private set; }

        public static SynthBehaviour Create(Func<IRng> rngFactory)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("LighthouseAudio"); DontDestroyOnLoad(go);
            var b = go.AddComponent<SynthBehaviour>(); b.rngFactory = rngFactory; return b;
        }

        Func<IRng> rngFactory;

        void Awake()
        {
            Instance = this;
            src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.loop = true; src.spatialBlend = 0f; src.volume = 1f; src.bypassEffects = true; src.bypassListenerEffects = true; src.bypassReverbZones = true;
            AudioSettings.OnAudioConfigurationChanged += OnConfigChanged;
        }

        void Start() { StartBuild(); }

        void StartBuild()
        {
            int sr = AudioSettings.outputSampleRate; if (sr <= 0) sr = 44100;
            if (building != null && !building.IsCompleted) return;
            building_sr = sr; var f = rngFactory; var t0 = DateTime.UtcNow;
            building = Task.Run(() => { var e = new SynthEngine(sr, f()); e.Paused = paused; return e; });
        }

        void Update()
        {
            if (building != null && building.IsCompleted)
            {
                var t = building; building = null;
                if (t.IsFaulted) { Debug.LogWarning("소리 엔진 만들기 실패: " + t.Exception); return; }
                var e = t.Result; e.Paused = paused;
                var old = engine; if (old != null) e.SetScene(lastScene);          // 장치가 바뀌어 다시 만든 경우: 장면을 이어 받는다
                engine = e; EnsureCarrier(e.Sr); if (!src.isPlaying) src.Play();
            }
        }

        AudioScene lastScene = AudioScene.Default;
        public void SetScene(AudioScene s) { lastScene = s; var e = engine; if (e != null) e.SetScene(s); }
        public void Sfx(string name) { var e = engine; if (e != null) e.Sfx(name); }

        void EnsureCarrier(int sr)
        {
            // OnAudioFilterRead 는 재생 중인 AudioSource 에서만 불린다 → 무음 1초짜리 되풀이 클립을 실어 둔다(엔진이 버퍼를 통째로 덮어쓴다)
            if (carrier != null && carrier.frequency == sr) return;
            carrier = AudioClip.Create("lh_synth_carrier", sr, 1, sr, false); carrier.SetData(new float[sr], 0);
            bool was = src.isPlaying; src.Stop(); src.clip = carrier; src.loop = true; if (was) src.Play();
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var e = engine;
            if (e == null) { Array.Clear(data, 0, data.Length); return; }
            e.ProcessInterleaved(data, channels);
        }

        void OnConfigChanged(bool deviceWasChanged)
        {
            // 메인 스레드에서 불린다. 샘플레이트가 달라졌으면 엔진을 새로 만들고, 재생이 멈췄으면 다시 건다.
            var e = engine;
            if (e != null && AudioSettings.outputSampleRate != e.Sr) StartBuild();
            else if (src != null && engine != null && !src.isPlaying) src.Play();
        }

        public void SetPaused(bool p)
        {
            paused = p; var e = engine; if (e != null) e.Paused = p;
            AudioListener.pause = p;
        }

        void OnApplicationPause(bool p) { SetPaused(p); }
        void OnApplicationFocus(bool focus) { if (!focus) SetPaused(true); else SetPaused(false); }

        void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnConfigChanged;
            if (Instance == this) Instance = null;
            if (carrier != null) Destroy(carrier);
        }
    }
}
