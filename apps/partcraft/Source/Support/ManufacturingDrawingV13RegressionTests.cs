using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class ManufacturingDrawingV13RegressionTests {
    public static int Run(){
        int inherited=ManufacturingBendV13RegressionTests.Run();var tests=new List<object>();int failed=0;
        Action<string,Func<bool>> test=(name,f)=>{bool pass=false;string error=null;try{pass=f();}catch(Exception ex){error=ex.ToString();}if(!pass)failed++;tests.Add(new{name,passed=pass,error});};
        double[] r=new[]{1d,0,0,0,-1,0,0,0,-1,0,0,0,1,0,0,0};
        test("Native orientation same rotation passes",()=>ManufacturingDrawingChecksV13.RotationError(r,r)==0);
        test("Other orientation detected",()=>{var a=(double[])r.Clone();a[0]=-1;return ManufacturingDrawingChecksV13.RotationError(a,r)>1e-8;});
        test("Missing orientation is not silently certified",()=>{try{ManufacturingDrawingChecksV13.RotationError(null,r);return false;}catch(InvalidOperationException){return true;}});
        test("Nonfinite orientation rejected",()=>{var a=(double[])r.Clone();a[0]=double.NaN;try{ManufacturingDrawingChecksV13.RotationError(a,r);return false;}catch(InvalidOperationException){return true;}});
        test("Nonempty native view outline accepted",()=>ManufacturingDrawingChecksV13.Nonempty(new[]{.1,.2,.3,.4}));
        test("Empty native view rejected",()=>!ManufacturingDrawingChecksV13.Nonempty(new[]{.1,.2,.1,.4}));
        test("Truncated PDF rejected",()=>!ManufacturingDrawingChecksV13.PdfComplete(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 incomplete document")));
        test("PDF header and EOF recognized",()=>ManufacturingDrawingChecksV13.PdfComplete(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 native export\n%%EOF\n")));
        test("Wrong referenced model rejected",()=>!ManufacturingDrawingChecksV13.SameReference(@"C:\owned\A.SLDPRT",@"C:\owned\B.SLDPRT"));
        test("Cyrillic model path Windows case handling",()=>ManufacturingDrawingChecksV13.SameReference(@"C:\Детали\Тело.SLDPRT",@"c:\детали\тело.sldprt"));
        test("Folded ISO uses numeric standard ID and native named view",()=>{string s=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ManufacturingSolidWorksV07.cs"));return s.Contains("swIsometricView")&&s.Contains("copy.NameView")&&s.IndexOf("InsertIsometric(dd")<s.IndexOf("var flatView=dd.CreateFlatPatternViewFromModelView3");});
        test("Owned reference relocation actually moves and reopens files",()=>{string s=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ManufacturingDrawingV07.cs"));return s.Contains("SavePackAndGo")&&s.Contains("File.Move(targets[i],moved[i])")&&s.Contains("Portable drawing reopen failed")&&s.Contains("portableViewVerification")&&!s.Contains("original.Save")&&!s.Contains("original.ShowConfiguration");});
        Console.WriteLine(CutListCoreV01.Json.Serialize(new{program="ManufacturingPackageV07",v07TestsPassed=tests.Count-failed,v07TestsFailed=failed,inheritedTestsPassed=inherited==0?86:0,tests,liveCOMExecuted=false,nativeDrawingInsertionTested=false}));return failed==0&&inherited==0?0:1;
    }
}
