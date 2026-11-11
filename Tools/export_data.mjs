// 등대지기 Unity 이식 M1 — 데이터 추출기 (동결 HTML → Resources/Data/*.json)
// 실행: node Tools/export_data.mjs [동결HTML경로] [출력폴더]
//   기본 입력  D:\Unity\EK_game_create\ek_Project_a9\lighthouse_idle.frozen-unity-m0.html (읽기만 한다)
//   기본 출력  Assets/_Lighthouse/Resources/Data   +  Tools/golden/m1/data_manifest.json
// 방법: 상수 이름으로 소스 구간을 잘라(중괄호 짝, 문자열·주석 인식) vm 샌드박스에서 평가한다.
//   평가 중 ReferenceError 가 나면 그 이름(상수/함수)의 소스를 같은 방식으로 찾아 먼저 평가한다(의존 자동 해결).
//   함수가 값인 표(DECOR_SLOTS 의 ab/xy, NATURE_LETTERS.need)는 선형식·규칙 이름으로 바꾸고, 평가값과 일치하는지 검증한다(불일치 시 실패).
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const srcPath = process.argv[2] || 'D:/Unity/EK_game_create/ek_Project_a9/lighthouse_idle.frozen-unity-m0.html';
const outDir = process.argv[3] || path.join(here, '..', 'Assets', '_Lighthouse', 'Resources', 'Data');
const html = fs.readFileSync(srcPath, 'utf8');
const md5 = crypto.createHash('md5').update(fs.readFileSync(srcPath)).digest('hex');
const a = html.indexOf('<script>'), b = html.lastIndexOf('</script>');
const S = html.slice(a + 8, b);

// ---------- 소스 자르기 ----------
function skipStr(s, i) {            // s[i] 가 따옴표일 때 닫는 따옴표 다음 위치
  const q = s[i];
  for (i++; i < s.length; i++) {
    if (s[i] === '\\') { i++; continue; }
    if (q === '`' && s[i] === '$' && s[i + 1] === '{') { i = skipBal(s, i + 1) - 1; continue; }
    if (s[i] === q) return i + 1;
  }
  throw new Error('문자열이 닫히지 않음');
}
function skipBal(s, i) {            // s[i] 가 여는 괄호일 때 짝 다음 위치
  let d = 0;
  for (; i < s.length; i++) {
    const c = s[i];
    if (c === '"' || c === "'" || c === '`') { i = skipStr(s, i) - 1; continue; }
    if (c === '/' && s[i + 1] === '/') { while (i < s.length && s[i] !== '\n') i++; continue; }
    if (c === '/' && s[i + 1] === '*') { i = s.indexOf('*/', i + 2) + 1; continue; }
    if (c === '(' || c === '[' || c === '{') d++;
    else if (c === ')' || c === ']' || c === '}') { d--; if (d === 0) return i + 1; }
  }
  throw new Error('괄호 짝 없음');
}
function exprEnd(s, i) {            // 식의 끝(깊이 0 의 ',' ';' 또는 줄 끝에서 다음 줄이 새 문장)
  for (; i < s.length; i++) {
    const c = s[i];
    if (c === '"' || c === "'" || c === '`') { i = skipStr(s, i) - 1; continue; }
    if (c === '/' && s[i + 1] === '/') { while (i < s.length && s[i] !== '\n') i++; i--; continue; }
    if (c === '/' && s[i + 1] === '*') { i = s.indexOf('*/', i + 2) + 1; continue; }
    if (c === '(' || c === '[' || c === '{') { i = skipBal(s, i) - 1; continue; }
    if (c === ',' || c === ';') return i;
  }
  return i;
}
// 최상위(줄 맨 앞) 선언만 찾는다. const A = ..., B = ...; 의 B 도 찾는다(쉼표 뒤 같은 줄·다음 줄 선언)
function findDecl(name) {
  const re = new RegExp('(^|\\n)(const|let|var) ' + name + ' =', 'g');
  let m = re.exec(S);
  if (m) { const st = m.index + m[1].length, vs = m.index + m[0].length; return { kind: 'var', start: st, valStart: vs, end: exprEnd(S, vs) }; }
  const re2 = new RegExp('(^|\\n)function ' + name + '\\(', 'g');
  m = re2.exec(S);
  if (m) { const st = m.index + m[1].length; const br = S.indexOf('{', S.indexOf(')', st)); return { kind: 'fn', start: st, end: skipBal(S, br) }; }
  // 같은 const 선언 안의 두 번째 이름:  const X = ..., NAME = ...;
  const re3 = new RegExp('[,\\s]' + name + ' = ', 'g');
  while ((m = re3.exec(S))) {
    // 앞 문장이 const 선언의 연속인지: 같은 줄 맨 앞 또는 이전 줄이 const 로 시작하는지 확인
    const ls = S.lastIndexOf('\n', m.index) + 1, line = S.slice(ls, m.index + 1);
    if (/^(const|let) /.test(line) || /^\s*\}?\s*,?\s*$/.test(line) === false && /^(const|let) /.test(S.slice(ls, ls + 6))) {
      const vs = m.index + m[0].length; return { kind: 'var', start: m.index + 1, valStart: vs, end: exprEnd(S, vs) };
    }
  }
  return null;
}

