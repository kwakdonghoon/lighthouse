// A2 타이틀 인트로 골든: 동결 HTML 의 타이틀 캔버스(#titleCv)를 인트로가 켜진 상태(?intro&at=0)에서 정해진 시각 t 로 그린 프레임.
// 실행: 페이지(index.html?dev&title&intro&at=0)에서  (0,eval)(await (await fetch('/gx.js')).text()); await (0,eval)(await (await fetch('/ext_title_intro.js')).text())
// 서버: Tools/golden/serve_golden_m4.py <port> (127.0.0.1 전용). 결과: Tools/golden/m4/out/titleI_<tag>.bin.gz, titleI_<tag>.meta
(async () => {
  const { Pack, postText, readCanvas } = GX;
  await (document.fonts && document.fonts.ready); await new Promise(r => setTimeout(r, 600));
  const cvT = document.getElementById('titleCv'), lw = cvT.width, lh = cvT.height, dpr = window.devicePixelRatio || 1, k = parseFloat(cvT.style.width) / lw;
  const tag = innerWidth + 'x' + innerHeight + 'd' + dpr;
  const meta = ['view;' + innerWidth + ';' + innerHeight + ';' + dpr + ';' + lw + ';' + lh + ';' + k + ';' + Math.round(k * dpr)];
  // 시각: 바닥 내려앉기 시작(1.2)·블록이 한창(2.4, 3.3)·섬 완성 직전(4.5)·탑이 올라오는 중(5.0)·탑 B·램프 번쩍(6.0, 6.5)·관리인(7.2)·배가 들어옴(8.0)·글자 내려앉음(9.3)·끝(12.0)
  const ts = [0.2, 1.2, 2.4, 3.3, 4.5, 5.0, 6.0, 6.5, 7.2, 8.0, 9.3, 12.0];
  const pk = new Pack();
  for (const t of ts) { window.__titleDraw(t); pk.add('t' + t, 0, 0, cvT, readCanvas(cvT)); meta.push('frame;t' + t + ';' + t); }
  const r = await pk.flush('titleI_' + tag + '.bin.gz'); await postText('titleI_' + tag + '.meta', meta.join('\n'));
  return JSON.stringify({ tag, r, meta });
})()
