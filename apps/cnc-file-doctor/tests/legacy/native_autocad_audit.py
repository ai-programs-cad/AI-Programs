"""Native read-only AutoCAD Core Console AUDIT _N on disposable DXF copies.
Never JOIN/OVERKILL/RECOVER/SAVE. No user-profile edits; documented /isolate.
"""
import argparse, json, re, shutil, subprocess, time
from pathlib import Path
import dxf_support as dx
from decimal import Decimal as D
from fractions import Fraction as F

def decode(raw):
    if raw[:2] in (b'\xff\xfe',b'\xfe\xff'):return raw.decode('utf-16',errors='replace')
    if raw.count(b'\0')>len(raw)//6:return raw.decode('utf-16-le',errors='replace')
    return raw.decode('utf-8',errors='replace')

def make_script(path,snapshot):
    # Entget snapshot is additional evidence; original DXF remains the geometry
    # reference. AUDIT No is a database check, not a CAM certificate.
    dest=str(snapshot).replace('\\','/').replace('"','\\"')
    text=f'''(princ "\\nCNC_NATIVE_AUDIT_BEGIN\\n")
(command "_.AUDIT" "_N")
(princ "\\nCNC_NATIVE_AUDIT_DONE\\n")
(setq cncOut (open "{dest}" "w" "utf8"))
(write-line (strcat "INSUNITS=" (itoa (getvar "INSUNITS"))) cncOut)
(defun cncNum (v) (if (numberp v) (rtos v 2 15) "NA"))
(defun cncCoord (code axis) (cncNum (nth axis (cdr (assoc code cncEntity)))))
(defun cncNormal (axis) (if (assoc 210 cncEntity) (cncCoord 210 axis) (if (= axis 2) "1" "0")))
(defun cncDeg (code) (if (assoc code cncEntity) (cncNum (* (/ 180.0 pi) (cdr (assoc code cncEntity)))) "NA"))
(setq cncSet (ssget "_X" '((410 . "Model"))) cncIndex 0)
(if cncSet (repeat (sslength cncSet) (setq cncEntity (entget (ssname cncSet cncIndex)) cncIndex (1+ cncIndex)) (write-line (vl-prin1-to-string cncEntity) cncOut) (write-line (strcat "GEOMETRY|" (cdr (assoc 5 cncEntity)) "|" (cdr (assoc 0 cncEntity)) "|" (cdr (assoc 8 cncEntity)) "|" (cncCoord 10 0) "|" (cncCoord 10 1) "|" (cncCoord 10 2) "|" (cncCoord 11 0) "|" (cncCoord 11 1) "|" (cncCoord 11 2) "|" (cncNum (cdr (assoc 40 cncEntity))) "|" (cncDeg 50) "|" (cncDeg 51) "|" (cncNormal 0) "|" (cncNormal 1) "|" (cncNormal 2)) cncOut)))
(close cncOut)
(princ "\\nCNC_NATIVE_SNAPSHOT_DONE\\n")
(command "_.QUIT" "_N")
'''
    path.write_text(text,encoding='utf-8')

def coordinate_check(text,source):
    actual=[line.split('|') for line in text.splitlines() if line.startswith('GEOMETRY|')]
    _,rows=dx.read(source);remaining=set(range(len(actual)));maximum=F(0);whole_bound=D(0)
    for row in rows:
        kind=dx.kind(row);layer=dx.one(row,8,'0');found=None
        for i in remaining:
            a=actual[i]
            if len(a)!=16 or a[2]!=kind or a[3]!=layer:continue
            try:
                if tuple(map(F,a[13:16]))!=(F(0),F(0),F(1)):continue
            except Exception:continue
            pairs=[(4,10),(5,20),(6,30)]+([(7,11),(8,21),(9,31)] if kind=='LINE' else [(10,40)])
            try:errors=[abs(F(a[n])-F(dx.one(row,code,'0'))) for n,code in pairs]
            except Exception:continue
            if kind=='LINE':shape_bound=dx.cc.I(max(sum(errors[:3]),sum(errors[3:6]))).hi
            else:shape_bound=dx.cc.I(sum(errors[:3])+errors[3]).hi
            if kind=='ARC':
                source_start,source_end=F(dx.one(row,50)),F(dx.one(row,51));native_start,native_end=F(a[11]),F(a[12])
                signed_start=(native_start-source_start+180)%360-180
                source_sweep=(source_end-source_start)%360;native_sweep=(native_end-native_start)%360
                angular=max(abs(signed_start),abs(signed_start+native_sweep-source_sweep))
                radius=max(abs(F(a[10])),abs(F(dx.one(row,40))))
                arc_factor=dx.cc.I(radius*angular).hi
                shape_bound=dx.cc.rnd(lambda:shape_bound+arc_factor*dx.cc.pi_interval().hi/D(180),True)
                for n,code in ((11,50),(12,51)):
                    difference=abs((F(a[n])-F(dx.one(row,code)))%360);errors.append(min(difference,360-difference))
            if max(errors,default=F(0))<=F(1,10**7) and shape_bound<=D('.0000001'):found=i;maximum=max(maximum,max(errors,default=F(0)));whole_bound=max(whole_bound,shape_bound);break
        if found is None:return dict(verified=False,reason='Native type/layer/coordinate correspondence absent',sourceHandle=dx.one(row,5))
        remaining.remove(found)
    return dict(verified=not remaining,matchedEntities=len(rows),maximumNumericParameterDifference=str(dx.cc.I(maximum).hi),nativeWholeCurveUpperBoundMm=str(whole_bound),coordinateToleranceMm='0.0000001',angularToleranceDegrees='0.0000001',method='Native entget coordinates, ARC radians converted to degrees; bijective parameter matching and full primitive bound including radius-scaled angular error, no rescaling')

