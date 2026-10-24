using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Read-only evidence collector, NOT a certified BREP equivalence verifier.
// No activation, selection, copying, transformation or creation of bodies.
class ManufacturingBrepV01 {
    const string ExpectedPath = @"C:\AIPrograms-TestData\sample.SLDPRT";
    const string ExpectedConfiguration = "По умолчанию<As Machined>";
    static readonly List<object> Errors = new List<object>();
    static readonly List<object> Gaps = new List<object>();
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer {MaxJsonLength=int.MaxValue,RecursionLimit=200};
    static SldWorks Sw;
    static object Read(string api,Func<object> call) {
        try { return call(); }
        catch(Exception ex) {
            if(ex is TargetInvocationException && ex.InnerException!=null) ex=ex.InnerException;
            Errors.Add(new {api,exception=ex.GetType().FullName,hresult="0x"+ex.HResult.ToString("X8"),message=ex.Message});
            return null;
        }
    }
    static void Gap(string entity,string reason) { Gaps.Add(new {entity,reason}); }
    static object[] Items(object value) { return value==null?new object[0]:((Array)value).Cast<object>().ToArray(); }
    static double[] Numbers(object value) { return value==null?null:((Array)value).Cast<object>().Select(Convert.ToDouble).ToArray(); }
    static bool Same(object a,object b) {
        return a!=null && b!=null && Sw.IsSame(a,b)==(int)swObjectEquality.swObjectSame;
    }
    static int Id(object value,object[] table) {
        if(value==null) return -1;
        for(int i=0;i<table.Length;i++) if(Same(value,table[i])) return i;
        return -1;
    }
    static object VertexPoint(object value) { return value==null?null:Numbers(((Vertex)value).GetPoint()); }
    static object CurveData(Curve curve,string label) {
        if(curve==null) { Gap(label,"GetCurve returned null"); return null; }
        var r=new Dictionary<string,object>();
        r["identity"]=Read(label+".Identity",()=>curve.Identity());
        var line=Read(label+".IsLine",()=>curve.IsLine());
        var circle=Read(label+".IsCircle",()=>curve.IsCircle());
        var ellipse=Read(label+".IsEllipse",()=>curve.IsEllipse());
        r["isLine"]=line; r["isCircle"]=circle; r["isEllipse"]=ellipse;
        if(line is bool && (bool)line) r["lineParams"]=Read(label+".LineParams",()=>Numbers(curve.LineParams));
        else if(circle is bool && (bool)circle) r["circleParams"]=Read(label+".CircleParams",()=>Numbers(curve.CircleParams));
        else if(ellipse is bool && (bool)ellipse) r["ellipseParams"]=Read(label+".GetEllipseParams",()=>Numbers(curve.GetEllipseParams()));
        else { r["parametersComplete"]=false; Gap(label,"Curve representation unsupported in this first analytic reader; no approximation substituted"); }
        // Evidence-only read of an intersection/native B-curve representation.
        // It is NEVER treated as an exact analytic replacement without a certified error enclosure.
        r["isBcurve"]=Read(label+".IsBcurve",()=>curve.IsBcurve());
        if(Convert.ToInt32(r["identity"])==3004 || (r["isBcurve"] is bool && (bool)r["isBcurve"])) {
            r["additionalRepresentation"]=Read(label+".GetBCurveParams5",()=> {
                double u0,u1;bool closed,periodic;
                if(!curve.GetEndParams(out u0,out u1,out closed,out periodic))throw new InvalidOperationException("GetEndParams did not provide native closed status for additional representation");
                SplineParamData data=curve.GetBCurveParams5(false,false,false,closed);
                if(data==null)throw new InvalidOperationException("GetBCurveParams5 returned null");
                object controls,knots,segments;
                bool cp=data.GetControlPoints(out controls),kp=data.GetKnotPoints(out knots),sp=data.GetSegments(out segments);
                return new{representation="GetBCurveParams5 evidence only",data.Dimension,data.Order,data.Periodic,data.ControlPointsCount,data.KnotPointsCount,
                    controlsReturned=cp,controlPointsRaw=controls,knotsReturned=kp,knotPointsRaw=knots,segmentsReturned=sp,segmentsRaw=segments,
                    requestFlags=new{WantCubicIn=false,WantNRational=false,ForceNonPeriodic=false,IsClosed=closed},
                    exactNativeIdentityCertified=false,representationErrorBoundM=(object)null};
            });
        }
        // No synthetic domain is inferred for closed curves or edges without vertices.
        r["underlyingDomain"]=Read(label+".GetEndParams",()=> {
            double u0=0,u1=0; bool closed=false,periodic=false;
            bool ok=curve.GetEndParams(out u0,out u1,out closed,out periodic);
            return new {returned=ok,start=u0,end=u1,closed,periodic};
        });
        return r;
    }
    static object SurfaceData(Surface s,string label) {
        if(s==null) { Gap(label,"GetSurface returned null"); return null; }
        var r=new Dictionary<string,object>();
        r["identity"]=Read(label+".Identity",()=>s.Identity());
        bool plane=s.IsPlane(),cylinder=s.IsCylinder();
        r["type"]=plane?"PLANE":cylinder?"CYLINDER":"UNSUPPORTED";
        if(plane) {
            var p=(double[])Read(label+".PlaneParams",()=>Numbers(s.PlaneParams)); r["rawParameters"]=p;
            if(p!=null && p.Length==6) {r["normal"]=p.Take(3).ToArray();r["rootPointM"]=p.Skip(3).ToArray();}
            else Gap(label,"PlaneParams missing or not length 6");
        } else if(cylinder) {
            var p=(double[])Read(label+".CylinderParams",()=>Numbers(s.CylinderParams)); r["rawParameters"]=p;
            if(p!=null && p.Length==7) {r["originM"]=p.Take(3).ToArray();r["axis"]=p.Skip(3).Take(3).ToArray();r["radiusM"]=p[6];}
            else Gap(label,"CylinderParams missing or not length 7");
        } else Gap(label,"Surface representation unsupported; no tessellation or fitted surface substituted");
        return r;
    }
    static object BodyData(Body2 body) {
        string name=body.Name;
        var r=new Dictionary<string,object>(); r["name"]=name;
        r["faceCount"]=Read(name+".GetFaceCount",()=>body.GetFaceCount());
        r["edgeCount"]=Read(name+".GetEdgeCount",()=>body.GetEdgeCount());
        r["vertexCount"]=Read(name+".GetVertexCount",()=>body.GetVertexCount());
        var facesRaw=Read(name+".GetFaces",()=>body.GetFaces());
        var edgesRaw=Read(name+".GetEdges",()=>body.GetEdges());
        var verticesRaw=Read(name+".GetVertices",()=>body.GetVertices());
        object[] faces=Items(facesRaw),edges=Items(edgesRaw),vertices=Items(verticesRaw);
        r["enumeratedCounts"]=new {faces=faces.Length,edges=edges.Length,vertices=vertices.Length};
        var er=new List<object>(); r["edges"]=er;
        for(int i=0;i<edges.Length;i++) {
            Edge edge=(Edge)edges[i]; string label=name+".edge["+i+"]";
            var e=new Dictionary<string,object>(); er.Add(e);e["id"]=i;
            var edgeOwner=Read(label+".GetBody/IsSame",()=>Same(body,edge.GetBody()));
            e["ownerVerified"]=edgeOwner;
            if(!(edgeOwner is bool) || !(bool)edgeOwner) Gap(label,"Edge owner not verified");
            var curve=(Curve)Read(label+".GetCurve",()=>edge.GetCurve());
            e["curve"]=Read(label+".curveData",()=>CurveData(curve,label+".curve"));
            e["bounds"]=Read(label+".GetCurveParams3",()=> {
                if(curve==null) throw new InvalidOperationException("GetCurve prerequisite failed");
                CurveParamData p=edge.GetCurveParams3();
                if(p==null) throw new InvalidOperationException("GetCurveParams3 returned null");
                return new {startPointM=Numbers(p.StartPoint),endPointM=Numbers(p.EndPoint),uMin=p.UMinValue,uMax=p.UMaxValue,sense=p.Sense,curveType=p.CurveType,curveTag=p.CurveTag};
            });
            e["startVertexM"]=Read(label+".GetStartVertex/GetPoint",()=>VertexPoint(edge.GetStartVertex()));
            e["endVertexM"]=Read(label+".GetEndVertex/GetPoint",()=>VertexPoint(edge.GetEndVertex()));
            e["adjacentFaceIds"]=Read(label+".GetTwoAdjacentFaces2",()=>Items(edge.GetTwoAdjacentFaces2()).Select(f=>Id(f,faces)).ToArray());
        }
        var fr=new List<object>();r["faces"]=fr;
        for(int i=0;i<faces.Length;i++) {
            Face2 face=(Face2)faces[i];string label=name+".face["+i+"]";
            var f=new Dictionary<string,object>();fr.Add(f);f["id"]=i;
            var owner=Read(label+".GetBody",()=>face.GetBody());
            f["ownerName"]=owner==null?null:Read(label+".owner.Name",()=>((Body2)owner).Name);
            var ownerOk=Read(label+".owner.IsSame",()=>Same(body,owner));f["ownerVerified"]=ownerOk;
            if(!(ownerOk is bool) || !(bool)ownerOk) Gap(label,"Face owner not verified");
            f["faceInSurfaceSense"]=Read(label+".FaceInSurfaceSense",()=>face.FaceInSurfaceSense());
            f["surface"]=Read(label+".GetSurface/parameters",()=>SurfaceData((Surface)face.GetSurface(),label+".surface"));
            f["uvBounds"]=Read(label+".GetUVBounds",()=>Numbers(face.GetUVBounds()));
            f["naturalCylinderRadialSign"]=Read(label+".cylinderNaturalNormal",()=> {
                Surface surface=(Surface)face.GetSurface();
                if(!surface.IsCylinder())return null;
                var cp=Numbers(surface.CylinderParams);
                var faceEdges=Items(face.GetEdges());
                if(faceEdges.Length==0)throw new InvalidOperationException("No boundary witness for cylinder orientation");
                Edge edge=(Edge)faceEdges[0];edge.GetCurve();
                var point=Numbers(edge.GetCurveParams3().StartPoint);
                var evaluation=Numbers(surface.EvaluateAtPoint(point[0],point[1],point[2]));
                if(evaluation==null||evaluation.Length<3)throw new InvalidOperationException("Cylinder normal evaluation unavailable");
                f["naturalNormalEvaluationRaw"]=evaluation;
                f["naturalNormalWitnessM"]=point;
                double axial=0,norm=0;for(int k=0;k<3;k++){axial+=(point[k]-cp[k])*cp[k+3];norm+=cp[k+3]*cp[k+3];}
                double dot=0;for(int k=0;k<3;k++)dot+=evaluation[k]*(point[k]-cp[k]-cp[k+3]*axial/norm);
                if(Double.IsNaN(dot)||Double.IsInfinity(dot)||dot==0)throw new InvalidOperationException("Cylinder radial normal sign ambiguous");
                return Math.Sign(dot);
            });

            var loopsRaw=Read(label+".GetLoops",()=>face.GetLoops());
            if(loopsRaw==null) Gap(label,"No loop array; cannot certify trimmed domain");
            var loops=Items(loopsRaw);var lr=new List<object>();f["loops"]=lr;
            for(int j=0;j<loops.Length;j++) {
                Loop2 loop=(Loop2)loops[j];string ll=label+".loop["+j+"]";
                var l=new Dictionary<string,object>();lr.Add(l);l["id"]=j;
                l["isOuter"]=Read(ll+".IsOuter",()=>loop.IsOuter());
                l["isSingular"]=Read(ll+".IsSingular",()=>loop.IsSingular());
                var coRaw=Read(ll+".GetCoEdges",()=>loop.GetCoEdges());
                var cos=Items(coRaw);l["coedgeCount"]=cos.Length;
                var cr=new List<object>();l["coedges"]=cr;
                // Array order is not assumed. nextId records the actual ordered linkage.
                for(int k=0;k<cos.Length;k++) {
                    CoEdge co=(CoEdge)cos[k];string cl=ll+".coedge["+k+"]";
                    var c=new Dictionary<string,object>();cr.Add(c);c["id"]=k;
                    var edgeId=Read(cl+".GetEdge",()=>Id(co.GetEdge(),edges));
                    var nextId=Read(cl+".GetNext",()=>Id(co.GetNext(),cos));
                    c["edgeId"]=edgeId;c["nextId"]=nextId;
                    if(edgeId==null || (int)edgeId<0)Gap(cl,"Unresolved edge reference");
                    if(nextId==null || (int)nextId<0)Gap(cl,"Unresolved next coedge reference");
                    c["sense"]=Read(cl+".GetSense",()=>co.GetSense());
                    c["curveBoundsRaw"]=Read(cl+".GetCurveParams",()=>Numbers(co.GetCurveParams()));
                }
                if(coRaw==null) Gap(ll,"Coedge array null; singular or incomplete loop requires review");
            }
        }
        r["verticesM"]=vertices.Select((v,i)=>new {id=i,point=Read(name+".vertex["+i+"].GetPoint",()=>VertexPoint(v))}).ToArray();
        r["idsScope"]="This body in this report only; -1 means unresolved reference";
        r["trimDomainCertified"]=false;
        return r;
    }
    static object Snapshot(ModelDoc2 doc,PartDoc part) {
        return new {path=doc.GetPathName(),configuration=doc.ConfigurationManager.ActiveConfiguration.Name,
            dirty=doc.GetSaveFlag(),updateStamp=doc.GetUpdateStamp(),
            selectionCount=((SelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1),
            bodies=Items(part.GetBodies2((int)swBodyType_e.swSolidBody,false)).Cast<Body2>()
            .Select(b=>new {name=b.Name,visible=b.Visible,stamp=b.GetUpdateStamp()}).OrderBy(b=>b.name,StringComparer.Ordinal).ToArray()};
    }
    static Body2 Resolve(Body2[] bodies,string name) {
        var found=bodies.Where(b=>String.Equals(b.Name,name,StringComparison.Ordinal)).ToArray();
        if(found.Length!=1) throw new InvalidOperationException(name+": exact match count="+found.Length);
        return found[0];
    }
    static object MatrixCheck(double[][] m,double[] raw) {
        double det=m[0][0]*(m[1][1]*m[2][2]-m[1][2]*m[2][1])-m[0][1]*(m[1][0]*m[2][2]-m[1][2]*m[2][0])+m[0][2]*(m[1][0]*m[2][1]-m[1][1]*m[2][0]);
        double residual=0;
        for(int i=0;i<3;i++) for(int j=0;j<3;j++) {
            double sum=0;for(int k=0;k<3;k++)sum+=m[k][i]*m[k][j];
            residual=Math.Max(residual,Math.Abs(sum-(i==j?1:0)));
        }
        bool finite=m.All(row=>row.All(x=>!Double.IsNaN(x)&&!Double.IsInfinity(x)));
        bool rigid=finite && residual<=1e-10 && Math.Abs(Math.Abs(det)-1)<=1e-10;
        return new {homogeneousColumnVectorMatrix=m,determinant=det,orthogonalityResidual=residual,
            numericalMatrixTolerance=1e-10,noScaleOrShear=rigid,
            transformType=!rigid?"NON_RIGID_OR_INVALID":det<0?"REFLECTION_WITH_POSSIBLE_ROTATION_TRANSLATION":"ROTATION_TRANSLATION",
            rawArrayData=raw,geometryEquivalenceProven=false};
    }
    static object TransformData(MathTransform transform) {
        double[] raw=Numbers(transform.ArrayData);
        // Decode by applying transform only to standalone MathPoint objects.
        // No Body2.Copy/ApplyTransform, no temporary geometry in a document.
        MathUtility utility=(MathUtility)Sw.GetMathUtility();
        var points=new[]{new double[]{0,0,0},new double[]{1,0,0},new double[]{0,1,0},new double[]{0,0,1}};
        var q=points.Select(p=>Numbers(((MathPoint)((MathPoint)utility.CreatePoint(p)).MultiplyTransform(transform)).ArrayData)).ToArray();
        double[][] m=new double[4][];
        for(int r=0;r<3;r++)m[r]=new[]{q[1][r]-q[0][r],q[2][r]-q[0][r],q[3][r]-q[0][r],q[0][r]};
        m[3]=new double[]{0,0,0,1};
        return MatrixCheck(m,raw);
    }
    static object Coincidence(Body2 target,Body2 input) {
        return Read(target.Name+".GetCoincidenceTransform2("+input.Name+")",()=> {
            MathTransform transform;
            bool result=target.GetCoincidenceTransform2(input,out transform);
            return new {runtimeCallCompleted=true,apiReturned=result,inputBody=input.Name,targetBody=target.Name,
                transform=transform==null?null:Read("MathTransform.ArrayData/decode",()=>TransformData(transform)),
                transformProvided=transform!=null,sourceBodiesTransformed=false,
                evidenceOnly=true};
        });
    }
    static object Signature() {
        var method=typeof(IBody2).GetMethod("GetCoincidenceTransform2");
        return new {assembly=typeof(IBody2).Assembly.FullName,availableInInstalledInterop=method!=null,
            signature=method==null?null:method.ToString(),
            parameters=method==null?null:method.GetParameters().Select(p=>new {name=p.Name,type=p.ParameterType.FullName,isOut=p.IsOut,isIn=p.IsIn}).ToArray()};
    }
    public static object Capture(Body2 body) {
        Sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");Errors.Clear();Gaps.Clear();
        var raw=AB.Obj(Json.DeserializeObject(Json.Serialize(BodyData(body))));
        raw["readErrors"]=Errors.ToArray();raw["readGaps"]=Gaps.ToArray();return raw;
    }
}
