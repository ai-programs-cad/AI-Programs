"""ManufacturingPackage post-export stage; no SolidWorks calls or original rewrites."""
import argparse, collections, csv, json, shutil, sys, time
from pathlib import Path
from decimal import Decimal as D
import spline_arc_batch as batch
import spline_arc_core as core
import dxf_support as dx
import dxf_audit

def display_resolution(path):
    """Change only existing *ACTIVE VPORT group 72, not geometry/header inventions."""
    pairs,rows=dx.read(path); replacements={}; changes=[]
    section=None; start=0
    for i,(code,value,raw) in enumerate(pairs+[(0,'EOF',b'')]):
        if code==0:
            record=pairs[start:i]
            if section=='TABLES' and record and record[0][1]=='VPORT' and any(k==2 and v.upper()=='*ACTIVE' for k,v,_ in record):
                indices=[start+j for j,(k,v,_) in enumerate(record) if k==72]
                if len(indices)!=1:raise ValueError('Active VPORT has no unique group 72; no speculative reconstruction')
                j=indices[0];before=pairs[j][1];old=pairs[j][2];lines=old.splitlines(keepends=True)
                ending=b'\r\n' if lines[1].endswith(b'\r\n') else b'\n'
                replacements[j]=lines[0]+b'20000'+ending
                changes.append(dict(group=72,before=before,after=20000))
            if value=='SECTION':section=pairs[i+1][1]
            elif value=='ENDSEC':section=None
            start=i
    if not changes:raise ValueError('No existing active VPORT: display setting not applied')
    entity_before=[dx.rawrow(pairs,r) for r in rows]
    path.write_bytes(b''.join(replacements.get(i,p[2]) for i,p in enumerate(pairs)))
    after,arows=dx.read(path)
    assert entity_before==[dx.rawrow(after,r) for r in arows], 'Display metadata changed entities'
    return dict(viewres=20000,storage='Existing active VPORT table group 72',changes=changes,entityBytesUnchanged=True,nativeAutoCADReadbackVerified=False,regenallExecuted=False,lineSmoothingStorage='AutoCAD profile/registry; not DXF geometry')

def preserved_bends(source,output):
    def selected(p):
        pairs,rows=dx.read(p)
        return {dx.one(r,5):dx.rawrow(pairs,r) for r in rows if dx.one(r,8,'').upper() in ('UP','DOWN')}
    a,b=selected(source),selected(output)
    return dict(count=len(a),byteIdentical=a==b,physicalDirectionsIndependentlyVerified=False)

CAM_APPROXIMATION_TOLERANCE_MM='0.01'