// ---------- 평가 ----------
const sandbox = { console, Math, JSON, Object, Array, Set, Map, String, Number, Date, RegExp, parseInt, parseFloat, isNaN, Infinity, NaN, undefined };
const ctx = vm.createContext(sandbox);
const defined = new Set();
function define(name) {
  if (defined.has(name)) return;
  const d = findDecl(name);
  if (!d) throw new Error('선언을 찾지 못함: ' + name);
  defined.add(name);
  const code = d.kind === 'fn' ? S.slice(d.start, d.end) : 'var ' + name + ' = ' + S.slice(d.valStart, d.end);
  runWithDeps(code, name);
}
function runWithDeps(code, label) {
  for (let n = 0; n < 60; n++) {
    try { return vm.runInContext(code, ctx); }
    catch (e) {
      const m = /^(\w+) is not defined/.exec(e.message);
      if (e && e.name === "ReferenceError" && m && !defined.has(m[1])) { define(m[1]); continue; }
      throw new Error(`평가 실패(${label}): ${e.message}`);
    }
  }
  throw new Error('의존 해결 반복 초과: ' + label);
}
const get = name => { define(name); return vm.runInContext(name, ctx); };
const J = v => JSON.parse(JSON.stringify(v));          // 함수 값은 지워진다

// ---------- 표 만들기 ----------
const out = {};            // 파일 → 객체
const manifest = { source: srcPath, md5, tables: {} };
const note = (name, n) => { manifest.tables[name] = n; };
const arrOf = (obj, key = 'id') => Object.entries(obj).map(([k, v]) => ({ [key]: k, ...J(v) }));

// 1) 선물·배·바다 표
const GIFTS = get('GIFTS'), SHIP_INFO = get('SHIP_INFO'), SEA = get('SEA'), AFFINITY = get('AFFINITY');
out['gifts.json'] = { gifts: arrOf(GIFTS), affinity: J(AFFINITY), affinityDefault: get('AFFINITY_DEFAULT') };
note('GIFTS', Object.keys(GIFTS).length); note('AFFINITY', Object.keys(AFFINITY).length);
const SHIP_MIX = get('SHIP_MIX');
out['ships.json'] = {
  ships: arrOf(SHIP_INFO), mix: SHIP_MIX.map(m => m ? Object.entries(m).map(([id, w]) => ({ id, w })) : null),
  big: [...get('BIG_SHIPS')], giftKind: J(get('GIFT_KIND'))
};
note('SHIP_INFO', Object.keys(SHIP_INFO).length); note('SHIP_MIX', SHIP_MIX.length - 1);
const natNeed = { '0000:sky': 'sky', '0000:sea': 'sea', '0000:all': 'all' };
const NL = get('NATURE_LETTERS');
for (const L of NL) if (!natNeed[L.key]) throw new Error('모르는 자연 편지 규칙: ' + L.key);
out['sea.json'] = {
  sea: arrOf(SEA), groups: get('SEA_GRP').map(([id, name]) => ({ id, name })), stampHalf: J(get('STAMP_HALF')),
  natureLetters: NL.map(L => ({ key: L.key, need: natNeed[L.key], nm: L.nm, text: L.text, sign: L.sign }))
};
note('SEA', Object.keys(SEA).length); note('NATURE_LETTERS', NL.length);

