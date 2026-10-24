"""Generic offline batch stage: native EXACT DXF -> CNC candidate DXF.
Inputs are never modified; originals are copied byte-for-byte into EXACT.
No body names, counts, models, COM calls or ManufacturingPackage assumptions.
"""
import argparse, collections, dataclasses, hashlib, json, math, sys, time, traceback
from pathlib import Path
from decimal import Decimal as D
from fractions import Fraction as F
import certified_curves as cc
import spline_arc_core as core
import dxf_support as dx
from InspectDxfStructure import inspect

def safe_error(e):return dict(type=type(e).__name__,message=str(e),stackTrace=traceback.format_exc())
def save_json(path,r):path.write_text(json.dumps(r,ensure_ascii=False,indent=2,default=str),encoding='utf-8')

def source_data(row):
    flags=int(dx.one(row,70,'0'));xs,ys=dx.values(row,10),dx.values(row,20)
    knots=dx.values(row,40);weights=dx.values(row,41)
    if len(xs)!=len(ys) or len(xs)!=int(dx.one(row,73,'0')) or len(knots)!=int(dx.one(row,72,'0')):raise core.ReviewRequired('Inconsistent SPLINE counts or fit-points-only representation')
    if any(k in (102,360) or k>=1000 for k,v in row['pairs']):raise core.ReviewRequired('Associative/XDATA payload needs explicit handling; not dropped')
    if any(F(v)!=0 for k,v in row['pairs'] if k in (30,31)):raise core.ReviewRequired('Nonplanar control/fit coordinates')
    return dict(degree=int(dx.one(row,71,'0')),knots=knots,points=list(zip(xs,ys)),weights=weights or None,closed=bool(flags&1),periodic=bool(flags&2))

def style(row):
    # Entity properties override layer values. Layer table stays byte-identical.
    return {str(k):dx.one(row,k) for k in (8,6,62,420,430,48,370,440) if dx.one(row,k) is not None}

def check_written(pieces,by_handle,options):
    budget=core.Budget(options);bounds=[];endpoint=D(0)
    for p in pieces:
        primitive=dx.primitive(by_handle[p['outputHandle']]);target=cc.primitive_h(primitive,p['reverse'])
        segments=[dict(domain=tuple(map(F,s['domain'])),h=[tuple(map(F,q)) for q in s['h']]) for s in p['segments']]
        if p.get('certificateKind')=='COLLINEAR_WHOLE_IMAGE':
            bound=core.collinear_certificate(segments,primitive)
            if bound is None or bound>min(options.tolerance_mm,options.endpoint_tolerance_mm):raise core.ReviewRequired('Written analytic LINE proof failed')
            bounds.append(bound);endpoint=max(endpoint,bound);continue
        left,right=segments[0]['domain'][0],segments[-1]['domain'][1]
        for seg in segments:
            a,b=seg['domain'];t=core.restrict(target,(a-left)/(right-left),(b-left)/(right-left))
            bound=core.certify_pair(seg['h'],t,options.tolerance_mm,budget)
            if bound is None:raise core.ReviewRequired('Written curve did not pass whole-domain recertification')
            bounds.append(bound)
        endpoint=max(endpoint,core.endpoint_bound([segments[0]['h'][0],segments[-1]['h'][-1]],target))
    first,last=pieces[0],pieces[-1]
    a=[tuple(map(F,q)) for q in first['segments'][0]['h']][0];z=[tuple(map(F,q)) for q in last['segments'][-1]['h']][-1]
    ta=cc.primitive_h(dx.primitive(by_handle[first['outputHandle']]),first['reverse'])[0];tz=cc.primitive_h(dx.primitive(by_handle[last['outputHandle']]),last['reverse'])[-1]
    outer=core.endpoint_bound([a,z],[ta,tz])
    if outer>options.endpoint_tolerance_mm:raise core.ReviewRequired('Original SPLINE endpoints exceed tolerance')
    if endpoint>options.tolerance_mm:raise core.ReviewRequired('Internal correspondence exceeds geometric tolerance')
    return dict(wholeCurveTwoSidedBoundMm=str(max(bounds,default=D(0))),endpointBoundMm=str(outer),internalCorrespondenceEndpointBoundMm=str(endpoint),precisionDigits=cc.PRECISION,method='Whole-domain rational Bernstein interval bound, or exact collinear convex-hull/continuity proof; includes serialized DXF rounding')

