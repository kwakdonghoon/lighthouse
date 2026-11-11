// M2 골든 B: 밤 사본(nightOf), 우체통·암초·물가, 배경(makeBg)·구름, 배 9종×항로×외형, 아이콘. 그리고 C# 이 읽을 표 데이터(배 도면·아이콘 격자).
// 실행: 페이지에서  await (0, eval)(await (await fetch('/ext_b.js')).text())   (gx.js 먼저)
(async () => {
  const { Pack, postText, readCanvas } = GX, out = {};
  const keepSeason = seasonForce, keepStage = STAGE, keepPrev = skinPreview;
  seasonForce = 'summer'; skinPreview = null;
  // --- 밤 사본: 섬 1~6단계·조각·탑(기본 외형)·우체통·암초. 낮 쪽도 같은 묶음에 (우체통·암초·물가는 낮 골든이 아직 없다)
  {
    const pk = new Pack();
    const nt = (name, spr, k) => pk.sprite(name, nightOf(spr, k), true);
    for (let s = 1; s <= 6; s++) {
      STAGE = s; const isl = bakeIsland(); nt('night_isle_s' + s, isl);
      if (s === 6) ISLE_PIECES.forEach((p, i) => nt('night_isle_s6_p' + i, p.spr));
      const P = skinTower(TOWER[s]); nt('night_tw_s' + s + '_A', bakeTowerA(P)); nt('night_tw_s' + s + '_B', bakeTowerB(P));
    }
    for (const up of [true, false]) { const m = bakeMailbox(up); pk.sprite('mail_' + (up ? 'up' : 'dn'), m); nt('night_mail_' + (up ? 'up' : 'dn'), m); }
    STAGE = 6; ISLE_N = ISLE_NS[6];
    { const r = reefSprites(); pk.sprite('reef', r.d); pk.sprite('night_reef', r.n, true); }
    for (const st of [1, 3, 6]) { STAGE = st; ISLE_N = ISLE_NS[st]; pk.sprite('shore_s' + st, bakeShore()); }
    // 다른 NT 단계(진 0.58, 약 0.26)와 k=0.35 (도움받은 배): 섬 6단계 하나로
    STAGE = 6; const isl6 = bakeIsland(); const keepNT = NT;
    for (const v of [0.58, 0.26]) { NT = v; pk.sprite('night_nt' + v + '_isle_s6', nightOf(isl6), true); }
    NT = keepNT; pk.sprite('night_k035_isle_s6', nightOf(isl6, 0.35), true);
    out.night = await pk.flush('night.bin.gz');
  }
  // --- 배경: 해상도 4종 × 낮·밤. W,H,OY 는 fit() 수식 결과 (720x1280→180x320, 1080x2340→216x468, 1440x3120→206x446, 768x1024→192x256)
  {
    const pk = new Pack(), keepW = cv.width, keepH = cv.height, keepOY = OY;
    for (const [w, h] of [[180, 320], [216, 468], [206, 446], [192, 256]]) {
      cv.width = w; cv.height = h; OY = h - DESIGN_H;
      for (const night of [false, true]) { const c = makeBg(night); pk.add('bg_' + w + 'x' + h + '_' + (night ? 'n' : 'd'), 0, 0, c, GX.rawOf({ c })); }
    }
    cv.width = keepW; cv.height = keepH; OY = keepOY;
    for (const night of [false, true]) { const s = bakeCloud(night); pk.sprite('cloud_' + (night ? 'n' : 'd'), s, true); }
    out.bg = await pk.flush('bg.bin.gz');
  }
  // --- 배: 도면(글자 격자) 표 + 외형 5 × 종 9 × 항로 3 × (깃발 뺀 것 / 도움받은 것) + 밤 사본
  {
    const types = Object.keys(SHIP_SPRITES), lines = [];
    for (const t of types) SHIP_SPRITES[t].forEach((d, lane) => lines.push([t, lane, d.ox, d.oy, d.rows.join('|')].join(';')));
    await postText('ship_rows.txt', lines.join('\n'));
    const pk = new Pack();
    for (const skin of Object.keys(SKINS.ship)) {
      buildShipPals(SKINS.ship[skin].pal);
      for (const t of types) for (let lane = 0; lane < 3; lane++) {
        const d = SHIP_SPRITES[t][lane], a = spriteFromData(d, SHIP_PAL_NOFLAG), h = spriteFromData(d, SHIP_PAL_HELPED), tag = 'ship_' + skin + '_' + t + '_' + lane;
        pk.sprite(tag + '_d', a, true); pk.sprite(tag + '_h', h, true);
        if (skin === 'classic') { pk.sprite(tag + '_n', nightOf(a), true); pk.sprite(tag + '_hn', nightOf(h, 0.35), true); }
      }
    }
    buildShipPals();
    out.ships = await pk.flush('ships.bin.gz');
  }
  // --- 아이콘: 데이터 표(C# 에 생성해 넣는다) + 굽기 결과
  {
    const ids = Object.keys(ICONS), lines = [];
    for (const id of [...ids, 'unknown']) {
      const ic = id === 'unknown' ? ICON_UNKNOWN : ICONS[id];
      lines.push([id, Object.entries(ic.pal).map(([k, v]) => k + '=' + v).join(','), ic.rows.join('|')].join(';'));
    }
    await postText('icons_data.txt', lines.join('\n'));
    const pk = new Pack();
    for (const id of ids) { const c = iconCanvas(id); pk.add('icon_' + id, 0, 0, c, readCanvas(c)); }
    { const c = iconCanvas('__nope__'); pk.add('icon_unknown', 0, 0, c, readCanvas(c)); }
    out.icons = await pk.flush('icons.bin.gz'); out.nIcons = ids.length;
  }
  seasonForce = keepSeason; STAGE = keepStage; skinPreview = keepPrev;
  return JSON.stringify(out);
})()
