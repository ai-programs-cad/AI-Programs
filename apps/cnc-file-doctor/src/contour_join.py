"""Closed analytic contours -> closed bulged LWPOLYLINE, with read-back proof."""
import math,time
from pathlib import Path
from decimal import Decimal as D
from fractions import Fraction as F
import dxf_support as dx
import certified_curves as c
import spline_arc_core as core
import topology_certification as topo
import junction_checks as junction

STYLE={330,67,410,8,6,62,420,430,48,370,440,60}

def poly_curves(row):
    if dx.kind(row)!='LWPOLYLINE':return dx.conics(dx.primitive(row))
    if int(dx.one(row,70,'0'))!=1:raise ValueError('Joined polyline is not closed')
    points=[]
    for k,v in row['pairs']:
        if k==10:points.append([F(v),None,F(0)])
        elif k==20:points[-1][1]=F(v)
        elif k==42:points[-1][2]=F(v)
    if len(points)!=int(dx.one(row,90,'0')) or len(points)<2:raise ValueError('Invalid joined vertex count')
    result=[]
    for a,b in zip(points,points[1:]+points[:1]):
        x,y,bulge=a;u,v,_=b
        if (x,y)==(u,v):raise ValueError('Collapsed joined segment')
        if not bulge:h=[(x,y,F(1)),(u,v,F(1))]
        else:
            if abs(bulge)>=1:raise ValueError('Joined sweep must be smaller than 180 degrees')
            w=(1-bulge*bulge)/(1+bulge*bulge);q=bulge/(1+bulge*bulge)
            h=[(x,y,F(1)),(w*(x+u)/2+q*(v-y),w*(y+v)/2-q*(u-x),w),(u,v,F(1))]
        result.append(core.as_intervals(h))
    return result

