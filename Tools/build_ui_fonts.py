# M3: 한글 폰트 준비 (라이선스: Noto Sans KR = SIL OFL 1.1, 글꼴 파일 name 표 13/14번에 명시 확인됨).
# 원본은 Windows 에 설치된 가변 글꼴(NotoSansKR-VF.ttf, 기본 굵기가 Thin 100 이라 그대로는 못 쓴다)이다.
# 이 스크립트가 (1) 굵기 400/700 고정 인스턴스를 만들고 (2) 게임에서 쓰는 글자(+ 한글 완성형 자주 쓰는 글자)만 남겨 Unity 에 넣는다.
# 수정본이므로 OFL 규칙대로 예약 글꼴 이름 'Source' 를 쓰지 않고, 파일 이름 뒤에 -Subset 을 붙인다. 라이선스 안내 파일은 같은 폴더에 둔다.
# 사용: python -I -X utf8 Tools/build_ui_fonts.py <글꼴.ttf> <글자목록.txt> <출력 폴더>
import sys, os
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from fontTools import subset

src, chars_path, out_dir = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(out_dir, exist_ok=True)
chars = open(chars_path, encoding='utf-8').read()
chars = ''.join(sorted(set(c for c in chars if c not in '\r\n')))
report = []
for wname, w in (('Regular', 400), ('Bold', 700)):
    f = TTFont(src)
    cmap = f.getBestCmap()
    if wname == 'Regular':
        missing = [c for c in chars if ord(c) not in cmap]
        report.append('cmap 에 없는 글자(%d): %s' % (len(missing), ''.join(missing)))
    inst = instancer.instantiateVariableFont(f, {'wght': w}, inplace=False)
    opts = subset.Options(); opts.layout_features = ['kern', 'liga', 'ccmp', 'locl', 'mark', 'mkmk']; opts.name_IDs = [0, 1, 2, 3, 4, 5, 6, 13, 14]; opts.notdef_outline = True
    opts.glyph_names = False; opts.hinting = False; opts.drop_tables += ['DSIG']
    s = subset.Subsetter(opts); s.populate(text=chars); s.subset(inst)
    path = os.path.join(out_dir, 'NotoSansKR-%s-Subset.ttf' % wname)
    inst.save(path)
    report.append('%s: %d 바이트, 글리프 %d' % (path, os.path.getsize(path), len(inst.getGlyphOrder())))
print('\n'.join(report))
