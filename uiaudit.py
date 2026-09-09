# -*- coding: utf-8 -*-
"""
UI 프리팹 정밀 측정기.

네 가지를 잰다:
  ① 텍스트 넘침   — 글꼴이 상자를 넘는가(자동크기면 최소 글꼴 기준)
  ② 판 밖 이탈    — 요소가 주 패널 밖으로 나가는가
  ③ 버튼 가려짐   — 버튼 위에 <b>나중에 그려지는 불투명 요소</b>가 덮는가
  ④ 공간 여유     — 내용 묶음이 판 대비 얼마나 차는가(너무 빡빡/너무 헐거움)

rect는 uGUI 규칙 그대로 푼다:
  size   = (anchorMax-anchorMin)*parent + sizeDelta
  offset = anchorMin*parent + anchoredPosition - pivot*sizeDelta      ← 피벗은 sizeDelta에만
localScale은 <b>중심 기준</b>으로 누적한다 — 모서리 기준으로 하면 배율 레이어가 거짓 이탈로 잡힌다.
"""
import re, io, glob

V2 = r'\{x: ([-\d.eE+]+), y: ([-\d.eE+]+)'


def _f2(b, key):
    m = re.search(r'm_' + key + r': ' + V2, b)
    return (float(m.group(1)), float(m.group(2))) if m else None


def load(path):
    t = io.open(path, encoding='utf-8', errors='replace').read()
    parts = re.split(r'(--- !u!\d+ &\d+[^\n]*\n)', t)
    docs = [(parts[i], parts[i + 1]) for i in range(1, len(parts), 2)]

    names, rt, comp = {}, {}, {}
    for h, b in docs:
        fid = re.search(r'&(\d+)', h).group(1)
        if h.startswith('--- !u!1 &'):
            m = re.search(r'\n  m_Name: (.*)', b)
            a = re.search(r'm_IsActive: (\d)', b)
            if m:
                names[fid] = {'name': m.group(1).strip(), 'active': (a.group(1) == '1') if a else True}
        elif h.startswith('--- !u!224 &'):
            mgo = re.search(r'm_GameObject: \{fileID: (\d+)\}', b)
            if not mgo:
                continue
            if any(_f2(b, k) is None for k in
                   ('AnchorMin', 'AnchorMax', 'Pivot', 'SizeDelta', 'AnchoredPosition')):
                continue
            ms = re.search(r'm_LocalScale: ' + V2, b)
            mch = re.search(r'm_Children:\n((?:  - \{fileID: \d+\}\n)*)', b)
            kids = re.findall(r'fileID: (\d+)', mch.group(1)) if mch else []
            rt[fid] = {
                'order': kids,
                'go': mgo.group(1),
                'father': re.search(r'm_Father: \{fileID: (\d+)\}', b).group(1),
                'amin': _f2(b, 'AnchorMin'), 'amax': _f2(b, 'AnchorMax'),
                'piv': _f2(b, 'Pivot'), 'sd': _f2(b, 'SizeDelta'), 'ap': _f2(b, 'AnchoredPosition'),
                'scale': (float(ms.group(1)), float(ms.group(2))) if ms else (1., 1.),
                'children': [],
            }
        elif h.startswith('--- !u!114'):
            mgo = re.search(r'm_GameObject: \{fileID: (\d+)\}', b)
            if not mgo:
                continue
            go = mgo.group(1)
            d = comp.setdefault(go, {})
            if 'm_FillMethod' in b:                      # Image
                mc = re.search(r'm_Color: \{r: ([-\d.eE+]+), g: ([-\d.eE+]+), b: ([-\d.eE+]+), a: ([-\d.eE+]+)\}', b)
                mr = re.search(r'm_RaycastTarget: (\d)', b)
                d['img'] = {'alpha': float(mc.group(4)) if mc else 1.0,
                            'ray': (mr.group(1) == '1') if mr else True,
                            'sprite': 'm_Sprite: {fileID: 0}' not in b}
            if 'm_fontSize:' in b and 'm_text:' in b:    # TMP
                fs = re.search(r'm_fontSize: ([-\d.eE+]+)', b)
                au = re.search(r'm_enableAutoSizing: (\d)', b)
                mn = re.search(r'm_fontSizeMin: ([-\d.eE+]+)', b)
                tx = re.search(r'm_text: (.*)', b)
                d['tmp'] = {'size': float(fs.group(1)) if fs else 0.,
                            'auto': (au.group(1) == '1') if au else False,
                            'min': float(mn.group(1)) if mn else 0.,
                            'text': (tx.group(1).strip() if tx else '')}
            if 'm_OnClick' in b:
                d['btn'] = True
            if 'm_ChildControlWidth' in b or 'm_CellSize' in b:
                d['layout'] = True
            if 'm_HorizontalFit' in b:
                d['fitter'] = True
    # 자식 목록은 m_Children 순서를 그대로 쓴다 — 그것이 uGUI 그리기 순서다.
    for fid, r in rt.items():
        r['children'] = [c for c in r.get('order', []) if c in rt]
    for fid, r in rt.items():                      # 목록에 안 실린 자식 보강
        f = r['father']
        if f in rt and fid not in rt[f]['children']:
            rt[f]['children'].append(fid)
    return t, names, rt, comp


