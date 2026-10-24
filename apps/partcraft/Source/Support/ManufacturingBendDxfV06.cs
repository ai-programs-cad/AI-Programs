using System;
using System.Linq;
using System.Collections.Generic;
public class BendBindingV06 {
    public string bodyName,configurationApiName,flatPatternFeature,modelPath;public int isolatedSolidCount,options;public bool flatPatternBodyVerified,contextUnchanged;
    public bool Verified(){return !String.IsNullOrEmpty(bodyName)&&!String.IsNullOrEmpty(configurationApiName)&&!String.IsNullOrEmpty(flatPatternFeature)&&!String.IsNullOrEmpty(modelPath)&&isolatedSolidCount==1&&(options==4||options==5)&&flatPatternBodyVerified&&contextUnchanged;}
}
public class BendValidationV06 {
    public bool structureAccepted,bindingVerified,isBendOnlyProven;public string status="NEEDS_REVIEW",reason,contentStatus;public int originalEntityCount,outputEntityCount,matchedCutRecords;public NativeDxfInspectionV05 nativeInspection;public bool closedCutContoursRequired=false,splineApproximationPerformed=false,productionCertified=false;
    [System.Web.Script.Serialization.ScriptIgnore]public string outputText;
}
public static class ManufacturingBendDxfV06 {
    class Doc {public string prefix,suffix;public List<string> records=new List<string>();}
    static Doc Parse(string text){
        var lines=text.Replace("\r","").Split('\n').ToList();if(lines.LastOrDefault()=="")lines.RemoveAt(lines.Count-1);if(lines.Count%2!=0)throw new InvalidOperationException("Invalid DXF pairs");
        int begin=-1,end=-1;for(int i=0;i+3<lines.Count;i+=2)if(lines[i].Trim()=="0"&&lines[i+1].Trim()=="SECTION"&&lines[i+2].Trim()=="2"&&lines[i+3].Trim()=="ENTITIES"){begin=i+4;break;}
        if(begin<0)throw new InvalidOperationException("DXF ENTITIES missing");for(int i=begin;i+1<lines.Count;i+=2)if(lines[i].Trim()=="0"&&lines[i+1].Trim()=="ENDSEC"){end=i;break;}if(end<0)throw new InvalidOperationException("ENTITIES terminator missing");
        var d=new Doc{prefix=String.Join("\n",lines.Take(begin))+"\n",suffix=String.Join("\n",lines.Skip(end))+"\n"};
        int start=begin;for(int i=begin+2;i<end;i+=2)if(lines[i].Trim()=="0"){d.records.Add(String.Join("\n",lines.Skip(start).Take(i-start))+"\n");start=i;}if(start<end)d.records.Add(String.Join("\n",lines.Skip(start).Take(end-start))+"\n");return d;
    }
    static string Key(string record){var a=record.TrimEnd('\n').Split('\n');var parts=new List<string>();for(int i=0;i<a.Length;i+=2){int code=int.Parse(a[i].Trim());if(new[]{5,330,100,8,6,62,370,420,430,440}.Contains(code))continue;parts.Add(code+":"+a[i+1].Trim());}return String.Join("|",parts);}
    public static BendValidationV06 Validate(string raw,string exactCut,BendBindingV06 binding){
        var r=new BendValidationV06{bindingVerified=binding!=null&&binding.Verified()};
        try{
            r.nativeInspection=ManufacturingNativeDxfV05.Inspect(raw);if(!r.nativeInspection.structureAccepted)throw new InvalidOperationException(r.nativeInspection.reason);
            var d=Parse(raw);r.originalEntityCount=d.records.Count;
            // Check all non-spline primitives as open technological paths. Never
            // run a laser-cut closure requirement on BEND entities.
            var nonSpline=d.records.Where(s=>s.Split('\n')[1].Trim()!="SPLINE").ToArray();
            if(nonSpline.Length>0){var check=ManufacturingDxfV04.Check(ManufacturingDxfV01.Wrap(String.Concat(nonSpline)),null,.001,false,true);if(!check.accepted)throw new InvalidOperationException("Invalid BEND primitive: "+check.reason);}
            var remaining=new List<string>(d.records);bool fullCutRemoved=false;
            if(!String.IsNullOrEmpty(exactCut)){
                var exact=Parse(exactCut);var counts=new Dictionary<string,int>();foreach(var record in exact.records){string key=Key(record);if(!counts.ContainsKey(key))counts[key]=0;counts[key]++;}
                var kept=new List<string>();foreach(var record in d.records){string key=Key(record);if(counts.ContainsKey(key)&&counts[key]>0){counts[key]--;r.matchedCutRecords++;}else kept.Add(record);}
                if(exact.records.Count>0&&counts.Values.All(n=>n==0)){remaining=kept;fullCutRemoved=true;}
            }
            if(fullCutRemoved&&remaining.Count==0)throw new InvalidOperationException("Native export contained only CUT records; no BEND remainder");
            if(!r.bindingVerified)throw new InvalidOperationException("BEND body/flat-pattern/configuration binding not established");
            r.structureAccepted=true;r.isBendOnlyProven=fullCutRemoved;r.outputEntityCount=remaining.Count;
            r.outputText=d.prefix+String.Concat(remaining)+d.suffix;
            r.contentStatus=fullCutRemoved?"ALL_EXACT_CUT_RECORDS_REMOVED_FROM_NATIVE_TECH_EXPORT":"NEEDS_REVIEW_NATIVE_TECH_CONTENT_NOT_SEPARATED";
            r.reason=fullCutRemoved?"Exact CUT multiset removed, native remaining geometry retained; no fitting or approximation. Binding checked, production certification pending.":"Open native SPLINE/primitive geometry retained; no closed-cut requirement. Pure bend content not independently established.";
        }catch(Exception ex){r.structureAccepted=false;r.reason=ex.Message;r.outputText=null;}
        return r;
    }
}
