# -*- coding: utf-8 -*-
"""
레이아웃 그룹이 <b>런타임에</b> 잡는 크기를 재현한다.

프리팹에 적힌 rect는 레이아웃 그룹 아래에서 죽은 값이다 — Unity가 첫 프레임에 다시 쓴다.
그래서 "프리팹을 재보니 안 넘친다"는 말이 레이아웃 그룹 화면에서는 근거가 되지 못한다.
여기서는 Unity의 계산을 그대로 따라해 <b>내용이 들어가는지</b>를 본다.

Unity 규칙(HorizontalOrVerticalLayoutGroup):
  주축 필요량 = padding + Σ(자식 preferred) + spacing×(n-1)
  자식 preferred = LayoutElement.preferredHeight(있으면)
                   아니면 rect 높이(childControlHeight=0일 때)
                   아니면 텍스트/이미지의 자체 preferred
"""
import re, io


def parse(path):
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
            sd = re.search(r'm_SizeDelta: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+)\}', b)
            am = re.search(r'm_AnchorMin: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+)\}', b)
            aM = re.search(r'm_AnchorMax: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+)\}', b)
            if not (sd and am and aM):
                continue
            mch = re.search(r'm_Children:\n((?:  - \{fileID: \d+\}\n)*)', b)
            rt[fid] = {
                'go': mgo.group(1),
                'father': re.search(r'm_Father: \{fileID: (\d+)\}', b).group(1),
                'sd': (float(sd.group(1)), float(sd.group(2))),
                'amin': (float(am.group(1)), float(am.group(2))),
                'amax': (float(aM.group(1)), float(aM.group(2))),
                'kids': re.findall(r'fileID: (\d+)', mch.group(1)) if mch else [],
            }
        elif h.startswith('--- !u!114'):
            mgo = re.search(r'm_GameObject: \{fileID: (\d+)\}', b)
            if not mgo:
                continue
            d = comp.setdefault(mgo.group(1), {})
            if 'm_ChildControlWidth' in b:                 # Horizontal/VerticalLayoutGroup
                def g(k, dv=0.0):
                    m = re.search(k + r': ([-\d.eE+]+)', b)
                    return float(m.group(1)) if m else dv
                pad = {k: g('m_' + k.capitalize() if False else k, 0) for k in ()}
                pl = re.search(r'm_Padding:\n\s+m_Left: (\d+)\n\s+m_Right: (\d+)\n\s+m_Top: (\d+)\n\s+m_Bottom: (\d+)', b)
                d['group'] = {
                    'vertical': 'm_ChildControlWidth' in b and '!u!114' in h,   # 방향은 아래에서 보정
                    'spacing': g('m_Spacing'),
                    'ctrlW': g('m_ChildControlWidth') == 1, 'ctrlH': g('m_ChildControlHeight') == 1,
                    'expW': g('m_ChildForceExpandWidth') == 1, 'expH': g('m_ChildForceExpandHeight') == 1,
                    'pad': (int(pl.group(1)), int(pl.group(2)), int(pl.group(3)), int(pl.group(4))) if pl else (0, 0, 0, 0),
                    'raw': b,
                }
            if 'm_MinWidth' in b:                          # LayoutElement
                def g(k):
                    m = re.search(k + r': ([-\d.eE+]+)', b)
                    return float(m.group(1)) if m else -1.0
                d['le'] = {'minW': g('m_MinWidth'), 'minH': g('m_MinHeight'),
                           'prefW': g('m_PreferredWidth'), 'prefH': g('m_PreferredHeight'),
                           'flexW': g('m_FlexibleWidth'), 'flexH': g('m_FlexibleHeight')}
            if 'm_HorizontalFit' in b:
                def g(k):
                    m = re.search(k + r': (\d+)', b)
                    return int(m.group(1)) if m else 0
                d['fitter'] = {'h': g('m_HorizontalFit'), 'v': g('m_VerticalFit')}
            if 'm_fontSize:' in b and 'm_text:' in b:
                fs = re.search(r'm_fontSize: ([-\d.eE+]+)', b)
                d['tmp'] = {'size': float(fs.group(1)) if fs else 0.}
    return t, names, rt, comp


def is_vertical(raw):
    """VerticalLayoutGroup 인지 — 스크립트 GUID로는 못 가르니 필드 순서/존재로 본다."""
    # Unity는 두 그룹이 같은 필드를 쓴다. 방향은 m_Script GUID로만 갈리므로
    # 호출부에서 알려주는 편이 정확하다. 여기서는 세로로 가정한다(이 프로젝트의 UI 대부분).
    return True


def sim(path, group_names=None, screen=(1920., 1080.)):
    """레이아웃 그룹을 가진 노드마다 주축 필요량 vs 실제 크기를 계산한다."""
    t, names, rt, comp = parse(path)
    roots = [f for f, r in rt.items() if r['father'] not in rt]
    if not roots:
        return []
    root = roots[0]

    size = {root: screen}

    def resolve(nd):
        pw, ph = size[nd]
        for c in rt[nd]['kids']:
            if c not in rt:
                continue
            r = rt[c]
            w = (r['amax'][0] - r['amin'][0]) * pw + r['sd'][0]
            h = (r['amax'][1] - r['amin'][1]) * ph + r['sd'][1]
            size[c] = (w, h)
            resolve(c)
    resolve(root)

    def nm(f):
        return names.get(rt[f]['go'], {}).get('name', '?')

    def alive(f):
        g = f
        while g in rt:
            if not names.get(rt[g]['go'], {}).get('active', True):
                return False
            g = rt[g]['father']
        return True

    out = []
    for f in rt:
        c = comp.get(rt[f]['go'], {})
        if 'group' not in c or not alive(f):
            continue
        grp = c['group']
        kids = [k for k in rt[f]['kids'] if k in rt and alive(k)]
        if not kids:
            continue
        pl, pr, pt, pb = grp['pad']
        needH = pt + pb + grp['spacing'] * (len(kids) - 1)
        needW = pl + pr + grp['spacing'] * (len(kids) - 1)
        maxW = 0.
        for k in kids:
            kc = comp.get(rt[k]['go'], {})
            le = kc.get('le')
            ph = le['prefH'] if le and le['prefH'] >= 0 else (le['minH'] if le and le['minH'] >= 0 else size[k][1])
            pw = le['prefW'] if le and le['prefW'] >= 0 else (le['minW'] if le and le['minW'] >= 0 else size[k][0])
            needH += ph
            needW += pw
            maxW = max(maxW, pw)
        w, h = size[f]
        has_fit = 'fitter' in c
        out.append({
            'node': nm(f), 'kids': len(kids),
            'w': round(w, 1), 'h': round(h, 1),
            'needH': round(needH, 1), 'needW': round(needW, 1), 'maxW': round(maxW, 1),
            'fitter': has_fit, 'spacing': grp['spacing'], 'pad': grp['pad'],
        })
    return out
