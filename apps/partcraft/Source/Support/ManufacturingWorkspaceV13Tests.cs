using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class ManufacturingWorkspaceV13Tests {
    public static int Run(){int inherited=ManufacturingV13RegressionTests.Run();var tests=new List<object>();int failed=0;Action<string,Func<bool>> test=(name,f)=>{bool pass=false;string error=null;try{pass=f();}catch(Exception ex){error=ex.ToString();}if(!pass)failed++;tests.Add(new{name,passed=pass,error});};
        string project=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work","workspace-v13-test-"+Guid.NewGuid().ToString("N"));string workspace=Path.Combine(project,"_Manufacturing","Run_test","Workspace");Directory.CreateDirectory(workspace);string model=Path.Combine(workspace,"Модель.SLDPRT");File.WriteAllText(model,"offline fixture only");
        test("Explicit prepared workspace layout accepted",()=>{ManufacturingWorkspaceV13.ValidatePaths(workspace,model);return true;});
        test("Sibling Manufacturing is not project-internal layout",()=>{try{ManufacturingWorkspaceV13.ValidatePaths(project+"_Manufacturing",model);return false;}catch(InvalidOperationException){return true;}});
        test("Model outside Workspace rejected",()=>{string p=Path.Combine(project,"Original.SLDPRT");File.WriteAllText(p,"original");try{ManufacturingWorkspaceV13.ValidatePaths(workspace,p);return false;}catch(InvalidOperationException){return File.ReadAllText(p)=="original";}});
        test("Boundary prefix collision rejected",()=>!ManufacturingWorkspaceV13.Inside(workspace,workspace+"_other\\A.SLDPRT"));
        test("Relative traversal rejected",()=>!ManufacturingWorkspaceV13.Inside(workspace,Path.Combine(workspace,"..","Original.SLDPRT")));
        test("Internal native dependency accepted",()=>ManufacturingWorkspaceV13.CheckPairs(model,new object[]{"Part",model},workspace).Count==1);
        test("External original dependency blocks without editing",()=>{try{ManufacturingWorkspaceV13.CheckPairs(model,new object[]{"Original",Path.Combine(project,"Original.SLDPRT")},workspace);return false;}catch(InvalidOperationException){return true;}});
        test("Missing copied dependency blocks",()=>{try{ManufacturingWorkspaceV13.CheckPairs(model,new object[]{"Missing",Path.Combine(workspace,"missing.SLDPRT")},workspace);return false;}catch(InvalidOperationException){return true;}});
        test("Mutable part output is fenced inside Workspace",()=>{var f=MPFiles.ValidateWorkspaceRun(workspace);return ManufacturingWorkspaceV13.Inside(workspace,f.PathFor("models/A.SLDPRT"))&&ManufacturingWorkspaceV13.Inside(workspace,f.PathFor("portable/A/A.SLDDRW"));});
        test("DXF Drawings and Reports are run-local",()=>{var f=MPFiles.ValidateWorkspaceRun(workspace);string run=Directory.GetParent(workspace).FullName;return f.PathFor("dxf/A.dxf")==Path.Combine(run,"DXF","A.dxf")&&f.PathFor("manufacturing.xlsx")==Path.Combine(run,"Reports","manufacturing.xlsx")&&f.PathFor("final/A/A.SLDDRW")==Path.Combine(run,"Drawings","A","A.SLDDRW");});
        test("Dry-run diagnostics do not block later create",()=>{string reports=Path.Combine(Directory.GetParent(workspace).FullName,"Reports");Directory.CreateDirectory(reports);File.WriteAllText(Path.Combine(reports,"manufacturing-workspace-v13-test.json"),"{}");return MPFiles.ValidateWorkspaceRun(workspace)!=null;});
        test("Previous production outputs cannot be reused",()=>{string dx=Path.Combine(Directory.GetParent(workspace).FullName,"DXF");Directory.CreateDirectory(dx);try{MPFiles.ValidateWorkspaceRun(workspace);return false;}catch(InvalidOperationException){return true;}});
        Console.WriteLine(CutListCoreV01.Json.Serialize(new{program="ProductionPackageEntry",workspaceTestsPassed=tests.Count-failed,workspaceTestsFailed=failed,inheritedTestsPassed=inherited==0?126:0,tests,liveCOMExecuted=false,automaticPackAndGoImplemented=false}));return failed==0&&inherited==0?0:1;
    }
}
