using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
public class NativeDxfInspectionV05 {public bool structureAccepted;public string reason;public int splineCount,polylineCount,entityCount;public int units;public Dictionary<string,int> entityTypes=new Dictionary<string,int>();public string topologyStatus="NEEDS_REVIEW";}
public class NativeDxfDeviationV05 {public string status="NEEDS_REVIEW",reason;public double toleranceMm;public double? certifiedUpperBoundMm,diagnosticSampleMaximumMm;public bool wholeContourCertified=false;}
public static class ManufacturingNativeDxfV05 {
    public class Pair {public int code;public string value;}
    static double Num(string value){double n=double.Parse(value,CultureInfo.InvariantCulture);if(!NV.Finite(n))throw new InvalidOperationException("Nonfinite DXF value");return n;}
    public static NativeDxfInspectionV05 Inspect(string text){
        var result=new NativeDxfInspectionV05{units=-1};
        try{
            var lines=text.Replace("\r","").Split('\n').ToList();if(lines.LastOrDefault()=="")lines.RemoveAt(lines.Count-1);if(lines.Count%2!=0)throw new InvalidOperationException("Invalid DXF pairs");
            var pairs=new List<Pair>();for(int i=0;i<lines.Count;i+=2)pairs.Add(new Pair{code=int.Parse(lines[i].Trim()),value=lines[i+1].Trim()});
            if(pairs.Count==0||pairs.Last().code!=0||pairs.Last().value!="EOF")throw new InvalidOperationException("Missing DXF EOF");
            for(int i=0;i<pairs.Count-1;i++)if(pairs[i].code==9&&pairs[i].value=="$INSUNITS")result.units=(int)Num(pairs[i+1].value);
            if(result.units!=4&&result.units!=0&&result.units!=-1)throw new InvalidOperationException("Native DXF units incompatible with millimetres");
            var entities=new List<List<Pair>>();bool inside=false;
            for(int i=0;i<pairs.Count;i++){
                if(pairs[i].code==0&&pairs[i].value=="SECTION"&&i+1<pairs.Count){inside=pairs[++i].value=="ENTITIES";continue;}
                if(pairs[i].code==0&&pairs[i].value=="ENDSEC"){inside=false;continue;}
                if(inside&&pairs[i].code==0){var e=new List<Pair>{pairs[i]};while(i+1<pairs.Count&&pairs[i+1].code!=0)e.Add(pairs[++i]);entities.Add(e);}
            }
            if(entities.Count==0)throw new InvalidOperationException("Empty ENTITIES");
            var keys=new HashSet<string>();
            foreach(var e in entities){
                string type=e[0].value;if(!new[]{"LINE","ARC","CIRCLE","ELLIPSE","LWPOLYLINE","POLYLINE","VERTEX","SEQEND","SPLINE"}.Contains(type))throw new InvalidOperationException("Unexpected native DXF entity: "+type);
                if(!result.entityTypes.ContainsKey(type))result.entityTypes[type]=0;result.entityTypes[type]++;
                if(type=="SPLINE"){
                    result.splineCount++;Func<int,int> integer=k=>int.Parse(e.Single(p=>p.code==k).value,CultureInfo.InvariantCulture);
                    int degree=integer(71),knots=integer(72),points=integer(73);
                    var u=e.Where(p=>p.code==40).Select(p=>Num(p.value)).ToArray();var cx=e.Where(p=>p.code==10).Select(p=>Num(p.value)).ToArray();var cy=e.Where(p=>p.code==20).Select(p=>Num(p.value)).ToArray();var weights=e.Where(p=>p.code==41).Select(p=>Num(p.value)).ToArray();
                    if(degree<1||degree>25||points<degree+1||knots!=points+degree+1||u.Length!=knots||cx.Length!=points||cy.Length!=points)throw new InvalidOperationException("Incomplete/unsupported SPLINE knot/control representation");
                    if(u.Where((v,j)=>j>0&&v<u[j-1]).Any()||u[degree]>=u[points]||weights.Any(w=>w<=0)||weights.Length!=0&&weights.Length!=points)throw new InvalidOperationException("Invalid SPLINE knots/weights");
                    if(e.Any(p=>(p.code==30||p.code==31)&&Math.Abs(Num(p.value))>1e-7)||e.Any(p=>p.code==67&&p.value!="0")||e.Any(p=>p.code==410&&p.value!="Model"))throw new InvalidOperationException("Nonplanar/paper-space SPLINE");
                    string key=String.Join("|",e.Where(p=>new[]{70,71,72,73,40,41,10,20,30}.Contains(p.code)).Select(p=>p.code+":"+p.value));if(!keys.Add(key))throw new InvalidOperationException("Duplicate SPLINE records");
                }
                if(type=="LWPOLYLINE"||type=="POLYLINE")result.polylineCount++;
            }
            result.entityCount=entities.Count;result.structureAccepted=true;
            result.reason=result.splineCount>0?"Native exact SPLINE data retained; full spline-loop topology, extrema and equivalence not certified":"No SPLINE; analytic validation reported separately";
        }catch(Exception ex){result.reason=ex.Message;result.structureAccepted=false;}
        return result;
    }
    public static string ToggleEffect(NativeDxfInspectionV05 exact,NativeDxfInspectionV05 cam){
        if(!exact.structureAccepted||!cam.structureAccepted)return "UNVERIFIED_EXPORT_DATA";
        if(exact.splineCount==0)return "NO_SOURCE_SPLINE_TO_TEST";
        if(cam.splineCount>0)return "NOT_HONORED_OR_UNSUPPORTED_SPLINE_REMAINS";
        return cam.polylineCount>0?"OBSERVED_SPLINE_ABSENT_AND_POLYLINE_PRESENT_NOT_EQUIVALENCE_PROOF":"SPLINE_ABSENT_BUT_POLYLINE_CONVERSION_NOT_CONFIRMED";
    }
    public static NativeDxfDeviationV05 Deviation(string exact,string cam,double toleranceMm){
        var d=new NativeDxfDeviationV05{toleranceMm=toleranceMm};if(!NV.Finite(toleranceMm)||toleranceMm<=0){d.reason="Invalid tolerance";return d;}
        var a=ManufacturingDxfV04.Check(exact??"");var b=ManufacturingDxfV04.Check(cam??"");
        if(a.accepted&&b.accepted&&a.normalizedText==b.normalizedText){d.status="PASS";d.certifiedUpperBoundMm=0;d.wholeContourCertified=true;d.reason="Identical complete supported analytic DXF records after metadata-only normalization; no spline approximation";}
        else d.reason="Certified bidirectional whole-curve deviation bound unavailable for native SPLINE-to-polyline conversion or changed records. No sample maximum is reported as a bound.";
        return d;
    }
}
