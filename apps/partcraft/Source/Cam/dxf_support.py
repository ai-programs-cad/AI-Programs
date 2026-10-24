"""Offline review-only DXF conversion. No SolidWorks/COM dependencies.
Original records are byte-preserved except CUT SPLINE and HANDSEED.
"""
import argparse, collections, hashlib, json, math, sys, time
from pathlib import Path
from fractions import Fraction as F
from decimal import Decimal as D
import certified_curves as cc
from InspectDxfStructure import inspect

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest().upper()

def read(p):
    lines=Path(p).read_bytes().splitlines(keepends=True)
    if len(lines)%2:raise ValueError('Odd tag count')
    pairs=[(int(lines[i].strip()),lines[i+1].decode('utf-8-sig').strip(),lines[i]+lines[i+1]) for i in range(0,len(lines),2)]
    rows=[];section=None
    for i,(k,v,raw) in enumerate(pairs):
        if (k,v)==(0,'SECTION'):section=pairs[i+1][1]
        if k==0:rows.append({'start':i,'end':i+1,'section':section,'pairs':[]})
        if rows:rows[-1]['pairs'].append((k,v));rows[-1]['end']=i+1
        if (k,v)==(0,'ENDSEC'):section=None
    return pairs,[r for r in rows if r['section']=='ENTITIES' and r['pairs'][0][1] not in ('SECTION','ENDSEC')]

def one(r,k,default=None):
    values=[v for c,v in r['pairs'] if c==k]
    if len(values)>1:raise ValueError('Ambiguous scalar code '+str(k))
    return values[0] if values else default

def kind(r):return r['pairs'][0][1]
def rawrow(pairs,r):return b''.join(p[2] for p in pairs[r['start']:r['end']])
def values(r,k):return [v for c,v in r['pairs'] if c==k]

def spline(r):
    flags=int(one(r,70));degree=int(one(r,71))
    xs,ys=values(r,10),values(r,20)
    if len(xs)!=len(ys) or len(xs)!=int(one(r,73)):raise ValueError('Invalid control count')
    weights=values(r,41) or ['1']*len(xs)
    knots=values(r,40)
    if len(knots)!=int(one(r,72)):raise ValueError('Knot count mismatch')
    import spline_arc_core
    return [(s['domain'][0],s['domain'][1],s['h']) for s in spline_arc_core.spans(degree,knots,list(zip(xs,ys)),weights,bool(flags&1),bool(flags&2))]

def primitive(r):
    if kind(r)=='LINE':return dict(type='LINE',x0=one(r,10),y0=one(r,20),x1=one(r,11),y1=one(r,21))
    if kind(r)=='ARC':return dict(type='ARC',cx=one(r,10),cy=one(r,20),r=one(r,40),start=one(r,50),end=one(r,51))
    if kind(r)=='CIRCLE':return dict(type='CIRCLE',cx=one(r,10),cy=one(r,20),r=one(r,40))
    raise ValueError('Unsupported primitive '+kind(r))

def conics(g):
    if g['type']=='LINE':return [cc.primitive_h(g)]
    if g['type']=='CIRCLE':return [cc.primitive_h(dict(g,type='ARC',start=str(a),end=str(a+90))) for a in range(0,360,90)]
    a=F(g['start']);s=(F(g['end'])-a)%360
    n=math.ceil(float(s)/90)
    return [cc.primitive_h(dict(g,start=str(a+s*i/n),end=str(a+s*(i+1)/n))) for i in range(n)]

def endpoints(r):
    if kind(r)=='SPLINE':
        spans=spline(r);return [tuple(cc.I(x) for x in cc.point(spans[0][2])),tuple(cc.I(x) for x in cc.point(spans[-1][2],F(1)))]
    hs=conics(primitive(r));return [tuple(h[k]/h[2] for k in (0,1)) for h in (hs[0][0],hs[-1][-1])]

def distbound(a,b):
    vs=[(x-y).absmax for x,y in zip(a,b)]
    return cc.rnd(lambda:sum(vs,D(0)),True)

