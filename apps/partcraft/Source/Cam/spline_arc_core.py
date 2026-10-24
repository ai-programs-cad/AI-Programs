"""Model-independent rational NURBS -> circular ARC/LINE with interval bounds.
No COM or DXF dependencies. Millimetres. Positive weights, degrees 1..32.
Global minimum and exact G1 are NOT claimed. Unsupported data fail closed.
"""
import math, time
from dataclasses import dataclass
from decimal import Decimal as D
from fractions import Fraction as F
import certified_curves as c

class ReviewRequired(ValueError):pass
class ResourceLimit(ReviewRequired):pass

@dataclass
class Options:
    tolerance_mm: D = D('.01')
    endpoint_tolerance_mm: D = D('.0000001')
    tangent_degrees: float = .05
    max_depth: int = 24
    max_segments: int = 2048
    max_candidates: int = 12000
    max_seconds: float = 60.
    certificate_depth: int = 7
    merge: bool = True

    def validate(self):
        if not self.tolerance_mm.is_finite() or self.tolerance_mm<=0:raise ValueError('Positive finite tolerance required')
        if not 0<self.endpoint_tolerance_mm<=self.tolerance_mm:raise ValueError('Endpoint tolerance outside geometric tolerance')
        if not 0<=self.tangent_degrees<=5:raise ValueError('Tangent tolerance must be 0..5 degrees')
        if not 1<=self.max_depth<=32 or not 1<=self.max_segments<=20000 or not 1<=self.max_candidates<=1000000 or not 0<self.max_seconds<=3600 or not 0<=self.certificate_depth<=12:raise ValueError('Invalid resource limit')

class Budget:
    def __init__(self,options):self.o=options;self.start=time.monotonic();self.candidates=0;self.leaves=0;self.nodes=0
    def check(self,candidate=False):
        self.nodes+=1
        if candidate:self.candidates+=1
        if self.candidates>self.o.max_candidates:raise ResourceLimit('Candidate budget exceeded')
        if self.nodes>self.o.max_candidates*128:raise ResourceLimit('Certificate node budget exceeded')
        if time.monotonic()-self.start>self.o.max_seconds:raise ResourceLimit('Per-SPLINE time budget exceeded')

def spans(degree,knots,points,weights=None,closed=False,periodic=False,budget=None):
    """Extract exact rational Bezier spans on the actual DXF active domain.
    Unclamped/periodic data use their stored extended knot/control arrays;
    no guessed padding or automatic control-point duplication.
    """
    p=int(degree);u=list(map(F,knots));ps=[tuple(map(F,xy)) for xy in points]
    w=list(map(F,weights)) if weights is not None else [F(1)]*len(ps)
    if not 1<=p<=32 or len(ps)<=p or len(u)!=len(ps)+p+1 or len(w)!=len(ps):raise ReviewRequired('Invalid degree/knot/control/weight count')
    if len(u)>200000:raise ResourceLimit('Too many knots')
    if any(len(xy)!=2 for xy in ps):raise ReviewRequired('Planar XY controls required')
    if any(x<=0 for x in w):raise ReviewRequired('Nonpositive weights: positive denominator not certified')
    if any(a>b for a,b in zip(u,u[1:])):raise ReviewRequired('Nonmonotone knots')
    left,right=u[p],u[len(ps)]
    if left>=right:raise ReviewRequired('Empty active parameter domain')
    h=[(x*wi,y*wi,wi) for (x,y),wi in zip(ps,w)]
    for value in sorted({x for x in u if left<=x<=right}):
        if budget is not None:budget.check()
        s=u.count(value)
        if left<value<right and s>p:raise ReviewRequired('Discontinuous interior knot; disconnected SPLINE requires review')
        if s>p+1:raise ReviewRequired('Excessive knot multiplicity')
        while s<p:
            if budget is not None:budget.check()
            # At the active right endpoint an unclamped knot insertion can
            # have k=n+1. Use its actual knot span rather than clamping k=n.
            k=max(i for i in range(len(u)-1) if u[i]<=value<u[i+1])
            n=len(h)-1;q=[None]*(len(h)+1)
            for i in range(k-p+1):q[i]=h[i]
            for i in range(k-s,n+1):q[i+1]=h[i]
            for i in range(k-p+1,k-s+1):
                alpha=(value-u[i])/(u[i+p]-u[i]);q[i]=tuple(alpha*a+(1-alpha)*b for a,b in zip(h[i],h[i-1]))
            if any(v is None for v in q):raise ReviewRequired('Unresolved endpoint knot insertion')
            u.insert(k+1,value);h=q;s+=1
    out=[dict(domain=(u[k],u[k+1]),h=h[k-p:k+1]) for k in range(p,len(h)) if left<=u[k]<u[k+1]<=right]
    if not out:raise ReviewRequired('No nonempty active spans')
    gap=sum(abs(x-y) for x,y in zip(c.point(out[0]['h']),c.point(out[-1]['h'],F(1))))
    if (closed or periodic) and gap>F(1,10**7):raise ReviewRequired('Closed/periodic flag contradicts active-domain endpoints')
    return out

