// M1 골든 생성기 — 동결 HTML 페이지 안에서 실행한다 (원본 함수를 그대로 부른다; HTML 은 읽기만).
// 사용: serve_m1.mjs 로 연 페이지(?dev&clean&s=1&n=0&season=summer)에서
//   await (0, eval)(await (await fetch('/m1_golden.js')).text())      // 전부
//   window.__m1only = ['save','func'] 로 일부만 (save · import · func · scen · sim · lint)
// 결과는 POST /put/<이름> 으로 Tools/golden/m1/ 에 쓰인다. 계산은 모두 동기 구간에서 해서 페이지의 프레임 루프가 끼어들지 않게 한다.
(async () => {
  const only = window.__m1only || ['save', 'import', 'func', 'scen', 'lint', 'sim'];
  const files = {};
  const RealDate = Date, FIX = RealDate.UTC(2026, 10, 11, 3, 30, 0);          // 2026-11-11 03:30 UTC (한국 12:30)
  const TZ = new RealDate(FIX).getTimezoneOffset();                           // 분 (한국 -540). C# 시험 시계가 같은 값을 쓴다
  globalThis.Date = class extends RealDate { constructor(...a) { if (a.length === 0) super(FIX); else super(...a); } static now() { return FIX; } };
  const clone = o => JSON.parse(JSON.stringify(o));
  const put = (name, obj) => { files[name] = typeof obj === 'string' ? obj : JSON.stringify(obj); };
  const mk = over => Object.assign(SAVE_DEFAULTS(), over || {});
  const meta = { fix: FIX, tzOffsetMinutes: TZ, saveVer: SAVE_VER, md5: 'e36a97797597bf77dceae187eacf51a1' };
  const jstr = v => JSON.stringify(v === undefined ? null : v);

  // ============ G-SAVE : migrate ============
  if (only.includes('save')) {
    const L = (id, extra) => Object.assign({ id, ship: '순풍', cap: '마리', gift: 'shell', t: 1700000000000 + id, read: false, text: '안녕하세요 ' + id, sign: '순풍호 마리 드림' }, extra || {});
    const big = 'ㄱ'.repeat(2000);
    const cases = [
      ['empty', {}], ['null', null], ['array', [1, 2]], ['number', 42], ['string', 'abc'],
      ['v1_min', { guided: 5, stage: 2 }],
      ['v1_stage3', { guided: 40, stage: 3, gifts: { shell: 4 }, letters: [L(1), L(2)] }],
      ['stageV2_nover', { stageV: 2, stage: 3, guided: 20, build: { to: 4, startAt: 1000, endAt: 2000 }, gifts: { pearl: 2 } }],
      ['stageV2_badbuild', { stageV: 2, stage: 2, build: { to: 9, startAt: 1000, endAt: 2000 } }],
      ['v2_stage1', { ver: 2, stage: 1, guided: 3 }], ['v2_stage2_build', { ver: 2, stage: 2, build: { to: 3, startAt: 5, endAt: 6 } }],
      ['v3_basic', { ver: 3, stage: 2, guided: 30, letters: [L(1)] }],
      ['v4_guided17', { ver: 4, stage: 2, guided: 17, letters: [L(1), L(2)] }], ['v4_guided3', { ver: 4, stage: 1, guided: 3 }],
      ['v5_ads', { ver: 5, stage: 3, guided: 90, ads: { day: '2026-11-10', used: { home: 1, star: 2, ship: 3 }, homeDay: '2026-11-10' } }],
      ['v6_letters', { ver: 6, stage: 3, guided: 90, letters: [L(1, { t: 1600000000000 }), L(2, { t: 1650000000000 }), L(3, { t: 'x' })] }],
      ['v7_gifts', { ver: 7, stage: 4, guided: 500, gifts: { shell: 4, pearl: 1, bogus: 5 }, shiny: { shell: 1, coral: 2 } }],
      ['v8_deco', { ver: 8, stage: 4, guided: 700, deco: ['shell', null, 'pearl'], tips: { h1: 1 } }],
      ['v8_guided30', { ver: 8, stage: 3, guided: 30 }],
      ['v9_deco', { ver: 9, stage: 5, guided: 3000, deco: [null, 'shell'], isle: 'bad' }],
      ['v10_full', { ver: 10, stage: 6, guided: 9000, growth: { lens: 130, garden: 90, port: 60 }, gifts: { shell: 5 }, dex: { fish: 3 }, reg: { swallow: { v: 3 } }, sea: { dolphin: 2 }, found: { shell: 1 }, isle: { v: 1, put: { P1: 'path_stone', B3: 'flower_planter', B1: 'lamp_post', F1: 'bogus', SH2: 'palm_tree' }, own: { pumpkin_pile: 1, path_stone: 1 }, news: ['pumpkin_pile', 'bogus'], mig: 1, tipSeen: 1 }, skin: { tower: 'dawn', isle: 'classic', keeper: 'nope' }, season: 'winter', beam: 'silver', region: 'home' }],
      ['v10_isle_put_open', { ver: 10, stage: 1, growth: { garden: 0 }, isle: { put: { P1: 'path_stone', P5: 'path_stone', F5: 'fence_wood', SK1: 'cloud_small', SK2: 'cloud_small' }, own: {} } }],
      ['v10_isle_put_open6', { ver: 10, stage: 6, growth: { garden: 10000 }, isle: { put: { P1: 'path_stone', P5: 'path_stone', F5: 'fence_wood', SK1: 'cloud_small', F2: 'fence_wood', B2: 'tent_small', B3: 'fence_wood', B5: 'fence_wood' }, own: {} }, reg: { swallow: { v: 6 } }, totals: { gifts: { shell: 25 } } }],
      ['v11_newer', { ver: 11, stage: 2, extra: 'keep', letters: [L(1)] }],
      ['t_ctrl_chars', { ver: 10, letters: [L(1, { text: 'a\u0000b\u001fc<script>', sign: 's\u0007' })] }],
      ['t_long_text', { ver: 10, letters: [L(1, { text: big, ship: big, cap: big, sign: big, nm: big })] }],
      ['t_unknown_gift', { ver: 10, letters: [L(1, { gift: 'zzz' }), L(2, { gift: '' }), L(3, { gift: 'constructor' }), L(4, { gift: 7 })] }],
      ['t_letter_ids', { ver: 10, letterId: 3, letters: [L(5), L(5), L(-1), L(1.5), L('x'), L(2e12), { text: 'no id', t: 1 }, { nope: 1 }, null, 'str', [1]] }],
      ['t_letter_kinds', { ver: 10, letters: [L(1, { keep: 'bogus' }), L(2, { keep: true }), L(3, { keep: 'nature' }), L(4, { reg: 'swallow', region: 'home', shiny: true, nm: 'n', read: true }), L(5, { reg: 'bogus', region: 'mars', shiny: 1, read: 'yes' })] }],
      ['t_letters_many', { ver: 10, letters: Array.from({ length: 320 }, (_, i) => L(i + 1)) }],
      ['t_proto_keys', JSON.parse('{"ver":10,"gifts":{"__proto__":{"x":1},"constructor":2,"shell":3},"dex":{"toString":1,"fish":2},"reg":{"__proto__":{"v":9}},"stats":{"valueOf":1,"a":1},"tips":{"hasOwnProperty":1,"h1":1},"totals":{"gifts":{"__proto__":{"y":1},"pearl":2}}}')],
      ['t_bad_times', { ver: 10, startAt: 1e15, lastSeen: -5, finale: 'x', build: { to: 3, startAt: 1, endAt: 1e15 } }],
      ['t_build_ok', { ver: 10, stage: 3, build: { to: 4, startAt: 5, endAt: 10 } }],
      ['t_choices', { ver: 10, season: 'toString', focus: 'hasOwnProperty', offerTo: 'port', clock: 'bogus', beam: 'zzz', region: '__proto__', view: 'bogus', skin: { tower: 'bogus', isle: 'classic', keeper: 7, decor: 'classic', ship: 'dev' } }],
      ['t_view_ok', { ver: 10, view: 'wide', season: 'autumn', focus: 'garden', clock: 'real', beam: 'violet' }],
      ['t_types', { ver: 10, guided: 'x', seen: 1, gifts: [], dex: 'a', growth: { lens: 'a' }, letters: {}, stage: '3', lastSeen: '5', tipsOff: 'yes', autoOffer: 0, totals: 5, build: 7, isle: [] }],
      ['t_ads_future', { ver: 10, ads: { day: '2031-01-01', used: { home: 1, star: 1, ship: 1 }, homeDay: '' } }],
      ['t_ads_bad', { ver: 10, ads: { day: 'abc', used: { home: 1 }, homeDay: 'x' } }],
      ['t_ads_past', { ver: 10, ads: { day: '2026-11-10', used: { home: 1, star: 0, ship: 2 }, homeDay: '2026-11-10' } }],
      ['t_found_from_gifts', { ver: 10, gifts: { shell: 1, pearl: 2 }, found: {} }],
      ['t_extra_fields', { ver: 10, unknownField: { a: 1 }, wake: true, lastTab: 'mail', decoTab: 'skin', growSub: 'log' }],
      ['t_totals_bad', { ver: 7, totals: { gifts: [] } }],
      ['t_ver_float', { ver: 3.5, stage: 2 }], ['t_ver_zero', { ver: 0, guided: 1 }], ['t_ver_neg', { ver: -3, guided: 1 }]
    ];
    const out = cases.map(([name, input]) => {
      const text = JSON.stringify(input === undefined ? null : input), inp = JSON.parse(text);
      let res; try { res = migrate(JSON.parse(text)); } catch (e) { res = { __error: String(e.message) }; }
      return { name, input: inp, inputText: text, expected: JSON.parse(jstr(res)) };
    });
    put('save_migrate.json', { meta, cases: out });
  }

  // ============ G-SAVE : sanitizeImport + parseSaveCode + LHS1 ============
  if (only.includes('import')) {
    const L = (id, extra) => Object.assign({ id, ship: '순풍', cap: '마리', gift: 'shell', t: 1700000000000 + id, read: false, text: '안녕하세요 ' + id, sign: '순풍호 마리 드림' }, extra || {});
    const base = (over) => Object.assign({ ver: 10, guided: 12, seen: true, gifts: { shell: 3 }, dex: { fish: 2 }, growth: { lens: 5, garden: 2, port: 1 }, letters: [L(1)], stage: 2, isle: { v: 1, put: {}, own: {}, news: [], mig: 0, tipSeen: 0 } }, over || {});
    const enc = o => { const body = btoa(unescape(encodeURIComponent(typeof o === 'string' ? o : JSON.stringify(o)))); return `LHS1.${body}.${hashStr(body)}`; };
    const obj = [
      ['basic', base()],
      ['rich', base({ stage: 6, guided: 9999, growth: { lens: 130, garden: 90, port: 60 }, gifts: { shell: 4, pearl: 2 }, shiny: { coral: 1 }, found: { shell: 1, coral: 1 }, dex: { fish: 9, trade: 2 }, sea: { dolphin: 3, rainbow: 1 }, reg: { swallow: { v: 4 }, moon: { v: 1 } }, tips: { h1: 1, h2: 1 }, anniv: { 7: 1 }, seasonLetters: { '2026:autumn': 1, '0000:sky': 1 }, finale: 1700000000000, diarySeen: { dolphin: 1 }, totals: { gifts: { shell: 9 } }, skin: { tower: 'dawn', isle: 'maple' }, beam: 'rose', season: 'winter', focus: 'lens', offerTo: 'port', autoOffer: false, clock: 'real', region: 'home', startAt: 1690000000000, lastSeen: 1700000000000, offered: 77, wake: true, sound: false, letters: [L(1, { reg: 'swallow', keep: true }), L(2, { gift: 'pearl', shiny: true, nm: 'x' }), L(3, { keep: 'season' })], owned: { noads: 1 }, ads: { day: '2026-11-11', used: { home: 1, star: 1, ship: 1 }, homeDay: '2026-11-11' }, isle: { v: 1, put: { P1: 'path_stone', SH1: 'fence_wood' }, own: { pumpkin_pile: 1, path_stone: 1 }, news: ['pumpkin_pile'], mig: 1, tipSeen: 1 }, build: { to: 5, startAt: 1700000000000, endAt: 1700000100000 }, view: 'near', lastTab: 'mail', decoTab: 'skin', growSub: 'mile' })],
      ['old_deco', base({ ver: 8, deco: ['shell', 'bogus', null, 'pearl'], isle: undefined })],
      ['old_deco_v9', base({ ver: 9, deco: [null, 'shell'], isle: undefined })],
      ['old_deco_v10_ignored', base({ ver: 10, deco: ['shell'] })],
      ['nover_stagev', { stageV: 2, stage: 3, guided: 30, gifts: { shell: 2 }, growth: { lens: 1 }, dex: {}, letters: [], deco: [] }],
      ['t_ver_huge', base({ ver: 1e9 })], ['t_ver_neg', base({ ver: -5 })], ['t_ver_float', base({ ver: 4.5 })],
      ['t_nums', base({ guided: 1e30, letterId: 1e30, dexSeen: -4, lastSeen: 5e15, stage: 6, offered: -1, startAt: 'x', finale: 1e30, growth: { lens: 1e30, garden: -3, port: 'a' } })],
      ['t_counts', base({ gifts: { shell: -5, pearl: 3.7, coral: 0, seed: '5', anchor: 1e12, bogus: 4, constructor: 1 }, shiny: { shell: 1e30, pearl: 2 }, found: { shell: true, pearl: 0, coral: 'a', bogus: 1 }, dex: { fish: 2.9, nope: 1 }, sea: { dolphin: 1, nope: 2, rainbow: -1 } })],
      ['t_proto', JSON.parse('{"ver":10,"guided":1,"gifts":{"__proto__":{"x":1},"shell":2},"growth":{"__proto__":{"lens":99}},"dex":{},"letters":[],"stage":1,"isle":{"put":{"__proto__":"path_stone"},"own":{"__proto__":1}},"reg":{"__proto__":{"v":3}},"stats":{"__proto__":1,"ok_1":2},"tips":{"constructor":1}}')],
      ['t_keys', base({ stats: { 'a-b': 1, ok_1: 2, ['x'.repeat(31)]: 3 }, tips: { h1: 1, 'bad key': 1, ['y'.repeat(21)]: 1 }, anniv: { 7: 1, 99999: 1, 123456: 1, abc: 1 }, seasonLetters: { '2026:autumn': 1, '0000:sky': 1, '26:spring': 1, '2026:Autumn': 1 }, diarySeen: { dolphin: 1, Dolphin: 1, abcdefghijklm: 1 } })],
      ['t_many_keys', base({ gifts: Object.fromEntries(Array.from({ length: 300 }, (_, i) => [i % 2 ? 'shell' : 'k' + i, i + 1])), stats: Object.fromEntries(Array.from({ length: 250 }, (_, i) => ['s' + i, 1])) })],
      ['t_choices', base({ focus: 'bogus', offerTo: 'garden', clock: 7, season: 'summer', beam: 'nope', region: 'mars', view: 'bogus', skin: { tower: 'dawn', isle: 'classic', keeper: 'nope', ship: 5, extra: 'x' } })],
      ['t_build', base({ build: { to: 1, startAt: -1, endAt: 1e15 } })], ['t_build2', base({ build: { to: 'x' } })], ['t_build3', base({ build: { to: 4.9, startAt: 5, endAt: 9 } })],
      ['t_letters', base({ letters: [L(1, { text: 'a'.repeat(2000) }), L(2, { gift: 'zzz' }), L(3, { keep: 'bogus' }), L(3), { text: 1 }, null, L(9, { reg: 'bogus', region: 'x' })] })],
      ['t_letters_nogift', base({ letters: [{ text: 't', t: 5 }, { text: 't2', t: 'x', gift: null }] })],
      ['t_isle', base({ isle: { v: 5, put: { P1: 'path_stone', zzz: 'path_stone', P2: 7, 'a b': 'x' }, own: { pumpkin_pile: 1, 'q r': 1, path_stone: 0 }, news: ['pumpkin_pile', 7, 'a b', 'x', 'y', 'z', 'a1', 'a2', 'a3'], mig: 'yes', tipSeen: 0 } })],
      ['t_tabs', base({ lastTab: 'shop', decoTab: 'gift', growSub: 'role' })], ['t_tabs2', base({ lastTab: 'deco', decoTab: 'shop', growSub: 'offer' })], ['t_tabs3', base({ lastTab: null, decoTab: 5, growSub: null })],
      ['t_wake', base({ wake: 'yes' })], ['t_wake2', base({ wake: false })],
      ['t_shape_noletters', { ver: 10, gifts: {}, growth: {}, dex: {}, stage: 1, isle: {} }],
      ['t_shape_stage99', base({ stage: 99 })], ['t_shape_stage0', base({ stage: 0 })], ['t_shape_stage7', base({ stage: 7 })], ['t_shape_stagestr', base({ stage: '2' })],
      ['t_shape_noisle', (() => { const o = base(); delete o.isle; return o; })()],
      ['t_shape_deco_only', (() => { const o = base({ deco: [] }); delete o.isle; return o; })()],
      ['t_shape_giftsarr', base({ gifts: [] })]
    ];
    const cases = obj.map(([name, o]) => {
      const text = JSON.stringify(o), code = enc(text);
      let san = null, sanErr = null; try { san = sanitizeImport(JSON.parse(text)); } catch (e) { sanErr = String(e.message); }
      const parsed = parseSaveCode(code);
      return { name, inputText: text, code, sanitized: san === null ? null : JSON.parse(jstr(san)), sanitizeError: sanErr, parsed: JSON.parse(jstr(parsed)) };
    });
    const raw = [
      ['raw_ok', enc(base())], ['raw_spaces', ' ' + enc(base()).replace('.', '. \n') + ' '], ['raw_empty', ''], ['raw_null', null], ['raw_noprefix', enc(base()).slice(5)],
      ['raw_badhash', enc(base()).replace(/\.[a-z0-9]+$/, '.zzzzzz')], ['raw_truncated', enc(base()).slice(0, -12)],
      ['raw_badjson', enc('{not json')], ['raw_notobj', enc('[1,2]')], ['raw_nostage', enc({ gifts: {}, growth: {}, dex: {}, letters: [], isle: {} })],
      ['raw_badb64', (() => { const body = '@@@@'; return `LHS1.${body}.${hashStr(body)}`; })()], ['raw_lower', enc(base()).replace('LHS1', 'lhs1')],
      ['raw_korean', enc(base({ letters: [L(1, { text: '한글 편지 ✓ 😀' })] }))]
    ].map(([name, text]) => { let r; try { r = parseSaveCode(text); } catch (e) { r = { __error: String(e.message) }; } return { name, code: text, parsed: JSON.parse(jstr(r)) }; });
    // LHS1 만들기: saveCode 와 같은 식(rest = save 에서 owned·ads 뺀 것)을 임의 저장에 적용
    const mkCode = s => { const { owned, ads, ...rest } = s, body = btoa(unescape(encodeURIComponent(JSON.stringify(rest)))); return { json: JSON.stringify(rest), body, code: `LHS1.${body}.${hashStr(body)}` }; };
    const exp = [mk({ stage: 3, guided: 90, letters: [Object.assign(L(1), { text: '한글 \n 줄 "따옴표" \\ 역슬래시 \u0001' })], growth: { lens: 1.5, garden: 0.1, port: 4.1e12 }, owned: { noads: 1 } }), migrate(clone(obj[1][1]))].map(mkCode);
    const hashes = ['', 'a', 'abc', 'LHS1', '한글', 'The quick brown fox jumps over the lazy dog', '😀', 'x'.repeat(5000)].map(t => ({ text: t, hash: hashStr(t) }));
    put('save_import.json', { meta, cases, raw, export: exp, hashes });
  }

  // ============ G-FUNC ============
  if (only.includes('func')) {
    const F = {};
    save.growth = { lens: 0, garden: 0, port: 0 };
    F.lvAt = Array.from({ length: 41 }, (_, i) => i + 1).map(n => lvAt(n));
    const profiles = [[0, 0, 0], [12, 0, 0], [11.9, 35, 70], [200, 150, 90], [3000, 2000, 1500], [100000, 80000, 60000], [1e7, 1e7, 1e7], [5, 5, 5], [34, 69, 119]];
    F.profiles = profiles.map(([l, g, p]) => {
      const sv = save.growth; save.growth = { lens: l, garden: g, port: p };
      const r = { growth: { lens: l, garden: g, port: p }, lv: { lens: lvlOf('lens'), garden: lvlOf('garden'), port: lvlOf('port') }, total: totalLv(), shiny: shinyChance(), harvest: harvestChance(), regBonus: regularBonus(), lensMul: lensMul(), lensShiny: lensShiny(), offlineMax: offlineMax(), rankIdx: rankIdx(), effLv: { lens: effLv('lens'), garden: effLv('garden'), port: effLv('port') } };
      save.growth = sv; return r;
    });
    F.offlineShips = [0, 1, 89, 90, 91, 100, 600, 1799, 1800, 3599, 3600, 7200, 28800, 30000, 50400, 55555.5, 66.66666666666667, 111.11111111111111, 0.9 / 60 * 3 * 1e9].map(a => ({ away: a, n: offlineShips(a) }));
    F.shipTypeAt = [];
    for (let st = 1; st <= 6; st++) { const sv = STAGE; STAGE = st; const rs = []; for (let i = 0; i <= 200; i++) rs.push(i / 200); rs.push(0.9999999999999999, 1e-17, 0.3, 0.7, 0.45, 0.65); F.shipTypeAt.push({ stage: st, r: rs, t: rs.map(r => shipTypeAt(r)) }); STAGE = sv; }
    // 슬롯 열림: 28 자리 × 단계 1~6 × 정원 레벨 0~30
    F.slotOpen = Object.keys(DECOR_SLOTS).map(id => ({ id, bits: [1, 2, 3, 4, 5, 6].map(st => Array.from({ length: 31 }, (_, gl) => slotOpenS(DECOR_SLOTS[id], st, gl) ? 1 : 0).join('')) }));
    // 슬롯 좌표(N·R 식) — 단계별
    F.slotPos = Object.keys(DECOR_SLOTS).map(id => { const s = DECOR_SLOTS[id]; return { id, ab: ISLE_NS.slice(1).map(N => typeof s.ab === 'function' ? s.ab(N) : s.ab || null), xy: [20, 30, 41, 55, 60, 77, 99.5, 100].map(R => typeof s.xy === 'function' ? s.xy(R) : null) }; });
    // 장식 보유
    const sts = [
      { growth: {}, reg: {}, totals: { gifts: {} }, isle: { own: {} } },
      { growth: { garden: 120, port: 60 }, reg: { swallow: { v: 3 } }, totals: { gifts: { shell: 20 } }, isle: { own: { pumpkin_pile: 1 } } },
      { growth: { garden: 12, port: 5000 }, reg: { swallow: { v: 2 } }, totals: { gifts: { shell: 19 } }, isle: { own: {} } },
      { growth: { garden: 70 }, reg: { swallow: { v: 6 }, moon: { v: 99 } }, totals: { gifts: { shell: 21, pearl: 5 } }, isle: { own: { path_stone: 1 } } },
      { growth: { garden: 'x' }, reg: { swallow: 5, __proto__x: 1 }, totals: null, isle: null }
    ];
    F.decorOwned = sts.map(s => ({ state: s, own: Object.keys(DECOR_ITEMS).concat(['bogus', 'constructor']).map(id => decorOwnedS(id, s) ? 1 : 0).join(''), ids: Object.keys(DECOR_ITEMS).concat(['bogus', 'constructor']), lv: ['lens', 'garden', 'port'].map(k => decorLv(s, k)) }));
    // 조사
    const josa = ['진주을(를) 받았어요', '조개껍데기{을/를} 두고', '불가사리이(가) 왔어요', '산호 조각{은/는} 예뻐요', '<b>진주</b>을(를)', '<b>조개</b></b>{을/를}', '벚꽃잎{이/가} 날려요', '단풍잎와(과) 눈꽃', '눈꽃{과/와} 단풍', 'abc을(를)', '가을(를)', '힣{을/를}', '한{을/를} 두', '구슬{을/를} 놓고 달{이/가} 떠요', '빗방울 구슬{은/는}', '고래 수염 빗{을/를}', '오래된 지도 조각{이/가}', '달조각을(를)', '이슬방울{을/를} 줍고', '안개 조각{이/가}', '유리병{을/를}', '옛 동전{이/가}', '작은 닻{을/를}', '바다 씨앗{을/를}', '해바라기{을/를}', '북 {을/를}', '(을)', '을(를)', '빛나는 진주{을/를} 받았어요', '0{을/를}', '쌀{은/는}', '밥{과/와} 국', '배{과/와} 돛', '꿈{은/는}', '섬{이/가}', '바다{이/가}', '물{을/를}', '꽃{을/를}', '하늘{이/가}', '별{을/를}'];
    F.fixJosa = josa.map(t => ({ t, o: fixJosa(t) }));
    // giftPts / affOf / autoTrack / spareOf
    F.giftPts = Object.keys(GIFTS).map(k => ({ k, aff: affOf(k), pts: ['lens', 'garden', 'port'].map(tr => [giftPts(k, tr, false), giftPts(k, tr, true), giftPts(k, tr, false, true), giftPts(k, tr, true, true)]) }));
    // duskAt / isDawnAt / smooth
    const sv0 = save.clock;
    F.dusk = ['fast', 'real'].map(clock => { save.clock = clock; const hours = []; for (let h = 0; h < 24; h++) for (const m of [0, 15, 29, 30, 45, 59]) { const ms = RealDate.UTC(2026, 5, 1, h, m, 7) + TZ * 60000; hours.push({ ms, d: duskAt(ms), dawn: isDawnAt(ms) }); } for (let i = 0; i < 40; i++) { const ms = 1700000000000 + i * 37123; hours.push({ ms, d: duskAt(ms), dawn: isDawnAt(ms) }); } return { clock, rows: hours }; });
    save.clock = sv0;
    // SAFE_ID, dayNum, adsDayBad, whenParts, dayKeyOf
    F.safeId = ['shell', 'a-b', 'A_1:x', '', 'x'.repeat(40), 'x'.repeat(41), 'constructor', '__proto__', 'toString', 'a b', '한글', 'valueOf', 'hasOwnProperty', 7, null, 'ok:1'].map(v => ({ v, o: SAFE_ID(v) }));
    F.dayNum = ['2026-11-11', '2026-02-30', '1970-01-01', 'abc', '2026-1-1', '2099-12-31', 5, null].map(v => ({ v, o: dayNum(v), bad: adsDayBad(v) }));
    F.dayKeyOf = [0, 1700000000000, FIX, 4.1e12, 86399999].map(v => ({ v, o: dayKeyOf(v) }));
    F.whenParts = [FIX, FIX - 3600e3 * 5, FIX - 86400e3, FIX - 86400e3 * 3, FIX - 3600e3 * 12.5, RealDate.UTC(2026, 10, 10, 15, 5, 0), RealDate.UTC(2026, 10, 10, 17, 5, 0), RealDate.UTC(2026, 10, 10, 20, 30, 0), NaN].map(ms => ({ ms: isNaN(ms) ? null : ms, o: whenParts(new Date(ms)) }));
    F.curSeason = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11].map(mo => ({ month: mo + 1, auto: (() => { const f = seasonForce, s = save.season; seasonForce = null; save.season = 'auto'; globalThis.Date = class extends RealDate { constructor(...a) { if (a.length === 0) super(RealDate.UTC(2026, mo, 15, 12)); else super(...a); } static now() { return RealDate.UTC(2026, mo, 15, 12); } }; const r = curSeason(); globalThis.Date = class extends RealDate { constructor(...a) { if (a.length === 0) super(FIX); else super(...a); } static now() { return FIX; } }; seasonForce = f; save.season = s; return r; })() }));
    F.seasonKey = ['spring', 'summer', 'autumn', 'winter'].map(s => { const f = seasonForce; seasonForce = s; const r = seasonKey(); seasonForce = f; return { season: s, key: r }; });
    // dueOf (상점 차감 가격: 순수 계산) — 7상품 × 보유 부분집합 전수
    F.dueOf = PRODUCTS.map(p => { const rows = []; const n = p.grants.length; for (let m = 0; m < (1 << n); m++) { const has = id => { const i = p.grants.indexOf(id); return i >= 0 && !!(m & (1 << i)); }; rows.push({ mask: m, due: dueOf(p, has) }); } return { id: p.id, price: p.price, grants: p.grants, rows }; });
    F.itemValue = ['noads', 'beam:silver', 'tower:dawn', 'skin:x'].map(g => ({ g, v: itemValue(g) }));
    F.adsDay = []; { const t = todayKey(); F.todayKey = t; }
    F.nextMile = ['lens', 'garden', 'port'].map(k => [0, 4, 5, 9, 12, 20, 30].map(lv => { const m = nextMile(k, lv); return m ? m.text : null; }));
    F.giftWhen = Object.keys(GIFTS).map(k => ({ k, o: giftWhen(GIFTS[k]) }));
    F.seaGrp = Object.keys(SEA).map(k => ({ k, grp: seaGrp(k) }));
    put('func.json', { meta, ...F });
  }

  // ============ G-LINT : FORBID_RE 등 ============
  if (only.includes('lint')) {
    const sents = [];
    const add = (t, tag) => sents.push({ t, tag });
    // 실제 문구 전부(통과해야 함)
    const real = new Set();
    const eat = v => { if (typeof v === 'string') real.add(v); else if (Array.isArray(v)) v.forEach(eat); else if (v && typeof v === 'object') Object.values(v).forEach(eat); };
    [L_OPEN, L_DAY, L_NIGHT, L_MID, L_THANKS, L_GIFT, L_CLOSE, L_SEASON_LETTER, L_SEASON_DIARY, CAPTAINS, SHIPNAMES].forEach(eat);
    DIARY.forEach(e => eat(e.text)); REGULARS.forEach(r => { eat(r.story); eat(r.ship); eat(r.cap); }); SEASON_GUESTS.forEach(g => eat(g.text)); ANNIV_LETTERS.forEach(g => eat(g.text));
    NATURE_LETTERS.forEach(L => { eat(L.text); eat(L.nm); eat(L.sign); }); Object.values(SEA).forEach(v => { eat(v.name); eat(v.where); eat(v.hint); eat(v.tag || ''); }); Object.values(GIFTS).forEach(g => eat(g.name));
    real.forEach(t => add(t, 'real'));
    const block = ['빨리 오세요', '서둘러 주세요', '당장 해야 해요', '꼭 한 번 와 주세요', '반드시 들러야 합니다', '마감이 곧 다가와요', '기간 한정 선물', '오늘만 받을 수 있어요', '선물을 놓치지 마세요', '기록이 사라져 버려요', '미안해요 늦었네요', '많이 외로웠어요', '연속 접속 5일째', '출석 체크', '순위가 올랐어요', '다른 등대지기보다 앞서요', '어린이 여러분', '용돈을 모아서', '곧 사라질 거예요', '접속해 주세요', '로그인 보상', '친구들은 벌써 더 높아요', '남들보다 뒤처져요', '할인 중이에요', '지금 구매하면 두 배', '이벤트 미션 도전', '서운해요', '눈물이 났어요', '어제보다 선물이 줄었어요', '타이머가 흘러요', '카운트다운 시작', '당신 때문에 늦었어요', '마지막 날이에요', '빚진 마음이에요', '아직도 안 오셨나요', '늦으면 떠나요', '내일은 없을지도 몰라요', '이번 주까지만', '이번 달 안에', '얼른 와요', '지금 바로 받으세요', '써야 해요', '와야 해요', '잊지 마세요', '면 안 돼요', '꺼뜨리지 마세요', '남은 시간이 얼마 없어요', '잃게 되어요', '손해예요', '날아가요', '선물이 사라져요', '레벨이 줄어요', '걱정돼요', '혼자 두지 마요', '기다리고 있었어요', '며칠째 안 오셨어요', '개근 상', '3위예요', '2등이에요', '경쟁해요', '비교해 봐요', '꼬마들아', '부모님께 여쭤봐요', '곧 끝나요', '없어져 버려요', '다시는 못 해요', '오지 않을 거예요', '얼마 남지 않았어요', '이틀째', '한 달째', '벌써 3일째', '12 레벨이 되었어요', '이웃들이 앞서요', '쉬면 안 돼요', '게을러요', '꼴찌예요', '1등이에요', '최고의 등대지기', '당신만 기다려요', '배들이 기다려요', '특가', '세일', '퀘스트', '챌린지', '오늘의 목표', '보너스 받기', '아쉬워요', '허전해요', '안타까워요', '쓸쓸해요', '섭섭해요'];
    const allow = ['서두르지 않고 천천히 갔어요', '꼭 한 번 들러 보고 싶었어요', '길을 잃지 않았어요', '무지개가 사라졌어요', '매일 아침 인사해요', '매일같이 와요', '이번 주말에 만나요', '가야금 소리가 들려요', '시간이 없어서 못 왔는데', '남은 시간을 사랑해요', '등대지기님 안녕하세요', '5등대 항로', '1등급 선박', '걱정하지 마세요', '걱정하지 않아요', '비교적 맑아요', '오늘은 맑아요', '아직 어두워요'];
    block.forEach(t => add(t, 'block')); allow.forEach(t => add(t, 'allow'));
    // 변형: 조합
    for (const t of block.slice(0, 40)) add('등대지기님께, ' + t + ' 안녕히.', 'combo');
    const rows = sents.map(({ t, tag }) => ({ t, tag, forbid: FORBID_RE.test(t), josa: JOSA_LEFT_RE.test(t), day: DAY_BAN_RE.test(t), diary: DIARY_BAN_RE.test(t) }));
    const cc = checkContent();
    // 일부러 망가뜨린 상태에서의 checkContent(원본이 잡는지): 잠깐 바꿨다 되돌린다
    const probes = [];
    { const bak = GIFTS.shell.name; GIFTS.shell.name = '빨리'; const r = checkContent(); GIFTS.shell.name = bak; probes.push({ probe: 'gift name', n: r.length }); }
    { const bak = SEA.fish.where; SEA.fish.where = '당장 와요'; const r = checkContent(); SEA.fish.where = bak; probes.push({ probe: 'sea where', n: r.length, first: r[0] }); }
    { const bak = AFFINITY.shell; delete AFFINITY.shell; const r = checkContent(); AFFINITY.shell = bak; probes.push({ probe: 'affinity missing', n: r.length, first: r[0] }); }
    { const bak = ICONS.shell; delete ICONS.shell; const r = checkContent(); ICONS.shell = bak; probes.push({ probe: 'icon missing', n: r.length, first: r[0] }); }
    put('lint.json', { meta, sentences: rows, checkContent: cc, probes, counts: { real: real.size, block: block.length, allow: allow.length } });
  }

  // ============ G-SCEN : 씨앗 고정 시나리오 (실제 함수 호출 → 반환값·저장·난수 호출 수) ============
  if (only.includes('scen')) {
    const L = (id, extra) => Object.assign({ id, ship: '순풍', cap: '마리', gift: 'shell', t: 1700000000000 + id, read: false, text: '안녕 ' + id, sign: '순풍호 마리 드림', region: 'home' }, extra || {});
    const scen = [];
    const cnt = { n: 0 };
    const run = (name, def) => {
      const r0 = seededRandom(def.seed); let calls = 0; Math.random = () => (calls++, r0());
      for (const k of Object.keys(letterRecent)) delete letterRecent[k];
      for (const k of Object.keys(save)) delete save[k];
      Object.assign(save, migrate(clone(def.save || {})));
      STAGE = def.stage || 1; save.stage = STAGE; seasonForce = def.season || 'summer'; NIGHT = !!def.night; wx.kind = def.wx || 'clear'; condOv = null; SIM = false; firstQ.length = 0; growthEvent = null; needRebuild = false; shownCnt = 0;
      let result = null, err = null;
      const shipsBak = ships.splice(0, ships.length);                 // 페이지가 띄워 둔 배(단골 배 포함)가 pickRegular 후보를 줄이지 않게 비운다
      try { result = def.act(); } catch (e) { err = String(e.stack || e.message).split('\n').slice(0, 3).join(' | '); }
      ships.push(...shipsBak);
      Math.random = seededRandomRestore;
      scen.push({ name, def: { seed: def.seed, stage: STAGE0(def), season: seasonForce, night: !!def.night, wx: def.wx || 'clear', save: clone(def.save || {}), act: def.desc }, result: JSON.parse(jstr(result)), error: err, save: JSON.parse(jstr(save)), calls, letterRecent: JSON.parse(jstr(letterRecent)), growthEvent, needRebuild, firstQ: JSON.parse(jstr(firstQ)) });
    };
    const STAGE0 = d => d.stage || 1; const seededRandomRestore = Math.random;
    const sav = (o) => Object.assign({ ver: 10, startAt: FIX - 3 * 86400e3, lastSeen: 0, guided: 40, seen: true }, o || {});
    const aways = [60, 100, 299, 301, 1800, 5000, 10800, 15000, 28800, 43200, 90000];
    for (const st of [1, 3, 6]) for (const away of aways) {
      const seed = 1000 + st * 100 + away % 97;
      run(`offline_s${st}_a${away}`, { seed, stage: st, save: sav({ growth: { lens: st * 20, garden: st * 10, port: st * 8 }, guided: 20 * st }), desc: `runOffline(${away}) stage ${st}`, act: () => runOffline(away) });
    }
    run('offline_first_back', { seed: 7, stage: 2, save: sav({ guided: 0, firstBack: false }), desc: 'runOffline(600) 첫 복귀', act: () => runOffline(600) });
    run('offline_first_back_met', { seed: 8, stage: 2, save: sav({ guided: 5, firstBack: false, reg: { swallow: { v: 2 } } }), desc: 'runOffline(600) 이미 단골 만남', act: () => runOffline(600) });
    run('offline_night_diary', { seed: 11, stage: 4, night: true, save: sav({ guided: 700, growth: { port: 300, garden: 200, lens: 100 }, diarySeen: { dolphin: 1 } }), desc: 'runOffline(30000) 밤', act: () => runOffline(30000) });
    run('offline_winter_s5', { seed: 12, stage: 5, season: 'winter', wx: 'rain', save: sav({ guided: 3000, growth: { port: 900, garden: 300, lens: 400 }, season: 'winter' }), desc: 'runOffline(50400) 겨울 5단계', act: () => runOffline(50400) });
    run('offline_noautooffer', { seed: 13, stage: 3, save: sav({ autoOffer: false, guided: 100 }), desc: 'runOffline(20000) 자동헌정 끔', act: () => runOffline(20000) });
    run('offline_focus', { seed: 14, stage: 3, save: sav({ focus: 'port', offerTo: 'lens', guided: 100 }), desc: 'runOffline(20000) focus port offerTo lens', act: () => runOffline(20000) });
    run('offline_real_clock', { seed: 15, stage: 3, save: sav({ clock: 'real', guided: 100 }), desc: 'runOffline(15000) real', act: () => runOffline(15000) });
    run('offline_other_region_gifts', { seed: 16, stage: 3, save: sav({ guided: 100, region: 'bogus' }), desc: 'runOffline 바다 이상', act: () => runOffline(15000) });
    run('ships_300_quiet', { seed: 21, stage: 3, save: sav({ guided: 0 }), desc: '배 300척 quiet (sim 방식 아님)', act: () => { const rs = []; for (let i = 0; i < 300; i++) { condOv = sampleCond(); const r = rewardShip(shipTypeAt(Math.random()), false, true); condOv = null; rs.push([r.gift, r.shiny ? 1 : 0, r.letter ? r.letter.id : 0, r.extra ? r.extra.gift : 0, r.guests]); } return rs; } });
    run('ships_300_s6_garden', { seed: 22, stage: 6, save: sav({ guided: 9000, growth: { lens: 5000, garden: 4000, port: 3000 } }), desc: '배 300척 6단계 풍년', act: () => { const rs = []; for (let i = 0; i < 300; i++) { const r = rewardShip(shipTypeAt(Math.random()), false, true); rs.push([r.gift, r.shiny ? 1 : 0, r.letter ? r.letter.id : 0, r.extra ? r.extra.gift : 0, r.guests]); } return rs; } });
    run('ships_nocond_night_fog', { seed: 23, stage: 4, night: true, wx: 'fog', save: sav({ guided: 700 }), desc: '배 200척 condNow (밤·안개)', act: () => { const rs = []; for (let i = 0; i < 200; i++) { const r = rewardShip(shipTypeAt(Math.random()), false, true); rs.push([r.gift, r.shiny ? 1 : 0]); } return rs; } });
    run('ships_rain_winter', { seed: 24, stage: 5, wx: 'rain', season: 'winter', save: sav({ guided: 3000, season: 'winter' }), desc: '배 100척 겨울 비', act: () => { const rs = []; for (let i = 0; i < 100; i++) { const r = rewardShip(shipTypeAt(Math.random()), false, true); rs.push([r.gift, r.shiny ? 1 : 0]); } return rs; } });
    run('letters_40', { seed: 31, stage: 3, save: sav({}), desc: '편지 40통 낮 가을', season: 'autumn', act: () => { const rs = []; const gs = Object.keys(GIFTS); for (let i = 0; i < 40; i++) { const l = makeLetter(gs[i % gs.length], i % 5 === 0); rs.push(l); } return rs; } });
    run('letters_night_winter', { seed: 32, stage: 3, night: true, season: 'winter', save: sav({}), desc: '편지 20통 밤 겨울', act: () => { const rs = []; for (let i = 0; i < 20; i++) rs.push(makeLetter('shell', false)); return rs; } });
    run('letters_o_night', { seed: 33, stage: 3, save: sav({}), desc: 'makeLetter o.night=true o.season=spring', act: () => { const rs = []; for (let i = 0; i < 12; i++) rs.push(makeLetter('pearl', i % 2 === 0, { night: true, season: 'spring' })); return rs; } });
    run('diary_8h', { seed: 41, stage: 3, save: sav({}), desc: 'makeDiary(28800)', act: () => makeDiary(28800) });
    run('diary_30m', { seed: 42, stage: 1, save: sav({}), desc: 'makeDiary(1800)', act: () => makeDiary(1800) });
    run('diary_14h_s5_winter', { seed: 43, stage: 5, season: 'winter', save: sav({ diarySeen: { dolphin: 1, meteor: 1 } }), desc: 'makeDiary(50400)', act: () => makeDiary(50400) });
    run('diary_week_false', { seed: 44, stage: 3, save: sav({ startAt: FIX - 30 * 86400e3 }), desc: 'makeDiary(28800) 첫 주 아님', act: () => makeDiary(28800) });
    run('diary_o_given', { seed: 45, stage: 4, save: sav({}), desc: 'makeDiary(20000, {tier:3,week:true,season:autumn,now:FIX,seen:{}})', act: () => makeDiary(20000, { tier: 3, week: true, season: 'autumn', now: FIX, seen: {} }) });
    run('visit_regular_30', { seed: 51, stage: 4, save: sav({}), desc: 'visitRegular swallow 30회 quiet', act: () => { const rs = []; for (let i = 0; i < 30; i++) { const l = visitRegular({ reg: 'swallow' }, true); rs.push(l ? [l.id, l.gift, l.text.slice(0, 10)] : null); } return rs; } });
    run('visit_regular_all', { seed: 52, stage: 6, save: sav({}), desc: '단골 8명 × 26회', act: () => { const rs = []; for (const r of REGULARS) for (let i = 0; i < 26; i++) { const l = visitRegular({ reg: r.id }, true); if (l) rs.push([r.id, l.id, l.gift]); } return rs; } });
    run('pick_regular', { seed: 53, stage: 6, save: sav({ growth: { port: 2000 }, reg: { swallow: { v: 6 }, moon: { v: 1 } } }), desc: 'pickRegular 200번', act: () => { const rs = []; for (let i = 0; i < 200; i++) { const r = pickRegular(); rs.push(r ? r.id : null); } return rs; } });
    run('guests_season', { seed: 61, stage: 3, season: 'autumn', save: sav({ guided: 10, startAt: FIX - 8 * 86400e3 }), desc: 'deliverGuests quiet (가을 + 7일)', act: () => deliverGuests(true) });
    run('guests_anniv_all', { seed: 62, stage: 3, season: 'winter', save: sav({ guided: 10, startAt: FIX - 400 * 86400e3 }), desc: 'deliverGuests quiet (겨울 + 365일)', act: () => deliverGuests(true) });
    run('guests_not_yet', { seed: 63, stage: 3, season: 'winter', save: sav({ guided: 4, startAt: FIX - 2 * 86400e3 }), desc: 'deliverGuests (guided 4)', act: () => deliverGuests(true) });
    run('guests_twice', { seed: 64, stage: 3, season: 'spring', save: sav({ guided: 10, startAt: FIX - 40 * 86400e3 }), desc: 'deliverGuests 두 번', act: () => [deliverGuests(true), deliverGuests(true)] });
    run('finale', { seed: 71, stage: 6, save: sav({ guided: 9000, reg: { swallow: { v: 2 }, moon: { v: 1 } } }), desc: 'deliverFinale quiet', act: () => [deliverFinale(true), deliverFinale(true)] });
    run('finale_stage5', { seed: 72, stage: 5, save: sav({ guided: 9000 }), desc: 'deliverFinale 5단계', act: () => deliverFinale(true) });
    run('finale_none_met', { seed: 73, stage: 6, save: sav({}), desc: 'deliverFinale 단골 없음', act: () => deliverFinale(true) });
    run('autooffer', { seed: 81, stage: 3, save: sav({ gifts: { shell: 9, pearl: 3, blossom: 7, coral: 4 }, shiny: { shell: 3, coral: 1, maple: 4 }, totals: { gifts: {} } }), desc: 'autoOfferAll', act: () => autoOfferAll() });
    run('autooffer_focus', { seed: 82, stage: 3, save: sav({ offerTo: 'garden', gifts: { shell: 9, blossom: 7, snow: 8 }, shiny: { maple: 4 } }), desc: 'autoOfferAll offerTo garden', act: () => autoOfferAll() });
    run('offer_each', { seed: 83, stage: 3, save: sav({ gifts: { shell: 9, pearl: 5, blossom: 7, moonshard: 3 }, shiny: { shell: 3 } }), desc: 'offer 여러 형태', act: () => [offer('shell', 'auto', 5, false), offer('pearl', 'lens', 9, false), offer('blossom', 'auto', 7, false), offer('moonshard', 'auto', 3, false), offer('shell', 'port', 5, true), offer('coral', 'auto', 2, false)] });
    run('addgift_seq', { seed: 84, stage: 3, save: sav({ growth: { lens: 400, garden: 100, port: 100 } }), desc: 'addGift 100번(렌즈 높음)', act: () => { const rs = []; for (let i = 0; i < 100; i++) rs.push(addGift('shell', { quiet: true }) ? 1 : 0); return rs; } });
    run('addgift_opts', { seed: 85, stage: 3, save: sav({}), desc: 'addGift 옵션 shiny/noShiny', act: () => [addGift('shell', { quiet: true, shiny: true }), addGift('pearl', { quiet: true, noShiny: true }), addGift('coral', { quiet: true, shiny: false }), addGift('seed', { quiet: true })] });
    run('sea_nature', { seed: 91, stage: 5, save: sav({ guided: 20 }), desc: 'seeSea 24칸 + natureLetters', act: () => { const rs = []; for (const k of Object.keys(SEA)) { seeSea(k, false, true); rs.push(Object.keys(save.seasonLetters).length); } seeSea('dolphin', true, true); seeSea('dolphin', true, true); rs.push(save.sea.dolphin); return rs; } });
    run('sea_sky_only', { seed: 92, stage: 5, save: sav({ guided: 20 }), desc: 'seeSea 하늘 12칸', act: () => { for (const k of Object.keys(SEA)) if (SEA[k].grp === 'sky') seeSea(k, false, true); return Object.keys(save.seasonLetters); } });
    run('growth_bump', { seed: 93, stage: 3, save: sav({}), desc: 'bumpGrowth 레벨 오름·growthEvent', act: () => { const rs = []; for (const [k, p] of [['lens', 5], ['lens', 40], ['garden', 30], ['port', 700], ['garden', 5000]]) { bumpGrowth(k, p); rs.push([growthEvent, needRebuild]); } return rs; } });
    run('add_growth_focus', { seed: 94, stage: 3, save: sav({ guided: 7 }), desc: 'addGrowth 30번', act: () => { const rs = []; for (let i = 0; i < 30; i++) { save.guided++; addGrowth(); } return rs; } });
    run('add_growth_focus2', { seed: 95, stage: 3, save: sav({ guided: 7, focus: 'lens' }), desc: 'addGrowth focus lens 30번', act: () => { for (let i = 0; i < 30; i++) { save.guided++; addGrowth(); } return null; } });
    run('canupgrade', { seed: 96, stage: 3, save: sav({ guided: 400, found: { shell: 1, pearl: 1 }, dex: { fish: 1, trade: 1 } }), desc: 'canUpgrade 단계별', act: () => { const rs = []; for (let st = 1; st <= 6; st++) { STAGE = st; rs.push([st, !!canUpgrade(), discovered()]); } save.build = { to: 4, startAt: 1, endAt: 2 }; rs.push(['build', !!canUpgrade()]); return rs; } });
    run('discovered_misc', { seed: 97, stage: 3, save: sav({ guided: 50, found: { shell: 1 }, dex: { fish: 2 }, reg: { swallow: { v: 1 } } }), desc: 'discovered/unreadCount', act: () => [discovered(), unreadCount()] });
    run('add_letter_trim', { seed: 98, stage: 3, save: sav({ letters: Array.from({ length: 100 }, (_, i) => L(i + 1, i % 7 === 0 ? { reg: 'swallow' } : i % 11 === 0 ? { keep: 'home' } : {})), letterId: 101 }), desc: 'addLetter 40번 (한도 정리)', act: () => { for (let i = 0; i < 40; i++) addLetter(L(save.letterId++)); return save.letters.length; } });
    run('add_letter_allkeep', { seed: 99, stage: 3, save: sav({ letters: Array.from({ length: 100 }, (_, i) => L(i + 1, { reg: 'swallow' })), letterId: 101 }), desc: 'addLetter 전부 보존 편지', act: () => { addLetter(L(save.letterId++)); addLetter(L(save.letterId++, { keep: 'home' })); return save.letters.length; } });
    run('sample_cond', { seed: 101, stage: 5, season: 'summer', save: sav({}), desc: 'sampleCond 100번 5단계', act: () => Array.from({ length: 100 }, () => sampleCond()).map(c => (c.night ? 1 : 0) + (c.dawn ? 2 : 0) + (c.rain ? 4 : 0) + (c.fog ? 8 : 0)) });
    run('sample_cond_t1', { seed: 102, stage: 1, season: 'winter', save: sav({}), desc: 'sampleCond 50번 1단계 겨울', act: () => Array.from({ length: 50 }, () => sampleCond()).map(c => (c.night ? 1 : 0) + (c.dawn ? 2 : 0) + (c.rain ? 4 : 0) + (c.fog ? 8 : 0)) });
    run('roll_gift_boost', { seed: 103, stage: 5, wx: 'fog', night: true, save: sav({}), desc: 'rollGift boost fog/rain/none 각 60번', act: () => ['fog', 'rain', null].map(b => Array.from({ length: 60 }, () => rollGift('trade', b))) });
    run('roll_gift_dawn', { seed: 104, stage: 5, save: sav({ clock: 'real' }), desc: 'rollGift condNow(실시간 새벽 아님) 50번 fish', act: () => Array.from({ length: 50 }, () => rollGift('fish')) });
    run('roll_gift_ov', { seed: 105, stage: 6, save: sav({}), desc: 'rollGift condOv 전부 참 100번', act: () => { condOv = { night: true, dawn: true, rain: true, fog: true }; const r = Array.from({ length: 100 }, () => rollGift('cruise')); condOv = null; return r; } });
    put('scenarios.json', { meta, scenarios: scen });
  }

  // ============ G-SIM ============
  if (only.includes('sim')) {
    const sims = [];
    for (const [spd, days, seed, ve] of [[468, 120, 1, 1], [192, 120, 1, 1], [48, 120, 1, 1], [48, 120, 1, 3], [468, 120, 7, 1], [192, 120, 2, 1], [48, 120, 3, 3]]) {
      const t0 = performance.now(); const r = simulate(spd, days, seed, ve); sims.push({ args: [spd, days, seed, ve], result: JSON.parse(jstr(r)), ms: Math.round(performance.now() - t0) });
    }
    put('sim.json', { meta, sims });
  }

  globalThis.Date = RealDate;
  const names = Object.keys(files);
  for (const n of names) { const r = await fetch('/put/' + n, { method: 'POST', body: files[n] }); if (!r.ok) throw new Error('put 실패 ' + n); }
  window.__m1done = names.map(n => n + ':' + files[n].length);
  return window.__m1done;
})();
