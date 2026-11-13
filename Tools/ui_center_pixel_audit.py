import sys, re, glob, os, collections
from PIL import Image
d = sys.argv[1]
rows = [l.split('\t') for l in open(os.path.join(d, 'center_log.tsv'), encoding='utf8').read().split('\n') if l]
imgs = {}
for f in glob.glob(os.path.join(d, 'm3_*.png')):
    m = re.match(r'm3_(.+)_([a-z]+)\.png', os.path.basename(f))
    im = Image.open(f).convert('RGB'); imgs[(im.size, m.group(2))] = im
out = []
for r in rows:
    tag = r[0].split(); w, h = map(int, tag[0].split('x')); st = tag[1]
    im = imgs.get(((w, h), st))
    if im is None: continue
    kv = {x.split('=')[0]: x.split('=', 1)[1] for x in r[2:]}
    if not kv.get('px'): continue
    x0, y0, x1, y1 = map(float, kv['px'].split(','))
    dot = float(kv['dot']); bw = float(kv['btn'].split('x')[0]); pxs = (x1 - x0) / bw      # 화면 픽셀 / 캔버스 단위
    pd = dot * pxs                                                                           # 1 도트의 화면 픽셀
    ix0, iy0, ix1, iy1 = int(x0 + 3 * pd), int(y0 + 3 * pd), int(x1 - 3 * pd), int(y1 - 5 * pd)
    if ix1 - ix0 < 6 or iy1 - iy0 < 6: continue
    reg = im.crop((ix0, iy0, ix1, iy1)); px = reg.load(); W, H = reg.size
    inseg = kv.get("chk") == "1"
    cnt = collections.Counter(px[x, y] for y in range(H) for x in range(W)); face = cnt.most_common(1)[0][0]
    minx = miny = 10 ** 9; maxx = maxy = -1
    for y in range(H):
        for x in range(W):
            p = px[x, y]
            if inseg and x > W - 11 * pd and y < 11 * pd: continue      # 오른쪽 위 모서리는 체크 표시(badge:check)가 얹히는 자리라 잉크 상자에서 뺀다
            if abs(p[0] - face[0]) + abs(p[1] - face[1]) + abs(p[2] - face[2]) > 90:
                minx = min(minx, x); maxx = max(maxx, x); miny = min(miny, y); maxy = max(maxy, y)
    if maxx < 0: continue
    # 잉크 상자 중심 ↔ 면(바깥 틀의 위 2칸 ~ 아래 그림자 3칸 위) 중심
    fx0, fy0, fx1, fy1 = x0 + 2 * pd, y0 + 2 * pd, x1 - 2 * pd, y1 - 3 * pd
    cx = ix0 + (minx + maxx + 1) / 2 - (fx0 + fx1) / 2
    cy = iy0 + (miny + maxy + 1) / 2 - (fy0 + fy1) / 2
    touch = (minx <= 0 or miny <= 0 or maxx >= W - 1 or maxy >= H - 1)
    out.append((cx / pd, cy / pd, r[1], r[0], touch, (maxx - minx + 1) / pd, (maxy - miny + 1) / pd))
# 판정: 잉크 중심이 면 중심에서 LIMIT(기본 1칸)을 넘으면 위반, 종료 코드 1. (touch 는 크롭 가장자리 접촉이라 틀 장식 때문에 흔해서 판정에서 뺐다)
# 알려진 예외(ad 상태=광고 오버레이가 단추 위를 덮음)는 IGNORE_STATES, 배지 숫자가 잉크로 잡히는 단추는 IGNORE_BTN 에서 뺀다.
IGNORE_STATES = {'ad'}
IGNORE_BTN = re.compile(r'(^|/)(tab:mail|tab:dex|tab:growth|tab:deco|tab:shop|menu button)$')
LIMIT = float(os.environ.get('CENTER_LIMIT_DOTS', '1.0'))
def ignored(o): return o[3].split()[1] in IGNORE_STATES or IGNORE_BTN.search(o[2]) is not None
bad = [o for o in out if not ignored(o) and (abs(o[0]) > LIMIT or abs(o[1]) > LIMIT)]
print('buttons', len(out), 'off', len(bad), '(ignored %d)' % sum(1 for o in out if ignored(o)))
for o in bad: print(f'  OFF {o[3]} {o[2]} dx={o[0]:+.1f} dy={o[1]:+.1f} touch={o[4]}')
agg = collections.defaultdict(list)
for o in out:
    k = re.sub(r'\d+', '#', re.sub(r'(btn:[a-zA-Z]+):.*', r'\1:*', o[2].split('/')[-1]))
    agg[k].append(o)
for k, v in sorted(agg.items()):
    print(f'{k:20s} n={len(v):3d} dx[{min(a[0] for a in v):+.1f},{max(a[0] for a in v):+.1f}] dy[{min(a[1] for a in v):+.1f},{max(a[1] for a in v):+.1f}] touch={sum(1 for a in v if a[4])}')
sys.exit(1 if bad or not out else 0)
