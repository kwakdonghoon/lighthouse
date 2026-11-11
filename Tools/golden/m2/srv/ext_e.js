// M4 골든 E: 게임 루프 연출(빛줄기·해질녘 틴트·날씨·무지개·물결·역할 효과·램프 번쩍임·돌고래·관리인 손동작·입자)을
// 시각(performance.now·Date.now)과 상태를 고정해 배경 위에 그린 프레임. 하늘 장식·바다 물결은 뺀다(M2 골든 별도).
// 실행: 페이지에서  await (0, eval)(await (await fetch('/ext_e.js')).text())   (gx.js 먼저).  서버: Tools/golden/serve_golden_m2.py (127.0.0.1)
(async () => {
  const { Pack, postText, readCanvas } = GX, out = {};
  const keep = { W, H, OY, cw: cv.width, ch: cv.height, dusk, NIGHT, st: STAGE, sn: seasonForce, mx: moonX, my: moonY, pn: performance.now, dn: Date.now, beam: save.beam, lens: save.growth.lens, clock: save.clock };
  const pk = new Pack(), meta = [];
  const setSize = (w, h) => { W = w; H = h; OY = h - DESIGN_H; cv.width = w; cv.height = h; const lo = HORIZON + OY; moonX = W * 0.76; moonY = -OY + lo * 0.4; cloudSprs = {}; };
  const mk = (w, h, d) => {
    const c = document.createElement('canvas'); c.width = w; c.height = h; const g = c.getContext('2d'); const kc = ctx; ctx = g; dusk = d; NIGHT = d >= 0.5;
    g.setTransform(Z, 0, 0, Z, 0, 0); g.imageSmoothingEnabled = false; g.globalAlpha = 1;
    return { c, g, kc };
  };
  const FP = 123480.5, EP0 = 1800000;          // performance.now() 고정값(warn 켜짐 자리), Date.now() 기본값(600초 주기의 3번째 시작)
  performance.now = () => FP; Date.now = () => EP0;
  save.clock = 'fast'; save.growth.lens = 40000; save.beam = 'gold';
  setSize(216, 468); STAGE = 6; seasonForce = 'summer'; rebuild();
  const P = skinTower(TOWER[STAGE]), cx = W / 2, ly = lampPos(P);
  const frame = (name, d, fn, w = 216, h = 468, plain) => {
    const { c, g, kc } = mk(w, h, d);
    try {
      if (plain) { g.fillStyle = plain; g.fillRect(0, 0, w, h); }
      else { if (dusk < 0.999) g.drawImage(bgD, 0, 0, W, H); if (dusk > 0.001) { g.globalAlpha = dusk; g.drawImage(bgN, 0, 0, W, H); g.globalAlpha = 1; } g.translate(0, OY); }
      fn(g);
    } finally { ctx = kc; g.globalAlpha = 1; }
    pk.add(name, 0, 0, c, readCanvas(c)); meta.push(name);
  };
  // ---- 빛줄기
  const setBeam = (th, t, v) => { beamPrev.th = th; beamPrev.t = t; beamPrev.v = v; };
  frame('beam_gold_idle_d1', 1, g => { setBeam(0.7, FP - 1000, 0); drawBeam(0.7, cx, ly, 1); });
  frame('beam_gold_spin_d1', 1, g => { setBeam(0.4, FP - 100, 0); drawBeam(0.7, cx, ly, 1); });
  frame('beam_gold_d0.6', 0.6, g => { setBeam(-1.9, FP - 1000, 0); g.globalAlpha = Math.min(1, (dusk - 0.4) / 0.4); drawBeam(-1.9, cx, ly); g.globalAlpha = 1; });
  frame('beam_day_aim_d0', 0, g => { setBeam(2.4, FP - 1000, 0); drawBeam(2.4, cx, ly, 0.8); });
  save.beam = 'rainbow'; frame('beam_rainbow_d1', 1, g => { setBeam(3.6, FP - 1000, 0); drawBeam(3.6, cx, ly, 1); }); save.beam = 'gold';
  // ---- 해질녘·새벽 틴트: Date.now 를 주기 위치 p 에 맞춘다 (저녁 = 올라가는 중, 새벽 = 내려가는 중)
  for (const p of [0.43, 0.47, 0.93, 0.97]) {
    const ms = (600 * 3 + 600 * p) * 1000; Date.now = () => ms; const d = duskAt(ms);
    frame('tint_p' + p, d, g => { drawDuskTint(); }); meta.push('tintd;' + p + ';' + d);
  }
  Date.now = () => EP0;
  // ---- 날씨
  const wxs = (kind, amt) => { wx.kind = kind; wx.amt = amt; fogTile = null; };
  wxs('fog', 1); frame('wx_fog_1_t7.3_d0', 0, g => drawWeather(7.3));
  wxs('fog', 0.6); frame('wx_fog_0.6_t41.9_d1', 1, g => drawWeather(41.9));
  for (const t of [2.04, 4.74, 8.18, 11.06, 20.31, 33.77]) { wxs('fog', 1); frame('wx_fogphase_' + t + '_d0', 0, g => drawWeather(t)); }
  wxs('rain', 1); frame('wx_rain_1_t7.3_d0', 0, g => drawWeather(7.3));
  wxs('rain', 0.5); frame('wx_rain_0.5_t13.1_d1', 1, g => drawWeather(13.1));
  seasonForce = 'winter'; wxs('rain', 1); frame('wx_snow_1_t41.9_d0', 0, g => drawWeather(41.9)); seasonForce = 'summer';
  wxs('clear', 0);
  // ---- 무지개
  rainbowUntil = EP0 + 30000; frame('rainbow_f1_d0', 0, g => drawRainbow());
  rainbowUntil = EP0 + 2000; frame('rainbow_f0.4_d0', 0, g => drawRainbow()); rainbowUntil = 0;
  // ---- 물결 고리 (frame() 안의 인라인 코드를 그대로)
  frame('rings_a_d0', 0, g => {
    const arr = [{ x: 80, y: 250, s: 1, age: 0.5 }, { x: 150, y: 300, s: 0.8, age: 1.8 }, { x: 60, y: 200, s: 0.55, age: 1.0 }];
    for (const r of arr) { const rad = (5 + r.age * 15) * r.s; g.globalAlpha = 1 - r.age / 2.4; for (let a = 0; a < 6.28; a += 0.35) Q(r.x + Math.cos(a) * rad, r.y + Math.sin(a) * rad * KY, 1, 1, '#ffeeaf'); g.globalAlpha = 1; }
  });
  // ---- 역할 효과
  warnUntil = FP + 1000; warnX = 43.2; warnY = 280; frame('role_warn_d1', 1, g => drawRoleEffects(1)); warnUntil = 0;
  signalUntil = FP + 1700; frame('role_signal_d1', 1, g => drawRoleEffects(1)); signalUntil = 0;
  sirens.length = 0; sirens.push({ age: 0.5 }, { age: 1.5 }); frame('role_siren_d1', 1, g => drawRoleEffects(1)); sirens.length = 0;
  // ---- 램프 번쩍임
  for (const f of [0.6, 0.2]) { frame('lamp_pulse_' + f + '_d1', 1, g => { flash = f; drawLampPulse(cx, ly); }); } flash = 0;
  // ---- 돌고래
  dolphins.length = 0; dolphins.push({ x0: 60, y0: 200, dir: 1, t: 0.5, dur: 4.6 }, { x0: 150, y0: 290, dir: -1, t: 2.1, dur: 4.6 }, { x0: 100, y0: 240, dir: 1, t: 1.2, dur: 4.6 });
  frame('dolphins_a_d0', 0, g => drawDolphins()); dolphins.length = 0;
  // ---- 입자(새·불꽃·반짝임·선물 팝): frame() 안의 인라인 코드를 그대로
  frame('particles_a_d0', 0, g => {
    const t = 3.37, birdsA = [{ x: 100, y: 120, life: 2, ph: 1.3 }, { x: 60, y: 90, life: 0.5, ph: 4.1 }];
    for (const b of birdsA) { const f = Math.floor(t * 7 + b.ph) % 2, col = NIGHT ? '#cfd8f0' : '#ffffff'; g.globalAlpha = Math.min(1, b.life); Q(b.x - 2, b.y - f, 2, 1, col); Q(b.x, b.y, 1, 1, col); Q(b.x + 1, b.y - f, 2, 1, col); }
    g.globalAlpha = 1;
    const sparksA = [{ x: 90, y: 100, life: 0.4, col: '#ffe08a' }, { x: 120, y: 60, life: 1.1, col: '#ff9bb8' }];
    for (const s of sparksA) { g.globalAlpha = Math.min(1, s.life * 1.6); Q(s.x, s.y, 1, 1, s.col); } g.globalAlpha = 1;
    const fxA = [{ x: 70, y: 150, life: 0.5 }, { x: 140, y: 170, life: 1.3 }];
    for (const f of fxA) { g.globalAlpha = Math.min(1, f.life * 1.5); Q(f.x, f.y, 1, 1, '#ffe9a0'); Q(f.x - 0.5, f.y + 0.25, 2, 0.5, '#fff6cc'); Q(f.x + 0.25, f.y - 0.5, 0.5, 2, '#fff6cc'); } g.globalAlpha = 1;
    const popsA = [{ x: 90, y: 200, gift: 'shell', age: 0.3 }, { x: 130, y: 260, gift: 'star', age: 1.2 }];
    for (const p of popsA) { g.globalAlpha = Math.min(1, (1.7 - p.age) * 2.5); g.drawImage(iconCanvas(p.gift), Math.round(p.x - 4), Math.round(p.y - p.age * 11 - 4)); } g.globalAlpha = 1;
  });
  // ---- 관리인 손동작 (120x80 작은 캔버스, 관리인 발 위치 (60, 60))
  const acts = ['wave', 'sweep', 'polish', 'anchor', 'gild', 'mail', 'water', 'fish', 'lamp', 'well', 'flag', 'rest', 'feed', 'stars'];
  const ts = [3.37, 7.81, 1.12, 5.5, 9.9, 2.2, 4.4, 6.6, 8.8, 0.7, 3.9, 5.1, 2.9, 7.3];
  acts.forEach((act, i) => {
    for (const dir of [1, -1]) frame('kact_' + act + '_' + dir, 0, g => {
      keeper.dir = dir; keeper.walking = false; keeper.waveT = act === 'wave' ? 1 : 0; keeper.act = act === 'wave' ? null : act; drawKeeperAct(ts[i], 60, 60);
    }, 120, 80, '#2a78ae');
  });
  await pk.flush('fx4.bin.gz'); await postText('fx4.meta', meta.join('\n'));
  out.frames = pk.n;
  W = keep.W; H = keep.H; OY = keep.OY; cv.width = keep.cw; cv.height = keep.ch; dusk = keep.dusk; NIGHT = keep.NIGHT; STAGE = keep.st; seasonForce = keep.sn; moonX = keep.mx; moonY = keep.my;
  performance.now = keep.pn; Date.now = keep.dn; save.beam = keep.beam; save.growth.lens = keep.lens; save.clock = keep.clock; keeper.act = 'sweep'; keeper.waveT = 0;
  return JSON.stringify(out);
})()