// 2) 진행(단계·갈래·레벨·승급·이정표·빛색)
const TRACKS = get('TRACKS'), MILES = get('MILESTONES').slice();
MILES.sort((x, y) => ['lens', 'garden', 'port'].indexOf(x.k) - ['lens', 'garden', 'port'].indexOf(y.k) || x.lv - y.lv);   // 원본 2146행과 같은 정렬(안정)
define('SAVE_DEFAULTS'); const SAVE_DEFAULTS = runWithDeps('SAVE_DEFAULTS()', 'SAVE_DEFAULTS()');
out['progression.json'] = {
  saveVer: get('SAVE_VER'), isleNs: get('ISLE_NS'), maxStage: get('MAXSTAGE'), tierOf: get('TIER_OF'), goal: get('GOAL'), dexNeed: get('DEX_NEED'), buildSec: get('BUILD_SEC'),
  stageName: get('STAGE_NAME'), stageRoles: get('STAGE_ROLES'),
  tracks: arrOf(TRACKS), lvBase: get('LV_BASE'), lvCap: J(get('LV_CAP')), ranks: get('RANKS').map(([min, name]) => ({ min, name })),
  milestones: J(MILES), beamColors: J(get('BEAM_COLORS')), saveDefaults: J(SAVE_DEFAULTS)
};
note('TRACKS', Object.keys(TRACKS).length); note('RANKS', out['progression.json'].ranks.length); note('MILESTONES', MILES.length); note('BEAM_COLORS', out['progression.json'].beamColors.length);

// 3) 밸런스 상수(원본 곳곳의 이름 있는 상수 + 함수 안에 박힌 숫자). 함수 안 숫자는 golden 함수 시험으로 일치를 확인한다
out['balance.json'] = {
  condOdds: J(get('COND_ODDS')), condName: J(get('COND_NAME')), condShort: J(get('COND_SHORT')), wxBoost: get('WX_BOOST'),
  letterMax: get('LETTER_MAX'), letterChance: get('LETTER_CHANCE'), letterLim: J(get('LETTER_LIM')), keepKinds: get('KEEP_KINDS'),
  offlineRate: get('OFFLINE_RATE'), offlineRegPerHour: get('OFFLINE_REG_PER_HOUR'), maxT: get('MAX_T'),
  visitAt: get('VISIT_AT'), giftAt: get('GIFT_AT'),
  // 아래는 함수 본문에 박혀 있던 값(행 번호는 동결 사본 기준)
  offlineMaxBaseHours: 8, offlineMaxPortHoursCap: 6,              // offlineMax 2671
  offlineMinSec: 90, offlineDiaryMinSec: 1800, offlineHomeMinSec: 10800, offlineFirstBackMinSec: 300, offlineQuietSec: 300, offlineRegLongSec: 25200, offlineRegMaxP: 0.9,   // runOffline 2688~2721, offlineRegulars 2675
  offlineShipEps: 1e-9, diaryMax: 14, diaryPerHour: 1.6,         // offlineShips 2672, makeDiary 2617
  shinyBase: 0.03, shinyPerLv: 0.003, shinyMax: 0.25, lensShinyPerLv: 0.002, lensShinyLvCap: 20,       // shinyChance 1959, lensShiny 1954
  harvestPerLv: 0.02, harvestMax: 0.5, regularBonusPerLv: 0.01, regularBonusMax: 0.3, regularBase: 0.3,    // harvestChance 1956, regularBonus 1957, pickRegular 4965
  lensMulPerLv: 0.04, lensMulBeyondPerLv: 0.01, lensMulMax: 2,                                           // lensMul 1953
  condOddsFogTier2: 0.25, giftSeasonWeight: 2.5,                                                         // sampleCond 2419, rollGift 2433
  giftPtsSeason: 3, giftPtsCond: 2, giftPtsFit: 2, giftPtsShiny: 6, autoOfferKeep: 3, autoOfferShinyKeep: 1,   // giftPts 2517, autoOfferAll 2552
  lvGrowthBase: 180, lvGrowthRatio: 1.38, lvGrowthFromLv: 5,                                             // lvAt 1945
  seasonGuestMinGuided: 5, annivDayMs: 86400000, firstBackRegular: 'swallow',                           // deliverGuests 4922, runOffline 2708
  itemValueNoAds: 2200, itemValueBeam: 825, itemValueSkin: 1100,          // itemValue 1678 (상점 차감 가격 계산)
  decorVisMin: 0.8, decorVisMinBack: 0.65, decorSlotCount: 28, decorOpenCount: 20, decorBlockedCount: 8   // DECOR_VIS_MIN 3493, 자리 수
};

