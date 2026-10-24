using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
public static class TransformSearchV04Tests {
    static JavaScriptSerializer J=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=300};
    static List<object> results=new List<object>();
    static void Test(string name,Func<bool> f){try{bool ok=f();results.Add(new{name,passed=ok});}catch(Exception e){results.Add(new{name,passed=false,error=e.ToString()});}}
    static Dictionary<string,object> Report(object a,object b){return new Dictionary<string,object>{{"bodyA",a},{"bodyB",b}};}
    static bool Flag(object r,string n){object x;return AB.TryGet(r,n,out x)&&x is bool&&(bool)x;}
    public static NTransform Rotation(double angle){
        var n=new NV(1,2,3).Unit();var v=n.Data();double c=Math.Cos(angle),s=Math.Sin(angle);
        var m=new[]{new double[4],new double[4],new double[4],new[]{0d,0,0,1}};
        for(int i=0;i<3;i++)for(int j=0;j<3;j++)m[i][j]=(i==j?c:0)+(1-c)*v[i]*v[j];
        m[0][1]-=s*v[2];m[0][2]+=s*v[1];m[1][0]+=s*v[2];m[1][2]-=s*v[0];m[2][0]-=s*v[1];m[2][1]+=s*v[0];
        m[0][3]=.123;m[1][3]=-.456;m[2][3]=.789;return new NTransform(m);
    }
    static object Point(object x,NTransform t,bool vector=false){var v=NV.Read(x);return (vector?t.Vector(v):t.Point(v)).Data();}
    public static object Move(object source,NTransform t){
        var b=AB.Obj(J.DeserializeObject(J.Serialize(source)));bool mirror=TS04.Det(t)<0;
        foreach(var eo in AB.Array(AB.Get(b,"edges"))){
            var e=AB.Obj(eo);var cv=AB.Obj(AB.Get(e,"curve"));
            if(AB.Bool(AB.Get(cv,"isCircle"))){var p=AB.Array(AB.Get(cv,"circleParams")).Select(AB.Num).ToArray();var c=t.Point(new NV(p[0],p[1],p[2]));var n=t.Vector(new NV(p[3],p[4],p[5]))*(mirror?-1:1);cv["circleParams"]=c.Data().Concat(n.Data()).Concat(new[]{p[6]}).ToArray();}
            else {var p=AB.Array(AB.Get(cv,"ellipseParams")).Select(AB.Num).ToArray();var c=t.Point(new NV(p[0],p[1],p[2]));var a=t.Vector(new NV(p[4],p[5],p[6]));var d=t.Vector(new NV(p[8],p[9],p[10]));cv["ellipseParams"]=c.Data().Concat(new[]{p[3]}).Concat(a.Data()).Concat(new[]{p[7]}).Concat(d.Data()).ToArray();}
            var bounds=AB.Obj(AB.Get(e,"bounds"));foreach(var key in new[]{"startPointM","endPointM"})bounds[key]=Point(bounds[key],t);
        }
        foreach(var fo in AB.Array(AB.Get(b,"faces"))){
            var f=AB.Obj(fo);var surf=AB.Obj(AB.Get(f,"surface"));string type=(string)surf["type"];
            foreach(var key in new[]{"rootPointM","originM"})if(surf.ContainsKey(key))surf[key]=Point(surf[key],t);
            foreach(var key in new[]{"normal","axis"})if(surf.ContainsKey(key))surf[key]=Point(surf[key],t,true);
            foreach(var key in new[]{"naturalNormalWitnessM","naturalNormalEvaluationRaw"})if(f.ContainsKey(key))f[key]=Point(f[key],t,key=="naturalNormalEvaluationRaw");
            foreach(var lo in AB.Array(f["loops"]))foreach(var co in AB.Array(AB.Get(lo,"coedges"))){
                var c=AB.Obj(co);var p=AB.Array(c["curveBoundsRaw"]).Select(AB.Num).ToArray();
                var start=t.Point(new NV(p[0],p[1],p[2]));var end=t.Point(new NV(p[3],p[4],p[5]));
                bool ellipse=AB.Bool(AB.Get(AB.Get(AB.Array(b["edges"])[AB.Int(c["edgeId"])],"curve"),"isEllipse"));
                if(mirror){c["sense"]=!AB.Bool(c["sense"]);double z=p[6];p[6]=p[7];p[7]=z;}
                c["curveBoundsRaw"]=start.Data().Concat(end.Data()).Concat(p.Skip(6)).ToArray();
            }
        }
        return b;
    }
    public static int Run(){
        var old=Console.Out;var capture=new StringWriter();Console.SetOut(capture);int legacy;
        try{legacy=NumericalBrepV03Tests.Run();}finally{Console.SetOut(old);}
        Test("previous 47 regressions",()=>legacy==0);
        var tube=V03Fixtures.Tube();
        Test("arbitrary spatial rotation and translation",()=>Flag(TS04.Verify(Report(tube,Move(tube,Rotation(.637)))),"properDiagnosticMatchFound"));
        Test("symmetry and multiple transformations",()=>Flag(TS04.Verify(Report(tube,tube)),"multipleTransformsDetected"));
        Test("mirror candidate independently found",()=>{
            var m=new NTransform(new[]{new[]{-1d,0,0,0},new[]{0d,1,0,0},new[]{0d,0,1,0},new[]{0d,0,0,1}});
            return Flag(TS04.Verify(Report(tube,Move(tube,m))),"improperDiagnosticMatchFound");});
        Test("changed radius",()=>!Flag(TS04.Verify(Report(tube,V03Fixtures.Tube(inner:.26))),"properDiagnosticMatchFound"));
        Test("changed length",()=>!Flag(TS04.Verify(Report(tube,V03Fixtures.Tube(length:4.01))),"properDiagnosticMatchFound"));
        Test("changed hole position",()=>!Flag(TS04.Verify(Report(tube,V03Fixtures.Tube(holeX:.05))),"properDiagnosticMatchFound"));
        Test("chiral blind holes: mirror without proper match",()=>{
            var chiral=V04Fixtures.BlindHoles();var m=new NTransform(new[]{new[]{-1d,0,0,0},new[]{0d,1,0,0},new[]{0d,0,1,0},new[]{0d,0,0,1}});
            var r=TS04.Verify(Report(chiral,Move(chiral,m)));
            return Flag(r,"improperDiagnosticMatchFound")&&!Flag(r,"properDiagnosticMatchFound");});
        Test("blind holes after arbitrary rotation",()=>Flag(TS04.Verify(Report(V04Fixtures.BlindHoles(),Move(V04Fixtures.BlindHoles(),Rotation(.413)))),"properDiagnosticMatchFound"));
        Test("opposite end material orientation rejected",()=>{
            var b=AB.Obj(V03Fixtures.Tube());var f=AB.Obj(AB.Array(b["faces"])[2]);var surf=AB.Obj(f["surface"]);surf["normal"]=new[]{0d,0,-1};
            return !Flag(TS04.Verify(Report(tube,b)),"properDiagnosticMatchFound");});
        Test("entity array ordering does not imply correspondence",()=>{
            var b=AB.Obj(V03Fixtures.Tube());b["edges"]=AB.Array(b["edges"]).Reverse().ToArray();b["faces"]=AB.Array(b["faces"]).Reverse().ToArray();
            return Flag(TS04.Verify(Report(tube,b)),"properDiagnosticMatchFound");});
        Test("incomplete coedge data never accepted",()=>{
            var b=AB.Obj(V03Fixtures.Tube());var f=AB.Obj(AB.Array(b["faces"])[0]);var l=AB.Obj(AB.Array(f["loops"])[0]);l["coedges"]=new object[0];
            return !Flag(TS04.Verify(Report(tube,b)),"properDiagnosticMatchFound");});
        Test("geometry change below tolerance",()=>Flag(TS04.Verify(Report(tube,V03Fixtures.Tube(length:4.0000001))),"properDiagnosticMatchFound"));
        Test("geometry change above tolerance",()=>!Flag(TS04.Verify(Report(tube,V03Fixtures.Tube(length:4.00001))),"properDiagnosticMatchFound"));
        Test("reject shear matrix",()=>!NB03.Passed(TS04.Check(new NTransform(new[]{new[]{1d,.001,0,0},new[]{0d,1,0,0},new[]{0d,0,1,0},new[]{0d,0,0,1}}))));
        Test("independent saved control pair discovery",()=>{
            var path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"compare-1-1-vs-1-2-v03.json");
            var r=TS04.Verify(AB.Obj(J.DeserializeObject(File.ReadAllText(path))));
            return AB.Array(AB.Get(r,"candidates")).Any(c=>{
                if(!Flag(c,"diagnosticsCompatible")||(string)AB.Get(c,"source")!="ANALYTIC_FEATURE_FRAME")return false;
                var t=new NTransform(AB.Get(c,"matrix"));var expected=new[]{1d,-1,-1};
                for(int i=0;i<3;i++)for(int j=0;j<3;j++)if(Math.Abs(t.M[i][j]-(i==j?expected[i]:0))>1e-10)return false;
                return Math.Abs(t.M[1][3]-.670)<1e-10&&Math.Abs(t.M[0][3])<1e-10&&Math.Abs(t.M[2][3])<1e-10;
            });});
        Test("always withhold uncertified classification",()=>(string)AB.Get(TS04.Verify(Report(tube,tube)),"classification")=="NEEDS_REVIEW");
        var output=new{legacyOutput=capture.ToString(),tests=results,passed=results.Count(NB03.Passed),failed=results.Count(x=>!NB03.Passed(x)),comCalls=0};
        string json=J.Serialize(output);File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results-mfg-legacy-v04.json"),json);
        Console.WriteLine(json);return results.All(NB03.Passed)?0:1;
    }
}

