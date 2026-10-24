using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class MappingTestsV03 {
    sealed class Fixture {
        public string path,files; public int index=1; public int fileWrites;
        public Dictionary<int,bool> toggles=new Dictionary<int,bool>{{8,true},{21,false},{358,false},{316,true}};
        public List<object> audit=new List<object>();public GroupMappingIOV02 io;
        public Fixture(){
            Directory.CreateDirectory("work");path=Path.GetFullPath(Path.Combine("work","mapping-fixture-"+Guid.NewGuid().ToString("N")));
            File.WriteAllText(path,"[Version]\r\nSW990001\r\n");files=path+"\n"+Path.Combine(Path.GetDirectoryName(path),"OtherMapping")+"\n";
            io=new GroupMappingIOV02{getFiles=k=>files,setFiles=(k,v)=>{fileWrites++;files=v;},getInteger=k=>k==2?index:0,setInteger=(k,v)=>{index=v;return true;},getToggle=k=>toggles[k],setToggle=(k,v)=>toggles[k]=v};
        }
        public bool Restored(string oldFiles){return files==oldFiles&&index==1&&toggles[8]&&!toggles[21]&&!toggles[358]&&toggles[316];}
        public GroupMappingV02 Open(){return new GroupMappingV02(io,path,audit);}
    }
    static void Check(bool ok){if(!ok)throw new Exception("Regression assertion failed");}
    public static int Run(){int count=0;Action<string,Action<Fixture>> test=(name,run)=>{var f=new Fixture();run(f);count++;Console.WriteLine(name+": PASS");};
        test("Existing native LF list reused without rewriting",f=>{string before=f.files;using(var s=f.Open()){Check(f.fileWrites==0&&f.index==0&&f.toggles[8]&&f.toggles[21]&&f.toggles[358]&&!f.toggles[316]);}Check(f.Restored(before));});
        test("Native list terminal LF is not a path mismatch",f=>Check(GroupMappingV02.FilesEqual(f.path,f.path+"\n")));
        test("CRLF native list is parsed as entries",f=>Check(GroupMappingV02.FilesEqual(f.path+"\r\n",f.path+"\n")));
        test("Path case and separators use Windows path semantics",f=>Check(GroupMappingV02.FilesEqual(f.path.ToUpperInvariant().Replace('\\','/'),f.path)));
        test("Another mapping file is never accepted",f=>Check(!GroupMappingV02.FilesEqual(f.path,Path.Combine(Path.GetDirectoryName(f.path),"different"))));
        test("List entry order remains significant",f=>Check(!GroupMappingV02.FilesEqual(f.path+"\nOther", "Other\n"+f.path)));
        test("Append missing mapping preserves user list",f=>{f.files="A\nB\n";string before=f.files;using(var s=f.Open()){Check(f.index==2&&GroupMappingV02.ParseFiles(f.files).Length==3);}Check(f.Restored(before));});
        test("Selected existing mapping uses its actual index",f=>{f.files="Other\n"+f.path+"\n";string before=f.files;using(var s=f.Open()){Check(f.index==1&&f.fileWrites==0);}Check(f.Restored(before));});
        test("Canonical LF readback after list setter passes",f=>{f.files="Other";string before=f.files;f.io.setFiles=(k,v)=>{f.fileWrites++;f.files=v.TrimEnd('\n')+"\n";};using(var s=f.Open()){Check(f.index==1);}Check(GroupMappingV02.FilesEqual(f.files,before)&&f.index==1);});
        test("COM-like export error still restores six settings",f=>{string before=f.files;try{using(var s=f.Open())throw new InvalidOperationException("export failure");}catch(InvalidOperationException){}Check(f.Restored(before));});
        test("Mapping toggle refusal identified and restored",f=>{string before=f.files;f.toggles[8]=false;var setter=f.io.setToggle;f.io.setToggle=(k,v)=>{if(k!=8||!v)setter(k,v);};try{f.Open();throw new Exception("Refusal accepted");}catch(InvalidOperationException ex){Check(ex.Message.Contains("swDxfMapping"));}Check(!f.toggles[8]&&f.index==1&&!f.toggles[21]&&!f.toggles[358]&&f.toggles[316]&&f.files==before);});
        test("Integer setter false is recorded and cannot pass",f=>{string before=f.files;var setter=f.io.setInteger;f.io.setInteger=(k,v)=>{setter(k,v);return v!=0;};try{f.Open();throw new Exception("False accepted");}catch(InvalidOperationException ex){Check(ex.Message.Contains("setter returned false"));}Check(f.Restored(before));});
        test("Partial COM setter exception restored",f=>{string before=f.files;var setter=f.io.setToggle;bool once=true;f.io.setToggle=(k,v)=>{setter(k,v);if(k==21&&v&&once){once=false;throw new Exception("COM setter exception");}};try{f.Open();throw new Exception("Exception ignored");}catch(InvalidOperationException ex){Check(ex.Message.Contains("swDXFDontShowMap"));}Check(f.Restored(before));});
        test("Cross-setting mapping reset caught by final check",f=>{string before=f.files;var setter=f.io.setToggle;f.toggles[8]=false;f.io.setToggle=(k,v)=>{setter(k,v);if(k==8&&v)f.index=1;};try{f.Open();throw new Exception("Cross-reset accepted");}catch(InvalidOperationException ex){Check(ex.Message.Contains("beforeExport")||ex.Message.Contains("Selected mapping"));}Check(f.files==before&&f.index==1&&!f.toggles[8]);});
        test("Restoration failures remain failures",f=>{var s=f.Open();f.io.setInteger=(k,v)=>{throw new Exception("Restore failure");};try{s.Dispose();throw new Exception("Restore error ignored");}catch(InvalidOperationException ex){Check(ex.Message.Contains("NATIVE_DXF_MAPPING_RESTORE_FAILED"));}});
        test("Dispose is idempotent",f=>{string before=f.files;var s=f.Open();s.Dispose();s.Dispose();Check(f.Restored(before));});
        test("Per-setting evidence includes API ID values and restore",f=>{using(var s=f.Open()){}var records=f.audit.OfType<Dictionary<string,object>>().ToArray();Check(records.Length==6&&records.All(r=>r.ContainsKey("parameterId")&&r.ContainsKey("getterApi")&&r.ContainsKey("setterApi")&&r.ContainsKey("originalValue")&&r.ContainsKey("requestedValue")&&r.ContainsKey("valueAfterApply")&&r.ContainsKey("afterRestoreValue")&&(bool)r["afterRestoreMatches"]));});
        test("Exact spline and endpoint-merge readback cannot be skipped",f=>{var setter=f.io.setToggle;f.io.setToggle=(k,v)=>{if(k!=358)setter(k,v);};try{f.Open();throw new Exception("EXACT not checked");}catch(InvalidOperationException ex){Check(ex.Message.Contains("swDxfExportSplinesAsSplines"));}});
        Console.WriteLine("Native mapping fix: "+count+"/"+count+" PASS; no COM");return 0;
    }
}
