using System;
using System.Collections.Generic;
using Lighthouse.Core;

namespace Lighthouse.Audio
{
    /// <summary>게임이 소리 엔진에 알려 주는 장면 상태(원본 updateAudio 가 읽던 전역: dusk · wx · 계절 · STAGE · save.build · adOpen · save.sound).</summary>
    public struct AudioScene
    {
        public bool SoundOn, AdOpen, Building;
        public double Dusk;            // 0 낮 ~ 1 밤
        public int Season;             // 0 봄 1 여름 2 가을 3 겨울
        public int Wx;                 // 0 맑음 1 안개 2 비(겨울엔 눈)
        public double WxAmt;           // 날씨 세기 0~1
        public int Stage;              // 1~6
        public static AudioScene Default { get { return new AudioScene { SoundOn = true, Stage = 1 }; } }
    }

    /// <summary>
    /// HTML 의 WebAudio 합성(4588~4827행)을 그대로 옮긴 순수 C# 소리 엔진. UnityEngine 에 의존하지 않는다 → dotnet 으로 시험·오프라인 렌더(WAV) 가능.
    /// 그래프: 목소리들 → (dry 합 | 울림 버스) → 마스터 0.7 → 컴프레서 → 출력.  울림 버스는 그대로 마스터로 가고, 컨볼버(3.2초 응답)·wet 0.5 를 거쳐 한 번 더 간다.
    /// 환경음(파도·물살·거품·바람·비)은 6초 소음 되풀이 + 필터 + 세기 자동화로 실시간 합성, 음악(즉흥 피아노)·갈매기·귀뚜라미·안개고동·공사 망치는
    /// 200ms 마다 도는 감독(Director = 원본 updateAudio)이 정한다. 엔진은 128표본 덩어리(WebAudio 렌더 퀀텀)로 진행한다.
    /// 스레드: 공개 API(Sfx/SetScene/…)는 아무 스레드에서나 부르고, 렌더(Render/ProcessInterleaved)는 오디오 스레드 한 곳에서만 부른다.
    /// </summary>
    public sealed class SynthEngine
    {
        public const int Quantum = 128;
        /// <summary>true: 크롬 WebAudio 의 알려진 버릇을 흉내 낸다(오직 골든 대조용). ① 시각→표본 변환에서 부동소수 오차로 정수 경계를 1표본 넘김 ② 주파수에 setValueAtTime 가 있는 오실레이터는 시작 퀀텀에서 이벤트 이전 값(440Hz)으로 위상이 쌓임. 게임에서는 false.</summary>
        public static bool ChromiumQuirks = false;
        public readonly int Sr;
        public IRng Rng;                                         // 시험이 씨앗을 바꿀 수 있다 (렌더 전에만)
        public float[] White;                                    // 터짐 소리용 흰 소음 2초 (원본 snd.nb)
        public readonly float[] DryL = new float[Quantum], DryR = new float[Quantum], BusL = new float[Quantum], BusR = new float[Quantum];
        long clock;                                              // 렌더한 표본 수(= ac.currentTime * sr)
        public double Now { get { return clock / (double)Sr; } }
        public const double MasterGain = 0.7;

        readonly Convolver conv; readonly Compressor comp;
        readonly float[] wetL = new float[Quantum], wetR = new float[Quantum], outL = new float[Quantum], outR = new float[Quantum];
        int qpos = Quantum;
        readonly List<Voice> voices = new List<Voice>(64);
        readonly Pool<ToneVoice> tones = new Pool<ToneVoice>(48); readonly Pool<NoiseVoice> noises = new Pool<NoiseVoice>(24);
        readonly Pool<PianoVoice> pianos = new Pool<PianoVoice>(12); readonly Pool<PadVoice> pads = new Pool<PadVoice>(3); readonly Pool<GullVoice> gulls = new Pool<GullVoice>(4);
        AmbLayer waves, wash, foam, wind, rain;

