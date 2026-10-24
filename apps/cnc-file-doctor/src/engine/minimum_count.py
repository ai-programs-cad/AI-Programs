"""Shortest certified ARC/LINE path over an existing source partition.

No global continuous-domain optimum is claimed. Failed certification means
an unavailable candidate, not a proof that no fitting circle exists.
"""
import time
from dataclasses import replace
from decimal import Decimal as D
import certified_curves as c
import spline_arc_core as core

def shortest_path(size,edges):
    """Minimum element count on a forward graph; bounds break count ties."""
    best={0:(0,D(0),[])}
    for end in range(1,size+1):
        for start in range(end):
            if start not in best or (start,end) not in edges:continue
            piece=edges[start,end];count,bound,path=best[start]
            candidate=(count+1,max(bound,piece['bound']),path+[piece])
            if end not in best or candidate[:2]<best[end][:2]:best[end]=candidate
    return best[size][2]

def optimize(pieces,options):
    n=len(pieces);edges={(i,i+1):piece for i,piece in enumerate(pieces)}
    info=dict(objective='Fewest certified ARC/LINE elements within tolerance',before=n,after=n,globalMinimumProven=False,completeCandidateSearch=False,minimumForCertifiedCandidateGraph=True,candidateEdges=n,intervalsTested=0,resourceLimitReached=False)
    budget=core.Budget(options)
    if n<=1:
        info['completeCandidateSearch']=True;return pieces,info,budget
    if n>80:
        info['resourceLimitReached']=True;info['reason']='Partition exceeds 80-vertex search limit';return pieces,info,budget
    corners=[i for i in range(1,n) if c.angle_error(core.endpoint_tangent(pieces[i-1]['segments'][-1]['h'],True),core.endpoint_tangent(pieces[i]['segments'][0]['h']))>1e-5]
    try:
        # Test long spans first so a bounded search can still find large savings.
        for width in range(n,1,-1):
            for start in range(n-width+1):
                end=start+width
                if any(start<corner<end for corner in corners):continue
                budget.check();info['intervalsTested']+=1
                segments=[s for p in pieces[start:end] for s in p['segments']]
                candidate=core.attempt(segments,budget)
                if candidate is not None:edges[start,end]=candidate
        info['completeCandidateSearch']=True
    except core.ResourceLimit as e:
        info['resourceLimitReached']=True;info['reason']=str(e)
    result=shortest_path(n,edges)
    info.update(after=len(result),candidateEdges=len(edges),elapsedSeconds=time.monotonic()-budget.start)
    return result,info,budget
