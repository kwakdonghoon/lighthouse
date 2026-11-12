// A1 골든 F: 월드 연출 잔여(먼 섬·부이·물가 불빛·표류물·공사 비계·물고기/반딧불이/나비·계절 입자·등불·오로라·별똥별·갈매기·배 물보라/선원/깃발/등불/표식·꾸미기 번호 배지·관리인 외형·그림자 타원)를
// 시각(performance.now·Date.now)과 상태를 고정해 단색/배경 위에 그린 프레임. 실행: 페이지에서  await (0, eval)(await (await fetch('/ext_f.js')).text())   (gx.js 먼저).
// 서버: Tools/golden/serve_golden_a1.py (127.0.0.1 전용) → Tools/golden/a1/out/fx_a1.bin.gz, fx_a1.meta
(async () => {
  const { Pack, postText, readCanvas, rawOf } = GX, out = {};
  const keep = { W, H, OY, cw: cv.width, ch: cv.height, dusk, NIGHT, st: STAGE, sn: seasonForce, mx: moonX, my: moonY, pn: performance.now, dn: Date.now, growth: JSON.stringify(save.growth), put: JSON.stringify(save.isle.put), pm: panelMode, ph: panel.hidden, dt: decoTab, dz: decoZone, ds: typeof decoSel === 'undefined' ? undefined : decoSel, el: ellipse };
  const pk = new Pack(), meta = [];
  const setSize = (w, h) => { W = w; H = h; OY = h - DESIGN_H; cv.width = w; cv.height = h; const lo = HORIZON + OY; moonX = W * 0.76; moonY = -OY + lo * 0.4; cloudSprs = {}; };
  const mk = (w, h, d) => {
    const c = document.createElement('canvas'); c.width = w; c.height = h; const g = c.getContext('2d'); const kc = ctx; ctx = g; dusk = d; NIGHT = d >= 0.5;
    g.setTransform(Z, 0, 0, Z, 0, 0); g.imageSmoothingEnabled = false; g.globalAlpha = 1;
    return { c, g, kc };
  };
  const FP = 123480.5, EP0 = 1800000;
  performance.now = () => FP; Date.now = () => EP0;
  save.clock = 'fast';
  setSize(216, 468); STAGE = 6; seasonForce = 'summer'; rebuild();
  const frame = (name, d, fn, w = 216, h = 468, plain) => {
    const { c, g, kc } = mk(w, h, d);
    try {
      if (plain) { g.fillStyle = plain; g.fillRect(0, 0, w, h); g.translate(0, OY); }
      else { if (dusk < 0.999) g.drawImage(bgD, 0, 0, W, H); if (dusk > 0.001) { g.globalAlpha = dusk; g.drawImage(bgN, 0, 0, W, H); g.globalAlpha = 1; } g.translate(0, OY); }
      fn(g);
    } finally { ctx = kc; g.globalAlpha = 1; }
    pk.add(name, 0, 0, c, readCanvas(c)); meta.push(name);
  };
  const setGrowth = (k, v) => { save.growth[k] = v; };
  const growthFor = (k, lv) => { let g = 0; while (true) { save.growth[k] = g; if (lvlOf(k) >= lv) return g; g = Math.max(1, Math.ceil(g * 1.15)); } };

  // ---- 먼 섬
  frame('far_d0_t1', 0, g => drawFarIslands(1));
  frame('far_d1_on', 1, g => drawFarIslands(1.0));
  frame('far_d1_off', 1, g => drawFarIslands(3.0));
  // ---- 물가 불빛 번짐 + 부이 (frame() 의 5245~5246행 순서 그대로)
  const warmBuoy = (name, d, t, stage, port) => {
    STAGE = stage; rebuild(); const gv = growthFor('port', port); meta.push(`growth;port;${gv};${name}`);
    frame(name, d, g => { if (dusk > 0.4) { g.globalAlpha = Math.min(1, (dusk - 0.4) / 0.4); waterWarmth(); g.globalAlpha = 1; } drawBuoys(t); });
  };
  warmBuoy('buoy_s6_p4_d0_t2.3', 0, 2.3, 6, 4);
  warmBuoy('buoy_s6_p12_d1_t5.1', 1, 5.1, 6, 12);
  warmBuoy('buoy_s6_p23_d1_t7.7', 1, 7.7, 6, 23);
  warmBuoy('buoy_s3_p8_d0.7_t3.3', 0.7, 3.3, 3, 8);
  warmBuoy('buoy_s1_p0_d1_t0.4', 1, 0.4, 1, 0);
  STAGE = 6; rebuild(); setGrowth('port', 0);
  // ---- 표류물
  const driftSet = () => { drift.length = 0; drift.push({ x: 60.4, y: 200.3, vx: 3, ph: 1.2, gift: 'shell' }, { x: 150.7, y: 290.2, vx: -3, ph: 3.7, gift: 'shell', take: 0.4 }, { x: 100, y: 276.5, vx: 3, ph: 5.5, gift: 'shell', take: 0.9 }, { x: 30.5, y: 192, vx: 3, ph: 0.3, gift: 'shell' }); };
  driftSet(); frame('drift_d0_t4.2', 0, g => drawDrift(4.2));
  driftSet(); frame('drift_d1_t9.7', 1, g => drawDrift(9.7));
  drift.length = 0;
  // ---- 공사 비계
  frame('scaffold_s6_d0_t3.37', 0, g => drawScaffold(3.37, TOWER[6]));
  frame('scaffold_s3_d1_t7.9', 1, g => drawScaffold(7.9, TOWER[3]));
  frame('scaffold_s1_d0_t1.9', 0, g => drawScaffold(1.9, TOWER[1]));
  frame('scaffold_s5_d0.55_t12.2', 0.55, g => drawScaffold(12.2, TOWER[5]));
  // ---- 물고기·반딧불이·나비
  const gardenV = [0, 40000];
  setGrowth('garden', 0); frame('life_day_g0_t6.3', 0, g => drawLife(6.3));
  setGrowth('garden', 40000); frame('life_day_g40000_t6.9', 0, g => drawLife(6.9)); frame('life_day_g40000_t2.0', 0, g => drawLife(2.0));
  setGrowth('garden', 0); frame('life_night_g0_t13.4', 1, g => drawLife(13.4));
  setGrowth('garden', 40000); frame('life_night_g40000_t27.8', 1, g => drawLife(27.8)); frame('life_night_g40000_t30.55', 1, g => drawLife(30.55));
  setGrowth('garden', 0);
  // ---- 계절 입자·등불·오로라·별똥별 (drawSeasonFx 전체)
  const T0 = 8.77, mset = s => { motes.length = 0; for (let i = 0; i < 8; i++) motes.push({ x: 20 + i * 24.3, y: -OY + 30 + i * 41.7, ph: i * 0.8 + 0.1, s }); };
  mset('spring'); seasonForce = 'spring'; frame('sfx_spring_d0', 0, g => drawSeasonFx(T0));
  mset('autumn'); seasonForce = 'autumn'; frame('sfx_autumn_d0', 0, g => drawSeasonFx(T0));
  mset('winter'); seasonForce = 'winter'; meteors.length = 0; meteors.push({ x: 80, y: -OY + 40, vx: 60, vy: 30, age: 0.3, life: 1.8, wished: false }, { x: 160, y: -OY + 90, vx: -60, vy: 30, age: 1.4, life: 1.8, wished: false });
  frame('sfx_winter_d1', 1, g => drawSeasonFx(T0)); frame('sfx_winter_d0.7', 0.7, g => drawSeasonFx(3.3));
  motes.length = 0; meteors.length = 0; seasonForce = 'summer';
  lanterns.length = 0; lanterns.push({ x: 90.4, y: 160.2, ph: 1.1 }, { x: 130, y: 100, ph: 3.2 }, { x: 70.7, y: 40.9, ph: 5.0 }); meteors.push({ x: 40, y: -OY + 20, vx: -60, vy: 30, age: 1.7, life: 1.8, wished: false });
  frame('sfx_summer_d1', 1, g => drawSeasonFx(T0)); lanterns.length = 0; meteors.length = 0;
  // ---- 갈매기 무리·빵부스러기
  gulls.length = 0; crumbs.length = 0;
  gulls.push({ x: 100.4, y: 250.2, st: 'stand', face: 1, pk: 0.1, ph: 0.5, delay: 0 }, { x: 120.2, y: 252.6, st: 'stand', face: -1, pk: 0, ph: 1.5, delay: 0 }, { x: 60.6, y: 200.1, st: 'in', face: 1, pk: 0, ph: 2.1, delay: 0 },
    { x: 150.5, y: 180.4, st: 'out', face: 1, pk: 0, ph: 4.4, delay: 0, life: 0.5 }, { x: 30, y: 100, st: 'in', face: 1, pk: 0, ph: 3, delay: 1.2 });
  crumbs.push({ x: 110.3, y: 245.6, land: false }, { x: 118.2, y: 252.1, land: true, life: 1 }, { x: 104.9, y: 249, land: true, life: 2 });
  frame('gulls_d0_t5.3', 0, g => drawGulls(5.3)); frame('gulls_d1_t6.1', 1, g => drawGulls(6.1));
  gulls.length = 0; crumbs.length = 0;
  // ---- 배: 깃발·등불·표식 (drawShipMarks), 선원, 물보라 (shipDraw 에서 스프라이트·그림자를 뺀 것)
  const mkInfo = (sh, ex) => { const ln = SHIP_LANES[sh.lane]; return { x: sh.x, y: ln.y + (sh.yo || 0), s: ln.s, dir: sh.dir, sh }; };
  const sh = (type, lane, x, dir, ph, o) => ({ type, lane, x, dir, ph, yo: 0, done: false, guide: 0, need: 1, sp: 5, ...o });
  const marks = () => [sh('fish', 2, 60.3, 1, 1.1), sh('trade', 1, 150.6, -1, 2.3, { done: true }), sh('galleon', 0, 100.2, 1, 0.7, { sos: true }), sh('rowboat', 1, 40.5, -1, 4.2, { restT: 2, guide: 0.5, reg: 'x' }),
    sh('steamer', 2, 170.4, 1, 3.6, { done: true }), sh('cruise', 0, 30, -1, 5.1, { guide: 0.3 }), sh('schooner', 1, 120, 1, 2.8, { guide: 0.95 }), sh('explorer', 2, 20.5, 1, 0.2, { done: true, yo: 5 }), sh('ferry', 1, 190, -1, 4.9)];
  for (const [d, t, n] of [[0, 3.37, 'marks_d0_t3.37'], [1, 3.37, 'marks_d1_t3.37'], [0.6, 7.1, 'marks_d0.6_t7.1'], [1, 1.9, 'marks_d1_t1.9']]) {
    const ss = marks(); frame(n, d, g => { drawShipMarks(ss.map(mkInfo), t); }, 216, 468, '#2a78ae');
  }
  { const pd = putDN, el = ellipse;
    putDN = () => {}; ellipse = () => {};
    for (const [d, t, n] of [[0, 3.37, 'spray_d0_t3.37'], [1, 6.2, 'spray_d1_t6.2']]) {
      const ss = marks(); frame(n, d, g => { for (const s of ss) { shipDraw(s, t, 0); } }, 216, 468, '#2a78ae');
    }
    for (const [d, t, n] of [[0, 3.37, 'sailor_d0_t3.37'], [1, 5.5, 'sailor_d1_t5.5']]) {
      const ss = marks(); frame(n, d, g => { for (const s of ss) { drawSailor(s, t); } }, 216, 468, '#2a78ae');
    }
    putDN = pd; ellipse = el; }
  // ---- 꾸미기 번호 배지
  { const zones = ['front', 'path', 'shore', 'back'];
    save.isle.put = {}; setGrowth('garden', 40000);
    const full = decorDevFill(); const ids = Object.keys(full); const drop = {}; ids.forEach((id, i) => { if (i % 3 === 0) delete full[id]; }); save.isle.put = full;
    panel.hidden = false; panelMode = 'deco'; decoTab = 'place';
    for (const z of zones) {
      decoZone = z; const open = zoneOpen(z); if (!open.length) continue; decoSel = open[1] || open[0];
      for (const id of open) { const [x, y] = decorPos(id), sl = DECOR_SLOTS[id]; meta.push(`marker;${z};${id};${x};${y};${sl.size};${decorPutOf(id) ? 1 : 0};${id === decoSel ? 1 : 0}`); }
      frame('badges_' + z + '_d0_t2.6', 0, g => drawDecorMarkers(2.6)); meta.push('badgeframe;' + z + ';2.6');
    }
    panel.hidden = keep.ph; panelMode = keep.pm; decoTab = keep.dt; decoZone = keep.dz; decoSel = keep.ds; save.isle.put = JSON.parse(keep.put); }
  // ---- 관리인 외형(몸 스프라이트): HTML drawKeeper 의 굽기 코드 그대로 (skinPal + B 어두운 색 + voxelize)
  for (const id of ['classic', 'dusk', 'moonlit', 'sailor', 'dev']) for (const dir of [1, -1]) for (const fr of [0, 1]) {
    const P0 = { ...KEEPER.pal, ...SKINS.keeper[id].pal }, KP = { ...P0, B: hexOf(vmul(rgb(P0.b), 0.78)) }, rows = [...KEEPER.head, ...KEEPER.legs[fr]];
    const kc = document.createElement('canvas'); kc.width = 8; kc.height = rows.length; const kg = kc.getContext('2d');
    rows.forEach((row, j) => { for (let i = 0; i < 8; i++) { const col = KP[row[dir > 0 ? i : 7 - i]]; if (col) { kg.fillStyle = col; kg.fillRect(i, j, 1, 1); } } }); voxelize(kc);
    const nm = `keeper_${id}_${dir}_${fr}`; pk.add(nm, 0, 0, kc, rawOf({ c: kc })); meta.push(nm);
  }
  // ---- 그림자 타원: 암초·배·관리인
  frame('shadows_d0', 0, g => {
    const sc = 'rgba(10,50,90,0.3)'; ellipse(g, 43 + 2, 280 + 1, 11, 3, sc); ellipse(g, 173 + 2, 290 + 1, 11, 3, sc);
    for (const [ty, L, lane] of [['fish', 15, 2], ['galleon', 30, 0], ['rowboat', 11, 1]]) { const ln = SHIP_LANES[lane]; ellipse(g, 100 + 1, ln.y + 1.8, L * ln.s * 0.95, 2 * ln.s * Math.max(0.5, KY / 0.36), 'rgba(10,50,90,0.3)'); }
    ellipse(g, 60, 220, 4, 1.4, 'rgba(0,0,0,0.28)');
  });
  frame('shadows_d1', 1, g => {
    const sc = 'rgba(0,6,24,0.4)'; ellipse(g, 43 + 2, 280 + 1, 11, 3, sc);
    for (const [ty, L, lane] of [['fish', 15, 2], ['galleon', 30, 0], ['rowboat', 11, 1]]) { const ln = SHIP_LANES[lane]; ellipse(g, 100 + 1, ln.y + 1.8, L * ln.s * 0.95, 2 * ln.s * Math.max(0.5, KY / 0.36), sc); }
  });
  await pk.flush('fx_a1.bin.gz'); await postText('fx_a1.meta', meta.join('\n'));
  out.frames = pk.n;
  W = keep.W; H = keep.H; OY = keep.OY; cv.width = keep.cw; cv.height = keep.ch; dusk = keep.dusk; NIGHT = keep.NIGHT; STAGE = keep.st; seasonForce = keep.sn; moonX = keep.mx; moonY = keep.my;
  performance.now = keep.pn; Date.now = keep.dn; save.growth = JSON.parse(keep.growth);
  return JSON.stringify(out);
})()
