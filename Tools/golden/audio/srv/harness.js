// M6 오디오 골든 하니스: 동결 HTML(MD5 e36a9779…)의 소리 구간(audio_src.js = 4587~4806행 그대로)을 OfflineAudioContext 로 렌더해 서버로 보낸다.
// 사용: 서버(serve_audio_golden.py)를 띄우고 이 페이지를 연 뒤, 페이지 안에서  await GOLD.runAll()  (또는 GOLD.runOne('pick') 로 하나만).
// 규칙: initAudio 씨앗 S0, 소리마다 S1+번호 (C# AudioFixture 와 같다). Math.random 은 mulberry32 로 바꿔 끼운다(호출 순서가 곧 결과).
(function () {
  const SR = 44100, S0 = 20260101, S1 = 5000;
  // 압축기는 맨 처음 0.3초쯤 이득이 낮게 시작하는 버릇이 있어(크롬), 소리는 345퀀텀(44160표본 = 1.0014초) 뒤에 예약하고 그 뒤만 저장한다. C# 도 같은 만큼 무음을 먼저 렌더한다.
  const WARM = 345 * 128, WT = (WARM - 0.5) / SR;
  const NAMES = ['tick', 'click', 'ding', 'pick', 'hello', 'bell', 'mail', 'firework', 'level', 'fanfare', 'hammer', 'horn', 'warn', 'sos', 'wood', 'stone', 'grass', 'gravel', 'plank', 'place', 'build', 'door', 'creak', 'pull', 'sit', 'brush', 'lamp', 'pour', 'bloop', 'gull'];
  const MUSIC = [
    { name: 'music_spring_day', season: 'spring', dusk: 0.1, storm: 0, seed: S1 + 200 },
    { name: 'music_summer_night', season: 'summer', dusk: 0.9, storm: 0, seed: S1 + 201 },
    { name: 'music_winter_day_storm', season: 'winter', dusk: 0.1, storm: 0.5, seed: S1 + 202 },
    { name: 'music_autumn_day', season: 'autumn', dusk: 0.1, storm: 0, seed: S1 + 203 },
  ];
  const AMB = [
    { name: 'amb_spring_day', season: 'spring', dusk: 0.1, wx: { kind: 'clear', amt: 0 }, stage: 3, build: false, seed: S1 + 300 },
    { name: 'amb_summer_night', season: 'summer', dusk: 0.9, wx: { kind: 'clear', amt: 0 }, stage: 2, build: false, seed: S1 + 301 },
    { name: 'amb_autumn_rain_build', season: 'autumn', dusk: 0.3, wx: { kind: 'rain', amt: 0.8 }, stage: 4, build: true, seed: S1 + 302 },
    { name: 'amb_winter_fog', season: 'winter', dusk: 0.2, wx: { kind: 'fog', amt: 0.8 }, stage: 1, build: false, seed: S1 + 303 },
  ];
  function mulberry32(seed) { let a = (seed >>> 0) || 1; return () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
  let srcText = null;
  async function makeH() {
    if (!srcText) srcText = await (await fetch('audio_src.js?' + Date.now())).text();
    const body = [
      "let dusk = 0, STAGE = 1, adOpen = false, __season = 'spring', __ms = 0;",
      "const wx = { kind: 'clear', t0: 0, until: 0, next: 0, nextKind: 'fog', amt: 0, count: 0 }, save = { sound: true, build: false };",
      "const curSeason = () => __season, pick = a => a[Math.floor(Math.random() * a.length)];",
      "const performance = { now: () => __ms };",
      srcText,
      "return { snd, initAudio, sfx, musicPhrase, updateAudio, tone, nz, piano, pad, bell, gullCall, cricket, set: o => { if ('dusk' in o) dusk = o.dusk; if ('season' in o) __season = o.season; if ('stage' in o) STAGE = o.stage; if ('build' in o) save.build = o.build; if ('wx' in o) { wx.kind = o.wx.kind; wx.amt = o.wx.amt; } if ('ms' in o) __ms = o.ms; } };"
    ].join('\n');
    return new Function(body)();
  }
  async function setup(seed0, frames) {
    const H = await makeH();
    const ctx = new OfflineAudioContext(2, frames, SR);
    Object.defineProperty(ctx, 'state', { get: () => 'running' });
    const keepAC = window.AudioContext; window.AudioContext = function () { return ctx; };
    const keepR = Math.random; Math.random = mulberry32(seed0);
    try { H.initAudio(); } finally { window.AudioContext = keepAC; }
    return { H, ctx, keepR };
  }
  function pack(buf, start) {
    const L = buf.getChannelData(0).subarray(start || 0), R = buf.getChannelData(1).subarray(start || 0); let last = 0, peak = 0, sq = 0;
    for (let i = 0; i < L.length; i++) { const m = Math.max(Math.abs(L[i]), Math.abs(R[i])); if (m > 3e-4) last = i; if (m > peak) peak = m; sq += L[i] * L[i] + R[i] * R[i]; }
    return { L, R, last, peak, rms: Math.sqrt(sq / (2 * L.length)) };
  }
  async function post(name, u8) { const r = await fetch('/put/' + name, { method: 'POST', body: u8 }); if (!r.ok) throw new Error('post ' + name); }
  async function emit(name, buf, keepAll, meta, start) {
    const p = pack(buf, start); const L0 = p.L.length; const frames = keepAll ? L0 : Math.min(L0, (Math.ceil((p.last + 1) / 128) + 1) * 128);
    const out = new Int16Array(frames * 2);
    for (let i = 0; i < frames; i++) { out[2 * i] = Math.max(-32768, Math.min(32767, Math.round(p.L[i] * 32767))); out[2 * i + 1] = Math.max(-32768, Math.min(32767, Math.round(p.R[i] * 32767))); }
    await post(name + '.pcm', new Uint8Array(out.buffer));
    return Object.assign({ name, frames, ch: 2, sr: SR, warm: start || 0, peak: +p.peak.toFixed(6), rms: +p.rms.toFixed(6) }, meta || {});
  }
  async function runSfx(name, i) {
    const { H, ctx, keepR } = await setup(S0, WARM + SR * 6);
    ctx.suspend(WT).then(() => { try { Math.random = mulberry32(S1 + i); H.sfx(name); } finally { Math.random = keepR; } ctx.resume(); });
    const buf = await ctx.startRendering();
    return emit('sfx_' + name, buf, false, { kind: 'sfx', index: i, seed: S1 + i }, WARM);
  }
  async function runMusic(c) {
    const { H, ctx, keepR } = await setup(S0, WARM + SR * 14); let len;
    H.set({ season: c.season, dusk: c.dusk });
    ctx.suspend(WT).then(() => { try { Math.random = mulberry32(c.seed); len = H.musicPhrase(c.storm); } finally { Math.random = keepR; } ctx.resume(); });
    const buf = await ctx.startRendering();
    return emit(c.name, buf, true, { kind: 'music', season: c.season, dusk: c.dusk, storm: c.storm, seed: c.seed, phraseLen: len }, WARM);
  }
  async function runAmb(c) {
    const frames = SR * 8, { H, ctx, keepR } = await setup(S0, frames);
    H.set({ season: c.season, dusk: c.dusk, wx: c.wx, stage: c.stage, build: c.build });
    Math.random = mulberry32(c.seed);
    for (let k = 1; k * 8832 < frames; k++) ctx.suspend((k * 8832 - 0.5) / SR).then(() => { H.set({ ms: ctx.currentTime * 1000 }); H.updateAudio(0.2, ctx.currentTime); ctx.resume(); });
    let buf; try { buf = await ctx.startRendering(); } finally { Math.random = keepR; }
    return emit(c.name, buf, true, { kind: 'amb', season: c.season, dusk: c.dusk, wx: c.wx, stage: c.stage, build: c.build, seed: c.seed });
  }
  // 압축기·울림 보정용: 같은 순음을 여러 크기·두 경로(dry / 울림 버스)로 렌더해 구간 RMS 를 낸다 (C# 은 같은 호출로 같은 값을 낸다)
  const CAL_VOLS = [0.003, 0.01, 0.03, 0.06, 0.1, 0.2, 0.4, 0.8], CAL_WIN = [[0.05, 0.1], [0.3, 0.4], [0.8, 0.9], [1.5, 1.6], [2.4, 2.5]];
  async function runCalib() {
    const out = [];
    for (const bus of [false, true]) for (const v of CAL_VOLS) {
      const { H, ctx, keepR } = await setup(S0, WARM + SR * 3);
      ctx.suspend(WT).then(() => { try { Math.random = mulberry32(S1 + 400); H.tone(440, 2, 'sine', v, 0, 0, bus ? H.snd.bus : null, 0.015); } finally { Math.random = keepR; } ctx.resume(); });
      const buf = await ctx.startRendering(), L = buf.getChannelData(0), R = buf.getChannelData(1), rec = { bus, vol: v, rms: [] };
      for (const [a, b] of CAL_WIN) { let s = 0, n = 0; for (let i = WARM + Math.floor(a * SR); i < WARM + Math.floor(b * SR); i++) { s += L[i] * L[i] + R[i] * R[i]; n += 2; } rec.rms.push(+Math.sqrt(s / n).toExponential(6)); }
      out.push(rec);
    }
    await post('calib.json', new TextEncoder().encode(JSON.stringify(out, null, 1)));
    return out;
  }
  async function runEnv() {
    const out = [];
    for (const [bus, v] of [[false, 0.1], [false, 0.01]]) {
      const { H, ctx, keepR } = await setup(S0, WARM + SR * 3);
      ctx.suspend(WT).then(() => { try { Math.random = mulberry32(S1 + 400); H.tone(440, 2, 'sine', v, 0, 0, bus ? H.snd.bus : null, 0.015); } finally { Math.random = keepR; } ctx.resume(); });
      const buf = await ctx.startRendering(), L = buf.getChannelData(0), R = buf.getChannelData(1), rec = { bus, vol: v, rms: [] };
      for (let k = 0; k < 60; k++) { let s = 0, n = 0; for (let i = WARM + k * 441; i < WARM + (k + 1) * 441; i++) { s += L[i] * L[i] + R[i] * R[i]; n += 2; } rec.rms.push(+Math.sqrt(s / n).toExponential(6)); }
      out.push(rec);
    }
    await post('env.json', new TextEncoder().encode(JSON.stringify(out)));
    return out;
  }
  // 순음 한 개를 여러 높이·시작 위치로 렌더(오실레이터 정밀도 진단): tone_<높이>_<when>
  async function runTones() {
    const man = [];
    for (const [f, when, bus] of [[220, 0, false], [988, 0, false], [988, 0, true], [3000, 0, false], [5335, 0, false], [660, 0.05, false], [2000, 0.2, false]]) {
      const { H, ctx, keepR } = await setup(S0, WARM + SR);
      ctx.suspend(WT).then(() => { try { Math.random = mulberry32(S1 + 500); H.tone(f, 0.5, 'sine', 0.05, when, 0, bus ? H.snd.bus : null, 0.004); } finally { Math.random = keepR; } ctx.resume(); });
      const buf = await ctx.startRendering();
      man.push(await emit('tone_' + f + '_' + when + (bus ? '_bus' : ''), buf, true, { kind: 'tone', f, when, bus }, WARM));
    }
    return man.map(m => m.name + ':' + m.frames);
  }
  window.GOLD = {
    runCalib, runEnv, runTones,
    NAMES, MUSIC, AMB,
    async runOne(name) { const i = NAMES.indexOf(name); return runSfx(name, i); },
    async runAll(what) {
      const man = [];
      if (!what || what === 'sfx') for (let i = 0; i < NAMES.length; i++) man.push(await runSfx(NAMES[i], i));
      if (!what || what === 'music') for (const c of MUSIC) man.push(await runMusic(c));
      if (!what || what === 'amb') for (const c of AMB) man.push(await runAmb(c));
      const enc = new TextEncoder().encode(JSON.stringify({ sr: SR, S0, S1, tickFrames: 8832, items: man }, null, 1));
      await post('manifest' + (what ? '_' + what : '') + '.json', enc);
      return man.map(m => m.name + ':' + m.frames + ':' + m.peak);
    },
  };
})();
