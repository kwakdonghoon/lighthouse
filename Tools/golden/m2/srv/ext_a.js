// M2 골든 A: 섬(계절 4 × 단계 6 + 섬·마을 외형), 탑(외형 15 × 단계 6) + 램프 낮·밤.
// 실행: 페이지(?dev&clean&s=1&n=0&season=summer)에서  await (0, eval)(await (await fetch('/ext_a.js')).text())
(async () => {
  const { Pack, postJson, postText } = GX, out = {};
  // 메타는 CSV 비슷한 줄 형식: tag;N;v 목록(공백);불빛 'x y r' 목록('|' 로 구분)
  const metaLine = (tag, N, pcs, ls) => [tag, N, pcs.map(p => p.v).join(' '), ls.map(l => l.join(' ')).join('|')].join(';');
  const keepSeason = seasonForce, keepStage = STAGE, keepPrev = skinPreview;
  // --- 섬: 계절 4 × 단계 6 (기본 외형)
  for (const season of ['spring', 'summer', 'autumn', 'winter']) {
    seasonForce = season; skinPreview = null;
    const pk = new Pack(), meta = [];
    for (let s = 1; s <= 6; s++) {
      STAGE = s;
      const isl = bakeIsland();
      pk.sprite('s' + s, isl);
      ISLE_PIECES.forEach((p, i) => pk.sprite('s' + s + '_p' + i, p.spr));
      meta.push(metaLine('s' + s, ISLE_N, ISLE_PIECES, lights));
    }
    out['isle_' + season] = await pk.flush('isle_' + season + '.bin.gz'); await postText('isle_' + season + '.meta', meta.join('\n'));
  }
  // --- 섬·마을 외형 (여름): 3·6단계
  {
    seasonForce = 'summer';
    const pk = new Pack(), meta = [];
    for (const [layer, id] of [['isle', 'blossom'], ['isle', 'maple'], ['decor', 'seaside'], ['decor', 'festival'], ['decor', 'dev']]) {
      skinPreview = { layer, id };
      for (const s of [3, 6]) {
        STAGE = s;
        const isl = bakeIsland(), tag = layer + '_' + id + '_s' + s;
        pk.sprite(tag, isl);
        ISLE_PIECES.forEach((p, i) => pk.sprite(tag + '_p' + i, p.spr));
        meta.push(metaLine(tag, ISLE_N, ISLE_PIECES, lights));
      }
    }
    skinPreview = null;
    out.isle_skins = await pk.flush('isle_skins.bin.gz'); await postText('isle_skins.meta', meta.join('\n'));
  }
  // --- 탑 외형 15 × 단계 6 (+ 1단계 T1V 0·1·2 는 외형 classic 에서 따로)
  {
    seasonForce = 'summer';
    const pk = new Pack(), meta = {};
    for (const id of Object.keys(SKINS.tower)) {
      skinPreview = { layer: 'tower', id };
      for (let s = 1; s <= 6; s++) {
        STAGE = s;
        const P = skinTower(TOWER[s]), a = bakeTowerA(P), b = bakeTowerB(P), ld = bakeLamp(P, false), ln = bakeLamp(P, true), tag = 'tw_' + id + '_s' + s;
        pk.sprite(tag + '_A', a); pk.sprite(tag + '_B', b); pk.sprite(tag + '_lampD', ld); pk.sprite(tag + '_lampN', ln);
        meta[tag] = { zt: P.zt, z0: P.z0, h: P.h, wide: !!P.wide, spire: !!P.spire, roof: P.roof, trim: P.trim || null };
      }
    }
    skinPreview = null;
    out.tower_skins = await pk.flush('tower_skins.bin.gz'); await postJson('tower_skins.json', meta);
  }
  seasonForce = keepSeason; STAGE = keepStage; skinPreview = keepPrev;
  return JSON.stringify(out);
})()
