using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;

public class SectionComparison {
    public string STRUCTURAL_MEMBER,PROFILE_TYPE,PROFILE_PATH,PROFILE_CONFIGURATION,NOMINAL_SOURCE;
    public double? NOMINAL_WIDTH,NOMINAL_HEIGHT,NOMINAL_WALL_THICKNESS,BOUNDING_BOX_WIDTH,BOUNDING_BOX_HEIGHT;
    public double? PLANAR_WALL_SPACING_U,PLANAR_WALL_SPACING_V,H;
    public string DIR1,DIR2,CROSS_SECTION_VERIFICATION_STATUS="NEEDS_REVIEW";
    public bool bodyPathAssociationVerified,orientedFrameVerified,permutationAllowed=true,sectionChecksPassed;
    public object frame,wallEvidence,endCutDiagnostics;
    public List<string> warnings=new List<string>();
}
public struct SectionVector {
    public double X,Y,Z;public SectionVector(double x,double y,double z){X=x;Y=y;Z=z;}
    public double Norm(){return Math.Sqrt(X*X+Y*Y+Z*Z);}public SectionVector Unit(){double n=Norm();if(n<1e-12)throw new Exception("Zero direction");return this*(1/n);}
    public static SectionVector operator +(SectionVector a,SectionVector b){return new SectionVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
    public static SectionVector operator -(SectionVector a,SectionVector b){return new SectionVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
    public static SectionVector operator *(SectionVector a,double s){return new SectionVector(a.X*s,a.Y*s,a.Z*s);}
    public static double Dot(SectionVector a,SectionVector b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
    public static SectionVector Cross(SectionVector a,SectionVector b){return new SectionVector(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);}
    public double[] Data(){return new[]{X,Y,Z};}
}
public static class NativeSectionCheck {
    static object[] A(object o){return NativeWeldmentAdapter.Items(o);}static double[] N(object o){return A(o).Select(Convert.ToDouble).ToArray();}
    static SectionVector V(double[] a,int i){return new SectionVector(a[i],a[i+1],a[i+2]);}
    public static double[] ParseSize(string size){
        var m=Regex.Match(size??"",@"^\s*(\d+(?:[.,]\d+)?)\s*[xXхХ×]\s*(\d+(?:[.,]\d+)?)\s*[xXхХ×]\s*(\d+(?:[.,]\d+)?)\s*$");
        if(!m.Success)return null;var v=Enumerable.Range(1,3).Select(i=>Double.Parse(m.Groups[i].Value.Replace(',','.'),CultureInfo.InvariantCulture)).ToArray();
        return v.Any(x=>Double.IsNaN(x)||Double.IsInfinity(x)||x<=0)||2*v[2]>=Math.Min(v[0],v[1])?null:v;
    }
    public static bool ExtentsMatch(double w,double h,double nw,double nh,double tolerance){
        if(new[]{w,h,nw,nh,tolerance}.Any(x=>Double.IsNaN(x)||Double.IsInfinity(x)||x<=0))return false;
        return Math.Abs(w-nw)<=tolerance&&Math.Abs(h-nh)<=tolerance||Math.Abs(w-nh)<=tolerance&&Math.Abs(h-nw)<=tolerance;
    }
    static SectionVector ModelPoint(SldWorks sw,SketchSegment segment,SketchPoint point){
        var p=(MathPoint)((MathUtility)sw.GetMathUtility()).CreatePoint(new[]{point.X,point.Y,point.Z});
        var sketch=(Sketch)segment.GetSketch();if(sketch==null||sketch.ModelToSketchTransform==null)throw new Exception("Native path sketch transform unavailable");
        p=(MathPoint)p.MultiplyTransform(sketch.ModelToSketchTransform.Inverse());return V(N(p.ArrayData),0);
    }
    static double Extreme(Body2 b,SectionVector v){double x,y,z;if(!b.GetExtremePoint(v.X,v.Y,v.Z,out x,out y,out z))throw new Exception("Body2.GetExtremePoint returned false");return SectionVector.Dot(v,new SectionVector(x,y,z));}
    static double Span(Body2 b,SectionVector v){return (Extreme(b,v)+Extreme(b,v*(-1)))*1000;}
    class Plane {public SectionVector Normal,Root;public Face2 Face;public double Area;}
    static double? WallSpacing(IEnumerable<Plane> planes,SectionVector n,out double[] stations){
        var offsets=planes.Where(p=>Math.Abs(SectionVector.Dot(p.Normal,n))>1-1e-8).Select(p=>SectionVector.Dot(p.Root,n)*1000).OrderBy(x=>x).ToArray();var distinct=new List<double>();
        foreach(double d in offsets)if(distinct.Count==0||Math.Abs(d-distinct.Last())>1e-6)distinct.Add(d);
        stations=distinct.ToArray();if(stations.Length!=4)return null;
        double a=stations[1]-stations[0],b=stations[3]-stations[2];return Math.Abs(a-b)<=.001?(double?)((a+b)/2):null;
    }
    public static SectionComparison Read(SldWorks sw,Body2 body,Feature[] related,double tolerance=.001){
        var r=new SectionComparison();
        try{
            var features=related.Where(f=>f.GetTypeName2()=="WeldMemberFeat").ToArray();if(features.Length!=1)throw new Exception("Unique native Structural Member association unavailable");
            var feature=features[0];r.STRUCTURAL_MEMBER=feature.Name;var member=(StructuralMemberFeatureData)feature.GetDefinition();
            r.PROFILE_PATH=member.WeldmentProfilePath;r.PROFILE_CONFIGURATION=member.ConfigurationName;
            if(String.IsNullOrWhiteSpace(r.PROFILE_PATH))throw new Exception("Native WeldmentProfilePath empty");
            string designation=String.IsNullOrWhiteSpace(r.PROFILE_CONFIGURATION)?Path.GetFileNameWithoutExtension(r.PROFILE_PATH):r.PROFILE_CONFIGURATION;
            r.PROFILE_TYPE=String.IsNullOrWhiteSpace(r.PROFILE_CONFIGURATION)?Path.GetFileName(Path.GetDirectoryName(r.PROFILE_PATH)):Path.GetFileNameWithoutExtension(r.PROFILE_PATH);
            r.NOMINAL_SOURCE="Assigned native WeldmentProfilePath + ConfigurationName designation, never feature display name/material/Description; library type label is retained, not treated as a geometric certificate";
            var size=ParseSize(designation);if(size!=null){r.NOMINAL_WIDTH=size[0];r.NOMINAL_HEIGHT=size[1];r.NOMINAL_WALL_THICKNESS=size[2];}else r.warnings.Add("Assigned profile designation not an unambiguous rectangular WxHxT; nominal dimensions remain unverified");
            var segments=A(member.GetSketchSegments(body)).Cast<SketchSegment>().ToArray();if(segments.Length==0)throw new Exception("Native GetSketchSegments(body) supplied no body-specific path");
            var directions=new List<SectionVector>();
            foreach(var s in segments){if(s.GetType()!=0)throw new Exception("Nonlinear structural path requires separate section handling");var line=(SketchLine)s;directions.Add((ModelPoint(sw,s,(SketchPoint)line.GetEndPoint2())-ModelPoint(sw,s,(SketchPoint)line.GetStartPoint2())).Unit());}
            var axis=directions[0];if(directions.Any(d=>Math.Abs(SectionVector.Dot(axis,d))<1-1e-8))throw new Exception("Body path segments are not collinear");r.bodyPathAssociationVerified=true;
            // Cross-check the transformed native path against real model-space
            // straight edges. No global XYZ box is used as a section.
            bool witness=A(body.GetEdges()).Cast<Edge>().Any(e=>{var c=(Curve)e.GetCurve();return c.IsLine()&&Math.Abs(SectionVector.Dot(V(N(c.LineParams),3).Unit(),axis))>1-1e-8;});
            if(!witness)throw new Exception("Native path/model coordinate-frame correspondence not confirmed by a straight body edge");
            var planes=new List<Plane>();foreach(var face in A(body.GetFaces()).Cast<Face2>()){var surface=(Surface)face.GetSurface();if(surface.IsPlane()){var p=N(surface.PlaneParams);planes.Add(new Plane{Face=face,Normal=V(p,0).Unit(),Root=V(p,3),Area=face.GetArea()});}}
            // Profiles+h GetCrossSectionAxes: dominant planar side normals.
            // Tightened acceptance is deliberate: an uncertain frame is REVIEW.
            var sides=planes.Where(p=>Math.Abs(SectionVector.Dot(p.Normal,axis))<.12).ToArray();
            var pairs=from a in sides from b in sides where Math.Abs(SectionVector.Dot(a.Normal,b.Normal))<.3 select new{A=a,B=b,Score=a.Area+b.Area};var best=pairs.OrderByDescending(p=>p.Score).FirstOrDefault();
            if(best==null)throw new Exception("Two native transverse side-face directions unavailable");
            if(Math.Abs(SectionVector.Dot(best.A.Normal,axis))>1e-8||Math.Abs(SectionVector.Dot(best.B.Normal,axis))>1e-8||Math.Abs(SectionVector.Dot(best.A.Normal,best.B.Normal))>1e-8)throw new Exception("Dominant side planes do not establish an orthogonal profile frame");
            var u=best.A.Normal;var v=SectionVector.Cross(axis,u).Unit();r.orientedFrameVerified=true;r.frame=new{axis=axis.Data(),widthDirection=u.Data(),heightDirection=v.Data(),origin="Model coordinate system; translation invariant extent differences",method="Native body path + Profiles+h side-plane axes + GetExtremePoint"};
            r.BOUNDING_BOX_WIDTH=Span(body,u);r.BOUNDING_BOX_HEIGHT=Span(body,v);
            double[] su,sv;r.PLANAR_WALL_SPACING_U=WallSpacing(planes,u,out su);r.PLANAR_WALL_SPACING_V=WallSpacing(planes,v,out sv);r.wallEvidence=new{uPlaneStationsMm=su,vPlaneStationsMm=sv,method="Native bounded planar longitudinal faces; four distinct stations per direction required",fullBoundedCrossSectionCertified=false};
            bool bounds=size!=null&&ExtentsMatch(r.BOUNDING_BOX_WIDTH.Value,r.BOUNDING_BOX_HEIGHT.Value,size[0],size[1],tolerance);
            bool walls=size!=null&&r.PLANAR_WALL_SPACING_U.HasValue&&r.PLANAR_WALL_SPACING_V.HasValue&&Math.Abs(r.PLANAR_WALL_SPACING_U.Value-size[2])<=tolerance&&Math.Abs(r.PLANAR_WALL_SPACING_V.Value-size[2])<=tolerance;
            if(!bounds)r.warnings.Add("Oriented transverse extrema not confirmed against nominal sizes. Cuts/protrusions may affect whole-body bounds; this is not proof of wrong section.");
            if(!walls)r.warnings.Add("Wall spacing not confirmed; holes, reliefs, nonplanar walls or complex section need a bounded cross-section check.");
            r.sectionChecksPassed=bounds&&walls;
            r.CROSS_SECTION_VERIFICATION_STATUS=r.sectionChecksPassed?"EXTENTS_AND_PLANAR_WALL_SPACINGS_MATCH_BOUNDED_SECTION_REVIEW_PENDING":"NEEDS_REVIEW";
            r.warnings.Add("Full bounded cross-section, per-body H, DIR1/DIR2 and additional processing remain unverified; no production grouping permitted.");
        }catch(Exception ex){r.warnings.Add(ex.GetType().FullName+": "+ex.Message+"; HRESULT=0x"+ex.HResult.ToString("X8"));}
        return r;
    }
    public static int Tests(){int n=0;Action<bool> c=x=>{if(!x)throw new Exception("Native section regression "+n);n++;};
        c(ParseSize("20x40x1,5").SequenceEqual(new[]{20d,40,1.5}));c(ParseSize("40×20×1.5")[2]==1.5);c(ParseSize("Rectangular pipe 20x40x1,5")==null);c(ParseSize("20x40")==null);c(ParseSize("20x40x0")==null);c(ParseSize("20x40x15")==null);
        c(ExtentsMatch(40,20,20,40,.001));c(!ExtentsMatch(40,25,22,40,.001));c(!ExtentsMatch(100,40,20,40,.001));c(!ExtentsMatch(Double.NaN,40,20,40,.001));c(ExtentsMatch(20.0005,40,20,40,.001));
        Console.WriteLine("Native section policy: "+n+"/"+n+" PASS; no COM");return 0;
    }
}
