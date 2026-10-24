"""Whole-curve topology checks at explicit DXF endpoint joining tolerance.
Nesting uses Bernstein root isolation and integer winding, not polygon sampling.
No geometry is joined, healed, deduplicated or moved by this module.
"""
import collections, math, time
from fractions import Fraction as F
from decimal import Decimal as D
import certified_curves as c
import spline_arc_core as core
import dxf_support as dx

class Unresolved(ValueError):pass

def product_f(a,b):
    n,m=len(a)-1,len(b)-1
    return [sum((F(math.comb(n,i)*math.comb(m,k-i),math.comb(n+m,k))*a[i]*b[k-i] for i in range(max(0,k-m),min(n,k)+1)),F(0)) for k in range(n+m+1)]

def monotone_projection(h):
    a,b=c.point(h),c.point(h,F(1));d=(b[0]-a[0],b[1]-a[1])
    if d==(0,0):return False
    n=len(h)-1;w=[p[2] for p in h];x=[p[0]*d[0]+p[1]*d[1] for p in h]
    dw=[n*(b-a) for a,b in zip(w,w[1:])];xx=[n*(b-a) for a,b in zip(x,x[1:])]
    coefficients=[a-b for a,b in zip(product_f(xx,w),product_f(x,dw))]
    return min(coefficients)>=0 and max(coefficients)>0

def injective_parts(h,depth=0,budget=None):
    if budget is not None:
        budget['nodes']+=1
        if budget['nodes']>20000 or time.monotonic()>budget['deadline']:raise Unresolved('Source injectivity budget reached')
    if monotone_projection(h):return [h]
    if depth>=18:raise Unresolved('Injectivity of a source Bezier span not certified')
    l,r=c.split(h)
    return injective_parts(l,depth+1,budget)+injective_parts(r,depth+1,budget)

def row_curves(row,prove_injective=False,budget=None):
    if dx.kind(row)=='SPLINE':
        parts=[h for a,b,h in dx.spline(row)]
        return [core.as_intervals(p) for h in parts for p in (injective_parts(h,budget=budget) if prove_injective else [h])]
    g=dx.primitive(row)
    if g['type']=='LINE' and (F(g['x0']),F(g['y0']))==(F(g['x1']),F(g['y1'])):raise Unresolved('Zero-length analytical LINE')
    if g['type'] in ('ARC','CIRCLE') and F(g['r'])<=0:raise Unresolved('Nonpositive analytical radius')
    if g['type']=='ARC' and (F(g['end'])-F(g['start']))%360==0:raise Unresolved('Degenerate analytical ARC')
    return dx.conics(g)

