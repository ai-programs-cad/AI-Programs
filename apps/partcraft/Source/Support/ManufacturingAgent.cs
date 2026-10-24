using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using Environment = System.Environment;

public class AgentJob {
    public string id,nonce,type,source,sectionEvidence;
    public int timeoutSeconds=900;
    public string requestedUtc;
}
public static class ManufacturingAgent {
    public const string Version="1.6-export-compare";
    static readonly string Home=AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string Root=Path.Combine(Home,"bridge");
    public static readonly string AllowedSource=@"C:\AIPrograms-TestData\sample.SLDPRT";
    static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=32*1024*1024,RecursionLimit=180};}
    static SldWorks sw;static int staThread;static AgentJob active;static DateTime deadline;static Stopwatch elapsed;
    static string phase="IDLE";static int progress;static readonly object gate=new object();
    static System.Threading.Timer heartbeat;static FileStream agentLock;static Form window;static Label label;
    static readonly HashSet<string> Types=new HashSet<string>(new[]{"PING","INVENTORY","WELDMENT_END_CUTS","PROFILE_CUT_TABLES","SHEET_METAL_DXF","CAM_ARCS","DRAWINGS","FULL_MANUFACTURING_PACKAGE","MACRO_TEST","NATIVE_EXPORT_COMPARE"},StringComparer.Ordinal);
    static readonly HashSet<string> Terminal=new HashSet<string>(new[]{"COMPLETED","NEEDS_REVIEW","FAILED"});
    static string Utc(){return DateTime.UtcNow.ToString("o");}
    static string Folder(string name){return Path.Combine(Root,name);}
    static void Setup(){Directory.CreateDirectory(Root);foreach(var d in new[]{"queue","results","status","cancel","logs","claims"})Directory.CreateDirectory(Folder(d));}
    static void NoLink(string path){for(var d=new DirectoryInfo(Path.GetFullPath(path));d!=null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new Exception("REPARSE_POINT_NOT_ALLOWED: "+d.FullName);}
    public static string SafeId(string id){Guid g;if(!Guid.TryParseExact(id,"N",out g))throw new Exception("INVALID_JOB_ID");return g.ToString("N");}
    static string StatusPath(string id){return Path.Combine(Folder("status"),SafeId(id)+".json");}
    static Dictionary<string,object> Read(string path){return Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));}
    public static void Atomic(string path,object value,bool replace){
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var w=new StreamWriter(f,new UTF8Encoding(false)))w.Write(Json().Serialize(value));
            if(replace&&File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    static string Hash(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","");}
    static void Validate(AgentJob j){
        SafeId(j.id);Guid n;if(!Guid.TryParseExact(j.nonce,"N",out n))throw new Exception("INVALID_REQUEST_NONCE");
        if(!Types.Contains(j.type))throw new Exception("UNKNOWN_TASK_TYPE");
        if(j.timeoutSeconds<5||j.timeoutSeconds>7200)throw new Exception("TIMEOUT_OUT_OF_RANGE");
        if(j.type!="PING"){
            if(!String.Equals(Path.GetFullPath(j.source??""),AllowedSource,StringComparison.OrdinalIgnoreCase))throw new Exception("STAGE1_SOURCE_NOT_ALLOWLISTED");
            if(!File.Exists(j.source))throw new FileNotFoundException("SOURCE_MISSING",j.source);NoLink(j.source);
            if(j.sectionEvidence!=null){string p=Path.GetFullPath(j.sectionEvidence),allowed=Path.GetFullPath(Path.Combine(Home,"..","native-weldments","runs"))+Path.DirectorySeparatorChar;
                if(!p.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!File.Exists(p))throw new Exception("SECTION_EVIDENCE_OUTSIDE_ALLOWED_REPORTS");NoLink(p);
            }
        }
    }
    static void Log(string id,string message){File.AppendAllText(Path.Combine(Folder("logs"),SafeId(id)+".log"),Utc()+" "+message+Environment.NewLine,new UTF8Encoding(false));}
    static void Status(AgentJob j,string state,string detail,object data=null){
        lock(gate){Atomic(StatusPath(j.id),new{id=j.id,nonce=j.nonce,type=j.type,state,stage=phase,progressPercent=progress,updatedUtc=Utc(),elapsedSeconds=elapsed==null?0:elapsed.Elapsed.TotalSeconds,detail,data},true);}
        Log(j.id,state+" "+detail);
    }
    public static SldWorks Connection(){
        if(Thread.CurrentThread.ManagedThreadId!=staThread||Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new Exception("COM_MUST_RUN_ON_AGENT_STA");
        try{if(sw!=null){var revision=sw.RevisionNumber();return sw;}}catch(COMException){sw=null;}
        sw=ExistingSolidWorksConnection.Attach();return sw;
    }
    public static void Boundary(string stage){
        if(active==null)return;
        if(File.Exists(Path.Combine(Folder("cancel"),active.id+".json")))throw new OperationCanceledException("CANCEL_REQUESTED_AT_SAFE_BOUNDARY");
        if(DateTime.UtcNow>deadline)throw new OperationCanceledException("TIMEOUT_AT_SAFE_BOUNDARY: running COM calls are never aborted");
        phase=stage;Status(active,"RUNNING",stage);
    }
    public static void BodyProgress(int done,int total){progress=10+(total==0?0:(int)(75.0*done/total));Boundary("READ_BODY_"+(done+1)+"_OF_"+total);}
    static void Heartbeat(object unused){
        try{lock(gate){Atomic(Path.Combine(Root,"heartbeat.json"),new{version=Version,pid=Process.GetCurrentProcess().Id,sessionId=Process.GetCurrentProcess().SessionId,identity=WindowsIdentity.GetCurrent().Name,updatedUtc=Utc(),taskId=active==null?null:active.id,stage=phase,progressPercent=progress,transport="FILESYSTEM_LOCAL_ONLY",singleSta=true,readOnlyJobsOnly=false,timeoutMode="COOPERATIVE_SAFE_BOUNDARIES_NO_COM_ABORT"},true);}}catch(Exception e){Debug.WriteLine(e);}
    }
    static void Recover(){foreach(var p in Directory.GetFiles(Folder("status"),"*.json"))try{var s=Read(p);if(Convert.ToString(s["state"])=="RUNNING"){
        var j=Json().Deserialize<AgentJob>(File.ReadAllText(Path.Combine(Folder("queue"),Convert.ToString(s["id"])+".json"),Encoding.UTF8));
        phase="RECOVERY";Status(j,"NEEDS_REVIEW","Previous agent stopped during task; automatic replay prohibited. Review existing artifacts, submit new ID if needed.");
    }}catch(Exception e){File.AppendAllText(Path.Combine(Root,"recovery.log"),Utc()+" "+e+Environment.NewLine);}}
    static void Execute(AgentJob j){
        Validate(j);active=j;elapsed=Stopwatch.StartNew();deadline=DateTime.UtcNow.AddSeconds(j.timeoutSeconds);phase="PRECHECK";progress=0;Status(j,"RUNNING","Claimed by user-session STA agent");
        string result=Path.Combine(Folder("results"),j.id);Directory.CreateDirectory(result);
        try{
            Boundary("CONNECT");var app=Connection();var doc=app.ActiveDoc as ModelDoc2;
            if(j.type=="PING"){
                var pong=new{j.id,j.nonce,agentIdentity=WindowsIdentity.GetCurrent().Name,sessionId=Process.GetCurrentProcess().SessionId,connected=true,revision=app.RevisionNumber(),activePath=doc==null?null:doc.GetPathName(),configuration=doc==null?null:doc.ConfigurationManager.ActiveConfiguration.Name,dirty=doc==null?(bool?)null:doc.GetSaveFlag(),readOnly=true,changesMade=false};
                Atomic(Path.Combine(result,"pong.json"),pong,false);progress=100;phase="FINISHED";Status(j,"COMPLETED","Real COM handshake returned",pong);return;
            }
            if(j.type=="NATIVE_EXPORT_COMPARE"){var compared=NativeExportComparison.Run(app,j.source,result);phase="FINISHED";progress=100;Status(j,Convert.ToString(compared["status"])=="COMPARISON_COMPLETED_NEEDS_REVIEW"?"NEEDS_REVIEW":"FAILED",Convert.ToString(compared["status"]),compared);return;}if(j.type=="MACRO_TEST"){var tested=MacroTestWork.Run(app,j.source,result);phase="FINISHED";progress=100;Status(j,Convert.ToString(tested["status"])=="MACRO_TEST_COMPLETED_NEEDS_REVIEW"?"NEEDS_REVIEW":"FAILED",Convert.ToString(tested["status"]),tested);return;}if(j.type=="FULL_MANUFACTURING_PACKAGE"){var full=ProductionBridge.Run(app,j.source,result,true);phase="FINISHED";progress=100;Status(j,Convert.ToString(full["status"]).EndsWith("NEEDS_REVIEW",StringComparison.Ordinal)?"NEEDS_REVIEW":"FAILED",Convert.ToString(full["status"]),full);return;}
            if(j.type!="INVENTORY"&&j.type!="WELDMENT_END_CUTS")throw new NotSupportedException("TASK_NOT_INTEGRATED_STAGE1: "+j.type);
            if(doc==null||doc.GetType()!=1||!String.Equals(Path.GetFullPath(doc.GetPathName()),Path.GetFullPath(j.source),StringComparison.OrdinalIgnoreCase))throw new Exception("ACTIVE_SOURCE_DOCUMENT_MISMATCH");
            if(doc.GetSaveFlag())throw new Exception("SOURCE_HAS_UNSAVED_CHANGES: save manually before manufacturing; agent never saves original");
            string initial=Hash(j.source);string configuration=doc.ConfigurationManager.ActiveConfiguration.Name;int stamp=doc.GetUpdateStamp();
            int selected=((SelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1);
            Atomic(Path.Combine(result,"context.json"),new{source=j.source,sha256=initial,configuration,updateStamp=stamp,selectedCount=selected,algorithm=Version,readerSourceHash=Hash(Path.Combine(Home,"generated","CheckNativeWeldments.cs")),cacheUsed=false},false);
            // No persistent cache hit is accepted until in-memory body/state checks also match.
            Boundary("READ_NATIVE_WELDMENTS");progress=10;
            var args=new List<string>{"--source",j.source,"--output",Path.Combine(result,"native-report.json")};
            if(j.sectionEvidence!=null){args.Add("--section-evidence");args.Add(j.sectionEvidence);}
            int exit=AgentNativeReader.Run(args.ToArray());progress=90;
            var report=Read(Path.Combine(result,"native-report.json"));
            if(Hash(j.source)!=initial||doc.GetUpdateStamp()!=stamp||doc.GetSaveFlag()||doc.ConfigurationManager.ActiveConfiguration.Name!=configuration||((SelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1)!=selected)throw new Exception("SOURCE_STATE_CHANGED: original not saved; stop for review");
            if(File.Exists(Path.Combine(Folder("cancel"),j.id+".json"))||DateTime.UtcNow>deadline){phase="CANCELLED_AT_BOUNDARY";Status(j,"NEEDS_REVIEW","Cancelled or timed out; partial read-only report retained. No COM call forcibly terminated.");return;}
            bool success=exit!=1&&Convert.ToString(report["status"])!="STOPPED_OR_INCOMPLETE";
            phase="FINISHED";progress=success?100:90;
            Status(j,success?"NEEDS_REVIEW":"FAILED",success?"Native COM read completed; production certification remains separate":"Reader returned incomplete result",new{readerExitCode=exit,resultDirectory=result,nativeStatus=report["status"],sourceSha256=initial});
        }catch(OperationCanceledException e){Status(j,"NEEDS_REVIEW",e.Message,new{exception=e.ToString()});}
        catch(Exception e){Status(j,"FAILED",e.Message,new{hresult="0x"+e.HResult.ToString("X8"),exception=e.ToString(),resultDirectory=result});}
        finally{elapsed.Stop();active=null;phase="IDLE";progress=0;}
    }
    static void Tick(object sender,EventArgs e){
        if(active!=null)return;
        foreach(var path in Directory.GetFiles(Folder("queue"),"*.json").OrderBy(p=>p,StringComparer.Ordinal)){
            AgentJob j=null;
            try{
                if(new FileInfo(path).Length>65536)throw new Exception("JOB_TOO_LARGE");
                j=Json().Deserialize<AgentJob>(File.ReadAllText(path,Encoding.UTF8));Validate(j);
                if(Path.GetFileNameWithoutExtension(path)!=j.id)throw new Exception("JOB_FILENAME_ID_MISMATCH");
                string sp=StatusPath(j.id);if(File.Exists(sp)&&Terminal.Contains(Convert.ToString(Read(sp)["state"])))continue;
                string claim=Path.Combine(Folder("claims"),j.id+".claim");if(File.Exists(claim))continue;
                using(var f=new FileStream(claim,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
                label.Text="Running "+j.type+"\r\n"+j.id;Execute(j);label.Text="Ready. Last task: "+j.id;break;
            }catch(Exception ex){if(j!=null){phase="REJECTED";Status(j,"FAILED",ex.Message,new{exception=ex.ToString()});}else File.AppendAllText(Path.Combine(Root,"rejected.log"),Utc()+" "+path+" "+ex+Environment.NewLine);}
        }
    }
    static int Agent(){
        NoLink(Root);Setup();try{agentLock=new FileStream(Path.Combine(Root,"agent.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){throw new Exception("AGENT_ALREADY_RUNNING_OR_LOCKED");}
        staThread=Thread.CurrentThread.ManagedThreadId;Recover();heartbeat=new System.Threading.Timer(Heartbeat,null,0,1500);
        Application.EnableVisualStyles();window=new Form{Text="SolidWorks Manufacturing Agent — Production",Width=660,Height=180};
        label=new Label{Dock=DockStyle.Fill,Text="Ready. Local queue only; existing SolidWorks only.\r\n"+Root,Padding=new Padding(12)};window.Controls.Add(label);
        var timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=Tick;timer.Start();
        window.FormClosing+=(s,e)=>{if(active!=null){e.Cancel=true;Atomic(Path.Combine(Folder("cancel"),active.id+".json"),new{requestedUtc=Utc()},true);}};
        try{Application.Run(window);}finally{timer.Dispose();heartbeat.Dispose();agentLock.Dispose();}return 0;
    }
    public static int Cli(string[] args){
        Setup();NoLink(Root);string mode=args[0];
        if(mode=="--enqueue"){
            var j=new AgentJob{id=Guid.NewGuid().ToString("N"),nonce=Guid.NewGuid().ToString("N"),requestedUtc=Utc()};
            for(int i=1;i<args.Length;i++){string k=args[i];if(i+1>=args.Length)throw new Exception("MISSING_ARGUMENT_VALUE");string v=args[++i];switch(k){case "--id":j.id=v;break;case "--type":j.type=v;break;case "--source":j.source=v;break;case "--section-evidence":j.sectionEvidence=v;break;case "--timeout":j.timeoutSeconds=Int32.Parse(v);break;default:throw new Exception("UNKNOWN_ARGUMENT "+k);}}
            Validate(j);string qp=Path.Combine(Folder("queue"),j.id+".json");if(File.Exists(qp)){var old=Json().Deserialize<AgentJob>(File.ReadAllText(qp,Encoding.UTF8));if(old.type!=j.type||old.source!=j.source||old.sectionEvidence!=j.sectionEvidence||old.timeoutSeconds!=j.timeoutSeconds)throw new Exception("IDEMPOTENCY_KEY_CONFLICT");Console.WriteLine(Json().Serialize(new{id=j.id,state="ALREADY_SUBMITTED",nonce=old.nonce}));return 0;}
            phase="WAITING_FOR_AGENT";Status(j,"QUEUED","Waiting for interactive user-session agent");Atomic(qp,j,false);Console.WriteLine(Json().Serialize(j));return 0;
        }
        if(mode=="--status"){Console.WriteLine(File.ReadAllText(StatusPath(args[1]),Encoding.UTF8));return 0;}
        if(mode=="--cancel"){string id=SafeId(args[1]);Atomic(Path.Combine(Folder("cancel"),id+".json"),new{requestedUtc=Utc()},true);Console.WriteLine("CANCEL_REQUESTED "+id);return 0;}
        if(mode=="--transport-check"){
            string id=Guid.NewGuid().ToString("N");string path=Path.Combine(Root,"transport-"+id+".json");Atomic(path,new{nonce=id,identity=WindowsIdentity.GetCurrent().Name,pid=Process.GetCurrentProcess().Id,sessionId=Process.GetCurrentProcess().SessionId},false);
            Console.WriteLine(File.ReadAllText(path,Encoding.UTF8));return 0;
        }
        if(mode=="--self-test"){AgentNativeReader.Run(new[]{"--self-test"});Tests();return ProductionBridge.Tests();}
        throw new Exception("Use no arguments to start agent, or --enqueue / --status / --cancel / --transport-check / --self-test");
    }
    static int Tests(){int n=0;Action<bool> c=x=>{if(!x)throw new Exception("Agent regression "+n);n++;};
        string id=Guid.NewGuid().ToString("N");c(SafeId(id)==id);try{SafeId("..\\evil");throw new Exception("accepted");}catch(Exception e){c(e.Message=="INVALID_JOB_ID");}
        var j=new AgentJob{id=id,nonce=Guid.NewGuid().ToString("N"),type="PING"};Validate(j);c(true);j.timeoutSeconds=1;try{Validate(j);throw new Exception("accepted");}catch(Exception e){c(e.Message=="TIMEOUT_OUT_OF_RANGE");}
        j.timeoutSeconds=30;j.type="ARBITRARY_EXEC";try{Validate(j);throw new Exception("accepted");}catch(Exception e){c(e.Message=="UNKNOWN_TASK_TYPE");}
        j.type="INVENTORY";j.source=@"C:\other.SLDPRT";try{Validate(j);throw new Exception("accepted");}catch(Exception e){c(e.Message=="STAGE1_SOURCE_NOT_ALLOWLISTED");}
        string test=Path.Combine(Root,"self-test-"+id+".json");Atomic(test,new{nonce=id,name="По умолчанию<As Machined>"},false);c(Convert.ToString(Read(test)["nonce"])==id);c(Convert.ToString(Read(test)["name"])=="По умолчанию<As Machined>");Atomic(test,new{nonce="changed"},true);c(Convert.ToString(Read(test)["nonce"])=="changed");try{Atomic(test,new{nonce="overwrite"},false);throw new Exception("accepted");}catch(IOException){c(Convert.ToString(Read(test)["nonce"])=="changed");}
        c(Types.Count==10&&Types.Contains("NATIVE_EXPORT_COMPARE"));c(Terminal.Contains("NEEDS_REVIEW"));c(!Terminal.Contains("RUNNING"));File.Delete(test);
        Console.WriteLine("Agent transport/policy: "+n+"/"+n+" PASS; no COM");return 0;
    }
    [STAThread] public static int Main(string[] args){try{Console.OutputEncoding=new UTF8Encoding(false);return args.Length==0?Agent():Cli(args);}catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}}
}