        // 감독 상태(원본 snd.*)
        double nextNote, nextGull, nextCricket, nextCricket2, nextHammer, nextWave, nextFog, gust = 0.3, tickAcc; int phr;

        struct Cmd { public int Kind; public string Name; public double A; }
        readonly object gate = new object(); List<Cmd> cmds = new List<Cmd>(), work = new List<Cmd>(); AudioScene scene = AudioScene.Default, sceneNow = AudioScene.Default;
        public bool DirectorOn = true;                           // false: 환경음·음악·새소리 감독을 끈다(효과음 골든 시험용)
        public bool Paused;                                      // 앱이 가려진 동안(전화·백그라운드): 시간도 멈춘다

        /// <summary>진단·시험용 계수: 감독이 시작시킨 음악 구절·갈매기·귀뚜라미·안개고동·망치·빗방울 수와 마지막 구절의 음계(0 낮,1 밤,2 겨울,3 가을)·으뜸음.</summary>
        public int CntPhrase, CntGull, CntCricket, CntFog, CntHammer, CntRain, LastKey; public double LastRoot;

        double R() { return Rng.NextDouble(); }

        /// <summary>원본 initAudio() 의 난수 소비 순서: 울림 응답 → 흰 소음 → 분홍·갈색 소음 → 환경음 5겹의 시작 위치.</summary>
        public SynthEngine(int sampleRate, IRng rng)
        {
            Sr = sampleRate; Rng = rng; OscTables.Ensure();
            var ir = NoiseMaker.MakeIr(rng, 3.2, sampleRate);
            conv = new Convolver(ir, sampleRate);
            White = NoiseMaker.MakeWhite(rng, sampleRate * 2);
            var pink = NoiseMaker.MakeNoise(rng, false, 6, sampleRate); var brown = NoiseMaker.MakeNoise(rng, true, 6, sampleRate);
            waves = new AmbLayer(Sr, brown, BqType.Lowpass, 420, 0.3, 0.3, R() * 4);
            wash = new AmbLayer(Sr, pink, BqType.Bandpass, 900, 0.4, 0.25, R() * 4);
            foam = new AmbLayer(Sr, pink, BqType.Highpass, 2800, 0.5, 0.15, R() * 4);
            wind = new AmbLayer(Sr, pink, BqType.Bandpass, 380, 0.5, 1, R() * 4);
            rain = new AmbLayer(Sr, pink, BqType.Highpass, 2200, 0.4, 1, R() * 4);
            comp = new Compressor(sampleRate);
        }

        // ================= 공개 API (스레드 안전) =================
        public void Sfx(string name) { lock (gate) cmds.Add(new Cmd { Kind = 0, Name = name }); }
        public void MusicPhrase(double storm) { lock (gate) cmds.Add(new Cmd { Kind = 1, A = storm }); }
        public void SetScene(AudioScene s) { lock (gate) scene = s; }
        /// <summary>렌더 전에 직접 예약 함수(MusicPhraseNow 등)를 부르는 시험이 장면을 즉시 반영하는 데 쓴다.</summary>
        public void SyncScene() { lock (gate) sceneNow = scene; }
        public int ActiveVoices { get { return voices.Count; } }
        public double Clock { get { return Now; } }

        // ================= 렌더 =================
        /// <summary>frames 만큼 스테레오로 렌더한다(어떤 길이든 되며, 내부는 128표본 단위).</summary>
        public void Render(float[] left, float[] right, int offset, int frames)
        {
            int o = offset, need = frames;
            while (need > 0)
            {
                if (qpos >= Quantum) { RenderQuantum(); qpos = 0; }
                int take = Math.Min(need, Quantum - qpos);
                Array.Copy(outL, qpos, left, o, take); Array.Copy(outR, qpos, right, o, take);
                qpos += take; o += take; need -= take;
            }
        }