def topology_pairs(rows,nodes,edges,seconds=60,max_pairs=100000):
    started=time.monotonic();hs=[dx.conics(dx.primitive(row)) for row in rows]
    unresolved=[];tested=0;boxes=0;finished=True;excluded=0
    hulls=[]
    for curves in hs:
        bounds=[dx.box(h) for h in curves]
        hulls.append((min(b[0] for b in bounds),max(b[1] for b in bounds),min(b[2] for b in bounds),max(b[3] for b in bounds)))
    for i in range(len(rows)):
        for j in range(i+1,len(rows)):
            if tested>=max_pairs or time.monotonic()-started>seconds:
                finished=False;break
            if dx.disjoint(hulls[i],hulls[j]):excluded+=1;continue
            shared=set(edges[i])&set(edges[j]);p=nodes[next(iter(shared))] if len(shared)==1 else None
            tested+=1
            for a in hs[i]:
                for b in hs[j]:
                    ok,reason,count=dx.separation(a,b,p,budget=2000);boxes+=count
                    if not ok:unresolved.append(dict(first=dx.one(rows[i],5),second=dx.one(rows[j],5),reason=reason))
        if not finished:break
    return dict(allPairsExcludedOutsideJoinTolerance=finished and not unresolved,completed=finished,pairsTested=tested,broadPhasePairsExcluded=excluded,subdivisionBoxes=boxes,unresolved=unresolved,joinToleranceMm='0.000001',exactMathematicalClosureCertified=False,resourceLimits=dict(maxPairs=max_pairs,maxSeconds=seconds,maxSubdivisionNodesPerPair=2000))

