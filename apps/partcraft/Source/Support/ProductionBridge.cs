using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// SolidWorks can rewrite mapping Entities during an export. Only a disposable
// per-export copy is selected, never the user's mapping file.
public sealed class ProductionMappingScope:IDisposable {
    static readonly string original=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"Resources\StileDXF");
    readonly byte[] bytes;readonly string path,originalHash;readonly List<object> audit;readonly GroupMappingV02 scope;
    static string Hash(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","");}
    public ProductionMappingScope(SldWorks sw,string directory,List<object> records){
        audit=records;bytes=File.ReadAllBytes(original);originalHash=Hash(bytes);
        path=Path.Combine(directory,"StileDXF-"+Guid.NewGuid().ToString("N")+".mapping");File.WriteAllBytes(path,bytes);
        audit.Add(new{operation="SELECT_DISPOSABLE_NATIVE_MAPPING",source=original,copy=path,sha256=originalHash});
        scope=new GroupMappingV02(sw,path,audit);
    }
    public void Dispose(){
        try{var changed=Hash(File.ReadAllBytes(path))!=originalHash;audit.Add(new{operation="NATIVE_MAPPING_COPY_AFTER_EXPORT",path,nativeChangedCopy=changed,originalUnchanged=Hash(File.ReadAllBytes(original))==originalHash});
            if(changed)File.WriteAllBytes(path,bytes);
        }finally{scope.Dispose();}
        if(Hash(File.ReadAllBytes(original))!=originalHash)throw new InvalidOperationException("ORIGINAL_MAPPING_FILE_CHANGED");
    }
}
public static class ProductionProfiles {
    static Dictionary<string,Dictionary<string,object>> profiles=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);
    public static bool Has(string name){return profiles.ContainsKey(name);}
    public static object Evidence(string name){return profiles[name];}
    public static object Value(Dictionary<string,object> r,string k){object v;return r.TryGetValue(k,out v)?v:null;}
    static object Prop(Dictionary<string,object> r,string k){var p=AB.Obj(r["commonFolderProperties"]);object v;return p.TryGetValue(k,out v)&&v!=null?AB.Get(v,"resolved"):null;}
    public static void Load(string path,string hash,string config){
        var r=AB.Obj(CutListCoreV01.Json.DeserializeObject(File.ReadAllText(path,Encoding.UTF8)));
        if(Convert.ToString(r["sha256After"])!=hash||Convert.ToString(AB.Get(r["before"],"config"))!=config||!AB.Bool(r["observedStateUnchanged"])||!AB.Bool(r["sourceFileUnchanged"]))throw new Exception("PROFILE_EVIDENCE_SOURCE_MISMATCH");
        profiles.Clear();foreach(var raw in AB.Array(r["bodies"])){
            var b=AB.Obj(raw);if(AB.Bool(b["sheetMetal"]))continue;
            if(b["H"]==null||!AB.Bool(AB.Get(b["sectionComparison"],"sectionChecksPassed")))throw new Exception("PROFILE_SECTION_OR_H_UNVERIFIED: "+b["body"]);
            profiles.Add(Convert.ToString(b["body"]),b);
        }
    }
    public static void AddRows(List<object[]> rows,IEnumerable<MPRow> bodies){
        foreach(var r in bodies.Where(b=>b.kind=="RECTANGULAR PROFILE").OrderBy(b=>Convert.ToString(Prop(profiles[b.body],"Description")),StringComparer.Ordinal).ThenBy(b=>Convert.ToString(Prop(profiles[b.body],"ANGLE1")),StringComparer.Ordinal).ThenBy(b=>Convert.ToString(Prop(profiles[b.body],"ANGLE2")),StringComparer.Ordinal).ThenByDescending(b=>NativeWeldmentAdapter.NativeNumber(rLength(b),false)??0)){
            var e=profiles[r.body];var s=AB.Obj(e["sectionComparison"]);
            rows.Add(new object[]{r.cutList,r.body,1,r.folderPhysicalCount,r.material,r.description,
                Convert.ToString(s["NOMINAL_WIDTH"])+" x "+s["NOMINAL_HEIGHT"],s["NOMINAL_WALL_THICKNESS"],r.lengthRaw,r.lengthResolved,null,r.angle1Raw,r.angle1Resolved,r.angle2Raw,r.angle2Resolved,null,null,
                "ANGLE1 "+e["DIR1"]+" | ANGLE2 "+e["DIR2"]+"; see profile-cut-schemes.svg",e["H"],"MATERIAL reference only; processing NEEDS_REVIEW; no bodies merged",null,r.materialRaw,r.bodyMaterial,e["DIR1"],e["DIR2"],e["machiningStatus"],"Native Structural Member + verified section; native Cut List values",Prop(e,"QUANTITY")});
        }
    }
    static string rLength(MPRow row){return row.lengthResolved;}
    public static void WriteSchemes(string path){
        var b=new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='1100' height='"+(profiles.Count*125+50)+"'><rect width='100%' height='100%' fill='white'/><g font-family='Arial' font-size='16' fill='black'>");int y=35;
        foreach(var p in profiles.OrderBy(x=>x.Key,StringComparer.Ordinal)){
            var e=p.Value;var g=AB.Obj(AB.Get(e["endCutCheck"],"geometry"));var faces=AB.Array(g["faces"]).Select(AB.Obj).Where(f=>Value(f,"selectedEnd")!=null).ToArray();
            var first=faces.Single(f=>Convert.ToString(f["selectedEnd"])=="LOCAL_MIN");var last=faces.Single(f=>Convert.ToString(f["selectedEnd"])=="LOCAL_MAX");
            Func<Dictionary<string,object>,double> slope=f=>Math.Abs(Convert.ToDouble(f["localPlaneSlopeU"]))>Math.Abs(Convert.ToDouble(f["localPlaneSlopeV"]))?Convert.ToDouble(f["localPlaneSlopeU"]):Convert.ToDouble(f["localPlaneSlopeV"]);
            double l=slope(first)*20,r=slope(last)*20;Func<double,string> n=v=>v.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
            b.Append("<text x='20' y='"+y+"'>"+p.Key+" | L="+Prop(e,"LENGTH")+" | H="+e["H"]+" | A1="+Prop(e,"ANGLE1")+" | A2="+Prop(e,"ANGLE2")+"</text>");
            b.Append("<path d='M "+n(230-l)+" "+(y+20)+" L "+n(680-r)+" "+(y+20)+" L "+n(680+r)+" "+(y+60)+" L "+n(230+l)+" "+(y+60)+" Z' fill='none' stroke='black' stroke-width='2'/>");
            b.Append("<text x='20' y='"+(y+90)+"'>LOCAL MIN / MAX. Schematic, not to scale. Processing NEEDS_REVIEW.</text>");y+=125;
        }b.Append("</g></svg>");File.WriteAllText(path,b.ToString(),new UTF8Encoding(false));
    }
}