def solve(rt, root, W=1920., H=1080.):
    """(중심x, 중심y, 폭, 높이, 그리기순서, 깊이) — 중심 기준 + 배율 누적."""
    out = {root: (W / 2, H / 2, W, H, 0, 0)}
    acc = {root: (1., 1.)}
    order = [0]

    def walk(nd, depth):
        pcx, pcy, pw, ph, _, _ = out[nd]
        sx, sy = acc[nd]
        for f in rt[nd]['children']:
            r = rt[f]
            w = (r['amax'][0] - r['amin'][0]) * pw + r['sd'][0]
            h = (r['amax'][1] - r['amin'][1]) * ph + r['sd'][1]
            l = r['amin'][0] * pw + r['ap'][0] - r['piv'][0] * r['sd'][0]
            b = r['amin'][1] * ph + r['ap'][1] - r['piv'][1] * r['sd'][1]
            ccx = pcx + (l + w / 2 - pw / 2) * sx
            ccy = pcy + (b + h / 2 - ph / 2) * sy
            osx, osy = r['scale']
            order[0] += 1
            out[f] = (ccx, ccy, w * sx * osx, h * sy * osy, order[0], depth + 1)
            acc[f] = (sx * osx, sy * osy)
            walk(f, depth + 1)
    walk(root, 0)
    return out


def rect(v):
    cx, cy, w, h = v[0], v[1], v[2], v[3]
    return (cx - w / 2, cy - h / 2, w, h)


def overlap(a, b):
    ax, ay, aw, ah = rect(a); bx, by, bw, bh = rect(b)
    ox = max(0, min(ax + aw, bx + bw) - max(ax, bx))
    oy = max(0, min(ay + ah, by + bh) - max(ay, by))
    return ox * oy


def audit(path):
    t, names, rt, comp = load(path)
    roots = [f for f, r in rt.items() if r['father'] not in rt]
    if not roots:
        return None
    root = roots[0]
    S = solve(rt, root)

    # 주 패널 = 전체화면이 아닌 최대 최상위
    tops = [f for f in rt[root]['children']]
    cand = [f for f in tops if not (S[f][2] >= 1900 and S[f][3] >= 1060)]
    panel = max(cand or tops, key=lambda f: S[f][2] * S[f][3])
    px, py, pw, ph = rect(S[panel])

    def nm(f):
        return names.get(rt[f]['go'], {}).get('name', '?')

    def alive(f):
        g = f
        while g in rt:
            if not names.get(rt[g]['go'], {}).get('active', True):
                return False
            g = rt[g]['father']
        return True

    def under(f, top):
        g = f
        while g in rt:
            if g == top: return True
            g = rt[g]['father']
        return False

    # 판 안쪽 자손만 본다 — 암막·플래시는 판의 형제라 비교 대상이 아니다.
    sub = [f for f in S if f != root and f != panel and alive(f) and under(f, panel)]

    res = {'panel': nm(panel), 'pw': pw, 'ph': ph, 'nodes': len(sub), 'fitter': 'UIWindowFitter' in t}

    # ① 텍스트 넘침 — 자동크기면 최소 글꼴이 기준
    over = []
    for f in sub:
        c = comp.get(rt[f]['go'], {})
        if 'tmp' not in c:
            continue
        tm = c['tmp']; _, _, w, h = rect(S[f])
        if h <= 1 or tm['size'] <= 0:
            continue
        need = tm['min'] if tm['auto'] and tm['min'] > 0 else tm['size']
        if need > h * 1.05:
            over.append({'n': nm(f), 'need': round(need, 1), 'h': round(h, 1), 'auto': tm['auto']})
    res['text_over'] = over

    # ② 판 밖 이탈
    out = []
    for f in sub:
        x, y, w, h = rect(S[f])
        d = max(px - x, py - y, (x + w) - (px + pw), (y + h) - (py + ph))
        if d > 2:
            out.append({'n': nm(f), 'px': round(d, 1), 'w': round(w), 'h': round(h)})
    res['panel_over'] = sorted(out, key=lambda r: -r['px'])

    # ③ 버튼 가려짐 — 나중에 그려지는 불투명(a>0.5) 이미지가 버튼을 덮는가
    occ = []
    imgs = [f for f in sub if 'img' in comp.get(rt[f]['go'], {})]
    for f in sub:
        c = comp.get(rt[f]['go'], {})
        if 'btn' not in c:
            continue
        area = S[f][2] * S[f][3]
        if area <= 1:
            continue
        worst = 0.0; who = ''
        for g in imgs:
            if g == f or S[g][4] <= S[f][4]:
                continue                                  # 버튼보다 먼저 그려짐 = 아래
            if g in _ancestors(rt, f) or f in _ancestors(rt, g):
                continue                                  # 자기 자손/조상은 제외
            gi = comp[rt[g]['go']]['img']
            if gi['alpha'] < 0.5:
                continue
            ov = overlap(S[f], S[g]) / area
            if ov > worst:
                worst, who = ov, nm(g)
        if worst > 0.35:
            occ.append({'n': nm(f), 'cov': round(worst * 100), 'by': who})
    res['btn_occluded'] = sorted(occ, key=lambda r: -r['cov'])

    # ④ 공간 여유 — 최상위 자식들의 묶음 상자가 판을 얼마나 차지하나
    kids = [f for f in rt[panel]['children'] if alive(f)]
    if kids:
        xs = [rect(S[f]) for f in kids]
        l = min(r[0] for r in xs); rr = max(r[0] + r[2] for r in xs)
        b = min(r[1] for r in xs); tt = max(r[1] + r[3] for r in xs)
        res['fill_w'] = round((rr - l) / pw * 100, 1)
        res['fill_h'] = round((tt - b) / ph * 100, 1)
    else:
        res['fill_w'] = res['fill_h'] = 0
    return res


def _ancestors(rt, f):
    s = set(); g = f
    while g in rt:
        s.add(g); g = rt[g]['father']
    return s
