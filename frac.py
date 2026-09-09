# -*- coding: utf-8 -*-
"""점 앵커 rect를 부모 기준 비율(스트레치) 앵커로 다시 쓴다. 기하학적으로 동일하다."""
import re, io, sys

V2 = r'\{x: ([-\d.eE+]+), y: ([-\d.eE+]+)\}'

def parse(path):
    t = io.open(path, encoding='utf-8', errors='replace').read()
    parts = re.split(r'(--- !u!\d+ &\d+[^\n]*\n)', t)
    head, docs = parts[0], [(parts[i], parts[i+1]) for i in range(1, len(parts), 2)]
    return t, head, docs

def f2(b, key):
    m = re.search(r'm_'+key+r': '+V2, b)
    return (float(m.group(1)), float(m.group(2))) if m else None

def build(docs):
    rt = {}           # fileID -> dict
    go_rt = {}        # gameObject fileID -> rect fileID
    layout, fitter, scrollpart = set(), set(), set()
    names = {}
    for h, b in docs:
        fid = re.search(r'&(\d+)', h).group(1)
        if h.startswith('--- !u!224 &'):
            go = re.search(r'm_GameObject: \{fileID: (\d+)\}', b).group(1)
            go_rt[go] = fid
            rt[fid] = dict(go=go, father=re.search(r'm_Father: \{fileID: (\d+)\}', b).group(1),
                           amin=f2(b,'AnchorMin'), amax=f2(b,'AnchorMax'), piv=f2(b,'Pivot'),
                           sd=f2(b,'SizeDelta'), ap=f2(b,'AnchoredPosition'), body=b)
        elif h.startswith('--- !u!1 &'):
            m = re.search(r'\n  m_Name: (.*)', b)
            if m: names[fid] = m.group(1).strip()
        elif h.startswith('--- !u!114 &'):
            go = re.search(r'm_GameObject: \{fileID: (\d+)\}', b)
            if not go: continue
            go = go.group(1)
            if 'm_ChildControlWidth' in b or 'm_CellSize' in b: layout.add(go)
            if 'm_HorizontalFit' in b: fitter.add(go)
            for k in ('m_Content', 'm_Viewport'):
                m = re.search(k+r': \{fileID: (\d+)\}', b)
                if m and m.group(1) != '0': scrollpart.add(m.group(1))
    return rt, go_rt, layout, fitter, scrollpart, names

def resolve(rt, root, W, H):
    """부모→자식 순으로 각 rect의 부모 좌표계 사각형(left,bottom,w,h)을 구한다."""
    size = {root: (W, H)}
    box  = {}
    kids = {}
    for fid, r in rt.items():
        kids.setdefault(r['father'], []).append(fid)
    order, stack = [], [root]
    while stack:
        n = stack.pop()
        for c in kids.get(n, []):
            order.append(c); stack.append(c)
    for fid in order:
        r = rt[fid]
        pw, ph = size[r['father']]
        w = (r['amax'][0]-r['amin'][0])*pw + r['sd'][0]
        h = (r['amax'][1]-r['amin'][1])*ph + r['sd'][1]
        # 피벗은 sizeDelta에만 곱한다(Unity: offsetMin = anchoredPosition - sizeDelta*pivot).
        # 점 앵커에서는 sizeDelta == 크기라 최종 크기를 곱해도 우연히 맞지만, 스트레치에서는 틀린다.
        left   = r['amin'][0]*pw + r['ap'][0] - r['piv'][0]*r['sd'][0]
        bottom = r['amin'][1]*ph + r['ap'][1] - r['piv'][1]*r['sd'][1]
        size[fid] = (w, h); box[fid] = (left, bottom, w, h)
    return size, box, order
