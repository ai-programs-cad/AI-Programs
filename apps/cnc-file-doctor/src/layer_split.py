"""Split top-level drawing entities into layers without changing geometry."""
import hashlib,json,re
from decimal import Decimal as D
from pathlib import Path
import dxf_support as dx

CRITERIA={'color','linetype','lineweight','entity_type'}
STYLE={8,6,62,420,430,370}
SUPPORTED={'LINE','ARC','CIRCLE','SPLINE','LWPOLYLINE','ELLIPSE','TEXT','POINT','SOLID','TRACE','3DFACE'}

def pattern_key(tags):
    """Compare linetype definitions by pattern rather than their names."""
    segments=[D(v) for k,v in tags if k==49]
    if not segments and all(D(v)==0 for k,v in tags if k==40):return ('CONTINUOUS',)
    def number(v):
        n=D(v);return '0' if n==0 else str(n.normalize())
    if all(int(v)==0 for k,v in tags if k==74) and not any(k in (75,340,46,50,44,45,9) for k,v in tags):
        length=next((D(v) for k,v in tags if k==40),sum(map(abs,segments)))
        alignment=int(next((v for k,v in tags if k==72),'65'))
        return ('SIMPLE',alignment,number(length),tuple(number(v) for v in segments))
    relevant=[(k,v) for k,v in tags if k not in (0,5,330,100,2,3,70,73)]
    return ('PATTERN',tuple((k,number(v) if k in (40,49,46,50,44,45) else v) for k,v in relevant))

def color_key(aci,true):
    if true is not None:return ('RGB',int(true))
    # ACI 7 is adaptive black/white and is not equated with fixed TrueColor white.
    rgb={1:0xff0000,2:0xffff00,3:0x00ff00,4:0x00ffff,5:0x0000ff,6:0xff00ff}
    return ('RGB',rgb[aci]) if aci in rgb else ('ACI',aci)

