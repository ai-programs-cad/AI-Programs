"""Display-only tessellation and location markers; never modifies DXF."""
import math,time
from fractions import Fraction as F
from decimal import Decimal as D
import dxf_support as dx
import certified_curves as cc
from polyline_adapter import vertices

def polyline_points(row):
    """Display signed bulge arcs; the manufacturing adapter stays unchanged."""
    segments=vertices({'pairs':[(k,v) for k,v in row['pairs'] if k!=42]})
    bulges=[];assigned=[]
    for k,v in row['pairs']:
        if k==10:bulges.append(0.);assigned.append(False)
        elif k==42:
            if not bulges or assigned[-1]:raise ValueError('Invalid bulge assignment')
            bulges[-1]=float(v);assigned[-1]=True
    points=[tuple(map(float,segments[0][0]))]
    for index,(raw_a,raw_b) in enumerate(segments):
        a,b=tuple(map(float,raw_a)),tuple(map(float,raw_b));bulge=bulges[index]
        if not math.isfinite(bulge):raise ValueError('Nonfinite bulge')
        if bulge==0:points.append(b);continue
        # Same signed center construction as ManufacturingDxfV04.cs.
        vx,vy=b[0]-a[0],b[1]-a[1];offset=(1-bulge*bulge)/(4*bulge)
        cx=(a[0]+b[0])/2-vy*offset;cy=(a[1]+b[1])/2+vx*offset
        radius=math.hypot(vx,vy)*(1+bulge*bulge)/(4*abs(bulge))
        start=math.atan2(a[1]-cy,a[0]-cx);sweep=4*math.atan(bulge)
        count=max(12,math.ceil(abs(sweep)*360/math.pi))
        if not all(map(math.isfinite,(cx,cy,radius))):raise ValueError('Unstable display arc')
        points.extend((cx+radius*math.cos(start+sweep*j/count),cy+radius*math.sin(start+sweep*j/count)) for j in range(1,count))
        points.append(b)
    return points,segments

def build(path,cut_layers=('0',)):
    pairs,rows=dx.read(path);geometry=[];markers=[];cut=[];started=time.monotonic();partial=False
    layer_colors={};layer_off={}
    for i,(k,v,_) in enumerate(pairs):
        if (k,v)==(0,'LAYER'):
            end=next(j for j in range(i+1,len(pairs)) if pairs[j][0]==0)
            row={'pairs':[(k,v) for k,v,_ in pairs[i:end]]};name=dx.one(row,2,'0').casefold()
            layer_colors[name]=(int(dx.one(row,62,'7')),dx.one(row,420));layer_off[name]=int(dx.one(row,62,'7'))<0
    def color(row):
        aci=int(dx.one(row,62,'256'));true=dx.one(row,420)
        if aci==256:
            aci,inherited=layer_colors.get(dx.one(row,8,'0').casefold(),(7,None))
            if true is None:true=inherited
        if true is not None:return int(true)&0xffffff
        return {1:0xff5959,2:0xffdf65,3:0x67e6a2,4:0x6ed9f7,5:0x6c9fff,6:0xe99cff,7:0xe8edf4,8:0x8f9ba9,9:0xc3cbd4}.get(abs(aci),0xe8edf4)
    def marker(point,code,ru,en,handle=None):markers.append(dict(point=list(map(float,point)),code=code,messageRu=ru,messageEn=en,handle=handle))
    seen={}
    for row in rows:
        if len(geometry)>=20000 or time.monotonic()-started>3:partial=True;break
        kind=dx.kind(row);handle=dx.one(row,5,'?');layer=dx.one(row,8,'0');points=[]
        try:
            if kind=='LINE':points=[(float(dx.one(row,10)),float(dx.one(row,20))),(float(dx.one(row,11)),float(dx.one(row,21)))]
            elif kind in ('ARC','CIRCLE'):
                x,y,r=map(float,(dx.one(row,10),dx.one(row,20),dx.one(row,40)))
                start=float(dx.one(row,50,'0'));sweep=(float(dx.one(row,51,'360'))-start)%360 if kind=='ARC' else 360
                count=max(24,int(sweep*2))
                points=[(x+r*math.cos(math.radians(start+sweep*i/count)),y+r*math.sin(math.radians(start+sweep*i/count))) for i in range(count+1)]
            elif kind=='SPLINE':
                for _,_,h in dx.spline(row):
                    points.extend(tuple(map(float,cc.point(h,F(i,32)))) for i in range(33))
                    if len(points)>5000:partial=True;break
            elif kind=='LWPOLYLINE':
                points,segments=polyline_points(row)
            else:partial=True;continue
            if not points or any(not math.isfinite(x) or not math.isfinite(y) for x,y in points):raise ValueError('Invalid display coordinates')
            geometry.append(dict(type=kind,handle=handle,layer=layer,points=points,displayRgb=color(row),pending=kind in ('SPLINE','LWPOLYLINE') and layer in cut_layers and layer.upper() not in ('UP','DOWN')))
            signature=(kind,tuple((k,v) for k,v in row['pairs'] if k not in (5,330)))
            if signature in seen:marker(points[0],'DUPLICATE','Совпадающие записи объектов; автоматическое удаление не выполняется.','Duplicate entity records; no automatic removal.',handle)
            seen[signature]=handle
            if kind=='LINE' and points[0]==points[-1]:marker(points[0],'ZERO_LENGTH','Нулевой отрезок; требуется проверка.','Zero-length line; review required.',handle)
            if kind in ('ARC','CIRCLE') and r<=0:marker(points[0],'INVALID_RADIUS','Некорректный радиус.','Invalid radius.',handle)
            if layer in cut_layers and layer.upper() not in ('UP','DOWN'):
                if kind=='LWPOLYLINE':
                    for i,(a,b) in enumerate(segments):cut.append({'pairs':[(0,'LINE'),(5,handle+'_'+str(i)),(10,a[0]),(20,a[1]),(11,b[0]),(21,b[1])]})
                else:cut.append(row)
        except (ValueError,KeyError,ArithmeticError) as e:
            partial=True
            x,y=dx.values(row,10),dx.values(row,20)
            if x and y:marker((x[0],y[0]),'UNSUPPORTED','Не удалось полностью показать объект: '+str(e),'Object preview incomplete: '+str(e),handle)
    closure_checked=False
    if cut and len(cut)<=3000 and not partial:
        try:
            graph,nodes,edges=dx.graph(cut,D('0.001'));closure_checked=True
            for node,degree in graph['invalidNodeDegrees'].items():
                p=nodes[int(node)];point=[(v.lo+v.hi)/2 for v in p]
                marker(point,'OPEN_ENDPOINT' if degree==1 else 'BRANCH','Открытый конец контура.' if degree==1 else 'Неоднозначное ветвление контура.','Open contour endpoint.' if degree==1 else 'Branched contour.')
        except (ValueError,KeyError,ArithmeticError):partial=True
    return dict(geometry=geometry,diagnostics=dict(markers=markers,partial=partial,cutLayers=list(cut_layers),closureChecked=closure_checked,closureToleranceMm='0.001',splinePreviewIsSampled=True,notFullIntersectionAudit=True))
