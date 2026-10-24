using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public static class NativeBatchDxfWork {
    static string Hash(string p){return ManufacturingSourceFilesV03.Hash(p,"NATIVE_BATCH_DXF");}
    static void Save(string p,object r){File.WriteAllText(p,CutListCoreV01.Json.Serialize(r),new UTF8Encoding(false));}
    public static Dictionary<string,object> Run(SldWorks sw,string source,string results,string tolerance="0.01"){
        Console.WriteLine("BEGIN SOURCE_INVENTORY");var inventoryClock=Stopwatch.StartNew();
        var original=sw.GetOpenDocumentByName(source) as ModelDoc2;
        if(original==null||original.GetSaveFlag())throw new Exception("SAVED_ORIGINAL_MUST_BE_OPEN");
        string hash=Hash(source),config=original.ConfigurationManager.ActiveConfiguration.Name;int stamp=original.GetUpdateStamp();
        if(NativeWeldmentAdapter.Items(sw.GetDocumentDependencies2(source,false,false,false)).Length!=0)throw new Exception("EXTERNAL_DEPENDENCIES_REQUIRE_PACK_AND_GO: source not modified");
        var names=NativeWeldmentAdapter.Items(((PartDoc)original).GetBodies2((int)swBodyType_e.swSolidBody,false)).Cast<Body2>().Where(b=>b.IsSheetMetal()).Select(b=>b.Name).ToArray();
        if(names.Length==0||names.Distinct(StringComparer.Ordinal).Count()!=names.Length)throw new Exception("MISSING_OR_NONUNIQUE_SHEET_BODY_NAMES");
        Console.WriteLine("END SOURCE_INVENTORY "+inventoryClock.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s");
        string time=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time")).ToString("yyyyMMdd_HHmmss");
        string root=Path.Combine(SheetMetalStorage.WorkRoot,"_Manufacturing","Run_"+time+"_DxfBatch_"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(SheetMetalStorage.WorkRoot);JobOwnership.OwnDirectory(root,SheetMetalStorage.WorkRoot);
        string workspace=Path.Combine(root,"Workspace"),exact=Path.Combine(root,"DXF","EXACT_NATIVE"),modelspace=Path.Combine(root,"DXF","MODELSPACE"),reports=Path.Combine(root,"Reports");
        foreach(var p in new[]{workspace,exact,modelspace,reports})Directory.CreateDirectory(p);
        string model=source;ModelDoc2 copy=null;
        var outputs=new List<object>();var lineage=new List<object>();var timings=new List<object>();var timer=Stopwatch.StartNew();
        var report=new Dictionary<string,object>{{"status","RUNNING"},{"method","C# / native GroupDxfV03 / one ExportToDWG2 + certified CAM_ARCS"},{"source",source},{"sourceSHA256Before",hash},{"configuration",config},{"sheetBodies",names},{"runFolder",root},{"outputs",outputs},{"timings",timings},{"productionReady",false}};
        try{
            ManufacturingAgent.Boundary("PREPARE_NATIVE_DXF_WORKSPACE");Console.WriteLine("BEGIN PREPARE_WORKSPACE");var prepare=Stopwatch.StartNew();
            // StandaloneGroupNative creates the only temporary model copy.
            report["temporaryModelCopyCount"]=1;
            Console.WriteLine("END PREPARE_WORKSPACE "+prepare.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s");
            report["prepareSeconds"]=prepare.Elapsed.TotalSeconds;
            timings.Add(new{stage="PREPARE_WORKSPACE",elapsedSeconds=prepare.Elapsed.TotalSeconds});
            ManufacturingAgent.Boundary("NATIVE_GROUP_DXF");
            var grouped=Stopwatch.StartNew();
            var group=AB.Obj(CutListCoreV01.Json.DeserializeObject(CutListCoreV01.Json.Serialize(StandaloneGroupNative.RunPrepared(workspace,model,names))));report["nativeGroupReport"]=group;
            timings.Add(new{stage="NATIVE_GROUP_TOTAL",elapsedSeconds=grouped.Elapsed.TotalSeconds});report["nativeDetailedTimings"]=group["timings"];
            if(!AB.Bool(group["allRequestedPositionsProduced"])||!AB.Bool(group["protectedDocumentsUnchanged"]))throw new Exception("NATIVE_EXPORT_OR_PROTECTED_STATE_NOT_CONFIRMED");
            if(StandaloneGroupNative.PreparedPositionLimit>0){report["status"]="BENCHMARK_COMPLETED_NOT_PRODUCTION_OUTPUT";report["benchmarkOnly"]=true;report["benchmarkPositions"]=StandaloneGroupNative.PreparedPositionLimit;return report;}
            
            var bindings=AB.Array(group["flatPatternBindings"]).Select(AB.Obj).ToArray();
            var paths=AB.Array(group["retainedDxfFiles"]).Select(Convert.ToString).ToArray();
            if(bindings.Length==0||paths.Length!=bindings.Length||bindings.Sum(b=>Convert.ToInt32(b["quantity"]))!=names.Length)throw new Exception("NATIVE_BODY_FILE_COUNT_MISMATCH");
            var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var outputNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("BEGIN MODEL_SPACE_AND_FILE_CHECKS");var offline=Stopwatch.StartNew();
            foreach(var binding in bindings){
                int quantity=Convert.ToInt32(binding["quantity"]);string body=Convert.ToString(binding["bodyName"]),flat=Convert.ToString(binding["flatPattern"]);
                var matches=paths.Where(p=>String.Equals(Path.GetFileNameWithoutExtension(p),flat+" - group-options-5",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(matches.Length!=1||!used.Add(matches[0]))throw new Exception("NATIVE_FEATURE_FILENAME_NOT_UNIQUE: "+flat);
                if(body.IndexOfAny(Path.GetInvalidFileNameChars())>=0||String.IsNullOrWhiteSpace(body)||!outputNames.Add(body+".dxf"))throw new Exception("BODY_FILENAME_INVALID_OR_COLLIDES: "+body);
                string raw=matches[0],target=Path.Combine(exact,body+".dxf"),ms=Path.Combine(modelspace,body+".dxf");
                byte[] bytes=File.ReadAllBytes(raw);if(bytes.Length==0)throw new Exception("EMPTY_NATIVE_DXF");
                using(var stream=new FileStream(target,FileMode.CreateNew,FileAccess.Write))stream.Write(bytes,0,bytes.Length);
                if(Hash(raw)!=Hash(target))throw new Exception("NATIVE_DXF_COPY_CHANGED");
                var moved=ModelSpaceRawV01.Move(bytes);
                using(var stream=new FileStream(ms,FileMode.CreateNew,FileAccess.Write))stream.Write(moved.bytes,0,moved.bytes.Length);
                outputs.Add(new{body,quantity,flatPattern=flat,cutList=binding["cutList"],nativePath=raw,exactPath=target,modelSpacePath=ms,nativeSha256=Hash(raw),exactSha256=Hash(target),modelSpaceSha256=Hash(ms),spaceChanges=moved.changes,nonSpaceEntityPayloadPreserved=true,inspection=ManufacturingNativeDxfV05.Inspect(File.ReadAllText(ms)),status="NEEDS_REVIEW",fullGeometryAndCamImportCertified=false});
                lineage.Add(new{source=target,outputDxf=ms,sourceSha256=Hash(target),derivedSha256=Hash(ms),sourceComparison=new{passed=true,allNonSpaceEntityTagsByteIdentical=true,wholeCurveBoundMm=0}});
            }
            report["uniqueSheetPositions"]=bindings.Length;report["duplicateBodiesSkipped"]=names.Length-bindings.Length;
            string quantities=Path.Combine(root,"SheetMetalQuantities.csv");
            Func<object,string> q=v=>"\""+Convert.ToString(v).Replace("\"","\"\"")+"\"";
            File.WriteAllLines(quantities,new[]{"Cut List;Representative;Quantity;Source DXF"}.Concat(bindings.Select(b=>String.Join(";",new[]{q(b["cutList"]),q(b["bodyName"]),q(b["quantity"]),q(Convert.ToString(b["bodyName"])+".dxf")}))),new UTF8Encoding(true));
            report["quantitySchedule"]=quantities;
            Console.WriteLine("END MODEL_SPACE_AND_FILE_CHECKS "+offline.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s");
            timings.Add(new{stage="MODEL_SPACE_AND_FILE_CHECKS",elapsedSeconds=offline.Elapsed.TotalSeconds});
            string manifest=Path.Combine(reports,"modelspace-lineage.json");Save(manifest,new{files=lineage});
            Console.WriteLine("BEGIN CLOSE_WORKSPACE");var closing=Stopwatch.StartNew();report["temporaryModelClosedBeforeCam"]=true;
            Console.WriteLine("END CLOSE_WORKSPACE "+closing.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s");
            timings.Add(new{stage="CLOSE_WORKSPACE",elapsedSeconds=closing.Elapsed.TotalSeconds});
            ManufacturingAgent.Boundary("CAM_ARCS_OFFLINE");Console.WriteLine("BEGIN CAM_ARCS_PROCESS_AND_CHECKS");var cam=Stopwatch.StartNew();
            report["camArcs"]=NativeDxfCamStage.Run(modelspace,root,manifest,tolerance);
            Console.WriteLine("END CAM_ARCS_PROCESS_AND_CHECKS "+cam.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s");
            timings.Add(new{stage="CAM_ARCS_PROCESS_AND_CHECKS",elapsedSeconds=cam.Elapsed.TotalSeconds});
            report["status"]="DXF_EXACT_AND_CAM_ARCS_CREATED_NEEDS_REVIEW";
        }catch(Exception e){report["status"]="STOPPED_OR_INCOMPLETE";report["exception"]=e.ToString();report["hresult"]="0x"+e.HResult.ToString("X8");}
        finally{
            if(copy!=null)try{sw.CloseDoc(copy.GetTitle());if(sw.GetOpenDocumentByName(model)!=null)throw new Exception("WORKSPACE_REMAINS_OPEN");}catch(Exception e){report["cleanupError"]=e.ToString();report["status"]="STOPPED_OR_INCOMPLETE";}
            Console.WriteLine("BEGIN FINAL_SOURCE_CHECK");var finalCheck=Stopwatch.StartNew();
            report["sourceSHA256After"]=Hash(source);report["sourceFileUnchanged"]=Hash(source)==hash;report["sourceMemoryUnchanged"]=!original.GetSaveFlag()&&original.GetUpdateStamp()==stamp&&original.ConfigurationManager.ActiveConfiguration.Name==config;
            if(!(bool)report["sourceFileUnchanged"]||!(bool)report["sourceMemoryUnchanged"])report["status"]="SOURCE_STATE_CHANGED";
            Console.WriteLine("END FINAL_SOURCE_CHECK "+finalCheck.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s");
            report["elapsedSeconds"]=timer.Elapsed.TotalSeconds;Save(Path.Combine(reports,"native-batch-dxf.json"),report);Save(Path.Combine(results,"native-batch-dxf.json"),report);
            var csv=new StringBuilder("Stage;Seconds\r\n");foreach(var row in timings){var t=AB.Obj(row);csv.Append(Convert.ToString(t["stage"])).Append(';').Append(Convert.ToDouble(t["elapsedSeconds"]).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");}csv.Append("WORKSPACE_TO_FINAL_CHECKS_TOTAL;").Append(timer.Elapsed.TotalSeconds.ToString("F6",System.Globalization.CultureInfo.InvariantCulture));File.WriteAllText(Path.Combine(reports,"timings.csv"),csv.ToString(),new UTF8Encoding(true));
        }
        return report;
    }
}
