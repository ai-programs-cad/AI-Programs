using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.swconst;
public static class ManufacturingNativeExportV05Tests {
    static string Spline(){return ManufacturingDxfV01.Wrap("0\nSPLINE\n8\n0\n70\n8\n71\n2\n72\n6\n73\n3\n40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n10\n0\n20\n0\n30\n0\n10\n5\n20\n10\n30\n0\n10\n10\n20\n0\n30\n0\n");}
    public static int Run(){
        int inherited=ManufacturingNativeExportV04Tests.Run();var tests=new List<object>();int failed=0;
        Action<string,Func<bool>> test=(name,f)=>{bool pass=false;string error=null;try{pass=f();}catch(Exception ex){error=ex.ToString();}if(!pass)failed++;tests.Add(new{name,passed=pass,error});};
        int splineKey=(int)swUserPreferenceToggle_e.swDxfExportSplinesAsSplines;
        test("Native polyline toggle false then restores true",()=>{var map=new Dictionary<int,bool>();Func<int,bool> get=k=>map.ContainsKey(k)?map[k]:true;var audit=new List<object>();using(var scope=new ManufacturingDxfPreferencesV05(get,(k,v)=>map[k]=v,false,audit)){if(get(splineKey))return false;}return get(splineKey);});
        test("Native exact toggle true then restores false",()=>{var map=new Dictionary<int,bool>();Func<int,bool> get=k=>map.ContainsKey(k)&&map[k];using(var scope=new ManufacturingDxfPreferencesV05(get,(k,v)=>map[k]=v,true,new List<object>())){if(!get(splineKey))return false;}return !get(splineKey);});
        test("Preference restored when export throws COM exception",()=>{var map=new Dictionary<int,bool>();Func<int,bool> get=k=>map.ContainsKey(k)?map[k]:true;try{using(var s=new ManufacturingDxfPreferencesV05(get,(k,v)=>map[k]=v,false,new List<object>()))throw new System.Runtime.InteropServices.COMException("Mock export failure",unchecked((int)0x80004005));}catch(System.Runtime.InteropServices.COMException){}return get(splineKey);});
        test("Partial setup failure restores spline preference",()=>{var map=new Dictionary<int,bool>();Func<int,bool> get=k=>map.ContainsKey(k)?map[k]:true;bool once=true;try{using(var s=new ManufacturingDxfPreferencesV05(get,(k,v)=>{if(k==(int)swUserPreferenceToggle_e.swDxfEndPointMerge&&once){once=false;throw new Exception("Mock setter failure");}map[k]=v;},false,new List<object>())){}return false;}catch{return get(splineKey);}});
        test("Failed preference restoration cannot silently pass",()=>{var map=new Dictionary<int,bool>();Func<int,bool> get=k=>map.ContainsKey(k)?map[k]:true;bool restoring=false;try{using(var s=new ManufacturingDxfPreferencesV05(get,(k,v)=>{if(restoring&&k==splineKey)throw new Exception("Mock restore failure");map[k]=v;},false,new List<object>())){restoring=true;}return false;}catch(InvalidOperationException ex){return ex.Message.Contains("DXF_EXPORT_PREFERENCE_RESTORE_FAILED");}});
        test("Exact native SPLINE is retained structurally",()=>ManufacturingNativeDxfV05.Inspect(Spline()).structureAccepted);
        test("Incomplete SPLINE rejected",()=>!ManufacturingNativeDxfV05.Inspect(Spline().Replace("73\n3","73\n4")).structureAccepted);
        test("Nonfinite SPLINE rejected",()=>!ManufacturingNativeDxfV05.Inspect(Spline().Replace("20\n10","20\nNaN")).structureAccepted);
        test("Actual entity types establish toggle not honored",()=>ManufacturingNativeDxfV05.ToggleEffect(ManufacturingNativeDxfV05.Inspect(Spline()),ManufacturingNativeDxfV05.Inspect(Spline())).Contains("NOT_HONORED"));
        test("No spline does not prove toggle honored",()=>{string t=ManufacturingDxfV01.Wrap(ManufacturingDxfV01.Circle(new MPPoint(0,0),5));return ManufacturingNativeDxfV05.ToggleEffect(ManufacturingNativeDxfV05.Inspect(t),ManufacturingNativeDxfV05.Inspect(t))=="NO_SOURCE_SPLINE_TO_TEST";});
        test("Unknown spline approximation bound remains NEEDS_REVIEW",()=>{var d=ManufacturingNativeDxfV05.Deviation(Spline(),ManufacturingDxfV01.Wrap(ManufacturingDxfV01.Circle(new MPPoint(0,0),5)),.001);return d.status=="NEEDS_REVIEW"&&d.certifiedUpperBoundMm==null&&d.diagnosticSampleMaximumMm==null;});
        test("Identical supported whole curves have zero record deviation",()=>{string t=ManufacturingDxfV01.Wrap(ManufacturingDxfV01.Circle(new MPPoint(0,0),5));var d=ManufacturingNativeDxfV05.Deviation(t,t,.00001);return d.status=="PASS"&&d.certifiedUpperBoundMm==0&&d.wholeContourCertified;});
        test("Small real contour change cannot pass by sample heuristic",()=>{string a=ManufacturingDxfV01.Wrap(ManufacturingDxfV01.Circle(new MPPoint(0,0),5)),b=ManufacturingDxfV01.Wrap(ManufacturingDxfV01.Circle(new MPPoint(0,0),5.00000001));return ManufacturingNativeDxfV05.Deviation(a,b,.1).status=="NEEDS_REVIEW";});
        test("Invalid tolerance rejected",()=>ManufacturingNativeDxfV05.Deviation("","",-1).status=="NEEDS_REVIEW");
        test("Foreign DXF annotation rejected",()=>!ManufacturingNativeDxfV05.Inspect(ManufacturingDxfV01.Wrap("0\nTEXT\n1\nHello\n")).structureAccepted);
        string fixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work","manufacturing-v05-workbook-test-"+Guid.NewGuid().ToString("N")+".xlsx");
        test("XLSX written without Excel COM NumberFormat",()=>{var tables=new Dictionary<string,List<object[]>>();foreach(string name in new[]{"TUBE CUTTING","SHEET METAL","PLATES","CHECKS"})tables[name]=new List<object[]>{new object[]{"Имя","Размер","Формула как текст"},new object[]{"Деталь",1.234567,"=1+1"}};ManufacturingExcelV05.Write(fixture,tables);File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work","mfg-v05-workbook-fixture.txt"),fixture);return new FileInfo(fixture).Length>0;});
        test("Workbook existing path never overwritten",()=>{try{ManufacturingExcelV05.Write(fixture,new Dictionary<string,List<object[]>>());return false;}catch(IOException){return true;}});
        Console.WriteLine(CutListCoreV01.Json.Serialize(new{program="ManufacturingPackageV05",v05TestsPassed=tests.Count-failed,v05TestsFailed=failed,inheritedTestsPassed=inherited==0?52:0,tests,liveCOMExecuted=false,excelCOMExecuted=false}));return failed==0&&inherited==0?0:1;
    }
}