        float[] tmpL = new float[1024], tmpR = new float[1024];
        /// <summary>Unity OnAudioFilterRead 용: 인터리브 버퍼를 덮어쓴다(채널 1 은 좌우 평균, 3 이상은 앞 둘만).</summary>
        public void ProcessInterleaved(float[] data, int channels)
        {
            int frames = data.Length / channels;
            if (tmpL.Length < frames) { tmpL = new float[frames]; tmpR = new float[frames]; }
            Render(tmpL, tmpR, 0, frames);
            for (int i = 0; i < frames; i++)
            {
                if (channels == 1) data[i] = 0.5f * (tmpL[i] + tmpR[i]);
                else { data[i * channels] = tmpL[i]; data[i * channels + 1] = tmpR[i]; for (int c = 2; c < channels; c++) data[i * channels + c] = 0; }
            }
        }

        void RenderQuantum()
        {
            lock (gate) { var t = cmds; cmds = work; work = t; cmds.Clear(); sceneNow = scene; }
            bool frozen = Paused || !sceneNow.SoundOn || sceneNow.AdOpen;
            if (frozen)
            {
                for (int i = 0; i < work.Count; i++) { }           // 멈춘 동안의 소리 요청은 버린다(원본 sfx 의 가드)
                work.Clear(); Array.Clear(outL, 0, Quantum); Array.Clear(outR, 0, Quantum); return;
            }
            for (int i = 0; i < work.Count; i++)
            {
                var c = work[i];
                if (c.Kind == 0) DoSfx(c.Name); else if (c.Kind == 1) PhrasePlay(c.A);
            }
            work.Clear();

            if (tickAcc >= 0.2 - 1e-12) { tickAcc = 0; if (DirectorOn) Director(); }   // 원본: snd.tick += dt; if (< 0.2) return; snd.tick = 0  (렌더한 시간 기준)

            Array.Clear(DryL, 0, Quantum); Array.Clear(DryR, 0, Quantum); Array.Clear(BusL, 0, Quantum); Array.Clear(BusR, 0, Quantum);
            long b0 = clock;
            for (int v = voices.Count - 1; v >= 0; v--)
            {
                var vo = voices[v]; vo.Render(this, b0, Quantum);
                if (b0 + Quantum >= vo.Stop) { voices.RemoveAt(v); Release(vo); }
            }
            waves.Render(this, Quantum); wash.Render(this, Quantum); foam.Render(this, Quantum); wind.Render(this, Quantum); rain.Render(this, Quantum);
            conv.Process(BusL, BusR, wetL, wetR);
            for (int i = 0; i < Quantum; i++)
            {
                outL[i] = (float)((DryL[i] + BusL[i] + 0.5 * wetL[i]) * MasterGain);
                outR[i] = (float)((DryR[i] + BusR[i] + 0.5 * wetR[i]) * MasterGain);
            }
            comp.Process(outL, outR, Quantum);
            clock += Quantum; tickAcc += Quantum / (double)Sr;
        }

        void Release(Voice v)
        {
            if (v is ToneVoice) tones.Put((ToneVoice)v); else if (v is NoiseVoice) noises.Put((NoiseVoice)v);
            else if (v is PianoVoice) pianos.Put((PianoVoice)v); else if (v is PadVoice) pads.Put((PadVoice)v); else if (v is GullVoice) gulls.Put((GullVoice)v);
        }

        void Add(Voice v) { if (voices.Count < 220) voices.Add(v); else Release(v); }

        // ================= 합성 도구 (원본 tone / nz / piano / pad / bell / gullCall / cricket) =================
        public void Tone(double freq, double dur, OscType ty, double vol, double when = 0, double slideTo = 0, bool bus = false, double atk = 0.015)
        {
            double det = (R() - 0.5) * 10;                           // 소리마다 아주 조금씩 다른 높이
            var v = tones.Get(); v.Init(this, freq, dur, ty, vol, when, slideTo, bus, atk, det); Add(v);
        }

