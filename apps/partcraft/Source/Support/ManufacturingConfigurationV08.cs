using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
public class ConfigurationStateV08 {
    public string name,parent;public bool derived;public int type;public bool needsRebuild;public bool? boundFlatSuppressed,sheetMetalActive,sourceBendsActive;public List<object> featureStates=new List<object>();public List<string> errors=new List<string>();
    public bool FoldedCandidate(){return errors.Count==0&&boundFlatSuppressed==true&&sheetMetalActive==true&&sourceBendsActive==true;}
}
public class ConfigurationActivationV08 {public string target,before,after;public bool called;public bool? returned;public bool success;public string error,hresult,stackTrace;}
public static class ManufacturingConfigurationV08 {
    public static object Evidence(string name){return new{name,utf16Escaped=name==null?null:String.Concat(name.Select(c=>"\\u"+((int)c).ToString("X4"))),utf8Hex=name==null?null:BitConverter.ToString(Encoding.UTF8.GetBytes(name)),utf16Length=name==null?(int?)null:name.Length};}
    public static ConfigurationActivationV08 Activate(string target,IEnumerable<string> names,Func<string> active,Func<string,bool> show){
        var r=new ConfigurationActivationV08{target=target,before=active()};
        if(!names.Contains(target,StringComparer.Ordinal)){r.after=r.before;r.error="Exact configuration absent from API enumeration";return r;}
        if(String.Equals(r.before,target,StringComparison.Ordinal)){r.after=active();r.success=String.Equals(r.after,target,StringComparison.Ordinal);return r;}
        try{r.called=true;r.returned=show(target);r.after=active();r.success=r.returned==true&&String.Equals(r.after,target,StringComparison.Ordinal);if(!r.success)r.error="Activation return/readback did not establish requested API configuration";}
        catch(Exception ex){r.error=ex.ToString();r.hresult="0x"+ex.HResult.ToString("X8");r.stackTrace=ex.StackTrace;r.after=active();}return r;
    }
    public static List<ConfigurationStateV08> Order(string source,List<ConfigurationStateV08> rows){
        if(rows.Select(r=>r.name).Distinct(StringComparer.Ordinal).Count()!=rows.Count)throw new InvalidOperationException("Duplicate configuration names in API enumeration");
        return rows.Where(r=>r.FoldedCandidate()).OrderBy(r=>r.name==source?0:1).ThenBy(r=>r.name,StringComparer.Ordinal).ToList();
    }
}
