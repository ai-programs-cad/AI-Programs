using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public sealed class GroupMappingIOV02 {
    public Func<int,string> getFiles; public Action<int,string> setFiles;
    public Func<int,int> getInteger; public Func<int,int,bool> setInteger;
    public Func<int,bool> getToggle; public Action<int,bool> setToggle;
}

// Owns the mapping AND exact-export preferences. No nested scope turns mapping off.
// The caller/export/binding algorithm is otherwise unchanged.
public sealed class GroupMappingV02:IDisposable {
    sealed class Setting {
        public string name,kind,getApi,setApi; public int id; public object original,requested;
        public bool touched; public Dictionary<string,object> evidence;
    }
    readonly GroupMappingIOV02 io; readonly List<object> audit;
    readonly List<Setting> settings=new List<Setting>();
    bool disposed; string mappingPath,mappingHash; int targetIndex;
    static readonly int filesId=(int)swUserPreferenceStringListValue_e.swDxfMappingFiles;
    static readonly int indexId=(int)swUserPreferenceIntegerValue_e.swDxfMappingFileIndex;
    static readonly int mapping=(int)swUserPreferenceToggle_e.swDxfMapping;
    static readonly int noDialog=(int)swUserPreferenceToggle_e.swDXFDontShowMap;
    static readonly int splines=(int)swUserPreferenceToggle_e.swDxfExportSplinesAsSplines;
    static readonly int merge=(int)swUserPreferenceToggle_e.swDxfEndPointMerge;

    public GroupMappingV02(SldWorks sw,string path,List<object> records):this(new GroupMappingIOV02{
        getFiles=k=>sw.GetUserPreferenceStringListValue(k),setFiles=(k,v)=>sw.SetUserPreferenceStringListValue(k,v),
        getInteger=k=>sw.GetUserPreferenceIntegerValue(k),setInteger=(k,v)=>sw.SetUserPreferenceIntegerValue(k,v),
        getToggle=k=>sw.GetUserPreferenceToggle(k),setToggle=(k,v)=>sw.SetUserPreferenceToggle(k,v)},path,records){}