def graph(rows,tol=D('.000001')):
    nodes=[];edges=[];maxgap=D(0);buckets=collections.defaultdict(list)
    for r in rows:
        ends=[]
        for p in endpoints(r):
            cell=tuple(int(((F(v.lo)+F(v.hi))/2)//F(tol)) for v in p)
            nearby=[i for x in (-1,0,1) for y in (-1,0,1) for i in buckets[(cell[0]+x,cell[1]+y)]]
            hits=[i for i in nearby if distbound(p,nodes[i])<=tol]
            if len(hits)>1:raise ValueError('Ambiguous endpoint clustering')
            if hits:
                i=hits[0];maxgap=max(maxgap,distbound(p,nodes[i]))
            else:i=len(nodes);nodes.append(p);buckets[cell].append(i)
            ends.append(i)
        edges.append(ends)
    adj=collections.defaultdict(list)
    for i,(a,b) in enumerate(edges):adj[a].append(i);adj[b].append(i)
    bad={str(n):len(e) for n,e in adj.items() if len(e)!=2}
    todo=set(range(len(rows)));components=[]
    while todo:
        stack=[todo.pop()];comp=[]
        while stack:
            i=stack.pop();comp.append(i)
            for n in edges[i]:
                for j in adj[n]:
                    if j in todo:todo.remove(j);stack.append(j)
        components.append(comp)
    return dict(closedDegreeTwo=not bad,invalidNodeDegrees=bad,componentCount=len(components),components=components,maxJoinBoundMm=str(maxgap)),nodes,edges

def box(h):
    ps=[(x/w,y/w) for x,y,w in h]
    return (min(p[0].lo for p in ps),max(p[0].hi for p in ps),min(p[1].lo for p in ps),max(p[1].hi for p in ps))

def disjoint(a,b):return a[1]<b[0] or b[1]<a[0] or a[3]<b[2] or b[3]<a[2]

def separation(h,j,shared=None,budget=20000):
    """Convex-hull exclusion on complete positive-weight rational curves.
    Adjacent contacts allowed only in the declared 1e-6 mm join square.
    """
    stack=[(h,j,0)];count=0
    while stack:
        a,b,depth=stack.pop();count+=1
        if count>budget:return False,'Subdivision budget',count
        x,y=box(a),box(b)
        if disjoint(x,y):continue
        if shared is not None:
            px,py=shared;e=D('.000001')
            xl=cc.rnd(lambda:px.hi-e,True);xr=cc.rnd(lambda:px.lo+e,False)
            yl=cc.rnd(lambda:py.hi-e,True);yr=cc.rnd(lambda:py.lo+e,False)
            def tiny(q):return q[0]>=xl and q[1]<=xr and q[2]>=yl and q[3]<=yr
            if tiny(x) and tiny(y):continue
        if depth>=90:return False,'Unresolved contact/overlap',count
        size=lambda q:max(q[1]-q[0],q[3]-q[2])
        if size(x)>=size(y):
            l,r=cc.split(a);stack.extend([(l,b,depth+1),(r,b,depth+1)])
        else:
            l,r=cc.split(b);stack.extend([(a,l,depth+1),(a,r,depth+1)])
    return True,None,count

def intersection_check(rows,nodes,edges):
    hs=[conics(primitive(r)) for r in rows];unresolved=[];tested=0;boxes=0
    for i in range(len(rows)):
        for j in range(i+1,len(rows)):
            shared=set(edges[i])&set(edges[j]);p=nodes[next(iter(shared))] if len(shared)==1 else None
            tested+=1
            for a in hs[i]:
                for b in hs[j]:
                    ok,why,n=separation(a,b,p);boxes+=n
                    if not ok:unresolved.append(dict(first=one(rows[i],5),second=one(rows[j],5),reason=why))
    return dict(allPairsExcludedOutsideJoinTolerance=not unresolved,pairsTested=tested,subdivisionBoxes=boxes,unresolved=unresolved,joinToleranceMm='0.000001',exactMathematicalClosureCertified=False)

def encode(tags):return b''.join((str(k)+'\r\n'+str(v)+'\r\n').encode('utf-8') for k,v in tags)

def replacement(pairs,row,pieces,next_handle):
    common=[]
    for k,v in row['pairs'][1:]:
        if (k,v)==(100,'AcDbSpline'):break
        if k in (102,360) or k>=1000:raise ValueError('SPLINE reactor/extension data unsupported')
        common.append((k,v))
    blobs=[]
    for i,p in enumerate(pieces):
        g=p['primitive'];handle=one(row,5) if i==0 else format(next_handle,'X')
        if i:next_handle+=1
        p['outputHandle']=handle
        tags=[(0,g['type'])]+[(k,handle if k==5 else v) for k,v in common]
        if g['type']=='LINE':tags += [(100,'AcDbLine'),(10,g['x0']),(20,g['y0']),(30,0),(11,g['x1']),(21,g['y1']),(31,0)]
        else:tags += [(100,'AcDbCircle'),(10,g['cx']),(20,g['cy']),(30,0),(40,g['r']),(210,0),(220,0),(230,1),(100,'AcDbArc'),(50,g['start']),(51,g['end'])]
        blobs.append(encode(tags))
    return b''.join(blobs),next_handle

