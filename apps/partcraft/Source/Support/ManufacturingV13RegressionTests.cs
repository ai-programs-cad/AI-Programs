using System;
using System.Linq;
using System.Collections.Generic;
public static class ManufacturingV13RegressionTests {
    public static int Run(){int inherited=ManufacturingConfigurationV13RegressionTests.Run();var tests=new List<object>();int failed=0;Action<string,Func<bool>> test=(name,f)=>{bool pass=false;try{pass=f();}catch{}if(!pass)failed++;tests.Add(new{name,passed=pass});};
        test("Snapshot dictionary order does not manufacture changes",()=>ManufacturingStateV09.Diff(new Dictionary<string,object>{{"a",1},{"b",2}},new Dictionary<string,object>{{"b",2},{"a",1}}).Count==0);
        test("Dirty change has exact field diff",()=>CutListCoreV01.Json.Serialize(ManufacturingStateV09.Diff(new{dirty=false},new{dirty=true})).Contains("dirty"));
        test("Revision differences are retained as safety relevant",()=>ManufacturingStateV09.Diff(new{updateStamp=1},new{updateStamp=2}).Count==1);
        test("Body visibility change is not ignored",()=>ManufacturingStateV09.Diff(new{bodies=new[]{new{visible=true}}},new{bodies=new[]{new{visible=false}}}).Count==1);
        test("No multibody filter with proven native single-body geometry can pass",()=>ManufacturingViewPolicyV09.Proven(true,true,true,true,true,20,0,0));
        test("Temporary drawing body name is not an identity criterion",()=>ManufacturingViewPolicyV09.Proven(true,true,true,true,true,20,1,1));
        test("Blank native view cannot pass on object existence",()=>!ManufacturingViewPolicyV09.Proven(true,true,true,true,true,0,0,0));
        test("Wrong geometry cannot pass ownership",()=>!ManufacturingViewPolicyV09.Proven(true,true,true,false,true,20,0,0));
        test("Wrong configuration cannot pass ownership",()=>!ManufacturingViewPolicyV09.Proven(true,false,true,true,true,20,0,0));
        test("Multiple displayed bodies remain incomplete",()=>!ManufacturingViewPolicyV09.Proven(true,true,true,true,true,20,2,2));
        test("ASCII-escaped full JSON preserves original Unicode values",()=>{string j=ManufacturingStateV09.Json(new{name="По умолчанию<As Machined>"});return j.All(c=>c<128)&&(string)AB.Obj(CutListCoreV01.Json.DeserializeObject(j))["name"]=="По умолчанию<As Machined>";});
        Console.WriteLine(CutListCoreV01.Json.Serialize(new{program="ManufacturingPackageV09",v09TestsPassed=tests.Count-failed,v09TestsFailed=failed,inheritedTestsPassed=inherited==0?115:0,tests,liveCOMExecuted=false}));return failed==0&&inherited==0?0:1;
    }
}
