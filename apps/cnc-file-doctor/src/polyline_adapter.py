"""Lossless straight LWPOLYLINE adapter; legacy engine remains unchanged.

Validation follows ManufacturingDxfV04.cs: vertex counts, zero width,
closed flag and degenerate segment rejection. Curved bulges are refused.
"""
from decimal import Decimal as D
from pathlib import Path
import dxf_support as dx

def vertices(row):
    allowed={0,5,330,100,67,410,8,6,62,420,430,48,370,440,60,90,70,43,10,20,40,41,42,38,39,210,220,230}
    if any(k not in allowed for k,v in row['pairs']):
        raise ValueError('LWPOLYLINE has unsupported metadata; preserved without conversion')
    if any(not D(v).is_finite() for k,v in row['pairs'] if k in (10,20,40,41,42,43,38,39,210,220,230)):
        raise ValueError('Nonfinite LWPOLYLINE coordinate')
    if any(D(v)!=0 for k,v in row['pairs'] if k in (40,41,43,38,39)):
        raise ValueError('Nonzero polyline width, elevation or thickness is not supported')
    if any(D(v)!=0 for k,v in row['pairs'] if k==42):
        raise ValueError('Curved LWPOLYLINE bulge is not supported yet')
    normal=[D(dx.one(row,k,d)) for k,d in ((210,'0'),(220,'0'),(230,'1'))]
    if normal!=[0,0,1]:raise ValueError('Nonstandard polyline extrusion is not supported')
    flags=int(dx.one(row,70,'0'))
    if flags & ~129:raise ValueError('Unsupported LWPOLYLINE flags')
    points=[]
    for k,v in row['pairs']:
        if k==10:
            if points and len(points[-1])!=2:raise ValueError('Missing polyline Y coordinate')
            points.append([v])
        elif k==20:
            if not points or len(points[-1])!=1:raise ValueError('Unexpected polyline Y coordinate')
            points[-1].append(v)
    if len(points)<2 or any(len(p)!=2 for p in points) or len(points)!=int(dx.one(row,90,'-1')):
        raise ValueError('Invalid LWPOLYLINE vertex count')
    count=len(points) if flags&1 else len(points)-1
    segments=[(points[i],points[(i+1)%len(points)]) for i in range(count)]
    if any(tuple(map(D,a))==tuple(map(D,b)) for a,b in segments):raise ValueError('Zero-length polyline segment')
    return segments

def normalize(source,target,cut_layers):
    pairs,rows=dx.read(source); replacements={}; evidence=[]
    polys=[r for r in rows if dx.kind(r)=='LWPOLYLINE']
    if not polys:return None
    next_handle=max(int(v,16) for k,v,_ in pairs if k in (5,105))+1
    style={330,67,410,8,6,62,420,430,48,370,440,60}
    for row in polys:
        if dx.one(row,8,'0') not in cut_layers:raise ValueError('Polyline outside selected CUT layers is preserved; conversion blocked')
        handle=dx.one(row,5)
        if not handle:raise ValueError('Polyline has no handle')
        if any(330<=k<=369 and v.upper()==handle.upper() for k,v,_ in pairs):
            raise ValueError('Referenced polyline cannot be split safely')
        segments=vertices(row); blob=b''; output_handles=[]
        for index,(a,b) in enumerate(segments):
            h=handle if index==0 else format(next_handle,'X')
            if index:next_handle+=1
            output_handles.append(h)
            # Copy style/owner tags as their original bytes, including legacy strings.
            blob+=dx.encode([(0,'LINE'),(5,h)])
            blob+=b''.join(raw for k,v,raw in pairs[row['start']:row['end']] if k==330)
            blob+=dx.encode([(100,'AcDbEntity')])
            blob+=b''.join(raw for k,v,raw in pairs[row['start']:row['end']] if k in style and k!=330)
            blob+=dx.encode([(100,'AcDbLine'),(10,a[0]),(20,a[1]),(30,'0'),(11,b[0]),(21,b[1]),(31,'0')])
        replacements[row['start']]=(row['end'],blob)
        evidence.append(dict(sourceHandle=handle,outputHandles=output_handles,segments=len(segments),coordinatesCopiedAsDecimalStrings=True))
    # HANDSEED must exceed all generated handles.
    for i,(k,v,raw) in enumerate(pairs[:-1]):
        if (k,v)==(9,'$HANDSEED'):
            if pairs[i+1][0]!=5:raise ValueError('Invalid HANDSEED')
            replacements[i+1]=(i+2,dx.encode([(5,format(next_handle,'X'))]))
    chunks=[];i=0
    while i<len(pairs):
        if i in replacements:
            end,blob=replacements[i];chunks.append(blob);i=end
        else:chunks.append(pairs[i][2]);i+=1
    Path(target).write_bytes(b''.join(chunks))
    after,arows=dx.read(target)
    original=[dx.rawrow(pairs,r) for r in rows if dx.kind(r)!='LWPOLYLINE']
    original_handles={dx.one(r,5) for r in rows if dx.kind(r)!='LWPOLYLINE'}
    preserved=[dx.rawrow(after,r) for r in arows if dx.one(r,5) in original_handles]
    if original!=preserved:raise ValueError('Existing entities changed during normalization')
    by_handle={dx.one(r,5):r for r in arows}
    for row,proof in zip(polys,evidence):
        for (a,b),h in zip(vertices(row),proof['outputHandles']):
            out=by_handle[h]
            if [dx.one(out,k) for k in (10,20,11,21)]!=a+b:raise ValueError('Written polyline coordinate mismatch')
    return dict(method='Exact straight segment expansion; no curve fitting',wholeCurveBoundMm='0',polylineCount=len(polys),lineCount=sum(e['segments'] for e in evidence),existingEntitiesBytePreserved=True,serializedCoordinatesVerified=True,sourceSha256=dx.sha(source),normalizedSha256=dx.sha(target),records=evidence,linetypePhaseMayRestartPerSegment=True)
