"""CNC File Doctor: offline inspection and isolated certified curve conversion."""
import argparse, collections, datetime, hashlib, html, json, math, os
from pathlib import Path
import subprocess, sys, time
ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'engine'))
VERSION = '0.7.0'
MAX_BYTES = 100_000_000
class OutputFolderExists(FileExistsError):pass

def digest(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def scan(path,cut_layers=('0',)):
    path = Path(path)
    if path.stat().st_size > MAX_BYTES:
        raise ValueError('Input exceeds 100 MB limit')
    raw = path.read_bytes()
    if raw.startswith(b'AutoCAD Binary DXF'):
        raise ValueError('Binary DXF is not supported; export ASCII DXF')
    lines = raw.splitlines()
    if len(lines) % 2:
        raise ValueError('Incomplete DXF group/value pair')
    pairs = [(int(lines[i].strip()), lines[i+1].decode('utf-8-sig', errors='replace').strip()) for i in range(0, len(lines), 2)]
    if not pairs or pairs[-1] != (0, 'EOF'):
        raise ValueError('Missing final EOF record')
    records=[]; section=None; row=None; units=None; issues=[]; sections=[]
    for i,(code,value) in enumerate(pairs):
        if (code,value)==(0,'SECTION'):
            if section is not None or i+1>=len(pairs) or pairs[i+1][0]!=2:
                raise ValueError('Invalid SECTION structure')
            section=pairs[i+1][1]; sections.append(section); row=None
        elif (code,value)==(0,'ENDSEC'):
            if section is None: raise ValueError('Unexpected ENDSEC')
            section=None; row=None
        elif code==0:
            row={'type':value,'tags':[],'section':section}
            if section in ('ENTITIES','BLOCKS'): records.append(row)
        elif row is not None: row['tags'].append((code,value))
        if section=='HEADER' and (code,value)==(9,'$INSUNITS') and i+1<len(pairs): units=pairs[i+1][1]
    if section is not None: raise ValueError('Unclosed SECTION')
    if 'ENTITIES' not in sections: issues.append('Missing ENTITIES section')
    counts=collections.Counter(r['type'] for r in records if r['section']=='ENTITIES')
    entities=[r for r in records if r['section']=='ENTITIES']
    def one(r,k,d=None): return next((v for c,v in r['tags'] if c==k),d)
    handles=[v.upper() for k,v in pairs if k in (5,105) and v!='0']
    dup=[h for h,n in collections.Counter(handles).items() if n>1]
    if dup: issues.append('Duplicate handles: '+', '.join(dup[:12]))
    if units!='4': issues.append('Units are not explicitly millimetres; conversion is blocked')
    unsupported=sorted(set(counts)-{'LINE','ARC','CIRCLE','SPLINE','LWPOLYLINE'})
    if unsupported: issues.append('Conversion does not support: '+', '.join(unsupported))
    geometry=[]; seen=set(); duplicates=0; open_nodes=collections.Counter()
    for r in entities:
        typ=r['type']; tag=r['tags']; handle=one(r,5,'?')
        nums={}
        for k,v in tag:
            if 10<=k<=59:
                n=float(v)
                if not math.isfinite(n): raise ValueError('Nonfinite coordinate at handle '+handle)
                nums[k]=n
        if any(abs(nums.get(k,0))>1e-12 for k in range(30,40)):
            issues.append('Nonplanar entity '+handle)
        signature=(typ,tuple((k,v) for k,v in tag if k not in (5,330)))
        if signature in seen: duplicates+=1
        seen.add(signature)
        g={'type':typ,'handle':handle,'layer':one(r,8,'0'),'n':nums}
        if typ=='LWPOLYLINE':
            from polyline_adapter import vertices
            try:
                for a,b in vertices({'pairs':[(0,typ)]+tag}):
                    geometry.append(dict(type='LINE',handle=handle,layer=g['layer'],n={10:float(a[0]),20:float(a[1]),11:float(b[0]),21:float(b[1])}))
            except ValueError as e:issues.append(str(e))
        if typ=='LINE':
            a=(nums.get(10,0),nums.get(20,0)); b=(nums.get(11,0),nums.get(21,0))
            if a==b: issues.append('Zero-length LINE '+handle)
            for pt in (a,b): open_nodes[tuple(round(v,6) for v in pt)]+=1
        if typ in ('ARC','CIRCLE') and nums.get(40,0)<=0: issues.append('Invalid radius '+handle)
        if typ=='ARC':
            if (nums.get(51,0)-nums.get(50,0))%360==0: issues.append('Zero-sweep ARC '+handle)
            for angle in (nums.get(50,0),nums.get(51,0)):
                t=math.radians(angle); pt=(nums.get(10,0)+nums.get(40,0)*math.cos(t),nums.get(20,0)+nums.get(40,0)*math.sin(t))
                open_nodes[tuple(round(v,6) for v in pt)]+=1
        if typ in ('LINE','ARC','CIRCLE'): geometry.append(g)
    if duplicates: issues.append(str(duplicates)+' duplicate entity records (not removed automatically)')
    if not entities: issues.append('No drawing entities')
    if len(geometry)>20000: issues.append('Preview limited to first 20000 analytic entities')
    try:
        from preview_geometry import build
        preview=build(path,cut_layers);geometry=preview['geometry'];diagnostics=preview['diagnostics']
        if any(marker['code'] in ('OPEN_ENDPOINT','BRANCH') for marker in diagnostics['markers']):issues.append('Open or branched CUT contour at 0.001 mm closure tolerance')
    except (ValueError,KeyError,ArithmeticError):diagnostics=dict(markers=[],partial=True,closureChecked=False)
    issues=list(dict.fromkeys(issues))
    issues_ru=['Разомкнутый или разветвлённый контур резки при допуске замыкания 0,001 мм.' if issue.startswith('Open or branched CUT') else 'Дуговые полилинии показаны полностью, но их преобразование в CAM_ARCS пока не поддерживается.' if issue=='Curved LWPOLYLINE bulge is not supported yet' else 'Найдено замечание к структуре или геометрии; см. подробный отчёт.' for issue in issues]
    return dict(source=str(path.resolve()),sha256=hashlib.sha256(raw).hexdigest(),bytes=len(raw),unitsCode=units,entityCounts=dict(counts),layers=sorted({one(r,8,'0') for r in entities}),issues=issues,issuesRu=issues_ru,duplicateRecords=duplicates,unmatchedAnalyticEndpoints=sum(n==1 for n in open_nodes.values()),endpointDiagnostic='Rounded to 0.000001 drawing units; excludes splines, blocks, polylines and layer roles; not closure certification',geometry=geometry[:20000],diagnostics=diagnostics,status='ISSUES_FOUND' if issues else 'INSPECTED',productionReady=False)

def write_json(path,obj):
    Path(path).write_text(json.dumps(obj,ensure_ascii=False,indent=2,default=str),encoding='utf-8')

def worker(source, exact, target, report, tolerance, layers):
    import spline_arc_batch as batch, spline_arc_core as core
    from decimal import Decimal
    options=core.Options(tolerance_mm=Decimal(tolerance),max_seconds=30); options.validate()
    from polyline_adapter import normalize
    source,exact,target,report=map(Path,(source,exact,target,report))
    normalized=report.with_suffix('.normalized.dxf')
    proof=normalize(source,normalized,tuple(layers))
    if proof:
        with exact.open('xb') as f:f.write(source.read_bytes())
        reference=report.with_suffix('.normalized-exact.dxf')
        result=batch.process_file(normalized,reference,target,options,tuple(layers))
        result['normalization']=proof
        result['normalizedReference']=str(reference)
        result['source']=str(source);result['exact']=str(exact)
        result['sourceSha256']=digest(source);result['exactCopySha256']=digest(exact)
        result['sourceUnchanged']=digest(source)==proof['sourceSha256'].lower()
    else:result=batch.process_file(source,exact,target,options,tuple(layers))
    write_json(report,result)

def friendly_findings(item,ru=True):
    diagnosis=item.get('diagnosis',{}); conversion=item.get('conversion',{})
    status=item.get('status'); findings=[]
    if item.get('errors'):findings.append('Не удалось обработать файл. Подробная причина указана ниже.' if ru else 'Processing failed. See technical details below.')
    if item.get('contourJoin',{}).get('verified'):
        joined=item['contourJoin']
        findings.append((f"Объединено в замкнутые полилинии: {joined['closedPolylineCount']}. Дуги сохранены. Готовый файл: " if ru else f"Joined into {joined['closedPolylineCount']} closed polylines; arcs preserved. Ready file: ")+Path(joined['path']).name)
    elif item.get('joinError'):findings.append(('Объединение в полилинии не выполнено: ' if ru else 'Polyline joining was not completed: ')+item['joinError'])
    if status in ('BLOCKED','BLOCKED_REVIEW'):
        findings.append('Преобразование не подтверждено. Результат не допускается к использованию без дополнительной проверки.' if ru else 'Conversion is unverified. Review is required before use.')
    if status=='CHECKED_CANDIDATE_REVIEW_REQUIRED':
        findings.append('Реализованные проверки пройдены. Перед резкой проверьте файл в вашей CAM-системе.' if ru else 'Implemented checks passed. Review the candidate in your CAM system before cutting.')
    topology=conversion.get('topologyComparison',{})
    classification=topology.get('outputHierarchy',{}) if topology.get('verifiedAtJoinTolerance') else item.get('contourClassification',{})
    if classification.get('verified'):
        findings.append((f"Определены контуры: наружных — {classification['outerCount']}, отверстий — {classification['holeCount']}, островков — {classification['islandCount']}." if ru else f"Contours identified: {classification['outerCount']} outer, {classification['holeCount']} holes, {classification['islandCount']} islands."))
        contours=topology.get('contours',[]) if topology.get('verifiedAtJoinTolerance') else classification.get('contours',[])
        for contour in contours:
            role=({'OUTER':'наружный','HOLE':'отверстие','ISLAND':'островок'}[contour['role']] if ru else contour['role'].lower())
            parent=contour['parentIndex']
            findings.append((f"Контур {contour['index']}: {role}" if ru else f"Contour {contour['index']}: {role}")+((f", внутри контура {parent}." if ru else f", inside contour {parent}.") if parent is not None else '.'))
    elif topology.get('error'):
        findings.append(('Вложенность контуров не подтверждена: ' if ru else 'Contour nesting is unverified: ')+topology['error'])
    if item.get('layerSplit'):
        split=item['layerSplit']
        findings.append((f"Создано слоёв: {split['createdLayerCount']}. Объектов распределено: {split['entityCount']}. Геометрия не изменена." if ru else f"Created {split['createdLayerCount']} layers for {split['entityCount']} entities. Geometry is unchanged."))
        if split.get('preservedEntityCount'):
            findings.append((f"Без изменений на исходных слоях сохранено объектов: {split['preservedEntityCount']} ({', '.join(split['preservedEntityTypes'])})." if ru else f"Preserved {split['preservedEntityCount']} objects on original layers ({', '.join(split['preservedEntityTypes'])})."))
    proof=conversion.get('normalization')
    closure=item.get('closureCheck')
    if closure:
        if closure['passed']:
            findings.append((f"Контуры резки замкнуты в пределах 0,001 мм. Контуров: {closure.get('componentCount',0)}; верхняя оценка зазора: {closure.get('maxJoinBoundMm','?')} мм." if ru else f"CUT contours close within 0.001 mm. Contours: {closure.get('componentCount',0)}; join-gap upper bound: {closure.get('maxJoinBoundMm','?')} mm."))
        else:findings.append('Замкнутость контуров резки с допуском 0,001 мм не подтверждена. Результат заблокирован.' if ru else 'CUT closure within 0.001 mm is not verified. Candidate blocked.')
    splines=conversion.get('splines',[])
    optimized=[s['optimization'] for s in splines if 'optimization' in s]
    if optimized:
        arcs=sum(s.get('arcCount',0) for s in optimized);lines=sum(s.get('lineCount',0) for s in optimized)
        findings.append((f'Аппроксимация сплайнов: дуг {arcs}, отрезков {lines}. Плавные стыки обязательны; число элементов сокращается в пределах заданного отклонения. Глобальный минимум не доказан.' if ru else f'Spline approximation: {arcs} arcs, {lines} lines. Smooth joins are required; element count is reduced within the selected deviation. Global optimality is not proven.'))
    if proof:findings.append((f"Полилинии: {proof['polylineCount']}; получено отрезков: {proof['lineCount']}. Координаты сохранены точно; положение штрихов линии может измениться." if ru else f"Expanded {proof['polylineCount']} straight polylines into {proof['lineCount']} lines with exact coordinates. Dash phase may change."))
    for issue in diagnosis.get('issues',[]):
        if ru:
            if issue.startswith('Conversion does not support:'):issue='Пока не поддерживаются объекты: '+issue.split(':',1)[1]
            elif issue=='Curved LWPOLYLINE bulge is not supported yet':issue='Просмотр дуговых полилиний поддерживается; их преобразование в CAM_ARCS пока не поддерживается.'
            elif issue=='Nonzero polyline width, elevation or thickness is not supported':issue='Полилиния с шириной, высотой или толщиной пока не поддерживается.'
            elif issue=='Units are not explicitly millimetres; conversion is blocked':issue='В файле не указаны миллиметры. Автоматическое преобразование заблокировано.'
            elif issue.startswith('Duplicate handles:'):issue='Повторяющиеся идентификаторы объектов: '+issue.split(':',1)[1]
            else:issue='Обнаружено замечание к структуре или геометрии; см. подробности.'
        findings.append(issue)
    if not findings:findings.append('Проверка завершена. Замечаний в реализованных проверках нет.' if ru else 'Inspection completed without findings in the implemented checks.')
    return list(dict.fromkeys(findings))

def render_report(result,path,language='ru'):
    ru=language=='ru'
    statuses={'LAYER_SPLIT_COMPLETE':('Разделено по слоям','Split into layers'),'ERROR':('Ошибка','Error'),'BLOCKED':('Преобразование отклонено','Conversion blocked'),'BLOCKED_REVIEW':('Требуется дополнительная проверка','Quarantined for review'),'CHECKED_CANDIDATE_REVIEW_REQUIRED':('Проверки пройдены; нужна проверка перед резкой','Checked candidate; manufacturing review required'),'INSPECTED':('Проверка завершена','Inspected'),'ISSUES_FOUND':('Есть замечания','Issues found')}
    rows=[]
    statuses['LAYER_SPLIT_PARTIAL']=('Разделено; часть объектов сохранена','Split; some objects preserved')
    for item in result['files']:
        d=item.get('diagnosis',{});c=item.get('conversion',{})
        findings=friendly_findings(item,ru)
        technical=d.get('issues',[])+item['errors']+c.get('unverifiedConditions',[])+[e.get('message','Engine error') for e in c.get('fileErrors',[])]
        details='<details><summary>'+('Технические подробности' if ru else 'Technical details')+'</summary><pre>'+html.escape('\n'.join(technical))+'</pre></details>' if technical else ''
        entities=', '.join(f'{k}: {v}' for k,v in d.get('entityCounts',{}).items())
        status=statuses.get(item['status'],(item['status'],item['status']))[0 if ru else 1]
        cells=[Path(item['source']).name,status,entities]
        rows.append('<tr>'+''.join('<td>'+html.escape(v)+'</td>' for v in cells)+'<td>'+ '<br>'.join(html.escape(v) for v in findings)+details+'</td></tr>')
    headings=['Файл','Результат','Объекты','Пояснение'] if ru else ['File','Result','Entities','Findings']
    intro='Исходные файлы не перезаписываются. Результаты требуют проверки технологом перед резкой. Неподтверждённые варианты помещаются в карантин.' if ru else 'Source files are preserved. Candidates require manufacturing review before cutting. Unverified candidates are quarantined.'
    Path(path).write_text('<!doctype html><html lang="'+language+'"><meta charset="utf-8"><title>CNC File Doctor</title><style>body{font:15px Segoe UI;margin:32px;background:#f4f6fa}table{border-collapse:collapse;width:100%;background:white}td,th{padding:12px;border:1px solid #ccd;vertical-align:top;text-align:left}details{margin-top:12px;color:#555}pre{white-space:pre-wrap;overflow-wrap:anywhere}</style><h1>CNC File Doctor '+VERSION+'</h1><p>'+intro+'</p><table><tr>'+''.join('<th>'+h+'</th>' for h in headings)+'</tr>'+''.join(rows)+'</table></html>',encoding='utf-8')

def discover_inputs(inputs):
    """Snapshot originals, pruning directories created by this application."""
    files=[]
    def generated(folder):
        marker=folder/'.cnc-file-doctor.json'
        if not marker.is_file():marker=folder/'Reports'/'report.json'
        if not marker.is_file():return False
        try:return json.loads(marker.read_text(encoding='utf-8-sig')).get('program')=='CNC File Doctor'
        except (ValueError,OSError):return False
    for entry in inputs:
        p=Path(entry).resolve()
        if p.is_file() and p.suffix.lower()=='.dxf':files.append(p)
        elif p.is_dir():
            if generated(p):
                files.extend(q for q in p.iterdir() if q.is_file() and q.suffix.lower()=='.dxf')
                continue
            for directory,subdirs,names in os.walk(p,followlinks=False):
                parent=Path(directory)
                if generated(parent):subdirs[:]=[];continue
                subdirs[:]=[name for name in subdirs if not (parent/name).is_symlink() and not generated(parent/name)]
                files.extend(parent/name for name in names if Path(name).suffix.lower()=='.dxf')
        else:raise ValueError('DXF file/directory not found: '+str(p))
    return sorted(set(files),key=lambda p:str(p).casefold())

def run_auto_job(inputs,convert=False,tolerance='0.01',layers=('0',),language='ru',split_criteria=None):
    files=discover_inputs(inputs)
    if not files:raise ValueError('No input DXF files')
    groups=collections.defaultdict(list)
    for source in files:groups[source.parent].append(source)
    summary=dict(program='CNC File Doctor',version=VERSION,files=[],outputFolders=[])
    for parent,sources in groups.items():
        number=1
        while True:
            output=parent/((parent.name or 'DXF')+'_'+str(number))
            try:
                job=run_job(sources,output,convert,tolerance,layers,language=language,flat_output=True,split_criteria=split_criteria)
                break
            except OutputFolderExists:number+=1
        summary['outputFolders'].append(str(output))
        for item in job['files']:
            item['processedFolder']=str(output);item['reportPath']=str(output/'Reports'/'report.html')
        # Persist navigation paths in each report too.
        write_json(output/'Reports'/'report.json',job)
        summary['files'].extend(job['files'])
    return summary

def run_job(inputs,output,convert=False,tolerance='0.01',layers=('0',),progress=None,language='ru',flat_output=False,split_criteria=None):
    import spline_arc_batch as batch, spline_arc_core as core
    from decimal import Decimal
    core.Options(tolerance_mm=Decimal(tolerance)).validate()
    files=discover_inputs(inputs)
    if not files: raise ValueError('No DXF files found')
    out=Path(output).resolve()
    if any(out==p or out in p.parents for p in files): raise ValueError('Output overlaps input files')
    if any(Path(s).is_dir() and (Path(s).resolve()==out or Path(s).resolve() in out.parents) for s in inputs): raise ValueError('Choose output outside the input folder')
    try:out.mkdir(parents=True,exist_ok=False)
    except FileExistsError as e:raise OutputFolderExists(str(out)) from e
    write_json(out/'.cnc-file-doctor.json',dict(program='CNC File Doctor',version=VERSION))
    for name in (('EXACT','Reports','Quarantine') if flat_output else ('EXACT','CAM_ARCS','Reports','Quarantine')): (out/name).mkdir()
    result=dict(program='CNC File Doctor',version=VERSION,created=datetime.datetime.now().astimezone().isoformat(),mode='layers' if split_criteria else 'convert' if convert else 'inspect',toleranceMm=tolerance,closureToleranceMm='0.001',cutLayers=list(layers),productionReady=False,files=[])
    reserved=set(); converted=set()
    for i,source in enumerate(files):
        if progress: progress(i,len(files),str(source))
        name,cnc=batch.output_names(source,reserved,converted)
        key=hashlib.sha256(str(source).encode()).hexdigest()[:16]
        item={'source':str(source),'errors':[],'productionReady':False}
        before=digest(source)
        try:
            item['diagnosis']=scan(source,layers)
            if split_criteria:
                from layer_split import split
                target=(out if flat_output else out/'CAM_ARCS')/cnc.replace('-CNC.dxf','-LAYERS.dxf')
                with (out/'EXACT'/name).open('xb') as f:f.write(source.read_bytes())
                item['layerSplit']=split(source,target,split_criteria)
                item['candidate']=str(target);item['status']='LAYER_SPLIT_PARTIAL' if item['layerSplit']['preservedEntityCount'] else 'LAYER_SPLIT_COMPLETE'
                item['diagnosis']['issues']=[issue for issue in item['diagnosis']['issues'] if not issue.startswith('Conversion does not support:')]
            elif convert:
                log=out/'Reports'/(key+'-engine.log'); report=out/'Reports'/(key+'-engine.json')
                target=(out if flat_output else out/'CAM_ARCS')/cnc
                cmd=[sys.executable,str(Path(__file__).resolve()),'--worker',str(source),str(out/'EXACT'/name),str(target),str(report),'--tolerance-mm',tolerance]
                for layer in layers: cmd+=['--cut-layer',layer]
                try:
                    with log.open('w',encoding='utf-8') as stream:
                        proc=subprocess.run(cmd,stdout=stream,stderr=stream,timeout=180,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
                    if proc.returncode: raise RuntimeError('Engine failed; see '+log.name)
                    item['conversion']=json.loads(report.read_text(encoding='utf-8'))
                    c=item['conversion']
                    valid=bool(c.get('geometryBoundWithinTolerance') and c.get('qualityChecksPassedAtDeclaredTolerances') and not c.get('fileErrors'))
                    if target.exists():
                        from closure_check import audit as check_closure
                        item['closureCheck']=check_closure(target,tuple(layers))
                        c['finalClosureCheck']=item['closureCheck']
                        valid=valid and item['closureCheck']['passed']
                        write_json(report,c)
                    if target.exists() and not valid:
                        quarantine=out/'Quarantine'/cnc; target.replace(quarantine)
                        c['cnc']=str(quarantine); c['cncExists']=False; c['quarantinedCandidate']=str(quarantine)
                        write_json(report,c)
                        item['candidate']=str(quarantine); item['status']='BLOCKED_REVIEW'
                    elif target.exists():
                        item['candidate']=str(target); item['status']='CHECKED_CANDIDATE_REVIEW_REQUIRED'
                        try:
                            from decimal import Decimal,localcontext,ROUND_FLOOR,ROUND_CEILING
                            from contour_join import join
                            joined=target.with_name(target.stem.replace('-CNC','')+'-CONTOURS.dxf')
                            with localcontext() as context:
                                context.prec=120;context.rounding=ROUND_FLOOR
                                remaining=Decimal(tolerance)-Decimal(c['maximumWholeCurveBoundMm'])
                            item['contourJoin']=join(target,joined,tuple(layers),remaining)
                            with localcontext() as context:
                                context.prec=120;context.rounding=ROUND_CEILING
                                item['contourJoin']['maximumTotalDeviationMm']=str(Decimal(c['maximumWholeCurveBoundMm'])+Decimal(item['contourJoin']['maximumAdditionalDeviationMm']))
                            analytic_reference=out/'Reports'/(key+'-analytic.dxf')
                            target.replace(analytic_reference)
                            item['analyticCandidate']=str(analytic_reference);item['candidate']=str(joined)
                            c['cnc']=str(analytic_reference);c['joinedCandidate']=str(joined)
                            write_json(report,c)
                        except (ValueError,ArithmeticError,KeyError) as e:item['joinError']=str(e)
                    else: item['status']='BLOCKED'
                except Exception:
                    if target.exists(): target.replace(out/'Quarantine'/cnc)
                    raise
            else:
                with (out/'EXACT'/name).open('xb') as f: f.write(source.read_bytes())
                item['status']=item['diagnosis']['status']
                import topology_certification,dxf_support as dx
                _,source_rows=dx.read(source)
                cut=[row for row in source_rows if dx.one(row,8,'0') in layers and dx.one(row,8,'0').upper() not in ('UP','DOWN')]
                if all(dx.kind(row) in ('LINE','ARC','CIRCLE','SPLINE') for row in cut):item['contourClassification']=topology_certification.classify(cut)
        except Exception as e:
            item['status']='ERROR'; item['errors'].append(type(e).__name__+': '+str(e))
        item['sourceUnchanged']=digest(source)==before
        item['exactCopyVerified']=(out/'EXACT'/name).exists() and digest(out/'EXACT'/name)==before
        if not item['sourceUnchanged']: item['status']='ERROR'; item['errors'].append('Source changed during processing')
        item['findingsRu']=friendly_findings(item,True)
        item['findingsEn']=friendly_findings(item,False)
        result['files'].append(item)
        write_json(out/'Reports'/'report.json',result)
    render_report(result,out/'Reports'/'report.html',language)
    return result

def main():
    p=argparse.ArgumentParser(description='CNC File Doctor — offline DXF diagnosis')
    p.add_argument('--input',action='append'); p.add_argument('--output'); p.add_argument('--convert',action='store_true')
    p.add_argument('--tolerance-mm',default='0.01'); p.add_argument('--cut-layer',action='append')
    p.add_argument('--worker',nargs=4,metavar=('SOURCE','EXACT','TARGET','REPORT'))
    p.add_argument('--request')
    p.add_argument('--scan')
    p.add_argument('--discover',help='JSON list of originals, excluding prior result folders')
    a=p.parse_args()
    if a.discover:
        print(json.dumps([str(p) for p in discover_inputs([a.discover])],ensure_ascii=True));return 0
    if a.scan:
        print(json.dumps(scan(a.scan,a.cut_layer or ['0']),ensure_ascii=True));return 0
    if a.request:
        request=json.loads(Path(a.request).read_text(encoding='utf-8-sig'))
        if request.get('outputMode')=='source-folder':
            r=run_auto_job(request['inputs'],request['convert'],request['tolerance'],request['layers'],request.get('language','ru'),request.get('splitCriteria'))
            write_json(str(a.request)+'.result.json',r)
        else:r=run_job(request['inputs'],request['output'],request['convert'],request['tolerance'],request['layers'],language=request.get('language','ru'))
        return 1 if any(f['status']=='ERROR' for f in r['files']) else 2 if request['convert'] else 0
    if a.worker: worker(*a.worker,a.tolerance_mm,a.cut_layer or ['0']); return 0
    if not a.input:
        subprocess.Popen([str(ROOT.parent/'CNC File Doctor.exe')]); return 0
    if not a.output: p.error('--output is required')
    r=run_job(a.input,a.output,a.convert,a.tolerance_mm,a.cut_layer or ['0'],lambda i,n,s:print(f'{i+1}/{n}: {s}',flush=True))
    print(json.dumps({'report':str(Path(a.output)/'Reports'/'report.html'),'files':len(r['files'])}))
    return 1 if any(f['status']=='ERROR' for f in r['files']) else (2 if a.convert or any(f['status']!='INSPECTED' for f in r['files']) else 0)

if __name__=='__main__':
    try: sys.exit(main())
    except Exception as e:
        print(type(e).__name__+': '+str(e),file=sys.stderr); sys.exit(1)


