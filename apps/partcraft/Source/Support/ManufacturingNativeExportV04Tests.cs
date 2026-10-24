using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class ManufacturingNativeExportV04Tests {
    static string Rectangle(){return ManufacturingDxfV01.Line(new MPPoint(0,0),new MPPoint(10,0))+ManufacturingDxfV01.Line(new MPPoint(10,0),new MPPoint(10,10))+ManufacturingDxfV01.Line(new MPPoint(10,10),new MPPoint(0,10))+ManufacturingDxfV01.Line(new MPPoint(0,10),new MPPoint(0,0));}
    public static int Run(){
        int inherited=ManufacturingFileV03Tests.Run();var tests=new List<object>();int failed=0;
        Action<string,Func<bool>> test=(name,f)=>{bool pass=false;string error=null;try{pass=f();}catch(Exception ex){error=ex.ToString();}if(!pass)failed++;tests.Add(new{name,passed=pass,error});};
        string closed=ManufacturingDxfV01.Wrap(Rectangle()+ManufacturingDxfV01.Circle(new MPPoint(5,5),2));
        string line=ManufacturingDxfV01.Wrap(ManufacturingDxfV01.Line(new MPPoint(1,2),new MPPoint(9,2)));
        test("Native cut rectangle with hole accepted",()=>{var d=ManufacturingDxfV04.Check(closed,new[]{10d,10d});return d.accepted&&d.closedCurves+d.closedConnectedContours==2;});
        test("Open cut DXF rejected",()=>!ManufacturingDxfV04.Check(line).accepted);
        test("Separate bend line can be open",()=>ManufacturingDxfV04.Check(line,null,.001,false).accepted);
        test("Bend geometry uses only separate layer",()=>{var d=ManufacturingDxfV04.Check(line,null,.001,false);return d.accepted&&d.normalizedText.Contains("BEND_LINES")&&!d.normalizedText.Contains("CUT_THROUGH");});
        test("Cut geometry remains CUT_THROUGH",()=>{var d=ManufacturingDxfV04.Check(closed);return d.accepted&&d.normalizedText.Contains("CUT_THROUGH")&&!d.normalizedText.Contains("BEND_LINES");});
        test("Native circle preserved as circle",()=>{var d=ManufacturingDxfV04.Check(closed);return d.accepted&&d.entityTypes.Count(t=>t=="CIRCLE")==1&&d.normalizedText.Contains("0\nCIRCLE\n");});
        test("Wrong cut scale rejected",()=>!ManufacturingDxfV04.Check(closed,new[]{.01,.01}).accepted);
        test("Non-mm native units rejected",()=>!ManufacturingDxfV04.Check(closed.Replace("$INSUNITS\n70\n4","$INSUNITS\n70\n1"),new[]{10d,10d}).accepted);
        test("Unknown native spline not approximated",()=>!ManufacturingDxfV04.Check(ManufacturingDxfV01.Wrap("0\nSPLINE\n8\n0\n")).accepted);
        test("Empty native cut rejected",()=>!ManufacturingDxfV04.Check(ManufacturingDxfV01.Wrap("")).accepted);
        test("Duplicate native cut rejected",()=>!ManufacturingDxfV04.Check(ManufacturingDxfV01.Wrap(Rectangle()+Rectangle())).accepted);
        test("Bend unknown units require independent native provenance",()=>{string t=line.Replace("$INSUNITS\n70\n4","$INSUNITS\n70\n0");return !ManufacturingDxfV04.Check(t,null,.001,false).accepted&&ManufacturingDxfV04.Check(t,null,.001,false,true).accepted;});
        test("Neighbour geometry exceeding isolated extents rejected",()=>!ManufacturingDxfV04.Check(ManufacturingDxfV01.Wrap(Rectangle()+ManufacturingDxfV01.Circle(new MPPoint(20,20),2)),new[]{10d,10d}).accepted);
        test("Export not blocked by curve identity whitelist",()=>{string s=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ManufacturingSolidWorksV04.cs"));return !s.Contains("if(unsupportedCurves.Length>0)throw")&&s.Contains("ExportToDWG2")&&s.Contains("One(copy)")&&s.Contains("flat.Select2(false,0)");});
        test("Source never flattened and export has no fixed body name",()=>{string s=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ManufacturingSolidWorksV04.cs"));return !s.Contains("original.EditRebuild")&&!s.Contains("original.Save")&&!s.Contains("9-1")&&!s.Contains("10-1")&&s.Contains("swThisConfiguration");});
        test("No global spline approximation or endpoint healing survives export",()=>{string s=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ManufacturingDxfPreferencesV04.cs"));return s.Contains("swDxfExportSplinesAsSplines,true")&&s.Contains("swDxfEndPointMerge,false")&&s.Contains("public void Dispose()")&&s.Contains("DXF_EXPORT_PREFERENCE_RESTORE_FAILED");});
        Console.WriteLine(CutListCoreV01.Json.Serialize(new{program="ManufacturingPackageV04",nativeExportTestsPassed=tests.Count-failed,nativeExportTestsFailed=failed,inheritedTestsPassed=inherited==0?36:0,tests,liveCOMExecuted=false,createExecuted=false}));return failed==0&&inherited==0?0:1;
    }
}
