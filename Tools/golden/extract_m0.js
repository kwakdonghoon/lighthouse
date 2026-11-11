// M0 골든 추출 스크립트 (브라우저 도구의 javascript_exec 로 페이지 안에서 실행한 그대로).
// 절차:
//  1) lighthouse_idle.frozen-unity-m0.html 을 index.html 로 복사한 srv/ 폴더와 out/ 폴더를 만든다.
//  2) serve_golden.py 를 127.0.0.1 에만 바인딩해 실행한다 (0.0.0.0 금지; 포트는 비어 있는 것으로 — 다른 프로세스와 겹치지 않게 확인).
//  3) 브라우저에서 http://127.0.0.1:<포트>/index.html?dev&clean&s=1&n=0&season=summer 를 열고 아래 코드를 실행한다.
//  4) out/ 의 *.rgba, hsh_grid.u32, manifest.json 이 골든이다. 끝나면 서버를 종료한다.
// 핵심: putImageData 를 가로채 캔버스에 올라가기 직전의 굽기 버퍼를 그대로 저장한다 (캔버스를 다시 읽으면 알파<255 픽셀이 premultiplied 로 깎인다).
(async () => {
const rec = new Map();
const orig = CanvasRenderingContext2D.prototype.putImageData;
CanvasRenderingContext2D.prototype.putImageData = function (im, ...a) { rec.set(this.canvas, { w: im.width, h: im.height, data: new Uint8Array(im.data) }); return orig.call(this, im, ...a); };
const post = async (name, u8) => { const r = await fetch('/put/' + name, { method: 'POST', body: u8 }); if (!r.ok) throw new Error('post fail ' + name); };
const man = [];
const dump = async (name, spr) => { const r = rec.get(spr.c); if (!r) throw new Error('no rec ' + name); await post(name + '.rgba', r.data); man.push({ name, ax: spr.ax, ay: spr.ay, w: spr.w, h: spr.h, rw: r.w, rh: r.h }); };
const keepStage = STAGE, keepT = T1V;
const timing = {};
for (let s = 1; s <= 6; s++) {
  STAGE = s;
  const isl = bakeIsland();
  await dump('island_s' + s, isl);
  const pcs = ISLE_PIECES;
  for (let i = 0; i < pcs.length; i++) await dump('island_s' + s + '_piece' + i, pcs[i].spr);
  man.push({ name: 'island_s' + s + '_meta', pieces: pcs.map(p => p.v), ISLE_N, RB, nLights: lights.length, lights });
}
const towerVariants = [[1, 0], [1, 1], [1, 2], [2, 2], [3, 2], [4, 2], [5, 2], [6, 2]];
for (const [s, v] of towerVariants) {
  STAGE = s; if (s === 1) { T1V = v; TOWER[1] = TOWER1[v]; }
  const P = skinTower(TOWER[s]);
  const a = bakeTowerA(P), b = bakeTowerB(P);
  const tag = 'tower_s' + s + (s === 1 ? '_v' + v : '');
  await dump(tag + '_A', a); await dump(tag + '_B', b);
  man.push({ name: tag + '_P', P });
}
STAGE = keepStage; T1V = keepT; TOWER[1] = TOWER1[keepT];
const gx0 = -128, gn = 384, hb = new Uint32Array(gn * gn);
for (let y = 0; y < gn; y++) for (let x = 0; x < gn; x++) hb[y * gn + x] = Math.round(hsh(gx0 + x, gx0 + y) * 4294967296);
await post('hsh_grid.u32', new Uint8Array(hb.buffer));
man.push({ name: 'hsh_grid', x0: gx0, n: gn });
const T = (f, n) => { f(); const a = []; for (let i = 0; i < n; i++) { const t = performance.now(); f(); a.push(performance.now() - t); } a.sort((x, y) => x - y); return { med: +a[a.length >> 1].toFixed(2), min: +a[0].toFixed(2), max: +a[a.length - 1].toFixed(2) }; };
for (let s = 1; s <= 6; s++) { STAGE = s; timing['island_s' + s] = T(() => bakeIsland(), 11); }
for (let s = 2; s <= 6; s++) { STAGE = s; const P = skinTower(TOWER[s]); timing['towerAB_s' + s] = T(() => { bakeTowerA(P); bakeTowerB(P); }, 11); }
STAGE = keepStage; TOWER[1] = TOWER1[keepT];
await post('manifest.json', new TextEncoder().encode(JSON.stringify({ url: location.href, dpr: devicePixelRatio, season: curSeason(), ua: navigator.userAgent, man, timing })));
return JSON.stringify({ n: man.length, timing });
})()
