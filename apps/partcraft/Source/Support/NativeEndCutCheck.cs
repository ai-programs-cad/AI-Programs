using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

// Read-only end evidence. Profiles+h supplies H rules, not a geometric DIR convention.
public class EndCutResult {
    public double? H;
    public string HStatus="NEEDS_REVIEW",DIR1,DIR2,directionStatus="NEEDS_REVIEW",angleEndMapping="NEEDS_REVIEW",machiningStatus="NEEDS_REVIEW",status="NEEDS_REVIEW";
    public bool additionalProcessingEvidenceFound,additionalMachiningCertifiedAbsent;
    public object geometry;
    public string exception,hresult;
    public List<string> issues=new List<string>();
}
public static class NativeEndCutCheck {
    static double D(SectionVector a,SectionVector b){return SectionVector.Dot(a,b);}
    static double[] N(object o){return NativeWeldmentAdapter.Items(o).Select(Convert.ToDouble).ToArray();}
    static SectionVector V(double[] a,int i){return new SectionVector(a[i],a[i+1],a[i+2]);}
    static double Ext(Body2 b,SectionVector a){double x,y,z;if(!b.GetExtremePoint(a.X,a.Y,a.Z,out x,out y,out z))throw new Exception("GetExtremePoint false");return D(a,new SectionVector(x,y,z));}
    public static double? Height(double cutSide,double width,double height){if(Math.Abs(cutSide-width)<.001)return height;if(Math.Abs(cutSide-height)<.001)return width;return null;}
    public static double? CutSide(SectionVector normal,SectionVector axis,SectionVector u,SectionVector v,double w,double h){var t=normal-axis*D(normal,axis);if(t.Norm()<.01)return 0; t=t.Unit();if(Math.Abs(D(t,u))>=.98)return w;if(Math.Abs(D(t,v))>=.98)return h;return null;}
    class End {public int faceIndex;public double area,station,angle;public SectionVector normal;public double? side;public int innerLoops;}
    public static string MatchAngles(double first,double last,double? a,double? b) {
        bool direct=a.HasValue&&b.HasValue&&Math.Abs(first-a.Value)<=.05&&Math.Abs(last-b.Value)<=.05;
        bool reverse=a.HasValue&&b.HasValue&&Math.Abs(first-b.Value)<=.05&&Math.Abs(last-a.Value)<=.05;
        return direct&&!reverse?"ANGLE1=LOCAL_MIN;ANGLE2=LOCAL_MAX":reverse&&!direct?"ANGLE1=LOCAL_MAX;ANGLE2=LOCAL_MIN":"NEEDS_REVIEW";
    }
    public static string SlopeDirection(SectionVector n,SectionVector axis,SectionVector u,SectionVector v) {
        double a=D(n,axis);if(Math.Abs(a)<1e-8)return null;
        double su=-D(n,u)/a,sv=-D(n,v)/a;
        if(Math.Abs(su)<1e-8&&Math.Abs(sv)<1e-8)return "STRAIGHT";
        if(Math.Abs(sv)<1e-8)return su>0?"LOCAL_U_POSITIVE":"LOCAL_U_NEGATIVE";
        if(Math.Abs(su)<1e-8)return sv>0?"LOCAL_V_POSITIVE":"LOCAL_V_NEGATIVE";
        return null;
    }
    public static EndCutResult Read(SldWorks sw,Body2 body,SectionComparison section,AuthoritativeFolderProfile properties){
        var issues=new List<string>();var evidence=new List<object>();
        try {
            if(section==null||!section.orientedFrameVerified)throw new Exception("Verified local frame unavailable");
            // Reflection reads our own anonymous record; no unsafe dictionary cast.
            var f=section.frame;Func<string,double[]> get=k=>f is Dictionary<string,object>?N(((Dictionary<string,object>)f)[k]):(double[])f.GetType().GetProperty(k).GetValue(f,null);
            var axis=V(get("axis"),0);var u=V(get("widthDirection"),0);var v=V(get("heightDirection"),0);
            double lo=-Ext(body,axis*(-1)),hi=Ext(body,axis),span=hi-lo;
            var ends=new List<End>();int index=0;int sideInnerLoops=0;int unclassifiedSurfaces=0;
            foreach(Face2 face in NativeWeldmentAdapter.Items(body.GetFaces())) {
                ManufacturingAgent.Boundary("READ_END_FACE");index++;if(sw.IsSame(face.GetBody(),body)!=1)throw new Exception("FACE_OWNER_MISMATCH at face "+index);var surface=(Surface)face.GetSurface();int inner=0;
                foreach(Loop2 loop in NativeWeldmentAdapter.Items(face.GetLoops()))if(!loop.IsOuter())inner++;
                if(!surface.IsPlane()){unclassifiedSurfaces++;evidence.Add(new{faceIndex=index,surfaceIdentity=surface.Identity(),innerLoops=inner,cylinderParameters=surface.IsCylinder()?N(surface.CylinderParams):null,classification="NONPLANAR_REQUIRES_REVIEW_NOT_AUTOMATICALLY_DRILLING"});continue;}
                var p=N(surface.PlaneParams);var normal=V(p,0).Unit();double axial=Math.Abs(D(normal,axis));
                if(axial<1e-8){sideInnerLoops+=inner;evidence.Add(new{faceIndex=index,normal=normal.Data(),root=V(p,3).Data(),innerLoops=inner,classification=inner>0?"LONGITUDINAL_WALL_WITH_INNER_LOOPS":"LONGITUDINAL_WALL",edgeCount=NativeWeldmentAdapter.Items(face.GetEdges()).Length});continue;}
                if(axial<=.15){issues.Add("Face "+index+": intermediate normal, neither longitudinal wall nor supported end");continue;}
                var box=N(face.GetBox());double station=D((V(box,0)+V(box,3))*.5,axis);
                ends.Add(new End{faceIndex=index,area=face.GetArea(),station=station,normal=normal,angle=Math.Acos(Math.Min(1,axial))*180/Math.PI,side=CutSide(normal,axis,u,v,section.BOUNDING_BOX_WIDTH.Value,section.BOUNDING_BOX_HEIGHT.Value),innerLoops=inner});
            }
            var first=ends.Where(e=>e.station<=lo+span*.35).OrderByDescending(e=>e.area).FirstOrDefault();
            var last=ends.Where(e=>e.station>=hi-span*.35).OrderByDescending(e=>e.area).FirstOrDefault();
            if(first==null||last==null||first==last)throw new Exception("Two separate end planes unavailable under Profiles+h end-zone rule");
            double? side=null;if(first.side.HasValue&&last.side.HasValue){if(first.side==0)side=last.side;else if(last.side==0)side=first.side;else if(Math.Abs(first.side.Value-last.side.Value)<=.75)side=(first.side+last.side)/2;}
            double? h=side.HasValue&&side.Value>0?Height(side.Value,section.NOMINAL_WIDTH.Value,section.NOMINAL_HEIGHT.Value):null;
            if(!h.HasValue)issues.Add("H: incompatible/compound cuts or both ends straight; no arbitrary lying orientation selected");
            double? a=properties==null?null:properties.angle1Degrees,b=properties==null?null:properties.angle2Degrees;
            string mapping=MatchAngles(first.angle,last.angle,a,b);
            if(mapping=="NEEDS_REVIEW")issues.Add("Cut List angle/end association absent, ambiguous, or outside 0.05 degree comparison tolerance");
            foreach(var e in ends){double na=D(e.normal,axis);evidence.Add(new{e.faceIndex,e.station,e.area,normal=e.normal.Data(),geometricCutAngleDegrees=e.angle,cutSideMm=e.side,e.innerLoops,localPlaneSlopeU=-D(e.normal,u)/na,localPlaneSlopeV=-D(e.normal,v)/na,selectedEnd=e==first?"LOCAL_MIN":e==last?"LOCAL_MAX":"ADDITIONAL_END_OR_PROCESSING_FACE"});}
            string dFirst=SlopeDirection(first.normal,axis,u,v),dLast=SlopeDirection(last.normal,axis,u,v);
            string d1=mapping.StartsWith("ANGLE1=LOCAL_MIN;")?dFirst:mapping.StartsWith("ANGLE1=LOCAL_MAX;")?dLast:null;
            string d2=mapping.StartsWith("ANGLE1=LOCAL_MIN;")?dLast:mapping.StartsWith("ANGLE1=LOCAL_MAX;")?dFirst:null;
            issues.Add("DIR values are geometric local plane-slope signs dt/du or dt/dv, invariant to normal reversal; they are not unverified operator table labels from Profiles+h");
            bool extra=sideInnerLoops>0||ends.Count>2;
            return new EndCutResult{H=h,HStatus=h.HasValue?"PROFILES_H_END_NORMAL_RULE_MATCH":"NEEDS_REVIEW",DIR1=d1,DIR2=d2,directionStatus=d1!=null&&d2!=null?"GEOMETRIC_LOCAL_SLOPES_MAPPED_TO_CUT_LIST_ANGLES":"NEEDS_REVIEW",angleEndMapping=mapping,geometry=new{axis=axis.Data(),u=u.Data(),v=v.Data(),faces=evidence,sideInnerLoops,nonplanarFaceCount=unclassifiedSurfaces},additionalProcessingEvidenceFound=extra,additionalMachiningCertifiedAbsent=false,machiningStatus=extra?"ADDITIONAL_PROCESSING_EVIDENCE_REQUIRES_CLASSIFICATION":"NEEDS_REVIEW: bounded wall/corner topology not certified; ordinary end annulus not counted as drilling",issues=issues};
        }catch(OperationCanceledException){throw;}catch(Exception ex){return new EndCutResult{exception=ex.ToString(),hresult="0x"+ex.HResult.ToString("X8"),issues=issues};}
    }
    public static int Tests(){int n=0;Action<bool> c=x=>{if(!x)throw new Exception("End cut regression "+n);n++;};var a=new SectionVector(1,0,0);var u=new SectionVector(0,1,0);var v=new SectionVector(0,0,1);
        c(Height(40,20,40)==20);c(Height(20,20,40)==40);c(!Height(30,20,40).HasValue);
        c(CutSide(new SectionVector(1,1,0).Unit(),a,u,v,40,20)==40);c(CutSide(new SectionVector(1,0,1).Unit(),a,u,v,40,20)==20);c(CutSide(a,a,u,v,40,20)==0);c(!CutSide(new SectionVector(1,1,1).Unit(),a,u,v,40,20).HasValue);
        c(MatchAngles(41,45,41,45)=="ANGLE1=LOCAL_MIN;ANGLE2=LOCAL_MAX");c(MatchAngles(45,41,41,45)=="ANGLE1=LOCAL_MAX;ANGLE2=LOCAL_MIN");c(MatchAngles(45,45,45,45)=="NEEDS_REVIEW");c(MatchAngles(0,45,null,45)=="NEEDS_REVIEW");c(MatchAngles(5,45,0,45)=="NEEDS_REVIEW");
        c(SlopeDirection(new SectionVector(1,1,0),a,u,v)=="LOCAL_U_NEGATIVE");c(SlopeDirection(new SectionVector(-1,-1,0),a,u,v)=="LOCAL_U_NEGATIVE");c(SlopeDirection(new SectionVector(1,-1,0),a,u,v)=="LOCAL_U_POSITIVE");c(SlopeDirection(a,a,u,v)=="STRAIGHT");c(SlopeDirection(new SectionVector(1,1,1),a,u,v)==null);
        Console.WriteLine("End cut rules: "+n+"/"+n+" PASS; no COM");return 0;}
}
