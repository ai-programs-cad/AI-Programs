"""G1 biarc candidate generator. Acceptance always uses complete interval curves.
Floating point constructions/closest-parameter search are candidates, not proofs.
"""
import math
from fractions import Fraction as F
from decimal import Decimal as D
import certified_curves as c
import spline_arc_core as core

def unit(v):
    length=math.hypot(*v)
    if length==0:return None
    return (v[0]/length,v[1]/length)

def arc_from_tangent(a,t,b):
    ax,ay=map(float,a);bx,by=map(float,b);v=(bx-ax,by-ay);n=(-t[1],t[0]);den=2*(v[0]*n[0]+v[1]*n[1])
    if abs(den)<1e-14*max(math.hypot(*v),1e-100):
        return dict(type='LINE',x0=c.number(a[0]),y0=c.number(a[1]),x1=c.number(b[0]),y1=c.number(b[1])),False
    r=(v[0]*v[0]+v[1]*v[1])/den;cx=ax+r*n[0];cy=ay+r*n[1]
    aa=math.degrees(math.atan2(ay-cy,ax-cx))%360;bb=math.degrees(math.atan2(by-cy,bx-cx))%360
    reverse=r<0;start,end=(bb,aa) if reverse else (aa,bb)
    if not 1e-9<(end-start)%360<170 or not math.isfinite(r):return None
    return dict(type='ARC',cx=c.number(cx),cy=c.number(cy),r=c.number(abs(r)),start=c.number(start),end=c.number(end)),reverse

def cut_segments(segments,u):
    left=[];right=[]
    for s in segments:
        a,b=s['domain']
        if b<=u:left.append(s)
        elif a>=u:right.append(s)
        else:
            h1,h2=core.split_at(s['h'],(u-a)/(b-a));left.append(dict(h=h1,domain=(a,u)));right.append(dict(h=h2,domain=(u,b)))
    return left,right

def candidates(segments):
    start,end=segments[0]['domain'][0],segments[-1]['domain'][1]
    a,b=core.global_point(segments,start),core.global_point(segments,end)
    t0=unit(core.endpoint_tangent(segments[0]['h']));t1=unit(core.endpoint_tangent(segments[-1]['h'],True))
    if t0 is None or t1 is None:return None
    v=(float(b[0]-a[0]),float(b[1]-a[1]));vv=v[0]*v[0]+v[1]*v[1]
    if vv==0:return None
    dot=t0[0]*t1[0]+t0[1]*t1[1];vt=v[0]*(t0[0]+t1[0])+v[1]*(t0[1]+t1[1]);den=2*(1-dot)
    if abs(den)<1e-12:
        if vt<=0:return None
        d=vv/(2*vt)
    else:
        disc=vt*vt+den*vv
        if disc<=0:return None
        # Rationalized positive root avoids cancellation for almost equal tangents.
        d=vv/(math.sqrt(disc)+vt) if math.sqrt(disc)+vt>1e-30 else (-vt+math.sqrt(disc))/den
    m=(F(c.number((float(a[0])+float(b[0])+d*(t0[0]-t1[0]))/2)),F(c.number((float(a[1])+float(b[1])+d*(t0[1]-t1[1]))/2)))
    first=arc_from_tangent(a,t0,m);second=arc_from_tangent(b,(-t1[0],-t1[1]),m)
    if first is None or second is None:return None
    second=(second[0],not second[1]) if second[0]['type']=='ARC' else (dict(type='LINE',x0=second[0]['x1'],y0=second[0]['y1'],x1=second[0]['x0'],y1=second[0]['y0']),False)
    # Find a correspondence split. Its quality is subsequently certified over
    # the whole curve; this minimizer is not a Hausdorff test.
    def distance(f):
        u=start+(end-start)*F(str(f));p=core.global_point(segments,u)
        return float((p[0]-m[0])**2+(p[1]-m[1])**2)
    lo,hi=.02,.98
    for _ in range(36):
        x=lo+(hi-lo)/3;y=hi-(hi-lo)/3
        if distance(x)<distance(y):hi=y
        else:lo=x
    u=start+(end-start)*F(str((lo+hi)/2));left,right=cut_segments(segments,u)
    return [(first,left),(second,right)]

