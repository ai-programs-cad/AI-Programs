using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;

// A Windows job contains only the process tree launched by this application.
static class JobControl {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateJobObject(IntPtr a,string n);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr j,IntPtr p);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateJobObject(IntPtr j,uint code);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool QueryInformationJobObject(IntPtr j,int kind,IntPtr data,uint size,out uint returned);
 static IntPtr job;static Process runner;static string context;public static bool Stopping;public static string LastMessage="";
 public static bool Running {get{try{return runner!=null&&!runner.HasExited;}catch{return false;}}}
 public static void Prepare(ProcessStartInfo info){if(Running||Stopping)throw new Exception("Another job is active");Finish();RetryCleanup();string runs=Path.Combine(AppStorage.DataRoot,"Runs");if(Directory.Exists(runs))foreach(string pending in Directory.GetDirectories(runs)){string state=Path.Combine(pending,"status.txt");if(File.Exists(state)&&new[]{"STOPPING","CLEANUP_PENDING"}.Contains(File.ReadAllText(state)))throw new Exception("Resolve pending stop/cleanup before starting a new job: "+pending);}context=Path.Combine(AppStorage.DataRoot,"Runs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(context);Environment.SetEnvironmentVariable("SOLIDWORKSBREP_RUN_CONTEXT",context);info.EnvironmentVariables["SOLIDWORKSBREP_RUN_CONTEXT"]=context;File.WriteAllText(Path.Combine(context,"status.txt"),"STARTING");}
 public static void Started(Process child){runner=child;job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero||!AssignProcessToJobObject(job,child.Handle)){try{child.Kill();child.WaitForExit();}catch{}Finish();throw new Exception("Cannot establish safe process ownership; job was not started");}File.WriteAllText(Path.Combine(context,"status.txt"),"RUNNING");File.WriteAllText(Path.Combine(context,"start.ready"),"ready");}
 static bool Empty(){IntPtr p=Marshal.AllocHGlobal(48);try{uint size;if(!QueryInformationJobObject(job,1,p,48,out size))throw new System.ComponentModel.Win32Exception();return Marshal.ReadInt32(p,40)==0;}finally{Marshal.FreeHGlobal(p);}}
 public static void Finish(){if(Stopping)return;if(runner!=null){try{if(!runner.HasExited)return;}catch{}runner=null;Environment.SetEnvironmentVariable("SOLIDWORKSBREP_RUN_CONTEXT",null);if(context!=null&&File.Exists(Path.Combine(context,"status.txt"))&&File.ReadAllText(Path.Combine(context,"status.txt"))=="RUNNING"){JobOwnership.Commit(context);File.WriteAllText(Path.Combine(context,"status.txt"),"FINISHED");}}if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}}
 public static async Task<string> ForceStop(){if(!Running)throw new Exception("No active job");Stopping=true;string root=context;try{return await Task.Run(()=>{
   // Kill all owned workers first, and wait for every descendant before touching files.
   File.WriteAllText(Path.Combine(root,"status.txt"),"STOPPING");if(!TerminateJobObject(job,1223))throw new System.ComponentModel.Win32Exception();var timeout=Stopwatch.StartNew();while(!Empty()){if(timeout.Elapsed.TotalSeconds>20)throw new Exception("Worker processes did not exit; cleanup postponed");Thread.Sleep(100);}
   string sw=Path.Combine(root,"solidworks.json");if(File.Exists(sw)){var e=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(sw));int pid=Convert.ToInt32(e["pid"]);long ticks=Convert.ToInt64(e["start"]);Process p=null;try{p=Process.GetProcessById(pid);}catch(ArgumentException){}if(p!=null)using(p){if(!p.HasExited){if(p.ProcessName!="SLDWORKS"||p.SessionId!=Process.GetCurrentProcess().SessionId||p.StartTime.ToUniversalTime().Ticks!=ticks)throw new Exception("SolidWorks process identity changed; cleanup postponed");p.Kill();if(!p.WaitForExit(20000))throw new Exception("SolidWorks did not exit; cleanup postponed");}}}
   File.WriteAllText(Path.Combine(root,"status.txt"),"CLEANUP_PENDING");JobOwnership.Cleanup(root);File.WriteAllText(Path.Combine(root,"status.txt"),"CANCELLED_CLEANED");return "STOP_CLEANED";
  });}catch(Exception ex){File.WriteAllText(Path.Combine(root,"stop-error.txt"),ex.ToString());return "STOP_CLEANUP_FAILED"+Environment.NewLine+ex.Message;}finally{Stopping=false;runner=null;Environment.SetEnvironmentVariable("SOLIDWORKSBREP_RUN_CONTEXT",null);if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}}}
 public static void RetryCleanup(){string runs=Path.Combine(AppStorage.DataRoot,"Runs");if(!Directory.Exists(runs))return;foreach(string root in Directory.GetDirectories(runs)){string state=Path.Combine(root,"status.txt");if(!File.Exists(state)||File.ReadAllText(state)!="CLEANUP_PENDING")continue;try{JobOwnership.Cleanup(root);File.WriteAllText(state,"CANCELLED_CLEANED");}catch(Exception ex){LastMessage=ex.Message;}}}
}
