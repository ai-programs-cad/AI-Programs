using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Web.Script.Serialization;

public class ContextV05Exception:Exception {public ContextV05Exception(string s):base(s){}}
public static class BatchV05 {
    public static string[][] DefaultPairs(){return new[]{
        new[]{"1-1","1-2"},new[]{"2-1","2-2"},new[]{"3-1","3-2"},new[]{"3-1","6-1"},new[]{"3-1","6-2"},
        new[]{"3-2","6-1"},new[]{"3-2","6-2"},new[]{"6-1","6-2"},new[]{"4-1","7-1"},new[]{"5-1","8-1"},
        new[]{"11-1","11-2"},new[]{"11-1","11-3"},new[]{"11-1","11-4"},new[]{"11-2","11-3"},new[]{"11-2","11-4"},new[]{"11-3","11-4"}};}
    public static object Get(object o,string key){object v;return AB.TryGet(o,key,out v)?v:null;}
    public static bool Flag(object o,string key){return Get(o,key) is bool&&(bool)Get(o,key);}
    public static string[][] Pairs(object input){
        var a=AB.Array(input).Select(row=>AB.Array(row).Select(x=>x as string).ToArray()).ToArray();
        if(a.Length==0||a.Any(row=>row.Length!=2||row.Any(string.IsNullOrWhiteSpace)||row[0]==row[1]))throw new InvalidOperationException("Pairs require two distinct nonempty exact body names per row");
        return a;
    }
    public static object Error(Exception ex){return new{exception=ex.GetType().FullName,hresult="0x"+ex.HResult.ToString("X8"),message=ex.Message};}
    public static Dictionary<string,object> Inspect(object raw){
        var unsupported=new List<string>();var incomplete=new List<string>();
        if(raw==null){incomplete.Add("Body BREP unavailable");return new Dictionary<string,object>{{"unsupported",unsupported},{"incomplete",incomplete},{"accepted",false}};}
        try{
            foreach(var key in new[]{"readErrors","readGaps"})if(Get(raw,key)!=null&&AB.Array(Get(raw,key)).Length>0)incomplete.Add("Body "+key+" present; inspect stored exact API errors/gaps");
            var faces=AB.Array(AB.Get(raw,"faces"));var edges=AB.Array(AB.Get(raw,"edges"));var vertices=AB.Array(AB.Get(raw,"verticesM"));
            if(AB.Int(AB.Get(raw,"faceCount"))!=faces.Length||AB.Int(AB.Get(raw,"edgeCount"))!=edges.Length||AB.Int(AB.Get(raw,"vertexCount"))!=vertices.Length)incomplete.Add("Reported and enumerated BREP counts differ");
            foreach(var f in faces){
                string id=Convert.ToString(Get(f,"id"),CultureInfo.InvariantCulture);
                if(!Flag(f,"ownerVerified"))incomplete.Add("Face "+id+" owner unverified");
                string type=Get(Get(f,"surface"),"type") as string;
                if(type!="PLANE"&&type!="CYLINDER")unsupported.Add("Face "+id+" surface "+(type??"UNKNOWN"));
                foreach(var l in AB.Array(AB.Get(f,"loops")))if(AB.Array(AB.Get(l,"coedges")).Length!=1)unsupported.Add("Face "+id+" multi-coedge or empty loop");
            }
            foreach(var e in edges){var c=Get(e,"curve");if(!Flag(c,"isCircle")&&!Flag(c,"isEllipse"))unsupported.Add("Edge "+Get(e,"id")+" curve is not a circle/ellipse");}
            var p=NB03.Parse(raw);
            if(p.Edges.Count!=edges.Length||p.Faces.Count!=faces.Length)incomplete.Add("Not all raw entities parsed");
            foreach(var x in p.Issues)incomplete.Add(new JavaScriptSerializer().Serialize(x));
            if(p.Numeric.Any(x=>!NB03.Passed(x)))incomplete.Add("Native numerical checks failed");
            if(p.Topology.Any(x=>!NB03.Passed(x)))incomplete.Add("Native topology/coedge/owner checks failed");
            if(p.Domain.Any(x=>!NB03.Passed(x)))incomplete.Add("Native trim domain diagnostics failed or unsupported");
            if(faces.Length==0||edges.Length==0)incomplete.Add("No supported bounded faces/edges");
        }catch(Exception ex){incomplete.Add(ex.Message);}
        return new Dictionary<string,object>{{"unsupported",unsupported},{"incomplete",incomplete},{"accepted",unsupported.Count==0&&incomplete.Count==0}};
    }
    public static Dictionary<string,object> Compare(string a,string b,object ba,object bb,object api,object cutA,object cutB){
        var va=Inspect(ba);var vb=Inspect(bb);
        bool supported=((List<string>)va["unsupported"]).Count==0&&((List<string>)vb["unsupported"]).Count==0;
        bool complete=(bool)va["accepted"]&&(bool)vb["accepted"];
        var r=new Dictionary<string,object>{{"bodyA",a},{"bodyB",b},{"cutListA",cutA},{"cutListB",cutB},{"classification","NEEDS_REVIEW"},
            {"brepEquivalenceVerified",false},{"certifiedUpperBoundM",null},{"bodyAValidation",va},{"bodyBValidation",vb}};
        object v=null;
        if(complete){
            var source=new Dictionary<string,object>{{"bodyA",ba},{"bodyB",bb}};
            if(api!=null)foreach(var key in new[]{"coincidenceAtoB","coincidenceBtoA"}){var val=Get(api,key);if(val!=null)source[key]=val;}
            v=TS04.Verify(source);
        }
        bool proper=complete&&Flag(v,"properDiagnosticMatchFound"),mirror=complete&&Flag(v,"improperDiagnosticMatchFound");
        var flags=new List<string>();if(proper)flags.Add("PROPER_MATCH_FOUND");if(mirror)flags.Add("MIRROR_MATCH_FOUND");
        if(!supported)flags.Add("UNSUPPORTED_GEOMETRY");
        else if(!complete)flags.Add("INCOMPLETE_GEOMETRY");
        else if(!proper&&!mirror)flags.Add("NO_MATCH_FOUND_IN_SEARCH");
        r["diagnosticFlags"]=flags;r["properMatchFound"]=proper;r["mirrorMatchFound"]=mirror;r["symmetryDetected"]=proper&&mirror;
        r["verification"]=v;r["bestProperCandidate"]=Get(v,"bestProperCandidate");r["bestImproperCandidate"]=Get(v,"bestImproperCandidate");
        var best=Get(v,"bestCandidate");r["matrix"]=Get(best,"matrix");r["diagnosticEnvelopeM"]=Get(best,"diagnosticMaximumEnvelopeM");
        var forward=Get(best,"AtoB");r["matchedFaces"]=Get(forward,"faceMap")==null?(object)null:AB.Array(Get(forward,"faceMap")).Length;
        r["matchedEdges"]=Get(forward,"edgeMap")==null?(object)null:AB.Array(Get(forward,"edgeMap")).Length;
        r["candidateCount"]=Get(v,"candidateCount");
        r["properMatrix"]=Get(Get(v,"bestProperCandidate"),"matrix");r["mirrorMatrix"]=Get(Get(v,"bestImproperCandidate"),"matrix");
        r["properDiagnosticEnvelopeM"]=Get(Get(v,"bestProperCandidate"),"diagnosticMaximumEnvelopeM");
        r["mirrorDiagnosticEnvelopeM"]=Get(Get(v,"bestImproperCandidate"),"diagnosticMaximumEnvelopeM");
        r["apiReadErrors"]=Get(api,"readErrors");
        r["boundaryAndProcessingEvidence"]=new{bodyA=ShapeEvidence(ba),bodyB=ShapeEvidence(bb),
            interpretation="Different counts/types/radius distributions are observations only; no operation history or certified DIFFERENT classification is inferred. Candidate rejection details are in verification.candidates."};
        r["processingDifferences"]=new{status="NOT_CERTIFIED",differences=Differences(ba,bb),observations=new[]{
            "Hole/end/operation equivalence is diagnosed through complete native domains, conics, oriented surfaces and adjacency; feature history is not inferred.",
            proper||mirror?"A complete supported-domain diagnostic correspondence was found.":"No verified operation-level conclusion: unsupported/incomplete data or finite-search failure is not proof of a difference."}};
        r["reviewReason"]=!supported?"Unsupported BREP: no simplified size/area comparison performed":!complete?"Incomplete or failed native BREP validation":
            "No certified complete trimmed-BREP/pcurve equivalence or outward-rounded whole-body deviation bound; finite search is not a proof of noncongruence";
        return r;
    }
    static object Differences(object a,object b){
        var differences=new List<object>();
        foreach(var key in new[]{"faceCount","edgeCount","vertexCount"}){
            object av=Get(a,key),bv=Get(b,key);
            if(av!=null&&bv!=null&&AB.Int(av)!=AB.Int(bv))differences.Add(new{check=key,bodyA=av,bodyB=bv,source="Raw BREP counts",certifiedGeometricDifference=false});
        }
        try{
            var ae=ShapeEvidence(a);var be=ShapeEvidence(b);
            var ai=Get(ae,"innerPlanarLoops");var bi=Get(be,"innerPlanarLoops");
            if(ai!=null&&bi!=null&&AB.Int(ai)!=AB.Int(bi))differences.Add(new{check="innerPlanarLoopCount",bodyA=ai,bodyB=bi,source="Native planar face loops",certifiedGeometricDifference=false});
            var ar=AB.Array(AB.Get(ae,"cylinderRadiiM")).Select(AB.Num).OrderBy(x=>x).ToArray();
            var br=AB.Array(AB.Get(be,"cylinderRadiiM")).Select(AB.Num).OrderBy(x=>x).ToArray();
            if(ar.Length!=br.Length||ar.Where((v,i)=>i<br.Length&&Math.Abs(v-br[i])>NB03.Tolerance.GeometryM).Any())
                differences.Add(new{check="cylinderRadiusMultiset",bodyA=ar,bodyB=br,source="Native cylinder surfaces in metres",certifiedGeometricDifference=false});
        }catch(Exception ex){differences.Add(new{check="processingEvidence",status="NOT_VERIFIED",reason=ex.Message});}
        return differences;
    }
    static object ShapeEvidence(object body){
        try{
            var faces=AB.Array(AB.Get(body,"faces"));var edges=AB.Array(AB.Get(body,"edges"));
            return new{faceCount=Get(body,"faceCount"),edgeCount=Get(body,"edgeCount"),vertexCount=Get(body,"vertexCount"),
                surfaceTypes=faces.GroupBy(f=>Convert.ToString(Get(Get(f,"surface"),"type"))).Select(g=>new{type=g.Key,count=g.Count()}).ToArray(),
                cylinderRadiiM=faces.Where(f=>Convert.ToString(Get(Get(f,"surface"),"type"))=="CYLINDER").Select(f=>Get(Get(f,"surface"),"radiusM")).ToArray(),
                innerPlanarLoops=faces.Where(f=>Convert.ToString(Get(Get(f,"surface"),"type"))=="PLANE").Sum(f=>AB.Array(AB.Get(f,"loops")).Count(l=>!Flag(l,"isOuter"))),
                edgeTypes=edges.Select(e=>new{id=Get(e,"id"),circle=Flag(Get(e,"curve"),"isCircle"),ellipse=Flag(Get(e,"curve"),"isEllipse"),line=Flag(Get(e,"curve"),"isLine")}).ToArray()};
        }catch(Exception ex){return new{status="NOT_VERIFIED",reason=ex.Message};}
    }
    public static Dictionary<string,object> Run(string[][] pairs,Func<string,object> load,Func<string,string,object> api,Func<string,object> cut,Action guard){
        var cache=new Dictionary<string,object>(StringComparer.Ordinal);var failed=new Dictionary<string,object>(StringComparer.Ordinal);
        var comparisons=new List<object>();var result=new Dictionary<string,object>{{"version","0.5-batch-read-only"},{"classification","NEEDS_REVIEW"},
            {"readOnly",true},{"changesMade",false},{"geometryToleranceM",NB03.Tolerance.GeometryM},{"computationalTolerance",NB03.Tolerance.ComputationalDimensionless},
            {"pairsRequested",pairs},{"comparisons",comparisons},{"bodies",cache},{"bodyReadErrors",failed}};
        try{
            foreach(var pair in pairs){
                guard();
                foreach(var name in pair.Distinct())if(!cache.ContainsKey(name)&&!failed.ContainsKey(name)){
                    guard();
                    try{cache[name]=load(name);}catch(ContextV05Exception){throw;}catch(Exception ex){failed[name]=Error(ex);}finally{guard();}
                }
                try{
                    if(pair.Any(failed.ContainsKey))comparisons.Add(new{bodyA=pair[0],bodyB=pair[1],classification="NEEDS_REVIEW",diagnosticFlags=new[]{"BODY_READ_ERROR"},
                        reviewReason="Body read failed; remaining pairs continue",bodyErrors=pair.Where(failed.ContainsKey).Select(n=>new{name=n,error=failed[n]}).ToArray()});
                    else{
                        object candidates=null;var pairApiErrors=new List<object>();
                        // No API candidate calls for unsupported or incomplete geometry.
                        if((bool)Inspect(cache[pair[0]])["accepted"]&&(bool)Inspect(cache[pair[1]])["accepted"])
                            try{candidates=api(pair[0],pair[1]);}catch(ContextV05Exception){throw;}catch(Exception ex){pairApiErrors.Add(Error(ex));}
                        guard();
                        var row=Compare(pair[0],pair[1],cache[pair[0]],cache[pair[1]],candidates,cut(pair[0]),cut(pair[1]));
                        row["pairApiErrors"]=pairApiErrors;comparisons.Add(row);
                    }
                }catch(ContextV05Exception){throw;}catch(Exception ex){comparisons.Add(new{bodyA=pair[0],bodyB=pair[1],classification="NEEDS_REVIEW",
                    diagnosticFlags=new[]{"PAIR_ERROR"},reviewReason="Pair exception; remaining pairs continue",error=Error(ex)});}
                guard();
            }
            result["status"]="COMPLETED_DIAGNOSTICS_NOT_CERTIFIED";
        }catch(ContextV05Exception ex){result["status"]="STOPPED_CONTEXT_CHANGED";result["contextError"]=Error(ex);}
        result["uniqueBodyReadAttempts"]=cache.Count+failed.Count;result["pairsCompleted"]=comparisons.Count;return result;
    }
    static string Cell(object x){string s=x==null?"NOT_VERIFIED":x is string?(string)x:new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(x);char q=(char)34;return q+s.Replace(q.ToString(),new string(q,2))+q;}
    public static string Csv(object report){
        var columns=new[]{"bodyA","bodyB","cutListA","cutListB","classification","diagnosticFlags","properMatchFound","mirrorMatchFound","symmetryDetected",
            "matrix","properMatrix","mirrorMatrix","diagnosticEnvelopeM","properDiagnosticEnvelopeM","mirrorDiagnosticEnvelopeM",
            "matchedFaces","matchedEdges","candidateCount","bodyAValidation","bodyBValidation","boundaryAndProcessingEvidence","processingDifferences","reviewReason"};
        var sb=new StringBuilder();sb.AppendLine(String.Join(",",columns));
        foreach(var row in AB.Array(AB.Get(report,"comparisons")))sb.AppendLine(String.Join(",",columns.Select(c=>Cell(Get(row,c)))));
        return sb.ToString();
    }
}
