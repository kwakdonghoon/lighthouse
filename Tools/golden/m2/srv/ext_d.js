// M2 2차 골든 D: 연출(하늘 장식·바다 물결·불빛 번짐 glow)을 시각 t·dusk 고정 프레임으로, 그리고 fit() 배율 표(반 단계 포함).
// 실행: 페이지에서  await (0, eval)(await (await fetch('/ext_d.js')).text())   (gx.js 먼저)
(async () => {
  const { Pack, postText, readCanvas } = GX, out = {};
  const keep = { W, H, OY, cw: cv.width, ch: cv.height, dusk, NIGHT, st: STAGE, sn: seasonForce, mx: moonX, my: moonY };
  seasonForce = 'summer';
  const pk = new Pack(), meta = [];
  const setSize = (w, h) => { W = w; H = h; OY = h - DESIGN_H; cv.width = w; cv.height = h; const lo = HORIZON + OY; moonX = W * 0.76; moonY = -OY + lo * 0.4; cloudSprs = {}; };
  const mk = (w, h, d) => {
    const c = document.createElement('canvas'); c.width = w; c.height = h; const g = c.getContext('2d'); const kc = ctx; ctx = g; dusk = d; NIGHT = d >= 0.5;
    g.setTransform(Z, 0, 0, Z, 0, 0); g.imageSmoothingEnabled = false; g.globalAlpha = 1;
    return { c, g, kc };
  };
  // ---- 하늘 장식 + 바다 물결 (배경 위, 섬 없음)
  setSize(216, 468); STAGE = 6; rebuild();
  for (const [t, d] of [[0, 0], [12.5, 0], [250, 1], [137.3, 1], [30, 0.5], [34 * 3 + 0.5, 1]]) {
    const { c, g, kc } = mk(216, 468, d);
    try {
      if (dusk < 0.999) g.drawImage(bgD, 0, 0, W, H);
      if (dusk > 0.001) { g.globalAlpha = dusk; g.drawImage(bgN, 0, 0, W, H); g.globalAlpha = 1; }
      g.translate(0, OY); drawSkyDeco(t); drawSea(t);
    } finally { ctx = kc; }
    pk.add('sky_' + t + '_' + d, 0, 0, c, readCanvas(c));
  }
  // ---- 불빛 번짐(밤 6·3단계): 집·부두·등롱 불빛 glow + 창 불빛 + 램프 glow, bg 밤 위. dusk 0.6 은 알파 (dusk-0.4)/0.4 = 0.5 경로
  for (const st of [6, 3]) for (const [t, d] of [[0, 1], [5, 1], [0, 0.6]]) {
    STAGE = st; setSize(216, 468); rebuild();
    const P = skinTower(TOWER[STAGE]), { c, g, kc } = mk(216, 468, d);
    try {
      g.drawImage(bgN, 0, 0, W, H); g.translate(0, OY);
      const cx = W / 2, ly = lampPos(P);
      g.globalAlpha = Math.min(1, (dusk - 0.4) / 0.4); g.globalCompositeOperation = 'lighter';
      for (const [lx, ly2, r] of lights) glow(cx + lx, CY + ly2, r * 0.65, '255,190,90', 0.5);
      for (const [dc, zc] of P.win) { Q(cx + dc - 0.75, CY - zc - 1.2, 1.5, 2.4, '#ffd890'); glow(cx + dc, CY - zc, 4.5, '255,190,90', 0.3); }
      const br = 0.5 + 0.5 * Math.sin(t * 1.3);
      glow(cx, ly, 12 + Math.min(4, lvlOf('lens')), '255,205,115', 0.15 + 0.06 * br); glow(cx, ly, 5, '255,240,190', 0.42 + 0.1 * br);
      g.globalCompositeOperation = 'source-over'; g.globalAlpha = 1;
    } finally { ctx = kc; }
    pk.add('glow_s' + st + '_t' + t + '_d' + d, 0, 0, c, readCanvas(c)); meta.push(['glow_s' + st + '_t' + t + '_d' + d, st, t, d].join(';'));
  }
  out.frames = await pk.flush('fx.bin.gz'); await postText('fx.meta', meta.join('\n'));
  // ---- fit(): 기기 해상도·dpr·시점별 논리 크기·배율 (innerWidth/dpr 를 덮어쓰고 실제 fit() 호출)
  const rows = [], keepView = save.view, keepFit = fitKey;
  const def = (n, v) => Object.defineProperty(window, n, { value: v, configurable: true });
  const cases = [[360, 800, 3], [411, 891, 2.625], [393, 852, 3], [320, 568, 2], [360, 640, 2], [768, 1024, 2], [1080, 2340, 1], [720, 1280, 1], [1440, 3120, 1], [412, 915, 2.625], [390, 844, 3], [430, 932, 3], [800, 360, 2], [1024, 768, 1]];
  for (const [w, h, dpr] of cases) for (const view of ['near', 'normal', 'wide']) {
    def('innerWidth', w); def('innerHeight', h); def('devicePixelRatio', dpr); save.view = view;
    fit(); rows.push([w, h, dpr, view, +(SC * dpr).toFixed(6), W, H, OY].join(';'));
  }
  delete window.innerWidth; delete window.innerHeight; delete window.devicePixelRatio; save.view = keepView;
  await postText('fit.txt', rows.join('\n')); out.fit = rows.length;
  W = keep.W; H = keep.H; OY = keep.OY; cv.width = keep.cw; cv.height = keep.ch; dusk = keep.dusk; NIGHT = keep.NIGHT; STAGE = keep.st; seasonForce = keep.sn; moonX = keep.mx; moonY = keep.my;
  return JSON.stringify(out);
})()
