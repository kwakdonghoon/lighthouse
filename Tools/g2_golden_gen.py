# -*- coding: utf-8 -*-
"""G2' 새 골든 생성: 다시 정한 8자리의 열림 비트·좌표 표(Tools/golden/g2/decor_g2.json).
설계 문서(team/design/decor-g2-unity.md 3절)의 표를 손으로 옮긴 것이다 — C# 구현이나 decor.json 에서 읽지 않는다(독립 기준).
사용: python Tools/g2_golden_gen.py   (프로젝트 루트에서)
"""
import json, io, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
OUT = os.path.join(ROOT, 'Tools/golden/g2/decor_g2.json')

# id, 구역, 크기, 열림 종류(lv|stage), 값, 최소 섬 단계, 좌표 (a, b) — 섬 크기 N 과 무관한 고정 좌표
DESIGN = [
    ('F2', 'front', 'S', 'lv', 12, 4, (5.75, -0.5)),
    ('F3', 'front', 'S', 'lv', 16, 4, (6, 4.5)),
    ('F7', 'front', 'M', 'stage', 4, 1, (3, 6.25)),
    ('B1', 'back', 'M', 'lv', 8, 4, (-6.75, 2.75)),
    ('B2', 'back', 'L', 'stage', 6, 1, (-7.5, -0.5)),
    ('B4', 'back', 'S', 'lv', 16, 4, (1.25, -6.75)),
    ('B5', 'back', 'S', 'stage', 4, 1, (-6, 5)),
    ('B6', 'back', 'M', 'stage', 4, 1, (3.5, -6)),
]


def is_open(kind, val, mn, st, gl):
    if st < mn:
        return False
    return gl >= val if kind == 'lv' else st >= val


rows = []
for sid, chip, size, kind, val, mn, ab in DESIGN:
    bits = [''.join('1' if is_open(kind, val, mn, st, gl) else '0' for gl in range(31)) for st in range(1, 7)]
    rows.append(dict(id=sid, chip=chip, size=size, open={kind: val}, minStage=mn, ab=list(ab), bits=bits))
d = dict(note="G2' 다시 정한 8자리 — 열림 비트(단계 1~6 × 정원 레벨 0~30)와 좌표. 설계 문서 3절의 표. 독립 기준: tools/g2_golden_gen.py", slots=rows)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
io.open(OUT, 'w', encoding='utf-8', newline='').write(json.dumps(d, ensure_ascii=False, indent=1) + '\n')
print('wrote', OUT)