def split_at(h,t):
    if t==0:return [h[0]]*len(h),list(h)
    if t==1:return list(h),[h[-1]]*len(h)
    rows=[list(h)]
    while len(rows[-1])>1:
        r=rows[-1];rows.append([tuple((1-t)*x+t*y for x,y in zip(a,b)) for a,b in zip(r,r[1:])])
    return [r[0] for r in rows],[r[-1] for r in rows[::-1]]

def restrict(h,a,b):
    if not 0<=a<b<=1:raise ValueError('Invalid restriction')
    l,_=split_at(h,b)
    if a:_,l=split_at(l,a/b)
    return l

def as_intervals(h):return [tuple(c.I(x) for x in p) for p in h]

def difference_bound(s,t):
    s,t=as_intervals(s),as_intervals(t)
    sw,tw=[p[2] for p in s],[p[2] for p in t]
    denom=c.rnd(lambda:min(x.lo for x in sw)*min(x.lo for x in tw),False)
    if denom<=0:raise ReviewRequired('Positive denominator not certified')
    axis=[]
    for k in (0,1):
        lhs=c.product([p[k] for p in s],tw);rhs=c.product([p[k] for p in t],sw)
        axis.append(max((a-b).absmax for a,b in zip(lhs,rhs)))
    return c.rnd(lambda:sum(axis,D(0))/denom,True)

def endpoint_bound(s,t):
    s,t=as_intervals(s),as_intervals(t);bounds=[]
    for a,b in ((s[0],t[0]),(s[-1],t[-1])):
        xy=[(a[k]/a[2]-b[k]/b[2]).absmax for k in (0,1)]
        bounds.append(c.rnd(lambda:sum(xy,D(0)),True))
    return max(bounds)

def point_disproves(s,t,tol):
    for a,b in ((s[0],t[0]),(s[-1],t[-1])):
        for k in (0,1):
            d=a[k]/a[2]-b[k]/b[2]
            if d.lo>tol or d.hi < tol.copy_negate():return True
    return False

def certify_pair(s,t,tol,budget,depth=0):
    budget.check();b=difference_bound(s,t)
    if b<=tol:return b
    si,ti=as_intervals(s),as_intervals(t)
    if point_disproves(si,ti,tol):return None
    if depth>=budget.o.certificate_depth:return None
    sl,sr=c.split(s);tl,tr=c.split(t)
    a=certify_pair(sl,tl,tol,budget,depth+1)
    if a is None:return None
    b=certify_pair(sr,tr,tol,budget,depth+1)
    return None if b is None else max(a,b)

def global_point(segments,u):
    for seg in segments:
        a,b=seg['domain']
        if a<=u<=b:return c.point(seg['h'],(u-a)/(b-a))
    raise ReviewRequired('Parameter not covered')

def endpoint_tangent(h,end=False):
    # Repeated endpoint controls may give a zero first derivative although
    # the limiting tangent exists. Positive rational weights preserve the
    # direction of the first noncoincident Euclidean control.
    endpoint=h[-1] if end else h[0]
    candidates=reversed(h[:-1]) if end else h[1:]
    for p in candidates:
        v=(endpoint[0]*p[2]-p[0]*endpoint[2],endpoint[1]*p[2]-p[1]*endpoint[2]) if end else (p[0]*endpoint[2]-endpoint[0]*p[2],p[1]*endpoint[2]-endpoint[1]*p[2])
        if v!=(0,0):return tuple(map(float,v))
    return (0.,0.)

