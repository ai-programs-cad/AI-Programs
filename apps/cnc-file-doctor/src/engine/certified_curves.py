"""Positive rational Bezier certificates using directed Decimal intervals.
Fitting is heuristic; acceptance is a whole-domain Bernstein product bound.
"""
import math
from decimal import Decimal as D, localcontext, ROUND_FLOOR, ROUND_CEILING
from fractions import Fraction as F
from functools import lru_cache

PRECISION = 55


def rnd(fn, upward):
    with localcontext() as c:
        c.prec = PRECISION
        c.rounding = ROUND_CEILING if upward else ROUND_FLOOR
        return fn()


class I:
    __slots__ = ('lo', 'hi')

    def __init__(self, a, b=None):
        if isinstance(a, I):
            self.lo, self.hi = a.lo, a.hi
        elif isinstance(a, F):
            self.lo = rnd(lambda: D(a.numerator) / D(a.denominator), False)
            self.hi = rnd(lambda: D(a.numerator) / D(a.denominator), True)
        else:
            self.lo, self.hi = D(a), D(a if b is None else b)

    def __add__(self, b):
        b = I(b)
        return I(rnd(lambda: self.lo + b.lo, False), rnd(lambda: self.hi + b.hi, True))

    __radd__ = __add__

    def __neg__(self):
        return I(self.hi.copy_negate(), self.lo.copy_negate())

    def __sub__(self, b):
        return self + -I(b)

    def __rsub__(self, b):
        return I(b) - self

    def __mul__(self, b):
        b = I(b)
        return I(min(rnd(lambda: x*y, False) for x in (self.lo,self.hi) for y in (b.lo,b.hi)), max(rnd(lambda: x*y, True) for x in (self.lo,self.hi) for y in (b.lo,b.hi)))

    __rmul__ = __mul__

    def __truediv__(self, b):
        b = I(b)
        if b.lo <= 0 <= b.hi:
            raise ValueError('Interval denominator contains zero')
        return self * I(rnd(lambda: D(1)/b.hi, False), rnd(lambda: D(1)/b.lo, True))

    @property
    def absmax(self):
        return max(self.lo.copy_abs(), self.hi.copy_abs())


def atan_bounds(x):
    total, n = F(0), 0
    while True:
        term = x ** (2*n+1) / (2*n+1)
        total += term if n % 2 == 0 else -term
        nxt = x ** (2*n+3) / (2*n+3)
        if nxt < F(1,10**70):
            other = total - nxt if n % 2 == 0 else total + nxt
            return min(total,other), max(total,other)
        n += 1


@lru_cache(None)
def pi_interval():
    a,b=atan_bounds(F(1,5));c,d=atan_bounds(F(1,239))
    lo,hi=I(16*a-4*d),I(16*b-4*c)
    return I(lo.lo,hi.hi)


@lru_cache(50000)
def trig(degrees):
    degrees=F(degrees)%360
    if degrees>180:degrees-=360
    x=I(degrees)*pi_interval()/180
    xx=x*x
    sin,cos,st,ct=x,I(1),x,I(1)
    for k in range(1,36):
        st=-st*xx/((2*k)*(2*k+1));ct=-ct*xx/((2*k-1)*(2*k))
        sin=sin+st;cos=cos+ct
    # Taylor/Lagrange remainder, |all derivatives| <= 1.
    z=x.absmax
    sr=rnd(lambda:z**72/D(math.factorial(72)),True)
    cr=rnd(lambda:z**71/D(math.factorial(71)),True)
    return sin+I(sr.copy_negate(),sr),cos+I(cr.copy_negate(),cr)


def split(h):
    rows=[list(h)]
    while len(rows[-1])>1:
        r=rows[-1];rows.append([tuple((a+b)/2 for a,b in zip(r[i],r[i+1])) for i in range(len(r)-1)])
    return [r[0] for r in rows], [r[-1] for r in reversed(rows)]