        public void Nz(double dur, double vol, BqType type = BqType.Lowpass, double freq = 800, double q = 0.7, double when = 0, double toFreq = 0, bool bus = false)
        {
            double off = R() * 1.5;
            var v = noises.Get(); v.Init(this, dur, vol, type, freq, q, when, toFreq, bus, off); Add(v);
        }

        public void Piano(double f, double dur, double vol, double when, double pan = double.NaN)
        {
            double pn = double.IsNaN(pan) ? Math.Max(-0.5, Math.Min(0.5, (f / 1000 - 0.45) * 0.5 + (R() - 0.5) * 0.3)) : pan;
            var v = pianos.Get(); v.Init(this, f, dur, vol, when, pn); Add(v);
            Nz(0.035, vol * 0.62 * 0.4, BqType.Lowpass, Math.Min(2500, f * 3), 0.8, when, 0, true);          // 건반이 닿는 아주 작은 톡
        }

        public void Pad(double f, double dur, double vol) { var v = pads.Get(); v.Init(this, f, dur, vol); Add(v); }

        static readonly double[][] BellParts = { new[] { 1, 1, 1.0 }, new[] { 2, 0.5, 0.8 }, new[] { 2.41, 0.35, 0.6 }, new[] { 3, 0.25, 0.5 }, new[] { 4.2, 0.12, 0.3 }, new[] { 5.4, 0.07, 0.2 } };
        public void Bell(double f, double vol, double dur)
        {
            foreach (var p in BellParts) Tone(f * p[0], dur * p[2], OscType.Sine, vol * p[1] * 0.6, 0, 0, true, 0.004);
        }

        public void GullCall(double vol, int n)
        {
            double bs = 1450 + R() * 450;
            for (int k = 0; k < n; k++)
            {
                double when = 0.02 + k * (0.36 + R() * 0.08), d = 0.32 - k * 0.03, p = bs * (1 - k * 0.06), lfo = 30 + R() * 12;
                var v = gulls.Get(); v.Init(this, when, d, p, lfo, vol); Add(v);
            }
        }

        public void Cricket(double f, double vol)
        {
            int n = 3 + (int)Math.Floor(R() * 2); double sp = 0.05 + R() * 0.014, ff = f * (0.97 + R() * 0.06);
            for (int i = 0; i < n; i++) { double v = vol * (0.75 + 0.25 * R()); Tone(ff, 0.04, OscType.Sine, v, i * sp, 0, false, 0.01); }
        }

        // ================= 효과음 30종 (원본 sfx 4707) =================
        public static readonly string[] SfxNames =
        {
            "tick", "click", "ding", "pick", "hello", "bell", "mail", "firework", "level", "fanfare", "hammer", "horn", "warn", "sos", "wood", "stone", "grass", "gravel",
            "plank", "place", "build", "door", "creak", "pull", "sit", "brush", "lamp", "pour", "bloop", "gull"
        };

