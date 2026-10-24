"""Original-vs-output tangents at EVERY CUT junction, including native corners.
Small-angle upper bounds use directed intervals, not just float atan2.
"""
import math
from fractions import Fraction as F
from decimal import Decimal as D
import certified_curves as c
import spline_arc_core as core
import dxf_support as dx

def exact_source_tangent(h,end=False):
    e=h[-1] if end else h[0]
    for p in (reversed(h[:-1]) if end else h[1:]):
        v=(e[0]*p[2]-p[0]*e[2],e[1]*p[2]-p[1]*e[2]) if end else (p[0]*e[2]-e[0]*p[2],p[1]*e[2]-e[1]*p[2])
        if v!=(0,0):return tuple(c.I(x) for x in v)
    raise ValueError('Stationary/degenerate source endpoint')

def tangent(row,end):
    if dx.kind(row)=='SPLINE':
        spans=dx.spline(row);return exact_source_tangent(spans[-1][2] if end else spans[0][2],end)
    g=dx.primitive(row)
    if g['type']=='LINE':return (c.I(F(g['x1'])-F(g['x0'])),c.I(F(g['y1'])-F(g['y0'])))
    angle=F(g['end'] if end else g['start']) if g['type']=='ARC' else F(0)
    s,co=c.trig(angle);return (-s,co)

def small_angle_bound(a,b):
    dot=a[0]*b[0]+a[1]*b[1];cross=a[0]*b[1]-a[1]*b[0]
    if dot.lo<=0:return None
    # atan(q) <= q: rigorous and intentionally conservative.
    return c.rnd(lambda:cross.absmax/dot.lo*D(180)/c.pi_interval().lo,True)

def diagnostic(a,b):
    af=[float((v.lo+v.hi)/2) for v in a];bf=[float((v.lo+v.hi)/2) for v in b]
    return c.angle_error(af,bf)

def check(source,output,objects,g1_degrees=D('.00001'),corner_degrees=D('.05')):
    graph,nodes,edges=dx.graph(output);src={dx.one(row,5):row for row in source};proof={p['outputHandle']:p for o in objects if 'pieces' in o for p in o['pieces']}
    adjacent={}
    for i,edge in enumerate(edges):
        for endpoint,node in enumerate(edge):adjacent.setdefault(node,[]).append((i,endpoint))
    records=[];smooth_ok=True;corners_ok=True
    for node,pair in adjacent.items():
        if len(pair)!=2:continue
        dst=[];expected=[]
        for i,end in pair:
            row=output[i];handle=dx.one(row,5);v=tangent(row,bool(end));away=-1 if end else 1
            dst.append(tuple(away*x for x in v))
            if handle in proof:
                p=proof[handle];at_end=bool(end)!=p['reverse']
                h=[[F(x) for x in point] for point in (p['segments'][-1] if at_end else p['segments'][0])['h']]
                sv=exact_source_tangent(h,at_end);mult=away*(-1 if p['reverse'] else 1)
                expected.append(tuple(mult*x for x in sv))
            else:expected.append(tuple(away*x for x in tangent(src[handle],bool(end))))
        incoming=tuple(-x for x in dst[0]);src_incoming=tuple(-x for x in expected[0])
        sb=small_angle_bound(src_incoming,expected[1]);db=small_angle_bound(incoming,dst[1]);sa=diagnostic(src_incoming,expected[1]);da=diagnostic(incoming,dst[1])
        is_smooth=sb is not None and sb<=g1_degrees
        changes=[small_angle_bound(v,w) for v,w in zip(dst,expected)]
        change_bound=None if None in changes else c.rnd(lambda:sum(changes,D(0)),True)
        if is_smooth:
            ok=db is not None and db<=g1_degrees;smooth_ok &= ok
        else:
            ok=change_bound is not None and change_bound<=corner_degrees;corners_ok &= ok
        records.append(dict(handles=[dx.one(output[i],5) for i,e in pair],sourceAngleDegreesDiagnostic=sa,outputAngleDegreesDiagnostic=da,addedAngleDegreesDiagnostic=abs(da-sa),sourceSmooth=is_smooth,outputAngleUpperBoundDegrees=None if db is None else str(db),cornerAngleChangeUpperBoundDegrees=None if change_bound is None else str(change_bound),passed=ok))
    return dict(allSmoothJunctionsWithinAngularTolerance=smooth_ok,originalCornersPreservedWithinAngularTolerance=corners_ok,g1ToleranceDegrees=str(g1_degrees),cornerToleranceDegrees=str(corner_degrees),maxAddedAngleDegreesDiagnostic=max((r['addedAngleDegreesDiagnostic'] for r in records),default=0),maxSmoothAngleUpperBoundDegrees=str(max((D(r['outputAngleUpperBoundDegrees']) for r in records if r['sourceSmooth'] and r['outputAngleUpperBoundDegrees'] is not None),default=D(0))),junctions=records,exactMathematicalG1Claimed=False)
