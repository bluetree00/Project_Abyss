# -*- coding: utf-8 -*-
import frac, glob, io, re, sys, os, shutil, datetime

def plan(path, W=1920., H=1080.):
    t, head, docs = frac.parse(path)
    rt, go_rt, layout, fitter, scroll, names = frac.build(docs)
    root = next(f for f,r in rt.items() if r['father']=='0')
    size, box, order = frac.resolve(rt, root, W, H)
    top = {f for f,r in rt.items() if r['father']==root}
    conv=[]
    for fid in order:
        r=rt[fid]; pf=r['father']
        if fid in top or r['amin']!=r['amax']: continue
        if pf in rt and rt[pf]['go'] in layout: continue
        if r['go'] in fitter or fid in scroll: continue
        if size[pf][0]<=0 or size[pf][1]<=0: continue
        conv.append(fid)
    return t, head, docs, rt, size, box, conv, root, names

def num(v):
    return ('%.6f' % v).rstrip('0').rstrip('.') or '0'

def convert(path, W=1920., H=1080.):
    t, head, docs, rt, size, box, conv, root, names = plan(path, W, H)
    cs=set(conv)
    out=[head]
    for h,b in docs:
        fid=re.search(r'&(\d+)',h).group(1)
        if h.startswith('--- !u!224 &') and fid in cs:
            pw,ph = size[rt[fid]['father']]
            l,bo,w,hh = box[fid]
            b=re.sub(r'm_AnchorMin: \{x: [-\d.eE+]+, y: [-\d.eE+]+\}',
                     'm_AnchorMin: {x: %s, y: %s}'%(num(l/pw), num(bo/ph)), b, count=1)
            b=re.sub(r'm_AnchorMax: \{x: [-\d.eE+]+, y: [-\d.eE+]+\}',
                     'm_AnchorMax: {x: %s, y: %s}'%(num((l+w)/pw), num((bo+hh)/ph)), b, count=1)
            b=re.sub(r'm_AnchoredPosition: \{x: [-\d.eE+]+, y: [-\d.eE+]+\}',
                     'm_AnchoredPosition: {x: 0, y: 0}', b, count=1)
            b=re.sub(r'm_SizeDelta: \{x: [-\d.eE+]+, y: [-\d.eE+]+\}',
                     'm_SizeDelta: {x: 0, y: 0}', b, count=1)
        out.append(h); out.append(b)
    return ''.join(out), box, len(conv)

def verify(path, before_box, W=1920., H=1080.):
    """변환 후 다시 풀어 모든 rect가 같은 자리에 있는지 확인한다."""
    t, head, docs = frac.parse(path)
    rt, *_ = frac.build(docs)
    root = next(f for f,r in rt.items() if r['father']=='0')
    _, box, _ = frac.resolve(rt, root, W, H)
    worst=0.0; who=None
    for fid, b in before_box.items():
        if fid not in box: continue
        d=max(abs(x-y) for x,y in zip(b, box[fid]))
        if d>worst: worst, who = d, fid
    return worst, who

if __name__=='__main__':
    names=sys.argv[1:]
    stamp=datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    bak='Assets/RelicFairy/UI/_PrefabBackup'
    os.makedirs(bak, exist_ok=True)
    for n in names:
        path=glob.glob(f'Assets/RelicFairy/UI/**/{n}.prefab', recursive=True)[0]
        new, box, cnt = convert(path)
        if cnt==0:
            print(f'{n:24s} 변환 대상 없음 — 건드리지 않음'); continue
        shutil.copy(path, f'{bak}/{n}_{stamp}_prefrac.prefab')
        io.open(path,'w',encoding='utf-8',newline='').write(new)
        worst, who = verify(path, box)
        flag = 'OK' if worst < 0.05 else f'!! 어긋남 {worst:.3f}px'
        print(f'{n:24s} {cnt:3d}개 변환   최대 오차 {worst:.4f}px  {flag}')