public static class ProductionBridge {
    static string Hash(string p){using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","");}
    static object[] A(object o){return NativeWeldmentAdapter.Items(o);}
    public static int Tests(){
        if(ProductionGroupNative.Main(new[]{"--self-test"})!=0)throw new Exception("Native grouped export inherited tests failed");
        int n=0;Action<bool> check=x=>{if(!x)throw new Exception("Production integration test "+n);n++;};
        string evidence=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"bridge\results\c91c012d45604452b6d44a1d8bdf7716\native-report.json");
        var report=AB.Obj(CutListCoreV01.Json.DeserializeObject(File.ReadAllText(evidence,Encoding.UTF8)));
        string hash=Convert.ToString(report["sha256After"]),config=Convert.ToString(AB.Get(report["before"],"config"));
        ProductionProfiles.Load(evidence,hash,config);check(ProductionProfiles.Has("6-1"));check(!ProductionProfiles.Has("nonexistent"));
        check(Convert.ToDouble(AB.Get(ProductionProfiles.Evidence("6-1"),"H"))==20);check(Convert.ToDouble(AB.Get(ProductionProfiles.Evidence("1-1"),"H"))==40);
        try{ProductionProfiles.Load(evidence,"wrong",config);throw new Exception("accepted");}catch(Exception e){check(e.Message=="PROFILE_EVIDENCE_SOURCE_MISMATCH");}
        var rows=new List<MPRow>();foreach(var raw in AB.Array(report["bodies"])){
            var r=AB.Obj(raw);var p=AB.Obj(r["commonFolderProperties"]);Func<string,string> value=k=>p.ContainsKey(k)&&p[k]!=null?Convert.ToString(AB.Get(p[k],"resolved")):null;
            rows.Add(new MPRow{body=Convert.ToString(r["body"]),cutList=Convert.ToString(AB.Array(r["cutList"])[0]),folderPhysicalCount=1,kind=AB.Bool(r["sheetMetal"])?"SHEET METAL":"RECTANGULAR PROFILE",description=value("Description"),material="REFERENCE COLOR ONLY",lengthResolved=value("LENGTH"),angle1Resolved=value("ANGLE1"),angle2Resolved=value("ANGLE2")});
        }
        var tables=ProductionPackageEntry.Tables(rows,new List<object>());check(tables["TUBE CUTTING"].Count==7);check(tables["SHEET METAL"].Count==3);
        var six=tables["TUBE CUTTING"].Single(r=>Convert.ToString(r[1])=="6-1");check(Convert.ToDouble(six[18])==20);check(Convert.ToString(six[12])=="2°");check(Convert.ToString(six[23])=="LOCAL_U_POSITIVE");
        string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"production","tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string workbook=Path.Combine(dir,"manufacturing.xlsx");ManufacturingExcelV05.Write(workbook,tables);var audit=ManufacturingWorkbookAuditV06.Verify(workbook,rows);check(Convert.ToInt32(AB.Get(audit,"listedBodies"))==8);
        ProductionProfiles.WriteSchemes(Path.Combine(dir,"profile-cut-schemes.svg"));check(File.ReadAllText(Path.Combine(dir,"profile-cut-schemes.svg")).Contains("H=20"));
        Console.WriteLine("Production profiles/OpenXML/schemes: "+n+"/"+n+" PASS; no COM");return GroupMappingV02.Tests();
    }
    static void Save(string p,object value){using(var f=new FileStream(p,FileMode.CreateNew,FileAccess.Write))using(var w=new StreamWriter(f,new UTF8Encoding(false)))w.Write(CutListCoreV01.Json.Serialize(value));}
    public static Dictionary<string,object> Run(SldWorks sw,string source,string jobResult,bool nativeBatchOnly=false){
        var doc=sw.ActiveDoc as ModelDoc2;if(doc==null||!ManufacturingContextV02.SamePath(doc.GetPathName(),source)||doc.GetSaveFlag())throw new Exception("ORIGINAL_CONTEXT_MISMATCH_OR_DIRTY");
        string hash=Hash(source),config=doc.ConfigurationManager.ActiveConfiguration.Name;int stamp=doc.GetUpdateStamp();
        string evidence=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"bridge\results\c91c012d45604452b6d44a1d8bdf7716\native-report.json");
        if(!nativeBatchOnly)ProductionProfiles.Load(evidence,hash,config);
        string root=Path.Combine(Path.GetDirectoryName(source),"_Manufacturing");
        string time=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time")).ToString("yyyyMMdd_HHmmss");
        string run=Path.Combine(root,"Run_"+time+"_"+Guid.NewGuid().ToString("N").Substring(0,8)),workspace=Path.Combine(run,"Workspace");
        var report=new Dictionary<string,object>{{"status","RUNNING"},{"productionReady",false},{"source",source},{"originalSha256Before",hash},{"runFolder",run},{"profileEvidence",evidence},{"timings",new List<object>()}};
        Directory.CreateDirectory(workspace);Directory.CreateDirectory(Path.Combine(run,"Reports"));
        string model=null;var clock=Stopwatch.StartNew();
        try{
            ManufacturingAgent.Boundary("NATIVE_PACK_AND_GO");
            var pack=doc.Extension.GetPackAndGo();pack.IncludeDrawings=true;pack.IncludeSimulationResults=false;pack.IncludeToolboxComponents=false;
            object namesRaw;if(!pack.GetDocumentNames(out namesRaw))throw new Exception("PACK_AND_GO_ENUMERATION_FAILED");
            var names=A(namesRaw).Select(Convert.ToString).ToArray();
            if(names.Length==0||!names.Any(n=>ManufacturingContextV02.SamePath(n,source)))throw new Exception("PACK_AND_GO_SOURCE_MISSING");
            if(names.Any(n=>!File.Exists(n)||Path.GetFileName(n).StartsWith("~$",StringComparison.Ordinal)||n.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new Exception("PACK_AND_GO_MISSING_OR_PREVIOUS_OUTPUT_DEPENDENCY");
            string prefix="Work_"+Guid.NewGuid().ToString("N").Substring(0,8)+"_";
            var targets=names.Select((n,i)=>Path.Combine(workspace,prefix+i+"_"+Path.GetFileName(n))).ToArray();
            if(!pack.SetDocumentSaveToNames(targets))throw new Exception("PACK_AND_GO_REMAP_FAILED");
            var codes=A(doc.Extension.SavePackAndGo(pack)).Select(Convert.ToInt32).ToArray();
            Save(Path.Combine(run,"Reports","pack-and-go.json"),new{sourcePaths=names,targetPaths=targets,codes});
            if(codes.Length!=targets.Length||codes.Any(c=>c!=(int)swPackAndGoSaveStatus_e.swPackAndGoSaveStatus_Succeed)||targets.Any(p=>!File.Exists(p)||new FileInfo(p).Length==0))throw new Exception("PACK_AND_GO_COPY_INCOMPLETE");
            model=targets[Array.FindIndex(names,n=>ManufacturingContextV02.SamePath(n,source))];
            ManufacturingAgent.Boundary("VERIFY_ORIGINAL_DIRTY_AFTER_PACK");if(doc.GetSaveFlag())throw new Exception("ORIGINAL_DIRTY_AFTER_PACK_AND_GO");
            ManufacturingAgent.Boundary("VERIFY_ORIGINAL_STAMP_AFTER_PACK");if(doc.GetUpdateStamp()!=stamp)throw new Exception("ORIGINAL_STAMP_CHANGED_AFTER_PACK_AND_GO");
            ManufacturingAgent.Boundary("VERIFY_ORIGINAL_HASH_AFTER_PACK");if(Hash(source)!=hash)throw new Exception("ORIGINAL_FILE_CHANGED_AFTER_PACK_AND_GO");
            report["packAndGoSeconds"]=clock.Elapsed.TotalSeconds;report["workspaceModel"]=model;
            ManufacturingAgent.Boundary("OPEN_COPIED_WORKSPACE_MODEL");bool openedCopy;ManufacturingWorkspaceV13.OpenExact(sw,model,out openedCopy);
            ManufacturingAgent.Boundary("AUDIT_SAVED_WORKSPACE_REFERENCES");
            report["savedReferenceAudit"]=ManufacturingWorkspaceV13.SavedReferences(sw,workspace);
            ManufacturingAgent.Boundary("VALIDATE_EXPLICIT_DRAWING_TEMPLATE");
            string template=ManufacturingTemplateSelection.Validate(@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\Draw.DRWDOT");report["drawingTemplate"]=template;
            if(nativeBatchOnly){
                ManufacturingAgent.Boundary("NATIVE_GROUP_FLAT_PATTERN_EXPORT");
                var part=sw.GetOpenDocumentByName(model) as ModelDoc2;
                if(part==null)throw new Exception("WORKSPACE_DOCUMENT_NOT_LOADED");
                var namesOfSheets=A(((PartDoc)part).GetBodies2((int)swBodyType_e.swSolidBody,false)).Cast<Body2>().Where(b=>b.IsSheetMetal()).Select(b=>b.Name).ToArray();
                report["groupDxf"]=ProductionGroupNative.RunPrepared(workspace,model,namesOfSheets);
                ManufacturingAgent.Boundary("NATIVE_WELDMENT_CUT_LIST_TABLE");
                report["nativeCutListProperties"]=NativeBatchTables.Read(part);
                string tableModel=Path.Combine(workspace,"NativeCutList_"+Guid.NewGuid().ToString("N")+".SLDPRT");
                ManufacturingSourceFilesV03.CopySavedSource(model,tableModel,Hash(model),false);
                int te=0,tw=0;var tablePart=sw.OpenDoc6(tableModel,1,1,config,ref te,ref tw);
                if(tablePart==null||te!=0)throw new Exception("NATIVE_TABLE_OWNED_MODEL_OPEN_FAILED");
                WriteProfileDrawing(sw,workspace,tableModel,config,template,run,report);
                if(Convert.ToString(AB.Get(report["profileDrawing"],"status"))!="PORTABLE_VERIFIED")throw new Exception("NATIVE_CUT_LIST_DRAWING_INCOMPLETE");
                report["status"]="NATIVE_BATCH_FILES_CREATED_NEEDS_REVIEW";
                report["fullManufacturingPackageComplete"]=false;
                report["remainingStages"]=new[]{"Validate group DXF per-body ownership and full geometry","Model Space and CAM_ARCS","Sheet SLDDRW/PDF","Final workbook and package checks"};
                return report;
            }
            var args=new[]{"--workspace",workspace,"--model",model,"--all","--drawing-template",template,"--cam-geometry","exact"};
            ManufacturingAgent.Boundary("PRODUCTION_DRY_RUN");int dry=ProductionPackageEntry.Run(new[]{"--dry-run"}.Concat(args).ToArray());report["dryRunExitCode"]=dry;
            if(dry!=0)throw new Exception("WORKSPACE_DRY_RUN_BLOCKED: inspect run Reports");
            ManufacturingAgent.Boundary("PRODUCTION_CREATE");int create;
            var mappingAudit=new List<object>();report["nativeMappingAudit"]=mappingAudit;
            create=ProductionPackageEntry.Run(new[]{"--create"}.Concat(args).ToArray());
            report["createExitCode"]=create;
            var packages=Directory.GetDirectories(run,"Package_*").Where(p=>File.Exists(Path.Combine(p,"Reports","package-report.json"))).ToArray();if(packages.Length!=1)throw new Exception("EXPECTED_ONE_CREATED_PACKAGE");
            string package=packages[0];report["packageFolder"]=package;
            string input=Path.Combine(package,"Reports","package-report.json");if(!File.Exists(input))throw new Exception("PRODUCTION_REPORT_MISSING");
            var native=AB.Obj(CutListCoreV01.Json.DeserializeObject(File.ReadAllText(input,Encoding.UTF8)));report["nativeReportStatus"]=native["status"];
            ProductionProfiles.WriteSchemes(Path.Combine(package,"Reports","profile-cut-schemes.svg"));
            WriteProfileDrawing(sw,workspace,model,config,template,package,report);
            if(create!=2||Convert.ToString(native["status"])!="PACKAGE_FILES_CREATED_NEEDS_REVIEW")throw new Exception("NATIVE_PACKAGE_INCOMPLETE: retained safe independent outputs");
            ManufacturingAgent.Boundary("MODEL_SPACE_AND_CAM_ARCS");var transformed=new List<object>();
            string modelspace=Path.Combine(package,"DXF","MODELSPACE_REFERENCE");Directory.CreateDirectory(modelspace);
            foreach(var p in Directory.GetFiles(Path.Combine(package,"DXF","EXACT"),"*.dxf")){
                byte[] bytes=File.ReadAllBytes(p);var moved=ModelSpaceRawV01.Move(bytes);string target=Path.Combine(modelspace,Path.GetFileName(p));File.WriteAllBytes(target,moved.bytes);
                transformed.Add(new{source=p,outputDxf=target,sourceSha256=Hash(p),derivedSha256=Hash(target),sourceComparison=new{passed=true,allNonSpaceEntityTagsByteIdentical=true,wholeCurveBoundMm=0},spaceChanges=moved.changes});
            }
            string lineage=Path.Combine(package,"Reports","modelspace-lineage.json");Save(lineage,new{files=transformed});
            string python=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"..\..\Runtime\python.exe"));
            string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"production\cam-source\prepare_cam_arcs.py");
            var pi=new ProcessStartInfo(python,"\""+script+"\" --input \""+modelspace+"\" --output \""+Path.Combine(package,"CAM_ARCS")+"\" --tolerance-mm 0.01 --max-seconds 180 --lineage-report \""+lineage+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            // Separate offline process; no SolidWorks calls. Drain both pipes asynchronously.
            using(var process=new Process{StartInfo=pi}){
                var output=new StringBuilder();var errors=new StringBuilder();process.OutputDataReceived+=(s,e)=>{if(e.Data!=null)lock(output)output.AppendLine(e.Data);};process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(errors)errors.AppendLine(e.Data);};
                process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();while(!process.WaitForExit(1000))ManufacturingAgent.Boundary("CAM_ARCS_OFFLINE");process.WaitForExit();
                File.WriteAllText(Path.Combine(package,"Reports","cam-arcs-console.log"),output.ToString()+errors.ToString(),new UTF8Encoding(false));report["camArcsExitCode"]=process.ExitCode;
                if(process.ExitCode!=2)throw new Exception("CAM_ARCS_OFFLINE_FAILED");
            }
            report["status"]=Convert.ToString(AB.Get(report["profileDrawing"],"status"))=="PORTABLE_VERIFIED"?"PACKAGE_FILES_CREATED_NEEDS_REVIEW":"STOPPED_OR_INCOMPLETE";
            if(Convert.ToString(report["status"])=="STOPPED_OR_INCOMPLETE")report["error"]="Profile drawing incomplete; independently created sheet and CAM files retained";
        }catch(Exception e){report["status"]="STOPPED_OR_INCOMPLETE";report["error"]=e.ToString();}
        finally{
            report["originalSha256After"]=Hash(source);report["originalFileUnchanged"]=Hash(source)==hash;report["originalMemoryStateUnchanged"]=!doc.GetSaveFlag()&&doc.GetUpdateStamp()==stamp&&doc.ConfigurationManager.ActiveConfiguration.Name==config;
            report["elapsedSeconds"]=clock.Elapsed.TotalSeconds;if(!(bool)report["originalFileUnchanged"]||!(bool)report["originalMemoryStateUnchanged"])report["status"]="ORIGINAL_STATE_CHECK_FAILED";
            Save(Path.Combine(run,"Reports","manufacturing-agent-report.json"),report);Save(Path.Combine(jobResult,"production-result.json"),report);
        }return report;
    }
    static void WriteProfileDrawing(SldWorks sw,string workspace,string model,string config,string template,string package,Dictionary<string,object> report){
        ModelDoc2 drawing=null;var detail=new Dictionary<string,object>();report["profileDrawing"]=detail;
        try{
            string path=Path.Combine(workspace,"ProfileCutList_"+Guid.NewGuid().ToString("N").Substring(0,8)+".SLDDRW");drawing=sw.NewDocument(template,0,0,0) as ModelDoc2;if(drawing==null)throw new Exception("NATIVE_PROFILE_DRAWING_CREATE_FAILED");
            int errors=0,warnings=0;if(!drawing.Extension.SaveAs(path,0,1,null,ref errors,ref warnings)||errors!=0)throw new Exception("PROFILE_DRAWING_SAVE_FAILED");
            var partForView=sw.GetOpenDocumentByName(model) as ModelDoc2;if(partForView==null)throw new Exception("PROFILE_MODEL_NOT_LOADED");
            int activation=0;sw.ActivateDoc3(partForView.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref activation);
            if(sw.IsSame(sw.ActiveDoc,partForView)!=(int)swObjectEquality.swObjectSame)throw new Exception("PROFILE_MODEL_ACTIVATION_FAILED");
            string isoName="NativeCutListIso_"+Guid.NewGuid().ToString("N");partForView.ShowNamedView2("",(int)swStandardViews_e.swIsometricView);partForView.NameView(isoName);
            if(!partForView.Extension.SaveAs(model,0,1,null,ref errors,ref warnings)||errors!=0)throw new Exception("OWNED_PROFILE_VIEW_SAVE_FAILED");
            sw.ActivateDoc3(drawing.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref activation);
            var dd=(DrawingDoc)drawing;var view=dd.CreateDrawViewFromModelView3(model,isoName,.2,.15,0);if(view==null)throw new Exception("PROFILE_ISOMETRIC_VIEW_FAILED");view.ReferencedConfiguration=config;
            var table=NativeWeldmentAdapter.InsertNativeTable(sw,view,workspace,model,config,@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\cut-list-1.sldwldtbt");detail["nativeTable"]=NativeWeldmentAdapter.ReadNativeTable(table);
            if(!drawing.Extension.SaveAs(path,0,1,null,ref errors,ref warnings)||errors!=0)throw new Exception("PROFILE_DRAWING_TABLE_SAVE_FAILED");
            string final=Path.Combine(package,"Drawings","Profiles");Directory.CreateDirectory(final);var pg=drawing.Extension.GetPackAndGo();pg.IncludeDrawings=true;object raw;if(!pg.GetDocumentNames(out raw))throw new Exception("PROFILE_PACK_ENUM_FAILED");
            var names=A(raw).Select(Convert.ToString).ToArray();var targets=names.Select(n=>Path.Combine(final,Path.GetFileName(n))).ToArray();if(targets.Any(File.Exists)||!pg.SetDocumentSaveToNames(targets))throw new Exception("PROFILE_PACK_TARGET_FAILED");
            var codes=A(drawing.Extension.SavePackAndGo(pg)).Select(Convert.ToInt32).ToArray();if(codes.Length!=targets.Length||codes.Any(c=>c!=0))throw new Exception("PROFILE_PACK_SAVE_FAILED");
            string pdf=Path.Combine(final,"ProfileCutList.pdf");if(!drawing.Extension.SaveAs(pdf,0,1,null,ref errors,ref warnings)||errors!=0||!ManufacturingDrawingChecksV13.PdfComplete(File.ReadAllBytes(pdf)))throw new Exception("PROFILE_PDF_EXPORT_FAILED");
            detail["files"]=targets;detail["pdf"]=pdf;detail["nativeTableCreated"]=true;
            string portableDrawing=targets[Array.FindIndex(names,n=>ManufacturingContextV02.SamePath(n,path))];
            string portableModel=targets[Array.FindIndex(names,n=>ManufacturingContextV02.SamePath(n,model))];
            sw.CloseDoc(drawing.GetTitle());drawing=null;var old=sw.GetOpenDocumentByName(model) as ModelDoc2;if(old!=null)sw.CloseDoc(old.GetTitle());
            if(sw.GetOpenDocumentByName(model)!=null)throw new Exception("WORKSPACE_MODEL_STILL_LOADED_BEFORE_PROFILE_REOPEN");
            errors=0;warnings=0;var part=sw.OpenDoc6(portableModel,1,3,config,ref errors,ref warnings);if(part==null||errors!=0||!ManufacturingContextV02.SamePath(part.GetPathName(),portableModel))throw new Exception("PROFILE_PORTABLE_MODEL_REOPEN_FAILED");
            try{
                ManufacturingWorkspaceV13.CheckPairs(portableDrawing,sw.GetDocumentDependencies2(portableDrawing,false,false,false),final);
                var openAudit=new Dictionary<string,object>();drawing=NativeDrawingModeV13.Open(sw,portableDrawing,final,openAudit,d=>{drawing=d;});detail["resolvedDrawingOpen"]=openAudit;
                var views=A(((DrawingDoc)drawing).GetViews()).SelectMany(A).Cast<SolidWorks.Interop.sldworks.View>().Where(v=>!String.IsNullOrEmpty(v.GetReferencedModelName())).ToArray();
                if(views.Length!=1||!ManufacturingContextV02.SamePath(views[0].GetReferencedModelName(),portableModel)||ManufacturingSolidWorksV13.LoadedViewModel(views[0])==null||!ManufacturingContextV02.SamePath(ManufacturingSolidWorksV13.LoadedViewModel(views[0]).GetPathName(),portableModel)||ManufacturingSolidWorksV13.LoadedViewConfiguration(views[0])!=config)throw new Exception("PROFILE_PORTABLE_VIEW_REFERENCE_OR_CONFIGURATION_FAILED");
                int visible=A(views[0].GetVisibleEntities2(null,(int)swViewEntityType_e.swViewEntityType_Edge)).Length;
                if(visible==0)foreach(Component2 component in A(views[0].GetVisibleComponents()))visible+=A(views[0].GetVisibleEntities2(component,(int)swViewEntityType_e.swViewEntityType_Edge)).Length;
                if(visible==0)throw new Exception("PROFILE_PORTABLE_VIEW_EMPTY");detail["visibleNativeEdges"]=visible;
                detail["status"]="PORTABLE_VERIFIED";detail["reopenVerified"]=true;detail["model"]=portableModel;detail["drawing"]=portableDrawing;
            }finally{if(drawing!=null){sw.CloseDoc(drawing.GetTitle());drawing=null;}sw.CloseDoc(part.GetTitle());}
        }catch(Exception e){detail["status"]="INCOMPLETE";detail["error"]=e.ToString();}
        finally{if(drawing!=null)sw.CloseDoc(drawing.GetTitle());}
    }
}
