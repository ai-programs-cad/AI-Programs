using System;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

public static class V03Fixtures {
    static JavaScriptSerializer J=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=200};
    static Dictionary<string,object> D(params object[] p){var d=new Dictionary<string,object>();for(int i=0;i<p.Length;i+=2)d[(string)p[i]]=p[i+1];return d;}
    static object Canon(object x){return J.DeserializeObject(J.Serialize(x));}
    public static object Matrix(int turns=0){return turns==0?(object)new[]{new[]{1d,0,0,0},new[]{0d,1,0,0},new[]{0d,0,1,0},new[]{0d,0,0,1}}:new[]{new[]{0d,-1,0,8},new[]{1d,0,0,-4},new[]{0d,0,1,2},new[]{0d,0,0,1}};}
    static double[] P(double[] p,bool rotate,bool vector=false){return !rotate?p:new[]{-p[1]+(vector?0:8),p[0]+(vector?0:-4),p[2]+(vector?0:2)};}
    public static object Tube(bool rotate=false,double holeX=0,double inner=.25,double length=4){
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
            return D("id",eid,"isOuter",outer,"isSingular",false,"coedgeCount",1,"coedges",new[]{D("id",0,"edgeId",eid,"nextId",0,"sense",sense,
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
}
