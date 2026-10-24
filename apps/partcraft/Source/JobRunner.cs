using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
class JobRunner {
 static string Q(string p){if(p.Contains("\""))throw new ArgumentException("Invalid quoted value");return "\""+p+"\"";}
 static int Main(string[] args){Console.OutputEncoding=new UTF8Encoding(false);try{ string runContext=Environment.GetEnvironmentVariable("SOLIDWORKSBREP_RUN_CONTEXT");if(!String.IsNullOrEmpty(runContext)){var gateWatch=Stopwatch.StartNew();while(!File.Exists(Path.Combine(runContext,"start.ready"))){if(gateWatch.Elapsed.TotalSeconds>20)throw new Exception("Job ownership gate timed out");Thread.Sleep(25);}}
  var p=new Dictionary<string,string>();for(int i=0;i<args.Length;i+=2){if(i+1>=args.Length||p.ContainsKey(args[i])||!new[]{"--mode","--source","--doc-lang","--tolerance-mm"}.Contains(args[i]))throw new Exception("Invalid job arguments");p[args[i]]=args[i+1];}string mode=p["--mode"],lang=p.ContainsKey("--doc-lang")?p["--doc-lang"]:"ru";if(!LanguageCatalog.Valid(lang))throw new Exception("Invalid language");
  var license=new PersonalUseLicenseService().Check(mode);if(!license.Allowed)throw new Exception(license.Message);
  using(var mutex=new Mutex(false,@"Local\SolidWorksBREP_Test_ProductionJob")){bool locked=false;try{try{locked=mutex.WaitOne(0);}catch(AbandonedMutexException){locked=true;}if(!locked)throw new Exception("Another production job is running");if(new[]{"SheetMetalDxfBatch","ProfilesCutTables","SelectedFacesDxf","ManufacturingPackage","PackageHost"}.Any(n=>Process.GetProcessesByName(n).Length>0))throw new Exception("Другая обработка SolidWorks уже выполняется.");
   string source=p.ContainsKey("--source")?p["--source"]:null;if(mode!="faces"&&(String.IsNullOrWhiteSpace(source)||!File.Exists(source)))throw new Exception("Select an existing model");string executable,arguments;
   switch(mode){case "dxf-benchmark":executable="SheetMetalDxfBatch.exe";arguments="--benchmark-positions 2 --tolerance-mm 0.01 --source "+Q(source);break;case "dxf":executable="SheetMetalDxfBatch.exe";arguments="--tolerance-mm 0.01 --source "+Q(source);break;case "profiles":executable="ProfilesCutTables.exe";arguments="--source "+Q(source)+" --doc-lang "+lang;break;case "faces":executable="SelectedFacesDxf.exe";arguments="--doc-lang "+lang;break;default:throw new Exception("Unknown job mode");}
   var info=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,executable),arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};info.EnvironmentVariables["SOLIDWORKSBREP_JOB_MODE"]=mode;info.EnvironmentVariables["SOLIDWORKSBREP_SOURCE_MODEL"]=mode=="faces"?"":source;info.EnvironmentVariables["SOLIDWORKSBREP_CONNECTION_LOG"]=AppStorage.FilePath("connection-last.json");using(var child=new Process{StartInfo=info}){child.OutputDataReceived+=(s,e)=>{if(e.Data!=null)Console.WriteLine(e.Data);};child.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)Console.Error.WriteLine(e.Data);};child.Start();child.BeginOutputReadLine();child.BeginErrorReadLine();child.WaitForExit();return child.ExitCode;}
  }finally{if(locked)mutex.ReleaseMutex();}}
 }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}}
}
