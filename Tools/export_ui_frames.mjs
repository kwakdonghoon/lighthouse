// M3: 동결 HTML 의 CSS 변수(--i-이름 = base64 PNG, --bi-이름 = 9-slice 값)에서 도트 UI 틀 PNG 와 슬라이스 표를 뽑는다.
// HTML 은 읽기만 한다. 한 번 뽑은 뒤 Unity 쪽 UI 에셋이 정본(Assets/_Lighthouse/Art/UI). 다시 돌리면 덮어쓰므로 보관만.
// 사용: node Tools/export_ui_frames.mjs
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const root = path.resolve(import.meta.dirname, '..');
const html = path.resolve(root, '..', 'ek_Project_a9', 'lighthouse_idle.frozen-unity-m0.html');
const outDir = path.join(root, 'Assets', '_Lighthouse', 'Resources', 'UI', 'Frames');
fs.mkdirSync(outDir, { recursive: true });
const src = fs.readFileSync(html, 'utf8');
const md5 = crypto.createHash('md5').update(src).digest('hex');
if (md5 !== 'e36a97797597bf77dceae187eacf51a1') { console.error('동결 사본 MD5 불일치: ' + md5); process.exit(1); }

const imgs = {};
for (const m of src.matchAll(/--i-([A-Za-z0-9_]+):\s*url\("data:image\/png;base64,([A-Za-z0-9+/=]+)"\)/g)) imgs[m[1]] = m[2];
const slices = {};
for (const m of src.matchAll(/--bi-([A-Za-z0-9_]+):\s*var\(--i-[A-Za-z0-9_]+\)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+fill/g)) slices[m[1]] = [+m[2], +m[3], +m[4], +m[5]];   // CSS 순서: 위 오른쪽 아래 왼쪽
const manifest = {};
for (const [name, b64] of Object.entries(imgs)) {
  const buf = Buffer.from(b64, 'base64');
  const w = buf.readUInt32BE(16), h = buf.readUInt32BE(20);
  fs.writeFileSync(path.join(outDir, name + '.png'), buf);
  manifest[name] = { w, h, slice: slices[name] || null };      // slice = [위, 오른쪽, 아래, 왼쪽] (도트)
}
fs.writeFileSync(path.join(outDir, 'frames.json'), JSON.stringify(manifest, null, 1));
console.log('frames', Object.keys(manifest).length, 'with slice', Object.values(manifest).filter(v => v.slice).length);
