# -*- coding: utf-8 -*-
"""pixelkit - 등대지기 도트 아이콘 작업 도구 (Python 3 + Pillow).

아이콘 정의는 한 줄: "id;d=#2c3a4a,w=#c8d2e4,...;행|행|..."  (RenderData.IconLines 와 UiTheme.UnityIcons 의 형식, 8×8 또는 16×16)

  python Tools/pixelkit/pixelkit.py list                      # 아이콘 목록(출처: 표 / Unity 교체)
  python Tools/pixelkit/pixelkit.py lint [id ...] [--sym id]  # 모양·팔레트 검사(문제 있으면 종료 코드 1)
  python Tools/pixelkit/pixelkit.py preview id [id ...] -o out.png [--zoom 16] [--bg #e8b848]   # 확대 미리보기
  python Tools/pixelkit/pixelkit.py from-png in.png --id gear --like gear [-o line.txt]          # PNG -> 정의 한 줄
  python Tools/pixelkit/pixelkit.py mirror rows.txt --id gear --like gear [--quad]               # 반쪽 윤곽 -> 대칭·윤곽·명암 자동
  python Tools/pixelkit/pixelkit.py snap in.png out.png [색수]                                    # spritefusion-pixel-snapper 가 PATH 에 있으면 호출

외부 도구와 잇는 법(문서: Tools/pixelkit/README.md):
  AI 가 뽑은 그림 -> snap(격자·팔레트 맞춤) -> from-png -> UiTheme.UnityIcons 에 한 줄 붙이기 -> lint -> preview 로 확인
  claude-pxl(.px.js) 로 그린 그림 -> node bin/pxl.js build -> PNG -> from-png
"""
import argparse, os, re, shutil, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
RDATA = os.path.join(ROOT, 'Assets/_Lighthouse/Scripts/Render/RenderData.Generated.cs')
THEME = os.path.join(ROOT, 'Assets/_Lighthouse/Scripts/UI/UiTheme.cs')
LINE = re.compile(r'"([A-Za-z0-9_]+);((?:[A-Za-z]=#[0-9a-fA-F]{6},?)+);([A-Za-z.|]+)"')


def load():
    """{id: (palette dict, [rows], source)}. Unity 교체 그림(UiTheme.UnityIcons)이 표보다 우선."""
    out = {}
    for path, src in ((RDATA, '표'), (THEME, 'Unity')):
        if not os.path.exists(path):
            continue
        for m in LINE.finditer(open(path, encoding='utf8').read()):
            pal = {kv[0]: kv[2:] for kv in m.group(2).split(',') if kv}
            out[m.group(1)] = (pal, m.group(3).split('|'), src)
    return out


def hex2rgb(h):
    h = h.lstrip('#'); return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def fmt(id_, pal, rows):
    return '%s;%s;%s' % (id_, ','.join('%s=%s' % (k, v) for k, v in pal.items()), '|'.join(rows))