// 4) 한국어 글(편지·일지·단골·손님·완결)
const ko = {};
for (const n of ['L_OPEN', 'L_DAY', 'L_NIGHT', 'L_MID', 'L_THANKS', 'L_GIFT', 'L_CLOSE', 'CAPTAINS', 'SHIPNAMES']) ko[n] = get(n);
ko.L_SEASON_LETTER = J(get('L_SEASON_LETTER')); ko.L_SEASON_DIARY = J(get('L_SEASON_DIARY')); ko.SEA_OPEN = J(get('SEA_OPEN'));
out['ko/letters.json'] = {
  open: ko.L_OPEN, day: ko.L_DAY, night: ko.L_NIGHT, mid: ko.L_MID, thanks: ko.L_THANKS, gift: ko.L_GIFT, close: ko.L_CLOSE,
  seasonLetter: ko.L_SEASON_LETTER, seasonDiary: ko.L_SEASON_DIARY, captains: ko.CAPTAINS, shipNames: ko.SHIPNAMES, seaOpen: ko.SEA_OPEN
};
const DIARY = get('DIARY');
out['ko/diary.json'] = { entries: J(DIARY) };
note('DIARY', DIARY.length);
const REG = get('REGULARS');
out['ko/regulars.json'] = {
  regulars: J(REG), seasonGuests: J(get('SEASON_GUESTS')), annivLetters: J(get('ANNIV_LETTERS')), finaleVoices: Object.entries(get('FINALE_VOICES')).map(([id, text]) => ({ id, text }))
};
note('REGULARS', REG.length); note('REGULAR_STORIES', REG.map(r => r.story.length).join('/')); note('SEASON_GUESTS', out['ko/regulars.json'].seasonGuests.length); note('ANNIV_LETTERS', out['ko/regulars.json'].annivLetters.length);
// 완결 편지: make(met) 가 만드는 글을 조각으로 나눠 두고, 몇 가지 만난 단골 조합에서 같은 글이 나오는지 검증한다
{
  const FL = get('FINALE_LETTER'), voices = out['ko/regulars.json'].finaleVoices.map(v => v.id);
  const sample = FL.make(['x']);                                   // 만난 단골이 없는 경우의 글에서 조각을 자른다
  const p1 = '등대지기님께. 이 편지는 한 사람이 쓴 글이 아니에요. 이 바다를 오가던 배들이 한 줄씩 보태어 한 통이 되었어요. ';
  const none = '불빛 덕에 저녁상이 따뜻했다는 배도, 별 보는 밤이 길어졌다는 배도 있어요.';
  const tail = sample.slice(sample.indexOf(none) + none.length);
  const some = ' 이름은 몰라도 저마다 한마디씩 보탠 배들이 더 있어요.';
  if (!sample.startsWith(p1 + none) || !tail.startsWith('<br><br>')) throw new Error('완결 편지 조각 분리 실패');
  out['ko/regulars.json'].finale = { nm: FL.nm, sign: FL.sign, head: p1, none, someSuffix: some, someUntil: 3, tail };
  const rebuild = met => { const own = voices.filter(id => met.includes(id)).map(id => out['ko/regulars.json'].finaleVoices.find(v => v.id === id).text).join(' '), n = voices.filter(id => met.includes(id)).length; return p1 + (own ? own + (n < 3 ? some : '') : none) + tail; };
  for (const met of [[], ['swallow'], ['moon'], ['swallow', 'moon'], ['swallow', 'moon', 'starroad'], ['starroad']]) if (rebuild(met) !== FL.make(met)) throw new Error('완결 편지 재구성 불일치: ' + met);
}