def run(inputs,output,tolerance=CAM_APPROXIMATION_TOLERANCE_MM,max_seconds=180,lineage_report=None):
    options=core.Options(tolerance_mm=D(tolerance),max_seconds=max_seconds);options.validate()
    files=batch.discover(inputs);out=Path(output).resolve();lineage={}
    if lineage_report:
        manifest=json.loads(Path(lineage_report).read_text(encoding='utf-8-sig'))
        for item in manifest['files']:
            native,derived=Path(item['source']),Path(item['outputDxf'])
            if dx.sha(native)!=item['sourceSha256'] or dx.sha(derived)!=item['derivedSha256']:raise ValueError('Model Space lineage input SHA-256 mismatch')
            proof=item['sourceComparison']
            if not proof['passed'] or not proof['allNonSpaceEntityTagsByteIdentical'] or D(str(proof['wholeCurveBoundMm']))!=0:raise ValueError('Nonzero or unverified Model Space transformation')
            lineage[str(derived.resolve()).casefold()]=item
        if any(str(p).casefold() not in lineage for p in files):raise ValueError('Input missing from verified Model Space lineage')
    if not files:raise ValueError('No EXACT files')
    if any(out==p or out in p.parents for p in files):raise ValueError('Output overlaps inputs')
    if any(Path(p).is_dir() and Path(p).resolve() in out.parents for p in inputs):raise ValueError('Output must be outside recursive input tree')
    out.mkdir(parents=True,exist_ok=False)
    for name in ('EXACT','CAM_ARCS','Reports','MODELSPACE_REFERENCE'):(out/name).mkdir()
    report=dict(stage='ManufacturingPackage EXACT -> CAM_ARCS',CAM_APPROXIMATION_TOLERANCE_MM=str(options.tolerance_mm),toleranceMm=str(options.tolerance_mm),optimizationObjective='Fewest whole-curve-certified elements among evaluated candidates; endpoints and topology mandatory, smaller smooth-junction angle is a tie-breaker. Preferred G1 is separately reported, never silently certified.',globalMinimumProven=False,productionReady=False,status='NEEDS_REVIEW',files=[],COMCallsMade=0)
    reserved=set();cnames=set();started=time.perf_counter()
    for index,source in enumerate(files):
        name,cnc=batch.output_names(source,reserved,cnames);cnc=cnc.replace('-CNC.dxf','.dxf')
        target=out/'CAM_ARCS'/cnc
        print(f'[{index+1}/{len(files)}] CAM_ARCS {source.name}',flush=True)
        ancestor=lineage.get(str(source).casefold())
        r=batch.process_file(source,out/('MODELSPACE_REFERENCE' if ancestor else 'EXACT')/name,target,options)
        if ancestor:
            native=Path(ancestor['source']);shutil.copy2(native,out/'EXACT'/name)
            r['nativeExactReference']=dict(source=str(native),immutableCopy=str(out/'EXACT'/name),sha256=dx.sha(native),modelSpaceDerivationWholeCurveBoundMm=0,nativeSourceUnchanged=dx.sha(native)==ancestor['sourceSha256'])
            r['controlComparisonEvidence']=ancestor.get('controlComparison')
        if target.exists():
            try:r['display']=display_resolution(target)
            except Exception as e:r['display']=dict(error=str(e),nativeAutoCADReadbackVerified=False)
            r['bendPreservation']=preserved_bends(source,target)
            r['structuralDxfAudit']=dxf_audit.audit(target)
            r['cncSha256']=dx.sha(target)
            pairs,rows=dx.read(target)
            r['cutEntityCounts']=dict(collections.Counter(dx.kind(row) for row in rows if dx.one(row,8,'0')=='0'))
            r['allEntityCounts']=dict(collections.Counter(dx.kind(row) for row in rows))
            r['qualityChecksPassedAtDeclaredTolerances']=bool(r.get('qualityChecksPassedAtDeclaredTolerances') and r['bendPreservation']['byteIdentical'] and r['structuralDxfAudit']['status']=='STRUCTURAL_AUDIT_PASS')
            r['geometryAndTopologyChecksPassed']=bool(r.get('geometryBoundWithinTolerance') and r.get('topologyComparison',{}).get('verifiedAtJoinTolerance') and r['bendPreservation']['byteIdentical'] and r['structuralDxfAudit']['status']=='STRUCTURAL_AUDIT_PASS')
            r['preferredG1Satisfied']=r.get('junctionVerification',{}).get('allSmoothJunctionsWithinAngularTolerance',False)
            if not r['preferredG1Satisfied']:r['unverifiedConditions'].append('Preferred G1 junction criterion not met by lowest-count candidate; see rigorous angular bounds. No production PASS.')
        report['files'].append(r)
        batch.save_json(out/'Reports'/(Path(cnc).stem+'.json'),r)
        batch.save_json(out/'Reports'/'cam-arcs-report.json',report)
    report.update(fileCount=len(files),splineCount=sum(len(r['splines']) for r in report['files']),sourceFilesUnchanged=all(r['sourceUnchanged'] for r in report['files']),elapsedSeconds=time.perf_counter()-started,nativeAutoCADOpenVerified=False,targetCamImportVerified=False)
    batch.save_json(out/'Reports'/'cam-arcs-report.json',report)
    with (out/'Reports'/'comparison.csv').open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.writer(f,delimiter=';');w.writerow(['Part','SPLINE','CUT LINE','CUT ARC','CUT CIRCLE','Bound mm','Closed contours','Holes','Bends unchanged','Quality checks','Status'])
        for r in report['files']:
            c=r.get('cutEntityCounts',{});t=r.get('outputTopology',{});h=r.get('topologyComparison',{}).get('outputHierarchy',{})
            w.writerow([Path(r['source']).stem,len(r['splines']),c.get('LINE',0),c.get('ARC',0),c.get('CIRCLE',0),r.get('maximumWholeCurveBoundMm'),len(t.get('components',[])),h.get('holeCount'),r.get('bendPreservation',{}).get('byteIdentical'),r.get('qualityChecksPassedAtDeclaredTolerances'),r['status']])
    return report

def main():
    sys.stdout.reconfigure(encoding='utf-8');p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--input',action='append',required=True);p.add_argument('--output',required=True);p.add_argument('--tolerance-mm',default=CAM_APPROXIMATION_TOLERANCE_MM);p.add_argument('--max-seconds',type=float,default=180);p.add_argument('--lineage-report')
    a=p.parse_args();run(a.input,a.output,a.tolerance_mm,a.max_seconds,a.lineage_report);return 2
if __name__=='__main__':raise SystemExit(main())
