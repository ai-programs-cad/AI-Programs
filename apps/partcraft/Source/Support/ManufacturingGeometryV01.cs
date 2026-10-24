using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
public class MPTube {
    public string status="NOT_VERIFIED";public double? outerDiameterMm,wallMm,maximumAxialSpanMm,axisPlaneIntersectionLengthMm,angleLeftDeg,angleRightDeg;
    public NV axis;public string cutScheme="SEE_PART_DRAWING";public List<string> warnings=new List<string>();
    public int cylindricalFaceCount,intersectionCurveCount;
}
public class MPPlate {
    public bool accepted;public string reason,text;public double? thicknessMm;public double[] extentsMm;public object projection;
}
public static class ManufacturingGeometryV01 {
    public static string CuttingSchematic(MPTube tube){
        if(!tube.angleLeftDeg.HasValue||!tube.angleRightDeg.HasValue||tube.intersectionCurveCount>0||tube.cylindricalFaceCount!=2||tube.cutScheme.Contains("COMPLEX"))
            return "SEE PART DRAWING — end/clocking details not verified";
        double a=tube.angleLeftDeg.Value,b=tube.angleRightDeg.Value;
        string left=Math.Abs(a)<1e-6?"│":"╱",right=Math.Abs(b)<1e-6?"│":tube.cutScheme=="OPPOSED_SLOPES"?"╲":"╱";
        return "A "+a.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+"° | B "+b.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+"°\n"+
            left+"━━━━━━━━"+right+"\nSchematic; axis-relative; not to scale";
    }
    public static object[] Items(object o){return o==null?new object[0]:((Array)o).Cast<object>().ToArray();}
    static double[] Numbers(object o){return Items(o).Select(Convert.ToDouble).ToArray();}
    static NV V(double[] p,int i){return new NV(p[i],p[i+1],p[i+2]);}
    public static double CutAngle(NV normal,NV axis){return Math.Acos(Math.Max(0,Math.Min(1,Math.Abs(NV.Dot(normal.Unit(),axis.Unit())))))*180/Math.PI;}
    public static bool HollowCoaxialPair(NV oa,NV na,double ra,int signA,NV ob,NV nb,double rb,int signB){
        if(signA!=1||signB!=-1||!NV.Finite(ra)||!NV.Finite(rb)||rb<=0||ra<=rb+1e-8)return false;
        na=na.Unit();nb=nb.Unit();var d=oa-ob;
        return Math.Min((na-nb).Norm(),(na+nb).Norm())<1e-8&&(d-na*NV.Dot(na,d)).Norm()<1e-7;
    }
    public static string Scheme(NV normalA,NV normalB,NV axis){
        axis=axis.Unit();var a=normalA*(1/NV.Dot(normalA,axis))-axis;var b=normalB*(1/NV.Dot(normalB,axis))-axis;
        if(a.Norm()<1e-10&&b.Norm()<1e-10)return "STRAIGHT | STRAIGHT";
        if(a.Norm()<1e-10||b.Norm()<1e-10)return "STRAIGHT | MITRE";
        double dot=NV.Dot(a.Unit(),b.Unit());return dot>=.98?"PARALLEL_SLOPES":dot<=-.98?"OPPOSED_SLOPES":"OUT_OF_PLANE / COMPLEX";
    }
    public static double AxisPlaneLengthMm(NV axisOrigin,NV axis,NV root1,NV normal1,NV root2,NV normal2){
        axis=axis.Unit();double d1=NV.Dot(normal1,axis),d2=NV.Dot(normal2,axis);
        if(Math.Abs(d1)<1e-10||Math.Abs(d2)<1e-10)throw new InvalidOperationException("End plane parallel to tube axis; centreline length undefined");
        return Math.Abs(NV.Dot(normal2,root2-axisOrigin)/d2-NV.Dot(normal1,root1-axisOrigin)/d1)*1000;
    }
    public static double Extreme(Body2 body,NV axis){
        double x,y,z;if(!body.GetExtremePoint(axis.X,axis.Y,axis.Z,out x,out y,out z))throw new InvalidOperationException("Body2.GetExtremePoint failed");
        return NV.Dot(new NV(x,y,z),axis);
    }
    public static double Span(Body2 body,NV axis){return Extreme(body,axis)+Extreme(body,axis*(-1));}
    class Cylinder {public NV O,N;public double R,Area;public int MaterialSign;}
    public static MPTube Tube(Body2 body){
        var result=new MPTube();
        try{
            var faces=Items(body.GetFaces()).Cast<Face2>().ToArray();var cylinders=new List<Cylinder>();
            foreach(var f in faces){
                var s=(Surface)f.GetSurface();if(!s.IsCylinder())continue;var p=Numbers(s.CylinderParams);var origin=V(p,0);var axis=V(p,3).Unit();
                var edges=Items(f.GetEdges()).Cast<Edge>().ToArray();if(edges.Length==0)throw new InvalidOperationException("Cylinder has no native boundary witness");
                edges[0].GetCurve();var point=NV.Read(edges[0].GetCurveParams3().StartPoint);var evaluation=Numbers(s.EvaluateAtPoint(point.X,point.Y,point.Z));
                if(evaluation.Length<3)throw new InvalidOperationException("Natural cylinder normal unavailable");
                var delta=point-origin;var radial=delta-axis*NV.Dot(axis,delta);double dot=NV.Dot(V(evaluation,0),radial);
                if(!NV.Finite(dot)||dot==0)throw new InvalidOperationException("Cylinder material orientation not established");
                cylinders.Add(new Cylinder{O=origin,N=axis,R=p[6],Area=f.GetArea(),MaterialSign=Math.Sign(dot)*(f.FaceInSurfaceSense()?-1:1)});
            }
            var pairs=new List<Tuple<Cylinder,Cylinder>>();
            foreach(var a in cylinders)foreach(var b in cylinders)if(HollowCoaxialPair(a.O,a.N,a.R,a.MaterialSign,b.O,b.N,b.R,b.MaterialSign))pairs.Add(Tuple.Create(a,b));
            Func<Cylinder,double> combinedArea=a=>cylinders.Where(c=>{
                var d=c.O-a.O;return c.MaterialSign==a.MaterialSign&&Math.Abs(c.R-a.R)<1e-8&&Math.Min((c.N-a.N).Norm(),(c.N+a.N).Norm())<1e-8&&(d-a.N*NV.Dot(a.N,d)).Norm()<1e-7;
            }).Sum(c=>c.Area);
            var pair=pairs.OrderByDescending(p=>combinedArea(p.Item1)+combinedArea(p.Item2)).FirstOrDefault();if(pair==null)return result;
            if((combinedArea(pair.Item1)+combinedArea(pair.Item2))/faces.Sum(f=>f.GetArea())<.7){result.warnings.Add("Coaxial cylinders do not dominate body surface area; round-tube main section not established");return result;}
            result.cylindricalFaceCount=cylinders.Count;
            result.intersectionCurveCount=Items(body.GetEdges()).Cast<Edge>().Count(e=>((Curve)e.GetCurve()).Identity()==3004);
            if(result.intersectionCurveCount>0)result.warnings.Add("Complex machining evidence: "+result.intersectionCurveCount+" native INTERSECTION_TYPE (3004) edges; full end/hole machining is not certified by the plane-angle cutting scheme.");
            var outer=pair.Item1;result.axis=outer.N;result.outerDiameterMm=outer.R*2000;result.wallMm=(outer.R-pair.Item2.R)*1000;
            result.maximumAxialSpanMm=Span(body,outer.N)*1000;result.status="COAXIAL_CYLINDRICAL_SURFACES_MEASURED";
            double min=-Extreme(body,outer.N*(-1)),max=Extreme(body,outer.N),length=max-min;
            // Adapted macro GetEndPlaneNormals: largest planar candidate in each
            // end zone; box centre used only for heuristic face selection, never length.
            var left=new List<Tuple<NV,double,NV>>();var right=new List<Tuple<NV,double,NV>>();
            foreach(var f in faces){
                var s=(Surface)f.GetSurface();if(!s.IsPlane())continue;var p=Numbers(s.PlaneParams);var normal=V(p,0).Unit();
                if(Math.Abs(NV.Dot(normal,outer.N))<=.15)continue;
                var box=Numbers(f.GetBox());var center=new NV((box[0]+box[3])/2,(box[1]+box[4])/2,(box[2]+box[5])/2);double pos=NV.Dot(center,outer.N);
                var item=Tuple.Create(normal,f.GetArea(),V(p,3));if(pos<=min+length*.35)left.Add(item);if(pos>=max-length*.35)right.Add(item);
            }
            var l=left.OrderByDescending(p=>p.Item2).FirstOrDefault();var r=right.OrderByDescending(p=>p.Item2).FirstOrDefault();
            if(l!=null)result.angleLeftDeg=CutAngle(l.Item1,outer.N);if(r!=null)result.angleRightDeg=CutAngle(r.Item1,outer.N);
            result.warnings.Add("End-plane selection follows reviewed VBA end-zone/area heuristic; complex end machining requires the part drawing. Angles are diagnostic, cached ANGLE values remain separate.");
            if(l!=null&&r!=null){
                result.axisPlaneIntersectionLengthMm=AxisPlaneLengthMm(outer.O,outer.N,l.Item3,l.Item1,r.Item3,r.Item1);
                result.cutScheme=Scheme(l.Item1,r.Item1,outer.N);
            }
            result.warnings.Add("H is not required for round tubes. Maximum axial span is measured stock-envelope length, not an assumed centreline cut length.");
        }catch(Exception ex){result.status="NOT_VERIFIED";result.warnings.Add(ex.GetType().FullName+": "+ex.Message);}
        return result;
    }
    public static MPPlate Plate(object raw){
        var result=new MPPlate();
        try{
            var b=GBrep06.Parse(raw);if(!b.Accepted)throw new InvalidOperationException("Plate bounded BREP incomplete/unsupported: "+String.Join(" | ",b.Unsupported.Concat(b.Errors)));
            var planes=b.Faces.Where(f=>f.S.Type=="PLANE"&&f.Loops.Any(l=>l.Outer&&l.Co.Count>=3&&l.Co.All(c=>c.E.Line))).ToArray();
            GF06 front=null,back=null;NV normal=null;double thickness=0;
            foreach(var f in planes){
                var p=b.Faces.Where(g=>g!=f&&g.S.Type=="PLANE"&&Math.Abs(NV.Dot(g.S.N,f.S.N))>1-1e-8).ToArray();
                foreach(var g in p){double h=NV.Dot(f.S.N,g.S.O-f.S.O);
                    if(Math.Abs(h)>1e-8&&f.Loops.Count==g.Loops.Count&&f.Loops.Count>1){
                        if(front==null||Math.Abs(h)<Math.Abs(thickness)){front=f;back=g;normal=f.S.N;thickness=h;}
                    }}
            }
            if(front==null)throw new InvalidOperationException("Two bounded parallel plate faces with holes not established");
            foreach(var f in b.Faces)if(f!=front&&f!=back){
                if(f.S.Type=="PLANE"&&Math.Abs(NV.Dot(normal,f.S.N))>1e-8)throw new InvalidOperationException("Additional axial planar machining: not a uniform plate");
                if(f.S.Type=="CYLINDER"&&Math.Abs(NV.Dot(normal,f.S.N))<1-1e-8)throw new InvalidOperationException("Non-through cylindrical machining");
            }
            var shift=normal*thickness;var t=new NTransform(new[]{new[]{1d,0,0,shift.X},new[]{0d,1,0,shift.Y},new[]{0d,0,1,shift.Z},new[]{0d,0,0,1}});
            foreach(var l in front.Loops){
                var match=back.Loops.Where(g=>g.Outer==l.Outer&&g.Co.Count==l.Co.Count&&l.Co.All(c=>g.Co.Any(d=>NB03.Passed(GBrep06.EdgeMatch(c.E.Move(t),d.E))))).ToArray();
                if(match.Length!=1)throw new InvalidOperationException("Front/back bounded loops do not coincide through plate thickness");
            }
            var outer=front.Loops.Single(l=>l.Outer);var x=(outer.Co[0].Q-outer.Co[0].P).Unit();var y=NV.Cross(normal,x).Unit();var origin=outer.Co[0].P;
            result.projection=new{nativeFaceId=front.S.Id,oppositeFaceId=back.S.Id,originM=origin.Data(),normal=normal.Data(),xDirection=x.Data(),yDirection=y.Data(),scaleMetreToMillimetre=1000};
            Func<NV,MPPoint> point=p=>new MPPoint(NV.Dot(p-origin,x)*1000,NV.Dot(p-origin,y)*1000);
            var entities=new System.Text.StringBuilder();
            foreach(var loop in front.Loops)foreach(var co in loop.Co){
                var e=co.E;if(e.Line)entities.Append(ManufacturingDxfV01.Line(point(e.P),point(e.Q)));
                else if(e.Full&&Math.Abs(e.Conic.A.Norm()-e.Conic.B.Norm())<1e-10)entities.Append(ManufacturingDxfV01.Circle(point(e.C),e.Conic.A.Norm()*1000));
                else throw new InvalidOperationException("Plate contains unsupported exact DXF conic; no fitted contour created");
            }
            var check=ManufacturingDxfV01.Check(ManufacturingDxfV01.Wrap(entities.ToString()));if(!check.accepted)throw new InvalidOperationException(check.reason);
            result.accepted=true;result.text=check.normalizedText;result.thicknessMm=Math.Abs(thickness)*1000;result.extentsMm=check.extentsMm;
            result.reason="Exact projection of native bounded face loops; opposite face correspondence and uniform extrusion checked. No bounding-box DXF.";
        }catch(Exception ex){result.reason=ex.Message;}
        return result;
    }
}
