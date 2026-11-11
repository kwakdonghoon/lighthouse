// M2 골든 추출 공통 도구. 페이지 안에서 (0, eval)(await (await fetch('/gx.js')).text()) 로 한 번 불러 window.GX 를 만든다.
// 핵심: putImageData 를 가로채 캔버스에 올라가기 직전의 굽기 버퍼(straight RGBA)를 저장한다 (캔버스를 다시 읽으면 반투명 픽셀이 premultiplied 로 깎인다, 부록 B-15).
// 밤 사본(nightOf)·장식 그림자처럼 canvas 합성으로 만든 것은 getImageData(브라우저 결과 그대로)를 정본으로 쓴다.
(() => {
  if (window.GX) return;
  const rec = new Map();
  const orig = CanvasRenderingContext2D.prototype.putImageData;
  CanvasRenderingContext2D.prototype.putImageData = function (im, ...a) { rec.set(this.canvas, { w: im.width, h: im.height, data: new Uint8Array(im.data) }); return orig.call(this, im, ...a); };
  const enc = new TextEncoder();
  const post = async (name, u8) => { const r = await fetch('/put/' + name, { method: 'POST', body: u8 }); if (!r.ok) throw new Error('post fail ' + name); };
  const gz = async u8 => new Uint8Array(await new Response(new Blob([u8]).stream().pipeThrough(new CompressionStream('gzip'))).arrayBuffer());
  const u32 = n => { const b = new Uint8Array(4); new DataView(b.buffer).setInt32(0, n, true); return b; };
  const readCanvas = c => new Uint8Array(c.getContext('2d').getImageData(0, 0, c.width, c.height).data);
  // 굽기 버퍼가 있으면 그것, 없으면 캔버스 읽기. 스프라이트 {c, ax, ay, w, h}
  const rawOf = spr => { const r = rec.get(spr.c); return r ? r.data : readCanvas(spr.c); };
  class Pack {
    constructor() { this.parts = []; this.n = 0; this.names = []; }
    add(name, ax, ay, c, data) {
      const w = c.width, h = c.height, nm = enc.encode(name);
      this.parts.push(u32(nm.length), nm, u32(ax), u32(ay), u32(w), u32(h), data); this.n++; this.names.push(name);
    }
    sprite(name, spr, canvasOnly) { const data = canvasOnly ? readCanvas(spr.c) : rawOf(spr); this.add(name, spr.ax, spr.ay, spr.c, data); }
    async flush(file) {
      const parts = [u32(this.n), ...this.parts]; let len = 0; for (const p of parts) len += p.length;
      const out = new Uint8Array(len); let o = 0; for (const p of parts) { out.set(p, o); o += p.length; }
      const z = await gz(out); await post(file, z); return { file, n: this.n, bytes: z.length };
    }
  }
  const postJson = (name, obj) => post(name, enc.encode(JSON.stringify(obj)));
  const postText = (name, text) => post(name, enc.encode(text));
  window.GX = { rec, post, postJson, postText, Pack, rawOf, readCanvas, gz };
})();
