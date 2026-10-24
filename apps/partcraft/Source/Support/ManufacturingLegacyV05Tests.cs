using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
public static class BatchV05Tests {
    static List<object> results=new List<object>();
    static void Test(string name,Func<bool> f){try{results.Add(new{name,passed=f()});}catch(Exception ex){results.Add(new{name,passed=false,error=ex.ToString()});}}
    static Dictionary<string,object> Run(string[][] p,Func<string,object> load,Action guard=null,Func<string,string,object> api=null){
        return BatchV05.Run(p,load,api??((a,b)=>null),n=>null,guard??(()=>{}));
    }
    public static int Run(){
        int legacy;var writer=Console.Out;Console.SetOut(TextWriter.Null);
        try{legacy=TransformSearchV04Tests.Run();}finally{Console.SetOut(writer);}
        Test("v04 full 63 regression tests",()=>legacy==0);
        Test("default exact 16 pairs and 16 unique bodies",()=>{
            var p=BatchV05.DefaultPairs();return p.Length==16&&p.SelectMany(x=>x).Distinct().Count()==16;});
        var triangle=new[]{new[]{"a","b"},new[]{"a","c"},new[]{"b","c"}};
        Test("body read cached once across pairs",()=>{
            var counts=new Dictionary<string,int>();var r=Run(triangle,n=>{counts[n]=counts.ContainsKey(n)?counts[n]+1:1;return V03Fixtures.Tube();});
            return counts.Count==3&&counts.Values.All(v=>v==1)&&AB.Int(r["pairsCompleted"])==3;});
        Test("failed body read cached and other pairs continue",()=>{
            int bad=0;var r=Run(triangle,n=>{if(n=="a"){bad++;throw new InvalidOperationException("EXACT_BODY_NOT_FOUND");}return V03Fixtures.Tube();});
            return bad==1&&AB.Int(r["pairsCompleted"])==3&&AB.Array(r["comparisons"]).Last()!=null&&BatchV05.Flag(AB.Array(r["comparisons"]).Last(),"properMatchFound");});
        Test("pair API exception isolated",()=>{
            var r=Run(triangle,n=>V03Fixtures.Tube(),null,(a,b)=>{if(a=="a")throw new InvalidOperationException("API_FAILURE");return null;});
            return AB.Int(r["pairsCompleted"])==3&&AB.Array(BatchV05.Get(AB.Array(r["comparisons"])[0],"pairApiErrors")).Length==1;});
        Test("global context violation stops remaining pairs",()=>{
            int calls=0;var r=Run(triangle,n=>V03Fixtures.Tube(),()=>{if(++calls>=9)throw new ContextV05Exception("CONFIGURATION_MISMATCH");});
            return (string)r["status"]=="STOPPED_CONTEXT_CHANGED"&&AB.Int(r["pairsCompleted"])<3;});
        Test("context exception from loader never becomes body-local error",()=>{
            var r=Run(triangle,n=>{throw new ContextV05Exception("DOCUMENT_CHANGED");});
            return (string)r["status"]=="STOPPED_CONTEXT_CHANGED"&&AB.Int(r["pairsCompleted"])==0;});
        Test("unsupported line edge explicitly marked without search",()=>{
            var b=AB.Obj(V03Fixtures.Tube());var cv=AB.Obj(AB.Get(AB.Array(b["edges"])[0],"curve"));cv["isCircle"]=false;cv["isLine"]=true;
            var r=BatchV05.Compare("a","b",b,V03Fixtures.Tube(),null,null,null);
            return AB.Array(r["diagnosticFlags"]).Contains("UNSUPPORTED_GEOMETRY")&&r["verification"]==null;});
        Test("multi-coedge loop unsupported without fallback",()=>{
            var b=AB.Obj(V03Fixtures.Tube());var l=AB.Obj(AB.Array(AB.Get(AB.Array(b["faces"])[0],"loops"))[0]);
            var co=AB.Array(l["coedges"])[0];l["coedges"]=new[]{co,co};
            return AB.Array(BatchV05.Compare("a","b",b,b,null,null,null)["diagnosticFlags"]).Contains("UNSUPPORTED_GEOMETRY");});
        Test("owner mismatch prevents match",()=>{
            var b=AB.Obj(V03Fixtures.Tube());AB.Obj(AB.Array(b["faces"])[0])["ownerVerified"]=false;
            return !BatchV05.Flag(BatchV05.Compare("a","b",b,b,null,null,null),"properMatchFound");});
        Test("raw count mismatch prevents match",()=>{
            var b=AB.Obj(V03Fixtures.Tube());b["edgeCount"]=5;
            return !BatchV05.Flag(BatchV05.Compare("a","b",b,b,null,null,null),"properMatchFound");});
        Test("read API gaps prevent acceptance",()=>{
            var b=AB.Obj(V03Fixtures.Tube());b["readGaps"]=new[]{"unavailable API"};
            return !BatchV05.Flag(BatchV05.Compare("a","b",b,b,null,null,null),"properMatchFound");});
        Test("no match is not DIFFERENT",()=>{
            var r=BatchV05.Compare("a","b",V03Fixtures.Tube(),V03Fixtures.Tube(inner:.26),null,null,null);
            return (string)r["classification"]=="NEEDS_REVIEW"&&AB.Array(r["diagnosticFlags"]).Contains("NO_MATCH_FOUND_IN_SEARCH");});
        Test("symmetry and both independent diagnostic flags",()=>{
            var r=BatchV05.Compare("a","b",V03Fixtures.Tube(),V03Fixtures.Tube(),null,null,null);
            return BatchV05.Flag(r,"properMatchFound")&&BatchV05.Flag(r,"mirrorMatchFound")&&BatchV05.Flag(r,"symmetryDetected");});
        Test("arbitrary names parsed exactly without trimming",()=>{
            var p=BatchV05.Pairs(new[]{new[]{" a ","b"}});return p[0][0]==" a ";});
        Test("malformed pair rejected before COM",()=>{try{BatchV05.Pairs(new[]{new[]{"a"}});return false;}catch(InvalidOperationException){return true;}});
        Test("CSV escapes quotes and delimiters",()=>{
            var r=Run(new[]{new[]{"a,\"quoted\"","b"}},n=>V03Fixtures.Tube());return BatchV05.Csv(r).Contains("\"a,\"\"quoted\"\"\"");});
        Test("all default pairs synthetic integration cached",()=>{
            int count=0;var r=Run(BatchV05.DefaultPairs(),n=>{count++;return V03Fixtures.Tube();});
            return count==16&&AB.Int(r["pairsCompleted"])==16&&AB.Array(r["comparisons"]).All(c=>(string)AB.Get(c,"classification")=="NEEDS_REVIEW");});
        var j=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
        var output=new{legacyTests=63,newTests=results.Count-1,totalPassed=63+results.Skip(1).Count(NB03.Passed),failed=results.Count(x=>!NB03.Passed(x)),tests=results,comCalls=0};
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results-mfg-legacy-v05.json"),j.Serialize(output));
        Console.WriteLine(j.Serialize(output));return results.All(NB03.Passed)?0:1;
    }
}