def graph_h(curves,tol=D('.000001')):
    nodes=[];edges=[];buckets=collections.defaultdict(list);degrees=collections.Counter();gap=D(0)
    for h in curves:
        edge=[]
        for p in (h[0],h[-1]):
            xy=(p[0]/p[2],p[1]/p[2]);cell=tuple(int(((F(v.lo)+F(v.hi))/2)//F(tol)) for v in xy)
            hits=[i for x in (-1,0,1) for y in (-1,0,1) for i in buckets[(cell[0]+x,cell[1]+y)] if dx.distbound(xy,nodes[i])<=tol]
            if len(hits)>1:raise Unresolved('Ambiguous source endpoint clustering')
            if hits:i=hits[0];gap=max(gap,dx.distbound(xy,nodes[i]))
            else:i=len(nodes);nodes.append(xy);buckets[cell].append(i)
            edge.append(i);degrees[i]+=1
        edges.append(edge)
    return nodes,edges,all(d==2 for d in degrees.values()),gap

def all_pair_exclusions(curves,seconds=60,max_pairs=100000):
    from curve_pair_candidates import curve_box,candidate_pairs
    start=time.monotonic();nodes,edges,closed,gap=graph_h(curves);tested=0;unresolved=[];boxes=0
    bounds=[curve_box(h) for h in curves]
    for i,j in candidate_pairs(bounds):
        if tested>=max_pairs or time.monotonic()-start>seconds:
            return dict(verified=False,completed=False,reason='Topology candidate pair/time budget',pairsTested=tested,unresolved=unresolved)
        shared=set(edges[i])&set(edges[j]);p=nodes[next(iter(shared))] if len(shared)==1 else None
        ok,why,n=dx.separation(curves[i],curves[j],p,budget=3000);tested+=1;boxes+=n
        if not ok:unresolved.append(dict(first=i,second=j,reason=why))
    if time.monotonic()-start>seconds:return dict(verified=False,completed=False,reason='Topology candidate pair/time budget',pairsTested=tested,unresolved=unresolved)
    total=len(curves)*(len(curves)-1)//2
    return dict(verified=closed and not unresolved,completed=True,closedAtJoinTolerance=closed,maxEndpointJoinBoundMm=str(gap),pairsTested=tested,totalPossiblePairs=total,convexHullBoxExcludedPairs=total-tested,broadPhase='Conservative positive-weight interval convex-hull boxes; touching boxes retained',subdivisionBoxes=boxes,unresolved=unresolved,joinToleranceMm='0.000001',strictZeroGapCertified=False)

def sign(v):
    if v.lo>0:return 1
    if v.hi<0:return -1
    if v.lo==v.hi==0:return 0
    return None

def crossings(h,point,ray,budget,depth=0):
    budget[0]+=1
    if budget[0]>50000 or depth>60 or (len(budget)>1 and time.monotonic()>budget[1]):raise Unresolved('Ray root isolation budget')
    px,py=point;rx,ry=ray
    normal=[rx*(p[1]-py*p[2])-ry*(p[0]-px*p[2]) for p in h]
    if all(v.lo>0 for v in normal) or all(v.hi<0 for v in normal):return 0
    signs=[sign(v) for v in normal]
    if signs[0] in (0,None) or signs[-1] in (0,None):raise Unresolved('Ray through a vertex or unresolved boundary')
    if None not in signs:
        nz=[v for v in signs if v]
        changes=sum(a!=b for a,b in zip(nz,nz[1:]))
        if changes==0:return 0
        if changes==1:
            forward=[rx*(p[0]-px*p[2])+ry*(p[1]-py*p[2]) for p in h]
            if all(v.hi<0 for v in forward):return 0
            if all(v.lo>0 for v in forward):return 1 if signs[0]<signs[-1] else -1
    l,r=c.split(h)
    return crossings(l,point,ray,budget,depth+1)+crossings(r,point,ray,budget,depth+1)

def winding(curves,point,deadline=None):
    # Several exact ray directions avoid vertex/tangent degeneracies. A failed
    # direction never counts as zero; all failures leave the check unresolved.
    _,edges,closed,_=graph_h(curves)
    if not closed:raise Unresolved('Winding requires a closed contour at join tolerance')
    for slope in (F(0),F(1,997),F(7,991),F(-13,983),F(37,971),F(101,953)):
        budget=[0,deadline] if deadline is not None else [0]
        try:
            endpoints={}
            for h,edge in zip(curves,edges):
                for p,node in zip((h[0],h[-1]),edge):
                    normal=p[1]-point[1]*p[2]-slope*(p[0]-point[0]*p[2]);s=sign(normal)
                    if s in (0,None):raise Unresolved('Winding ray touches endpoint join zone')
                    if node in endpoints and endpoints[node]!=s:raise Unresolved('Winding ray crosses endpoint gap; choose a nondegenerate ray')
                    endpoints[node]=s
            n=sum(crossings(h,point,(F(1),slope),budget) for h in curves)
            if abs(n)>1:raise Unresolved('Winding > 1: source not a certified simple contour')
            return n,dict(ray=[str(F(1)),str(slope)],rootIsolationNodes=budget[0],winding=n,endpointJoinZonesDoNotCrossRay=True)
        except Unresolved:continue
    raise Unresolved('No nondegenerate certified winding ray')

def hierarchy(component_curves,deadline=None):
    n=len(component_curves);matrix=[[False]*n for _ in range(n)];witness=[]
    for i,curves in enumerate(component_curves):
        h=curves[0];p=(h[0][0]/h[0][2],h[0][1]/h[0][2])
        for j,other in enumerate(component_curves):
            if i==j:continue
            if deadline is not None and time.monotonic()>deadline:raise Unresolved('Containment matrix budget reached')
            w,evidence=winding(other,p,deadline);matrix[i][j]=w!=0
            witness.append(dict(component=i,relativeTo=j,pointIntervals=[[str(v.lo),str(v.hi)] for v in p],**evidence))
    if any(matrix[i][j] and matrix[j][i] for i in range(n) for j in range(n)):raise Unresolved('Cyclic containment')
    depth=[sum(row) for row in matrix]
    parents=[]
    for i in range(n):
        containers=[j for j in range(n) if matrix[i][j]]
        parent=max(containers,key=lambda j:depth[j]) if containers else None
        if parent is not None and depth[parent]!=depth[i]-1:raise Unresolved('Inconsistent nesting hierarchy')
        parents.append(parent)
    return dict(verified=True,containment=matrix,depths=depth,parents=parents,holeCount=sum(d%2==1 for d in depth),outerOrIslandCount=sum(d%2==0 for d in depth),witnesses=witness,method='Whole rational curve Bernstein sign-variation root isolation and integer winding; no polyline ray approximation')

def duplicate_signature(row):
    k=dx.kind(row)
    if k=='LINE':return (k,tuple(sorted((tuple(map(F,(dx.one(row,10),dx.one(row,20)))),tuple(map(F,(dx.one(row,11),dx.one(row,21))))))))
    if k in ('ARC','CIRCLE'):
        key=(k,F(dx.one(row,10)),F(dx.one(row,20)),F(dx.one(row,40)))
        return key+(F(dx.one(row,50))%360,F(dx.one(row,51))%360) if k=='ARC' else key
    return None

def duplicates(rows):
    seen={};found=[]
    for row in rows:
        key=duplicate_signature(row)
        if key is None:continue
        if key in seen:found.append([seen[key],dx.one(row,5)])
        else:seen[key]=dx.one(row,5)
    return found

def compare(source,output,parent,geometric_bound,seconds=60,geometry_correspondence_verified=False):
    result=dict(status='NEEDS_REVIEW',strictZeroGapCertified=False,joinToleranceMm='0.000001')
    try:
        deadline=time.monotonic()+seconds;budget={'nodes':0,'deadline':deadline}
        sg,_,_=dx.graph(source);og,_,_=dx.graph(output)
        if not sg['closedDegreeTwo'] or not og['closedDegreeTwo']:raise Unresolved('Open/branched contour: nesting not certified')
        result['sourceDuplicateAnalyticEntities']=duplicates(source);result['outputDuplicateAnalyticEntities']=duplicates(output)
        sc=[[h for i in comp for h in row_curves(source[i],True,budget)] for comp in sg['components']]
        oc=[[h for i in comp for h in row_curves(output[i],True,budget)] for comp in og['components']]
        result['sourceSelfIntersectionAndOverlapCheck']=all_pair_exclusions([h for curves in sc for h in curves],max(0,deadline-time.monotonic()))
        result['outputSelfIntersectionAndOverlapCheck']=all_pair_exclusions([h for curves in oc for h in curves],max(0,deadline-time.monotonic()))
        sh=hierarchy(sc,deadline);oh=hierarchy(oc,deadline);result['sourceHierarchy']=sh;result['outputHierarchy']=oh
        source_keys=[frozenset(dx.one(source[i],5) for i in comp) for comp in sg['components']]
        output_keys=[frozenset(parent[dx.one(output[i],5)] for i in comp) for comp in og['components']]
        correspondence=[source_keys.index(key) for key in output_keys]
        nesting=all(oh['containment'][i][j]==sh['containment'][correspondence[i]][correspondence[j]] for i in range(len(oc)) for j in range(len(oc)))
        result.update(componentCorrespondence=correspondence,closedContoursPreserved=sg['closedDegreeTwo'] and og['closedDegreeTwo'],holeCountPreserved=sh['holeCount']==oh['holeCount'],nestingPreserved=nesting,relativePlacementPreservedWithinGeometricBound=nesting and geometry_correspondence_verified,geometricBoundMm=str(geometric_bound),geometryCorrespondenceVerifiedByCaller=geometry_correspondence_verified,placementEvidence='Requires certified whole-curve reference correspondence and unchanged native analytical entities from batch checker; nesting alone does not bound displacement')
        verified=all(result[k] for k in ('closedContoursPreserved','holeCountPreserved','nestingPreserved')) and all(result[k]['verified'] for k in ('sourceSelfIntersectionAndOverlapCheck','outputSelfIntersectionAndOverlapCheck')) and not result['sourceDuplicateAnalyticEntities'] and not result['outputDuplicateAnalyticEntities']
        result['verifiedAtJoinTolerance']=verified
        result['status']='TOPOLOGY_VERIFIED_AT_JOIN_TOLERANCE' if verified else 'NEEDS_REVIEW'
    except (ValueError,ArithmeticError,KeyError) as e:result['error']=str(e);result['verifiedAtJoinTolerance']=False
    return result
