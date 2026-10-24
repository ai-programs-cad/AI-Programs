using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
public class CLProperty {
    public string name,rawValue,resolvedValue;public int? typeCode,resultCode;public bool? wasResolved,linkedToProperty;public object error;
}
public class CLBody {
    public string id,name;public bool? visible,sheetMetal;public object geometry,material;public List<string> cutListIds=new List<string>();
}
public class CLFolder {
    public string id,name;public int? apiBodyCount,enumeratedBodyCount;public int? physicalSolidBodyCount;
    public List<string> bodyIds=new List<string>();public List<object> unknownBodies=new List<object>();
    public List<CLProperty> properties=new List<CLProperty>();public List<string> warnings=new List<string>();
    public object freshness;public bool bodyEnumerationSucceeded,propertiesEnumerationSucceeded;public object suppressed,excludedFromCutList;
}
public static class CutListCoreV01 {
    public static List<T> Traverse<T>(IEnumerable<T> roots,Func<T,IEnumerable<T>> children,Func<T,T,bool> same){
        var result=new List<T>();var stack=new Stack<T>(roots.Reverse());
        while(stack.Count>0){
            var node=stack.Pop();if(result.Any(x=>same(x,node)))continue;
            if(result.Count>=10000)throw new InvalidOperationException("Feature traversal safety limit exceeded; traversal incomplete");
            result.Add(node);foreach(var child in children(node).Reverse())stack.Push(child);
        }
        return result;
    }
    public static object Audit(List<CLBody> bodies,List<CLFolder> folders,bool traversalComplete){
        var issues=new List<object>();bool complete=traversalComplete&&folders.All(f=>f.bodyEnumerationSucceeded);
        foreach(var b in bodies)b.cutListIds.Clear();
        foreach(var f in folders){
            var unique=f.bodyIds.Distinct(StringComparer.Ordinal).ToArray();
            if(unique.Length!=f.bodyIds.Count){issues.Add(new{kind="DUPLICATE_WITHIN_FOLDER",folder=f.id});f.warnings.Add("Duplicate references inside GetBodies result");}
            f.physicalSolidBodyCount=f.bodyEnumerationSucceeded&&f.unknownBodies.Count==0&&unique.All(id=>bodies.Any(b=>b.id==id))?(int?)unique.Length:null;
            if(f.apiBodyCount.HasValue&&f.enumeratedBodyCount.HasValue&&f.apiBodyCount!=f.enumeratedBodyCount)issues.Add(new{kind="BODY_COUNT_MISMATCH",folder=f.id,api=f.apiBodyCount,enumerated=f.enumeratedBodyCount});
            foreach(var id in unique){var b=bodies.SingleOrDefault(x=>x.id==id);if(b==null){issues.Add(new{kind="UNKNOWN_BODY_ID",folder=f.id,body=id});complete=false;}else b.cutListIds.Add(f.id);}
            if(f.unknownBodies.Count>0){issues.Add(new{kind="UNKNOWN_FOLDER_BODY_REFERENCES",folder=f.id,count=f.unknownBodies.Count});complete=false;}
        }
        var unassigned=bodies.Where(b=>b.cutListIds.Count==0).Select(b=>new{b.id,b.name}).ToArray();
        var multiple=bodies.Where(b=>b.cutListIds.Count>1).Select(b=>new{b.id,b.name,folders=b.cutListIds}).ToArray();
        var duplicateNames=bodies.GroupBy(b=>b.name,StringComparer.Ordinal).Where(g=>g.Count()>1).Select(g=>new{name=g.Key,ids=g.Select(b=>b.id).ToArray()}).ToArray();
        bool noRepeated=multiple.Length==0&&!issues.Any(x=>Json.Serialize(x).Contains("DUPLICATE_WITHIN_FOLDER"));
        return new{enumeratedSolidBodyCount=bodies.Count,cutListFolderCount=folders.Count,traversalComplete,bodyMembershipReadsComplete=complete,
            accountedSolidBodyCount=bodies.Count(b=>b.cutListIds.Count>0),allSolidsAccounted=complete&&unassigned.Length==0,
            noRepeatedAccounting=noRepeated,unassignedBodies=unassigned,multipleMemberships=multiple,duplicateBodyNames=duplicateNames,issues,
            membershipConsistent=complete&&unassigned.Length==0&&noRepeated&&issues.Count==0,
            brepEquivalenceCertified=false,groupingMeaning="Existing SolidWorks grouping only. No manual body moves inferred; folder reorder does not determine membership."};
    }
    public static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=200};
    static string Cell(string s){if(s==null)s="НЕ ПРОВЕРЕНО";char q=(char)34;return q+s.Replace(q.ToString(),new string(q,2))+q;}
    public static string Csv(List<CLBody> bodies,List<CLFolder> folders){
        var propertyNames=folders.SelectMany(f=>f.properties.Select(p=>p.name)).Distinct(StringComparer.Ordinal).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var headers=new List<string>{"CutList_ID","CutList_Name","PhysicalSolidBodyCount","GetBodyCount","EnumeratedGetBodiesCount","GroupKind","BodyNames","BodyVisibility","Suppressed","ExcludedFromCutList","Warnings","Freshness"};
        foreach(var name in propertyNames)headers.AddRange(new[]{"Property:"+name+":Raw","Property:"+name+":Resolved","Property:"+name+":WasResolved","Property:"+name+":ResultCode"});
        var sb=new StringBuilder();sb.AppendLine(String.Join(";",headers.Select(Cell)));
        foreach(var f in folders){
            var members=f.bodyIds.Distinct().Select(id=>bodies.SingleOrDefault(b=>b.id==id)).Where(b=>b!=null).ToArray();
            var row=new List<string>{f.id,f.name,f.physicalSolidBodyCount.HasValue?f.physicalSolidBodyCount.Value.ToString():null,
                f.apiBodyCount.HasValue?f.apiBodyCount.Value.ToString():null,f.enumeratedBodyCount.HasValue?f.enumeratedBodyCount.Value.ToString():null,
                !f.physicalSolidBodyCount.HasValue?"UNKNOWN":f.physicalSolidBodyCount.Value==0?"EMPTY":f.physicalSolidBodyCount.Value==1?"SINGLE_BODY":"MULTI_BODY_GROUP",
                String.Join(" | ",members.Select(b=>b.name)),String.Join(" | ",members.Select(b=>b.name+": "+(b.visible.HasValue?b.visible.Value.ToString():"НЕ ПРОВЕРЕНО"))),
                f.suppressed==null?null:Json.Serialize(f.suppressed),f.excludedFromCutList==null?null:Json.Serialize(f.excludedFromCutList),String.Join(" | ",f.warnings),Json.Serialize(f.freshness)};
            foreach(var name in propertyNames){
                var p=f.properties.SingleOrDefault(x=>x.name==name);
                row.AddRange(p==null?new string[4]:new[]{p.rawValue,p.resolvedValue,p.wasResolved.HasValue?p.wasResolved.Value.ToString():null,p.resultCode.HasValue?p.resultCode.Value.ToString():null});
            }
            sb.AppendLine(String.Join(";",row.Select(Cell)));
        }
        return sb.ToString();
    }
}