        /// <summary>즉시 예약(오프라인 렌더·시험용). 게임은 Sfx(name) 을 쓴다.</summary>
        public void DoSfx(string name)
        {
            double dt = 1 + (R() - 0.5) * 0.05;                      // 누를 때마다 높이가 아주 조금씩 달라진다
            const OscType Si = OscType.Sine, Tr = OscType.Triangle, Sa = OscType.Sawtooth;
            const BqType Lp = BqType.Lowpass, Hp = BqType.Highpass, Bp = BqType.Bandpass;
            switch (name)
            {
                case "click": Nz(0.05, 0.05, Bp, 1800, 1.2); Tone(330 * dt, 0.05, Tr, 0.03); break;
                case "wood": Nz(0.07, 0.1, Bp, 900, 1.5); Tone(220 * dt, 0.07, Tr, 0.05, 0, 150 * dt); break;
                case "stone": Nz(0.12, 0.12, Lp, 500, 0.8, 0, 200); Tone(110 * dt, 0.12, Tr, 0.05, 0, 70 * dt); break;
                case "grass": Nz(0.09, 0.035, Hp, 2500, 0.5); break;
                case "gravel": Nz(0.07, 0.08, Bp, 3200, 0.6); break;
                case "plank": Nz(0.05, 0.09, Bp, 1300, 1.8); Tone(180 * dt, 0.05, Tr, 0.035, 0, 130 * dt); break;
                case "place": Nz(0.07, 0.09, Bp, 900, 1.5); Tone(220 * dt, 0.07, Tr, 0.05, 0, 150 * dt); Piano(523 * dt, 0.8, 0.05, 0.05); break;
                case "build":
                    {
                        double[] ws = { 0, 0.14, 0.28, 0.42 };
                        for (int i = 0; i < 4; i++) { double w = ws[i]; Nz(0.12, 0.1, Lp, 500 + i * 70, 0.8, w + R() * 0.02, 200); Tone((110 + i * 12) * dt, 0.12, Tr, 0.045, w, 70 * dt); }
                        break;
                    }
                case "door": Nz(0.07, 0.08, Bp, 900, 1.5); Tone(150 * dt, 0.32, Sa, 0.016, 0.02, 100 * dt); Nz(0.06, 0.07, Bp, 800, 1.5, 0.3); break;
                case "creak": Tone(170 * dt, 0.3, Sa, 0.03, 0, 130 * dt); Nz(0.25, 0.05, Bp, 700, 3); break;
                case "pull": Nz(0.18, 0.12, Bp, 1400, 2, 0, 900); break;
                case "sit": Nz(0.15, 0.05, Lp, 400, 0.7); Tone(90 * dt, 0.15, Si, 0.03); break;
                case "brush": Nz(0.2, 0.06, Hp, 2800, 0.4); break;
                case "lamp": Piano(660 * dt, 0.8, 0.045, 0); Piano(990 * dt, 1.1, 0.04, 0.08); Nz(0.04, 0.035, Bp, 2200, 2); break;
                case "pour": Nz(0.5, 0.09, Bp, 1800, 0.8); break;
                case "bloop": Tone(380 * dt, 0.12, Si, 0.045, 0, 760 * dt); Nz(0.1, 0.015, Hp, 2000); break;
                case "tick": Tone(660 * dt, 0.06, Si, 0.03); Nz(0.02, 0.02, Bp, 2500, 2); break;
                case "ding": Bell(988 * dt, 0.07, 1.3); break;
                case "pick": Piano(784 * dt, 0.9, 0.1, 0); Piano(1175 * dt, 1.3, 0.1, 0.09); break;
                case "hello": Piano(523 * dt, 0.7, 0.08, 0); Piano(659 * dt, 0.9, 0.08, 0.1); break;
                case "bell": Bell(330 * dt, 0.11, 2.6); break;
                case "mail": Piano(880 * dt, 1.0, 0.08, 0); Piano(1109 * dt, 1.1, 0.08, 0.14); Piano(1319 * dt, 1.4, 0.07, 0.3); break;
                case "firework":
                    Tone(220 * dt, 0.35, Si, 0.06, 0, 60 * dt); Nz(0.1, 0.03, Bp, 2800, 3, 0.1); Nz(0.08, 0.025, Bp, 3600, 3, 0.22); Nz(0.06, 0.02, Bp, 3200, 3, 0.31); break;
                case "level": { double[] fs = { 523, 659, 784 }; for (int i = 0; i < 3; i++) Piano(fs[i] * dt, 1.1, 0.08, i * 0.1); break; }
                case "fanfare":
                    { double[] fs = { 392, 523, 659, 784, 1047 }; for (int i = 0; i < 5; i++) Piano(fs[i] * dt, 1.5, 0.09, i * 0.14); Tone(196 * dt, 1.6, Si, 0.07, 0, 0, true, 0.05); break; }
                case "hammer": Nz(0.05, 0.1, Bp, 1500 + R() * 300, 1.5); Tone(180 * dt, 0.08, Tr, 0.05, 0, 90 * dt); break;
                case "horn": Tone(98 * dt, 2.4, Si, 0.12, 0, 0, true, 0.35); Tone(147 * dt, 2.4, Si, 0.06, 0, 0, true, 0.4); Tone(196 * dt, 2.2, Si, 0.025, 0, 0, true, 0.5); break;
                case "warn": { double[] ws = { 0, 0.2, 0.4 }; for (int i = 0; i < 3; i++) Tone(660 * dt, 0.14, Si, 0.045, ws[i]); break; }
                case "sos": Tone(587 * dt, 0.28, Si, 0.045); Tone(440 * dt, 0.38, Si, 0.045, 0.3); break;
                case "gull": GullCall(0.05, 2); break;
            }
        }