def collinear_certificate(segments,g):
    """Exact whole-image proof independent of nonuniform curve speed.
    Positive-weight convex hull on a segment + endpoint continuity covers
    the whole segment in both directions. Ordered controls also rule out
    backtracking within each span. Serialized endpoints supply the bound.
    """
    if g['type']!='LINE':return None
    a,b=c.point(segments[0]['h']),c.point(segments[-1]['h'],F(1));d=(b[0]-a[0],b[1]-a[1]);dd=d[0]*d[0]+d[1]*d[1]
    if dd==0:return None
    written_d=(F(g['x1'])-F(g['x0']),F(g['y1'])-F(g['y0']))
    if d[0]*written_d[0]+d[1]*written_d[1]<=0:return None
    previous=None
    for seg in segments:
        h=seg['h']
        if any(p[2]<=0 for p in h):return None
        start=c.point(h)
        if previous is not None and start!=previous:return None
        ts=[]
        for p in h:
            x,y=p[0]/p[2]-a[0],p[1]/p[2]-a[1]
            if d[0]*y-d[1]*x!=0:return None
            t=(x*d[0]+y*d[1])/dd
            if not 0<=t<=1:return None
            ts.append(t)
        if any(x>y for x,y in zip(ts,ts[1:])):return None
        previous=c.point(h,F(1))
    return endpoint_bound([segments[0]['h'][0],segments[-1]['h'][-1]],c.primitive_h(g))

def candidate(segments):
    left,right=segments[0]['domain'][0],segments[-1]['domain'][1]
    a,b=global_point(segments,left),global_point(segments,right)
    m=global_point(segments,(left+right)/2)
    synthetic=[(a[0],a[1],F(1)),(2*m[0]-(a[0]+b[0])/2,2*m[1]-(a[1]+b[1])/2,F(1)),(b[0],b[1],F(1))]
    choices=[];arc=c.fit_arc(synthetic)
    if arc:choices.append(arc)
    if a!=b:choices.append((dict(type='LINE',x0=c.number(a[0]),y0=c.number(a[1]),x1=c.number(b[0]),y1=c.number(b[1])),False))
    # Prefer LINE only for genuinely near-straight endpoint tangents.
    if choices and choices[-1][0]['type']=='LINE':
        line=choices[-1][0];v=c.primitive_tangent(line,False)
        if max(c.angle_error(endpoint_tangent(segments[0]['h']),v),c.angle_error(endpoint_tangent(segments[-1]['h'],True),v))<=.01:choices=choices[-1:]+choices[:-1]
    return choices

def attempt(segments,budget):
    o=budget.o;left,right=segments[0]['domain'][0],segments[-1]['domain'][1]
    a,b=global_point(segments,left),global_point(segments,right)
    if a!=b:
        line=dict(type='LINE',x0=c.number(a[0]),y0=c.number(a[1]),x1=c.number(b[0]),y1=c.number(b[1]))
        bound=collinear_certificate(segments,line)
        if bound is not None and bound<=min(o.endpoint_tolerance_mm,o.tolerance_mm):
            budget.check(candidate=True)
            tangent=max(c.angle_error(endpoint_tangent(segments[0]['h']),c.primitive_tangent(line,False)),c.angle_error(endpoint_tangent(segments[-1]['h'],True),c.primitive_tangent(line,True)))
            if tangent<=o.tangent_degrees:return dict(primitive=line,reverse=False,segments=segments,bound=bound,endpointBound=bound,tangentErrorDegreesDiagnostic=tangent,certificateKind='COLLINEAR_WHOLE_IMAGE')
    for g,reverse in candidate(segments):
        budget.check(candidate=True)
        tang=max(c.angle_error(endpoint_tangent(segments[0]['h']),c.primitive_tangent(g,False,reverse)),c.angle_error(endpoint_tangent(segments[-1]['h'],True),c.primitive_tangent(g,True,reverse)))
        if tang>o.tangent_degrees:continue
        target=c.primitive_h(g,reverse)
        e=endpoint_bound([segments[0]['h'][0],segments[-1]['h'][-1]],target)
        if e>o.endpoint_tolerance_mm:continue
        bounds=[]
        for seg in segments:
            a,b=seg['domain'];t=restrict(target,(a-left)/(right-left),(b-left)/(right-left))
            bound=certify_pair(seg['h'],t,o.tolerance_mm,budget)
            if bound is None:break
            bounds.append(bound)
        if len(bounds)==len(segments):
            return dict(primitive=g,reverse=reverse,segments=segments,bound=max(bounds),endpointBound=e,tangentErrorDegreesDiagnostic=tang,certificateKind='PAIRED_BERNSTEIN')
    return None

