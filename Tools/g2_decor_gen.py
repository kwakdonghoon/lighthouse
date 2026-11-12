# -*- coding: utf-8 -*-
"""G2' 꾸미기 데이터 생성기: decor.json(표) 과 RenderData.Generated.cs(굽기용 사본)의 꾸미기 부분을 같은 원본에서 다시 쓴다.
HTML 판은 동결이라 이 스크립트가 Unity 판 꾸미기 데이터의 원본이다. 두 곳이 어긋나면 시험(DecorG2Tests)이 잡는다.
사용: python Tools/g2_decor_gen.py   (프로젝트 루트에서)
"""
import json, re, io, os, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
DECOR = os.path.join(ROOT, 'Assets/_Lighthouse/Resources/Data/decor.json')
RDATA = os.path.join(ROOT, 'Assets/_Lighthouse/Scripts/Render/RenderData.Generated.cs')
SLOTS = os.path.join(ROOT, 'Tools/g2_slots.json')

# ---------------- 새 아이템 15종 (광고 3 + 팩 4 + 2차 8). 격자는 한 칸 = 도트 1개, '.' = 투명 ----------------
NEW_ITEMS = [
    # --- 광고 전용 3종 (D34·D43·U5): 업적(안내한 배 N척)으로도 무료로 얻는다 ---
    dict(id='lantern_float', phase='G2', name='소원 등불', size='S', zones=['shore'], src='ad', need=['guided', 1000], ax=4, ay=5,
         rows=['........', '..RRRR..', '..rRRe..', '..HHhk..', 'wWWWWWWx', '.wwWWxx.', '...wx...']),
    dict(id='kite_red', phase='G2', name='빨간 연', size='S', zones=['sky'], src='ad', need=['guided', 5000], ax=5, ay=6,
         rows=['....RR....', '...RRrr...', '..RRRRrr..', '.RRRUUrrr.', '..RRUUrr..', '...RRrr...', '....Rr....', '....w.....', '...w......', '....w.....', '.....w....', '....w.....']),
    dict(id='balloon_cluster', phase='G2', name='풍선 묶음', size='S', zones=['sky'], src='ad', need=['guided', 15000], ax=5, ay=6,
         rows=['..RR..AA..', '.RRRr.AAa.', '.RRRr.AAa.', '..Rr...Aa.', '..w.TT.w..', '...wTTt...', '....TTt...', '.....Tt...', '....w.w...', '.....w....', '.....w....', '.....w....']),
    # --- 축제 마을 팩 4종 (D33·D42): 시안 격자(draft). 최종 도트는 D37(정책 확인) 뒤 ---
    dict(id='festival_bunting', phase='G2', name='축제 깃발 줄', size='M', zones=['front', 'back', 'shore'], src='iap', need=['pack:festival'], ax=5, ay=10, draft=1,
         rows=['..........', '.x........', '.xi.......', '.xrai.....', '.xrabti.W.', '.x.abtraw.', '.x...traw.', '.x.....aw.', '.x......w.', '........w.', '........w.', '........w.', '..........']),
    dict(id='festival_stall', phase='G2', name='작은 축제 가판', size='L', zones=['back'], src='iap', need=['pack:festival'], ax=8, ay=15, draft=1,
         rows=['................', '......RR........', '....RRRRCC......', '..RRRRCCCCRR....', '.rRRCCCCRRRRCC..', '..xcCCRRRRCCCCd.', '..x..rRRCCCCdd..', '..x..hkcCCdd.w..',
               '..x.aAC..d...w..', '..x.CCCCCx...w..', '..CCCCCCCxC..w..', '..wcCCCCCxCCCw..', '..wwwcCCCxCCCx..', '..wwwwwcCxCxxx..', '..wwwwwwwxxxxx..', '....wwwwwxxxxx..', '......wwwxxx....', '........wx......']),
    dict(id='festival_lanterns', phase='G2', name='등롱 기둥', size='S', zones=['front', 'back', 'shore'], src='iap', need=['pack:festival'], ax=4, ay=11, draft=1,
         rows=['.RWWW...', '.rewW...', '.hkHHRR.', '.hkhkre.', '...hkhk.', '...wxhk.', '...wx...', '...wx...', '...wx...', '...wx...', '...wx...', '........']),
    dict(id='festival_arch', phase='G2', name='축제 환영 아치', size='M', zones=['front', 'back', 'shore'], src='iap', need=['pack:festival'], ax=5, ay=14, draft=1,
         rows=['..........', '.LLL......', 'WWWPPP....', 'wwWWWLLL..', 'wwwwWWWPPP', 'wwxewwWWWq', 'wwxea.wwxx', 'wwx...twxx', 'wwx....wxx', 'wwx....wxx', 'wwx....wxx', 'wwx....wxx', '.wx....wxx', '.......wxx', '.......wxx', '.......wxx', '..........']),
    # --- 2차 8종 (U4): 길·하늘·물가 보강, 해금 15~60일 분산 ---
    dict(id='path_brick', phase='G2', name='벽돌 길 조각', size='S', zones=['path'], src='earn', need=['garden', 17], ax=4, ay=3,
         rows=['...RR...', '.RJRRJR.', 'rRRJRRRe', '.rrRJRe.', '...rJ...']),
    dict(id='path_plank', phase='G2', name='나무판 길', size='S', zones=['path'], src='guided', need=['guided', 40000], ax=4, ay=3,
         rows=['...WW...', '.WWwWWw.', 'WwWWwWWx', '.wwWwWx.', '...wx...']),
    dict(id='path_petal', phase='G2', name='꽃잎 길', size='S', zones=['path'], src='season', need=['spring'], ax=4, ay=3,
         rows=['..Z..U..', '.ZZ.ZZ..', 'Z.UKZ.ZU', '.UZ.Z.Z.', '..Z.U...']),
    dict(id='sandcastle', phase='G2', name='모래성', size='S', zones=['shore'], src='guided', need=['guided', 20000], ax=5, ay=7,
         rows=['....RR....', '....wr....', '..D.wD.D..', '.DDDDDDDD.', 'DDJDDJDDJE', 'DJJJDJJJJE', 'DJJJJJJJJE', '.EEEEEEEE.', '..........']),
    dict(id='beach_umbrella', phase='G2', name='해변 파라솔', size='M', zones=['shore'], src='earn', need=['port', 19], ax=6, ay=13,
         rows=['.....TT.....', '...TTUUTT...', '..TUUTTUUT..', '.tTUUTTUUTu.', 'ttTTUUttUUuu', 't.t..wx...uu', '.....wx.....', '.....wx.....', '.....wx.....', '.....wx.....', '.....wx.....', '.....wx.....', '....DDDD....', '...DJJJJE...', '............']),
    dict(id='snowman', phase='G2', name='눈사람', size='S', zones=['front', 'back'], src='season', need=['winter'], ax=4, ay=11,
         rows=['...ii...', '..iIIj..', '..UUUU..', '.U*UU*U.', '.UUOUUU.', '..UUUU..', 'wwUUUUww', '.UU*UUU.', 'UUUUUUUU', 'UUU*UUcc', '.UUUUccc', '..cccc..']),
    dict(id='paper_plane', phase='G2', name='종이비행기', size='S', zones=['sky'], src='guided', need=['guided', 30000], ax=5, ay=5,
         rows=['..........', '.........U', '.......UUC', '.....UUUUc', '...UUUUUcc', '.UUUUUUccc', 'UUUUUccccd', '.cccccdddd', '...cdddd..', '.....dd...']),
    dict(id='star_mobile', phase='G2', name='별 모빌', size='S', zones=['sky'], src='earn', need=['lens', 21], ax=5, ay=6,
         rows=['.gggggggg.', '.g...g..g.', '.g...g..g.', 'AAA..g.AAA', 'AAa..g.AAa', '.A...g..A.', '...AAAa...', '...AAAAa..', '....Aaa...', '..........']),
]