        // ================= 음악: 느리고 부드러운 건반 단음 (원본 MUSIC / musicPhrase) =================
        static readonly int[][] Scales = { new[] { 0, 2, 4, 7, 9 }, new[] { 0, 3, 5, 7, 10 }, new[] { 0, 2, 7, 9, 14 }, new[] { 0, 2, 3, 7, 9 } };   // day, night, winter, autumn
        static readonly double[] Roots = { 261.63, 293.66, 246.94, 329.63 };                                                                          // spring, summer, autumn, winter
        static readonly int[][] Motifs = { new[] { 0, 2, 1 }, new[] { 0, 1, 2, 4 }, new[] { 2, 1, 0 }, new[] { 0, 3, 2, 1 }, new[] { 4, 2, 3 }, new[] { 1, 0, 2 }, new[] { 0, 2, 4, 3 } };

        void PhrasePlay(double storm) { MusicPhraseNow(storm); }

        /// <summary>한 구절을 예약하고 그 길이(초)를 돌려준다.</summary>
        public double MusicPhraseNow(double storm)
        {
            int sn = sceneNow.Season; bool night = sceneNow.Dusk >= 0.5, winter = sn == 3;
            int key = winter ? 2 : night ? 1 : sn == 2 ? 3 : 0;
            var sc = Scales[key]; double root = Roots[sn] * (night ? 0.5 : 1); CntPhrase++; LastKey = key; LastRoot = root;
            var m = Motifs[(int)Math.Floor(R() * Motifs.Length)];
            double gap = (night ? 0.95 : 0.7) + R() * 0.25, vol = (night ? 0.034 : 0.04) * (1 - 0.35 * storm), t = 0;
            for (int i = 0; i < m.Length; i++)
            {
                int d = m[i];
                double f = root * Math.Pow(2, (sc[d % 5] + 12 * (d / 5) + (winter ? 12 : 0)) / 12.0);
                double vel = (i == 0 ? 1.08 : 0.82 + R() * 0.28) * (i == m.Length - 1 ? 0.88 : 1);
                double when = i == 0 ? 0 : t + (R() - 0.5) * 0.06;
                Piano(f, 2.8 - i * 0.2, vol * vel, when);
                t += gap * (0.85 + R() * 0.4) * (i == m.Length - 2 ? 1.25 : 1);
            }
            if (R() < 0.4) Piano(root * 0.5, 4.5, vol * 0.7, 0);                  // 가끔 낮은 으뜸음이 먼저 깔린다
            phr++; if (phr % 2 == 1) Pad(root, 9, 0.02);                          // 두 번에 한 번, 바닥에 깔리는 울림
            return t;
        }

