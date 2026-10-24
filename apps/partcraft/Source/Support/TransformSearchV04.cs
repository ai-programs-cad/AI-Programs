using System;
using System.Linq;
using System.Collections.Generic;

// Numeric transforms only. No SolidWorks body mutation or creation.
public static class TS04 {
    public static int MappingLimit=4096;
    class Feature {public NV V;public string Kind;public double Size;}
    public static double Det(NTransform t){return NV.Dot(t.Vector(new NV(1,0,0)),NV.Cross(t.Vector(new NV(0,1,0)),t.Vector(new NV(0,0,1))));}
    public static object Check(NTransform t){
        bool finite=t.M.All(r=>r.All(NV.Finite));double ortho=0;
        for(int i=0;i<3;i++)for(int j=0;j<3;j++){double d=0;for(int k=0;k<3;k++)d+=t.M[k][i]*t.M[k][j];ortho=Math.Max(ortho,Math.Abs(d-(i==j?1:0)));}
        double det=Det(t),h=Math.Abs(t.M[3][3]-1)+Math.Abs(t.M[3][0])+Math.Abs(t.M[3][1])+Math.Abs(t.M[3][2]);
        return new{passed=finite&&ortho<=NB03.Tolerance.ComputationalDimensionless&&Math.Abs(Math.Abs(det)-1)<=NB03.Tolerance.ComputationalDimensionless&&h<=NB03.Tolerance.ComputationalDimensionless,
            determinant=det,orthogonalityResidual=ortho,homogeneousResidual=h,translationM=new[]{t.M[0][3],t.M[1][3],t.M[2][3]},scaleCheckedByGramMatrix=true};
    }
    public static NTransform Inverse(NTransform t){
        var m=new[]{new double[4],new double[4],new double[4],new[]{0d,0,0,1}};
        for(int i=0;i<3;i++){for(int j=0;j<3;j++)m[i][j]=t.M[j][i];for(int j=0;j<3;j++)m[i][3]-=m[i][j]*t.M[j][3];}
        return new NTransform(m);
    }
    static List<Feature> Features(NBody b){
        var f=new List<Feature>();
        foreach(var x in b.Faces)f.Add(new Feature{V=x.N.Unit(),Kind=x.Type,Size=x.Type=="CYLINDER"?x.Radius:0});
        foreach(var x in b.Edges){f.Add(new Feature{V=x.Normal,Kind="CONIC_NORMAL",Size=Math.Max(x.A.Norm(),x.B.Norm())});
            if(Math.Abs(x.A.Norm()-x.B.Norm())>NB03.Tolerance.GeometryM)f.Add(new Feature{V=(x.A.Norm()>x.B.Norm()?x.A:x.B).Unit(),Kind="ELLIPSE_MAJOR",Size=Math.Max(x.A.Norm(),x.B.Norm())});}
        for(int i=0;i<b.Edges.Count;i++)for(int j=i+1;j<b.Edges.Count;j++){var d=b.Edges[j].C-b.Edges[i].C;if(d.Norm()>NB03.Tolerance.GeometryM)f.Add(new Feature{V=d.Unit(),Kind="CENTER_CHORD",Size=d.Norm()});}
        return f;
    }
    static bool Compatible(Feature a,Feature b){return a.Kind==b.Kind&&Math.Abs(a.Size-b.Size)<=NB03.Tolerance.GeometryM;}
    static NV[] Frame(NV a,NV b){a=a.Unit();var q=b-a*NV.Dot(a,b);if(q.Norm()<1e-7)return null;var v=q.Unit();return new[]{a,v,NV.Cross(a,v).Unit()};}
    static NTransform FrameTransform(NV[] a,NV[] b,int parity,NV ca,NV cb){
        var m=new[]{new double[4],new double[4],new double[4],new[]{0d,0,0,1}};
        for(int i=0;i<3;i++)for(int j=0;j<3;j++)for(int k=0;k<3;k++)m[i][j]+=b[k].Data()[i]*a[k].Data()[j]*(k==2?parity:1);
        var t=new NTransform(m);var d=cb-t.Vector(ca);m[0][3]=d.X;m[1][3]=d.Y;m[2][3]=d.Z;return new NTransform(m);
    }
    static NV Center(NBody b){var c=new NV(0,0,0);foreach(var e in b.Edges)c+=e.C;return c*(1d/b.Edges.Count);}
    static string Key(NTransform t){return String.Join(",",t.M.Take(3).SelectMany(r=>r).Select(v=>Math.Round(v,11).ToString("R",System.Globalization.CultureInfo.InvariantCulture)));}
    public static List<NTransform> Generate(NBody a,NBody b,out bool continuousGauge){
        var fa=Features(a);var fb=Features(b);continuousGauge=false;
        if(!fa.Any(x=>fa.Any(y=>NV.Cross(x.V,y.V).Norm()>1e-7))){
            continuousGauge=true;
            foreach(var pair in new[]{fa,fb}){var n=pair[0].V;var h=Math.Abs(n.X)<.8?new NV(1,0,0):new NV(0,1,0);pair.Add(new Feature{V=NV.Cross(n,h).Unit(),Kind="SYMMETRY_GAUGE",Size=0});}
        }
        var result=new Dictionary<string,NTransform>();var ca=Center(a);var cb=Center(b);
        for(int i=0;i<fa.Count;i++)for(int j=i+1;j<fa.Count;j++){
            var af=Frame(fa[i].V,fa[j].V);if(af==null)continue;
            foreach(var x in fb.Where(x=>Compatible(fa[i],x)))foreach(var y in fb.Where(y=>Compatible(fa[j],y)))
                for(int s=-1;s<=1;s+=2)for(int q=-1;q<=1;q+=2){
                    if(Math.Abs(NV.Dot(fa[i].V,fa[j].V)-s*q*NV.Dot(x.V,y.V))>1e-8)continue;
                    var bf=Frame(x.V*s,y.V*q);if(bf==null)continue;
                    for(int parity=-1;parity<=1;parity+=2){var t=FrameTransform(af,bf,parity,ca,cb);if(NB03.Passed(Check(t)))result[Key(t)]=t;}
                }
        }
        return result.Values.ToList();
    }
    static List<int[]> Bijections(bool[][] ok,ref bool truncated){
        var result=new List<int[]>();var map=new int[ok.Length];var used=new bool[ok.Length];
        Action<int> visit=null;bool cut=false;
        visit=k=>{if(result.Count>=MappingLimit){cut=true;return;}if(k==ok.Length){result.Add((int[])map.Clone());return;}
            for(int j=0;j<used.Length;j++)if(!used[j]&&ok[k][j]){used[j]=true;map[k]=j;visit(k+1);used[j]=false;}};
        visit(0);truncated|=cut;return result;
    }
    static object Surface(NFace a,NFace b){
        if(a.Type!=b.Type)return new{passed=false,diagnosticEnvelopeM=(object)null};
        double angle,res;bool material;
        if(a.Type=="PLANE"){angle=(a.N-b.N).Norm();material=NV.Dot(a.N,b.N)>0;res=Math.Abs(NV.Dot(b.N,a.O-b.O))+angle*a.Reach();}
        else {angle=Math.Min((a.N-b.N).Norm(),(a.N+b.N).Norm());var d=a.O-b.O;res=(d-b.N*NV.Dot(d,b.N)).Norm()+angle*a.Reach()+Math.Abs(a.Radius-b.Radius);material=a.MaterialSign==b.MaterialSign;}
        return new{passed=material&&res<=NB03.Tolerance.GeometryM+NB03.Tolerance.LengthGuard(a.O.Norm()+b.O.Norm()+a.Reach()),diagnosticEnvelopeM=(object)res,directionResidual=angle,materialOrientationCompatible=material,certified=false};
    }
    public static object Direction(NBody a,NBody b,NTransform t){
        bool truncated=false;int parity=Det(t)<0?-1:1;
        if(a.Edges.Count!=b.Edges.Count||a.Faces.Count!=b.Faces.Count)return new{passed=false,reason="Different entity counts",mappingSearchTruncated=false};
        var ec=a.Edges.Select(x=>b.Edges.Select(y=>NB03.ConicResidual(x.Transform(t),y)).ToArray()).ToArray();
        var fc=a.Faces.Select(x=>b.Faces.Select(y=>Surface(x.Transform(t),y)).ToArray()).ToArray();
        var maps=Bijections(ec.Select(row=>row.Select(NB03.Passed).ToArray()).ToArray(),ref truncated);
        foreach(var em in maps){
            Func<int,int> edge=id=>b.Edges[em[a.Edges.FindIndex(e=>e.Id==id)]].Id;
            var faceOK=fc.Select(row=>row.Select(NB03.Passed).ToArray()).ToArray();
            for(int i=0;i<a.Faces.Count;i++)for(int j=0;j<b.Faces.Count;j++){
                var x=a.Faces[i];var y=b.Faces[j];
                faceOK[i][j]&=x.Loops.Count==y.Loops.Count;
                foreach(var l in x.Loops){var matches=y.Loops.Where(z=>z.Edge.Id==edge(l.Edge.Id)&&z.Outer==l.Outer).ToArray();
                    if(matches.Length!=1){faceOK[i][j]=false;continue;}
                    int ei=a.Edges.FindIndex(e=>e.Id==l.Edge.Id);int sign=AB.Int(AB.Get(ec[ei][em[ei]],"parameterOrientation"));
                    int sa=l.Sense==l.Edge.EdgeSense?1:-1,sb=matches[0].Sense==matches[0].Edge.EdgeSense?1:-1;
                    faceOK[i][j]&=sa*sign*parity==sb;
                }
            }
            foreach(var fm in Bijections(faceOK,ref truncated)){
                Func<int,int> face=id=>b.Faces[fm[a.Faces.FindIndex(f=>f.Id==id)]].Id;
                bool adjacent=true;
                for(int i=0;i<a.Edges.Count;i++)adjacent&=a.Edges[i].Adj.Select(face).OrderBy(x=>x).SequenceEqual(b.Edges[em[i]].Adj.OrderBy(x=>x));
                if(!adjacent)continue;
                var edges=Enumerable.Range(0,em.Length).Select(i=>new{input=a.Edges[i].Id,target=b.Edges[em[i]].Id,check=ec[i][em[i]]}).ToArray();
                var faces=Enumerable.Range(0,fm.Length).Select(i=>new{input=a.Faces[i].Id,target=b.Faces[fm[i]].Id,check=fc[i][fm[i]]}).ToArray();
                double max=edges.Max(e=>AB.Num(AB.Get(e.check,"diagnosticWholeCurveEnvelopeM")));
                max=Math.Max(max,faces.Max(f=>AB.Num(AB.Get(f.check,"diagnosticEnvelopeM"))));
                return new{passed=true,edgeMap=edges,faceMap=faces,diagnosticMaximumEnvelopeM=max,mappingSearchTruncated=truncated,loopCoedgeOrientationCompatible=true,adjacencyCompatible=true,certified=false};
            }
        }
        return new{passed=false,reason="No complete compatible edge/face/loop/coedge/material/adjacency bijection",mappingSearchTruncated=truncated,contourChecks=ec,surfaceChecks=fc};
    }
    static bool Valid(NBody b){return b.Issues.Count==0&&b.Numeric.Count>0&&b.Numeric.All(NB03.Passed)&&b.Topology.All(NB03.Passed)&&b.Domain.All(NB03.Passed);}
    static object Summary(NBody b){return new{certificateAvailable=false,numericalChecks=b.Numeric,topologyChecks=b.Topology,trimChecks=b.Domain,incompleteChecks=b.Issues,allAvailableDiagnosticsCompatible=Valid(b)};}
    public static object Verify(Dictionary<string,object> report){
        var r=new Dictionary<string,object>{{"classification","NEEDS_REVIEW"},{"brepEquivalenceVerified",false},{"maximumDeviationM",null},{"certifiedUpperBoundM",null}};
        r["unfinishedChecks"]=new[]{"Outward-rounded whole-BREP error certification and COM parameter uncertainty","Certified complete trimmed domain and pcurve equivalence","Global continuous symmetry search completeness; finite feature hypotheses are not an exhaustive congruence proof"};
        r["tolerances"]=new{geometryM=NB03.Tolerance.GeometryM,computationalDimensionless=NB03.Tolerance.ComputationalDimensionless};
        try{
            var a=NB03.Parse(AB.Get(report,"bodyA"));var b=NB03.Parse(AB.Get(report,"bodyB"));
            r["bodyAValidation"]=Summary(a);r["bodyBValidation"]=Summary(b);
            if(a.Edges.Count==0||b.Edges.Count==0)throw new InvalidOperationException("No supported full conics available");
            bool gauge;var generated=Generate(a,b,out gauge);int automatic=generated.Count;var sources=generated.Select(t=>"ANALYTIC_FEATURE_FRAME").ToList();
            var candidateInputIssues=new List<object>();
            foreach(var name in new[]{"coincidenceAtoB","coincidenceBtoA"})try{
                var c=AB.Get(report,name);if(!AB.Bool(AB.Get(c,"apiReturned")))continue;
                var tr=AB.Get(c,"transform");var t=new NTransform(AB.Get(tr,"homogeneousColumnVectorMatrix"));
                object raw;if(AB.TryGet(tr,"rawArrayData",out raw)&&raw!=null){var ar=AB.Array(raw);if(ar.Length!=16||!NV.Finite(AB.Num(ar[12]))||Math.Abs(AB.Num(ar[12])-1)>NB03.Tolerance.ComputationalDimensionless)throw new InvalidOperationException("Invalid API rawArrayData size or explicit scale");}
                generated.Add(name=="coincidenceBtoA"?Inverse(t):t);sources.Add(name);
            }catch(Exception ex){candidateInputIssues.Add(new{source=name,reason=ex.Message,exception=ex.GetType().FullName});}
            r["candidateInputIssues"]=candidateInputIssues;
            var candidates=new List<Dictionary<string,object>>();
            for(int i=0;i<generated.Count;i++){
                var t=generated[i];var check=Check(t);object f=null,rev=null;bool ok=false;double max=Double.PositiveInfinity;
                if(NB03.Passed(check)){f=Direction(a,b,t);rev=Direction(b,a,Inverse(t));ok=NB03.Passed(f)&&NB03.Passed(rev)&&Valid(a)&&Valid(b);
                    if(ok)max=Math.Max(AB.Num(AB.Get(f,"diagnosticMaximumEnvelopeM")),AB.Num(AB.Get(rev,"diagnosticMaximumEnvelopeM")));}
                candidates.Add(new Dictionary<string,object>{{"source",sources[i]},{"matrix",t.M},{"transformChecks",check},{"AtoB",f},{"BtoA",rev},{"diagnosticsCompatible",ok},{"diagnosticMaximumEnvelopeM",NV.Finite(max)?(object)max:null},{"parity",Det(t)<0?-1:1}});
            }
            var accepted=candidates.Where(c=>(bool)c["diagnosticsCompatible"]).OrderBy(c=>AB.Num(c["diagnosticMaximumEnvelopeM"]))
                .ThenByDescending(c=>(int)c["parity"]).ThenByDescending(c=>new NTransform(c["matrix"]).M[0][0]).ToArray();
            r["automaticCandidateCount"]=automatic;r["candidateCount"]=candidates.Count;r["continuousSymmetryGaugeUsed"]=gauge;r["candidates"]=candidates;
            r["bestCandidate"]=accepted.FirstOrDefault();r["bestProperCandidate"]=accepted.FirstOrDefault(c=>(int)c["parity"]==1);r["bestImproperCandidate"]=accepted.FirstOrDefault(c=>(int)c["parity"]==-1);
            r["properDiagnosticMatchFound"]=accepted.Any(c=>(int)c["parity"]==1);r["improperDiagnosticMatchFound"]=accepted.Any(c=>(int)c["parity"]==-1);
            r["acceptedCandidateCount"]=accepted.Length;r["multipleTransformsDetected"]=accepted.Length>1;r["independentReflectionSearchPerformed"]=true;
            r["candidateRanking"]="Smallest bidirectional diagnostic envelope; exact ties prefer proper parity then greatest R00. Symmetry alternatives remain reported.";
            r["searchScope"]=new{arbitrarySpatialAngles=true,axisSignChoices=true,finiteFeatureFrameEnumeration=true,centroidTranslationUsesAllConicCenters=true,
                deduplicationDecimalPlaces=11,originalCandidateDoublesPreserved=true,completeGlobalSearchCertified=false,mappingLimitPerEnumeration=MappingLimit,
                supportedDomains="Full circular/elliptical single-coedge loops, planar disk/annulus/multiple holes and full cylindrical bands; native checks are diagnostic"};
            object state;r["sourceObservedStateUnchanged"]=AB.TryGet(report,"observedStateUnchanged",out state)?state:null;
            object errors,gaps;r["sourceExtractionClean"]=AB.TryGet(report,"errors",out errors)&&errors!=null&&AB.TryGet(report,"dataGaps",out gaps)&&gaps!=null?
                (object)(AB.Array(errors).Length==0&&AB.Array(gaps).Length==0):null;
            r["reason"]="Finite analytic feature search and bidirectional diagnostics do not certify complete bounded BREP equivalence.";
        }catch(Exception ex){r["reason"]=ex.Message;r["exception"]=ex.GetType().FullName;}
        return r;
    }
}