ITEM_PATCH = {   # 기존 10종 중 바뀌는 것: 호박 더미의 두 번째 해금 경로(D44, 안내한 배 N척)
    'pumpkin_pile': dict(alt=['guided', 1500]),
}


def load_slots_patch():
    if os.path.exists(SLOTS):
        return json.load(io.open(SLOTS, encoding='utf-8'))
    return {}


def main():
    raw = io.open(DECOR, encoding='utf-8', newline='').read()
    d = json.loads(raw)
    items = [i for i in d['items'] if i['id'] not in {n['id'] for n in NEW_ITEMS}]
    for it in items:
        if it['id'] in ITEM_PATCH:
            it.update(ITEM_PATCH[it['id']])
    for n in NEW_ITEMS:
        o = dict(n)
        assert len({len(r) for r in o['rows']}) == 1, o['id'] + ' 행 길이'
        items.append(o)
    d['items'] = items
    sp = load_slots_patch()
    for s in d['slots']:
        if s['id'] in sp:
            p = sp[s['id']]
            s.pop('abByStage', None)
            if any(k in p for k in ('ab', 'abLin', 'xyFn')):
                for k in ('ab', 'abLin', 'xyFn'):
                    s.pop(k, None)
            if 'open' in p:
                s['open'] = {}
            s.update(p)
    out = json.dumps(d, indent=1, ensure_ascii=False) + '\n'
    io.open(DECOR, 'w', encoding='utf-8', newline='').write(out)

    # ---- RenderData.Generated.cs 의 DecorItems·DecorSlots ----
    src = io.open(RDATA, encoding='utf-8', newline='').read()
    nl = '\r\n' if '\r\n' in src else '\n'

    def fmt(x):
        s = repr(float(x)) if not float(x).is_integer() else str(int(x))
        return s

    il = []
    for it in d['items']:
        il.append('        "%s;%s;%s;%s;%s;%d;%d;%d;%d;%s",' % (it['id'], it['name'], it['size'], ','.join(it['zones']), it['src'], it['ax'], it['ay'], 1 if it.get('ss') else 0, 1 if it.get('sk') else 0, '|'.join(it['rows'])))
    sl = []
    for s in d['slots']:
        op = s['open']; ok, ov = ('base', 1) if 'base' in op else ('stage', op['stage']) if 'stage' in op else ('lv', op['lv'])
        ms = s.get('minStage', 1)
        if 'xyFn' in s:
            sl.append('        "%s;%s;%s;xy;0;0;0;0;%s;%d;%d;%d",' % (s['id'], s['chip'], s['size'], ok, ov, 1 if s.get('blocked') else 0, ms))
        else:
            if 'ab' in s:
                ka, ca, kb, cb = 0, s['ab'][0], 0, s['ab'][1]
            else:
                ka, ca, kb, cb = s['abLin'][0]['k'], s['abLin'][0]['c'], s['abLin'][1]['k'], s['abLin'][1]['c']
            sl.append('        "%s;%s;%s;ab;%s;%s;%s;%s;%s;%d;%d;%d",' % (s['id'], s['chip'], s['size'], fmt(ka), fmt(ca), fmt(kb), fmt(cb), ok, ov, 1 if s.get('blocked') else 0, ms))

    def repl(name, lines, text):
        pat = re.compile(r'(public static readonly string\[\] %s =\s*\{)(.*?)(\r?\n    \};)' % name, re.S)
        assert pat.search(text), name
        return pat.sub(lambda mm: mm.group(1) + nl + nl.join(lines) + nl + '    };', text, count=1)

    src = repl('DecorItems', il, src)
    src = repl('DecorSlots', sl, src)
    io.open(RDATA, 'w', encoding='utf-8', newline='').write(src)
    print('items', len(d['items']), 'slots', len(d['slots']), 'blocked', sum(1 for s in d['slots'] if s.get('blocked')))


if __name__ == '__main__':
    main()
