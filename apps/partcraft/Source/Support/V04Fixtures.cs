using System;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
public static class V04Fixtures {
    static Dictionary<string,object> D(params object[] p){var d=new Dictionary<string,object>();for(int i=0;i<p.Length;i+=2)d[(string)p[i]]=p[i+1];return d;}
    // Three unequal blind holes make top and bottom distinct and break reflection symmetry.
    public static object BlindHoles(){
        var edges=new List<object>();var faces=new List<object>();
        Action<int,double,double,double,double,int,int> edge=(id,x,y,z,r,a,b)=>{
            var p=new[]{x+r,y,z};
            edges.Add(D("id",id,"ownerVerified",true,"curve",D("isLine",false,"isCircle",true,"isEllipse",false,
                "circleParams",new[]{x,y,z,0d,0,1,r},"underlyingDomain",D("returned",true,"start",0,"end",2*Math.PI,"closed",true,"periodic",true)),
                "bounds",D("startPointM",p,"endPointM",p,"uMin",0,"uMax",2*Math.PI,"sense",true),"startVertexM",null,"endVertexM",null,"adjacentFaceIds",new[]{a,b}));};
        Func<int,bool,bool,object> loop=(id,outer,sense)=>{
            var p=AB.Array(AB.Get(AB.Get(edges[id],"bounds"),"startPointM")).Select(AB.Num).ToArray();
            return D("id",id,"isOuter",outer,"isSingular",false,"coedgeCount",1,"coedges",new[]{D("id",0,"edgeId",id,"nextId",0,"sense",sense,"curveBoundsRaw",p.Concat(p).Concat(new[]{sense?0:2*Math.PI,sense?2*Math.PI:0,0,0}).ToArray())});};
        Action<int,bool,double,double,double,double,object[]> face=(id,cyl,x,y,z,r,loops)=>{
            faces.Add(D("id",id,"ownerVerified",true,"faceInSurfaceSense",cyl&&id!=0,"naturalCylinderRadialSign",1,
                "naturalNormalWitnessM",new[]{x+r,y,z},"naturalNormalEvaluationRaw",new[]{1d,0,0},
                "surface",cyl?D("type","CYLINDER","originM",new[]{x,y,z},"axis",new[]{0d,0,1},"radiusM",r):
                D("type","PLANE","rootPointM",new[]{x,y,z},"normal",new[]{0d,0,id==1?-1d:1}),
                "loops",loops));};
        edge(0,0,0,-2,.5,0,1);edge(1,0,0,2,.5,0,2);
        var holes=new[]{new[]{.13,.04,.08,.7},new[]{-.10,.07,.06,.9},new[]{.03,-.16,.04,1.1}};
        for(int h=0;h<3;h++){var p=holes[h];edge(2+2*h,p[0],p[1],p[3],p[2],3+2*h,4+2*h);edge(3+2*h,p[0],p[1],2,p[2],3+2*h,2);}
        face(0,true,0,0,0,.5,new[]{loop(0,true,true),loop(1,true,false)});
        face(1,false,0,0,-2,.5,new[]{loop(0,true,false)});
        face(2,false,0,0,2,.5,new[]{loop(1,true,true),loop(3,false,false),loop(5,false,false),loop(7,false,false)});
        for(int h=0;h<3;h++){var p=holes[h];face(3+2*h,true,p[0],p[1],0,p[2],new[]{loop(2+2*h,true,false),loop(3+2*h,true,true)});
            face(4+2*h,false,p[0],p[1],p[3],p[2],new[]{loop(2+2*h,true,true)});}
        var j=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=300};
        return j.DeserializeObject(j.Serialize(D("name","chiral-blind-hole-fixture","faceCount",faces.Count,"edgeCount",edges.Count,"vertexCount",0,"verticesM",new object[0],"faces",faces,"edges",edges)));
    }
}