// 5) 외형·상품·광고·바다 구역
const SKINS = get('SKINS');
out['skins.json'] = {
  layers: Object.entries(SKINS).map(([layer, o]) => ({ id: layer, skins: arrOf(o) })), skinLayers: J(get('SKIN_LAYERS')),
  products: J(get('PRODUCTS')), adKinds: arrOf(get('AD_KINDS')), regions: arrOf(get('REGIONS')), itemNames: J(get('ITEM_NAMES')), priceStep: get('PRICE_STEP')
};
note('SKINS', Object.values(SKINS).reduce((x, o) => x + Object.keys(o).length, 0)); note('PRODUCTS', out['skins.json'].products.length); note('AD_KINDS', out['skins.json'].adKinds.length);

// 6) 꾸미기: 자리(함수 → 선형식), 장식, 글자표
const DS = get('DECOR_SLOTS'), slots = [];
const ISLE_NS = out['progression.json'].isleNs;
const lin = (fn, label) => {                                        // N=>[x,y] 를 k·N+c 로. 0..12 전부 같은지 검증
  const f0 = fn(0), f1 = fn(1), r = f0.map((c, i) => ({ k: f1[i] - c, c }));
  for (let N = 0; N <= 12; N++) { const v = fn(N); v.forEach((x, i) => { if (r[i].k * N + r[i].c !== x) throw new Error(`${label}: 선형식 불일치 N=${N}`); }); }
  return r;
};
const roundNeg = (fn, label) => {                                   // R=>[-Math.round(R*k), c] / [Math.round(R*k), c]
  const f1 = fn(1000), f0 = fn(0);
  const kx = Math.abs(f1[0]) / 1000, neg = f1[0] < 0, c = [f0[0], f0[1]];
  const comp = [{ k: kx, round: true, neg, c: 0 }, { k: 0, c: f0[1] }];
  for (let R = 0; R <= 400; R++) { const v = fn(R), x = (neg ? -1 : 1) * Math.round(R * kx); if (v[0] !== x || v[1] !== f0[1]) throw new Error(`${label}: 하늘 식 불일치 R=${R}`); }
  return comp;
};
for (const [id, v] of Object.entries(DS)) {
  const o = { id, chip: v.chip, size: v.size, open: J(v.open), blocked: v.blocked ? 1 : 0 };
  if (typeof v.ab === 'function') o.abLin = lin(v.ab, id); else if (v.ab) o.ab = v.ab;
  if (typeof v.xy === 'function') o.xyFn = roundNeg(v.xy, id);
  slots.push(o);
}
// 단계별(N=ISLE_NS) 값 목록도 같이 적어 둔다(검증·사람 확인용)
for (const o of slots) if (o.abLin) o.abByStage = ISLE_NS.map(N => o.abLin.map(c => c.k * N + c.c));
const DI = get('DECOR_ITEMS');
out['decor.json'] = {
  sizeRank: J(get('SIZE_RANK')), sizeWord: J(get('SIZE_WORD')), zones: get('DECOR_ZONES').map(([id, chip, title]) => ({ id, chip, title })),
  letters: J(get('DECOR_LETTERS')), season: J(get('DECOR_SEASON')), slots, items: arrOf(DI)
};
note('DECOR_ZONES', out['decor.json'].zones.length); note('DECOR_SLOTS', slots.length); note('DECOR_SLOTS_BLOCKED', slots.filter(s => s.blocked).length); note('DECOR_SLOTS_OPEN', slots.filter(s => !s.blocked).length); note('DECOR_ITEMS', Object.keys(DI).length);
if (slots.length !== 28 || slots.filter(s => s.blocked).length !== 8) throw new Error('꾸미기 자리 개수(28/막힘 8) 불일치');

// 7) 그림 도면·색표(렌더용; M2 가 쓴다). 평가가 되는 것만 담는다
const art = {}, artMiss = [];
for (const n of ['ICONS', 'ICON_UNKNOWN', 'KEEPER', 'BARREL', 'BARREL_PAL', 'TIPS', 'DECO_MSG', 'DECO_DIGITS', 'SEASON_GRASS', 'TREE_COL', 'SKY_N', 'SKY_D', 'SEA_N', 'SEA_D', 'TOWER_PAL', 'DECOR_PAL', 'DECO_FLOWER', 'REEFS', 'ROLES', 'SEASON_NAME', 'CLOUDS']) {
  try { art[n] = J(get(n)); } catch (e) { artMiss.push(`${n}: ${e.message}`); }
}
out['art.json'] = art;
if (art.ICONS) note('ICONS', Object.keys(art.ICONS).length);
manifest.artMissing = artMiss;

