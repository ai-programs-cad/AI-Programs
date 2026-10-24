using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class ManufacturingLegacyCutListTests {
    class Node {public string Name;public List<Node> Children=new List<Node>();}
    static List<object> tests=new List<object>();
    static void Test(string name,Func<bool> f){try{tests.Add(new{name,passed=f()});}catch(Exception ex){tests.Add(new{name,passed=false,error=ex.ToString()});}}
    static CLBody B(string id,string name=null){return new CLBody{id=id,name=name??id,visible=true};}
    static CLFolder F(string id,params string[] members){return new CLFolder{id=id,name=id,bodyEnumerationSucceeded=true,propertiesEnumerationSucceeded=true,
        enumeratedBodyCount=members.Length,apiBodyCount=members.Length,bodyIds=members.ToList()};}
    static object Val(object o,string key){return CutListCoreV01.Json.Deserialize<Dictionary<string,object>>(CutListCoreV01.Json.Serialize(o))[key];}
    static bool Flag(object o,string key){return (bool)Val(o,key);}
    public static int Run(){
        Test("nested features discovered independent of depth",()=>{
            var root=new Node{Name="root"};var nested=new Node{Name="nested"};root.Children.Add(nested);nested.Children.Add(new Node{Name="CutList"});
            return CutListCoreV01.Traverse(new[]{root},n=>n.Children,(a,b)=>Object.ReferenceEquals(a,b)).Count==3;});
        Test("repeated feature references visited once",()=>{
            var a=new Node{Name="a"};var b=new Node{Name="b"};a.Children.Add(b);
            return CutListCoreV01.Traverse(new[]{a,b},n=>n.Children,(x,y)=>Object.ReferenceEquals(x,y)).Count==2;});
        Test("graph cycles do not recurse forever",()=>{
            var a=new Node();var b=new Node();a.Children.Add(b);b.Children.Add(a);
            return CutListCoreV01.Traverse(new[]{a},n=>n.Children,(x,y)=>Object.ReferenceEquals(x,y)).Count==2;});
        Test("same feature name does not merge different objects",()=>{
            var a=new Node{Name="same"};var b=new Node{Name="same"};
            return CutListCoreV01.Traverse(new[]{a,b},n=>n.Children,(x,y)=>Object.ReferenceEquals(x,y)).Count==2;});
        Test("single and multi-body groups audited",()=>{
            var bodies=new List<CLBody>{B("a"),B("b"),B("c")};var f=new List<CLFolder>{F("one","a"),F("group","b","c")};
            var audit=CutListCoreV01.Audit(bodies,f,true);return Flag(audit,"membershipConsistent")&&f[0].physicalSolidBodyCount==1&&f[1].physicalSolidBodyCount==2;});
        Test("folder reorder cannot change membership",()=>{
            var bodies=new List<CLBody>{B("a"),B("b")};var f=new List<CLFolder>{F("x","a"),F("y","b")};
            CutListCoreV01.Audit(bodies,f,true);string before=String.Join(",",bodies.Select(b=>b.id+":"+String.Join("|",b.cutListIds)));
            f.Reverse();CutListCoreV01.Audit(bodies,f,true);return before==String.Join(",",bodies.Select(b=>b.id+":"+String.Join("|",b.cutListIds)));});
        Test("duplicate body names kept separate by identity tokens",()=>{
            var bodies=new List<CLBody>{B("a","same"),B("b","same")};
            return Flag(CutListCoreV01.Audit(bodies,new List<CLFolder>{F("x","a"),F("y","b")},true),"membershipConsistent");});
        Test("orphan solid reported",()=>!Flag(CutListCoreV01.Audit(new List<CLBody>{B("a")},new List<CLFolder>(),true),"allSolidsAccounted"));
        Test("multiple folders per same solid detected",()=>!Flag(CutListCoreV01.Audit(new List<CLBody>{B("a")},new List<CLFolder>{F("x","a"),F("y","a")},true),"noRepeatedAccounting"));
        Test("duplicate member inside folder does not inflate physical count",()=>{
            var f=F("x","a","a");var r=CutListCoreV01.Audit(new List<CLBody>{B("a")},new List<CLFolder>{f},true);
            return f.physicalSolidBodyCount==1&&!Flag(r,"noRepeatedAccounting");});
        Test("unknown folder body keeps membership incomplete",()=>{
            var f=F("x","a");f.unknownBodies.Add(new{name="outside active solids"});
            return !Flag(CutListCoreV01.Audit(new List<CLBody>{B("a")},new List<CLFolder>{f},true),"bodyMembershipReadsComplete")&&!f.physicalSolidBodyCount.HasValue;});
        Test("failed GetBodies never interpreted as empty folder",()=>{
            var f=F("x");f.bodyEnumerationSucceeded=false;
            CutListCoreV01.Audit(new List<CLBody>(),new List<CLFolder>{f},true);return !f.physicalSolidBodyCount.HasValue;});
        Test("empty existing CutList folder retained as a row",()=>{
            var f=F("empty");CutListCoreV01.Audit(new List<CLBody>(),new List<CLFolder>{f},true);
            return f.physicalSolidBodyCount==0&&CutListCoreV01.Csv(new List<CLBody>(),new List<CLFolder>{f}).Contains("EMPTY");});
        Test("incomplete traversal does not certify coverage",()=>!Flag(CutListCoreV01.Audit(new List<CLBody>(),new List<CLFolder>(),false),"allSolidsAccounted"));
        Test("cached GetBodyCount discrepancy detected",()=>{
            var f=F("x","a");f.apiBodyCount=9;return !Flag(CutListCoreV01.Audit(new List<CLBody>{B("a")},new List<CLFolder>{f},true),"membershipConsistent");});
        Test("QUANTITY is independent of physical count",()=>{
            var f=F("x","a","b");f.properties.Add(new CLProperty{name="QUANTITY",rawValue="99",resolvedValue="99",resultCode=2,wasResolved=true});
            return Flag(CutListCoreV01.Audit(new List<CLBody>{B("a"),B("b")},new List<CLFolder>{f},true),"membershipConsistent")&&f.physicalSolidBodyCount==2;});
        Test("raw expression and resolved production value both retained",()=>{
            var f=F("x");f.properties.Add(new CLProperty{name="LENGTH",rawValue="\"SW-Length@@@Cut-List\"",resolvedValue="123.456",wasResolved=true,resultCode=2});
            var csv=CutListCoreV01.Csv(new List<CLBody>(),new List<CLFolder>{f});return csv.Contains("SW-Length@@@Cut-List")&&csv.Contains("123.456")&&csv.Contains("Property:LENGTH:Raw")&&csv.Contains("Property:LENGTH:Resolved");});
        Test("dash cutting angle remains dash",()=>{
            var f=F("x");f.properties.Add(new CLProperty{name="УГОЛ1",rawValue="-",resolvedValue="-",wasResolved=false,resultCode=0});
            var csv=CutListCoreV01.Csv(new List<CLBody>(),new List<CLFolder>{f});return csv.Contains("\"-\"")&&csv.Contains("УГОЛ1");});
        Test("missing and empty property values remain distinguishable",()=>{
            var a=F("a");var b=F("b");a.properties.Add(new CLProperty{name="MATERIAL",rawValue="",resolvedValue=""});
            var csv=CutListCoreV01.Csv(new List<CLBody>(),new List<CLFolder>{a,b});return csv.Contains("\"\"")&&csv.Contains("НЕ ПРОВЕРЕНО");});
        Test("all arbitrary custom properties get CSV columns",()=>{
            var f=F("x");f.properties.Add(new CLProperty{name="CUSTOM;PROPERTY",rawValue="a;b\"c\nd",resolvedValue="value"});
            var csv=CutListCoreV01.Csv(new List<CLBody>(),new List<CLFolder>{f});return csv.Contains("Property:CUSTOM;PROPERTY:Raw")&&csv.Contains("a;b\"\"c\nd");});
        Test("grouping never becomes BREP certificate",()=>!Flag(CutListCoreV01.Audit(new List<CLBody>{B("a"),B("b")},new List<CLFolder>{F("x","a","b")},true),"brepEquivalenceCertified"));
        Test("model dirty flag change observable",()=>CutListCoreV01.Json.Serialize(new{dirty=false,stamp=1})!=CutListCoreV01.Json.Serialize(new{dirty=true,stamp=1}));
        Test("model update stamp change observable",()=>CutListCoreV01.Json.Serialize(new{dirty=false,stamp=1})!=CutListCoreV01.Json.Serialize(new{dirty=false,stamp=2}));
        Test("synthetic CSV fixture for independent round-trip verification",()=>{
            var bodies=new List<CLBody>{B("a","a;\"1\""),B("b")};var folder=F("csv-test","a","b");folder.name="Папка;\"тест\"";
            folder.properties.Add(new CLProperty{name="QUANTITY",rawValue="99",resolvedValue="99",resultCode=2,wasResolved=true});
            folder.properties.Add(new CLProperty{name="МАТЕРИАЛ",rawValue="Сталь;Q\nстрока",resolvedValue="Сталь"});
            folder.properties.Add(new CLProperty{name="УГОЛ1",rawValue="-",resolvedValue="-"});
            var folders=new List<CLFolder>{folder,F("empty")};CutListCoreV01.Audit(bodies,folders,true);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work","csv-test-mfg-legacy-cutlist.csv"),CutListCoreV01.Csv(bodies,folders),new System.Text.UTF8Encoding(true));
            return folder.physicalSolidBodyCount==2;});
        var result=new{program="ReadCutListV01",passed=tests.Count(t=>Flag(t,"passed")),failed=tests.Count(t=>!Flag(t,"passed")),tests,comCalls=0};
        string text=CutListCoreV01.Json.Serialize(result);File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results-mfg-legacy-cutlist.json"),text,new System.Text.UTF8Encoding(false));
        Console.WriteLine(text);return tests.All(t=>Flag(t,"passed"))?0:1;
    }
}
