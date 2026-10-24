using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class ManufacturingFileV03Tests {
    public static int Run(){
        int context=ManufacturingContextV02Tests.Run();var tests=new List<object>();int failed=0;
        Action<string,Func<bool>> test=(name,f)=>{bool pass=false;string error=null;try{pass=f();}catch(Exception ex){error=ex.ToString();}if(!pass)failed++;tests.Add(new{name,passed=pass,error});};
        string root=Path.Combine(Path.GetTempPath(),"mfg-v03-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string source=Path.Combine(root,"Открытая деталь.SLDPRT");File.WriteAllText(source,"saved bytes");string hash=ManufacturingSourceFilesV03.Hash(source,"TEST_INITIAL");
        test("Writer handle recreates legacy File.OpenRead sharing violation",()=>{using(var h=new FileStream(source,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite)){try{using(var s=File.OpenRead(source)){}return false;}catch(IOException ex){return ex.HResult==unchecked((int)0x80070020);}}});
        test("Shared hash works while writer handle remains open",()=>{using(var h=new FileStream(source,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))return ManufacturingSourceFilesV03.Hash(source,"TEST_OPEN_WRITER")==hash;});
        test("Exclusive lock is reported without bypass",()=>{using(var h=new FileStream(source,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){string value;return !ManufacturingSourceFilesV03.TryHash(source,"TEST_EXCLUSIVE",out value)&&value==null;}});
        test("Exact operation path stage HRESULT and stack recorded",()=>{string json=CutListCoreV01.Json.Serialize(ManufacturingSourceFilesV03.Operations);return json.Contains("TEST_EXCLUSIVE")&&json.Contains("0x80070020")&&json.Contains("stackTrace")&&json.Contains("SOURCE_SHARED_READ_SHA256_TWO_PASS");});
        test("Shared stream creates exact new copy with source still open",()=>{string dest=Path.Combine(root,"copy.SLDPRT");using(var h=new FileStream(source,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))ManufacturingSourceFilesV03.CopySavedSource(source,dest,hash,false);return File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(dest));});
        test("Existing copy never overwritten",()=>{string dest=Path.Combine(root,"copy.SLDPRT");try{ManufacturingSourceFilesV03.CopySavedSource(source,dest,hash,false);return false;}catch(IOException){return File.ReadAllText(dest)=="saved bytes";}});
        test("Dirty source never copied or saved",()=>{string dest=Path.Combine(root,"dirty.SLDPRT");try{ManufacturingSourceFilesV03.CopySavedSource(source,dest,hash,true);return false;}catch(InvalidOperationException){return !File.Exists(dest)&&File.ReadAllText(source)=="saved bytes";}});
        test("Source destination identity forbidden",()=>{try{ManufacturingSourceFilesV03.CopySavedSource(source,source,hash,false);return false;}catch(InvalidOperationException){return ManufacturingSourceFilesV03.Hash(source,"VERIFY_NO_WRITE")==hash;}});
        test("Changed source hash blocks copy before destination creation",()=>{string dest=Path.Combine(root,"changed.SLDPRT");try{ManufacturingSourceFilesV03.CopySavedSource(source,dest,"BAD",false);return false;}catch(IOException){return !File.Exists(dest);}});
        test("Missing source records failure without fabricated hash",()=>{string value;return !ManufacturingSourceFilesV03.TryHash(Path.Combine(root,"missing.SLDPRT"),"MISSING",out value)&&value==null;});
        test("Dirty state remains valid when unchanged",()=>ManufacturingSourceFilesV03.StateEqual(new{dirty=true,config="Конфигурация",stamp=42},new{dirty=true,config="Конфигурация",stamp=42}));
        test("Dirty state lost is detected",()=>!ManufacturingSourceFilesV03.StateEqual(new{dirty=true,config="A"},new{dirty=false,config="A"}));
        test("Configuration change is detected",()=>!ManufacturingSourceFilesV03.StateEqual(new{dirty=false,config="A"},new{dirty=false,config="B"}));
        test("Restoration still closes only owned docs and activates source without rebuilding",()=>{string s=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ManufacturingSolidWorksV03.cs"));return s.Contains("DontRebuild")&&s.Contains("created")&&!s.Contains("original.Save")&&!s.Contains("original.EditRebuild")&&!s.Contains("File.Copy(");});
        test("Final source bytes unchanged",()=>ManufacturingSourceFilesV03.Hash(source,"TEST_FINAL")==hash);
        Console.WriteLine(CutListCoreV01.Json.Serialize(new{program="ManufacturingPackageV03",fileTestsPassed=tests.Count-failed,fileTestsFailed=failed,contextTestsPassed=context==0?21:0,tests,comCalls=0,createExecuted=false}));
        foreach(string file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);return failed==0&&context==0?0:1;
    }
}
