// M4 타이틀 골든: 동결 HTML 의 타이틀 캔버스(#titleCv)를 정해진 시각(?nointro 상태, 인트로 없음)에 그린 프레임과 글자 마스크.
// 실행: 페이지(index.html?dev&title&nointro&at=999)에서  (0,eval)(await (await fetch('/gx.js')).text()); await (0,eval)(await (await fetch('/ext_title.js')).text())
// 서버: Tools/golden/serve_golden_m4.py <port> (127.0.0.1 전용). 결과: Tools/golden/m4/out/title_<tag>.bin.gz, title_<tag>.meta, (첫 실행) title_glyphs.txt
(async () => {
  const { Pack, postText, readCanvas } = GX;
  await (document.fonts && document.fonts.ready); await new Promise(r => setTimeout(r, 600));
  const cvT = document.getElementById('titleCv'), lw = cvT.width, lh = cvT.height, dpr = window.devicePixelRatio || 1, k = parseFloat(cvT.style.width) / lw;
  const tag = innerWidth + 'x' + innerHeight + 'd' + dpr;
  const meta = ['view;' + innerWidth + ';' + innerHeight + ';' + dpr + ';' + lw + ';' + lh + ';' + k + ';' + Math.round(k * dpr)];
  // 글자 마스크 (buildLetters 와 같은 방식: S px 굵은 글꼴, 알파 > 90). 글자 크기 S = 16..28
  if (!window.__glyphsDone) {
    const lines = [], chars = [...'등대지기'];
    for (let S = 16; S <= 28; S++) {
      const m = document.createElement('canvas'); m.width = S + 2; m.height = S + 2; const mg = m.getContext('2d', { willReadFrequently: true });
      for (const ch of chars) {
        mg.clearRect(0, 0, m.width, m.height); mg.fillStyle = '#000'; mg.textAlign = 'center'; mg.textBaseline = 'middle'; mg.font = '700 ' + S + 'px "Noto Sans KR", "Malgun Gothic", "Apple SD Gothic Neo", sans-serif'; mg.fillText(ch, (S + 2) / 2, (S + 2) / 2 + 1);
        const im = mg.getImageData(0, 0, S + 2, S + 2).data; let bits = '';
        for (let y = 0; y < S + 2; y++) for (let x = 0; x < S + 2; x++) bits += im[(y * (S + 2) + x) * 4 + 3] > 90 ? '1' : '0';
        lines.push(S + ';' + ch + ';' + bits);
      }
    }
    await postText('title_glyphs.txt', lines.join('\n')); window.__glyphsDone = true;
  }
  // 시각 목록: 로고가 떨어지는 중(0.4), 완성(1.5), 중간(5.0, 12.7) + 빛이 배를 비춘 뒤 선물이 뜨는 때(c0+1.6, c0+0.25+0.2)
  const W_ROT = 0.7, TH0 = -0.6, PER = Math.PI / W_ROT, cx = Math.round(lw / 2), cy = Math.round(lh * 0.6);
  const shx = cx + Math.min(lw * 0.31, 56), shy = Math.round(cy + 22), thShip = Math.atan2((shy - cy) * 2, shx - cx), c0 = (((thShip - TH0) / W_ROT) % PER + PER) % PER;
  const ts = [0.4, 1.5, 5.0, 12.7, +(c0 + 1.6).toFixed(3), +(c0 + 0.5).toFixed(3), 20.3];
  const pk = new Pack();
  for (const t of ts) { window.__titleDraw(t); pk.add('t' + t, 0, 0, cvT, readCanvas(cvT)); meta.push('frame;t' + t + ';' + t); }
  const r = await pk.flush('title_' + tag + '.bin.gz'); await postText('title_' + tag + '.meta', meta.join('\n'));
  return JSON.stringify({ tag, r, meta });
})()