def process_file(source,exact,cnc,options,cut_layers=('0',),convert_bends=False,max_input_bytes=100_000_000):
    started=time.perf_counter();r=dict(partName=source.stem,source=str(source),exact=str(exact),cnc=str(cnc),status='NEEDS_REVIEW',productionReady=False,splines=[],unverifiedConditions=[],fileErrors=[],COMCallsMade=0)
    before=dx.sha(source);r['sourceSha256']=before
    try:
        if source.stat().st_size>max_input_bytes:raise core.ResourceLimit('Input size limit exceeded')
        raw=source.read_bytes()
        with exact.open('xb') as f:f.write(raw)
        r['exactCopySha256']=dx.sha(exact)
        pairs,rows=dx.read(exact);r['sourceEntityCounts']=dict(collections.Counter(dx.kind(row) for row in rows))
        r['discoveredSplineObjects']=[dict(handle=dx.one(row,5),layer=dx.one(row,8,'0')) for row in rows if dx.kind(row)=='SPLINE']
        r['totalSplineRecordsInFile']=sum(k==0 and v=='SPLINE' for k,v,_ in pairs)
        if r['totalSplineRecordsInFile']!=len(r['discoveredSplineObjects']):raise core.ReviewRequired('SPLINE inside BLOCKS/nested references: transform-aware expansion is not certified; no silent omission')
        structure=inspect(exact)
        if structure['headers'].get('$INSUNITS')!=[(70,'4')]:raise core.ReviewRequired('Only explicit millimetre units currently certified; no unit guess')
        if any(e['space']!='*Model_Space' for e in structure['entities']):raise core.ReviewRequired('Input geometry must already be in Model Space; no implicit relocation')
        r['allGeometryInModelSpace']=True;r['scale']=1;r['units']='mm'
        next_handle=max(int(v,16) for k,v,rawtag in pairs if k in (5,105))+1
        changes={};parent={dx.one(row,5):dx.one(row,5) for row in rows}
        for row in rows:
            if dx.kind(row)!='SPLINE':continue
            layer=dx.one(row,8,'0');role='BEND' if layer.upper() in ('UP','DOWN') else ('CUT' if layer in cut_layers else 'UNCLASSIFIED')
            obj=dict(partName=source.stem,sourceHandle=dx.one(row,5),layer=layer,role=role,attributes=style(row),status='NEEDS_REVIEW')
            r['splines'].append(obj)
            try:
                data=source_data(row)
                obj['sourceDefinition']=dict(degree=data['degree'],knotCount=len(data['knots']),controlCount=len(data['points']),weightsExplicit=data['weights'] is not None,closed=data['closed'],periodic=data['periodic'])
                if role=='BEND' and not convert_bends:raise core.ReviewRequired('BEND SPLINE preserved: native assignment/direction evidence required; geometric conversion opt-in is --convert-bend-splines and remains NEEDS_REVIEW')
                converted=core.convert_spline(**data,options=options)
                info=core.serializable(converted);obj.update(info)
                # Reuse only the proven DXF entity writer, not any fixed batch list.
                pieces=converted['pieces'];blob,next_handle=dx.replacement(pairs,row,pieces,next_handle)
                changes[row['start']]=(row['end'],blob)
                # Replacement assigns output handles; include them in the serialized proof.
                obj['pieces']=core.serializable(dict(pieces=pieces))['pieces']
                for p in pieces:parent[p['outputHandle']]=dx.one(row,5)
                obj['outputCounts']=dict(collections.Counter(p['primitive']['type'] for p in pieces))
                obj['deviationBoundVerified']=True;obj['endpointCheckPassed']=True
                obj['status']='GEOMETRY_BOUND_VERIFIED_NEEDS_REVIEW'
                if role!='CUT':obj['assignmentVerified']=False;r['unverifiedConditions'].append(role+' SPLINE '+obj['sourceHandle']+': physical purpose/direction not certified offline')
            except Exception as e:obj['error']=safe_error(e);obj['deviationBoundVerified']=False
        result=[];i=0
        while i<len(pairs):
            if i in changes:end,blob=changes[i];result.append(blob);i=end;continue
            if pairs[i][:2]==(9,'$HANDSEED'):
                result.append(pairs[i][2]);i+=1
                if pairs[i][0]!=5:raise core.ReviewRequired('Invalid HANDSEED record')
                result.append(dx.encode([(5,format(next_handle,'X'))]));i+=1;continue
            result.append(pairs[i][2]);i+=1
        with cnc.open('xb') as f:f.write(b''.join(result))
        written=inspect(cnc);outpairs,outrows=dx.read(cnc);by_handle={dx.one(row,5):row for row in outrows}
        r['layerTablesUnchanged']=structure['layers']==written['layers']
        r['linetypeTablesUnchanged']=structure['linetypes']==written['linetypes']
        r['headerExceptHandseedUnchanged']={k:v for k,v in structure['headers'].items() if k!='$HANDSEED'}=={k:v for k,v in written['headers'].items() if k!='$HANDSEED'}
        if not all(r[k] for k in ('layerTablesUnchanged','linetypeTablesUnchanged','headerExceptHandseedUnchanged')):raise core.ReviewRequired('Unexpected table/header change')
        r['outputEntityCounts']=dict(collections.Counter(dx.kind(row) for row in outrows))
        r['unchangedEntityRecords']=[]
        modified_handles={s['sourceHandle'] for s in r['splines'] if 'pieces' in s}
        for row in rows:
            handle=dx.one(row,5)
            if handle in modified_handles:continue
            a,b=dx.rawrow(pairs,row),dx.rawrow(outpairs,by_handle[handle])
            if a!=b:raise core.ReviewRequired('Unrequested entity change: '+handle)
            r['unchangedEntityRecords'].append(dict(handle=handle,type=dx.kind(row),layer=dx.one(row,8,'0'),sha256=hashlib.sha256(b).hexdigest().upper()))
        r['existingAnalyticPrimitivesBytePreserved']=True
        r['bendValidation']=dict(originalEntityCount=sum(dx.one(row,8,'0').upper() in ('UP','DOWN') for row in rows),unchangedRecords=sum(p['layer'].upper() in ('UP','DOWN') for p in r['unchangedEntityRecords']),convertedSplines=sum(s.get('role')=='BEND' and 'pieces' in s for s in r['splines']),physicalDirectionCertified=False)
        # Check every new entity's inherited style and its actual serialized geometry.
        cc.PRECISION=80;cc.trig.cache_clear();cc.pi_interval.cache_clear()
        try:
            for obj in r['splines']:
                if 'pieces' not in obj:continue
                for p in obj['pieces']:
                    if style(by_handle[p['outputHandle']])!=obj['attributes']:raise core.ReviewRequired('Style mismatch')
                obj['writtenCurveVerification']=check_written(obj['pieces'],by_handle,options)
        finally:cc.PRECISION=55;cc.trig.cache_clear();cc.pi_interval.cache_clear()
        cut=[row for row in rows if dx.one(row,8,'0') in cut_layers]
        outcut=[row for row in outrows if dx.one(row,8,'0') in cut_layers]
        r['cutSplineCountRemaining']=sum(dx.kind(row)=='SPLINE' for row in outcut)
        r['allSplineCountRemaining']=sum(dx.kind(row)=='SPLINE' for row in outrows)
        if cut:
            sg,_,_=dx.graph(cut);og,nodes,edges=dx.graph(outcut)
            r['sourceTopology']=sg;r['outputTopology']=og
            srcsets=sorted([sorted(dx.one(cut[j],5) for j in component) for component in sg['components']])
            outsets=sorted([sorted(set(parent[dx.one(outcut[j],5)] for j in component)) for component in og['components']])
            r['componentCorrespondencePreserved']=srcsets==outsets
            if not r['cutSplineCountRemaining']:
                # Bound work before quadratic pair enumeration.
                r['intersectionCheck']=topology_pairs(outcut,nodes,edges,seconds=options.max_seconds)
                if not r['intersectionCheck']['completed']:r['unverifiedConditions'].append('Topology time/pair budget reached; partial exclusion is not PASS')
                r['unverifiedConditions'].append('Exact zero-gap closure inside 0.000001 mm endpoint contact zones not certified')
            else:r['unverifiedConditions'].append('Unconverted CUT SPLINE prevents complete output primitive topology verification')
            if not sg['closedDegreeTwo'] or not og['closedDegreeTwo']:r['unverifiedConditions'].append('Open or branched CUT contour')
            if srcsets!=outsets:r['unverifiedConditions'].append('Closed-component source correspondence differs')
            if r.get('intersectionCheck',{}).get('unresolved'):r['unverifiedConditions'].append('Possible intersections/overlaps: interval exclusion incomplete')
        else:r['unverifiedConditions'].append('No CUT entities in explicitly selected CUT layers')
        r['unverifiedConditions']+=['Hole nesting/isotopy not certified','Exact G1 not certified; joint angles are diagnostics','AutoCAD/CNC application inspection not performed']
        for obj in r['splines']:
            obj['topologyVerification']=dict(componentCorrespondencePreserved=r.get('componentCorrespondencePreserved'),closedWithinJoinTolerance=r.get('outputTopology',{}).get('closedDegreeTwo'),pairExclusionOutsideJoinTolerance=r.get('intersectionCheck',{}).get('allPairsExcludedOutsideJoinTolerance'),holeNestingCertified=False,exactZeroGapCertified=False)
        r['allConvertedSplineBoundsVerified']=all(s.get('deviationBoundVerified') and 'writtenCurveVerification' in s for s in r['splines'])
        r['maximumWholeCurveBoundMm']=str(max((D(s['writtenCurveVerification']['wholeCurveTwoSidedBoundMm']) for s in r['splines'] if 'writtenCurveVerification' in s),default=D(0)))
        r['geometryBoundWithinTolerance']=r['allConvertedSplineBoundsVerified'] and r['allSplineCountRemaining']==0
        if cut and not r['cutSplineCountRemaining']:
            import topology_certification
            import junction_checks,dxf_audit
            r['topologyComparison']=topology_certification.compare(cut,outcut,parent,r['maximumWholeCurveBoundMm'],options.max_seconds,geometry_correspondence_verified=r['geometryBoundWithinTolerance'] and r['existingAnalyticPrimitivesBytePreserved'])
            r['junctionVerification']=junction_checks.check(cut,outcut,r['splines'])
            r['structuralDxfAudit']=dxf_audit.audit(cnc)
            if r['topologyComparison'].get('verifiedAtJoinTolerance'):
                r['unverifiedConditions']=[s for s in r['unverifiedConditions'] if s!='Hole nesting/isotopy not certified']
                r['unverifiedConditions'].append('Topology verified at explicit join tolerance; strict zero-gap closure and sub-tolerance contacts not certified')
            for obj in r['splines']:
                obj['topologyVerification']['hierarchyPreserved']=r['topologyComparison'].get('nestingPreserved',False)
                obj['topologyVerification']['holeCountPreserved']=r['topologyComparison'].get('holeCountPreserved',False)
                obj['topologyVerification']['holeNestingCertifiedAtJoinTolerance']=r['topologyComparison'].get('nestingPreserved',False)
            r['qualityChecksPassedAtDeclaredTolerances']=all((r['geometryBoundWithinTolerance'],r['topologyComparison'].get('verifiedAtJoinTolerance',False),r['junctionVerification']['allSmoothJunctionsWithinAngularTolerance'],r['junctionVerification']['originalCornersPreservedWithinAngularTolerance'],r['structuralDxfAudit']['status']=='STRUCTURAL_AUDIT_PASS'))
    except Exception as e:r['fileErrors'].append(safe_error(e))
    finally:
        r['sourceUnchanged']=dx.sha(source)==before
        if not r['sourceUnchanged']:r['fileErrors'].append(dict(message='Source changed during processing'))
        r['cncExists']=cnc.exists()
        if cnc.exists():r['cncSha256']=dx.sha(cnc)
        r['elapsedSeconds']=time.perf_counter()-started
    return r

