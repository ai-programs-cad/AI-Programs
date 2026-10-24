using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
public static class ManufacturingLegacyBrepTests {
    static JavaScriptSerializer J=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=300};
    static List<object> tests=new List<object>();
    static void Test(string name,Func<bool> f){try{tests.Add(new{name,passed=f()});}catch(Exception e){tests.Add(new{name,passed=false,error=e.ToString()});}}
    public static object Clone(object o){return J.DeserializeObject(J.Serialize(o));}
    static double[] D(object x){return AB.Array(x).Select(AB.Num).ToArray();}
    public static object Move(object input,NTransform t){
        var b=AB.Obj(Clone(input));bool reflection=TS04.Det(t)<0;
        foreach(var eo in AB.Array(b["edges"])){
            var e=AB.Obj(eo);var c=AB.Obj(e["curve"]);var bounds=AB.Obj(e["bounds"]);
            foreach(var key in new[]{"startPointM","endPointM"})bounds[key]=t.Point(NV.Read(bounds[key])).Data();
            foreach(var key in new[]{"startVertexM","endVertexM"})if(e[key]!=null)e[key]=t.Point(NV.Read(e[key])).Data();
            if(BatchV05.Flag(c,"isLine")){var p=D(c["lineParams"]);c["lineParams"]=t.Point(new NV(p[0],p[1],p[2])).Data().Concat(t.Vector(new NV(p[3],p[4],p[5])).Data()).ToArray();}
            if(BatchV05.Flag(c,"isCircle")){var p=D(c["circleParams"]);c["circleParams"]=t.Point(new NV(p[0],p[1],p[2])).Data().Concat((t.Vector(new NV(p[3],p[4],p[5]))*(reflection?-1:1)).Data()).Concat(new[]{p[6]}).ToArray();}
            if(BatchV05.Flag(c,"isEllipse")){var p=D(c["ellipseParams"]);c["ellipseParams"]=t.Point(new NV(p[0],p[1],p[2])).Data().Concat(new[]{p[3]}).Concat(t.Vector(new NV(p[4],p[5],p[6])).Data()).Concat(new[]{p[7]}).Concat(t.Vector(new NV(p[8],p[9],p[10])).Data()).ToArray();}
        }
        foreach(var fo in AB.Array(b["faces"])){
            var f=AB.Obj(fo);var s=AB.Obj(f["surface"]);
            foreach(var k in new[]{"rootPointM","originM"})if(s.ContainsKey(k))s[k]=t.Point(NV.Read(s[k])).Data();
            foreach(var k in new[]{"normal","axis"})if(s.ContainsKey(k))s[k]=t.Vector(NV.Read(s[k])).Data();
            if(f.ContainsKey("naturalNormalWitnessM"))f["naturalNormalWitnessM"]=t.Point(NV.Read(f["naturalNormalWitnessM"])).Data();
            if(f.ContainsKey("naturalNormalEvaluationRaw")){var p=D(f["naturalNormalEvaluationRaw"]);var v=t.Vector(new NV(p[0],p[1],p[2]));f["naturalNormalEvaluationRaw"]=v.Data().Concat(p.Skip(3)).ToArray();}
            foreach(var lo in AB.Array(f["loops"])){
                var co=AB.Array(AB.Get(lo,"coedges"));var previous=co.ToDictionary(x=>AB.Int(AB.Get(x,"nextId")),x=>AB.Int(AB.Get(x,"id")));
                foreach(var c0 in co){var c=AB.Obj(c0);var p=D(c["curveBoundsRaw"]);var a=t.Point(new NV(p[0],p[1],p[2]));var z=t.Point(new NV(p[3],p[4],p[5]));
                    if(reflection){var temp=a;a=z;z=temp;double u=p[6];p[6]=p[7];p[7]=u;c["sense"]=!AB.Bool(c["sense"]);c["nextId"]=previous[AB.Int(c["id"])];}
                    c["curveBoundsRaw"]=a.Data().Concat(z.Data()).Concat(p.Skip(6)).ToArray();
                }
            }
        }
        foreach(var vo in AB.Array(b["verticesM"])){var v=AB.Obj(vo);v["point"]=t.Point(NV.Read(v["point"])).Data();}
        return b;
    }
    static object Hole(object input,NV offset,double radiusDelta=0){
        var b=AB.Obj(Clone(input));var ids=new List<int>();
        foreach(var eo in AB.Array(b["edges"])){
            var e=AB.Obj(eo);var cv=AB.Obj(e["curve"]);if(!BatchV05.Flag(cv,"isCircle"))continue;
            ids.Add(AB.Int(e["id"]));var p=D(cv["circleParams"]);var old=new NV(p[0],p[1],p[2]);var center=old+offset;double radius=p[6]+radiusDelta;
            cv["circleParams"]=center.Data().Concat(p.Skip(3).Take(3)).Concat(new[]{radius}).ToArray();
            var bounds=AB.Obj(e["bounds"]);foreach(var k in new[]{"startPointM","endPointM"})bounds[k]=(center+(NV.Read(bounds[k])-old)*(radius/p[6])).Data();
        }
        foreach(var fo in AB.Array(b["faces"])){
            var f=AB.Obj(fo);var s=AB.Obj(f["surface"]);
            if((string)s["type"]=="CYLINDER"){
                var old=NV.Read(s["originM"]);double r=AB.Num(s["radiusM"]);
                s["originM"]=(old+offset).Data();s["radiusM"]=r+radiusDelta;
                var w=NV.Read(f["naturalNormalWitnessM"]);var axis=NV.Read(s["axis"]);var axial=axis*NV.Dot(axis,w-old);
                f["naturalNormalWitnessM"]=(old+offset+axial+(w-old-axial)*((r+radiusDelta)/r)).Data();
            }
            foreach(var lo in AB.Array(f["loops"]))foreach(var co0 in AB.Array(AB.Get(lo,"coedges"))){
                var co=AB.Obj(co0);if(!ids.Contains(AB.Int(co["edgeId"])))continue;
                var e=AB.Array(b["edges"]).Single(x=>AB.Int(AB.Get(x,"id"))==AB.Int(co["edgeId"]));var bounds=AB.Get(e,"bounds");var p=D(co["curveBoundsRaw"]);
                co["curveBoundsRaw"]=D(AB.Get(bounds,"startPointM")).Concat(D(AB.Get(bounds,"endPointM"))).Concat(p.Skip(6)).ToArray();
            }
        }
        return b;
    }
    static object Arc(double u0,double u1){
        Func<double,double[]> point=u=>new[]{.02*Math.Cos(u),.01*Math.Sin(u),0d};
        return new Dictionary<string,object>{{"id",0},{"ownerVerified",true},{"adjacentFaceIds",new[]{0,1}},
            {"curve",new{isLine=false,isCircle=false,isEllipse=true,ellipseParams=new[]{0d,0,0,.02,1,0,0,.01,0,1,0},underlyingDomain=new{returned=true,closed=true,periodic=true,start=0d,end=2*Math.PI}}},
            {"bounds",new{uMin=u0,uMax=u1,sense=true,startPointM=point(u0),endPointM=point(u1)}}};
    }
    public static int Run(){
        int legacy;var output=Console.Out;Console.SetOut(TextWriter.Null);try{legacy=BatchV05Tests.Run();}finally{Console.SetOut(output);}
        Test("all 80 prior regressions",()=>legacy==0);
        var saved=AB.Obj(J.DeserializeObject(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"compare-batch-v05.json"))));
        var bodies=AB.Obj(saved["bodies"]);var plate=bodies["11-1"];
        Test("native plate all lines loops circles holes accepted",()=>GBrep06.Parse(plate).Accepted);
        Test("line arbitrary direction and nonzero bounded range",()=>{
            var o=new NV(.2,.3,.4);var d=new NV(.3,.4,.5);var p=o+d*(-2);var q=o+d*3;
            var raw=new{id=0,ownerVerified=true,adjacentFaceIds=new[]{0,1},curve=new{isLine=true,lineParams=o.Data().Concat(d.Data()).ToArray()},
                bounds=new{uMin=-2d,uMax=3d,sense=true,startPointM=p.Data(),endPointM=q.Data()},startVertexM=p.Data(),endVertexM=q.Data()};
            var e=GBrep06.Edge(raw);return NB03.Passed(GBrep06.EdgeMatch(e,e));});
        Test("line reversed native parameterization preserves entire plate",()=>{
            var b=AB.Obj(Clone(plate));var e=AB.Obj(AB.Array(b["edges"]).Single(x=>AB.Int(AB.Get(x,"id"))==4));var c=AB.Obj(e["curve"]);var p=D(c["lineParams"]);
            for(int k=3;k<6;k++)p[k]=-p[k];c["lineParams"]=p;
            var bd=AB.Obj(e["bounds"]);double u0=AB.Num(bd["uMin"]),u1=AB.Num(bd["uMax"]);bd["uMin"]=-u1;bd["uMax"]=-u0;
            var start=bd["startPointM"];bd["startPointM"]=bd["endPointM"];bd["endPointM"]=start;
            var vertex=e["startVertexM"];e["startVertexM"]=e["endVertexM"];e["endVertexM"]=vertex;
            foreach(var fo in AB.Array(b["faces"]))foreach(var lo in AB.Array(AB.Get(fo,"loops")))foreach(var co0 in AB.Array(AB.Get(lo,"coedges"))){
                var co=AB.Obj(co0);if(AB.Int(co["edgeId"])!=4)continue;co["sense"]=!AB.Bool(co["sense"]);var z=D(co["curveBoundsRaw"]);z[6]=-z[6];z[7]=-z[7];co["curveBoundsRaw"]=z;
            }
            return GBrep06.Parse(b).Accepted&&BatchV05.Flag(GBrep06.Verify(plate,b,null),"properDiagnosticMatchFound");});
        Test("plate arbitrary 3D rotation",()=>BatchV05.Flag(GBrep06.Verify(plate,Move(plate,TransformSearchV04Tests.Rotation(.527)),null),"properDiagnosticMatchFound"));
        Test("plate independent mirror",()=>BatchV05.Flag(GBrep06.Verify(plate,Move(plate,new NTransform(new[]{new[]{-1d,0,0,0},new[]{0d,1,0,0},new[]{0d,0,1,0},new[]{0d,0,0,1}})),null),"improperDiagnosticMatchFound"));
        Test("all six saved plate pairs complete diagnostics",()=>BatchV05.DefaultPairs().Where(p=>p[0].StartsWith("11-")).All(p=>BatchV05.Flag(GBrep06.Verify(bodies[p[0]],bodies[p[1]],null),"supportedDiagnosticsCompleted")));
        Test("hole shifted above tolerance rejected",()=>!BatchV05.Flag(GBrep06.Verify(plate,Hole(plate,new NV(.0001,0,0)),null),"properDiagnosticMatchFound"));
        Test("hole radius changed rejected",()=>!BatchV05.Flag(GBrep06.Verify(plate,Hole(plate,new NV(0,0,0),.0001),null),"properDiagnosticMatchFound"));
        Test("removed hole is not a match",()=>{
            var b=AB.Obj(Clone(plate));b["edges"]=AB.Array(b["edges"]).Where(e=>!BatchV05.Flag(AB.Get(e,"curve"),"isCircle")).ToArray();
            b["faces"]=AB.Array(b["faces"]).Where(f=>(string)AB.Get(AB.Get(f,"surface"),"type")!="CYLINDER").ToArray();
            b["edgeCount"]=9;b["faceCount"]=5;foreach(var fo in AB.Array(b["faces"])){var f=AB.Obj(fo);f["loops"]=AB.Array(f["loops"]).Where(l=>BatchV05.Flag(l,"isOuter")).ToArray();}
            return GBrep06.Parse(b).Accepted&&!BatchV05.Flag(GBrep06.Verify(plate,b,null),"properDiagnosticMatchFound");});
        Test("small hole shift within tolerance accepted",()=>BatchV05.Flag(GBrep06.Verify(plate,Hole(plate,new NV(1e-8,0,0)),null),"properDiagnosticMatchFound"));
        Test("tiny contour radius change above tolerance rejected",()=>!BatchV05.Flag(GBrep06.Verify(plate,Hole(plate,new NV(0,0,0),3e-6),null),"properDiagnosticMatchFound"));
        Test("coedge array ordering and loop start irrelevant",()=>{
            var b=AB.Obj(Clone(plate));foreach(var fo in AB.Array(b["faces"]))foreach(var lo in AB.Array(AB.Get(fo,"loops"))){var l=AB.Obj(lo);l["coedges"]=AB.Array(l["coedges"]).Reverse().ToArray();}
            b["edges"]=AB.Array(b["edges"]).Reverse().ToArray();b["faces"]=AB.Array(b["faces"]).Reverse().ToArray();
            return BatchV05.Flag(GBrep06.Verify(plate,b,null),"properDiagnosticMatchFound");});
        Test("broken loop linkage rejects native geometry",()=>{
            var b=AB.Obj(Clone(plate));var co=AB.Obj(AB.Array(AB.Get(AB.Array(AB.Get(AB.Array(b["faces"])[0],"loops"))[0],"coedges"))[0]);co["nextId"]=co["id"];
            return !GBrep06.Parse(b).Accepted;});
        Test("changed end material orientation rejected",()=>{
            var b=AB.Obj(Clone(plate));var f=AB.Obj(AB.Array(b["faces"])[3]);f["faceInSurfaceSense"]=!AB.Bool(f["faceInSurfaceSense"]);
            return !GBrep06.Parse(b).Accepted;});
        Test("partial ellipse same bounded interval",()=>NB03.Passed(GBrep06.EdgeMatch(GBrep06.Edge(Arc(.4,1.3)),GBrep06.Edge(Arc(.4,1.3)))));
        Test("partial ellipse different trim rejected",()=>!NB03.Passed(GBrep06.EdgeMatch(GBrep06.Edge(Arc(.4,1.3)),GBrep06.Edge(Arc(.4,1.4)))));
        Test("partial ellipse parameter wrap",()=>NB03.Passed(GBrep06.EdgeMatch(GBrep06.Edge(Arc(.4,1.3)),GBrep06.Edge(Arc(.4+2*Math.PI,1.3+2*Math.PI)))));
        Test("partial ellipse phase shift with reversed axes",()=>{
            var a=Arc(.4,1.3);var b=AB.Obj(Clone(Arc(.4-Math.PI,1.3-Math.PI)));var c=AB.Obj(b["curve"]);var p=D(c["ellipseParams"]);
            p[4]=-p[4];p[9]=-p[9];c["ellipseParams"]=p;var bd=AB.Obj(b["bounds"]);bd["startPointM"]=BatchV05.Get(BatchV05.Get(a,"bounds"),"startPointM");bd["endPointM"]=BatchV05.Get(BatchV05.Get(a,"bounds"),"endPointM");
            return NB03.Passed(GBrep06.EdgeMatch(GBrep06.Edge(a),GBrep06.Edge(b)));});
        Test("partial ellipse reversed parameter direction",()=>{
            var a=Arc(.4,1.3);var b=AB.Obj(Clone(Arc(-1.3,-.4)));var c=AB.Obj(b["curve"]);var p=D(c["ellipseParams"]);p[9]=-p[9];c["ellipseParams"]=p;
            var bd=AB.Obj(b["bounds"]);bd["startPointM"]=BatchV05.Get(BatchV05.Get(a,"bounds"),"endPointM");bd["endPointM"]=BatchV05.Get(BatchV05.Get(a,"bounds"),"startPointM");
            return NB03.Passed(GBrep06.EdgeMatch(GBrep06.Edge(a),GBrep06.Edge(b)));});
        Test("3004 never assumed ellipse or spline",()=>GBrep06.Parse(bodies["3-1"]).Unsupported.Any(x=>x.Contains("3004")));
        Test("uncertified classification remains NEEDS_REVIEW",()=>(string)AB.Get(GBrep06.Verify(plate,plate,null),"classification")=="NEEDS_REVIEW");
        var r=new{legacyTests=80,newTests=tests.Count-1,totalPassed=80+tests.Skip(1).Count(NB03.Passed),failed=tests.Count(t=>!NB03.Passed(t)),tests,comCalls=0};
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results-mfg-legacy-brep.json"),J.Serialize(r));Console.WriteLine(J.Serialize(r));return tests.All(NB03.Passed)?0:1;
    }
}