def point(h,t=F(0)):
    row=list(h)
    while len(row)>1:
        row=[tuple((1-t)*a+t*b for a,b in zip(row[i],row[i+1])) for i in range(len(row)-1)]
    return row[0][0]/row[0][2],row[0][1]/row[0][2]


def tangent(h,end=False):
    a,b=(h[-2],h[-1]) if end else (h[0],h[1])
    return float(b[0]*a[2]-a[0]*b[2]),float(b[1]*a[2]-a[1]*b[2])


def bezier_spans(degree,knots,controls,weights):
    """Exact homogeneous knot insertion up to multiplicity degree."""
    p=degree
    if len(controls)!=len(weights):raise ValueError('Weight count mismatch')
    u=list(map(F,knots));h=[(F(x)*F(w),F(y)*F(w),F(w)) for (x,y),w in zip(controls,weights)]
    if not 1<=p<=8 or len(u)!=len(h)+p+1 or any(w<=0 for _,_,w in h):raise ValueError('Unsupported B-spline')
    if any(u[i]>u[i+1] for i in range(len(u)-1)) or len(set(u[:p+1]))!=1 or len(set(u[-p-1:]))!=1:raise ValueError('Unclamped/nonmonotone knot data')
    for value in sorted(set(u[p+1:-p-1])):
        if u.count(value)>p:raise ValueError('Discontinuous interior knot')
        while u.count(value)<p:
            n=len(h)-1;k=max(i for i in range(len(u)-1) if u[i]<=value<u[i+1]);s=u.count(value)
            q=[None]*(len(h)+1)
            for i in range(k-p+1):q[i]=h[i]
            for i in range(k-s,n+1):q[i+1]=h[i]
            for i in range(k-p+1,k-s+1):
                alpha=(value-u[i])/(u[i+p]-u[i]);q[i]=tuple(alpha*a+(1-alpha)*b for a,b in zip(h[i],h[i-1]))
            u.insert(k+1,value);h=q
    spans=[]
    for k in range(p,len(h)):
        if u[k]<u[k+1]:spans.append((u[k],u[k+1],h[k-p:k+1]))
    if not spans:raise ValueError('Empty knot domain')
    return spans


def number(f):
    # Serial output is later certified AS WRITTEN, including this rounding.
    with localcontext() as c:
        c.prec=18
        return str(+(D(f.numerator)/D(f.denominator))) if isinstance(f,F) else format(float(f),'.17g')


def primitive_h(g,reverse=False):
    if g['type']=='LINE':
        return [(I(F(g['x0'])),I(F(g['y0'])),I(1)),(I(F(g['x1'])),I(F(g['y1'])),I(1))]
    a=F(g['start']);span=(F(g['end'])-a)%360
    if not 0<span<180:raise ValueError('Arc certificate requires sweep below 180 degrees')
    cx,cy,r=map(I,(g['cx'],g['cy'],g['r']))
    sa,ca=trig(a);sb,cb=trig(a+span);sm,cm=trig(a+span/2);_,w=trig(span/2)
    if w.lo<=0:raise ValueError('Nonpositive conic weight')
    h=[(cx+r*ca,cy+r*sa,I(1)),(cx*w+r*cm,cy*w+r*sm,w),(cx+r*cb,cy+r*sb,I(1))]
    return list(reversed(h)) if reverse else h


def product(a,b):
    n,m=len(a)-1,len(b)-1
    result=[]
    for k in range(n+m+1):
        result.append(sum((I(F(math.comb(n,i)*math.comb(m,k-i),math.comb(n+m,k)))*a[i]*b[k-i] for i in range(max(0,k-m),min(n,k)+1)),I(0)))
    return result


