"""Tangent-constrained adaptive biarcs, followed by bounded certified merging.

Floating point work only proposes/rejects candidates. Acceptance remains the
existing whole-domain rational interval certificate and serialized DXF audit.
"""
from dataclasses import replace
import time
import certified_curves as c
import spline_arc_core as core
import biarc_refinement as bi

def fit(source, options):
    # Singles must share the source limiting tangent almost exactly, otherwise
    # independently valid arcs can introduce visible kinks at subdivision joins.
    strict=replace(options,tangent_degrees=min(options.tangent_degrees,1e-6))
    budget=core.Budget(strict);groups=[];current=[]
    for seg in source:
        if current and c.angle_error(core.endpoint_tangent(current[-1]['h'],True),core.endpoint_tangent(seg['h']))>1e-5:
            groups.append(current);current=[]
        current.append(seg)
    if current:groups.append(current)
    calls=0
    def candidate(segments):
        nonlocal calls
        calls+=1;budget.check()
        one=core.attempt(segments,budget)
        return [one] if one is not None else bi.attempt(segments,budget)
    def adaptive(segments,depth=0):
        proposed=candidate(segments)
        if proposed is not None:return [proposed]
        if depth>=min(options.max_depth,20):raise core.ResourceLimit('Smooth biarc subdivision limit')
        a,b=segments[0]['domain'][0],segments[-1]['domain'][1]
        left,right=bi.cut_segments(segments,(a+b)/2)
        result=adaptive(left,depth+1)+adaptive(right,depth+1)
        if sum(map(len,result))>options.max_segments:raise core.ResourceLimit('Smooth biarc element limit')
        return result
    units=[unit for group in groups for unit in adaptive(group)]
    before=sum(map(len,units));merges=0
    # Local windows avoid the old quadratic all-pairs search. Every successful
    # replacement preserves source tangents at its outer boundaries.
    if options.merge:
        deadline=min(options.max_seconds*.85,time.monotonic()-budget.start+2.)
        for width in (8,4,3,2):
            i=0
            while i+width<=len(units) and time.monotonic()-budget.start<deadline:
                window=[p for unit in units[i:i+width] for p in unit]
                if any(q['intentionalCorner'] for q in core.joint_quality(window)):
                    i+=1;continue
                segments=[s for p in window for s in p['segments']]
                try:proposed=candidate(segments)
                except core.ResourceLimit:break
                if proposed is not None and len(proposed)<len(window):
                    units[i:i+width]=[proposed];merges+=1
                else:i+=1
    pieces=[p for unit in units for p in unit]
    if len(pieces)>options.max_segments:raise core.ResourceLimit('Smooth biarc element limit')
    quality=core.joint_quality(pieces)
    if any(q['addedAngleDegreesDiagnostic']>3e-6 for q in quality if not q['intentionalCorner']):
        raise core.ReviewRequired('Smooth tangent acceptance failed')
    return pieces,budget,dict(method='Tangent-constrained adaptive biarcs and bounded certified local-window merging',beforeMerging=before,afterMerging=len(pieces),merges=merges,candidateIntervals=calls,sourceGroupsPreservingIntentionalCorners=len(groups),globalMinimumProven=False)