def audit_files(files,output,executable,timeout=90,isolate=True):
    output=Path(output);output.mkdir(parents=True,exist_ok=False)
    results=[];environment_failure=None
    for source in files:
        source=Path(source);item=dict(source=str(source),sourceSha256=dx.sha(source),status='NOT_RUN',fixErrors=False,geometryChangingCommandsUsed=False)
        if environment_failure:
            item['reason']=environment_failure;results.append(item);continue
        folder=output/source.stem;folder.mkdir();copy=folder/source.name;shutil.copyfile(source,copy)
        script=folder/'audit.scr';snapshot=folder/'native-entities.txt';make_script(script,snapshot)
        profile=folder/'IsolatedProfile';profile.mkdir()
        command=[str(executable),'/i',str(copy),'/s',str(script),'/l','en-US','/readonly']
        if isolate:command+=['/isolate','CncAuditV03',str(profile)]
        item['command']=command;start=time.monotonic()
        try:
            run=subprocess.run(command,cwd=folder,capture_output=True,timeout=timeout)
            log=decode(run.stdout)+decode(run.stderr);(folder/'console.txt').write_text(log,encoding='utf-8')
            item['exitCode']=run.returncode;item['log']=str(folder/'console.txt')
            complete='CNC_NATIVE_AUDIT_DONE' in log and 'CNC_NATIVE_SNAPSHOT_DONE' in log and snapshot.exists()
            errors=re.findall(r'Total errors found\s+(\d+)\s+fixed\s+(\d+)',log,re.I)
            item['auditComplete']=complete
            if complete and errors:
                item['errorsFound'],item['errorsFixed']=map(int,errors[-1]);item['status']='NATIVE_AUDIT_PASS' if item['errorsFound']==0 and item['errorsFixed']==0 else 'NEEDS_REVIEW'
            elif complete:item['status']='NEEDS_REVIEW';item['reason']='Native audit ran but summary could not be parsed'
            else:
                item['status']='NATIVE_AUDIT_NOT_COMPLETED';item['reason']='Native engine did not complete AUDIT/snapshot; see console log'
                if ('profile' in log.lower() and ('fatal' in log.lower() or 'Problem with setting up current profile' in log)) or "Can't open HKCU" in log or 'Failed to create registry keys' in log:environment_failure='Native AutoCAD profile/HKCU initialization failed in execution environment; no file-specific audit was completed'
                if 'license' in log.lower() and not complete:environment_failure='Native AutoCAD licensing/initialization did not complete in execution environment'
            if snapshot.exists():
                text=snapshot.read_text(encoding='utf-8-sig');types=re.findall(r'\(0 \. "([A-Z0-9_]+)"\)',text)
                from collections import Counter
                item['nativeModelSpaceEntityCounts']=dict(Counter(types));item['nativeUnitsMm']='INSUNITS=4' in text
                _,rows=dx.read(source);expected=dict(Counter(dx.kind(row) for row in rows))
                item['nativeTypesMatchDxf']=item['nativeModelSpaceEntityCounts']==expected
                if not item['nativeTypesMatchDxf'] or not item['nativeUnitsMm']:item['status']='NEEDS_REVIEW'
                item['coordinateVerification']=coordinate_check(text,source)
                if not item['coordinateVerification']['verified']:item['status']='NEEDS_REVIEW'
                item['snapshot']=str(snapshot)
        except (OSError,subprocess.TimeoutExpired) as e:item['status']='NATIVE_AUDIT_NOT_COMPLETED';item['reason']=type(e).__name__+': '+str(e);environment_failure=item['reason']
        item['elapsedSeconds']=time.monotonic()-start;item['sourceUnchanged']=dx.sha(source)==item['sourceSha256'];item['disposableCopyUnchanged']=dx.sha(copy)==item['sourceSha256']
        if not item['sourceUnchanged'] or not item['disposableCopyUnchanged']:item['status']='NEEDS_REVIEW'
        results.append(item)
    report=dict(nativeEngine=str(executable),files=results,productionReady=False,camImportTested=False)
    (output/'native-audit-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    return report

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--dxf-dir',required=True);p.add_argument('--output',required=True);p.add_argument('--acad',default=r'C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe');p.add_argument('--user-profile',action='store_true',help='For explicit local user-session execution; omit /isolate, retain /readonly and AUDIT No');a=p.parse_args()
    r=audit_files(sorted(Path(a.dxf_dir).glob('*.dxf')),a.output,a.acad,isolate=not a.user_profile)
    print(json.dumps([dict(file=x['source'],status=x['status'],reason=x.get('reason')) for x in r['files']],ensure_ascii=False))
    raise SystemExit(0 if r['files'] and all(x['status']=='NATIVE_AUDIT_PASS' for x in r['files']) else 2)