def attempt(segments,budget):
    def rejected(reason):
        if not hasattr(budget,'rejections'):budget.rejections={}
        budget.rejections[reason]=budget.rejections.get(reason,0)+1
        return None
    proposed=candidates(segments)
    if proposed is None:return rejected('constructionOrSweepRejected')
    out=[]
    for (g,rev),source in proposed:
        budget.check(candidate=True);target=c.primitive_h(g,rev)
        a,b=source[0]['domain'][0],source[-1]['domain'][1];bounds=[]
        for seg in source:
            l,r=seg['domain'];th=core.restrict(target,(l-a)/(b-a),(r-a)/(b-a))
            bound=core.certify_pair(seg['h'],th,budget.o.tolerance_mm,budget)
            if bound is None:return rejected('wholeCurveDeviationNotCertified')
            bounds.append(bound)
        e=core.endpoint_bound([source[0]['h'][0],source[-1]['h'][-1]],target)
        tang=max(c.angle_error(core.endpoint_tangent(source[0]['h']),c.primitive_tangent(g,False,rev)),c.angle_error(core.endpoint_tangent(source[-1]['h'],True),c.primitive_tangent(g,True,rev)))
        out.append(dict(primitive=g,reverse=rev,segments=source,bound=max(bounds),endpointBound=e,tangentErrorDegreesDiagnostic=tang,certificateKind='PAIRED_BERNSTEIN_BIARC'))
    join=c.angle_error(c.primitive_tangent(out[0]['primitive'],True,out[0]['reverse']),c.primitive_tangent(out[1]['primitive'],False,out[1]['reverse']))
    if join>1e-5:return rejected('jointTangencyRejected')
    # Only original boundaries must interpolate source endpoints. Artificial
    # internal correspondence points are allowed their certified tolerance.
    first=c.primitive_h(out[0]['primitive'],out[0]['reverse'])[0];last=c.primitive_h(out[-1]['primitive'],out[-1]['reverse'])[-1]
    outer=core.endpoint_bound([segments[0]['h'][0],segments[-1]['h'][-1]],[first,last])
    if outer>budget.o.endpoint_tolerance_mm:return rejected('originalEndpointsRejected')
    ends=max(c.angle_error(core.endpoint_tangent(segments[0]['h']),c.primitive_tangent(out[0]['primitive'],False,out[0]['reverse'])),c.angle_error(core.endpoint_tangent(segments[-1]['h'],True),c.primitive_tangent(out[-1]['primitive'],True,out[-1]['reverse'])))
    if ends>budget.o.tangent_degrees:return rejected('originalEndpointTangentsRejected')
    return out

def adaptive(segments,budget,depth=0):
    budget.check()
    one=core.attempt(segments,budget)
    if one is not None:return [one]
    two=attempt(segments,budget)
    if two is not None:return two
    # Cost-first bounded search: nonuniform subdivisions may need fewer arcs
    # than repeated midpoint splitting. Every proposed piece is still proved
    # over its entire rational source domain before it can enter the result.
    a,b=segments[0]['domain'][0],segments[-1]['domain'][1]
    alternatives=[]
    for fraction in (F(3,8),F(5,8),F(1,4),F(3,4),F(1,2)):
        left,right=cut_segments(segments,a+(b-a)*fraction)
        proposed=[]
        for group in (left,right):
            single=core.attempt(group,budget)
            if single is not None:proposed.append([single])
            else:
                pair=attempt(group,budget)
                if pair is None:break
                proposed.append(pair)
        if len(proposed)==2:
            merged=proposed[0]+proposed[1]
            if len(merged)<=budget.o.max_segments:
                alternatives.append(merged)
    if alternatives:
        return min(alternatives,key=lambda ps:(len(ps),max(p['bound'] for p in ps)))
    if depth>=min(budget.o.max_depth,16):raise core.ResourceLimit('Biarc depth limit')
    a,b=segments[0]['domain'][0],segments[-1]['domain'][1]
    l,r=cut_segments(segments,(a+b)/2)
    result=adaptive(l,budget,depth+1)+adaptive(r,budget,depth+1)
    if len(result)>budget.o.max_segments:raise core.ResourceLimit('Biarc segment limit')
    return result

def fit(source,options):
    budget=core.Budget(options);groups=[];current=[]
    for seg in source:
        if current:
            corner=c.angle_error(core.endpoint_tangent(current[-1]['h'],True),core.endpoint_tangent(seg['h']))
            if corner>1e-5:groups.append(current);current=[]
        current.append(seg)
    if current:groups.append(current)
    budget.sourceGroups=len(groups)
    result=[p for group in groups for p in adaptive(group,budget)]
    if len(result)>options.max_segments:raise core.ResourceLimit('Biarc segment limit')
    return result,budget
