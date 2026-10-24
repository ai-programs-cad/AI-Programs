using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Web.Script.Serialization;
using System.Reflection;

// Exact rational arithmetic on the supplied IEEE-754 double values.
// No epsilon, sampling, fitting, matrix snapping, or mesh equivalence.
public struct Rat : IEquatable<Rat> {
    public BigInteger N,D;
    public Rat(BigInteger n,BigInteger d) {
        if(d.IsZero)throw new ArgumentException("Zero denominator");
        if(d.Sign<0){n=-n;d=-d;}var g=BigInteger.GreatestCommonDivisor(BigInteger.Abs(n),d);N=n/g;D=d/g;
    }
    public static Rat Of(double x) {
        if(Double.IsNaN(x)||Double.IsInfinity(x))throw new ArgumentException("Nonfinite double");
        long bits=BitConverter.DoubleToInt64Bits(x);int exp=(int)((bits>>52)&2047);
        long mant=bits&0xfffffffffffffL;int shift=exp==0?-1074:exp-1075;
        BigInteger n=exp==0?mant:mant|(1L<<52);if(bits<0)n=-n;
        return shift>=0?new Rat(n<<shift,1):new Rat(n,BigInteger.One<<(-shift));
    }
    public static implicit operator Rat(int n){return new Rat(n,1);}
    public static Rat operator +(Rat a,Rat b){return new Rat(a.N*b.D+b.N*a.D,a.D*b.D);}
    public static Rat operator -(Rat a,Rat b){return new Rat(a.N*b.D-b.N*a.D,a.D*b.D);}
    public static Rat operator -(Rat a){return new Rat(-a.N,a.D);}
    public static Rat operator *(Rat a,Rat b){return new Rat(a.N*b.N,a.D*b.D);}
    public static Rat operator /(Rat a,Rat b){return new Rat(a.N*b.D,a.D*b.N);}
    public static bool operator ==(Rat a,Rat b){return a.N==b.N && a.D==b.D;}
    public static bool operator !=(Rat a,Rat b){return !(a==b);}
    public bool Equals(Rat x){return this==x;}
    public override bool Equals(object x){return x is Rat && Equals((Rat)x);}
    public override int GetHashCode(){return N.GetHashCode()^D.GetHashCode();}
    public int Sign{get{return N.Sign;}}
    public static Rat Abs(Rat x){return x.Sign<0?-x:x;}
}
public class AV {
    public Rat[] X;
    public AV(params Rat[] a){X=a;}
    public static AV Read(object o){var a=AB.Array(o).Select(AB.Num).Select(Rat.Of).ToArray();if(a.Length!=3)throw new Exception("Expected 3-vector");return new AV(a);}
    public static AV operator +(AV a,AV b){return new AV(Enumerable.Range(0,3).Select(i=>a.X[i]+b.X[i]).ToArray());}
    public static AV operator -(AV a,AV b){return new AV(Enumerable.Range(0,3).Select(i=>a.X[i]-b.X[i]).ToArray());}
    public static AV operator *(AV a,Rat k){return new AV(a.X.Select(x=>x*k).ToArray());}
    public static Rat Dot(AV a,AV b){Rat r=0;for(int i=0;i<3;i++)r+=a.X[i]*b.X[i];return r;}
    public static AV Cross(AV a,AV b){return new AV(a.X[1]*b.X[2]-a.X[2]*b.X[1],a.X[2]*b.X[0]-a.X[0]*b.X[2],a.X[0]*b.X[1]-a.X[1]*b.X[0]);}
    public static bool Equal(AV a,AV b){return a.X.SequenceEqual(b.X);}
    public static bool Zero(AV a){return a.X.All(x=>x.Sign==0);}
}
public class ATransform {
    public Rat[][] M;
    public ATransform(object o){M=AB.Array(o).Select(row=>AB.Array(row).Select(AB.Num).Select(Rat.Of).ToArray()).ToArray();if(M.Length!=4||M.Any(r=>r.Length!=4))throw new Exception("Expected 4x4 transform");}
    public AV Vector(AV v){return new AV(Enumerable.Range(0,3).Select(i=>M[i][0]*v.X[0]+M[i][1]*v.X[1]+M[i][2]*v.X[2]).ToArray());}
    public AV Point(AV v){return Vector(v)+new AV(M[0][3],M[1][3],M[2][3]);}
    public bool ProperExact() {
        if(M[3][0]!=0||M[3][1]!=0||M[3][2]!=0||M[3][3]!=1)return false;
        var a=Vector(new AV(1,0,0));var b=Vector(new AV(0,1,0));var c=Vector(new AV(0,0,1));
        return AV.Dot(a,a)==1&&AV.Dot(b,b)==1&&AV.Dot(c,c)==1&&AV.Dot(a,b)==0&&AV.Dot(a,c)==0&&AV.Dot(b,c)==0&&AV.Dot(a,AV.Cross(b,c))==1;
    }
}
public class AConic {
    public int Id; public AV C,A,B;public bool EdgeSense;public int[] Adj;
    public Dictionary<string,object> Source;
    public AV Normal{get{return AV.Cross(A,B);}}
    public Rat[,] Cov(){var q=new Rat[3,3];for(int i=0;i<3;i++)for(int j=0;j<3;j++)q[i,j]=A.X[i]*A.X[j]+B.X[i]*B.X[j];return q;}
    public AConic Transform(ATransform t){return new AConic{Id=Id,C=t.Point(C),A=t.Vector(A),B=t.Vector(B),EdgeSense=EdgeSense,Adj=Adj,Source=Source};}
    public bool Equal(AConic b){if(!AV.Equal(C,b.C))return false;var x=Cov();var y=b.Cov();for(int i=0;i<3;i++)for(int j=0;j<3;j++)if(x[i,j]!=y[i,j])return false;return true;}
}
public class ALoop { public bool Outer;public AConic Edge;public bool Sense; }
public class AFace {
    public int Id;public string Type;public AV O,N;public Rat Radius;public int MaterialSign;public List<ALoop> Loops=new List<ALoop>();
    public AFace Transform(ATransform t){return new AFace{Id=Id,Type=Type,O=t.Point(O),N=t.Vector(N),Radius=Radius,MaterialSign=MaterialSign,Loops=Loops.Select(l=>new ALoop{Outer=l.Outer,Sense=l.Sense,Edge=l.Edge.Transform(t)}).ToList()};}
    public bool SurfaceEqual(AFace f){
        if(Type!=f.Type||MaterialSign!=f.MaterialSign)return false;
        if(!AV.Zero(AV.Cross(N,f.N)))return false;
        if(Type=="PLANE")return AV.Dot(N,f.N).Sign>0&&AV.Dot(N,f.O-O)==0;
        return Radius==f.Radius&&AV.Zero(AV.Cross(N,f.O-O));
    }
}
public class ABody { public List<AFace> Faces=new List<AFace>();public List<AConic> Edges=new List<AConic>(); }
public static class AB {
    public static object[] Array(object o){if(o==null)throw new Exception("Required array missing");return ((IEnumerable)o).Cast<object>().ToArray();}
    // Preserve real dictionaries by reference; adapt anonymous/DTO properties safely.
    // Geometry and COM objects must be extracted before entering this data accessor.
    public static Dictionary<string,object> Obj(object o){
        if(o==null)throw new InvalidOperationException("Required object missing");
        var concrete=o as Dictionary<string,object>;
        if(concrete!=null)return concrete;
        var generic=o as IDictionary<string,object>;
        if(generic!=null)return new Dictionary<string,object>(generic,StringComparer.Ordinal);
        var map=o as IDictionary;
        if(map!=null){
            var converted=new Dictionary<string,object>(StringComparer.Ordinal);
            foreach(DictionaryEntry entry in map){
                var key=entry.Key as string;
                if(key==null)throw new InvalidOperationException("Object dictionary contains a non-string key");
                converted.Add(key,entry.Value);
            }
            return converted;
        }
        var properties=DataProperties(o);
        if(properties.Length==0)throw new InvalidOperationException("Expected a property object or dictionary; received "+o.GetType().FullName);
        var result=new Dictionary<string,object>(StringComparer.Ordinal);
        foreach(var property in properties)result.Add(property.Name,ReadProperty(o,property));
        return result;
    }
    static PropertyInfo[] DataProperties(object o){
        Type type=o.GetType();
        if(type.IsPrimitive||type.IsEnum||o is string||o is decimal||o is IEnumerable||System.Runtime.InteropServices.Marshal.IsComObject(o))
            return new PropertyInfo[0];
        return type.GetProperties(BindingFlags.Public|BindingFlags.Instance)
            .Where(p=>p.GetIndexParameters().Length==0&&p.GetGetMethod(false)!=null).ToArray();
    }
    static object ReadProperty(object source,PropertyInfo property){
        try{return property.GetValue(source,null);}
        catch(Exception ex){throw new InvalidOperationException("Could not read property: "+property.Name,ex is TargetInvocationException&&ex.InnerException!=null?ex.InnerException:ex);}
    }
    // true + null means a present null field; false means missing/null source.
    public static bool TryGet(object source,string key,out object value){
        value=null;
        if(String.IsNullOrEmpty(key))throw new ArgumentException("Property name must not be null or empty","key");
        if(source==null)return false;
        var generic=source as IDictionary<string,object>;
        if(generic!=null)return generic.TryGetValue(key,out value);
        var map=source as IDictionary;
        if(map!=null){if(!map.Contains(key))return false;value=map[key];return true;}
        var property=DataProperties(source).SingleOrDefault(p=>String.Equals(p.Name,key,StringComparison.Ordinal));
        if(property==null)return false;
        value=ReadProperty(source,property);return true;
    }
    public static object Get(object o,string key){
        object value;
        if(!TryGet(o,key,out value))throw new InvalidOperationException("Missing required field: "+key);
        if(value==null)throw new InvalidOperationException("Required field is null: "+key);
        return value;
    }
    public static void ApplyTrimDomainCertificates(IDictionary<string,object> report,IDictionary<string,object> verification){
        if(report==null)throw new ArgumentNullException("report");
        if(verification==null)throw new ArgumentNullException("verification");
        foreach(string item in new[]{"bodyA","bodyB"}){
            object body,validation,certificate;
            if(!TryGet(report,item,out body)||body==null||!TryGet(verification,item+"Validation",out validation))continue;
            var target=body as IDictionary<string,object>;
            if(target==null){target=Obj(body);report[item]=target;}
            target["trimDomainCertified"]=TryGet(validation,"certificateAvailable",out certificate)&&certificate is bool&&(bool)certificate;
        }
    }
    public static double Num(object x){return Convert.ToDouble(x,System.Globalization.CultureInfo.InvariantCulture);}
    public static int Int(object x){return Convert.ToInt32(x);}
    public static bool Bool(object x){return Convert.ToBoolean(x);}
    public static void Need(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static Rat Max(Rat a,Rat b){return (a-b).Sign>=0?a:b;}
    // Exact inequality sqrt(a)+sqrt(b)<sqrt(c), without square roots.
    static bool SumRootsLess(Rat a,Rat b,Rat c){var k=c-a-b;return a.Sign>=0&&b.Sign>=0&&k.Sign>0&&(k*k-4*a*b).Sign>0;}
    static Rat OperatorNormBoundSquared(Rat[,] m){
        Rat row=0,col=0;for(int i=0;i<2;i++){row=Max(row,Rat.Abs(m[i,0])+Rat.Abs(m[i,1]));col=Max(col,Rat.Abs(m[0,i])+Rat.Abs(m[1,i]));}return row*col;
    }
    static Rat[] Coordinates(AConic frame,AV v){return new[]{AV.Dot(frame.A,v)/AV.Dot(frame.A,frame.A),AV.Dot(frame.B,v)/AV.Dot(frame.B,frame.B)};}
    static Rat Norm2(Rat[] v){return v[0]*v[0]+v[1]*v[1];}
    static Rat ConicRadiusBound2(AConic frame,AConic c){var a=Coordinates(frame,c.A);var b=Coordinates(frame,c.B);return OperatorNormBoundSquared(new Rat[,]{{a[0],b[0]},{a[1],b[1]}});}
    internal static AConic Edge(object o){
        int id=Int(Get(o,"id"));string label="edge["+id+"]";
        Need(Bool(Get(o,"ownerVerified")),label+": owner not verified");
        var cv=Get(o,"curve");var domain=Get(cv,"underlyingDomain");var bounds=Get(o,"bounds");
        Need(Bool(Get(domain,"returned"))&&Bool(Get(domain,"closed"))&&Bool(Get(domain,"periodic")),label+": complete periodic closed curve required");
        double ds=Num(Get(domain,"start")),de=Num(Get(domain,"end"));double u0=Num(Get(bounds,"uMin")),u1=Num(Get(bounds,"uMax"));
        Need(de>ds && Rat.Abs(Rat.Of(u1)-Rat.Of(u0))==Rat.Of(de)-Rat.Of(ds),label+": edge interval does not cover exactly one underlying period");
        Need(AV.Equal(AV.Read(Get(bounds,"startPointM")),AV.Read(Get(bounds,"endPointM"))),label+": nonclosed bound endpoints");
        var c=new AConic{Id=id,EdgeSense=Bool(Get(bounds,"sense")),Adj=Array(Get(o,"adjacentFaceIds")).Select(Int).ToArray(),Source=Obj(o)};
        if(Bool(Get(cv,"isEllipse"))) {
            var p=Array(Get(cv,"ellipseParams")).Select(Num).ToArray();Need(p.Length==11,label+": ellipse parameter length");
            c.C=AV.Read(p.Take(3).ToArray());var ua=AV.Read(p.Skip(4).Take(3).ToArray());var ub=AV.Read(p.Skip(8).Take(3).ToArray());
            Need(AV.Dot(ua,ua)==1&&AV.Dot(ub,ub)==1&&AV.Dot(ua,ub)==0,label+": unit orthogonal ellipse axes not exact in readback doubles; cannot certify normalization");
            Need(p[3]>0&&p[7]>0,label+": nonpositive ellipse radius");c.A=ua*Rat.Of(p[3]);c.B=ub*Rat.Of(p[7]);
        } else if(Bool(Get(cv,"isCircle"))) {
            var p=Array(Get(cv,"circleParams")).Select(Num).ToArray();Need(p.Length==7,label+": circle parameter length");
            c.C=AV.Read(p.Take(3).ToArray());var normal=AV.Read(p.Skip(3).Take(3).ToArray());Rat radius=Rat.Of(p[6]);
            Need(radius.Sign>0&&AV.Dot(normal,normal)==1,label+": circle normal/radius not certified");
            // Exact rational orthonormal chart via Householder; no sqrt or fitting.
            var z=new AV(0,0,1);var v=z-normal;
            Func<AV,AV> h=x=>AV.Zero(v)?x:x-v*(2*AV.Dot(v,x)/AV.Dot(v,v));
            c.A=h(new AV(1,0,0))*radius;c.B=h(new AV(0,1,0))*radius;
            if(AV.Dot(c.Normal,normal).Sign<0)c.B=c.B*(-1);
        } else throw new Exception(label+": only full circles/ellipses supported");
        Need(AV.Dot(c.A,c.A).Sign>0&&AV.Dot(c.B,c.B).Sign>0&&AV.Dot(c.A,c.B)==0,label+": degenerate conic");
        Need(c.Adj.Length==2&&c.Adj[0]!=c.Adj[1]&&c.Adj.All(x=>x>=0),label+": expected two distinct adjacent faces");
        return c;
    }
    public static ABody Parse(object source){
        Need(Int(Get(source,"vertexCount"))==0&&Array(Get(source,"verticesM")).Length==0,"Only vertexless full-conic topology is supported in this release");
        var body=new ABody();body.Edges=Array(Get(source,"edges")).Select(Edge).ToList();
        Need(body.Edges.Select(e=>e.Id).Distinct().Count()==body.Edges.Count,"Duplicate edge IDs");
        foreach(var fo in Array(Get(source,"faces"))) {
            int id=Int(Get(fo,"id"));string label="face["+id+"]";Need(Bool(Get(fo,"ownerVerified")),label+": owner not verified");
            var s=Get(fo,"surface");var f=new AFace{Id=id,Type=(string)Get(s,"type"),MaterialSign=Bool(Get(fo,"faceInSurfaceSense"))?-1:1};
            if(f.Type=="PLANE") {f.O=AV.Read(Get(s,"rootPointM"));f.N=AV.Read(Get(s,"normal"))*f.MaterialSign;f.MaterialSign=1;Need(!AV.Zero(f.N),label+": zero plane normal");}
            else if(f.Type=="CYLINDER") {f.O=AV.Read(Get(s,"originM"));f.N=AV.Read(Get(s,"axis"));f.Radius=Rat.Of(Num(Get(s,"radiusM")));Need(f.Radius.Sign>0&&AV.Dot(f.N,f.N)==1,label+": cylinder radius/axis not exact unit");
                // Natural cylinder parameterization may have reversed orientation.
                int radial=Int(Get(fo,"naturalCylinderRadialSign"));Need(radial==1||radial==-1,label+": natural cylinder material normal not confirmed");
                var witness=AV.Read(Get(fo,"naturalNormalWitnessM"));var eval=Array(Get(fo,"naturalNormalEvaluationRaw"));Need(eval.Length>=3,label+": natural normal evaluation missing");
                var natural=AV.Read(eval.Take(3).ToArray());var delta=witness-f.O;var projected=delta-f.N*AV.Dot(delta,f.N);
                Need(AV.Dot(natural,projected).Sign==radial,label+": natural radial normal sign not verified by exact sign calculation");f.MaterialSign*=radial;}
            else throw new Exception(label+": unsupported surface "+f.Type);
            foreach(var lo in Array(Get(fo,"loops"))) {
                Need(!Bool(Get(lo,"isSingular")),label+": singular loop unsupported");var co=Array(Get(lo,"coedges"));Need(co.Length==1&&Int(Get(lo,"coedgeCount"))==1,label+": only one full conic per loop supported");
                Need(Int(Get(co[0],"nextId"))==Int(Get(co[0],"id")),label+": coedge does not close on itself");
                int eid=Int(Get(co[0],"edgeId"));var edge=body.Edges.SingleOrDefault(e=>e.Id==eid);Need(edge!=null,label+": unresolved edge "+eid);
                var raw=Array(Get(co[0],"curveBoundsRaw")).Select(Num).ToArray();Need(raw.Length==10,label+": coedge bounds missing");
                var eb=Get(edge.Source,"bounds");Need(Rat.Abs(Rat.Of(raw[7])-Rat.Of(raw[6]))==Rat.Abs(Rat.Of(Num(Get(eb,"uMax")))-Rat.Of(Num(Get(eb,"uMin")))),label+": coedge not complete edge period");
                Need(AV.Equal(AV.Read(raw.Take(3).ToArray()),AV.Read(raw.Skip(3).Take(3).ToArray())),label+": open coedge bounds");
                bool coSense=Bool(Get(co[0],"sense"));Need((raw[7]>raw[6])==(coSense==edge.EdgeSense),label+": coedge parameter direction disagrees with edge/curve senses");
                f.Loops.Add(new ALoop{Outer=Bool(Get(lo,"isOuter")),Edge=edge,Sense=coSense});
            }
            Need(f.Loops.Count>0,label+": no trimming loops");body.Faces.Add(f);
        }
        Need(Int(Get(source,"faceCount"))==body.Faces.Count&&Int(Get(source,"edgeCount"))==body.Edges.Count,"Enumeration/count discrepancy");
        Need(body.Faces.Select(f=>f.Id).Distinct().Count()==body.Faces.Count,"Duplicate face IDs");
        foreach(var e in body.Edges){var uses=body.Faces.Where(f=>f.Loops.Any(l=>l.Edge.Id==e.Id)).Select(f=>f.Id).OrderBy(x=>x).ToArray();Need(uses.SequenceEqual(e.Adj.OrderBy(x=>x)),"edge["+e.Id+"]: adjacency disagrees with loop uses");var co=body.Faces.SelectMany(f=>f.Loops).Where(l=>l.Edge.Id==e.Id).ToArray();Need(co.Length==2,"edge["+e.Id+"]: non-manifold incidence");Need(co[0].Sense!=co[1].Sense,"edge["+e.Id+"]: shared boundary directions are not opposite");}
        foreach(var f in body.Faces)CertifyDomain(f);
        return body;
    }
    static void CertifyDomain(AFace f){
        string label="face["+f.Id+"]";
        if(f.Type=="PLANE") {
            var outer=f.Loops.Where(l=>l.Outer).ToArray();Need(outer.Length==1,label+": plane needs one bounded outer loop");var frame=outer[0].Edge;
            foreach(var l in f.Loops){var c=l.Edge;Need(AV.Dot(f.N,c.C-f.O)==0&&AV.Dot(f.N,c.A)==0&&AV.Dot(f.N,c.B)==0,label+": conic is not exactly on supporting plane");
                int sense=l.Sense==c.EdgeSense?1:-1;Need(AV.Dot(f.N,c.Normal).Sign*sense==(l.Outer?1:-1),label+": loop winding/material side inconsistent");}
            var holes=f.Loops.Where(l=>!l.Outer).Select(l=>l.Edge).ToArray();
            foreach(var h in holes){var offset=Coordinates(frame,h.C-frame.C);Need(SumRootsLess(Norm2(offset),ConicRadiusBound2(frame,h),1),label+": inner conic strict containment cannot be certified");}
            for(int i=0;i<holes.Length;i++)for(int j=i+1;j<holes.Length;j++)Need(SumRootsLess(ConicRadiusBound2(frame,holes[i]),ConicRadiusBound2(frame,holes[j]),Norm2(Coordinates(frame,holes[i].C-holes[j].C))),label+": inner loops disjointness cannot be certified");
        } else {
            Need(f.Loops.Count==2&&f.Loops.All(l=>l.Outer),label+": only full cylinder band with two outer loops supported");
            foreach(var l in f.Loops){var c=l.Edge;AV d=c.C-f.O;AV p=d-f.N*AV.Dot(d,f.N);AV a=c.A-f.N*AV.Dot(c.A,f.N);AV b=c.B-f.N*AV.Dot(c.B,f.N);
                Need(AV.Zero(p)&&AV.Dot(a,a)==f.Radius*f.Radius&&AV.Dot(b,b)==f.Radius*f.Radius&&AV.Dot(a,b)==0,label+": conic-cylinder polynomial identity failed");
                Need(AV.Dot(f.N,c.Normal).Sign!=0,label+": conic does not wind once around cylinder");}
            var x=f.Loops[0].Edge;var y=f.Loops[1].Edge;
            Rat center=AV.Dot(f.N,y.C-x.C);Rat ax=AV.Dot(f.N,x.A),bx=AV.Dot(f.N,x.B),ay=AV.Dot(f.N,y.A),by=AV.Dot(f.N,y.B);
            Need(SumRootsLess(ax*ax+bx*bx,ay*ay+by*by,center*center),label+": boundary ordering/intersection cannot be certified by analytic extrema");
            int sx=(f.Loops[0].Sense==x.EdgeSense?1:-1)*AV.Dot(f.N,x.Normal).Sign;
            int sy=(f.Loops[1].Sense==y.EdgeSense?1:-1)*AV.Dot(f.N,y.Normal).Sign;
            Need(sx==-sy,label+": cylinder boundary windings not opposite");
            Need(sx==center.Sign*f.MaterialSign,label+": cylinder band material side/winding inconsistent");
        }
    }
    static bool LoopMatch(AFace a,AFace b,Dictionary<int,int> em){
        if(a.Loops.Count!=b.Loops.Count)return false;
        foreach(var l in a.Loops){var candidates=b.Loops.Where(x=>x.Edge.Id==em[l.Edge.Id]&&x.Outer==l.Outer).ToArray();if(candidates.Length!=1)return false;
            var k=candidates[0];int orient=AV.Dot(l.Edge.Normal,k.Edge.Normal).Sign;
            if(orient==0)return false;int sa=l.Sense==l.Edge.EdgeSense?1:-1;int sb=k.Sense==k.Edge.EdgeSense?1:-1;if(sa*orient!=sb)return false;}
        return true;
    }
    static bool FindMap(ABody input,ABody target,out Dictionary<int,int> edgeMap,out Dictionary<int,int> faceMap,out List<object> mismatches){
        edgeMap=new Dictionary<int,int>();faceMap=new Dictionary<int,int>();mismatches=new List<object>();
        // Unique analytic edge matching only; ambiguous symmetric graphs require review.
        foreach(var e in input.Edges){var matches=target.Edges.Where(x=>e.Equal(x)).ToArray();if(matches.Length!=1){mismatches.Add(new {edge=e.Id,reason=matches.Length==0?"No exact full-conic center/covariance match":"Ambiguous conic mapping"});continue;}edgeMap[e.Id]=matches[0].Id;}
        if(edgeMap.Count!=input.Edges.Count||edgeMap.Values.Distinct().Count()!=target.Edges.Count)return false;
        var em=edgeMap;
        foreach(var f in input.Faces){var matches=target.Faces.Where(x=>f.SurfaceEqual(x)&&LoopMatch(f,x,em)).ToArray();if(matches.Length!=1){mismatches.Add(new {face=f.Id,reason="No unique oriented support + bounded loops/coedges match"});continue;}faceMap[f.Id]=matches[0].Id;}
        if(faceMap.Count!=input.Faces.Count||faceMap.Values.Distinct().Count()!=target.Faces.Count)return false;
        var fm=faceMap;
        foreach(var e in input.Edges)if(!e.Adj.Select(x=>fm[x]).OrderBy(x=>x).SequenceEqual(target.Edges.Single(x=>x.Id==em[e.Id]).Adj.OrderBy(x=>x))){mismatches.Add(new {edge=e.Id,reason="Mapped face adjacency differs"});return false;}
        return true;
    }
    public static object Direction(object input,object target,object matrix){
        var r=new Dictionary<string,object>{{"verified",false},{"surfaceVerified",false},{"trimDomainCertified",false},{"topologyVerified",false},{"maximumDeviationM",null}};
        try {
            var t=new ATransform(matrix);r["candidateProperIsometryExact"]=t.ProperExact();
            var a=Parse(input);var b=Parse(target);r["inputDomainCertified"]=true;r["targetDomainCertified"]=true;
            Need(t.ProperExact(),"API candidate is not an EXACT proper isometry in rational arithmetic; no rounding/snapping allowed");
            var ta=new ABody{Edges=a.Edges.Select(e=>e.Transform(t)).ToList(),Faces=a.Faces.Select(f=>f.Transform(t)).ToList()};
            Dictionary<int,int> em,fm;List<object> mismatch;
            bool ok=FindMap(ta,b,out em,out fm,out mismatch);r["edgeMap"]=em.Select(x=>new {input=x.Key,target=x.Value}).ToArray();r["faceMap"]=fm.Select(x=>new {input=x.Key,target=x.Value}).ToArray();r["mismatches"]=mismatch;
            r["surfaceVerified"]=ok;r["trimDomainCertified"]=ok;r["topologyVerified"]=ok;r["verified"]=ok;
            r["candidateStatus"]=ok?"EXACT_ANALYTIC_BREP_MATCH":"CANDIDATE_NOT_CERTIFIED";
            r["reason"]=ok?"Exact oriented analytic surfaces, full-conic loops, finite domains, coedge directions and adjacency correspond bijectively":"Mismatch for this supplied candidate does not exclude other rigid transforms";
            if(ok)r["maximumDeviationM"]=0.0;
        }catch(Exception ex){r["candidateStatus"]="NEEDS_REVIEW";r["reason"]=ex.Message;}
        return r;
    }
    public static object Verify(Dictionary<string,object> report){
        var r=new Dictionary<string,object>{{"classification","NEEDS_REVIEW"},{"brepEquivalenceVerified",false},{"maximumDeviationM",null},{"mirrorStatus","NOT_IMPLEMENTED"},
            {"proofMode","Exact rational identities of returned analytic double parameters; zero tolerance; no approximate equivalence claims"}};
        try {
            var ab=Get(report,"coincidenceAtoB");var ba=Get(report,"coincidenceBtoA");
            Need(Bool(Get(ab,"apiReturned"))&&Bool(Get(ba,"apiReturned")),"Both API candidate transforms required");
            var a=Get(report,"bodyA");var b=Get(report,"bodyB");
            r["bodyAValidation"]=Diagnostics(a);r["bodyBValidation"]=Diagnostics(b);
            var forward=Obj(Direction(a,b,Get(Get(ab,"transform"),"homogeneousColumnVectorMatrix")));
            var reverse=Obj(Direction(b,a,Get(Get(ba,"transform"),"homogeneousColumnVectorMatrix")));
            r["AtoB"]=forward;r["BtoA"]=reverse;
            bool clean=Array(Get(report,"errors")).Length==0&&Array(Get(report,"dataGaps")).Length==0&&Bool(Get(report,"observedStateUnchanged"));
            if(Bool(forward["verified"])&&Bool(reverse["verified"])&&clean){r["classification"]="IDENTICAL_3D";r["brepEquivalenceVerified"]=true;r["maximumDeviationM"]=0.0;}
            else r["reason"]="Both complete exact direction certificates, clean extraction and unchanged model state are required; see directional reasons";
        }catch(Exception ex){r["reason"]=ex.Message;}
        return r;
    }
    static object Diagnostics(object body){
        var failures=new List<object>();
        try {foreach(var e in Array(Get(body,"edges"))){try{Edge(e);}catch(Exception ex){failures.Add(new{edge=Int(Get(e,"id")),reason=ex.Message});}}}catch(Exception ex){failures.Add(new{reason=ex.Message});}
        try {Parse(body);}catch(Exception ex){failures.Add(new{domainReason=ex.Message});}
        return new {certificateAvailable=failures.Count==0,failures};
    }
}