// 8) 금지어 정규식(lint/forbid_ko.json): FORBID_RE 의 12갈래를 배열 그대로 뽑는다 (결합 전 문자열)
{
  const at = S.indexOf('const FORBID_RE = new RegExp(['), arrStart = S.indexOf('[', at), arrEnd = skipBal(S, arrStart);
  const branches = vm.runInContext(S.slice(arrStart, arrEnd), ctx);
  const names = ['압박', '기한', '손실', '죄책감', '출석', '순위', '어린이', 'E4 손실·기한', 'E4 출석·비교·뒤처짐', 'E4 압박 호소·상업·과제', 'E4 감정 호소', 'E5 추궁·타이머·결제·탓'];
  if (branches.length !== 12) throw new Error('FORBID_RE 갈래가 12개가 아님: ' + branches.length);
  const src = (n) => get(n).source;
  const joined = new RegExp(branches.join('|')).source;
  if (joined !== get('FORBID_RE').source) throw new Error('FORBID_RE 재구성 불일치');
  out['lint/forbid_ko.json'] = { branches: branches.map((p, i) => ({ name: names[i], pattern: p })), dayBan: src('DAY_BAN_RE'), diaryBan: src('DIARY_BAN_RE'), josaLeft: src('JOSA_LEFT_RE'), natureTextBan: '일째|연속|순위|상위' };
  note('FORBID_BRANCHES', branches.length);
}

// ---------- 교차 정합(원본 checkContent 와 같은 종류) ----------
const errs = [];
for (const k of Object.keys(GIFTS)) { if (!AFFINITY[k]) errs.push('AFFINITY 없음 ' + k); if (art.ICONS && !art.ICONS[k]) errs.push('ICONS 없음 ' + k); }
for (const k of Object.keys(AFFINITY)) if (!GIFTS[k]) errs.push('AFFINITY 에 없는 선물 ' + k);
if (art.ICONS) for (const [k, v] of Object.entries(SEA)) if (!art.ICONS[v.icon]) errs.push('SEA 아이콘 없음 ' + k);
if (errs.length) throw new Error('교차 정합 실패: ' + errs.join(' | '));

// ---------- 기대 개수(부록 A / 설계서 M1 완료 기준) ----------
const expect = { GIFTS: 18, SHIP_INFO: 9, SEA: 24, REGULARS: 8, DECOR_SLOTS: 28, DECOR_SLOTS_BLOCKED: 8, DECOR_SLOTS_OPEN: 20, DECOR_ITEMS: 10, SKINS: 33, PRODUCTS: 7, AD_KINDS: 3, TRACKS: 3, RANKS: 10, DECOR_ZONES: 5, SEASON_GUESTS: 4, ANNIV_LETTERS: 4 };
const bad = Object.entries(expect).filter(([k, v]) => manifest.tables[k] !== v).map(([k, v]) => `${k}: 기대 ${v}, 실제 ${manifest.tables[k]}`);
manifest.expect = expect; manifest.mismatch = bad;

// ---------- 쓰기 ----------
fs.mkdirSync(outDir, { recursive: true });
manifest.files = {};
for (const [f, obj] of Object.entries(out)) {
  const p = path.join(outDir, f); fs.mkdirSync(path.dirname(p), { recursive: true });
  const text = JSON.stringify(obj, null, 1) + '\n'; fs.writeFileSync(p, text);
  manifest.files[f] = { bytes: Buffer.byteLength(text), md5: crypto.createHash('md5').update(text).digest('hex') };
}
const gdir = path.join(here, 'golden', 'm1'); fs.mkdirSync(gdir, { recursive: true });
fs.writeFileSync(path.join(gdir, 'data_manifest.json'), JSON.stringify(manifest, null, 1) + '\n');
console.log('입력 MD5', md5);
console.log(Object.entries(manifest.tables).map(([k, v]) => `${k}=${v}`).join('  '));
if (artMiss.length) console.log('그림 표 중 평가 못 한 것:\n  ' + artMiss.join('\n  '));
if (bad.length) { console.error('개수 불일치:\n  ' + bad.join('\n  ')); process.exit(1); }
console.log('개수 대조 통과, 파일', Object.keys(out).length, '개 →', outDir);
