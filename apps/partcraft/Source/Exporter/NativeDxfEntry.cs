using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using Environment = System.Environment;

public static class NativeDxfEntry {
    [STAThread] public static int Main(string[] args){
        bool interactive=args.Length==0;
        var overall=Stopwatch.StartNew();
        try{
            Console.OutputEncoding=new UTF8Encoding(false);
            string home=AppDomain.CurrentDomain.BaseDirectory;
            if(args.Length==1&&args[0]=="--self-test")return StandaloneGroupNative.Main(args);
            if(Process.GetProcesses().Any(p=>p.ProcessName.StartsWith("ManufacturingAgent",StringComparison.OrdinalIgnoreCase)))throw new Exception("Close the Manufacturing Agent window before starting the standalone exporter; SolidWorks must stay open.");
            Directory.CreateDirectory(Path.Combine(home,"bridge"));
            // Exclude concurrent jobs from the existing sequential COM agent.
            using(var guard=new FileStream(Path.Combine(home,"bridge","agent.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                var connection=Stopwatch.StartNew();var sw=ExistingSolidWorksConnection.Attach();double connectionSeconds=connection.Elapsed.TotalSeconds;
                string source=null,tolerance="0.01";
                for(int i=0;i<args.Length;i++){if(i+1>=args.Length)throw new Exception("Missing argument value");string key=args[i],value=args[++i];if(key=="--source")source=Path.GetFullPath(value);else if(key=="--tolerance-mm")tolerance=value;else if(key=="--benchmark-positions"){if(value!="2")throw new Exception("Benchmark must use exactly two positions");StandaloneGroupNative.PreparedPositionLimit=2;StandaloneGroupNative.PreparedBatchSize=2;}else throw new Exception("Unknown option: "+key);}
                decimal parsed;if(!decimal.TryParse(tolerance,NumberStyles.Float,CultureInfo.InvariantCulture,out parsed)||parsed<=0)throw new Exception("Positive --tolerance-mm required");
                if(source==null){var doc=sw.ActiveDoc as ModelDoc2;if(doc==null||doc.GetType()!=1||String.IsNullOrWhiteSpace(doc.GetPathName()))throw new Exception("Activate a saved SolidWorks part before launch.");source=doc.GetPathName();}
                if(!File.Exists(source)||!String.Equals(Path.GetExtension(source),".SLDPRT",StringComparison.OrdinalIgnoreCase))throw new Exception("Source SLDPRT not found: "+source);
                string logs=Path.Combine(SheetMetalStorage.WorkRoot,"NativeDxfLogs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(SheetMetalStorage.WorkRoot);JobOwnership.OwnDirectory(logs,SheetMetalStorage.WorkRoot);
                System.Collections.Generic.Dictionary<string,object> report;
                using(var automation=new NativeAutomationScope(sw))report=NativeBatchDxfWork.Run(sw,source,logs,tolerance);
                string status=Convert.ToString(report["status"]),folder=Convert.ToString(report["runFolder"]);
                report["connectionSeconds"]=connectionSeconds;report["programElapsedSecondsBeforeFinalDialog"]=overall.Elapsed.TotalSeconds;
                File.WriteAllText(Path.Combine(folder,"Reports","native-batch-dxf.json"),CutListCoreV01.Json.Serialize(report),new UTF8Encoding(false));
                File.AppendAllText(Path.Combine(folder,"Reports","timings.csv"),Environment.NewLine+"CONNECT;"+connectionSeconds.ToString("F6",CultureInfo.InvariantCulture)+Environment.NewLine+"PROGRAM_TOTAL_BEFORE_DIALOG;"+overall.Elapsed.TotalSeconds.ToString("F6",CultureInfo.InvariantCulture),new UTF8Encoding(true));
                Console.WriteLine(status+Environment.NewLine+folder);
                if(interactive)MessageBox.Show(status+Environment.NewLine+Environment.NewLine+folder,"Native Sheet Metal DXF",MessageBoxButtons.OK,status=="DXF_EXACT_AND_CAM_ARCS_CREATED_NEEDS_REVIEW"?MessageBoxIcon.Information:MessageBoxIcon.Warning);
                return status=="DXF_EXACT_AND_CAM_ARCS_CREATED_NEEDS_REVIEW"?2:status=="BENCHMARK_COMPLETED_NOT_PRODUCTION_OUTPUT"?3:1;
            }
        }catch(Exception e){
            string message=e is IOException?"COM agent may still be running. Close its window before starting the standalone exporter. Details: "+e.Message:e.ToString();
            Console.Error.WriteLine(message);
            if(interactive)MessageBox.Show(message,"Native Sheet Metal DXF",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}