def split(source,target,criteria=('color','linetype','lineweight')):
    criteria=tuple(criteria)
    if not criteria or set(criteria)-CRITERIA:raise ValueError('Choose color, linetype, lineweight or entity_type')
    pairs,rows=dx.read(source)
    if not rows:raise ValueError('No drawing entities')
    preserved=[r for r in rows if dx.kind(r) not in SUPPORTED]
    if len(preserved)==len(rows):raise ValueError('No supported entities to distribute; original file retained')
    # Locate the existing layer table and keep all its original definitions.
    table=None;end=None;definitions={};i=0
    while i<len(pairs):
        if pairs[i][:2]==(0,'TABLE') and i+1<len(pairs) and pairs[i+1][:2]==(2,'LAYER'):
            if table is not None:raise ValueError('Multiple LAYER tables')
            table=i
            end=next(j for j in range(i+2,len(pairs)) if pairs[j][:2]==(0,'ENDTAB'))
            j=i+2
            while j<end:
                if pairs[j][:2]==(0,'LAYER'):
                    stop=next(k for k in range(j+1,end+1) if pairs[k][0]==0)
                    row={'start':j,'end':stop,'pairs':[(k,v) for k,v,_ in pairs[j:stop]]}
                    name=dx.one(row,2)
                    if name.casefold() in definitions:raise ValueError('Duplicate source layer names')
                    definitions[name.casefold()]=row;j=stop
                else:j+=1
            i=end
        i+=1
    if table is None:raise ValueError('Existing LAYER table required')
    handles=[v.upper() for k,v,_ in pairs if k in (5,105)]
    if len(handles)!=len(set(handles)):raise ValueError('Duplicate handles; layer split blocked')
    next_handle=max(int(h,16) for h in handles)+1
    linetypes={};default_weight=None
    for pos,(k,v,_) in enumerate(pairs[:-1]):
        if (k,v)==(9,'$LWDEFAULT'):
            default_weight=int(pairs[pos+1][1])
            if default_weight<0:raise ValueError('Invalid drawing default lineweight')
    for pos,(k,v,_) in enumerate(pairs):
        if (k,v)==(0,'LTYPE'):
            stop=next(j for j in range(pos+1,len(pairs)) if pairs[j][0]==0)
            names=[value for code,value,_ in pairs[pos:stop] if code==2]
            if len(names)!=1:raise ValueError('Invalid linetype definition')
            linetypes[names[0].casefold()]=pattern_key([(code,value) for code,value,_ in pairs[pos:stop]])
    changes={};groups={};new_layers=[];mapping=[]
    for row in rows:
        if dx.kind(row) not in SUPPORTED:continue
        source_name=dx.one(row,8,'0');layer=definitions.get(source_name.casefold())
        if layer is None:raise ValueError('Entity refers to an undefined layer: '+source_name)
        if any(k in (102,360) or k>=1000 for k,v in layer['pairs']):raise ValueError('Layer extension payload cannot be cloned safely')
        aci=int(dx.one(row,62,'256'))
        if aci==256:
            aci=abs(int(dx.one(layer,62,'7')))
            true=dx.one(row,420,dx.one(layer,420))
            book=dx.one(row,430,dx.one(layer,430))
        else:true=dx.one(row,420);book=dx.one(row,430)
        ltype=dx.one(row,6,'BYLAYER')
        if ltype.upper()=='BYLAYER':ltype=dx.one(layer,6,'CONTINUOUS')
        if ltype.upper()=='BYLAYER':raise ValueError('Unresolved inherited linetype')
        if ltype.upper()!='BYBLOCK' and ltype.casefold() not in linetypes:raise ValueError('Undefined source linetype: '+ltype)
        weight=int(dx.one(row,370,'-1'))
        if weight==-1:weight=int(dx.one(layer,370,'-3'))
        if weight<0 and weight not in (-2,-3):raise ValueError('Invalid lineweight cannot be resolved')
        effective_aci=7 if aci==0 else aci
        effective_pattern=('CONTINUOUS',) if ltype.upper()=='BYBLOCK' else linetypes[ltype.casefold()]
        effective_weight=(default_weight if default_weight is not None else ('DEFAULT',)) if weight in (-2,-3) else weight
        # Scale is meaningful for patterned types; it does not distinguish solid lines.
        effective_type=(effective_pattern,str(D(dx.one(row,48,'1')).normalize())) if effective_pattern!=('CONTINUOUS',) else effective_pattern
        values=dict(color=color_key(effective_aci,true),linetype=effective_type,lineweight=effective_weight,entity_type=dx.kind(row))
        # Preserve inherited visibility/plot/material state even if not selected.
        state=(('flags',int(dx.one(layer,70,'0'))),('plot',int(dx.one(layer,290,'1'))),('off',int(dx.one(layer,62,'7'))<0),('extra',tuple(sorted((k,v) for k,v in layer['pairs'] if k not in (0,5,330,100,2,6,62,420,430,370,70,290)))))
        key=tuple((name,values[name]) for name in sorted(criteria))+(('layerState',state),)
        if key not in groups:
            suffix=hashlib.sha256(repr(key).encode()).hexdigest()[:10].upper()
            name='CFD_'+('_'.join(re.sub('[^A-Za-z0-9_-]','_',str(values[k]))[:24] for k in criteria))+'_'+suffix
            if name.casefold() in definitions:raise ValueError('Generated layer name already exists; choose original input')
            groups[key]=name;clone=[]
            for k,v in layer['pairs']:clone.append((k,format(next_handle,'X') if k==5 else name if k==2 else v))
            next_handle+=1;new_layers.append(dx.encode(clone))
        name=groups[key]
        properties=[(8,name),(62,aci),(6,ltype),(370,weight)]
        if true is not None:properties.append((420,true))
        if book is not None:properties.append((430,book))
        blob=[];inserted=False
        for k,v,raw in pairs[row['start']:row['end']]:
            if k==8:
                blob.append(dx.encode(properties));inserted=True
            elif k in STYLE:continue
            else:
                blob.append(raw)
                if (k,v)==(100,'AcDbEntity') and dx.one(row,8) is None:
                    blob.append(dx.encode(properties));inserted=True
        if not inserted:raise ValueError('Missing entity subclass/layer marker')
        changes[row['start']]=(row['end'],b''.join(blob))
        mapping.append(dict(handle=dx.one(row,5),sourceLayer=source_name,outputLayer=name,effectiveProperties=values,writtenProperties=dict(colorAci=aci,trueColor=true,linetype=ltype,lineweight=weight)))
    header_end=next(j for j in range(table+1,end) if pairs[j][0]==0)
    counts=[j for j in range(table,header_end) if pairs[j][0]==70]
    if len(counts)>1:raise ValueError('Ambiguous layer-table count')
    insertions={end:b''.join(new_layers)}
    count_blob=dx.encode([(70,len(definitions)+len(new_layers))])
    if counts:changes[counts[0]]=(counts[0]+1,count_blob)
    else:insertions[header_end]=count_blob
    for i,(k,v,_) in enumerate(pairs[:-1]):
        if (k,v)==(9,'$HANDSEED'):
            if pairs[i+1][0]!=5:raise ValueError('Invalid HANDSEED')
            changes[i+1]=(i+2,dx.encode([(5,format(next_handle,'X'))]))
    chunks=[];i=0
    while i<len(pairs):
        if i in insertions:chunks.append(insertions[i])
        if i in changes:
            stop,blob=changes[i];chunks.append(blob);i=stop
        else:chunks.append(pairs[i][2]);i+=1
    target=Path(target)
    with target.open('xb') as stream:stream.write(b''.join(chunks))
    def geometry(p,rs):return [b''.join(raw for k,v,raw in p[r['start']:r['end']] if k not in STYLE) for r in rs]
    try:
        after,arows=dx.read(target)
        if geometry(pairs,rows)!=geometry(after,arows):raise ValueError('Geometry byte comparison failed')
        supported_written=[r for r in arows if dx.kind(r) in SUPPORTED]
        for written,proof in zip(supported_written,mapping):
            props=proof['writtenProperties']
            if dx.one(written,8)!=proof['outputLayer'] or dx.one(written,6).casefold()!=props['linetype'].casefold() or int(dx.one(written,370))!=props['lineweight']:raise ValueError('Written layer/style properties differ')
            if props['trueColor'] is not None and int(dx.one(written,420))!=int(props['trueColor']):raise ValueError('Written truecolor differs')
            if int(dx.one(written,62))!=props['colorAci']:raise ValueError('Written color differs')
        untouched=[r for r in arows if dx.kind(r) not in SUPPORTED]
        if [dx.rawrow(pairs,r) for r in preserved]!=[dx.rawrow(after,r) for r in untouched]:raise ValueError('Preserved annotation/block records changed')
    except Exception:
        target.unlink();raise
    return dict(criteria=list(criteria),groupingBasis='Effective properties of top-level drawing entities; assignment origin ignored',externalDefaultLineweightNotGuessed=default_weight is None,entityCount=len(mapping),totalEntityCount=len(rows),preservedEntityCount=len(preserved),preservedEntityTypes=sorted({dx.kind(r) for r in preserved}),preservedRecordsByteUnchanged=True,byBlockPropertiesPreservedSymbolically=True,createdLayerCount=len(groups),newLayers=list(groups.values()),geometryBytesUnchanged=True,effectiveEntityPropertiesMadeExplicit=True,sourceSha256=dx.sha(source),outputSha256=dx.sha(target),mapping=mapping)
