// M2 골든 C: 꾸미기 표(C# 소스로 생성)·굽기·아이콘·자리 좌표·장면 목록, 그리고 낮/밤 장면 합성 프레임.
// 실행: 페이지에서  await (0, eval)(await (await fetch('/ext_c.js')).text())   (gx.js 먼저)
(async () => {
  const { Pack, postText, readCanvas } = GX, out = {};
  const keepSeason = seasonForce, keepStage = STAGE, keepPrev = skinPreview;
  seasonForce = 'summer'; skinPreview = null;
  const lit = s => JSON.stringify(s);
  const arr = (name, a, doc) => `    /// <summary>${doc}</summary>\n    public static readonly string[] ${name} =\n    {\n${a.map(s => '        ' + lit(s) + ',').join('\n')}\n    };\n\n`;
  // ---------- 표 데이터 → C# 소스 ----------
  const ids = Object.keys(DECOR_ITEMS);
  const itemLines = ids.map(id => { const it = DECOR_ITEMS[id]; return [id, it.name, it.size, it.zones.join(','), it.src, it.ax, it.ay, it.ss ? 1 : 0, it.sk ? 1 : 0, it.rows.join('|')].join(';'); });
  const slotLines = Object.keys(DECOR_SLOTS).map(id => {
    const s = DECOR_SLOTS[id], o = s.open, ok = o.base ? 'base' : o.stage ? 'stage' : 'lv', ov = o.base || o.stage || o.lv;
    let kind = 'ab', ka = 0, ca = 0, kb = 0, cb = 0;
    if (s.xy) kind = 'xy';
    else if (typeof s.ab === 'function') {
      const a6 = s.ab(6), a7 = s.ab(7); ka = a7[0] - a6[0]; kb = a7[1] - a6[1]; ca = a6[0] - 6 * ka; cb = a6[1] - 6 * kb;
      for (const N of [8, 9]) { const v = s.ab(N); if (Math.abs(v[0] - (ka * N + ca)) > 1e-9 || Math.abs(v[1] - (kb * N + cb)) > 1e-9) throw new Error('not linear ' + id); }
    } else { ca = s.ab[0]; cb = s.ab[1]; }
    return [id, s.chip, s.size, kind, ka, ca, kb, cb, ok, ov, s.blocked ? 1 : 0].join(';');
  });
  const letters = Object.entries(DECOR_LETTERS).map(([k, v]) => k + '=' + v);
  const seas = []; for (const k of ['pine', 'leaf']) for (const sn of Object.keys(DECOR_SEASON[k])) seas.push([k, sn, ...DECOR_SEASON[k][sn]].join(';'));
  const iconLines = []; for (const id of [...Object.keys(ICONS), 'unknown']) { const ic = id === 'unknown' ? ICON_UNKNOWN : ICONS[id]; iconLines.push([id, Object.entries(ic.pal).map(([k, v]) => k + '=' + v).join(','), ic.rows.join('|')].join(';')); }
  const src = '// 자동 생성: Tools/golden/m2/srv/ext_c.js 가 동결 사본(lighthouse_idle.frozen-unity-m0.html)의 표를 그대로 옮겨 쓴 파일. 직접 고치지 않는다.\n'
    + 'namespace Lighthouse.Render\n{\n    public static class RenderData\n    {\n'
    + arr('IconLines', iconLines, 'ICONS(68종)+ICON_UNKNOWN: id;팔레트(글자=#색,…);격자 줄(|)')
    + arr('DecorLetters', letters, 'DECOR_LETTERS: 글자=#색')
    + arr('DecorSeason', seas, 'DECOR_SEASON: 종류;계절;윗;왼;오른')
    + arr('DecorItems', itemLines, 'DECOR_ITEMS: id;이름;크기;구역(,);출처;ax;ay;계절색;외형색;격자(|)')
    + arr('DecorSlots', slotLines, 'DECOR_SLOTS: id;구역;크기;ab|xy;ka;ca;kb;cb;열림종류;값;막힘 — a=ka*N+ca, b=kb*N+cb')
    + '    }\n}\n';
  await postText('RenderData.Generated.cs', src);
  out.gen = { items: ids.length, slots: slotLines.length, icons: iconLines.length };

  // ---------- 장식 굽기 (아이템 × 계절 × 마을 외형) ----------
  {
    const pk = new Pack();
    const clear = () => { for (const k of Object.keys(decorCache)) delete decorCache[k]; };
    for (const id of ids) {
      const it = DECOR_ITEMS[id];
      for (const sn of (it.ss ? Palettes_seasons() : ['summer'])) for (const sk of (it.sk ? ['classic', 'seaside', 'festival'] : ['classic'])) {
        seasonForce = sn; skinPreview = sk === 'classic' ? null : { layer: 'decor', id: sk }; clear();
        const s = bakeDecor(id), tag = id + '_' + sn + '_' + sk;
        pk.sprite('decor_' + tag, s, true); pk.sprite('decorN_' + tag, nightOf(s), true);
      }
    }
    seasonForce = 'summer'; skinPreview = null; clear();
    for (const id of ids) { const c = decoIcon(id); pk.add('decoicon_' + id, 0, 0, c, readCanvas(c)); }
    out.decor = await pk.flush('decor.bin.gz');
  }
  function Palettes_seasons() { return ['spring', 'summer', 'autumn', 'winter']; }

  // ---------- 자리 좌표 (W=180 기준) ----------
  {
    const keepW = W, lines = [];
    W = 180;
    for (let st = 1; st <= 6; st++) {
      STAGE = st; ISLE_N = ISLE_NS[st]; DPOS = {};
      for (const id of Object.keys(DECOR_SLOTS)) {
        const sl = DECOR_SLOTS[id], [x, y] = decorPos(id), ab = sl.xy ? null : decorSlotAB(id);
        lines.push([st, id, x, y, ab ? (ab[0] + ab[1]) * 4 : 0].join(';'));
      }
    }
    W = keepW;
    await postText('decor_pos.txt', lines.join('\n'));
  }

  // ---------- 풀세트 장면: 프레임 합성 + 장면 목록 ----------
  {
    const keepW = W, keepH = H, keepOY = OY, keepCW = cv.width, keepCH = cv.height, keepDusk = dusk, keepNight = NIGHT, keepGrowth = save.growth.garden, keepLetters = save.letters;
    const lists = [], pk = new Pack(), info = [];
    save.growth.garden = 5000000; save.letters = [];
    const gl = effLv('garden');
    const frame = (w, h, season, st, d) => {
      W = w; H = h; OY = h - DESIGN_H; cv.width = w; cv.height = h; seasonForce = season; STAGE = st; skinPreview = null;
      rebuild(); decorDevFill(); decorSeasonTick && 0;
      const P = skinTower(TOWER[STAGE]), c = document.createElement('canvas'); c.width = w; c.height = h;
      const g = c.getContext('2d'), keepCtx = ctx;
      ctx = g; dusk = d; NIGHT = d >= 0.5;
      try {
        ctx.setTransform(Z, 0, 0, Z, 0, 0); ctx.imageSmoothingEnabled = false; ctx.globalAlpha = 1;
        if (dusk < 0.999) ctx.drawImage(bgD, 0, 0, W, H);
        if (dusk > 0.001) { ctx.globalAlpha = dusk; ctx.drawImage(bgN, 0, 0, W, H); ctx.globalAlpha = 1; }
        ctx.translate(0, OY);
        const cx = W / 2, t = 0;
        drawShore(t);
        putDN(spr.island, spr.islandN, cx, CY + ISLE_Y);
        drawScene('A', t, null);
        putDN(spr.a, spr.aN, cx, CY); drawLamp(P, t); putDN(spr.b, spr.bN, cx, CY);
        drawScene('B', t, null); drawScene('S', t, null);
      } finally { ctx = keepCtx; }
      return c;
    };
    const listOf = (season, st) => {
      const L = sceneLists(), tag = season + '_s' + st;
      for (const pass of ['A', 'B', 'S']) L[pass].forEach((e, i) => lists.push([tag, pass, i, e.k, e.v, e.x, e.y === undefined ? 0 : e.y, e.k === 'd' ? e.it : e.k === 'p' ? spr.pieces.indexOf(e.p) : 'm'].join(';')));
    };
    const jobs = [];
    for (let st = 1; st <= 6; st++) jobs.push([216, 468, 'summer', st]);
    for (const sn of ['spring', 'autumn', 'winter']) jobs.push([216, 468, sn, 6]);
    jobs.push([180, 320, 'summer', 6]); jobs.push([192, 256, 'summer', 3]);
    for (const [w, h, sn, st] of jobs) {
      for (const d of [0, 1]) {
        const c = frame(w, h, sn, st, d), tag = 'frame_' + w + 'x' + h + '_' + sn + '_s' + st + '_' + (d ? 'n' : 'd');
        pk.add(tag, 0, 0, c, readCanvas(c));
        if (d === 0) listOf(w + 'x' + h + '_' + sn, st);
      }
      info.push([w, h, sn, st, gl].join(';'));
    }
    out.frames = await pk.flush('frames.bin.gz'); await postText('frames.meta', info.join('\n')); await postText('scene_lists.txt', lists.join('\n'));
    save.growth.garden = keepGrowth; save.letters = keepLetters;
    W = keepW; H = keepH; OY = keepOY; cv.width = keepCW; cv.height = keepCH; dusk = keepDusk; NIGHT = keepNight; out.gl = gl;
  }
  seasonForce = keepSeason; STAGE = keepStage; skinPreview = keepPrev;
  return JSON.stringify(out);
})()
