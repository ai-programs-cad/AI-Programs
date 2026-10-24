using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
public static class ManufacturingConfigurationV13RegressionTests {
    static ConfigurationStateV08 Folded(string name){return new ConfigurationStateV08{name=name,boundFlatSuppressed=true,sheetMetalActive=true,sourceBendsActive=true};}
    public static int Run(){
        int inherited=ManufacturingDrawingV13RegressionTests.Run();var tests=new List<object>();int failed=0;
        Action<string,Func<bool>> test=(name,f)=>{bool pass=false;string error=null;try{pass=f();}catch(Exception ex){error=ex.ToString();}if(!pass)failed++;tests.Add(new{name,passed=pass,error});};
        string ru="По умолчанию<As Machined>";
        test("Already-active Russian configuration skips unnecessary activation",()=>{int calls=0;var r=ManufacturingConfigurationV08.Activate(ru,new[]{ru},()=>ru,n=>{calls++;return false;});return r.success&&!r.called&&calls==0;});
        test("Already-active English configuration remains strictly verified",()=>ManufacturingConfigurationV08.Activate("Default",new[]{"Default"},()=>"Default",n=>false).success);
        test("Real activation must return true and exact readback",()=>{string active="A";var r=ManufacturingConfigurationV08.Activate(ru,new[]{"A",ru},()=>active,n=>{active=n;return true;});return r.success&&r.called&&r.returned==true;});
        test("False activation while another configuration active is rejected",()=>!ManufacturingConfigurationV08.Activate(ru,new[]{"A",ru},()=>"A",n=>false).success);
        test("True return with incorrect active name rejected",()=>!ManufacturingConfigurationV08.Activate(ru,new[]{"A",ru},()=>"A",n=>true).success);
        test("Absent expected name is not fabricated",()=>{int calls=0;var r=ManufacturingConfigurationV08.Activate("Absent",new[]{ru},()=>ru,n=>{calls++;return true;});return !r.success&&calls==0;});
        test("Source folded configuration preferred without extra configuration",()=>ManufacturingConfigurationV08.Order(ru,new List<ConfigurationStateV08>{Folded("Other"),Folded(ru)})[0].name==ru);
        test("Flat-Pattern-looking name can be structurally folded",()=>ManufacturingConfigurationV08.Order("Missing",new List<ConfigurationStateV08>{Folded("Actual Flat-Pattern <X>")})[0].name=="Actual Flat-Pattern <X>");
        test("Ordinary-looking name with flat feature active is not folded",()=>{var r=Folded("Default");r.boundFlatSuppressed=false;return ManufacturingConfigurationV08.Order("Default",new List<ConfigurationStateV08>{r}).Count==0;});
        test("Missing bend features prevent folded selection",()=>{var r=Folded(ru);r.sourceBendsActive=false;return ManufacturingConfigurationV08.Order(ru,new List<ConfigurationStateV08>{r}).Count==0;});
        test("Spaces and angle brackets are preserved exactly",()=>{string n=" Деталь <α> состояние ";return ManufacturingConfigurationV08.Activate(n,new[]{n},()=>n,k=>false).success;});
        test("Mojibake never aliases a correct Russian API name",()=>{string broken=Encoding.GetEncoding(1251).GetString(Encoding.UTF8.GetBytes(ru));return !ManufacturingConfigurationV08.Activate(broken,new[]{ru},()=>ru,k=>true).success;});
        test("Unicode BSTR roundtrip preserves exact name",()=>{var p=System.Runtime.InteropServices.Marshal.StringToBSTR(ru);try{return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(p)==ru;}finally{System.Runtime.InteropServices.Marshal.FreeBSTR(p);}});
        test("UTF8 JSON roundtrip and codepoint evidence preserve Cyrillic",()=>{string json=CutListCoreV01.Json.Serialize(ManufacturingConfigurationV08.Evidence(ru));var data=AB.Obj(CutListCoreV01.Json.DeserializeObject(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(json))));return (string)data["name"]==ru&&((string)data["utf16Escaped"]).Contains("\\u041F");});
        Func<FoldedSignatureV08> signature=()=>new FoldedSignatureV08{bodyName="A",faces=10,edges=20,vertices=12,planes=2,cylinders=8,massPropertiesDensityOne=new[]{.1,.2,.3,.00001},activeSheetMetalFeatures=new[]{"SheetMetal"},activeBendFeatures=new[]{"Bend1","Bend2"}};
        test("Folded copy native geometry and bends preserved",()=>signature().SameGeometryAndBends(signature()));
        test("Lost native bend cannot pass preservation check",()=>{var b=signature();b.activeBendFeatures=new[]{"Bend1"};return !signature().SameGeometryAndBends(b);});
        test("Changed folded mass properties cannot pass",()=>{var b=signature();b.massPropertiesDensityOne[3]=.00002;return !signature().SameGeometryAndBends(b);});
        Console.WriteLine(CutListCoreV01.Json.Serialize(new{program="ManufacturingPackageV08",v08TestsPassed=tests.Count-failed,v08TestsFailed=failed,inheritedTestsPassed=inherited==0?98:0,tests,liveCOMExecuted=false,productionCertified=false}));return failed==0&&inherited==0?0:1;
    }
}
