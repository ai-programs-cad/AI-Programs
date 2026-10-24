using System;
using System.Linq;
using System.Collections.Generic;

public class V03Tolerance {
    public double GeometryM=1e-6; // 0.001 mm
    public double ComputationalDimensionless=1e-10;
    public double MachineEpsilon=2.2204460492503131e-16;
    public double LengthGuard(double scale){return 128*MachineEpsilon*Math.Max(1,scale);}
}
public class NV {
    public double X,Y,Z;
    public NV(double x,double y,double z){X=x;Y=y;Z=z;}
    public double[] Data(){return new[]{X,Y,Z};}
    public static NV Read(object o){var a=AB.Array(o).Select(AB.Num).ToArray();if(a.Length!=3||a.Any(x=>!Finite(x)))throw new InvalidOperationException("Expected three finite coordinates");return new NV(a[0],a[1],a[2]);}
    public static bool Finite(double x){return !Double.IsNaN(x)&&!Double.IsInfinity(x);}
    public static NV operator +(NV a,NV b){return new NV(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
    public static NV operator -(NV a,NV b){return new NV(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
    public static NV operator *(NV a,double s){return new NV(a.X*s,a.Y*s,a.Z*s);}
    public static double Dot(NV a,NV b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
    public static NV Cross(NV a,NV b){return new NV(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);}
    public double Norm(){double m=Math.Max(Math.Abs(X),Math.Max(Math.Abs(Y),Math.Abs(Z)));return m==0?0:m*Math.Sqrt((X/m)*(X/m)+(Y/m)*(Y/m)+(Z/m)*(Z/m));}
    public NV Unit(){double n=Norm();if(!Finite(n)||n==0)throw new InvalidOperationException("Zero/nonfinite direction");return this*(1/n);}
}
public class NTransform {
    public double[][] M;
    public NTransform(object value){M=AB.Array(value).Select(r=>AB.Array(r).Select(AB.Num).ToArray()).ToArray();if(M.Length!=4||M.Any(r=>r.Length!=4))throw new InvalidOperationException("Expected 4x4 matrix");}
    public NV Vector(NV v){return new NV(M[0][0]*v.X+M[0][1]*v.Y+M[0][2]*v.Z,M[1][0]*v.X+M[1][1]*v.Y+M[1][2]*v.Z,M[2][0]*v.X+M[2][1]*v.Y+M[2][2]*v.Z);}
    public NV Point(NV v){return Vector(v)+new NV(M[0][3],M[1][3],M[2][3]);}
    public object Check(V03Tolerance tol,object rawData=null){
        bool finite=M.All(r=>r.All(NV.Finite));
        if(!finite)return new {passed=false,finite=false,reason="Nonfinite rotation, translation or homogeneous matrix component"};
        var a=Vector(new NV(1,0,0));var b=Vector(new NV(0,1,0));var c=Vector(new NV(0,0,1));
        double det=NV.Dot(a,NV.Cross(b,c));double orth=0;
        var cols=new[]{a,b,c};for(int i=0;i<3;i++)for(int j=0;j<3;j++)orth=Math.Max(orth,Math.Abs(NV.Dot(cols[i],cols[j])-(i==j?1:0)));
        double homogeneous=Math.Max(Math.Abs(M[3][3]-1),Math.Max(Math.Abs(M[3][0]),Math.Max(Math.Abs(M[3][1]),Math.Abs(M[3][2]))));
        object scale=null;bool rawScaleOk=true;
        if(rawData!=null){var data=AB.Array(rawData).Select(AB.Num).ToArray();rawScaleOk=data.Length==16&&NV.Finite(data[12])&&Math.Abs(data[12]-1)<=tol.ComputationalDimensionless;scale=data.Length==16&&NV.Finite(data[12])?(object)data[12]:null;}
        bool passed=NV.Finite(det)&&NV.Finite(orth)&&Math.Abs(det-1)<=tol.ComputationalDimensionless&&orth<=tol.ComputationalDimensionless&&homogeneous<=tol.ComputationalDimensionless&&rawScaleOk;
        return new {passed,finite=true,determinant=FiniteValue(det),orthogonalityResidual=FiniteValue(orth),homogeneousResidual=homogeneous,
            explicitScale=scale,explicitScaleChecked=rawData!=null,rawScaleOk,translationM=new[]{M[0][3],M[1][3],M[2][3]},translationFinite=true,
            computationalTolerance=tol.ComputationalDimensionless,type=passed?"PROPER_RIGID_WITHIN_NUMERICAL_TOLERANCE":"INVALID_OR_IMPROPER_TRANSFORM",
            geometryEquivalenceProven=false,orthogonalizationApplied=false};
    }
    static object FiniteValue(double x){return NV.Finite(x)?(object)x:null;}
}
public class NConic {
    public int Id;public NV C,A,B;public bool EdgeSense,FullPeriod,BasisAccepted;public int[] Adj;public double CorrectionM;
    public object Source;
    public NV Normal{get{return NV.Cross(A,B).Unit();}}
    public NConic Transform(NTransform t){return new NConic{Id=Id,C=t.Point(C),A=t.Vector(A),B=t.Vector(B),EdgeSense=EdgeSense,FullPeriod=FullPeriod,BasisAccepted=BasisAccepted,Adj=Adj,CorrectionM=CorrectionM,Source=Source};}
}
public class NLoop {public int Id;public bool Outer,Sense;public NConic Edge;}
public class NFace {
    public int Id;public string Type;public NV O,N;public double Radius;public int MaterialSign;public object Source;public List<NLoop> Loops=new List<NLoop>();
    public double Reach(){return Loops.Count==0?0:Loops.Max(l=>(l.Edge.C-O).Norm()+l.Edge.A.Norm()+l.Edge.B.Norm());}
    public NFace Transform(NTransform t){return new NFace{Id=Id,Type=Type,O=t.Point(O),N=t.Vector(N).Unit(),Radius=Radius,MaterialSign=MaterialSign,Source=Source,Loops=Loops.Select(l=>new NLoop{Id=l.Id,Outer=l.Outer,Sense=l.Sense,Edge=l.Edge.Transform(t)}).ToList()};}
}
public class NBody {public List<NConic> Edges=new List<NConic>();public List<NFace> Faces=new List<NFace>();public List<object> Numeric=new List<object>();public List<object> Topology=new List<object>();public List<object> Domain=new List<object>();public List<object> Issues=new List<object>();}

// Non-sampling analytic diagnostics. Floating envelopes are NOT interval certificates.
// This version deliberately cannot emit IDENTICAL_3D without a certified bound backend.
public static class NB03 {
    public static V03Tolerance Tolerance=new V03Tolerance();
    public static bool Passed(object o){object v;return AB.TryGet(o,"passed",out v)&&v is bool&&(bool)v;}
    static double[] Doubles(object x,int count){var a=AB.Array(x).Select(AB.Num).ToArray();if(a.Length!=count||a.Any(v=>!NV.Finite(v)))throw new InvalidOperationException("Expected "+count+" finite parameters");return a;}
    static NV Vec(double[] a,int offset){return new NV(a[offset],a[offset+1],a[offset+2]);}
    static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    static object Missing(string section,int id,Exception ex){return new {section,id,status="INCOMPLETE",reason=ex.Message};}
    static bool Flag(object o,string field){return AB.Bool(AB.Get(o,field));}
    public static NBody Parse(object source){
        var b=new NBody();
        object edges,faces;
        if(!AB.TryGet(source,"edges",out edges)||edges==null){b.Issues.Add(new{section="edges",reason="Missing edges array"});edges=new object[0];}
        if(!AB.TryGet(source,"faces",out faces)||faces==null){b.Issues.Add(new{section="faces",reason="Missing faces array"});faces=new object[0];}
        foreach(var e in AB.Array(edges)){
            int id=-1;try {
                id=AB.Int(AB.Get(e,"id"));Need(!b.Edges.Any(x=>x.Id==id),"Duplicate edge ID");Need(Flag(e,"ownerVerified"),"Edge owner not verified");
                var cv=AB.Get(e,"curve");NV center,u,v;double ra,rb;bool isEllipse=Flag(cv,"isEllipse");
                if(isEllipse){var p=Doubles(AB.Get(cv,"ellipseParams"),11);center=Vec(p,0);u=Vec(p,4);v=Vec(p,8);ra=p[3];rb=p[7];}
                else if(Flag(cv,"isCircle")){var p=Doubles(AB.Get(cv,"circleParams"),7);center=Vec(p,0);var normal=Vec(p,3).Unit();var helper=Math.Abs(normal.X)<.8?new NV(1,0,0):new NV(0,1,0);u=NV.Cross(normal,helper).Unit();v=NV.Cross(normal,u).Unit();ra=rb=p[6];}
                else throw new InvalidOperationException("Only full circular/elliptical curves supported");
                Need(ra>0&&rb>0,"Nonpositive conic radius");double un=u.Norm(),vn=v.Norm();double ortho=Math.Abs(NV.Dot(u.Unit(),v.Unit()));
                double normErr=Math.Max(Math.Abs(un-1),Math.Abs(vn-1));bool basis=normErr<=Tolerance.ComputationalDimensionless&&ortho<=Tolerance.ComputationalDimensionless;
                var unitU=u.Unit();var unitV=(v-unitU*NV.Dot(unitU,v)).Unit();
                var rawA=u*ra;var rawB=v*rb;var a=unitU*ra;var c=unitV*rb;double correction=(rawA-a).Norm()+(rawB-c).Norm();
                b.Numeric.Add(new{entity="edge",id,check="axisNormalizationAndOrthogonality",passed=basis,normError=normErr,normalizedDotError=ortho,computationalTolerance=Tolerance.ComputationalDimensionless,derivedBasisCorrectionEnvelopeM=correction,originalParametersPreserved=true});
                var domain=AB.Get(cv,"underlyingDomain");var bounds=AB.Get(e,"bounds");
                double period=AB.Num(AB.Get(domain,"end"))-AB.Num(AB.Get(domain,"start"));double span=Math.Abs(AB.Num(AB.Get(bounds,"uMax"))-AB.Num(AB.Get(bounds,"uMin")));
                bool full=Flag(domain,"returned")&&Flag(domain,"closed")&&Flag(domain,"periodic")&&period>0&&Math.Abs(span-period)<=Tolerance.ComputationalDimensionless*Math.Max(1,Math.Abs(period));
                var start=NV.Read(AB.Get(bounds,"startPointM"));var end=NV.Read(AB.Get(bounds,"endPointM"));double gap=(start-end).Norm();
                full=full&&gap<=Tolerance.LengthGuard(center.Norm()+ra+rb);
                var nc=new NConic{Id=id,C=center,A=a,B=c,CorrectionM=correction,BasisAccepted=basis,FullPeriod=full,Source=e,EdgeSense=Flag(bounds,"sense"),Adj=AB.Array(AB.Get(e,"adjacentFaceIds")).Select(AB.Int).ToArray()};
                b.Edges.Add(nc);b.Topology.Add(new{entity="edge",id,check="closedFullPeriod",passed=full,periodResidual=Math.Abs(span-period),closureGapM=gap,noVerticesRequired=true});
                double endpointResidual=Math.Max(PointOnConicResidual(nc,start),PointOnConicResidual(nc,end));
                b.Domain.Add(new{entity="edge",id,check="nativeEndpointsOnConic",passed=endpointResidual<=Tolerance.GeometryM+Tolerance.LengthGuard(center.Norm()+ra+rb),diagnosticResidualEnvelopeM=endpointResidual,certified=false});
            }catch(Exception ex){b.Issues.Add(Missing("edge",id,ex));}
        }
        foreach(var fo in AB.Array(faces)){
            int id=-1;try {
                id=AB.Int(AB.Get(fo,"id"));Need(!b.Faces.Any(x=>x.Id==id),"Duplicate face ID");Need(Flag(fo,"ownerVerified"),"Face owner not verified");var s=AB.Get(fo,"surface");
                var f=new NFace{Id=id,Type=(string)AB.Get(s,"type"),MaterialSign=Flag(fo,"faceInSurfaceSense")?-1:1,Source=fo};
                NV rawN;
                if(f.Type=="PLANE"){f.O=NV.Read(AB.Get(s,"rootPointM"));rawN=NV.Read(AB.Get(s,"normal"));f.N=rawN.Unit()*f.MaterialSign;f.MaterialSign=1;}
                else if(f.Type=="CYLINDER"){
                    f.O=NV.Read(AB.Get(s,"originM"));rawN=NV.Read(AB.Get(s,"axis"));f.N=rawN.Unit();f.Radius=AB.Num(AB.Get(s,"radiusM"));Need(NV.Finite(f.Radius)&&f.Radius>0,"Invalid cylinder radius");
                    int radial=AB.Int(AB.Get(fo,"naturalCylinderRadialSign"));Need(Math.Abs(radial)==1,"Natural radial sign missing");
                    var witness=NV.Read(AB.Get(fo,"naturalNormalWitnessM"));var eval=AB.Array(AB.Get(fo,"naturalNormalEvaluationRaw")).Select(AB.Num).ToArray();Need(eval.Length>=3,"Natural normal missing");
                    var d=witness-f.O;double dot=NV.Dot(Vec(eval,0),d-f.N*NV.Dot(f.N,d));Need(NV.Finite(dot)&&Math.Sign(dot)==radial&&dot!=0,"Cylinder material orientation not confirmed");f.MaterialSign*=radial;
                }else throw new InvalidOperationException("Unsupported surface: "+f.Type);
                b.Numeric.Add(new{entity="face",id,check="directionNormalization",passed=Math.Abs(rawN.Norm()-1)<=Tolerance.ComputationalDimensionless,normError=Math.Abs(rawN.Norm()-1),computationalTolerance=Tolerance.ComputationalDimensionless});
                b.Faces.Add(f);
                object loopArray;if(!AB.TryGet(fo,"loops",out loopArray)||loopArray==null){b.Issues.Add(new{section="face",id,reason="Missing trimming loops"});continue;}
                foreach(var lo in AB.Array(loopArray)){
                    try {
                        int lid=AB.Int(AB.Get(lo,"id"));var cos=AB.Array(AB.Get(lo,"coedges"));Need(!Flag(lo,"isSingular")&&cos.Length==1&&AB.Int(AB.Get(lo,"coedgeCount"))==1,"Only nonsingular full-conic single-coedge loops supported");
                        var co=cos[0];Need(AB.Int(AB.Get(co,"nextId"))==AB.Int(AB.Get(co,"id")),"Coedge linkage not closed");int eid=AB.Int(AB.Get(co,"edgeId"));var edge=b.Edges.SingleOrDefault(x=>x.Id==eid);Need(edge!=null,"Unresolved edge "+eid);
                        var raw=Doubles(AB.Get(co,"curveBoundsRaw"),10);var eb=AB.Get(edge.Source,"bounds");double span=Math.Abs(AB.Num(AB.Get(eb,"uMax"))-AB.Num(AB.Get(eb,"uMin")));double diff=Math.Abs(Math.Abs(raw[7]-raw[6])-span);
                        var p0=Vec(raw,0);var p1=Vec(raw,3);double closure=(p0-p1).Norm();double pointResidual=Math.Max(PointOnConicResidual(edge,p0),PointOnConicResidual(edge,p1));
                        bool sense=Flag(co,"sense");bool consistent=diff<=Tolerance.ComputationalDimensionless*Math.Max(1,span)&&((raw[7]>raw[6])==(sense==edge.EdgeSense))&&closure<=Tolerance.LengthGuard(edge.C.Norm()+edge.A.Norm()+edge.B.Norm());
                        f.Loops.Add(new NLoop{Id=lid,Edge=edge,Outer=Flag(lo,"isOuter"),Sense=sense});b.Topology.Add(new{entity="faceLoop",face=id,loop=lid,check="coedgePeriodAndDirection",passed=consistent,parameterSpanResidual=diff,closureGapM=closure});
                        b.Domain.Add(new{face=id,loop=lid,check="coedgeEndpointsOnConic",passed=pointResidual<=Tolerance.GeometryM+Tolerance.LengthGuard(edge.C.Norm()),diagnosticResidualEnvelopeM=pointResidual,certified=false});
                    }catch(Exception ex){b.Issues.Add(Missing("face["+id+"].loop",id,ex));}
                }
                if(f.Loops.Count==0)b.Issues.Add(new{section="face",id,reason="No usable trimming loops"});
            }catch(Exception ex){b.Issues.Add(Missing("face",id,ex));}
        }
        try{b.Topology.Add(new{check="completeEnumeration",passed=AB.Int(AB.Get(source,"faceCount"))==b.Faces.Count&&AB.Int(AB.Get(source,"edgeCount"))==b.Edges.Count});}catch(Exception ex){b.Issues.Add(Missing("counts",-1,ex));}
        foreach(var e in b.Edges){var uses=b.Faces.SelectMany(f=>f.Loops.Where(l=>l.Edge.Id==e.Id).Select(l=>new{face=f.Id,loop=l})).ToArray();
            bool ok=uses.Length==2&&uses[0].face!=uses[1].face&&uses[0].loop.Sense!=uses[1].loop.Sense&&uses.Select(x=>x.face).OrderBy(x=>x).SequenceEqual(e.Adj.OrderBy(x=>x));
            b.Topology.Add(new{entity="edge",id=e.Id,check="manifoldAdjacencyAndOppositeUses",passed=ok});}
        foreach(var f in b.Faces)DomainChecks(b,f);
        return b;
    }
    static void DomainChecks(NBody body,NFace f){
        foreach(var l in f.Loops){var c=l.Edge;double residual;
            if(f.Type=="PLANE")residual=Math.Abs(NV.Dot(f.N,c.C-f.O))+Math.Abs(NV.Dot(f.N,c.A))+Math.Abs(NV.Dot(f.N,c.B));
            else {
                var dc=c.C-f.O;var pc=dc-f.N*NV.Dot(f.N,dc);var a=c.A-f.N*NV.Dot(f.N,c.A);var b=c.B-f.N*NV.Dot(f.N,c.B);
                // Entire harmonic polynomial over theta, no sampled points.
                double q0=NV.Dot(pc,pc)+(NV.Dot(a,a)+NV.Dot(b,b))/2-f.Radius*f.Radius;
                double squareResidual=Math.Abs(q0)+2*Math.Abs(NV.Dot(pc,a))+2*Math.Abs(NV.Dot(pc,b))+Math.Abs(NV.Dot(a,a)-NV.Dot(b,b))/2+Math.Abs(NV.Dot(a,b));
                residual=squareResidual/f.Radius;
            }
            residual+=c.CorrectionM;
            body.Domain.Add(new{face=f.Id,loop=l.Id,edge=c.Id,check="wholeConicOnSupport",passed=NV.Finite(residual)&&residual<=Tolerance.GeometryM+Tolerance.LengthGuard(f.Reach()+f.O.Norm()),diagnosticResidualEnvelopeM=residual,certified=false,method=f.Type=="PLANE"?"constant + absolute sinusoid coefficients":"absolute harmonic polynomial coefficients divided by radius"});
            int direction=l.Sense==c.EdgeSense?1:-1;
            if(f.Type=="PLANE")body.Topology.Add(new{face=f.Id,loop=l.Id,check="materialWinding",passed=Math.Sign(NV.Dot(f.N,c.Normal))*direction==(l.Outer?1:-1)});
        }
        if(f.Type=="PLANE"){
            var outer=f.Loops.Where(l=>l.Outer).ToArray();body.Topology.Add(new{face=f.Id,check="singleOuterPlaneLoop",passed=outer.Length==1});
            if(outer.Length==1){var frame=outer[0].Edge;var holes=f.Loops.Where(l=>!l.Outer).ToArray();
                foreach(var h in holes){var offset=Coordinates(frame,h.Edge.C-frame.C);double bound=Norm2(offset)+RadiusBound(frame,h.Edge);body.Domain.Add(new{face=f.Id,loop=h.Id,check="innerLoopContainment",passed=bound<1-Tolerance.ComputationalDimensionless,diagnosticNormalizedEnvelope=bound,certified=false});}
                for(int i=0;i<holes.Length;i++)for(int j=i+1;j<holes.Length;j++){double d=Norm2(Coordinates(frame,holes[i].Edge.C-holes[j].Edge.C));double sum=RadiusBound(frame,holes[i].Edge)+RadiusBound(frame,holes[j].Edge);body.Domain.Add(new{face=f.Id,check="innerLoopDisjointness",loopA=holes[i].Id,loopB=holes[j].Id,passed=d>sum+Tolerance.ComputationalDimensionless,certified=false});}
            }
        }else{
            bool supported=f.Loops.Count==2&&f.Loops.All(l=>l.Outer);body.Topology.Add(new{face=f.Id,check="fullCylinderBandStructure",passed=supported});
            if(supported){var a=f.Loops[0];var b=f.Loops[1];double delta=NV.Dot(f.N,b.Edge.C-a.Edge.C);
                double ampA=Hypot(NV.Dot(f.N,a.Edge.A),NV.Dot(f.N,a.Edge.B)),ampB=Hypot(NV.Dot(f.N,b.Edge.A),NV.Dot(f.N,b.Edge.B));
                int wa=Math.Sign(NV.Dot(f.N,a.Edge.Normal))*(a.Sense==a.Edge.EdgeSense?1:-1),wb=Math.Sign(NV.Dot(f.N,b.Edge.Normal))*(b.Sense==b.Edge.EdgeSense?1:-1);
                body.Domain.Add(new{face=f.Id,check="bandBoundaryOrdering",passed=Math.Abs(delta)>ampA+ampB+Tolerance.LengthGuard(f.Reach()),axialCenterSeparationM=Math.Abs(delta),sumAnalyticAmplitudesM=ampA+ampB,certified=false});
                body.Topology.Add(new{face=f.Id,check="cylinderMaterialAndWinding",passed=wa==-wb&&wa==Math.Sign(delta)*f.MaterialSign});}
        }
    }
    static double[] Coordinates(NConic c,NV p){return new[]{NV.Dot(c.A,p)/NV.Dot(c.A,c.A),NV.Dot(c.B,p)/NV.Dot(c.B,c.B)};}
    static double PointOnConicResidual(NConic c,NV point){var d=point-c.C;var uv=Coordinates(c,d);return Math.Abs(NV.Dot(c.Normal,d))+Math.Max(c.A.Norm(),c.B.Norm())*Math.Abs(Norm2(uv)-1)+c.CorrectionM;}
    static double Hypot(double x,double y){return new NV(x,y,0).Norm();}
    static double Norm2(double[] v){return Hypot(v[0],v[1]);}
    static double RadiusBound(NConic frame,NConic c){var a=Coordinates(frame,c.A);var b=Coordinates(frame,c.B);double row=Math.Max(Math.Abs(a[0])+Math.Abs(b[0]),Math.Abs(a[1])+Math.Abs(b[1]));double col=Math.Max(Math.Abs(a[0])+Math.Abs(a[1]),Math.Abs(b[0])+Math.Abs(b[1]));return Math.Sqrt(row*col);}
    public static object ConicResidual(NConic input,NConic target){
        double m00=NV.Dot(target.A,input.A),m01=NV.Dot(target.A,input.B),m10=NV.Dot(target.B,input.A),m11=NV.Dot(target.B,input.B);
        double best=Double.PositiveInfinity;int orientation=0;double[] bestPhase=null;
        for(int sign=-1;sign<=1;sign+=2){double x=sign==1?m00+m11:m00-m11;double y=sign==1?m10-m01:m01+m10;double n=Hypot(x,y);if(n==0)continue;double c=x/n,s=y/n;
            NV a=target.A*c+target.B*s;NV b=sign==1?target.A*(-s)+target.B*c:target.A*s-target.B*c;
            double bound=(input.C-target.C).Norm()+(input.A-a).Norm()+(input.B-b).Norm()+input.CorrectionM+target.CorrectionM;
            if(bound<best){best=bound;orientation=sign;bestPhase=new[]{c,s};}}
        bool valid=input.FullPeriod&&target.FullPeriod&&input.BasisAccepted&&target.BasisAccepted&&NV.Finite(best);
        return new {passed=valid&&best<=Tolerance.GeometryM+Tolerance.LengthGuard(input.C.Norm()+target.C.Norm()),inputEdge=input.Id,targetEdge=target.Id,
            diagnosticWholeCurveEnvelopeM=NV.Finite(best)?(object)best:null,centerDistanceM=(input.C-target.C).Norm(),parameterOrientation=orientation,phaseCosSin=bestPhase,
            certified=false,method="Analytic coefficient correspondence for every theta; triangle inequality; floating arithmetic not outward-rounded",fullPeriod=input.FullPeriod&&target.FullPeriod};
    }
    static object SurfaceResidual(NFace a,NFace b){
        if(a.Type!=b.Type)return new{passed=false,inputFace=a.Id,targetFace=b.Id,reason="Different surface types"};
        double residual,angle;bool material;
        if(a.Type=="PLANE"){
            angle=(a.N-b.N).Norm();material=NV.Dot(a.N,b.N)>0;
            residual=Math.Abs(NV.Dot(b.N,a.O-b.O))+angle*a.Reach();
        }else{
            angle=Math.Min((a.N-b.N).Norm(),(a.N+b.N).Norm());var d=a.O-b.O;double offset=(d-b.N*NV.Dot(b.N,d)).Norm();
            residual=offset+angle*a.Reach()+Math.Abs(a.Radius-b.Radius);material=a.MaterialSign==b.MaterialSign;
        }
        return new{passed=material&&NV.Finite(residual)&&residual<=Tolerance.GeometryM+Tolerance.LengthGuard(a.Reach()+a.O.Norm()+b.O.Norm()),inputFace=a.Id,targetFace=b.Id,
            type=a.Type,diagnosticBoundedSupportEnvelopeM=residual,directionResidual=angle,materialOrientationCompatible=material,certified=false};
    }
    static object BodySummary(NBody b){return new{numericalChecks=b.Numeric,topologyChecks=b.Topology,trimChecks=b.Domain,incompleteChecks=b.Issues,
        numericalAccepted=b.Numeric.Count>0&&b.Numeric.All(Passed)&&b.Issues.Count==0,topologyCompatible=b.Topology.Count>0&&b.Topology.All(Passed)&&b.Issues.Count==0,trimDiagnosticsCompatible=b.Domain.Count>0&&b.Domain.All(Passed)&&b.Issues.Count==0,
        trimDomainCertified=false,certificateAvailable=false};}
    public static object Direction(NBody input,NBody target,object candidate){
        var r=new Dictionary<string,object>{{"certifiedUpperBoundM",null},{"surfaceVerified",false},{"trimDomainCertified",false},{"topologyVerified",false}};
        try{
            object raw,returned;bool apiAccepted=AB.TryGet(candidate,"apiReturned",out returned)&&returned is bool&&(bool)returned;r["apiReturned"]=apiAccepted;
            var tr=AB.Get(candidate,"transform");AB.TryGet(tr,"rawArrayData",out raw);var t=new NTransform(AB.Get(tr,"homogeneousColumnVectorMatrix"));r["transformChecks"]=t.Check(Tolerance,raw);r["transformAccepted"]=apiAccepted&&Passed(r["transformChecks"]);
            var edgeCandidates=new List<object>();var faceCandidates=new List<object>();var em=new Dictionary<int,int>();var fm=new Dictionary<int,int>();var mappingIssues=new List<object>();
            foreach(var edge in input.Edges){var moved=edge.Transform(t);var matches=new List<int>();foreach(var targetEdge in target.Edges){var check=ConicResidual(moved,targetEdge);edgeCandidates.Add(check);if(Passed(check))matches.Add(targetEdge.Id);}if(matches.Count==1)em[edge.Id]=matches[0];else mappingIssues.Add(new{edge=edge.Id,reason="Conic match count="+matches.Count});}
            foreach(var face in input.Faces){var moved=face.Transform(t);var matches=new List<int>();foreach(var targetFace in target.Faces){var check=SurfaceResidual(moved,targetFace);faceCandidates.Add(check);if(Passed(check))matches.Add(targetFace.Id);}if(matches.Count==1)fm[face.Id]=matches[0];else mappingIssues.Add(new{face=face.Id,reason="Oriented support match count="+matches.Count});}
            var topology=new List<object>();
            foreach(var face in input.Faces){
                if(!fm.ContainsKey(face.Id)){topology.Add(new{face=face.Id,passed=false,reason="Face correspondence unresolved"});continue;}var targetFace=target.Faces.Single(x=>x.Id==fm[face.Id]);var moved=face.Transform(t);
                foreach(var loop in moved.Loops){if(!em.ContainsKey(loop.Edge.Id)){topology.Add(new{face=face.Id,loop=loop.Id,passed=false,reason="Edge correspondence unresolved"});continue;}var matches=targetFace.Loops.Where(l=>l.Edge.Id==em[loop.Edge.Id]&&l.Outer==loop.Outer).ToArray();
                    bool ok=matches.Length==1;if(ok){var pair=AB.Get(ConicResidual(loop.Edge,matches[0].Edge),"parameterOrientation");int sign=AB.Int(pair);int sa=loop.Sense==loop.Edge.EdgeSense?1:-1,sb=matches[0].Sense==matches[0].Edge.EdgeSense?1:-1;ok=sa*sign==sb;}
                    topology.Add(new{face=face.Id,loop=loop.Id,passed=ok,check="mappedLoopAndCoedgeOrientation"});}
            }
            foreach(var edge in input.Edges){bool ok=em.ContainsKey(edge.Id)&&edge.Adj.All(fm.ContainsKey);if(ok)ok=edge.Adj.Select(x=>fm[x]).OrderBy(x=>x).SequenceEqual(target.Edges.Single(x=>x.Id==em[edge.Id]).Adj.OrderBy(x=>x));topology.Add(new{edge=edge.Id,passed=ok,check="mappedAdjacency"});}
            bool edgesComplete=input.Edges.Count>0&&target.Edges.Count>0&&em.Count==input.Edges.Count&&em.Values.Distinct().Count()==target.Edges.Count;
            bool facesComplete=input.Faces.Count>0&&target.Faces.Count>0&&fm.Count==input.Faces.Count&&fm.Values.Distinct().Count()==target.Faces.Count;
            r["surfaceChecks"]=faceCandidates;r["contourChecks"]=edgeCandidates;r["topologyChecks"]=topology;r["mappingIssues"]=mappingIssues;
            r["edgeMap"]=em.Select(x=>new{input=x.Key,target=x.Value}).ToArray();r["faceMap"]=fm.Select(x=>new{input=x.Key,target=x.Value}).ToArray();
            r["surfaceDiagnosticsCompatible"]=facesComplete;r["contourDiagnosticsCompatible"]=edgesComplete;r["topologyDiagnosticsCompatible"]=edgesComplete&&facesComplete&&topology.All(Passed);
            r["status"]="DIAGNOSTICS_COMPLETED_NOT_CERTIFIED";
        }catch(Exception ex){r["status"]="INCOMPLETE";r["reason"]=ex.Message;}
        return r;
    }
    public static object Verify(Dictionary<string,object> report){
        var r=new Dictionary<string,object>{{"classification","NEEDS_REVIEW"},{"brepEquivalenceVerified",false},{"maximumDeviationM",null},{"certifiedUpperBoundM",null},{"mirrorStatus","NOT_IMPLEMENTED"},
            {"tolerances",new{geometryM=Tolerance.GeometryM,geometryMm=Tolerance.GeometryM*1000,computationalDimensionless=Tolerance.ComputationalDimensionless,lengthRoundoffGuard="128 * binary64 epsilon * max(1 metre, coordinate scale)",roundoffGuardIsCertified=false}}};
        r["unfinishedChecks"]=new[]{"Outward-rounded interval error bounds for complete surface/trim correspondence, including basis correction and COM parameter uncertainty", "Certified reconstruction and bidirectional equivalence of complete trimmed face domains/pcurves", "Certified upper bound for entire bounded BREP deviation; diagnostic floating envelopes are not a certificate"};
        try{
            Need(NV.Finite(Tolerance.GeometryM)&&Tolerance.GeometryM>0,"Invalid geometric tolerance");var a=Parse(AB.Get(report,"bodyA"));var b=Parse(AB.Get(report,"bodyB"));r["bodyAValidation"]=BodySummary(a);r["bodyBValidation"]=BodySummary(b);
            r["AtoB"]=Direction(a,b,AB.Get(report,"coincidenceAtoB"));r["BtoA"]=Direction(b,a,AB.Get(report,"coincidenceBtoA"));
            var inverseChecks=new List<object>();try{var ta=new NTransform(AB.Get(AB.Get(AB.Get(report,"coincidenceAtoB"),"transform"),"homogeneousColumnVectorMatrix"));var tb=new NTransform(AB.Get(AB.Get(AB.Get(report,"coincidenceBtoA"),"transform"),"homogeneousColumnVectorMatrix"));
                var cols=new[]{new NV(1,0,0),new NV(0,1,0),new NV(0,0,1)};double rot=cols.Max(v=>(tb.Vector(ta.Vector(v))-v).Norm());double pos=tb.Point(ta.Point(new NV(0,0,0))).Norm();inverseChecks.Add(new{check="roundTripCandidateConsistency",passed=rot<=Tolerance.ComputationalDimensionless&&pos<=Tolerance.GeometryM,linearResidual=rot,translationResidualM=pos});
            }catch(Exception ex){inverseChecks.Add(new{check="roundTripCandidateConsistency",passed=false,reason=ex.Message});}r["bidirectionalChecks"]=inverseChecks;
            object state;bool unchanged=AB.TryGet(report,"observedStateUnchanged",out state)&&state is bool&&(bool)state;r["observedStateUnchanged"]=unchanged;
            object extractionErrors,gaps;bool extractionKnown=AB.TryGet(report,"errors",out extractionErrors)&&extractionErrors!=null&&AB.TryGet(report,"dataGaps",out gaps)&&gaps!=null;
            bool extractionClean=false;
            if(extractionKnown)extractionClean=AB.Array(extractionErrors).Length==0&&AB.Array(AB.Get(report,"dataGaps")).Length==0;
            r["extractionClean"]=extractionClean;
            bool diagnostics=unchanged&&extractionClean&&inverseChecks.All(Passed);
            foreach(string body in new[]{"bodyAValidation","bodyBValidation"})foreach(string field in new[]{"numericalAccepted","topologyCompatible","trimDiagnosticsCompatible"})diagnostics=diagnostics&&AB.Bool(AB.Get(r[body],field));
            foreach(string direction in new[]{"AtoB","BtoA"})foreach(string field in new[]{"transformAccepted","surfaceDiagnosticsCompatible","contourDiagnosticsCompatible","topologyDiagnosticsCompatible"}){object value;diagnostics=diagnostics&&AB.TryGet(r[direction],field,out value)&&value is bool&&(bool)value;}
            r["allAvailableDiagnosticsCompatible"]=diagnostics;
            r["reason"]="No certified whole-BREP deviation bound backend is implemented; even all passing diagnostics cannot establish IDENTICAL_3D";
        }catch(Exception ex){r["reason"]=ex.Message;}
        return r;
    }
}
