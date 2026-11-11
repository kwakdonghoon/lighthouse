// M1 골든 추출용 임시 서버 (127.0.0.1 전용, 끝나면 반드시 종료).
// 실행: node Tools/golden/serve_m1.mjs <포트> [동결HTML경로]
// 주소: http://127.0.0.1:<포트>/lighthouse_idle.html?dev&clean&s=1&n=0&season=summer   (골든 스크립트는 /m1_golden.js)
// 수집: POST /put/<파일이름> 본문을 Tools/golden/m1/<파일이름> 으로 저장한다 (이름은 영숫자·_·. 만 허용)
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const port = +process.argv[2];
if (!(port > 1024 && port < 65536)) { console.error('사용법: node serve_m1.mjs <포트>'); process.exit(2); }
const html = process.argv[3] || 'D:/Unity/EK_game_create/ek_Project_a9/lighthouse_idle.frozen-unity-m0.html';
const outDir = path.join(here, 'm1'); fs.mkdirSync(outDir, { recursive: true });
http.createServer((req, res) => {
  const u = new URL(req.url, 'http://127.0.0.1');
  if (req.method === 'GET' && u.pathname === '/lighthouse_idle.html') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); return res.end(fs.readFileSync(html)); }
  if (req.method === 'GET' && u.pathname === '/m1_golden.js') { res.writeHead(200, { 'content-type': 'text/javascript; charset=utf-8', 'cache-control': 'no-store' }); return res.end(fs.readFileSync(path.join(here, 'm1_golden.js'))); }
  if (req.method === 'POST' && u.pathname.startsWith('/put/')) {
    const name = u.pathname.slice(5);
    if (!/^[A-Za-z0-9_.]+$/.test(name)) { res.writeHead(400); return res.end('bad name'); }
    const chunks = []; req.on('data', c => chunks.push(c)); req.on('end', () => { fs.writeFileSync(path.join(outDir, name), Buffer.concat(chunks)); res.writeHead(200); res.end('ok'); });
    return;
  }
  res.writeHead(404); res.end('no');
}).listen(port, '127.0.0.1', () => console.log('listening 127.0.0.1:' + port));
