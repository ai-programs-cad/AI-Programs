using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
class PreflightCheck {
 [STAThread]static int Main(string[] args){Console.OutputEncoding=new System.Text.UTF8Encoding(false);var report=new Dictionary<string,object>{{"version",AppStorage.Version},{"personalEdition",true},{"readOnly",true},{"macroExecuted",false},{"modelSavedOrRebuilt",false},{"paymentSystem",false}};int exit=1;try{if(args.Length==2&&args[0]=="--source"){System.Environment.SetEnvironmentVariable("SOLIDWORKSBREP_SOURCE_MODEL",Path.GetFullPath(args[1]));System.Environment.SetEnvironmentVariable("SOLIDWORKSBREP_CONNECTION_LOG",AppStorage.FilePath("connection-check.json"));}
  report["solidWorksFolder"]=ProductEnvironment.SolidWorksRoot();report["framework48Available"]=ProductEnvironment.FrameworkAvailable();if(!ProductEnvironment.FrameworkAvailable())throw new Exception(".NET Framework 4.8 is required");
  string bin=AppDomain.CurrentDomain.BaseDirectory;foreach(string f in new[]{"SheetMetalDxfBatch.exe","ProfilesCutTables.exe","SelectedFacesDxf.exe","Resources/Profiles+h.swp","Resources/StileDXF"})if(!File.Exists(Path.Combine(bin,f)))throw new Exception("Missing module: "+f);
  string python=Path.GetFullPath(Path.Combine(bin,"..","..","Runtime","python.exe"));var pi=new ProcessStartInfo(python,"-I -c \"import sys,decimal,fractions,dataclasses,json,hashlib; print(sys.version)\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};using(var p=Process.Start(pi)){string s=p.StandardOutput.ReadToEnd(),error=p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new Exception("CAM runtime: "+error);report["python"]=s.Trim();}
  if(args.Length==1&&args[0]=="--static"){report["status"]="STATIC_DEPENDENCIES_OK_COM_NOT_TESTED";exit=0;}
  else{if(new[]{"JobRunner","SheetMetalDxfBatch","ProfilesCutTables","SelectedFacesDxf","ManufacturingPackage","PackageHost"}.Any(n=>Process.GetProcessesByName(n).Length>0))throw new Exception("Дождитесь завершения текущей обработки перед проверкой COM.");var sw=ExistingSolidWorksConnection.Attach();string revision=sw.RevisionNumber();report["solidWorksRevision"]=revision;if(!revision.StartsWith("33.",StringComparison.Ordinal))throw new Exception("Active SOLIDWORKS is not version 2025");var doc=sw.ActiveDoc as ModelDoc2;report["activeDocument"]=doc==null?null:doc.GetPathName();report["drawingTemplate"]=sw.GetUserPreferenceStringValue((int)SolidWorks.Interop.swconst.swUserPreferenceStringValue_e.swDefaultTemplateDrawing);report["status"]="SOLIDWORKS_2025_COM_AVAILABLE";exit=0;}
 }catch(Exception ex){report["status"]="DEPENDENCY_OR_COM_CHECK_REQUIRED";report["error"]=ex.Message;report["hint"]="Откройте SOLIDWORKS 2025 под тем же пользователем и с теми же правами, затем повторите проверку.";}
  string json=new JavaScriptSerializer().Serialize(report);File.WriteAllText(AppStorage.FilePath("preflight.json"),json);Console.WriteLine(json);return exit;
 }
}