        // ================= 감독 (원본 updateAudio 4765) =================
        void Director()
        {
            double now = Now, ms = now * 1000, t = now, sw2 = 0.5 + 0.5 * Math.Sin(t * 0.37 + 1.3);
            double storm = sceneNow.Wx == 2 ? sceneNow.WxAmt : 0, fog = sceneNow.Wx == 1 ? sceneNow.WxAmt : 0;
            bool winter = sceneNow.Season == 3; double dusk = sceneNow.Dusk;
            waves.G.SetTargetAtTime(0.14 + 0.07 * storm, now, 0.4);
            wash.G.SetTargetAtTime(0.11 + 0.05 * storm, now, 0.4);
            foam.G.SetTargetAtTime(0.013 + 0.01 * storm, now, 0.3);
            if (ms > nextWave)                                                    // 파도 한 줄기: 천천히 밀려 올라와 길게 빠진다
            {
                double big = 0.5 + R() * 0.5 + 0.2 * storm, rise = 2.6 + R() * 1.8, fall = 4.5 + R() * 3, t0 = now + 0.05;
                Swell(waves, waves.Base + 0.45 * big, 0, rise, fall, t0);
                Swell(wash, wash.Base + 0.45 * big, 0.25, rise, fall, t0);
                Swell(foam, foam.Base + 0.5 * big, rise * 0.7, rise * 0.4, fall * 0.7, t0);
                waves.F.Cancel(t0); waves.F.SetTargetAtTime(420 + 650 * big, t0, rise / 3); waves.F.SetTargetAtTime(420, t0 + rise, fall / 3);
                nextWave = ms + (rise + fall) * 1000 * (1.0 + R() * 0.6);
            }
            gust = Math.Max(0, Math.Min(1, gust + (R() - 0.5) * 0.25));            // 바람: 세기와 높이가 불규칙하게 떠돈다
            wind.G.SetTargetAtTime((0.5 + 0.5 * sw2 + 0.6 * gust) * 0.05 + 0.05 * fog + (winter ? 0.09 : 0.07) * storm, now, 0.9);
            wind.F.SetTargetAtTime(300 + 260 * sw2 + 220 * gust + 120 * storm, now, 1.5);
            rain.G.SetTargetAtTime((winter ? 0.035 : 0.3) * storm, now, 0.5);
            if (ms > nextNote)                                                    // 잔잔한 음악
            {
                double len = MusicPhraseNow(storm), calm = (dusk >= 0.5 ? 4500 : 3500) * (1 + 0.5 * storm + 0.4 * fog) * (1.4 - 0.06 * sceneNow.Stage);
                nextNote = ms + len * 1000 + calm + R() * 3500;
            }
            bool snow = storm > 0 && winter;
            if (storm > 0.2 && !snow && R() < 0.6 * storm) { CntRain++; Nz(0.03, 0.02 * storm, BqType.Bandpass, 3500 + R() * 2000, 2); }          // 빗방울이 지붕에 톡톡
            if (fog > 0.5 && ms > nextFog)                                        // 안개 속 먼 뱃고동
            {
                CntFog++; Tone(110, 3.2, OscType.Sine, 0.028 * fog, 0, 0, true, 0.6); Tone(165, 3.2, OscType.Sine, 0.012 * fog, 0, 0, true, 0.7);
                nextFog = ms + 22000 + R() * 12000;
            }
            if (dusk < 0.4 && ms > nextGull)                                      // 낮: 멀리서 갈매기 소리
            {
                CntGull++; GullCall(0.035, 1 + (int)Math.Floor(R() * 3));
                nextGull = ms + 7000 + R() * 11000;
            }
            if (dusk > 0.6 && ms > nextCricket)                                   // 밤: 귀뚜라미 두 마리
            {
                CntCricket++; Cricket(4300, 0.008 * dusk);
                nextCricket = ms + 500 + R() * 1800 + (R() < 0.3 ? 3500 : 0);
            }
            if (dusk > 0.65 && ms > nextCricket2) { CntCricket++; Cricket(4750, 0.006 * dusk); nextCricket2 = ms + 900 + R() * 2600; }
            if (sceneNow.Building && ms > nextHammer) { CntHammer++; DoSfx("hammer"); nextHammer = ms + 800 + R() * 300; }       // 공사 중: 망치 소리
        }

        static void Swell(AmbLayer L, double peak, double delay, double tr, double tf, double t0)
        {
            double a = t0 + delay; L.S.Cancel(a); L.S.SetTargetAtTime(peak, a, tr / 3); L.S.SetTargetAtTime(L.Base, a + tr, tf / 3);
        }
    }
}
