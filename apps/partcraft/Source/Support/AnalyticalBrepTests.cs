using System;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

public static class AnalyticalBrepTests {
    static JavaScriptSerializer J=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=200};
    static Dictionary<string,object> D(params object[] p){var d=new Dictionary<string,object>();for(int i=0;i<p.Length;i+=2)d[(string)p[i]]=p[i+1];return d;}
    static object Canon(object x){return J.DeserializeObject(J.Serialize(x));}
    static object Matrix(int turns=0){return turns==0?(object)new[]{new[]{1d,0,0,0},new[]{0d,1,0,0},new[]{0d,0,1,0},new[]{0d,0,0,1}}:new[]{new[]{0d,-1,0,8},new[]{1d,0,0,-4},new[]{0d,0,1,2},new[]{0d,0,0,1}};}
    static double[] P(double[] p,bool rotate,bool vector=false){return !rotate?p:new[]{-p[1]+(vector?0:8),p[0]+(vector?0:-4),p[2]+(vector?0:2)};}
    static object Tube(bool rotate=false,double holeX=0,double inner=.25,double length=4){
        var edges=new List<object>();
        for(int i=0;i<4;i++){
            bool inside=i>=2;bool top=i%2==1;double r=inside?inner:.5,z=top?length/2:-length/2;
            double[] center=P(new[]{inside?holeX:0,0,z},rotate);double[] axis=P(new[]{0d,0,1},rotate,true);
            double[] point=P(new[]{(inside?holeX:0)+r,0,z},rotate);
            edges.Add(D("id",i,"ownerVerified",true,"curve",D("isLine",false,"isCircle",true,"isEllipse",false,
                "circleParams",center.Concat(axis).Concat(new[]{r}).ToArray(),"underlyingDomain",D("returned",true,"start",0,"end",2*Math.PI,"closed",true,"periodic",true)),
                "bounds",D("startPointM",point,"endPointM",point,"uMin",0,"uMax",2*Math.PI,"sense",true),"startVertexM",null,"endVertexM",null,
                "adjacentFaceIds",new[]{inside?0:1,top?2:3}));
        }
        Func<int,bool,bool,object> loop=(eid,outer,sense)=>{
            var edge=AB.Obj(edges[eid]);var b=AB.Get(edge,"bounds");var p=(double[])AB.Get(b,"startPointM");
            return D("isOuter",outer,"isSingular",false,"coedgeCount",1,"coedges",new[]{D("id",0,"edgeId",eid,"nextId",0,"sense",sense,
                "curveBoundsRaw",p.Concat(p).Concat(new[]{sense?0:2*Math.PI,sense?2*Math.PI:0,0,0}).ToArray())});};
        var faces=new List<object>();
        for(int i=0;i<4;i++){
            bool cyl=i<2;bool innerFace=i==0;bool top=i==2;
            object surface=cyl?D("type","CYLINDER","originM",P(new[]{innerFace?holeX:0,0d,0},rotate),"axis",P(new[]{0d,0,1},rotate,true),"radiusM",innerFace?inner:.5):
                D("type","PLANE","rootPointM",P(new[]{0d,0,top?length/2:-length/2},rotate),"normal",P(new[]{0d,0,top?1d:-1},rotate,true));
            object[] loops=cyl?(innerFace?new[]{loop(2,true,false),loop(3,true,true)}:new[]{loop(0,true,true),loop(1,true,false)}):
                (top?new[]{loop(1,true,true),loop(3,false,false)}:new[]{loop(0,true,false),loop(2,false,true)});
            faces.Add(D("id",i,"ownerVerified",true,"faceInSurfaceSense",innerFace,"naturalCylinderRadialSign",1,"naturalNormalWitnessM",P(new[]{(innerFace?holeX:0)+(innerFace?inner:.5),0d,0},rotate),"naturalNormalEvaluationRaw",P(new[]{1d,0,0},rotate,true),"surface",surface,"loops",loops));
        }
        return Canon(D("name","fixture","faceCount",4,"edgeCount",4,"vertexCount",0,"verticesM",new object[0],"edges",edges,"faces",faces));
    }
    static Dictionary<string,object> Verify(object a,object b,object ab,object ba){
        Func<object,object> candidate=m=>D("apiReturned",true,"transform",D("homogeneousColumnVectorMatrix",m));
        return AB.Obj(AB.Verify(AB.Obj(Canon(D("bodyA",a,"bodyB",b,"coincidenceAtoB",candidate(ab),"coincidenceBtoA",candidate(ba),"errors",new object[0],"dataGaps",new object[0],"observedStateUnchanged",true)))));
    }
    public static int Run(){
        var results=new List<object>();
        Action<string,bool,string> test=(name,ok,detail)=>{results.Add(new{name,passed=ok,detail});if(!ok)throw new Exception("FAILED: "+name+" "+detail);};
        var a=Tube();var b=Tube(true);
        var inverse=new[]{new[]{0d,1,0,4},new[]{-1d,0,0,8},new[]{0d,0,1,-2},new[]{0d,0,0,1}};
        var match=Verify(a,b,Matrix(1),inverse);
        test("identical tubes after exact rotation and translation",(string)match["classification"]=="IDENTICAL_3D",J.Serialize(match));
        // Same object/path as Main: Diagnostics returns an anonymous C# object.
        test("regression: anonymous body validation in Main",AB.Bool(AB.Get(match["bodyAValidation"],"certificateAvailable")),"Read certificateAvailable directly, without JSON normalization");
        var liveBodies=D("bodyA",D("trimDomainCertified",false),"bodyB",D("trimDomainCertified",false));
        AB.ApplyTrimDomainCertificates(liveBodies,match);
        test("regression: complete Main certificate propagation",AB.Bool(AB.Get(liveBodies["bodyA"],"trimDomainCertified"))&&AB.Bool(AB.Get(liveBodies["bodyB"],"trimDomainCertified")),"Diagnostics anonymous objects propagated into live body dictionaries without JSON conversion");
        var anonymous=new {certificateAvailable=true,child=new {value=0.004000000000000001},nullable=(object)null};
        test("anonymous nested property access",BitConverter.DoubleToInt64Bits(AB.Num(AB.Get(AB.Get(anonymous,"child"),"value")))==BitConverter.DoubleToInt64Bits(0.004000000000000001),"Native double unchanged");
        test("Obj adapts anonymous property object",AB.Bool(AB.Obj(anonymous)["certificateAvailable"]),"Reflection adapter; no direct dictionary cast");
        var dictionary=D("value",17,"nullable",null);object optional;
        test("dictionary identity and property access",Object.ReferenceEquals(dictionary,AB.Obj(dictionary))&&AB.Int(AB.Get(dictionary,"value"))==17,"Existing dictionary stays mutable by reference");
        test("optional anonymous null is present",AB.TryGet(anonymous,"nullable",out optional)&&optional==null,"Null is distinguished from a missing field");
        test("optional dictionary null is present",AB.TryGet(dictionary,"nullable",out optional)&&optional==null,"Dictionary null handled");
        test("missing field is optional false",!AB.TryGet(anonymous,"missing",out optional)&&!AB.TryGet(dictionary,"missing",out optional),"No InvalidCastException");
        test("null source is optional false",!AB.TryGet(null,"value",out optional)&&optional==null,"No NullReferenceException");
        bool nullRejected=false,missingRejected=false;
        try{AB.Get(anonymous,"nullable");}catch(InvalidOperationException ex){nullRejected=ex.Message.Contains("null")&&ex.Message.Contains("nullable");}
        try{AB.Get(dictionary,"missing");}catch(InvalidOperationException ex){missingRejected=ex.Message.Contains("Missing")&&ex.Message.Contains("missing");}
        test("required null and missing have explicit diagnostics",nullRejected&&missingRejected,"Controlled InvalidOperationException with the field name");
        var incompleteValidation=D("bodyAValidation",new {other=true},"bodyBValidation",null);
        AB.ApplyTrimDomainCertificates(liveBodies,incompleteValidation);
        test("Main handles missing and null certificate fields",!AB.Bool(AB.Get(liveBodies["bodyA"],"trimDomainCertified"))&&!AB.Bool(AB.Get(liveBodies["bodyB"],"trimDomainCertified")),"Absent proof remains false; no crash");
        var anonymousBody=D("bodyA",new {name="fixture",trimDomainCertified=false});
        AB.ApplyTrimDomainCertificates(anonymousBody,D("bodyAValidation",new {certificateAvailable=true}));
        test("Main supports anonymous body records",AB.Bool(AB.Get(anonymousBody["bodyA"],"trimDomainCertified")),"Anonymous body safely adapted for report updates");
        var cut=AB.Obj(Tube());var face=AB.Obj(AB.Array(cut["faces"])[2]);AB.Obj(face["surface"])["normal"]=new[]{0d,0,-1};
        var rejected=AB.Obj(AB.Direction(a,cut,Canon(Matrix())));
        test("opposite end-cut material orientation",!AB.Bool(rejected["verified"]),J.Serialize(rejected));
        var tilted=AB.Obj(Tube());var tf=AB.Obj(AB.Array(tilted["faces"])[2]);AB.Obj(tf["surface"])["normal"]=new[]{1d,0,1};
        var tiltCheck=AB.Obj(AB.Direction(a,tilted,Canon(Matrix())));
        test("end-cut plane geometry inconsistent with contours",!AB.Bool(tiltCheck["verified"])&&((string)tiltCheck["reason"]).Contains("supporting plane"),J.Serialize(tiltCheck));
        var hole=Verify(a,Tube(false,.125),Matrix(),Matrix());
        test("different hole location rejected",(string)hole["classification"]=="NEEDS_REVIEW",J.Serialize(hole));
        var radius=Verify(a,Tube(false,0,.125),Matrix(),Matrix());
        test("changed radius rejected",(string)radius["classification"]=="NEEDS_REVIEW",J.Serialize(radius));
        var length=Verify(a,Tube(false,0,.25,6),Matrix(),Matrix());
        test("changed length rejected",(string)length["classification"]=="NEEDS_REVIEW",J.Serialize(length));
        test("closed edge without vertices certified",AB.Parse(a).Edges.Count==4 && (string)match["classification"]=="IDENTICAL_3D","No synthetic vertices; full periodic conics are certified");
        var ellipse=AB.Obj(Canon(AB.Array(AB.Get(a,"edges"))[0]));var ellipseCurve=AB.Obj(ellipse["curve"]);
        ellipseCurve["isCircle"]=false;ellipseCurve["isEllipse"]=true;
        ellipseCurve["ellipseParams"]=new[]{0d,0,-2,.5,1,0,0,.25,0,1,0};
        var ec=AB.Edge(ellipse);
        var covariance=ec.Cov();
        test("full elliptical edge without vertices",covariance[0,0]==Rat.Of(.25)&&covariance[1,1]==Rat.Of(.0625)&&covariance[2,2]==0&&ellipse["startVertexM"]==null&&ellipse["endVertexM"]==null,"Exact ellipse center/axes/radii and one full period; no invented vertices");
        var missing=AB.Obj(Tube());AB.Obj(AB.Array(missing["faces"])[0]).Remove("loops");
        var incomplete=AB.Obj(AB.Direction(a,missing,Canon(Matrix())));
        test("missing trim data requires review",!AB.Bool(incomplete["verified"])&&((string)incomplete["reason"]).Contains("loops"),J.Serialize(incomplete));
        var approximate=(double[][])Matrix();approximate[0][0]=1.0000000000000002;
        var nonrigid=AB.Obj(AB.Direction(a,a,Canon(approximate)));
        test("near-rotation is not silently snapped",!AB.Bool(nonrigid["verified"]),J.Serialize(nonrigid));
        var reflected=(double[][])Matrix();reflected[0][0]=-1;
        var mirror=Verify(a,a,reflected,reflected);
        test("no MIRRORED classification without search",(string)mirror["classification"]=="NEEDS_REVIEW",J.Serialize(mirror));
        Console.WriteLine(J.Serialize(new{status="PASS",tests=results,count=results.Count,comCalls=0}));return 0;
    }
}
