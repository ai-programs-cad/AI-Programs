using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;

public static class NativeDxfCamStage {
    public static Dictionary<string,object> Run(string input,string root,string lineage,string tolerance){
        decimal requested;
        if(!decimal.TryParse(tolerance,NumberStyles.Float,CultureInfo.InvariantCulture,out requested)||requested<=0)throw new Exception("Invalid CAM tolerance in millimetres");
        string configuration=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cam-python-path.txt");
        var candidates=new List<string>();
        if(File.Exists(configuration))candidates.Add(File.ReadAllText(configuration,Encoding.UTF8).Trim().TrimStart('\uFEFF'));
        foreach(var profile in new[]{System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),System.Environment.GetEnvironmentVariable("USERPROFILE")})if(!String.IsNullOrWhiteSpace(profile))candidates.Add(Path.Combine(profile,@".cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"));
        string python=candidates.FirstOrDefault(File.Exists);
        string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"production\cam-source\prepare_cam_arcs.py");
        if(python==null||!File.Exists(script))throw new Exception("CAM runtime/module unavailable; native EXACT files retained. Checked Python paths="+String.Join("; ",candidates)+"; module="+script+" exists="+File.Exists(script));
        string destination=Path.Combine(root,"CAM_Preparation");
        string arguments="\""+script+"\" --input \""+input+"\" --output \""+destination+"\" --tolerance-mm "+requested.ToString(CultureInfo.InvariantCulture)+" --max-seconds 180 --lineage-report \""+lineage+"\"";
        var stdout=new StringBuilder();var stderr=new StringBuilder();var timer=Stopwatch.StartNew();
        var info=new ProcessStartInfo(python,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        int exit;
        using(var process=new Process{StartInfo=info}){
            process.OutputDataReceived+=(s,e)=>{if(e.Data!=null){lock(stdout)stdout.AppendLine(e.Data);Console.WriteLine(e.Data);}};
            process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(stderr)stderr.AppendLine(e.Data);};
            process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
            try{while(!process.WaitForExit(500))ManufacturingAgent.Boundary("CAM_ARCS_OFFLINE");process.WaitForExit();exit=process.ExitCode;}
            catch{if(!process.HasExited){process.Kill();process.WaitForExit();}throw;}
        }
        string logs=Path.Combine(root,"Reports","cam-arcs-console.txt");File.WriteAllText(logs,stdout.ToString()+stderr.ToString(),new UTF8Encoding(false));
        if(exit!=2)throw new Exception("CAM module failed, exit="+exit+"; see "+logs);
        string reportPath=Path.Combine(destination,"Reports","cam-arcs-report.json");
        var report=AB.Obj(CutListCoreV01.Json.DeserializeObject(File.ReadAllText(reportPath,Encoding.UTF8)));
        if(!AB.Bool(report["sourceFilesUnchanged"]))throw new Exception("CAM source immutability not verified");
        var files=AB.Array(report["files"]).Select(AB.Obj).ToArray();
        if(files.Length!=Directory.GetFiles(input,"*.dxf").Length||files.Any(f=>!AB.Bool(f["cncExists"])||!File.Exists(Convert.ToString(f["cnc"]))||!AB.Bool(f["geometryAndTopologyChecksPassed"])||Convert.ToInt32(f["cutSplineCountRemaining"])!=0||!AB.Bool(f["existingAnalyticPrimitivesBytePreserved"])))throw new Exception("CAM geometry/topology/file checks incomplete; see "+reportPath);
        return new Dictionary<string,object>{{"reportPath",reportPath},{"files",report["files"]},{"moduleElapsedSeconds",report["elapsedSeconds"]},{"processElapsedSeconds",timer.Elapsed.TotalSeconds},{"toleranceMm",requested},{"globalMinimumProven",false},{"productionReady",false},{"status","NEEDS_REVIEW"},{"outputFolder",destination}};
    }
}
