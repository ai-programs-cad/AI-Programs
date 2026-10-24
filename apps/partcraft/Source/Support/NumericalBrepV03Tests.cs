using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

public static class NumericalBrepV03Tests {
    static JavaScriptSerializer J=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=200};
    static Dictionary<string,object> D(params object[] p){var d=new Dictionary<string,object>();for(int i=0;i<p.Length;i+=2)d[(string)p[i]]=p[i+1];return d;}
    static object Canon(object o){return J.DeserializeObject(J.Serialize(o));}
    static object Candidate(object matrix){return Canon(D("apiReturned",true,"transform",D("homogeneousColumnVectorMatrix",matrix)));}
    static Dictionary<string,object> Direction(object a,object b,object matrix=null){return AB.Obj(NB03.Direction(NB03.Parse(a),NB03.Parse(b),Candidate(matrix??V03Fixtures.Matrix())));}
    static object Report(object a,object b){return Canon(D("bodyA",a,"bodyB",b,"coincidenceAtoB",Candidate(V03Fixtures.Matrix()),"coincidenceBtoA",Candidate(V03Fixtures.Matrix()),"observedStateUnchanged",true,"errors",new object[0],"dataGaps",new object[0]));}
    static object EllipseFixture(double normNoise=0,double dotNoise=0){
        var body=AB.Obj(V03Fixtures.Tube());
        foreach(var e in AB.Array(body["edges"])){
            var cv=AB.Obj(AB.Get(e,"curve"));var p=AB.Array(cv["circleParams"]).Select(AB.Num).ToArray();
            cv["isEllipse"]=true;cv["isCircle"]=false;cv["ellipseParams"]=new[]{p[0],p[1],p[2],p[6],1+normNoise,0,0,p[6],dotNoise,1,0};
        }
        return Canon(body);
    }
    public static int Run(){
        var cases=new List<object>();int failed=0;object legacy=null;
        var original=Console.Out;
        try{var capture=new StringWriter(System.Globalization.CultureInfo.InvariantCulture);Console.SetOut(capture);int status=AnalyticalBrepTests.Run();legacy=J.DeserializeObject(capture.ToString());if(status!=0)failed++;}
        catch(Exception ex){failed++;legacy=new{status="FAIL",reason=ex.Message};}
        finally{Console.SetOut(original);}
        Action<string,Func<bool>> test=(name,fn)=>{try{NB03.Tolerance=new V03Tolerance();bool ok=fn();cases.Add(new{name,passed=ok});if(!ok)failed++;}catch(Exception ex){cases.Add(new{name,passed=false,reason=ex.Message});failed++;}};
        test("ellipse norm roundoff accepted",()=>{var b=NB03.Parse(EllipseFixture(4e-15));return b.Issues.Count==0&&b.Numeric.All(NB03.Passed)&&b.Edges.Count==4;});
        test("ellipse orthogonality roundoff accepted",()=>{var b=NB03.Parse(EllipseFixture(0,3e-15));return b.Issues.Count==0&&b.Numeric.All(NB03.Passed);});
        test("invalid axes reported without stopping remaining edges",()=>{var b=NB03.Parse(EllipseFixture(1e-5,1e-5));return b.Edges.Count==4&&b.Faces.Count==4&&b.Numeric.Any(x=>!NB03.Passed(x))&&b.Domain.Count>0;});
        test("normalization repair retains original doubles",()=>{var b=EllipseFixture(4e-15,3e-15);string before=J.Serialize(b);NB03.Parse(b);return before==J.Serialize(b);});
        test("ellipse noise still reaches mapping and topology",()=>{var r=Direction(EllipseFixture(),EllipseFixture(4e-15,3e-15));return AB.Bool(r["contourDiagnosticsCompatible"])&&AB.Array(r["topologyChecks"]).Length>0;});
        test("near-orthogonal matrix accepted numerically",()=>{var m=(double[][])V03Fixtures.Matrix();m[0][0]+=4e-15;return NB03.Passed(new NTransform(Canon(m)).Check(NB03.Tolerance));});
        test("invalid matrix scale rejected",()=>{var m=(double[][])V03Fixtures.Matrix();m[0][0]=1.00001;return !NB03.Passed(new NTransform(Canon(m)).Check(NB03.Tolerance));});
        test("improper matrix rejected without MIRRORED",()=>{var m=(double[][])V03Fixtures.Matrix();m[0][0]=-1;return !NB03.Passed(new NTransform(Canon(m)).Check(NB03.Tolerance));});
        test("nonfinite translation explicitly rejected",()=>{var m=(double[][])V03Fixtures.Matrix();m[0][3]=Double.NaN;return !NB03.Passed(new NTransform(m).Check(NB03.Tolerance));});
        test("raw explicit scale checked",()=>{var raw=new double[16];raw[12]=1.1;return !NB03.Passed(new NTransform(Canon(V03Fixtures.Matrix())).Check(NB03.Tolerance,raw));});
        test("small radius change within geometric tolerance is diagnostic-compatible",()=>AB.Bool(Direction(V03Fixtures.Tube(),V03Fixtures.Tube(false,0,.25+1e-8))["contourDiagnosticsCompatible"]));
        test("radius change above geometric tolerance is detected",()=>!AB.Bool(Direction(V03Fixtures.Tube(),V03Fixtures.Tube(false,0,.25+2e-6))["contourDiagnosticsCompatible"]));
        test("small hole location change within tolerance is retained",()=>AB.Bool(Direction(V03Fixtures.Tube(),V03Fixtures.Tube(false,1e-8))["contourDiagnosticsCompatible"]));
        test("hole location change above tolerance is detected",()=>!AB.Bool(Direction(V03Fixtures.Tube(),V03Fixtures.Tube(false,2e-6))["contourDiagnosticsCompatible"]));
        test("small length change within tolerance is diagnostic-compatible",()=>AB.Bool(Direction(V03Fixtures.Tube(),V03Fixtures.Tube(false,0,.25,4+1e-8))["contourDiagnosticsCompatible"]));
        test("matrix translation retained and tested geometrically",()=>{var m=(double[][])V03Fixtures.Matrix();m[0][3]=2e-6;var r=Direction(V03Fixtures.Tube(),V03Fixtures.Tube(),m);return NB03.Passed(r["transformChecks"])&&!AB.Bool(r["contourDiagnosticsCompatible"]);});
        test("passing diagnostics never fabricate certified bound",()=>{var r=AB.Obj(NB03.Verify(AB.Obj(Report(EllipseFixture(),EllipseFixture(4e-15,3e-15)))));return (string)r["classification"]=="NEEDS_REVIEW"&&r["certifiedUpperBoundM"]==null&&r["maximumDeviationM"]==null&&!AB.Bool(r["brepEquivalenceVerified"]);});
        test("all edges checked after one unsupported edge",()=>{var b=AB.Obj(EllipseFixture());var cv=AB.Obj(AB.Get(AB.Array(b["edges"])[0],"curve"));cv["isEllipse"]=false;var parsed=NB03.Parse(b);return parsed.Issues.Count>0&&parsed.Edges.Count==3&&parsed.Faces.Count==4;});
        test("missing loops recorded while surface checks continue",()=>{var b=AB.Obj(EllipseFixture());AB.Obj(AB.Array(b["faces"])[0]).Remove("loops");var parsed=NB03.Parse(b);return parsed.Faces.Count==4&&parsed.Issues.Count>0&&parsed.Numeric.Count>=8;});
        test("computational and geometric tolerance independent",()=>{var body=EllipseFixture(1e-7);NB03.Tolerance.GeometryM=.1;return NB03.Parse(body).Numeric.Any(x=>!NB03.Passed(x));});
        test("tightened geometric tolerance resolves small geometry change",()=>{NB03.Tolerance.GeometryM=1e-10;return !AB.Bool(Direction(V03Fixtures.Tube(),V03Fixtures.Tube(false,1e-8))["contourDiagnosticsCompatible"]);});
        test("no empty-data vacuous geometry success",()=>{var b=NB03.Parse(D());var r=AB.Obj(NB03.Direction(b,b,Candidate(V03Fixtures.Matrix())));return !AB.Bool(r["surfaceDiagnosticsCompatible"])&&!AB.Bool(r["contourDiagnosticsCompatible"]);});
        test("declined API candidate is recorded and checks continue",()=>{var c=AB.Obj(Candidate(V03Fixtures.Matrix()));c["apiReturned"]=false;var r=AB.Obj(NB03.Direction(NB03.Parse(V03Fixtures.Tube()),NB03.Parse(V03Fixtures.Tube()),c));return !AB.Bool(r["transformAccepted"])&&AB.Array(r["contourChecks"]).Length==16;});
        test("corrupt coedge endpoints do not pass trim diagnostics",()=>{var b=AB.Obj(V03Fixtures.Tube());var loop=AB.Array(AB.Get(AB.Array(b["faces"])[0],"loops"))[0];var co=AB.Obj(AB.Array(AB.Get(loop,"coedges"))[0]);var raw=AB.Array(co["curveBoundsRaw"]).Select(AB.Num).ToArray();raw[0]+=.01;co["curveBoundsRaw"]=raw;var parsed=NB03.Parse(b);return parsed.Topology.Any(x=>!NB03.Passed(x))&&parsed.Domain.Any(x=>!NB03.Passed(x));});
        NB03.Tolerance=new V03Tolerance();
        int legacyCount=0;object count;if(AB.TryGet(legacy,"count",out count))legacyCount=AB.Int(count);
        Console.WriteLine(J.Serialize(new{status=failed==0?"PASS":"FAIL",totalTests=legacyCount+cases.Count,legacyV02=legacy,v03Tests=cases,failed,comCalls=0}));return failed==0?0:1;
    }
}
