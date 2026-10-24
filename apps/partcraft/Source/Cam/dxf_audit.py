"""Independent DXF structure/coordinate audit. Not Autodesk AUDIT replacement."""
from collections import Counter
from decimal import Decimal as D
from pathlib import Path
import dxf_support as dx
from InspectDxfStructure import inspect

def audit(path):
    structure=inspect(path);pairs,rows=dx.read(path);definitions=[];references=[];section=None;record=[]
    for i,(k,v,raw) in enumerate(pairs):
        if (k,v)==(0,'SECTION'):section=pairs[i+1][1]
        if (k,v)==(0,'ENDSEC'):section=None
        if section and section!='HEADER':
            if k in (5,105) and v!='0':definitions.append(v.upper())
            if 330<=k<=369 and v!='0':references.append(v.upper())
    duplicates=[h for h,n in Counter(definitions).items() if n>1]
    unresolved=sorted(set(references)-set(definitions))
    finite=True
    for row in rows:
        for code,val in row['pairs']:
            if 10<=code<=59 and not D(val).is_finite():finite=False
    invalid=[]
    for row in rows:
        k=dx.kind(row)
        if k=='LINE' and (D(dx.one(row,10)),D(dx.one(row,20)))==(D(dx.one(row,11)),D(dx.one(row,21))):invalid.append(dx.one(row,5))
        if k in ('ARC','CIRCLE') and D(dx.one(row,40))<=0:invalid.append(dx.one(row,5))
        if k=='ARC' and (D(dx.one(row,51))-D(dx.one(row,50)))%360==0:invalid.append(dx.one(row,5))
    curves=[h for r in rows for h in dx.conics(dx.primitive(r))] if not invalid and all(dx.kind(r) in ('LINE','ARC','CIRCLE') for r in rows) else []
    boxes=[dx.box(h) for h in curves]
    bounds=None if not boxes else dict(xMin=str(min(b[0] for b in boxes)),xMax=str(max(b[1] for b in boxes)),yMin=str(min(b[2] for b in boxes)),yMax=str(max(b[3] for b in boxes)),method='Conservative whole-conic convex-hull bounds, not stale header extents')
    return dict(status='STRUCTURAL_AUDIT_PASS' if not duplicates and not unresolved and not invalid and finite else 'NEEDS_REVIEW',autodeskAudit=False,modelSpaceOnly=all(e['space']=='*Model_Space' for e in structure['entities']),unitsMm=structure['headers']['$INSUNITS']==[(70,'4')],coordinateTransformApplied=False,scale=1,finiteCoordinates=finite,onlyArcLineCircle=all(dx.kind(r) in ('LINE','ARC','CIRCLE') for r in rows),entityCounts=structure['geometryCounts'],layerTables=structure['layers'],linetypeTables=structure['linetypes'],duplicateHandles=duplicates,zeroLengthOrInvalidEntities=invalid,unresolvedObjectReferences=unresolved,actualGeometryBoundsMm=bounds,sha256=dx.sha(path),fileUnchanged=True)

if __name__=='__main__':
    import argparse,json
    p=argparse.ArgumentParser();p.add_argument('dxf');a=p.parse_args();print(json.dumps(audit(Path(a.dxf)),ensure_ascii=False,indent=2))
