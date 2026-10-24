using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class ManufacturingContextV02Tests {
    public static int Run(){
        var tests=new List<object>();int failed=0;
        Action<string,Func<bool>> test=(name,f)=>{bool pass=false;string error=null;try{pass=f();}catch(Exception ex){error=ex.ToString();}if(!pass)failed++;tests.Add(new{name,passed=pass,error});};
        string p=@"D:\Проект\Деталь.SLDPRT";
        test("Cyrillic exact context",()=>ManufacturingContextV02.Matches(p,"По умолчанию",p,"По умолчанию"));
        test("Windows path case",()=>ManufacturingContextV02.SamePath(p,@"d:\проект\деталь.sldprt"));
        test("Path dot normalization",()=>ManufacturingContextV02.SamePath(p,@"D:\Проект\sub\..\Деталь.SLDPRT"));
        test("Slash normalization",()=>ManufacturingContextV02.SamePath(p,"D:/Проект/Деталь.SLDPRT"));
        test("Different model rejected",()=>!ManufacturingContextV02.SamePath(p,@"D:\Проект\Другая.SLDPRT"));
        test("Different directory rejected",()=>!ManufacturingContextV02.SamePath(p,@"E:\Проект\Деталь.SLDPRT"));
        test("Missing active path rejected",()=>!ManufacturingContextV02.SamePath(p,null));
        test("Unsaved model rejected",()=>!ManufacturingContextV02.SamePath("",""));
        test("Null expected rejected",()=>!ManufacturingContextV02.Matches(null,"x",p,"x"));
        test("Different configuration rejected",()=>!ManufacturingContextV02.Matches(p,"A",p,"B"));
        test("Configuration case retained",()=>!ManufacturingContextV02.Matches(p,"Default",p,"default"));
        test("Display label not silently accepted as API name",()=>!ManufacturingContextV02.Matches(p,"По умолчанию<As Machined>",p,"По умолчанию"));
        test("Literal decorated API name accepted",()=>ManufacturingContextV02.Matches(p,"По умолчанию<As Machined>",p,"По умолчанию<As Machined>"));
        test("Derived configuration must remain exact",()=>!ManufacturingContextV02.Matches(p,"Base",p,"BaseSM-FLAT-PATTERN"));
        test("Configuration whitespace not silently removed",()=>!ManufacturingContextV02.Matches(p,"Base",p,"Base "));
        test("Empty configuration rejected",()=>!ManufacturingContextV02.Matches(p,"",p,""));
        test("Arbitrary model accepted with explicit correct expectation",()=>ManufacturingContextV02.Matches(@"C:\Other\Another.SLDPRT","Manufactured",@"C:\Other\Another.SLDPRT","Manufactured"));
        test("Mismatch diagnostic contains expected actual and stage",()=>{var d=ManufacturingContextV02.Check("READ",p,"A",p,"B");return (string)d["stage"]=="READ"&&(string)d["expectedConfigurationApiName"]=="A"&&(string)d["actualConfigurationApiName"]=="B"&&!(bool)d["passed"];});
        test("No fixed body or folder count gate",()=>{string s=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ManufacturingPackageV02.cs"));return !s.Contains("bodies.Count!=20")&&!s.Contains("folders.Count!=11")&&!s.Contains("var sheetNames=")&&!s.Contains("SourcePath=@");});
        test("Diagnostic persists early-stop failure without production folder",()=>{string d=Path.Combine(Path.GetTempPath(),"mfg-context-test-"+Guid.NewGuid().ToString("N"));string f=ManufacturingContextV02.SaveDiagnostic(d,new{status="STOPPED_OR_INCOMPLETE",sourceContext=ManufacturingContextV02.Check("CAPTURE",p,"A",p,"B")});bool ok=File.Exists(f)&&File.ReadAllText(f).Contains("STOPPED_OR_INCOMPLETE");File.Delete(f);Directory.Delete(d);return ok;});
        test("Geometry regression closed line contour",()=>{string s=ManufacturingDxfV01.Line(new MPPoint(0,0),new MPPoint(10,0))+ManufacturingDxfV01.Line(new MPPoint(10,0),new MPPoint(0,10))+ManufacturingDxfV01.Line(new MPPoint(0,10),new MPPoint(0,0));return ManufacturingDxfV01.Check(ManufacturingDxfV01.Wrap(s)).accepted;});
        Console.WriteLine(CutListCoreV01.Json.Serialize(new{program="ManufacturingPackageV02",passed=tests.Count-failed,failed,tests,comCalls=0,sourceMutations=0}));return failed==0?0:1;
    }
}