def adaptive(seg,budget,depth=0):
    budget.check();result=attempt([seg],budget)
    if result is not None:
        budget.leaves+=1
        if budget.leaves>budget.o.max_segments:raise ResourceLimit('Maximum output segments exceeded')
        return [result]
    if depth>=budget.o.max_depth:raise ResourceLimit('Maximum geometric subdivision depth exceeded')
    l,r=c.split(seg['h']);a,b=seg['domain'];m=(a+b)/2
    return adaptive(dict(h=l,domain=(a,m)),budget,depth+1)+adaptive(dict(h=r,domain=(m,b)),budget,depth+1)

def length_bounds(segments,budget):
    # Whole-domain derivative bound in local u; lower bound from endpoint chord.
    # These are an enclosure, not a falsely exact sampled length.
    lower,upper=D(0),D(0)
    for seg in segments:
        leaves=[seg['h']]
        for _ in range(3):leaves=[q for h in leaves for q in c.split(h)]
        for h in leaves:
            budget.check();hi=as_intervals(h);n=len(h)-1;w=[p[2] for p in hi]
            dw=[n*(b-a) for a,b in zip(w,w[1:])]
            v=[]
            for k in (0,1):
                x=[p[k] for p in hi];dx=[n*(b-a) for a,b in zip(x,x[1:])]
                prod1=c.product(dx,w);prod2=c.product(x,dw)
                numerator=max((a-b).absmax for a,b in zip(prod1,prod2));minw=min(t.lo for t in w)
                v.append(c.rnd(lambda:numerator/(minw*minw),True))
            def sqrt_bounds(f):
                scale=10**24
                root=math.isqrt(f.numerator*scale*scale//f.denominator)
                return c.I(F(root,scale)).lo,c.I(F(root+1,scale)).hi
            _,ub=sqrt_bounds(sum((F(x)**2 for x in v),F(0)))
            endpoints=[c.point(h),c.point(h,F(1))];ds=[abs(a-b) for a,b in zip(*endpoints)]
            lb,_=sqrt_bounds(sum((d*d for d in ds),F(0)))
            lower=c.rnd(lambda:lower+lb,False);upper=c.rnd(lambda:upper+ub,True)
    mid=c.rnd(lambda:(lower+upper)/2,True)
    return dict(estimateMm=str(mid),lowerBoundMm=str(lower),upperBoundMm=str(upper),kind='Certified conservative length enclosure; midpoint is an estimate, not an exact length')

def _convert_v02(degree,knots,points,weights=None,closed=False,periodic=False,options=None):
    o=options or Options();o.validate();budget=Budget(o)
    source=spans(degree,knots,points,weights,closed,periodic,budget)
    if all(c.point([p])==c.point([source[0]['h'][0]]) for seg in source for p in seg['h']):raise ReviewRequired('Zero-length SPLINE')
    # Try one element across all knots before subdividing.
    one=attempt(source,budget)
    pieces=[one] if one is not None else [p for seg in source for p in adaptive(seg,budget)]
    before=len(pieces);merges=0
    if o.merge:
        for _ in range(3):
            next_pieces=[];i=0;changed=False
            while i<len(pieces):
                if i+1<len(pieces):
                    merged=attempt(pieces[i]['segments']+pieces[i+1]['segments'],budget)
                    if merged is not None:next_pieces.append(merged);merges+=1;changed=True;i+=2;continue
                next_pieces.append(pieces[i]);i+=1
            pieces=next_pieces
            if not changed:break
    length=length_bounds(source,budget)
    joints=[]
    adjacent=list(zip(pieces,pieces[1:]))+([(pieces[-1],pieces[0])] if closed or periodic else [])
    for a,b in adjacent:
        joints.append(c.angle_error(c.primitive_tangent(a['primitive'],True,a['reverse']),c.primitive_tangent(b['primitive'],False,b['reverse'])))
    return dict(pieces=pieces,sourceLength=length,sourceSpans=len(source),closed=closed,periodic=periodic,maxBoundMm=str(max(p['bound'] for p in pieces)),maxEndpointBoundMm=str(max(p['endpointBound'] for p in pieces)),jointTangentErrorsDegreesDiagnostic=joints,maxJointTangentErrorDegreesDiagnostic=max(joints,default=0),exactG1Certified=False,minimumElementCountProven=False,optimization=dict(method='Whole-domain single-element attempt, adaptive subdivision, three certified adjacent merge passes',beforeMerging=before,afterMerging=len(pieces),merges=merges),candidatesTested=budget.candidates,certificateNodes=budget.nodes,elapsedSeconds=time.monotonic()-budget.start)

def joint_quality(pieces,closed=False):
    result=[]
    pairs=list(zip(pieces,pieces[1:]))+([(pieces[-1],pieces[0])] if closed else [])
    for a,b in pairs:
        src=c.angle_error(endpoint_tangent(a['segments'][-1]['h'],True),endpoint_tangent(b['segments'][0]['h']))
        dst=c.angle_error(c.primitive_tangent(a['primitive'],True,a['reverse']),c.primitive_tangent(b['primitive'],False,b['reverse']))
        result.append(dict(sourceAngleDegreesDiagnostic=src,outputAngleDegreesDiagnostic=dst,addedAngleDegreesDiagnostic=abs(dst-src),intentionalCorner=src>1e-5))
    return result

def convert_spline(degree,knots,points,weights=None,closed=False,periodic=False,options=None):
    from dataclasses import replace
    import biarc_refinement
    o=options or Options();start=time.monotonic()
    r=_convert_v02(degree,knots,points,weights,closed,periodic,o)
    before=r['pieces'];quality=joint_quality(before,closed or periodic)
    old_smooth=max((p['addedAngleDegreesDiagnostic'] for p in quality if not p['intentionalCorner']),default=0.)
    trial=dict(baselineElementCount=len(before),baselineBoundMm=r['maxBoundMm'],baselineMaxAddedSmoothJointDegreesDiagnostic=old_smooth,selected=False)
    if len(before)>2 and o.max_seconds-(time.monotonic()-start)>.01:
        try:
            remaining=replace(o,max_seconds=o.max_seconds-(time.monotonic()-start),max_candidates=max(1,o.max_candidates-r['candidatesTested']))
            source=spans(degree,knots,points,weights,closed,periodic)
            pieces,budget=biarc_refinement.fit(source,remaining)
            q=joint_quality(pieces,closed or periodic)
            smooth=max((p['addedAngleDegreesDiagnostic'] for p in q if not p['intentionalCorner']),default=0.)
            trial.update(candidateElementCount=len(pieces),candidateMaxAddedSmoothJointDegreesDiagnostic=smooth,coarseBiarcRejections=getattr(budget,'rejections',{}),sourceGroupsPreservingIntentionalCorners=budget.sourceGroups)
            if len(pieces)<len(before) or (len(pieces)==len(before) and smooth<old_smooth):
                r['pieces']=pieces;quality=q;trial['selected']=True
                r['maxBoundMm']=str(max(p['bound'] for p in pieces))
                r['maxEndpointBoundMm']=str(endpoint_bound([pieces[0]['segments'][0]['h'][0],pieces[-1]['segments'][-1]['h'][-1]],[c.primitive_h(pieces[0]['primitive'],pieces[0]['reverse'])[0],c.primitive_h(pieces[-1]['primitive'],pieces[-1]['reverse'])[-1]]))
                r['maxInternalCorrespondenceEndpointBoundMm']=str(max(p['endpointBound'] for p in pieces))
                r['optimization'].update(method='Count-first bounded nonuniform ARC/LINE and biarc candidate search; whole-curve certified acceptance; tangent quality is a tie-breaker and separately checked',afterMerging=len(pieces),biarcReplacementSavings=len(before)-len(pieces))
            r['candidatesTested']+=budget.candidates;r['certificateNodes']+=budget.nodes
        except (ReviewRequired,ValueError,OverflowError,ZeroDivisionError) as e:trial['fallbackReason']=str(e)
    r['jointChecks']=quality;r['jointTangentErrorsDegreesDiagnostic']=[q['outputAngleDegreesDiagnostic'] for q in quality]
    r['maxJointTangentErrorDegreesDiagnostic']=max(r['jointTangentErrorsDegreesDiagnostic'],default=0.)
    r['maxAddedSmoothJointDegreesDiagnostic']=max((q['addedAngleDegreesDiagnostic'] for q in quality if not q['intentionalCorner']),default=0.)
    r['biarcOptimization']=trial;r['elapsedSeconds']=time.monotonic()-start
    return r

def serializable(result):
    def default(x):
        if isinstance(x,(F,D)):return str(x)
        raise TypeError(type(x).__name__)
    import json
    return json.loads(json.dumps(result,default=default))
