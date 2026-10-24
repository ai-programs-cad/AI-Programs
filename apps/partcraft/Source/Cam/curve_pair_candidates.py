"""Conservative sweep of interval convex-hull boxes. Touching boxes are retained."""
import dxf_support as dx

def curve_box(h):
    if any(w.lo <= 0 for x,y,w in h):
        raise ValueError('Positive rational weights required for convex-hull exclusion')
    return dx.box(h)

def union_box(curves):
    boxes=[curve_box(h) for h in curves]
    if not boxes:raise ValueError('Empty curve group')
    return (min(b[0] for b in boxes),max(b[1] for b in boxes),min(b[2] for b in boxes),max(b[3] for b in boxes))

def candidate_pairs(boxes):
    active=[]
    for i in sorted(range(len(boxes)),key=lambda i:(boxes[i][0],i)):
        b=boxes[i]
        active=[j for j in active if boxes[j][1]>=b[0]]
        for j in active:
            a=boxes[j]
            if not (a[3]<b[2] or b[3]<a[2]):
                yield (j,i) if j<i else (i,j)
        active.append(i)