def certificate(source,g,reverse=False):
    target=primitive_h(g,reverse);s=[tuple(I(v) for v in p) for p in source]
    sw=[p[2] for p in s];tw=[p[2] for p in target]
    denominator=rnd(lambda:min(w.lo for w in sw)*min(w.lo for w in tw),False)
    if denominator<=0:raise ValueError('Denominator not certified positive')
    bounds=[]
    for axis in (0,1):
        lhs=product([p[axis] for p in s],tw);rhs=product([p[axis] for p in target],sw)
        bounds.append(max((a-b).absmax for a,b in zip(lhs,rhs)))
    bound=rnd(lambda:(bounds[0]+bounds[1])/denominator,True)
    endpoints=[]
    for a,b in ((s[0],target[0]),(s[-1],target[-1])):
        values=[(a[k]/a[2]-b[k]/b[2]).absmax for k in (0,1)]
        endpoints.append(rnd(lambda:sum(values,D(0)),True))
    return bound,max(endpoints),target


def angle_error(a,b):
    if math.hypot(*a)==0 or math.hypot(*b)==0:return 180.
    return abs(math.degrees(math.atan2(a[0]*b[1]-a[1]*b[0],a[0]*b[0]+a[1]*b[1])))


def fit_arc(h):
    a,m,b=point(h),point(h,F(1,2)),point(h,F(1))
    ux,uy=m[0]-a[0],m[1]-a[1];vx,vy=b[0]-a[0],b[1]-a[1]
    det=2*(ux*vy-uy*vx)
    if det==0:return None
    uu,vv=ux*ux+uy*uy,vx*vx+vy*vy
    cx=a[0]+(uu*vy-vv*uy)/det;cy=a[1]+(ux*vv-vx*uu)/det
    radius=math.hypot(float(cx-a[0]),float(cy-a[1]))
    aa=math.degrees(math.atan2(float(a[1]-cy),float(a[0]-cx)))%360
    bb=math.degrees(math.atan2(float(b[1]-cy),float(b[0]-cx)))%360
    reverse=det<0
    start,end=(bb,aa) if reverse else (aa,bb)
    if not 1e-8<(end-start)%360<170 or not math.isfinite(radius):return None
    return {'type':'ARC','cx':number(cx),'cy':number(cy),'r':number(radius),'start':number(start),'end':number(end)},reverse


def primitive_tangent(g,end,reverse=False):
    if g['type']=='LINE':return float(F(g['x1'])-F(g['x0'])),float(F(g['y1'])-F(g['y0']))
    angle=float(g['start'] if (end==reverse) else g['end'])
    v=(-math.sin(math.radians(angle)),math.cos(math.radians(angle)))
    return (-v[0],-v[1]) if reverse else v


def approximate(h,domain,tolerance=D('.01'),depth=0):
    a,b=point(h),point(h,F(1));line={'type':'LINE','x0':number(a[0]),'y0':number(a[1]),'x1':number(b[0]),'y1':number(b[1])}
    choices=[(line,False)]
    arc=fit_arc(h)
    if arc:choices.append(arc)
    for g,reverse in choices:
        if g['type']=='LINE' and (g['x0'],g['y0'])==(g['x1'],g['y1']):continue
        bound,endpoint,_=certificate(h,g,reverse)
        tang=max(angle_error(tangent(h),primitive_tangent(g,False,reverse)),angle_error(tangent(h,True),primitive_tangent(g,True,reverse)))
        if bound<=tolerance and endpoint<=D('.0000001') and tang<=.05:
            return [{'primitive':g,'reverse':reverse,'sourceDomain':[str(domain[0]),str(domain[1])],'sourceHomogeneous':[[str(v) for v in p] for p in h],'boundMm':str(bound),'endpointBoundMm':str(endpoint),'tangentErrorDegreesDiagnostic':tang}]
    if depth>=24:raise ValueError('Cannot certify approximation within subdivision limit')
    left,right=split(h);mid=(domain[0]+domain[1])/2
    return approximate(left,(domain[0],mid),tolerance,depth+1)+approximate(right,(mid,domain[1]),tolerance,depth+1)