    public static string[] ParseFiles(string value){
        if(String.IsNullOrEmpty(value))return new string[0];
        value=value.Replace("\r\n","\n").TrimEnd('\n');
        if(value.Length==0)return new string[0];
        var rows=value.Split('\n');
        if(rows.Any(v=>v.Length==0||v.IndexOf('\r')>=0||v.IndexOf('\0')>=0))throw new InvalidOperationException("Invalid native DXF mapping list delimiter/empty interior entry");
        return rows;
    }
    static string Full(string path){return Path.GetFullPath(path.Replace('/',Path.DirectorySeparatorChar));}
    static bool PathEqual(string a,string b){return String.Equals(Full(a),Full(b),StringComparison.OrdinalIgnoreCase);}
    public static bool FilesEqual(string a,string b){
        var x=ParseFiles(a);var y=ParseFiles(b);return x.Length==y.Length&&x.Where((v,i)=>!PathEqual(v,y[i])).Count()==0;
    }
    object Read(Setting s){return s.kind=="files"?(object)io.getFiles(s.id):s.kind=="integer"?(object)io.getInteger(s.id):io.getToggle(s.id);}
    object Write(Setting s,object value){
        if(s.kind=="files"){io.setFiles(s.id,(string)value);return "VOID_COMPLETED";}
        if(s.kind=="integer")return io.setInteger(s.id,(int)value);
        io.setToggle(s.id,(bool)value);return "VOID_COMPLETED";
    }
    bool Equal(Setting s,object a,object b){return s.kind=="files"?FilesEqual((string)a,(string)b):Object.Equals(a,b);}
    static object Error(Exception ex){return new{exception=ex.GetType().FullName,message=ex.Message,hresult="0x"+ex.HResult.ToString("X8"),stackTrace=ex.StackTrace};}
    Setting Capture(string name,int id,string kind){
        var s=new Setting{name=name,id=id,kind=kind,getApi="ISldWorks.GetUserPreference"+(kind=="files"?"StringListValue":kind=="integer"?"IntegerValue":"Toggle"),setApi="ISldWorks.SetUserPreference"+(kind=="files"?"StringListValue":kind=="integer"?"IntegerValue":"Toggle")};
        s.evidence=new Dictionary<string,object>{{"operation","NATIVE_DXF_PREFERENCE"},{"parameterName",name},{"parameterId",id},{"getterApi",s.getApi},{"setterApi",s.setApi},{"applyResult","NOT_ATTEMPTED"},{"restoreResult","NOT_REQUIRED"}};audit.Add(s.evidence);
        try{s.original=Read(s);s.evidence["originalValue"]=s.original;}catch(Exception ex){s.evidence["initialReadError"]=Error(ex);throw;}
        settings.Add(s);return s;
    }
    void Apply(Setting s,List<string> errors){
        s.evidence["requestedValue"]=s.requested;
        try{
            if(Equal(s,s.original,s.requested))s.evidence["applyResult"]="SKIPPED_ALREADY_MATCHES";
            else{s.touched=true;s.evidence["sentValue"]=s.requested;object result=Write(s,s.requested);s.evidence["applyResult"]=result;if(result is bool&&!(bool)result)errors.Add(s.name+" setter returned false");}
        }catch(Exception ex){s.evidence["applyError"]=Error(ex);errors.Add(s.name+" setter: "+ex.Message);}
        try{object observed=Read(s);s.evidence["valueAfterApply"]=observed;bool ok=Equal(s,observed,s.requested);s.evidence["readbackMatches"]=ok;if(!ok)errors.Add(s.name+" readback differs");}
        catch(Exception ex){s.evidence["readbackError"]=Error(ex);errors.Add(s.name+" getter: "+ex.Message);}
    }
    void FinalCheck(string phase,List<string> errors,bool restore){
        foreach(var s in settings)try{
            object observed=Read(s),expected=restore?s.original:s.requested;bool ok=Equal(s,observed,expected);
            s.evidence[phase+"Value"]=observed;s.evidence[phase+"Matches"]=ok;if(!ok)errors.Add(s.name+" "+phase+" differs");
        }catch(Exception ex){s.evidence[phase+"Error"]=Error(ex);errors.Add(s.name+" "+phase+": "+ex.Message);}
    }
    public GroupMappingV02(GroupMappingIOV02 settingsIO,string path,List<object> records){
        io=settingsIO;audit=records;
        // Capture every value before performing any mutation.
        var fileSetting=Capture("swDxfMappingFiles",filesId,"files");
        var indexSetting=Capture("swDxfMappingFileIndex",indexId,"integer");
        var splineSetting=Capture("swDxfExportSplinesAsSplines",splines,"toggle");
        var mergeSetting=Capture("swDxfEndPointMerge",merge,"toggle");
        var noDialogSetting=Capture("swDXFDontShowMap",noDialog,"toggle");
        var mappingSetting=Capture("swDxfMapping",mapping,"toggle");
        mappingPath=path;
        if(String.IsNullOrEmpty(path))throw new ArgumentException("Explicit native DXF mapping file required");
        if(!File.Exists(path))throw new FileNotFoundException("Native DXF mapping file not found",path);
        mappingHash=Hash(path);
        audit.Add(new{operation="READ_NATIVE_MAPPING_FILE",path,sha256=mappingHash,encoding="Windows-1251",content=Encoding.GetEncoding(1251).GetString(File.ReadAllBytes(path)),fileModifiedByProgram=false});
        var files=ParseFiles((string)fileSetting.original).ToList();targetIndex=files.FindIndex(v=>PathEqual(v,path));
        if(targetIndex<0){targetIndex=files.Count;files.Add(Full(path));fileSetting.requested=String.Join("\n",files)+"\n";}
        else fileSetting.requested=fileSetting.original; // Preserve the user's list verbatim when already configured.
        indexSetting.requested=targetIndex;splineSetting.requested=true;mergeSetting.requested=false;noDialogSetting.requested=true;mappingSetting.requested=true;
        foreach(var s in settings)s.evidence["requestedValue"]=s.requested;
        audit.Add(new{operation="MAPPING_SELECTION_PLAN",requestedPath=Full(path),selectedIndex=targetIndex,existingListEntryReused=Object.Equals(fileSetting.original,fileSetting.requested),listEquality="Ordered canonical Windows paths; LF/CRLF and terminal line feed are list serialization, not a mapping-file mismatch"});
        // Record native export context, but do not guess or change the user's line-style mode.
        foreach(var id in new[]{(int)swUserPreferenceIntegerValue_e.swDxfVersion,(int)swUserPreferenceIntegerValue_e.swDxfOutputLineStyles,(int)swUserPreferenceIntegerValue_e.swDxfOutputNoScale}){
            try{audit.Add(new{operation="READ_DXF_CONTEXT",api="ISldWorks.GetUserPreferenceIntegerValue",parameterId=id,parameterName=Enum.GetName(typeof(swUserPreferenceIntegerValue_e),id),value=io.getInteger(id),changed=false});}
            catch(Exception ex){audit.Add(new{operation="READ_DXF_CONTEXT",parameterId=id,error=Error(ex),changed=false});}
        }
        try{
            var errors=new List<string>();foreach(var s in settings)Apply(s,errors);FinalCheck("beforeExport",errors,false);
            try{var selected=ParseFiles(io.getFiles(filesId));int actual=io.getInteger(indexId);bool match=actual>=0&&actual<selected.Length&&PathEqual(selected[actual],path);audit.Add(new{operation="VERIFY_SELECTED_NATIVE_MAPPING",requestedPath=Full(path),actualIndex=actual,actualSelectedPath=actual>=0&&actual<selected.Length?selected[actual]:null,matches=match});if(!match)errors.Add("Selected mapping file differs");}catch(Exception ex){audit.Add(new{operation="VERIFY_SELECTED_NATIVE_MAPPING",error=Error(ex)});errors.Add(ex.Message);}
            if(errors.Count>0)throw new InvalidOperationException("NATIVE_MAPPING_PREFERENCE_VALIDATION_FAILED: "+String.Join(" | ",errors));
        }catch(Exception original){try{Dispose();}catch(Exception restore){throw new AggregateException("Native mapping setup failed; restoration also failed. See per-setting evidence.",original,restore);}throw;}
    }
    static string Hash(string path){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    public void Dispose(){
        if(disposed)return;disposed=true;var errors=new List<string>();
        // Files first, selected index second. A list setter may reset the selected index.
        foreach(var s in settings){
            try{
                // Restore a dependent setting even if initially skipped: another setter may have changed it.
                object current=Read(s);s.evidence["valueBeforeRestore"]=current;
                if(Equal(s,current,s.original))s.evidence["restoreResult"]="SKIPPED_ALREADY_MATCHES";
                else{object result=Write(s,s.original);s.evidence["restoreResult"]=result;if(result is bool&&!(bool)result)errors.Add(s.name+" restore setter returned false");}
            }catch(Exception ex){s.evidence["restoreError"]=Error(ex);errors.Add(s.name+" restore: "+ex.Message);
                // A failed read must not prevent a best-effort setter to the captured value.
                try{s.evidence["restoreFallbackResult"]=Write(s,s.original);}catch(Exception retry){s.evidence["restoreFallbackError"]=Error(retry);}
            }
        }
        FinalCheck("afterRestore",errors,true);
        try{bool unchanged=Hash(mappingPath)==mappingHash;audit.Add(new{operation="VERIFY_MAPPING_FILE_UNCHANGED",path=mappingPath,unchanged});if(!unchanged)errors.Add("Native mapping file changed");}catch(Exception ex){errors.Add("Mapping hash: "+ex.Message);}
        if(errors.Count>0)throw new InvalidOperationException("NATIVE_DXF_MAPPING_RESTORE_FAILED: "+String.Join(" | ",errors));
    }

    public static int Tests(){return MappingTestsV03.Run();}
}