def join(source,target,cut_layers=('0',),remaining_tolerance=D('.000001')):
    source,target=Path(source),Path(target)
    if target.exists():raise FileExistsError(target)
    pairs,rows=dx.read(source)
    for i,(k,v,raw) in enumerate(pairs[:-1]):
        if (k,v)==(9,'$ACADVER') and pairs[i+1][1]<'AC1014':raise ValueError('DXF version predates LWPOLYLINE; automatic header upgrade refused')
    cut=[r for r in rows if dx.one(r,8,'0') in cut_layers and dx.one(r,8,'0').upper() not in ('UP','DOWN')]
    if not cut or any(dx.kind(r) not in ('LINE','ARC','CIRCLE') for r in cut):raise ValueError('Joining requires analytic CUT contours')
    graph,nodes,edges=dx.graph(cut,D('.001'))
    if not graph['closedDegreeTwo']:raise ValueError('Open or branched contour; joining refused')
    limit=min(D('.001'),D(remaining_tolerance))
    if limit<=0:raise ValueError('No deviation budget remains for serialized joining')
    replacements={};records=[];proofs={};preserved=[]
    adjacency={}
    for i,(a,b) in enumerate(edges):
        adjacency.setdefault(a,[]).append(i);adjacency.setdefault(b,[]).append(i)
    for component in graph['components']:
        if len(component)==1 and dx.kind(cut[component[0]])=='CIRCLE':
            preserved.append(dx.one(cut[component[0]],5));continue
        subset=set(component);used=set();node=edges[component[0]][0];start=node;ordered=[]
        while len(used)<len(component):
            candidates=[i for i in adjacency[node] if i in subset and i not in used]
            if not candidates:raise ValueError('Disconnected contour during joining')
            i=candidates[0];a,b=edges[i];reverse=node!=a;used.add(i);ordered.append((cut[i],reverse));node=a if reverse else b
        if node!=start:raise ValueError('Contour did not close')
        styles=[[(k,v) for k,v in r['pairs'] if k in STYLE] for r,rev in ordered]
        if any(dict(style)!=dict(styles[0]) for style in styles):raise ValueError('Contour has different entity styles; one polyline cannot preserve them')
        segment_sources=[];vertices=[]
        for row,reverse in ordered:
            allowed={0,5,100,10,20,30,11,21,31,40,50,51,210,220,230}|STYLE
            if any(k not in allowed for k,v in row['pairs']):raise ValueError('Referenced or extended entity metadata prevents joining')
            if any(D(v)!=0 for k,v in row['pairs'] if k in (30,31)):raise ValueError('Nonplanar contour')
            if [D(dx.one(row,k,v)) for k,v in ((210,'0'),(220,'0'),(230,'1'))]!=[0,0,1]:raise ValueError('Unsupported extrusion')
            g=dx.primitive(row);hs=dx.conics(g)
            if reverse:hs=[list(reversed(h)) for h in hs[::-1]]
            sweep=0 if g['type']=='LINE' else float((F(g['end'])-F(g['start']))%360)/len(hs)*(-1 if reverse else 1)
            bulge='0' if not sweep else format(math.tan(math.radians(sweep)/4),'.17g')
            for h in hs:
                p=h[0];xy=[format(float(((p[k]/p[2]).lo+(p[k]/p[2]).hi)/2),'.17g') for k in (0,1)]
                vertices.append((xy[0],xy[1],bulge));segment_sources.append(h)
        first=ordered[0][0];handle=dx.one(first,5);consumed={dx.one(r,5) for r,rev in ordered}
        for k,v,raw in pairs:
            if 330<=k<=369 and v in consumed:raise ValueError('External reference to a contour entity prevents joining')
        tags=[(0,'LWPOLYLINE'),(5,handle)]+[(k,v) for k,v in styles[0] if k==330]+[(100,'AcDbEntity')]+[(k,v) for k,v in styles[0] if k!=330]+[(100,'AcDbPolyline'),(90,len(vertices)),(70,1)]
        for x,y,bulge in vertices:tags += [(10,x),(20,y),(42,bulge)]
        replacements[first['start']]=(first['end'],dx.encode(tags))
        for row,rev in ordered[1:]:replacements[row['start']]=(row['end'],b'')
        proofs[handle]=segment_sources
        records.append(dict(outputHandle=handle,sourceHandles=sorted(consumed),vertices=len(vertices),closed=True,arcSegments=sum(v[2]!='0' for v in vertices)))
    chunks=[];i=0
    while i<len(pairs):
        if i in replacements:end,blob=replacements[i];chunks.append(blob);i=end
        else:chunks.append(pairs[i][2]);i+=1
    target.write_bytes(b''.join(chunks))
    try:
        op,output=dx.read(target);byhandle={dx.one(r,5):r for r in output};maximum=D(0)
        budget=core.Budget(core.Options(tolerance_mm=limit,endpoint_tolerance_mm=min(limit,D('.0000001')),max_seconds=30))
        for handle,originals in proofs.items():
            reread=poly_curves(byhandle[handle])
            if len(reread)!=len(originals):raise ValueError('Joined segment count changed')
            for old,new in zip(originals,reread):
                bound=core.certify_pair(old,new,limit,budget)
                if bound is None:raise ValueError('Joined geometry exceeds remaining tolerance')
                maximum=max(maximum,bound)
            def tangent(h,end):
                a,b=(h[-2],h[-1]) if end else (h[0],h[1])
                return tuple(b[k]*a[2]-a[k]*b[2] for k in (0,1))
            for i in range(len(originals)):
                j=(i+1)%len(originals)
                oa,ob=tangent(originals[i],True),tangent(originals[j],False)
                na,nb=tangent(reread[i],True),tangent(reread[j],False)
                angle=junction.small_angle_bound(oa,ob)
                if angle is not None and angle<=D('.00001'):
                    newangle=junction.small_angle_bound(na,nb)
                    if newangle is None or newangle>D('.00001'):raise ValueError('Joined smooth tangent check failed')
                else:
                    ca,cb=junction.small_angle_bound(oa,na),junction.small_angle_bound(ob,nb)
                    if ca is None or cb is None or ca+cb>D('.05'):raise ValueError('Joined original corner preservation failed')
        touched={h for r in records for h in r['sourceHandles']}
        for row in rows:
            h=dx.one(row,5)
            if h not in touched and dx.rawrow(pairs,row)!=dx.rawrow(op,byhandle[h]):raise ValueError('Unrequested entity change')
        groups=[poly_curves(r) for r in output if dx.one(r,8,'0') in cut_layers and dx.one(r,8,'0').upper() not in ('UP','DOWN')]
        exclusion=topo.all_pair_exclusions([h for group in groups for h in group],seconds=30)
        if not exclusion['verified']:raise ValueError('Joined contour intersection check not verified')
        hierarchy=topo.hierarchy(groups,time.monotonic()+15)
        original_groups=[[h for i in comp for h in dx.conics(dx.primitive(cut[i]))] for comp in graph['components']]
        original_hierarchy=topo.hierarchy(original_groups,time.monotonic()+15)
        original_component={dx.one(cut[i],5):n for n,comp in enumerate(graph['components']) for i in comp}
        outcut=[r for r in output if dx.one(r,8,'0') in cut_layers and dx.one(r,8,'0').upper() not in ('UP','DOWN')]
        correspondence=[original_component[dx.one(r,5)] for r in outcut]
        if len(correspondence)!=len(graph['components']) or any(hierarchy['containment'][i][j]!=original_hierarchy['containment'][correspondence[i]][correspondence[j]] for i in range(len(groups)) for j in range(len(groups))):raise ValueError('Joined contour nesting changed')
        return dict(verified=True,path=str(target),closedPolylineCount=len(records),preservedCircleCount=len(preserved),records=records,maximumAdditionalDeviationMm=str(maximum),intersectionCheck=exclusion,hierarchy=hierarchy,nestingPreserved=True,sourceSha256=dx.sha(source),outputSha256=dx.sha(target),method='Closed LWPOLYLINE with signed bulges; whole-curve read-back interval certificate; no closing connector added')
    except Exception:
        target.unlink(missing_ok=True);raise