def discover(inputs):
    files=[]
    for s in inputs:
        p=Path(s)
        if p.is_dir():files.extend(q for q in p.rglob('*') if q.is_file() and q.suffix.lower()=='.dxf')
        elif p.is_file() and p.suffix.lower()=='.dxf':files.append(p)
        else:raise ValueError('DXF file/directory not found: '+str(p))
    return sorted({p.resolve() for p in files},key=lambda p:str(p).casefold())

def output_names(source,reserved_exact,reserved_cnc):
    suffix=hashlib.sha256(str(source).encode('utf-8')).hexdigest()[:12]
    name=source.name
    if name.casefold() in reserved_exact:name=source.stem+'_'+suffix+source.suffix
    if name.casefold() in reserved_exact:raise ValueError('Unresolved exact filename collision')
    reserved_exact.add(name.casefold())
    stem=Path(name).stem
    if stem.upper().endswith(('-EXACT','_EXACT')):stem=stem[:-6]
    cnc=stem+'-CNC.dxf'
    if cnc.casefold() in reserved_cnc:cnc=stem+'_'+suffix+'-CNC.dxf'
    if cnc.casefold() in reserved_cnc:raise ValueError('Unresolved CNC filename collision')
    reserved_cnc.add(cnc.casefold())
    return name,cnc

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--input',action='append',required=True,help='Repeatable file or directory; directory scan is recursive')
    ap.add_argument('--output',required=True,help='New output folder, must not exist')
    ap.add_argument('--tolerance-mm',default='0.01');ap.add_argument('--max-segments',type=int,default=2048)
    ap.add_argument('--max-depth',type=int,default=24);ap.add_argument('--max-candidates',type=int,default=12000)
    ap.add_argument('--max-seconds',type=float,default=60);ap.add_argument('--no-merge',action='store_true')
    ap.add_argument('--cut-layer',action='append',help='Repeatable exact layer name; default 0')
    ap.add_argument('--convert-bend-splines',action='store_true',help='Review-only geometric proposal; native bend-purpose verification still mandatory')
    args=ap.parse_args();options=core.Options(tolerance_mm=D(args.tolerance_mm),max_segments=args.max_segments,max_depth=args.max_depth,max_candidates=args.max_candidates,max_seconds=args.max_seconds,merge=not args.no_merge);options.validate()
    files=discover(args.input)
    if not files:raise ValueError('No input DXF')
    out=Path(args.output).resolve()
    if any(out==p or out in p.parents for p in files):raise ValueError('Output must not contain an input')
    # Prevent future recursive scans from consuming prior outputs by accident.
    if any(Path(s).is_dir() and (Path(s).resolve()==out or Path(s).resolve() in out.parents) for s in args.input):raise ValueError('Output must be outside recursively scanned input directories')
    out.mkdir(parents=True,exist_ok=False)
    for d in ('EXACT','CNC','Reports'):(out/d).mkdir()
    report=dict(program='SplineArcModuleV03',version='0.3',referencePolicy='Only native EXACT DXF; no CAM polyline fitting',options=dataclasses.asdict(options),files=[],productionReady=False,status='NEEDS_REVIEW',COMCallsMade=0)
    reserved=set();reserved_cnc=set()
    for i,source in enumerate(files):
        name,cncname=output_names(source,reserved,reserved_cnc)
        print(f'[{i+1}/{len(files)}] {source.name}',flush=True)
        item=process_file(source,out/'EXACT'/name,out/'CNC'/cncname,options,tuple(args.cut_layer or ['0']),args.convert_bend_splines)
        report['files'].append(item);save_json(out/'Reports'/(Path(name).stem+'.json'),item);save_json(out/'Reports'/'batch-report.json',report)
        print(json.dumps({k:item.get(k) for k in ('status','maximumWholeCurveBoundMm','allSplineCountRemaining','elapsedSeconds')}) ,flush=True)
    report['fileCount']=len(report['files']);report['splineCount']=sum(len(f['splines']) for f in report['files']);report['sourceFilesUnchanged']=all(f['sourceUnchanged'] for f in report['files'])
    report['filesWithAllSplineBoundsVerified']=sum(bool(f.get('geometryBoundWithinTolerance')) for f in report['files'])
    save_json(out/'Reports'/'batch-report.json',report)
    return 2  # Files are review candidates until the explicit outstanding conditions are certified.

if __name__=='__main__':raise SystemExit(main())
