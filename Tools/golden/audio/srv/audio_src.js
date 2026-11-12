const snd = { ac: null, master: null, bus: null, waves: null, wash: null, foam: null, wind: null, rain: null, nextNote: 0, nextGull: 0, nextCricket: 0, nextCricket2: 0, nextHammer: 0, nextWave: 0, gust: 0.3, tick: 0 };
const PENTA = [261.63, 293.66, 329.63, 392.0, 440.0];             // 도 레 미 솔 라
// 소음 바탕 (자연음): 짧은 흰 소음을 되풀이하면 기계처럼 들려서, 6초짜리 좌우가 다른 소음을 만들고 이음매를 겹쳐 붙인다.
//   brown = 낮게 울리는 파도 밑소리, pink = 부드러운 쉿 소리(파도 물살·거품·바람·비). 음량은 같게 맞춘다
function mkNoise(ac, kind, sec) {
  const sr = ac.sampleRate, n = Math.floor(sr * sec), x = Math.floor(sr * 0.6), buf = ac.createBuffer(2, n, sr);
  for (let c = 0; c < 2; c++) {
    const raw = new Float32Array(n + x); let l = 0, b0 = 0, b1 = 0, b2 = 0;
    for (let i = 0; i < n + x; i++) {
      const w = Math.random() * 2 - 1;
      if (kind === 'brown') { l = (l + 0.02 * w) / 1.02; raw[i] = l; }
      else { b0 = 0.99765 * b0 + w * 0.099046; b1 = 0.963 * b1 + w * 0.2965164; b2 = 0.57 * b2 + w * 1.0526913; raw[i] = b0 + b1 + b2 + w * 0.1848; }
    }
    let s = 0; for (let i = 0; i < n; i++) s += raw[i] * raw[i];
    const k = 0.3 / Math.sqrt(s / n), d = buf.getChannelData(c);                                  // 실효값 0.3 으로 맞춤
    for (let i = 0; i < n; i++) d[i] = (i < x ? raw[i] * (i / x) + raw[n + i] * (1 - i / x) : raw[i]) * k;
  }
  return buf;
}
// 울림(리버브): 간격이 일정한 에코 대신, 지수로 줄어드는 소음을 점점 어둡게 만든 울림 응답. 방이 아니라 바닷가 공기 같은 부드러운 꼬리
function mkIR(ac, sec) {
  const sr = ac.sampleRate, n = Math.floor(sr * sec), pre = Math.floor(sr * 0.018), buf = ac.createBuffer(2, n, sr);
  for (let c = 0; c < 2; c++) {
    const d = buf.getChannelData(c); let y = 0;
    for (let i = pre; i < n; i++) {
      const t = (i - pre) / sr, k = 0.55 * Math.exp(-t * 1.6) + 0.07;                                // 시간이 갈수록 고음이 먼저 사라진다
      y += ((Math.random() * 2 - 1) - y) * k;
      d[i] = y * Math.exp(-t * 2.3);
    }
  }
  return buf;
}
function initAudio() {
  if (snd.ac) { if (snd.ac.state === 'suspended' && save.sound !== false) snd.ac.resume(); return; }
  const AC = window.AudioContext || window.webkitAudioContext;
  if (!AC) return;
  const ac = snd.ac = new AC();
  snd.master = ac.createGain(); snd.master.gain.value = save.sound === false ? 0 : 0.7;
  const comp = ac.createDynamicsCompressor(); comp.threshold.value = -20; comp.knee.value = 24; comp.ratio.value = 3; comp.attack.value = 0.02; comp.release.value = 0.4;    // 여러 소리가 겹쳐도 갈라지지 않게 살짝 눌러 준다
  snd.master.connect(comp); comp.connect(ac.destination);
  snd.bus = ac.createGain(); snd.bus.connect(snd.master);                                    // 음악 버스: 울림을 입힌다
  const conv = ac.createConvolver(), wet = ac.createGain();
  conv.buffer = mkIR(ac, 3.2); wet.gain.value = 0.5;
  snd.bus.connect(conv); conv.connect(wet); wet.connect(snd.master);
  const nb = ac.createBuffer(1, ac.sampleRate * 2, ac.sampleRate), d = nb.getChannelData(0);   // 짧은 터짐 소리(블록·빗방울)용 흰 소음
  for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
  snd.nb = nb;
  const pink = mkNoise(ac, 'pink', 6), brown = mkNoise(ac, 'brown', 6);
  const layer = (buf, type, freq, q, base) => {                                              // 소음 → 필터 → 물결 세기(s) → 전체 크기(g)
    const src = ac.createBufferSource(), f = ac.createBiquadFilter(), s = ac.createGain(), g = ac.createGain();
    src.buffer = buf; src.loop = true; f.type = type; f.frequency.value = freq; f.Q.value = q; s.gain.value = base; g.gain.value = 0;
    src.connect(f); f.connect(s); s.connect(g); g.connect(snd.master); src.start(0, Math.random() * 4);
    return { g, f, s, base };
  };
  snd.waves = layer(brown, 'lowpass', 420, 0.3, 0.3); snd.wash = layer(pink, 'bandpass', 900, 0.4, 0.25); snd.foam = layer(pink, 'highpass', 2800, 0.5, 0.15);
  snd.wind = layer(pink, 'bandpass', 380, 0.5, 1); snd.rain = layer(pink, 'highpass', 2200, 0.4, 1);
}
function tone(freq, dur, type = 'sine', vol = 0.1, when = 0, slideTo = 0, bus = null, atk = 0.015) {
  const ac = snd.ac, t0 = ac.currentTime + when, a = Math.min(atk, dur * 0.4), o = ac.createOscillator(), g = ac.createGain();
  o.type = type; o.frequency.setValueAtTime(freq, t0); o.detune.value = (Math.random() - 0.5) * 10;     // 소리마다 아주 조금씩 다른 높이 (똑같이 반복되면 기계 같다)
  if (slideTo) o.frequency.exponentialRampToValueAtTime(slideTo, t0 + dur);
  g.gain.setValueAtTime(0.0001, t0); g.gain.exponentialRampToValueAtTime(vol, t0 + a); g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
  o.connect(g); g.connect(bus || snd.master); o.start(t0); o.stop(t0 + dur + 0.05);
}
// 짧은 노이즈 터짐: 필터를 거친 흰 소음. 돌·나무·풀 같은 블록 소리의 바탕 (freq → toFreq 로 미끄러질 수 있다)
function nz(dur, vol, type = 'lowpass', freq = 800, q = 0.7, when = 0, toFreq = 0, bus = null) {
  const ac = snd.ac; if (!snd.nb) return;
  const t0 = ac.currentTime + when, src = ac.createBufferSource(), f = ac.createBiquadFilter(), g = ac.createGain();
  src.buffer = snd.nb; src.loop = true; f.type = type; f.frequency.setValueAtTime(freq, t0); if (toFreq) f.frequency.exponentialRampToValueAtTime(toFreq, t0 + dur); f.Q.value = q;
  g.gain.setValueAtTime(0.0001, t0); g.gain.exponentialRampToValueAtTime(vol, t0 + 0.008); g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
  src.connect(f); f.connect(g); g.connect(bus || snd.master); src.start(t0, Math.random() * 1.5); src.stop(t0 + dur + 0.05);
}
// 부드러운 건반 소리 (오르골 + 펠트 피아노 사이): 순음 여러 개를 겹쳐 처음엔 또렷하고 금방 부드러워지며 길게 남는다.
//   배음마다 줄어드는 속도가 다르고, 위쪽 소리는 필터로 점점 어두워지며, 소리마다 좌우 위치와 미세한 높이가 다르다
function piano(f, dur, vol, when, pan) {
  const ac = snd.ac, t0 = ac.currentTime + Math.max(0, when) + 0.01, v = vol * 0.62;
  const lp = ac.createBiquadFilter(); lp.type = 'lowpass'; lp.Q.value = 0.4;
  lp.frequency.setValueAtTime(Math.min(6500, f * 6 + 500), t0); lp.frequency.exponentialRampToValueAtTime(Math.max(500, f * 1.8), t0 + dur * 0.7);
  let last = lp;
  if (ac.createStereoPanner) { const p = ac.createStereoPanner(); p.pan.value = pan === undefined ? Math.max(-0.5, Math.min(0.5, (f / 1000 - 0.45) * 0.5 + (Math.random() - 0.5) * 0.3)) : pan; lp.connect(p); last = p; }
  last.connect(snd.bus);
  [[1, 1, 1, -3], [1, 0.55, 0.9, 4], [2, 0.22, 0.45, 0], [3.01, 0.07, 0.22, 0], [5.03, 0.025, 0.1, 0]].forEach(([r, a, dm, ct]) => {
    const o = ac.createOscillator(), g = ac.createGain(), e = t0 + dur * dm, mid = t0 + Math.min(0.45, dur * dm * 0.35);
    o.type = 'sine'; o.frequency.value = f * r; o.detune.value = ct;
    g.gain.setValueAtTime(0.0001, t0); g.gain.exponentialRampToValueAtTime(v * a, t0 + 0.006); g.gain.exponentialRampToValueAtTime(v * a * 0.28, mid); g.gain.exponentialRampToValueAtTime(0.0001, e);
    o.connect(g); g.connect(lp); o.start(t0); o.stop(e + 0.05);
  });
  nz(0.035, v * 0.4, 'lowpass', Math.min(2500, f * 3), 0.8, when, 0, snd.bus);                  // 건반이 닿는 아주 작은 톡
}
// 바닥에 깔리는 울림(패드): 살짝 어긋난 톱니파 둘과 5도·옥타브 순음을 어두운 필터에 통과시켜 천천히 커졌다 사라진다
function pad(f, dur, vol) {
  const ac = snd.ac, t0 = ac.currentTime + 0.02, lp = ac.createBiquadFilter(), g = ac.createGain();
  lp.type = 'lowpass'; lp.frequency.value = 650; lp.Q.value = 0.3;
  g.gain.setValueAtTime(0.0001, t0); g.gain.linearRampToValueAtTime(vol, t0 + dur * 0.4); g.gain.linearRampToValueAtTime(0.0001, t0 + dur);
  lp.connect(g); g.connect(snd.bus);
  [['sawtooth', 1, -7, 0.5], ['sawtooth', 1, 6, 0.5], ['sine', 1.5, 0, 0.35], ['sine', 2, -3, 0.3]].forEach(([ty, r, ct, a]) => {
    const o = ac.createOscillator(), og = ac.createGain();
    o.type = ty; o.frequency.value = f * r; o.detune.value = ct; og.gain.value = a; o.connect(og); og.connect(lp); o.start(t0); o.stop(t0 + dur + 0.1);
  });
}
// 종: 맞부딪히는 쇠의 소리는 배음이 정수배가 아니라서(2.4·4.2 …) 맑고 쇳소리가 난다. 높은 소리부터 먼저 사라진다
function bell(f, vol, dur) { [[1, 1, 1], [2, 0.5, 0.8], [2.41, 0.35, 0.6], [3, 0.25, 0.5], [4.2, 0.12, 0.3], [5.4, 0.07, 0.2]].forEach(([r, a, dm]) => tone(f * r, dur * dm, 'sine', vol * a * 0.6, 0, 0, snd.bus, 0.004)); }
// 갈매기: 톱니파를 입 모양 필터에 통과시키고 빠르게 떨어 끽끽 울게 한다. 멀리서 들리도록 울림 버스로 보낸다
function gullCall(vol, n) {
  const ac = snd.ac, base = 1450 + Math.random() * 450;
  for (let k = 0; k < n; k++) {
    const t0 = ac.currentTime + 0.02 + k * (0.36 + Math.random() * 0.08), d = 0.32 - k * 0.03, p = base * (1 - k * 0.06);
    const o = ac.createOscillator(), lfo = ac.createOscillator(), lg = ac.createGain(), f = ac.createBiquadFilter(), g = ac.createGain();
    o.type = 'sawtooth'; o.frequency.setValueAtTime(p * 0.8, t0); o.frequency.linearRampToValueAtTime(p * 1.35, t0 + d * 0.28); o.frequency.exponentialRampToValueAtTime(p * 0.7, t0 + d);
    lfo.frequency.value = 30 + Math.random() * 12; lg.gain.value = p * 0.05; lfo.connect(lg); lg.connect(o.frequency);
    f.type = 'bandpass'; f.frequency.value = 2300; f.Q.value = 1.1;
    g.gain.setValueAtTime(0.0001, t0); g.gain.exponentialRampToValueAtTime(vol, t0 + 0.05); g.gain.exponentialRampToValueAtTime(0.0001, t0 + d);
    o.connect(f); f.connect(g); g.connect(snd.bus); o.start(t0); lfo.start(t0); o.stop(t0 + d + 0.05); lfo.stop(t0 + d + 0.05);
  }
}
// 귀뚜라미: 짧은 울림 3~4번이 한 묶음이고, 묶음마다 높이와 박자가 조금씩 다르다
function cricket(f, vol) {
  const n = 3 + Math.floor(Math.random() * 2), sp = 0.05 + Math.random() * 0.014, ff = f * (0.97 + Math.random() * 0.06);
  for (let i = 0; i < n; i++) tone(ff, 0.04, 'sine', vol * (0.75 + 0.25 * Math.random()), i * sp, 0, null, 0.01);
}
function sfx(name) {
  if (!snd.ac || snd.ac.state !== 'running' || save.sound === false || adOpen) return;
  const dt = 1 + (Math.random() - 0.5) * 0.05;                                                    // 누를 때마다 높이가 아주 조금씩 달라진다
  const T = (f, d, ty, v, w, s) => tone(f * dt, d, ty, v, w, s ? s * dt : 0), P = (f, d, v, w) => piano(f * dt, d, v, w);
  if (name !== 'click') snd.last = performance.now();
  switch (name) {
    // ---- 블록 소리 (복셀 a9): 모두 작고 부드럽게. 압박 없는 소리만. 삐 소리(사각파)는 쓰지 않고 나무·돌 두드림 느낌으로 ----
    case 'click': nz(0.05, 0.05, 'bandpass', 1800, 1.2); T(330, 0.05, 'triangle', 0.03); break;                                   // 단추·탭: 나무 블록 톡
    case 'wood': nz(0.07, 0.1, 'bandpass', 900, 1.5); T(220, 0.07, 'triangle', 0.05, 0, 150); break;
    case 'stone': nz(0.12, 0.12, 'lowpass', 500, 0.8, 0, 200); T(110, 0.12, 'triangle', 0.05, 0, 70); break;
    case 'grass': nz(0.09, 0.035, 'highpass', 2500, 0.5); break;
    case 'gravel': nz(0.07, 0.08, 'bandpass', 3200, 0.6); break;
    case 'plank': nz(0.05, 0.09, 'bandpass', 1300, 1.8); T(180, 0.05, 'triangle', 0.035, 0, 130); break;
    case 'place': nz(0.07, 0.09, 'bandpass', 900, 1.5); T(220, 0.07, 'triangle', 0.05, 0, 150); P(523, 0.8, 0.05, 0.05); break;   // 선물을 놓는 소리
    case 'build': [0, 0.14, 0.28, 0.42].forEach((w, i) => { nz(0.12, 0.1, 'lowpass', 500 + i * 70, 0.8, w + Math.random() * 0.02, 200); T(110 + i * 12, 0.12, 'triangle', 0.045, w, 70); }); break;   // 블록이 하나씩 쌓인다
    case 'door': nz(0.07, 0.08, 'bandpass', 900, 1.5); T(150, 0.32, 'sawtooth', 0.016, 0.02, 100); nz(0.06, 0.07, 'bandpass', 800, 1.5, 0.3); break;
    case 'creak': T(170, 0.3, 'sawtooth', 0.03, 0, 130); nz(0.25, 0.05, 'bandpass', 700, 3); break;
    case 'pull': nz(0.18, 0.12, 'bandpass', 1400, 2, 0, 900); break;
    case 'sit': nz(0.15, 0.05, 'lowpass', 400, 0.7); T(90, 0.15, 'sine', 0.03); break;
    case 'brush': nz(0.2, 0.06, 'highpass', 2800, 0.4); break;
    case 'lamp': P(660, 0.8, 0.045, 0); P(990, 1.1, 0.04, 0.08); nz(0.04, 0.035, 'bandpass', 2200, 2); break;
    case 'pour': nz(0.5, 0.09, 'bandpass', 1800, 0.8); break;
    case 'bloop': T(380, 0.12, 'sine', 0.045, 0, 760); nz(0.1, 0.015, 'highpass', 2000); break;
    case 'tick': T(660, 0.06, 'sine', 0.03); nz(0.02, 0.02, 'bandpass', 2500, 2); break;
    case 'ding': bell(988 * dt, 0.07, 1.3); break;
    case 'pick': P(784, 0.9, 0.1, 0); P(1175, 1.3, 0.1, 0.09); break;
    case 'hello': P(523, 0.7, 0.08, 0); P(659, 0.9, 0.08, 0.1); break;
    case 'bell': bell(330 * dt, 0.11, 2.6); break;
    case 'mail': P(880, 1.0, 0.08, 0); P(1109, 1.1, 0.08, 0.14); P(1319, 1.4, 0.07, 0.3); break;
    case 'firework': T(220, 0.35, 'sine', 0.06, 0, 60); nz(0.1, 0.03, 'bandpass', 2800, 3, 0.1); nz(0.08, 0.025, 'bandpass', 3600, 3, 0.22); nz(0.06, 0.02, 'bandpass', 3200, 3, 0.31); break;
    case 'level': [523, 659, 784].forEach((f, i) => P(f, 1.1, 0.08, i * 0.1)); break;
    case 'fanfare': [392, 523, 659, 784, 1047].forEach((f, i) => P(f, 1.5, 0.09, i * 0.14)); tone(196 * dt, 1.6, 'sine', 0.07, 0, 0, snd.bus, 0.05); break;
    case 'hammer': nz(0.05, 0.1, 'bandpass', 1500 + Math.random() * 300, 1.5); T(180, 0.08, 'triangle', 0.05, 0, 90); break;                // 망치: 나무 두드림
    case 'horn': tone(98 * dt, 2.4, 'sine', 0.12, 0, 0, snd.bus, 0.35); tone(147 * dt, 2.4, 'sine', 0.06, 0, 0, snd.bus, 0.4); tone(196 * dt, 2.2, 'sine', 0.025, 0, 0, snd.bus, 0.5); break;
    case 'warn': [0, 0.2, 0.4].forEach(w => T(660, 0.14, 'sine', 0.045, w)); break;
    case 'sos': T(587, 0.28, 'sine', 0.045); T(440, 0.38, 'sine', 0.045, 0.3); break;
    case 'gull': gullCall(0.05, 2); break;
  }
}
// ---------- 음악 (a9): 느리고 부드러운 건반 단음 — 모티프(2~4음)를 오음 음계로, 가끔 낮은 울림과 바닥에 깔리는 소리 ----------
const MUSIC = {
  scales: { day: [0, 2, 4, 7, 9], night: [0, 3, 5, 7, 10], winter: [0, 2, 7, 9, 14], autumn: [0, 2, 3, 7, 9] },          // 낮 밝은 오음 / 밤 차분한 단음계 오음 / 겨울 투명한 넓은 음 / 가을 약간 쓸쓸한 음
  root: { spring: 261.63, summer: 293.66, autumn: 246.94, winter: 329.63 },
  motifs: [[0, 2, 1], [0, 1, 2, 4], [2, 1, 0], [0, 3, 2, 1], [4, 2, 3], [1, 0, 2], [0, 2, 4, 3]]
};
function musicPhrase(storm) {
  const sn = curSeason(), night = dusk >= 0.5, key = sn === 'winter' ? 'winter' : night ? 'night' : sn === 'autumn' ? 'autumn' : 'day';
  const sc = MUSIC.scales[key], root = MUSIC.root[sn] * (night ? 0.5 : 1), m = pick(MUSIC.motifs), gap = (night ? 0.95 : 0.7) + Math.random() * 0.25, vol = (night ? 0.034 : 0.04) * (1 - 0.35 * storm);
  let t = 0;
  m.forEach((d, i) => {                                                                          // 사람이 치는 것처럼: 박자를 조금씩 흔들고, 세기를 다르게, 끝에서 숨 쉬듯 늦춘다
    const f = root * Math.pow(2, (sc[d % 5] + 12 * Math.floor(d / 5) + (sn === 'winter' ? 12 : 0)) / 12), vel = (i === 0 ? 1.08 : 0.82 + Math.random() * 0.28) * (i === m.length - 1 ? 0.88 : 1);
    piano(f, 2.8 - i * 0.2, vol * vel, i === 0 ? 0 : t + (Math.random() - 0.5) * 0.06);
    t += gap * (0.85 + Math.random() * 0.4) * (i === m.length - 2 ? 1.25 : 1);
  });
  if (Math.random() < 0.4) piano(root * 0.5, 4.5, vol * 0.7, 0);                                // 가끔 낮은 으뜸음이 먼저 깔린다
  snd.phr = (snd.phr || 0) + 1; if (snd.phr % 2 === 1) pad(root, 9, 0.02);                      // 두 번에 한 번, 바닥에 깔리는 울림이 천천히 번졌다 사라진다
  return t;
}
function updateAudio(dt, t) {
  const ac = snd.ac;
  if (!ac || ac.state !== 'running') return;
  snd.tick += dt;
  if (snd.tick < 0.2) return;
  snd.tick = 0;
  const now = ac.currentTime, ms = performance.now(), sw2 = 0.5 + 0.5 * Math.sin(t * 0.37 + 1.3);
  const storm = wx.kind === 'rain' ? wx.amt : 0, fog = wx.kind === 'fog' ? wx.amt : 0;
  snd.waves.g.gain.setTargetAtTime(0.14 + 0.07 * storm, now, 0.4);                                // 파도: 밑소리·물살·거품이 따로 있고, 파도 한 줄기마다 세기가 오르내린다 (아래)
  snd.wash.g.gain.setTargetAtTime(0.11 + 0.05 * storm, now, 0.4);
  snd.foam.g.gain.setTargetAtTime(0.013 + 0.01 * storm, now, 0.3);
  if (ms > snd.nextWave) {                                                                       // 파도 한 줄기: 천천히 밀려 올라와(rise) 길게 빠진다(fall). 크기와 간격이 매번 다르다
    const big = 0.5 + Math.random() * 0.5 + 0.2 * storm, rise = 2.6 + Math.random() * 1.8, fall = 4.5 + Math.random() * 3, t0 = now + 0.05;
    const swell = (L, peak, delay, tr, tf) => { const p = L.s.gain, a = t0 + delay; p.cancelScheduledValues(a); p.setTargetAtTime(peak, a, tr / 3); p.setTargetAtTime(L.base, a + tr, tf / 3); };
    swell(snd.waves, snd.waves.base + 0.45 * big, 0, rise, fall);
    swell(snd.wash, snd.wash.base + 0.45 * big, 0.25, rise, fall);
    swell(snd.foam, snd.foam.base + 0.5 * big, rise * 0.7, rise * 0.4, fall * 0.7);            // 거품은 물이 부서지는 끝에서
    snd.waves.f.frequency.cancelScheduledValues(t0); snd.waves.f.frequency.setTargetAtTime(420 + 650 * big, t0, rise / 3); snd.waves.f.frequency.setTargetAtTime(420, t0 + rise, fall / 3);
    snd.nextWave = ms + (rise + fall) * 1000 * (1.0 + Math.random() * 0.6);
  }
  snd.gust = Math.max(0, Math.min(1, snd.gust + (Math.random() - 0.5) * 0.25));               // 바람: 세기와 높이가 불규칙하게 떠돈다
  snd.wind.g.gain.setTargetAtTime((0.5 + 0.5 * sw2 + 0.6 * snd.gust) * 0.05 + 0.05 * fog + (curSeason() === 'winter' ? 0.09 : 0.07) * storm, now, 0.9);
  snd.wind.f.frequency.setTargetAtTime(300 + 260 * sw2 + 220 * snd.gust + 120 * storm, now, 1.5);
  snd.rain.g.gain.setTargetAtTime((curSeason() === 'winter' ? 0.035 : 0.3) * storm, now, 0.5);
  if (ms > snd.nextNote) {                                                                       // 잔잔한 음악: 단계가 오를수록 조금 더 자주, 비·안개가 낄수록 더 드물게
    const len = musicPhrase(storm), calm = (dusk >= 0.5 ? 4500 : 3500) * (1 + 0.5 * storm + 0.4 * fog) * (1.4 - 0.06 * STAGE);
    snd.nextNote = ms + len * 1000 + calm + Math.random() * 3500;
  }
  const snow = storm > 0 && curSeason() === 'winter';
  if (storm > 0.2 && !snow && Math.random() < 0.6 * storm) nz(0.03, 0.02 * storm, 'bandpass', 3500 + Math.random() * 2000, 2);          // 빗방울이 지붕에 톡톡
  if (fog > 0.5 && ms > (snd.nextFog || 0)) { tone(110, 3.2, 'sine', 0.028 * fog, 0, 0, snd.bus, 0.6); tone(165, 3.2, 'sine', 0.012 * fog, 0, 0, snd.bus, 0.7); snd.nextFog = ms + 22000 + Math.random() * 12000; }     // 안개 속 먼 뱃고동
  if (dusk < 0.4 && ms > snd.nextGull) {                                                         // 낮: 멀리서 갈매기 소리
    gullCall(0.035, 1 + Math.floor(Math.random() * 3));
    snd.nextGull = ms + 7000 + Math.random() * 11000;
  }
  if (dusk > 0.6 && ms > snd.nextCricket) {                                                      // 밤: 귀뚜라미 두 마리가 서로 다른 높이로, 가끔은 한참 쉰다
    cricket(4300, 0.008 * dusk);
    snd.nextCricket = ms + 500 + Math.random() * 1800 + (Math.random() < 0.3 ? 3500 : 0);
  }
  if (dusk > 0.65 && ms > snd.nextCricket2) { cricket(4750, 0.006 * dusk); snd.nextCricket2 = ms + 900 + Math.random() * 2600; }
  if (save.build && ms > snd.nextHammer) { sfx('hammer'); snd.nextHammer = ms + 800 + Math.random() * 300; }          // 공사 중: 망치 소리 (박자를 조금씩 흔든다)
}
