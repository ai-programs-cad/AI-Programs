using System;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
public class GE06 {
    public int Id;public bool Line,Full,Sense;public NV P,Q,C;public double U0,U1;public NConic Conic;public int[] Adj;
    public GE06 Move(NTransform t){return new GE06{Id=Id,Line=Line,Full=Full,Sense=Sense,P=t.Point(P),Q=t.Point(Q),C=t.Point(C),U0=U0,U1=U1,Conic=Line?null:Conic.Transform(t),Adj=Adj};}
}
public class GC06 {public int Id,Next;public GE06 E;public bool Sense;public NV P,Q;}
public class GL06 {public int Id;public bool Outer;public List<GC06> Co=new List<GC06>();}
public class GF06 {public NFace S;public List<GL06> Loops=new List<GL06>();}
public class GB06 {
    public List<GE06> Edges=new List<GE06>();public List<GF06> Faces=new List<GF06>();
    public List<object> Checks=new List<object>();public List<string> Unsupported=new List<string>(),Errors=new List<string>();
    public bool Accepted {get{return Unsupported.Count==0&&Errors.Count==0&&Checks.Count>0&&Checks.All(NB03.Passed);}}
}
public static class GBrep06 {
    static double Tol {get{return NB03.Tolerance.GeometryM;}}
    static double Num {get{return NB03.Tolerance.ComputationalDimensionless;}}
    static double[] Arr(object x){return AB.Array(x).Select(AB.Num).ToArray();}
    static NV V(double[] a,int i){return new NV(a[i],a[i+1],a[i+2]);}
    static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    static void Check(GB06 b,string name,int id,bool ok,double residual=0){b.Checks.Add(new{check=name,id,passed=ok,diagnosticResidual=residual,certified=false});}
    public static GE06 Edge(object raw){
        Need(BatchV05.Flag(raw,"ownerVerified"),"Edge owner unverified");
        var cv=AB.Get(raw,"curve");var bd=AB.Get(raw,"bounds");int id=AB.Int(AB.Get(raw,"id"));
        var e=new GE06{Id=id,Line=BatchV05.Flag(cv,"isLine"),Sense=AB.Bool(AB.Get(bd,"sense")),P=NV.Read(AB.Get(bd,"startPointM")),Q=NV.Read(AB.Get(bd,"endPointM")),
            U0=AB.Num(AB.Get(bd,"uMin")),U1=AB.Num(AB.Get(bd,"uMax")),Adj=AB.Array(AB.Get(raw,"adjacentFaceIds")).Select(AB.Int).ToArray()};
        Need(NV.Finite(e.U0)&&NV.Finite(e.U1)&&e.U1>e.U0,"Invalid bounded parameter interval");
        if(e.Line){
            var p=Arr(AB.Get(cv,"lineParams"));Need(p.Length==6&&p.All(NV.Finite),"Invalid line parameters");
            var o=V(p,0);var d=V(p,3);Need(d.Norm()>0,"Zero line direction");
            double error=Math.Max((o+d*e.U0-e.P).Norm(),(o+d*e.U1-e.Q).Norm());
            Need(error<=Tol+NB03.Tolerance.LengthGuard(o.Norm()),"Line endpoints disagree with origin + direction * bounded parameter");
            Need((e.P-e.Q).Norm()>0,"Zero-length segment");e.C=(e.P+e.Q)*.5;
            Need((NV.Read(AB.Get(raw,"startVertexM"))-e.P).Norm()<=Tol&&(NV.Read(AB.Get(raw,"endVertexM"))-e.Q).Norm()<=Tol,"Line endpoint vertices missing/inconsistent");
        }else{
            bool circle=BatchV05.Flag(cv,"isCircle"),ellipse=BatchV05.Flag(cv,"isEllipse");
            Need(circle||ellipse,"Unsupported curve identity "+BatchV05.Get(cv,"identity")+"; INTERSECTION_TYPE 3004 requires native representation and error enclosure");
            NV c,a,b;double ra,rb;
            if(ellipse){var p=Arr(AB.Get(cv,"ellipseParams"));Need(p.Length==11&&p.All(NV.Finite),"Invalid ellipse parameters");c=V(p,0);a=V(p,4);b=V(p,8);ra=p[3];rb=p[7];}
            else {var p=Arr(AB.Get(cv,"circleParams"));Need(p.Length==7&&p.All(NV.Finite),"Invalid circle parameters");c=V(p,0);var n=V(p,3).Unit();a=NV.Cross(n,Math.Abs(n.X)<.8?new NV(1,0,0):new NV(0,1,0)).Unit();b=NV.Cross(n,a).Unit();ra=rb=p[6];}
            Need(ra>0&&rb>0&&Math.Abs(a.Norm()-1)<=Num&&Math.Abs(b.Norm()-1)<=Num&&Math.Abs(NV.Dot(a,b))<=Num,"Invalid conic axes/radii");
            var au=a.Unit();var bu=(b-au*NV.Dot(au,b)).Unit();
            var co=new NConic{Id=id,C=c,A=au*ra,B=bu*rb,BasisAccepted=true,FullPeriod=true,EdgeSense=e.Sense,Adj=e.Adj,
                CorrectionM=(a*ra-au*ra).Norm()+(b*rb-bu*rb).Norm()};
            e.Conic=co;e.C=c;e.Full=Math.Abs(e.U1-e.U0-2*Math.PI)<=Num&&((e.P-e.Q).Norm()<=Tol);
            Need(e.U1-e.U0<=2*Math.PI+Num,"Conic span exceeds one period");
            var domain=AB.Get(cv,"underlyingDomain");Need(BatchV05.Flag(domain,"returned"),"Underlying conic domain unavailable");
            if(e.Full)Need(BatchV05.Flag(domain,"closed")&&BatchV05.Flag(domain,"periodic")&&Math.Abs(AB.Num(AB.Get(domain,"end"))-AB.Num(AB.Get(domain,"start"))-2*Math.PI)<=Num,"Full conic periodic domain not confirmed");
            if(!e.Full){
                // Ellipse native coefficients are checked against BOTH actual bounded endpoints.
                // No fitted curve or finite point sampling substitutes for the analytic formula.
                Need(ellipse,"Partial circle phase not present in native circleParams; bounded native frame required");
                Need((Eval(co,e.U0)-e.P).Norm()<=Tol&&(Eval(co,e.U1)-e.Q).Norm()<=Tol,"Ellipse native phase/direction not established from saved parameters");
            }else Need(OnConic(co,e.P)<=Tol,"Closed edge start is not on conic");
        }
        return e;
    }
    static NV Eval(NConic c,double u){return c.C+c.A*Math.Cos(u)+c.B*Math.Sin(u);}
    static double OnConic(NConic c,NV p){var d=p-c.C;double x=NV.Dot(d,c.A)/NV.Dot(c.A,c.A),y=NV.Dot(d,c.B)/NV.Dot(c.B,c.B);return Math.Abs(NV.Dot(d,c.Normal))+Math.Max(c.A.Norm(),c.B.Norm())*Math.Abs(Math.Sqrt(x*x+y*y)-1);}
    public static object EdgeMatch(GE06 a,GE06 b){
        if(a.Line!=b.Line||a.Full!=b.Full)return new{passed=false,reason="Different bounded curve classes"};
        double error;int direction=1;object underlying=null;
        if(a.Line){
            double direct=Math.Max((a.P-b.P).Norm(),(a.Q-b.Q).Norm()),reverse=Math.Max((a.P-b.Q).Norm(),(a.Q-b.P).Norm());
            direction=direct<=reverse?1:-1;error=Math.Min(direct,reverse);
            // Every affine point of a segment is enclosed by the maximum endpoint error.
        }else{
            underlying=NB03.ConicResidual(a.Conic,b.Conic);
            if(!NB03.Passed(underlying))return new{passed=false,reason="Whole supporting conic coefficient mismatch",underlying};
            error=AB.Num(AB.Get(underlying,"diagnosticWholeCurveEnvelopeM"));direction=AB.Int(AB.Get(underlying,"parameterOrientation"));
            if(!a.Full){
                var phase=Arr(AB.Get(underlying,"phaseCosSin"));double alpha=Math.Atan2(phase[1],phase[0]);
                double lo=direction==1?alpha+a.U0:alpha-a.U1,hi=direction==1?alpha+a.U1:alpha-a.U0;
                double shift=2*Math.PI*Math.Round((b.U0-lo)/(2*Math.PI));
                double angular=Math.Max(Math.Abs(lo+shift-b.U0),Math.Abs(hi+shift-b.U1));
                error+=(b.Conic.A.Norm()+b.Conic.B.Norm())*angular;
            }
        }
        return new{passed=NV.Finite(error)&&error<=Tol+NB03.Tolerance.LengthGuard(a.C.Norm()+b.C.Norm()),
            diagnosticWholeBoundedCurveEnvelopeM=error,parameterDirection=direction,underlying,certified=false,
            method=a.Line?"Affine interpolation: maximum of paired endpoint distances encloses entire segment":"Whole conic coefficients plus aligned bounded angular interval"};
    }
    public static GB06 Parse(object raw){
        var b=new GB06();
        foreach(var eo in AB.Array(AB.Get(raw,"edges")))try{var e=Edge(eo);Need(!b.Edges.Any(x=>x.Id==e.Id),"Duplicate edge ID");b.Edges.Add(e);}
            catch(Exception ex){string s="edge "+BatchV05.Get(eo,"id")+": "+ex.Message;if(ex.Message.StartsWith("Unsupported")||ex.Message.StartsWith("Partial circle"))b.Unsupported.Add(s);else b.Errors.Add(s);}
        foreach(var fo in AB.Array(AB.Get(raw,"faces")))try{
            int id=AB.Int(AB.Get(fo,"id"));Need(!b.Faces.Any(existing=>existing.S.Id==id),"Duplicate face ID");Need(BatchV05.Flag(fo,"ownerVerified"),"Face owner unverified");var s=AB.Get(fo,"surface");
            var f=new GF06{S=new NFace{Id=id,Type=(string)AB.Get(s,"type"),MaterialSign=AB.Bool(AB.Get(fo,"faceInSurfaceSense"))?-1:1,Source=fo}};
            if(f.S.Type=="PLANE"){f.S.O=NV.Read(AB.Get(s,"rootPointM"));var normal=NV.Read(AB.Get(s,"normal"));Need(Math.Abs(normal.Norm()-1)<=Num,"Plane normal normalization outside computational tolerance");f.S.N=normal.Unit()*f.S.MaterialSign;}
            else if(f.S.Type=="CYLINDER"){
                f.S.O=NV.Read(AB.Get(s,"originM"));f.S.N=NV.Read(AB.Get(s,"axis")).Unit();f.S.Radius=AB.Num(AB.Get(s,"radiusM"));
                Need(Math.Abs(NV.Read(AB.Get(s,"axis")).Norm()-1)<=Num&&NV.Finite(f.S.Radius)&&f.S.Radius>0,"Invalid cylinder axis/radius");
                int radial=AB.Int(AB.Get(fo,"naturalCylinderRadialSign"));var w=NV.Read(AB.Get(fo,"naturalNormalWitnessM"));var ev=Arr(AB.Get(fo,"naturalNormalEvaluationRaw"));
                var d=w-f.S.O;Need(ev.Length>=3&&Math.Abs(radial)==1&&Math.Sign(NV.Dot(V(ev,0),d-f.S.N*NV.Dot(f.S.N,d)))==radial,"Natural cylinder material normal unverified");
                f.S.MaterialSign*=radial;
            }else throw new NotSupportedException("Unsupported surface "+f.S.Type);
            foreach(var lo in AB.Array(AB.Get(fo,"loops"))){
                var l=new GL06{Id=AB.Int(AB.Get(lo,"id")),Outer=AB.Bool(AB.Get(lo,"isOuter"))};
                Need(!AB.Bool(AB.Get(lo,"isSingular")),"Singular loop unsupported");
                foreach(var co in AB.Array(AB.Get(lo,"coedges"))){
                    int eid=AB.Int(AB.Get(co,"edgeId"));var e=b.Edges.SingleOrDefault(x=>x.Id==eid);
                    Need(e!=null,"Unresolved/unsupported edge "+eid);var p=Arr(AB.Get(co,"curveBoundsRaw"));Need(p.Length==10&&p.All(NV.Finite),"Invalid coedge bounds");
                    bool sense=AB.Bool(AB.Get(co,"sense"));bool same=sense==e.Sense;
                    Need(Math.Abs(p[6]-(same?e.U0:e.U1))<=Num&&Math.Abs(p[7]-(same?e.U1:e.U0))<=Num,"Coedge bounded interval disagrees with edge/sense");
                    var c=new GC06{Id=AB.Int(AB.Get(co,"id")),Next=AB.Int(AB.Get(co,"nextId")),E=e,Sense=sense,P=V(p,0),Q=V(p,3)};
                    Need((c.P-(same?e.P:e.Q)).Norm()<=Tol&&(c.Q-(same?e.Q:e.P)).Norm()<=Tol,"Coedge oriented endpoints disagree with edge");
                    l.Co.Add(c);
                }
                Need(l.Co.Count>0&&l.Co.Count==AB.Int(AB.Get(lo,"coedgeCount"))&&l.Co.Select(c=>c.Id).Distinct().Count()==l.Co.Count,"Coedge enumeration incomplete");
                var ordered=new List<GC06>();var cur=l.Co[0];
                do {Need(!ordered.Any(c=>c.Id==cur.Id),"Coedge cycle closes prematurely");ordered.Add(cur);cur=l.Co.Single(c=>c.Id==cur.Next);}while(cur.Id!=ordered[0].Id);
                Need(ordered.Count==l.Co.Count,"Disconnected loop cycle");l.Co=ordered;
                for(int i=0;i<l.Co.Count;i++)Check(b,"loopEndpointConnectivity",id,(l.Co[i].Q-l.Co[(i+1)%l.Co.Count].P).Norm()<=Tol);
                f.Loops.Add(l);
            }
            b.Faces.Add(f);NativeDomain(b,f);
        }catch(Exception ex){if(ex is NotSupportedException)b.Unsupported.Add("face "+BatchV05.Get(fo,"id")+": "+ex.Message);else b.Errors.Add("face "+BatchV05.Get(fo,"id")+": "+ex.Message);}
        foreach(var e in b.Edges){
            var uses=b.Faces.SelectMany(f=>f.Loops.SelectMany(l=>l.Co.Where(c=>c.E.Id==e.Id).Select(c=>new{face=f.S.Id,co=c}))).ToArray();
            Check(b,"manifoldAdjacencyAndOppositeUses",e.Id,uses.Length==2&&uses[0].face!=uses[1].face&&uses[0].co.Sense!=uses[1].co.Sense&&uses.Select(x=>x.face).OrderBy(x=>x).SequenceEqual(e.Adj.OrderBy(x=>x)));
        }
        Check(b,"completeEnumeration",-1,b.Edges.Count==AB.Int(AB.Get(raw,"edgeCount"))&&b.Faces.Count==AB.Int(AB.Get(raw,"faceCount"))&&AB.Array(AB.Get(raw,"verticesM")).Length==AB.Int(AB.Get(raw,"vertexCount")));
        var vertices=AB.Array(AB.Get(raw,"verticesM")).Select(v=>NV.Read(AB.Get(v,"point"))).ToArray();
        foreach(var v in vertices)Check(b,"nativeVertexBelongsToBoundedEdge",-1,b.Edges.Any(e=>(e.P-v).Norm()<=Tol||(e.Q-v).Norm()<=Tol));
        foreach(var e in b.Edges.Where(e=>!e.Full))Check(b,"boundedEndpointsBelongToNativeVertices",e.Id,vertices.Any(v=>(v-e.P).Norm()<=Tol)&&vertices.Any(v=>(v-e.Q).Norm()<=Tol));
        if(BatchV05.Get(raw,"readErrors")!=null&&AB.Array(AB.Get(raw,"readErrors")).Length>0)b.Errors.Add("Source API errors present");
        return b;
    }
    static void NativeDomain(GB06 b,GF06 f){
        Need(f.Loops.Count>0,"Missing loops");
        foreach(var l in f.Loops)foreach(var co in l.Co){
            var e=co.E;double err;
            if(f.S.Type=="PLANE"){
                err=e.Line?Math.Max(Math.Abs(NV.Dot(f.S.N,e.P-f.S.O)),Math.Abs(NV.Dot(f.S.N,e.Q-f.S.O))):
                    Math.Abs(NV.Dot(f.S.N,e.C-f.S.O))+Math.Abs(NV.Dot(f.S.N,e.Conic.A))+Math.Abs(NV.Dot(f.S.N,e.Conic.B));
            }else{
                var n=f.S.N;var d=e.C-f.S.O;d=d-n*NV.Dot(n,d);
                var a=e.Line?(e.Q-e.P)*.5:e.Conic.A;var v=e.Line?new NV(0,0,0):e.Conic.B;
                a=a-n*NV.Dot(n,a);v=v-n*NV.Dot(n,v);
                double square=Math.Abs(NV.Dot(d,d)+(e.Line?0:(NV.Dot(a,a)+NV.Dot(v,v))/2)-f.S.Radius*f.S.Radius)+2*Math.Abs(NV.Dot(d,a))+2*Math.Abs(NV.Dot(d,v));
                square+=e.Line?NV.Dot(a,a):Math.Abs(NV.Dot(a,a)-NV.Dot(v,v))/2+Math.Abs(NV.Dot(a,v));
                err=square/f.S.Radius;
            }
            Check(b,"wholeBoundedEdgeOnSurface",e.Id,NV.Finite(err)&&err<=Tol,err);
        }
        if(f.S.Type=="CYLINDER"){
            if(f.Loops.Count!=2||f.Loops.Any(l=>l.Co.Count!=1||!l.Co[0].E.Full)){
                b.Unsupported.Add("face "+f.S.Id+": multi-edge cylinder domain coverage/pcurve validation not yet complete (topology is read)");return;}
            var x=f.Loops[0].Co[0];var y=f.Loops[1].Co[0];double delta=NV.Dot(f.S.N,y.E.C-x.E.C);
            double ax=Math.Sqrt(Math.Pow(NV.Dot(f.S.N,x.E.Conic.A),2)+Math.Pow(NV.Dot(f.S.N,x.E.Conic.B),2));
            double ay=Math.Sqrt(Math.Pow(NV.Dot(f.S.N,y.E.Conic.A),2)+Math.Pow(NV.Dot(f.S.N,y.E.Conic.B),2));
            Check(b,"cylinderBandOrdering",f.S.Id,Math.Abs(delta)>ax+ay);
            int wx=Math.Sign(NV.Dot(f.S.N,x.E.Conic.Normal))*(x.Sense==x.E.Sense?1:-1);
            int wy=Math.Sign(NV.Dot(f.S.N,y.E.Conic.Normal))*(y.Sense==y.E.Sense?1:-1);
            Check(b,"cylinderMaterialWinding",f.S.Id,wx==-wy&&wx==Math.Sign(delta)*f.S.MaterialSign);return;
        }
        var outers=f.Loops.Where(l=>l.Outer).ToArray();Need(outers.Length==1,"Plane needs exactly one outer loop");var outer=outers[0];
        if(outer.Co.Any(c=>!c.E.Line)){b.Unsupported.Add("face "+f.S.Id+": general curved planar domain containment not implemented");return;}
        // Convex polygon proof by signed halfplanes over ALL vertices, not sampled edges.
        var polygonNormal=f.S.N;var points=outer.Co.Select(c=>c.P).ToArray();
        NV area=new NV(0,0,0);for(int i=0;i<points.Length;i++)area+=NV.Cross(points[i]-points[0],points[(i+1)%points.Length]-points[0]);
        Check(b,"polygonMaterialWinding",f.S.Id,NV.Dot(area,polygonNormal)>0);
        foreach(var co in outer.Co){var inward=NV.Cross(polygonNormal,co.Q-co.P).Unit();
            Check(b,"convexPolygonWholeDomain",f.S.Id,points.All(p=>NV.Dot(inward,p-co.P)>=-Tol));
            foreach(var hole in f.Loops.Where(l=>!l.Outer)){
                if(hole.Co.Count!=1||!hole.Co[0].E.Full){b.Unsupported.Add("face "+f.S.Id+": non-conic inner loop containment unsupported");continue;}
                var h=hole.Co[0];var c=h.E.Conic;double clearance=NV.Dot(inward,c.C-co.P)-Math.Sqrt(Math.Pow(NV.Dot(inward,c.A),2)+Math.Pow(NV.Dot(inward,c.B),2));
                Check(b,"wholeHoleInsidePolygonHalfplane",f.S.Id,clearance>Tol);
                Check(b,"holeMaterialWinding",f.S.Id,Math.Sign(NV.Dot(polygonNormal,c.Normal))*(h.Sense==h.E.Sense?1:-1)==-1);
            }
        }
        var holes=f.Loops.Where(l=>!l.Outer&&l.Co.Count==1&&l.Co[0].E.Full).Select(l=>l.Co[0].E.Conic).ToArray();
        for(int i=0;i<holes.Length;i++)for(int j=i+1;j<holes.Length;j++)Check(b,"holeDisjointness",f.S.Id,(holes[i].C-holes[j].C).Norm()>Math.Max(holes[i].A.Norm(),holes[i].B.Norm())+Math.Max(holes[j].A.Norm(),holes[j].B.Norm())+Tol);
    }
    class F {public NV D;public string Kind;public double Size;}
    static List<F> Features(GB06 b){
        var list=b.Faces.Select(f=>new F{D=f.S.N,Kind=f.S.Type,Size=f.S.Radius}).ToList();
        foreach(var e in b.Edges)if(e.Line)list.Add(new F{D=(e.Q-e.P).Unit(),Kind="SEGMENT",Size=(e.Q-e.P).Norm()});
            else {list.Add(new F{D=e.Conic.Normal,Kind="CONIC_NORMAL",Size=e.Conic.A.Norm()});
                if(Math.Abs(e.Conic.A.Norm()-e.Conic.B.Norm())>Tol)list.Add(new F{D=e.Conic.A.Unit(),Kind="ELLIPSE_AXIS",Size=e.Conic.A.Norm()});}
        return list;
    }
    static NV[] Frame(NV x,NV y){x=x.Unit();y=y-x*NV.Dot(x,y);if(y.Norm()<1e-7)return null;y=y.Unit();return new[]{x,y,NV.Cross(x,y).Unit()};}
    public static List<NTransform> Candidates(GB06 a,GB06 b){
        var fa=Features(a);var fb=Features(b);var result=new Dictionary<string,NTransform>();
        var ca=a.Edges.Aggregate(new NV(0,0,0),(c,e)=>c+e.C)*(1d/a.Edges.Count);
        var cb=b.Edges.Aggregate(new NV(0,0,0),(c,e)=>c+e.C)*(1d/b.Edges.Count);
        for(int i=0;i<fa.Count;i++)for(int j=i+1;j<fa.Count;j++){
            var x=fa[i];var y=fa[j];var af=Frame(x.D,y.D);if(af==null)continue;
            foreach(var u in fb.Where(f=>f.Kind==x.Kind&&Math.Abs(f.Size-x.Size)<=Tol))foreach(var v in fb.Where(f=>f.Kind==y.Kind&&Math.Abs(f.Size-y.Size)<=Tol))
                for(int s=-1;s<=1;s+=2)for(int q=-1;q<=1;q+=2){
                    if(Math.Abs(NV.Dot(x.D,y.D)-s*q*NV.Dot(u.D,v.D))>1e-8)continue;var bf=Frame(u.D*s,v.D*q);if(bf==null)continue;
                    for(int parity=-1;parity<=1;parity+=2){
                        var m=new[]{new double[4],new double[4],new double[4],new[]{0d,0,0,1}};
                        for(int r=0;r<3;r++)for(int c=0;c<3;c++)for(int k=0;k<3;k++)m[r][c]+=bf[k].Data()[r]*af[k].Data()[c]*(k==2?parity:1);
                        var t=new NTransform(m);var shift=cb-t.Vector(ca);m[0][3]=shift.X;m[1][3]=shift.Y;m[2][3]=shift.Z;t=new NTransform(m);
                        string key=String.Join(",",m.Take(3).SelectMany(row=>row).Select(z=>Math.Round(z,11).ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
                        if(NB03.Passed(TS04.Check(t)))result[key]=t;
                    }
                }
        }
        return result.Values.ToList();
    }
    static List<int[]> Maps(bool[][] valid,out bool truncated){
        var result=new List<int[]>();var map=new int[valid.Length];var used=new bool[valid.Length];bool cut=false;Action<int> walk=null;
        walk=i=>{if(result.Count>=4096){cut=true;return;}if(i==map.Length){result.Add((int[])map.Clone());return;}
            for(int j=0;j<used.Length;j++)if(!used[j]&&valid[i][j]){used[j]=true;map[i]=j;walk(i+1);used[j]=false;}};
        walk(0);truncated=cut;return result;
    }
    static object Surface(GF06 a,GF06 b,NTransform t){
        if(a.S.Type!=b.S.Type)return new{passed=false,diagnosticEnvelopeM=(object)null};
        var x=a.S;var y=b.S;var o=t.Point(x.O);var n=t.Vector(x.N).Unit();double reach=0;
        foreach(var e in a.Loops.SelectMany(l=>l.Co.Select(c=>c.E)).Distinct()){
            reach=Math.Max(reach,e.Line?Math.Max((e.P-x.O).Norm(),(e.Q-x.O).Norm()):(e.C-x.O).Norm()+e.Conic.A.Norm()+e.Conic.B.Norm());
        }
        double error,angle;bool material;
        if(x.Type=="PLANE"){angle=(n-y.N).Norm();material=NV.Dot(n,y.N)>0;error=Math.Abs(NV.Dot(y.N,o-y.O))+angle*reach;}
        else {angle=Math.Min((n-y.N).Norm(),(n+y.N).Norm());var d=o-y.O;error=(d-y.N*NV.Dot(y.N,d)).Norm()+angle*reach+Math.Abs(x.Radius-y.Radius);material=x.MaterialSign==y.MaterialSign;}
        return new{passed=material&&error<=Tol+NB03.Tolerance.LengthGuard(o.Norm()+reach),diagnosticEnvelopeM=(object)error,materialCompatible=material,certified=false};
    }
    static bool LoopMatch(GL06 a,GL06 b,Func<int,int> edge,Dictionary<int,int> directions,int parity){
        if(a.Outer!=b.Outer||a.Co.Count!=b.Co.Count)return false;int n=a.Co.Count;
        for(int start=0;start<n;start++){
            bool ok=true;
            for(int i=0;i<n;i++){
                var x=a.Co[i];var y=b.Co[(start+parity*i+n*2)%n];
                int sa=x.Sense==x.E.Sense?1:-1,sb=y.Sense==y.E.Sense?1:-1;
                ok&=edge(x.E.Id)==y.E.Id&&sa*directions[x.E.Id]*parity==sb;
            }
            if(ok)return true;
        }
        return false;
    }
    public static object Direction(GB06 a,GB06 b,NTransform t){
        if(a.Edges.Count!=b.Edges.Count||a.Faces.Count!=b.Faces.Count)return new{passed=false,reason="Native entity counts differ; not a certified geometric classification"};
        var ec=a.Edges.Select(e=>b.Edges.Select(g=>EdgeMatch(e.Move(t),g)).ToArray()).ToArray();
        var sc=a.Faces.Select(f=>b.Faces.Select(g=>Surface(f,g,t)).ToArray()).ToArray();
        int parity=TS04.Det(t)<0?-1:1;bool truncated;
        foreach(var em in Maps(ec.Select(row=>row.Select(NB03.Passed).ToArray()).ToArray(),out truncated)){
            Func<int,int> edge=id=>b.Edges[em[a.Edges.FindIndex(e=>e.Id==id)]].Id;
            var directions=Enumerable.Range(0,em.Length).ToDictionary(i=>a.Edges[i].Id,i=>AB.Int(AB.Get(ec[i][em[i]],"parameterDirection")));
            var fmok=sc.Select(row=>row.Select(NB03.Passed).ToArray()).ToArray();
            for(int i=0;i<a.Faces.Count;i++)for(int j=0;j<b.Faces.Count;j++){
                var x=a.Faces[i];var y=b.Faces[j];fmok[i][j]&=x.Loops.Count==y.Loops.Count;
                foreach(var l in x.Loops)fmok[i][j]&=y.Loops.Count(k=>LoopMatch(l,k,edge,directions,parity))==1;
            }
            bool faceCut;foreach(var fm in Maps(fmok,out faceCut)){
                Func<int,int> face=id=>b.Faces[fm[a.Faces.FindIndex(f=>f.S.Id==id)]].S.Id;
                bool adjacent=true;for(int i=0;i<em.Length;i++)adjacent&=a.Edges[i].Adj.Select(face).OrderBy(x=>x).SequenceEqual(b.Edges[em[i]].Adj.OrderBy(x=>x));
                if(!adjacent)continue;
                var edges=Enumerable.Range(0,em.Length).Select(i=>new{input=a.Edges[i].Id,target=b.Edges[em[i]].Id,check=ec[i][em[i]]}).ToArray();
                var faces=Enumerable.Range(0,fm.Length).Select(i=>new{input=a.Faces[i].S.Id,target=b.Faces[fm[i]].S.Id,check=sc[i][fm[i]]}).ToArray();
                double max=Math.Max(edges.Max(e=>AB.Num(AB.Get(e.check,"diagnosticWholeBoundedCurveEnvelopeM"))),faces.Max(f=>AB.Num(AB.Get(f.check,"diagnosticEnvelopeM"))));
                return new{passed=true,edgeMap=edges,faceMap=faces,diagnosticMaximumEnvelopeM=max,loopCoedgeCyclicOrientationCompatible=true,adjacencyCompatible=true,mappingSearchTruncated=truncated||faceCut,certified=false};
            }
        }
        return new{passed=false,reason="No complete bounded curve/surface/ordered-loop/material/adjacency bijection",contourChecks=ec,surfaceChecks=sc,mappingSearchTruncated=truncated};
    }
    public static object Summary(GB06 b){return new{accepted=b.Accepted,unsupported=b.Unsupported,incomplete=b.Errors,nativeChecks=b.Checks,certificateAvailable=false};}
    public static object Verify(object ra,object rb,object api){
        var a=Parse(ra);var b=Parse(rb);
        var r=new Dictionary<string,object>{{"classification","NEEDS_REVIEW"},{"brepEquivalenceVerified",false},{"maximumDeviationM",null},{"certifiedUpperBoundM",null},
            {"bodyAValidation",Summary(a)},{"bodyBValidation",Summary(b)},{"supportedDiagnosticsCompleted",false}};
        r["unfinishedChecks"]=new[]{"Outward-rounded whole-BREP deviation bound and COM uncertainty enclosure","Certified complete trimmed-domain/pcurve equivalence","Exhaustiveness under all continuous symmetries"};
        if(!a.Accepted||!b.Accepted){r["reason"]="Unsupported/incomplete native BREP; see validations. No fitted, sampled or size-only fallback.";return r;}
        var candidates=Candidates(a,b);int analyticCount=candidates.Count;var sources=candidates.Select(t=>"ANALYTIC_FEATURE_FRAME").ToList();
        foreach(var key in new[]{"coincidenceAtoB","coincidenceBtoA"})try{
            var c=BatchV05.Get(api,key);if(c==null||!BatchV05.Flag(c,"apiReturned"))continue;
            var tr=AB.Get(c,"transform");var raw=BatchV05.Get(tr,"rawArrayData");
            if(raw!=null){var data=Arr(raw);Need(data.Length==16&&NV.Finite(data[12])&&Math.Abs(data[12]-1)<=Num,"Invalid raw API scale");}
            var t=new NTransform(AB.Get(tr,"homogeneousColumnVectorMatrix"));if(key=="coincidenceBtoA")t=TS04.Inverse(t);
            candidates.Add(t);sources.Add(key);
        }catch(Exception ex){r[key+"InputError"]=ex.Message;}
        var rows=new List<object>();
        for(int i=0;i<candidates.Count;i++){
            var t=candidates[i];var tc=TS04.Check(t);object f=null,rev=null;bool ok=false;object max=null;
            if(NB03.Passed(tc)){f=Direction(a,b,t);rev=Direction(b,a,TS04.Inverse(t));ok=NB03.Passed(f)&&NB03.Passed(rev);
                if(ok)max=Math.Max(AB.Num(AB.Get(f,"diagnosticMaximumEnvelopeM")),AB.Num(AB.Get(rev,"diagnosticMaximumEnvelopeM")));}
            rows.Add(new{source=sources[i],matrix=t.M,parity=TS04.Det(t)<0?-1:1,transformChecks=tc,AtoB=f,BtoA=rev,diagnosticsCompatible=ok,diagnosticMaximumEnvelopeM=max});
        }
        var accepted=rows.Where(x=>BatchV05.Flag(x,"diagnosticsCompatible")).OrderBy(x=>AB.Num(AB.Get(x,"diagnosticMaximumEnvelopeM"))).ThenByDescending(x=>AB.Int(AB.Get(x,"parity"))).ToArray();
        r["candidateCount"]=rows.Count;r["automaticCandidateCount"]=analyticCount;r["candidates"]=rows;r["acceptedCandidateCount"]=accepted.Length;
        r["bestCandidate"]=accepted.FirstOrDefault();r["bestProperCandidate"]=accepted.FirstOrDefault(x=>AB.Int(AB.Get(x,"parity"))==1);
        r["bestImproperCandidate"]=accepted.FirstOrDefault(x=>AB.Int(AB.Get(x,"parity"))==-1);
        r["properDiagnosticMatchFound"]=r["bestProperCandidate"]!=null;r["improperDiagnosticMatchFound"]=r["bestImproperCandidate"]!=null;
        r["supportedDiagnosticsCompleted"]=true;r["reason"]="Complete supported native contour/topology diagnostics are not certified bounded BREP equivalence";
        return r;
    }
}
