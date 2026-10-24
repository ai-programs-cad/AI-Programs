using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public static class ManufacturingStateV09 {
    public static string Json(object data){return String.Concat(CutListCoreV01.Json.Serialize(data).Select(c=>c>127?"\\u"+((int)c).ToString("X4"):c.ToString()));}
    public static Dictionary<string,object> Capture(SldWorks sw,ModelDoc2 source){var active=sw.ActiveDoc as ModelDoc2;return new Dictionary<string,object>{{"modelState",ManufacturingInventoryV01.Snapshot(source)},{"sourceIsActive",active!=null&&sw.IsSame(source,active)==(int)swObjectEquality.swObjectSame},{"activePath",active==null?null:active.GetPathName()},{"activeConfiguration",active==null?null:active.ConfigurationManager.ActiveConfiguration.Name}};}
    public static List<object> Diff(object expected,object actual){var result=new List<object>();Walk("",CutListCoreV01.Json.DeserializeObject(CutListCoreV01.Json.Serialize(expected)),CutListCoreV01.Json.DeserializeObject(CutListCoreV01.Json.Serialize(actual)),result);return result;}
    static void Walk(string path,object a,object b,List<object> result){
        var da=a as Dictionary<string,object>;var db=b as Dictionary<string,object>;
        if(da!=null&&db!=null){foreach(string key in da.Keys.Union(db.Keys).OrderBy(k=>k,StringComparer.Ordinal))Walk(path==""?key:path+"."+key,da.ContainsKey(key)?da[key]:"<ABSENT>",db.ContainsKey(key)?db[key]:"<ABSENT>",result);return;}
        var aa=a as object[];var bb=b as object[];if(aa!=null&&bb!=null){for(int i=0;i<Math.Max(aa.Length,bb.Length);i++)Walk(path+"["+i+"]",i<aa.Length?aa[i]:"<ABSENT>",i<bb.Length?bb[i]:"<ABSENT>",result);return;}
        if(CutListCoreV01.Json.Serialize(a)!=CutListCoreV01.Json.Serialize(b))result.Add(new{field=path,expected=a,actual=b,category=path.Contains("stamp")||path.Contains("Stamp")?"INTERNAL_REVISION_UNVERIFIED_NOT_AUTOMATIC_GEOMETRY_PROOF":"SOURCE_DOCUMENT_STATE",safetyRelevant=true});
    }
}
