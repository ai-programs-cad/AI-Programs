"""Independent, read-only final CUT contour gate at 0.001 mm."""
from decimal import Decimal as D
import dxf_support as dx

CLOSURE_TOLERANCE_MM=D('0.001')

def audit(path,layers):
    result=dict(toleranceMm=str(CLOSURE_TOLERANCE_MM),passed=False,cutLayers=list(layers),excludedBendLayers=['UP','DOWN'],geometryModified=False,exactZeroGapProven=False,method='Conservative whole-endpoint interval distance upper bound; degree-two closed components')
    try:
        pairs,rows=dx.read(path)
        cut=[r for r in rows if dx.one(r,8,'0') in layers and dx.one(r,8,'0').upper() not in ('UP','DOWN')]
        if not cut:raise ValueError('No CUT geometry to verify')
        if any(dx.kind(r) not in ('LINE','ARC','CIRCLE') for r in cut):raise ValueError('Unconverted CUT entities prevent closure verification')
        graph,nodes,edges=dx.graph(cut,CLOSURE_TOLERANCE_MM)
        collapsed=[dx.one(cut[c[0]],5) for c in graph['components'] if len(c)==1 and dx.kind(cut[c[0]])=='LINE']
        result.update(graph,cutEntityCount=len(cut),collapsedLineComponents=collapsed)
        result['passed']=bool(graph['closedDegreeTwo'] and not collapsed)
        if not result['passed']:result['reason']='Open, branched or degenerate CUT contour'
    except (ValueError,KeyError,ArithmeticError) as e:result['reason']=str(e)
    return result