def lint_one(id_, pal, rows, sym=False):
    errs, notes = [], []
    n = len(rows)
    if n not in (8, 16):
        errs.append('줄 수 %d (8 또는 16 이어야 함)' % n)
    for y, r in enumerate(rows):
        if len(r) != n:
            errs.append('%d번째 줄 길이 %d ≠ %d' % (y, len(r), n))
    used = set(''.join(rows)) - {'.'}
    for c in sorted(used - set(pal)):
        errs.append("팔레트에 없는 글자 '%s'" % c)
    for c in sorted(set(pal) - used):
        notes.append("안 쓴 팔레트 글자 '%s'" % c)
    if not used:
        errs.append('빈 그림')
    # 외톨이 점(상하좌우에 같은 색·다른 색 이웃이 전혀 없는 점)
    if all(len(r) == n for r in rows):
        for y in range(n):
            for x in range(n):
                if rows[y][x] == '.':
                    continue
                if not any(0 <= x + a < n and 0 <= y + b < n and rows[y + b][x + a] != '.' for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    notes.append('외톨이 점 (%d,%d)' % (x, y))
        # 투명 칸 모양의 좌우·상하 대칭 정도(참고용; --sym 이면 어긋남을 오류로)
        mask = [[ch != '.' for ch in r] for r in rows]
        lr = sum(mask[y][x] != mask[y][n - 1 - x] for y in range(n) for x in range(n)) // 2
        ud = sum(mask[y][x] != mask[n - 1 - y][x] for y in range(n) for x in range(n)) // 2
        notes.append('윤곽 비대칭 좌우 %d점·상하 %d점' % (lr, ud))
        if sym and (lr or ud):
            errs.append('윤곽이 대칭이 아님: 좌우 %d점·상하 %d점' % (lr, ud))
    return errs, notes


def render(pal, rows, zoom, bg):
    from PIL import Image
    n = len(rows); im = Image.new('RGB', (n, n), hex2rgb(bg))
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch in pal:
                im.putpixel((x, y), hex2rgb(pal[ch]))
    return im.resize((n * zoom, n * zoom), Image.NEAREST)


def cmd_list(a):
    for k, (pal, rows, src) in load().items():
        print('%-14s %2dx%-2d %s' % (k, len(rows), len(rows[0]) if rows else 0, src))


def cmd_lint(a):
    icons = load(); ids = a.ids or list(icons); bad = 0
    for k in ids:
        if k not in icons:
            print('없는 id:', k); bad += 1; continue
        pal, rows, src = icons[k]
        e, nt = lint_one(k, pal, rows, a.sym == k)
        for m in e:
            print('오류 %s: %s' % (k, m)); bad += 1
        if a.verbose:
            for m in nt:
                print('참고 %s: %s' % (k, m))
    print('검사 %d개, 오류 %d건' % (len(ids), bad))
    return 1 if bad else 0


def cmd_preview(a):
    from PIL import Image
    icons = load(); ims = []
    for k in a.ids:
        if k not in icons:
            print('없는 id:', k); return 1
        ims.append(render(icons[k][0], icons[k][1], a.zoom, a.bg))
    w = sum(i.width for i in ims) + 8 * (len(ims) + 1); h = max(i.height for i in ims) + 16
    sheet = Image.new('RGB', (w, h), (40, 44, 56)); x = 8
    for i in ims:
        sheet.paste(i, (x, 8)); x += i.width + 8
    sheet.save(a.o); print('저장', a.o)


def nearest(pal_rgb, px):
    return min(pal_rgb, key=lambda c: sum((u - v) ** 2 for u, v in zip(pal_rgb[c], px)))


def cmd_from_png(a):
    from PIL import Image
    icons = load()
    if a.like not in icons:
        print('--like 로 쓸 기존 아이콘 id 필요(팔레트를 빌려 옴):', a.like); return 1
    pal = icons[a.like][0]; prgb = {k: hex2rgb(v) for k, v in pal.items()}
    im = Image.open(a.png).convert('RGBA'); n = im.width
    if im.width != im.height or n not in (8, 16):
        print('8×8 또는 16×16 정사각형 PNG 만 변환합니다(받은 크기 %dx%d). 큰 그림은 먼저 snap 으로 격자에 맞추고 1:1 로 줄이세요.' % im.size); return 1
    rows, far = [], 0
    for y in range(n):
        r = ''
        for x in range(n):
            R, G, B, A = im.getpixel((x, y))
            if A < 128:
                r += '.'; continue
            k = nearest(prgb, (R, G, B))
            if sum((u - v) ** 2 for u, v in zip(prgb[k], (R, G, B))) > 40 ** 2 * 3:
                far += 1
            r += k
        rows.append(r)
    line = fmt(a.id, pal, rows)
    if far:
        print('주의: 팔레트와 먼 색 %d점을 가장 가까운 글자로 바꿨습니다(눈으로 확인).' % far, file=sys.stderr)
    e, _ = lint_one(a.id, pal, rows)
    for m in e:
        print('오류:', m, file=sys.stderr)
    if a.o:
        open(a.o, 'w', encoding='utf8').write(line + '\n')
    print(line)
    return 1 if e else 0


def cmd_mirror(a):
    """윤곽만 그린 반쪽(X/.)을 대칭으로 펴고, 바깥 테두리는 d, 안쪽은 왼쪽 위=h · 가운데=w · 오른쪽 아래=s 로 칠한다."""
    icons = load(); pal = icons[a.like][0]
    q = [l.strip() for l in open(a.rows, encoding='utf8').read().split('\n') if l.strip()]
    if a.quad:
        top = [r + r[::-1] for r in q]; m = top + top[::-1]
    else:
        m = [r + r[::-1] for r in q]
    n = len(m)
    if any(len(r) != n for r in m) or n not in (8, 16):
        print('펼친 크기가 8×8 또는 16×16 이 아님: %d×%d' % (len(m[0]), n)); return 1
    c = (n - 1) / 2; rows = []
    for y in range(n):
        r = ''
        for x in range(n):
            if m[y][x] != 'X':
                r += '.'; continue
            edge = any(not (0 <= x + p < n and 0 <= y + q2 < n and m[y + q2][x + p] == 'X') for p, q2 in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if edge or (a.hole and abs(x - c) <= 0.6 and abs(y - c) <= 0.6):
                r += 'd'
            else:
                t = (x - c) + (y - c)
                r += 'h' if t < -n * 3 / 16 else ('w' if t < n * 2.5 / 16 else 's')
        rows.append(r)
    print(fmt(a.id, pal, rows))
    e, _ = lint_one(a.id, pal, rows, True)
    for msg in e:
        print('오류:', msg, file=sys.stderr)
    return 1 if e else 0


def cmd_snap(a):
    exe = shutil.which('spritefusion-pixel-snapper') or shutil.which('spritefusion-pixel-snapper.exe')
    if not exe:
        print('spritefusion-pixel-snapper 가 PATH 에 없습니다. (cargo install spritefusion-pixel-snapper - Rust 필요, 또는 웹판 https://spritefusion.com/pixel-snapper)'); return 2
    cmd = [exe, a.inp, a.out] + ([str(a.colors)] if a.colors else [])
    return subprocess.call(cmd)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    s = p.add_subparsers(dest='cmd', required=True)
    s.add_parser('list').set_defaults(f=cmd_list)
    x = s.add_parser('lint'); x.add_argument('ids', nargs='*'); x.add_argument('--sym'); x.add_argument('-v', '--verbose', action='store_true'); x.set_defaults(f=cmd_lint)
    x = s.add_parser('preview'); x.add_argument('ids', nargs='+'); x.add_argument('-o', required=True); x.add_argument('--zoom', type=int, default=16); x.add_argument('--bg', default='#c8cade'); x.set_defaults(f=cmd_preview)
    x = s.add_parser('from-png'); x.add_argument('png'); x.add_argument('--id', required=True); x.add_argument('--like', required=True); x.add_argument('-o'); x.set_defaults(f=cmd_from_png)
    x = s.add_parser('mirror'); x.add_argument('rows'); x.add_argument('--id', required=True); x.add_argument('--like', required=True); x.add_argument('--quad', action='store_true'); x.add_argument('--hole', action='store_true'); x.set_defaults(f=cmd_mirror)
    x = s.add_parser('snap'); x.add_argument('inp'); x.add_argument('out'); x.add_argument('colors', nargs='?', type=int); x.set_defaults(f=cmd_snap)
    a = p.parse_args()
    sys.exit(a.f(a) or 0)


if __name__ == '__main__':
    main()
