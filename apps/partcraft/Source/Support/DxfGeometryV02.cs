using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using System.Globalization;

// Exact rational arithmetic over decimal ASCII DXF values. No sampled-curve maxima.
public struct GQ:IComparable<GQ> {
    readonly BigInteger n,d;BigInteger D{get{return d.IsZero?BigInteger.One:d;}}
    public GQ(BigInteger a,BigInteger b){if(b.IsZero)throw new DivideByZeroException();if(b.Sign<0){a=-a;b=-b;}var g=BigInteger.GreatestCommonDivisor(BigInteger.Abs(a),b);n=a/g;d=b/g;}
    public static implicit operator GQ(int x){return new GQ(x,1);}
    public static GQ Parse(string text){text=text.Trim();int exponent=0;int at=text.IndexOfAny(new[]{'e','E'});if(at>=0){exponent=int.Parse(text.Substring(at+1),CultureInfo.InvariantCulture);text=text.Substring(0,at);}int dot=text.IndexOf('.');int digits=dot<0?0:text.Length-dot-1;text=text.Replace(".","");if(Math.Abs(exponent)>100||digits>100)throw new InvalidOperationException("Unsupported DXF numeric exponent");var a=BigInteger.Parse(text,CultureInfo.InvariantCulture);int power=digits-exponent;return power>=0?new GQ(a,BigInteger.Pow(10,power)):new GQ(a*BigInteger.Pow(10,-power),1);}
    public static GQ operator +(GQ a,GQ b){return new GQ(a.n*b.D+b.n*a.D,a.D*b.D);}public static GQ operator -(GQ a,GQ b){return new GQ(a.n*b.D-b.n*a.D,a.D*b.D);}public static GQ operator -(GQ a){return new GQ(-a.n,a.D);}public static GQ operator *(GQ a,GQ b){return new GQ(a.n*b.n,a.D*b.D);}public static GQ operator /(GQ a,GQ b){return new GQ(a.n*b.D,a.D*b.n);}
    public int CompareTo(GQ b){return (n*b.D).CompareTo(b.n*D);}public static bool operator <(GQ a,GQ b){return a.CompareTo(b)<0;}public static bool operator >(GQ a,GQ b){return a.CompareTo(b)>0;}public static bool operator <=(GQ a,GQ b){return a.CompareTo(b)<=0;}public static bool operator >=(GQ a,GQ b){return a.CompareTo(b)>=0;}
    public bool Eq(GQ b){return CompareTo(b)==0;}public static GQ Abs(GQ a){return a<0?-a:a;}public static GQ Max(GQ a,GQ b){return a>b?a:b;}public double Number{get{return (double)n/(double)D;}}public override string ToString(){return n.ToString(CultureInfo.InvariantCulture)+"/"+D.ToString(CultureInfo.InvariantCulture);}
    public GQ Mod360(){var v=n%(D*360);if(v.Sign<0)v+=D*360;return new GQ(v,D);}
}
public struct GV {public GQ x,y;public GV(GQ a,GQ b){x=a;y=b;}public static GV operator +(GV a,GV b){return new GV(a.x+b.x,a.y+b.y);}public static GV operator -(GV a,GV b){return new GV(a.x-b.x,a.y-b.y);}public GQ L1{get{return GQ.Abs(x)+GQ.Abs(y);}}public double Length{get{return Math.Sqrt(x.Number*x.Number+y.Number*y.Number);}}public double[] Values{get{return new[]{x.Number,y.Number};}}}
public class GT {public int a,b,c,d;public GV t;public int Det{get{return a*d-b*c;}}public GV Apply(GV v){return new GV(a*v.x+b*v.y,c*v.x+d*v.y)+t;}public int Degrees{get{return a==1?0:c==1?90:a==-1?180:270;}}public object Evidence(){return new{matrix=new[]{new[]{(double)a,(double)b,t.x.Number},new[]{(double)c,(double)d,t.y.Number},new[]{0d,0,1}},translationExact=new[]{t.x.ToString(),t.y.ToString()},determinant=Det,scale=1,orthogonalExact=true};}}
public class GE {
    public int index;public string type,rawType,layer,handle,entityColor,entityLinetype;public GV a,b,center;public GQ radius,start,span;public int degree;public GV[] control;public GQ[] knots,weights;
    public GV[] Endpoints(){if(type=="LINE")return new[]{a,b};if(type=="SPLINE")return new[]{control.First(),control.Last()};if(type=="CIRCLE")return new GV[0];double s=start.Number*Math.PI/180,e=(start+span).Number*Math.PI/180;return new[]{new GV(GQ.Parse((center.x.Number+radius.Number*Math.Cos(s)).ToString("R",CultureInfo.InvariantCulture)),GQ.Parse((center.y.Number+radius.Number*Math.Sin(s)).ToString("R",CultureInfo.InvariantCulture))),new GV(GQ.Parse((center.x.Number+radius.Number*Math.Cos(e)).ToString("R",CultureInfo.InvariantCulture)),GQ.Parse((center.y.Number+radius.Number*Math.Sin(e)).ToString("R",CultureInfo.InvariantCulture)))};}
}
public class GD {public string path;public int units=-1;public object layoutEvidence;public List<object> nativeStyleTables=new List<object>();public List<GE> entities=new List<GE>();}
public class GMatch {public bool matched;public string reason;public GT transform;public int[] map;public GQ[] bounds;public GQ maximum;public int candidates;}
public static class DxfGeometryV02 {
    public static readonly GQ Tolerance=GQ.Parse("0.001");static readonly GQ PiUpper=GQ.Parse("3.141592653589794");
    public static GD Read(string path){
        var document=DxfLayoutReaderV03.Read(path);var result=new GD{path=path,units=document.units,layoutEvidence=document.evidence};
        foreach(var row in document.records.Where(r=>r.section=="TABLES"&&(r.Type=="LAYER"||r.Type=="LTYPE")))result.nativeStyleTables.Add(new{type=row.Type,groups=row.pairs.Skip(1).Select(p=>new{code=p.Key,value=p.Value}).ToArray()});
        var records=document.geometry;
        if(result.units!=4)throw new InvalidOperationException("Millimetre INSUNITS=4 required; value="+result.units);
        foreach(var row in records){
            Func<int,string,string> text=(k,fallback)=>{var values=row.Where(p=>p.Key==k).Select(p=>p.Value).ToArray();if(values.Length>1)throw new InvalidOperationException("Duplicate scalar DXF group: "+k);return values.Length==0?fallback:values[0];};
            Func<int,GQ> number=k=>GQ.Parse(text(k,"0"));Func<int,GV> point=k=>new GV(number(k),number(k+10));
            if(!number(210).Eq(0)||!number(220).Eq(0)||!GQ.Parse(text(230,"1")).Eq(1))throw new InvalidOperationException("Nonstandard extrusion unsupported; layout ownership is checked separately");
            foreach(var p in row.Where(p=>p.Key>=30&&p.Key<=39))if(!GQ.Parse(p.Value).Eq(0))throw new InvalidOperationException("Nonplanar entity");
            var e=new GE{index=result.entities.Count,type=row[0].Value,rawType=row[0].Value,layer=text(8,"0"),handle=text(5,null),entityColor=text(62,"256"),entityLinetype=text(6,"BYLAYER")};
            var required=e.type=="LINE"?new[]{10,20,11,21}:e.type=="ARC"?new[]{10,20,40,50,51}:e.type=="CIRCLE"?new[]{10,20,40}:new int[0];foreach(int k in required)if(row.Count(p=>p.Key==k)!=1)throw new InvalidOperationException("Required DXF geometric group absent/duplicated: "+k);
            if(e.type=="LINE"){e.a=point(10);e.b=point(11);if((e.a-e.b).L1.Eq(0))throw new InvalidOperationException("Zero-length LINE");}
            else if(e.type=="ARC"||e.type=="CIRCLE"){e.center=point(10);e.radius=number(40);if(e.radius<=0)throw new InvalidOperationException("Nonpositive radius");e.start=number(50).Mod360();e.span=(number(51)-e.start).Mod360();if(e.type=="ARC"&&e.span.Eq(0))throw new InvalidOperationException("Zero/full ambiguous ARC span");}
            else if(e.type=="SPLINE"){
                e.degree=int.Parse(text(71,"-1"));e.knots=row.Where(p=>p.Key==40).Select(p=>GQ.Parse(p.Value)).ToArray();var x=row.Where(p=>p.Key==10).Select(p=>GQ.Parse(p.Value)).ToArray();var y=row.Where(p=>p.Key==20).Select(p=>GQ.Parse(p.Value)).ToArray();e.control=x.Select((v,i)=>new GV(v,y[i])).ToArray();
                if((int.Parse(text(70,"0"))&2)!=0||e.degree<1||e.degree>12||x.Length!=y.Length||x.Length!=int.Parse(text(73,"-1"))||e.knots.Length!=int.Parse(text(72,"-1"))||e.knots.Length!=x.Length+e.degree+1)throw new InvalidOperationException("Unsupported/incomplete periodic or knot SPLINE");
                if(e.knots.Where((v,i)=>i>0&&v<e.knots[i-1]).Any()||e.knots.First().Eq(e.knots.Last())||!e.knots.Take(e.degree+1).All(k=>k.Eq(e.knots.First()))||!e.knots.Skip(e.knots.Length-e.degree-1).All(k=>k.Eq(e.knots.Last())))throw new InvalidOperationException("Unclamped or invalid SPLINE domain");
                if(e.knots.Where(k=>k>e.knots.First()&&k<e.knots.Last()).GroupBy(k=>k.ToString()).Any(g=>g.Count()>e.degree))throw new InvalidOperationException("Discontinuous internal SPLINE knot unsupported");
                var rawWeights=row.Where(p=>p.Key==41).Select(p=>GQ.Parse(p.Value)).ToArray();e.weights=rawWeights.Length==0?x.Select(_=>(GQ)1).ToArray():rawWeights;if(e.weights.Length!=x.Length||e.weights.Any(w=>w<=0))throw new InvalidOperationException("Unsupported SPLINE weights");
                GV delta=e.control.Last()-e.control.First();GQ len2=delta.x*delta.x+delta.y*delta.y;
                if(len2>0&&e.control.All(p=>{GV v=p-e.control[0];GQ projection=v.x*delta.x+v.y*delta.y;return (v.x*delta.y-v.y*delta.x).Eq(0)&&projection>=0&&projection<=len2;})){e.type="LINE";e.a=e.control.First();e.b=e.control.Last();}
            }else throw new InvalidOperationException("Unsupported curve representation: "+e.type);
            result.entities.Add(e);
        }return result;
    }
    static bool Sequence(GQ[] a,GQ[] b){return a.Length==b.Length&&a.Select((v,i)=>v.Eq(b[i])).All(v=>v);}
    public static GQ? Bound(GE source,GE target,GT t){
        if(source.type!=target.type)return null;
        if(source.type=="LINE"){GQ direct=GQ.Max((t.Apply(source.a)-target.a).L1,(t.Apply(source.b)-target.b).L1),reverse=GQ.Max((t.Apply(source.a)-target.b).L1,(t.Apply(source.b)-target.a).L1);return direct<reverse?direct:reverse;}
        if(source.type=="CIRCLE")return (t.Apply(source.center)-target.center).L1+GQ.Abs(source.radius-target.radius);
        if(source.type=="ARC"){
            // Exact signed axis rotations; whole arc parameter error uses |sin'|,|cos'| <= 1.
            GQ begin=(source.start+t.Degrees).Mod360();GQ angular=(begin-target.start).Mod360();if(angular>180)angular-=360;
            return (t.Apply(source.center)-target.center).L1+GQ.Abs(source.radius-target.radius)+GQ.Max(source.radius,target.radius)*(GQ.Abs(angular)+GQ.Abs(source.span-target.span))*PiUpper/180;
        }
        if(source.degree!=target.degree||source.control.Length!=target.control.Length)return null;GQ? best=null;
        GQ low=source.knots.First(),range=source.knots.Last()-low;var u=source.knots.Select(k=>(k-low)/range).ToArray();GQ tlow=target.knots.First(),trange=target.knots.Last()-tlow;var v=target.knots.Select(k=>(k-tlow)/trange).ToArray();
        for(int reverse=0;reverse<2;reverse++){
            var control=reverse==0?target.control:target.control.Reverse().ToArray();var knots=reverse==0?v:v.Reverse().Select(k=>1-k).ToArray();var weights=reverse==0?target.weights:target.weights.Reverse().ToArray();
            if(!Sequence(u,knots)||!Sequence(source.weights.Select(w=>w/source.weights[0]).ToArray(),weights.Select(w=>w/weights[0]).ToArray()))continue;
            GQ maximum=0;for(int i=0;i<control.Length;i++)maximum=GQ.Max(maximum,(t.Apply(source.control[i])-control[i]).L1);if(!best.HasValue||maximum<best.Value)best=maximum;
        }return best;
    }
    public static GMatch Match(List<GE> reference,List<GE> target){
        var result=new GMatch{reason="NO_MATCH_FOUND_IN_SUPPORTED_SEARCH; this is not a DIFFERENT classification"};if(reference.Count!=target.Count){result.reason="Entity count differs; unmatched/split representations require review";return result;}
        var anchor=reference.Where(e=>e.type=="LINE").OrderByDescending(e=>(e.b-e.a).Length).FirstOrDefault();if(anchor==null){result.reason="No LINE anchor; transformation search incomplete";return result;}
        foreach(var rotation in new[]{new[]{1,0,0,1},new[]{-1,0,0,-1},new[]{0,-1,1,0},new[]{0,1,-1,0}})
        foreach(var line in target.Where(e=>e.type=="LINE"&&Math.Abs((e.b-e.a).Length-(anchor.b-anchor.a).Length)<=.002))for(int reversed=0;reversed<2;reversed++){
            var t=new GT{a=rotation[0],b=rotation[1],c=rotation[2],d=rotation[3]};t.t=(reversed==0?line.a:line.b)-t.Apply(anchor.a);result.candidates++;
            var graph=new List<int>[reference.Count];var bounds=new GQ?[reference.Count,target.Count];bool possible=true;
            for(int i=0;i<reference.Count;i++){graph[i]=new List<int>();for(int j=0;j<target.Count;j++){var bound=Bound(reference[i],target[j],t);bounds[i,j]=bound;if(bound.HasValue&&bound.Value<=Tolerance)graph[i].Add(j);}if(graph[i].Count==0){possible=false;break;}}
            if(!possible)continue;int[] owner=Enumerable.Repeat(-1,target.Count).ToArray();Func<int,bool[],bool> augment=null;augment=(i,seen)=>{foreach(int j in graph[i].OrderBy(j=>bounds[i,j].Value)){if(seen[j])continue;seen[j]=true;if(owner[j]<0||augment(owner[j],seen)){owner[j]=i;return true;}}return false;};
            if(!Enumerable.Range(0,reference.Count).OrderBy(i=>graph[i].Count).All(i=>augment(i,new bool[target.Count])))continue;
            var map=new int[reference.Count];for(int j=0;j<owner.Length;j++)map[owner[j]]=j;var errors=map.Select((j,i)=>bounds[i,j].Value).ToArray();GQ maximum=errors.Aggregate((GQ)0,(m,b)=>GQ.Max(m,b));
            if(!result.matched||maximum<result.maximum){result.matched=true;result.transform=t;result.map=map;result.bounds=errors;result.maximum=maximum;result.reason="Complete bijection of bounded native curves; exact rational conservative whole-curve bounds, unit-scale proper transform";if(maximum.Eq(0))return result;}
        }return result;
    }
    public static object Topology(List<GE> entities,out bool closed,out int components,out List<int[]> edgeComponents){
        var points=new List<GV>();var pairs=new List<int[]>();var degree=new List<int>();int circles=0;
        foreach(var e in entities){var ends=e.Endpoints();if(ends.Length==0){circles++;pairs.Add(new int[0]);continue;}int[] ids=new int[2];for(int i=0;i<2;i++){var candidates=points.Select((p,j)=>new{p,j}).Where(p=>(p.p-ends[i]).L1<=GQ.Parse("0.000001")).ToArray();if(candidates.Length>1)throw new InvalidOperationException("Ambiguous endpoint connectivity");ids[i]=candidates.Length==1?candidates[0].j:points.Count;if(ids[i]==points.Count){points.Add(ends[i]);degree.Add(0);}degree[ids[i]]++;}pairs.Add(ids);}
        closed=degree.All(d=>d==2);edgeComponents=new List<int[]>();var used=new HashSet<int>();for(int i=0;i<pairs.Count;i++){if(used.Contains(i))continue;var nodes=new HashSet<int>(pairs[i]);var members=new List<int>{i};used.Add(i);bool changed=true;while(changed){changed=false;for(int j=0;j<pairs.Count;j++)if(!used.Contains(j)&&pairs[j].Any(nodes.Contains)){used.Add(j);members.Add(j);foreach(int n in pairs[j])nodes.Add(n);changed=true;}}edgeComponents.Add(members.ToArray());}components=edgeComponents.Count;
        return new{closed,components,edgeCounts=edgeComponents.Select(c=>c.Length).OrderBy(n=>n).ToArray(),endpointJoinToleranceMm=.000001,degreeTwo=degree.All(d=>d==2),note="Endpoint connectivity only; no sampled curve maximum or self-intersection certificate"};
    }
}
