using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public class MPRow {
    public string body,cutList,kind,material,materialRaw,bodyMaterial,description,lengthRaw,lengthResolved,angle1Raw,angle1Resolved,angle2Raw,angle2Resolved;
    public int? folderPhysicalCount;public MPTube tube;public MPPlate plate;public List<string> warnings=new List<string>();public object output;
}
class ProductionPackageEntry {

    public const string ReviewedMacroHash="741E1216878BD4C150F88223C27B781E5053E291F362F7DEDCA450ACE614E2AB";
    static string Hash(string path){return ManufacturingSourceFilesV03.Hash(path,"HASH_OTHER_INPUT_OR_OUTPUT");}
    public static CLProperty Property(CLFolder f,params string[] names){return f.properties.FirstOrDefault(p=>names.Any(n=>String.Equals(n,p.name,StringComparison.OrdinalIgnoreCase)));}
    static string Value(CLProperty p){return p==null?null:p.resolvedValue;}
    static string BendText(object data){
        if(data==null)return null;var rows=AB.Array(data);if(rows.Length==0)return "НЕ ПРОВЕРЕНО";
        var lines=new List<string>();foreach(var r in rows){
            object feature,angle,radius;AB.TryGet(r,"feature",out feature);AB.TryGet(r,"angleDegrees",out angle);AB.TryGet(r,"effectiveRadiusMm",out radius);
            lines.Add((feature??"Bend").ToString()+": "+(angle==null?"НЕ ПРОВЕРЕНО":AB.Num(angle).ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+"°")+
                "; R "+(radius==null?"НЕ ПРОВЕРЕНО":AB.Num(radius).ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" mm"));
        }return String.Join("\n",lines);
    }
    static string DirectionText(object data){
        if(data==null)return null;var rows=AB.Array(data);if(rows.Length==0)return "НЕ ПРОВЕРЕНО";var lines=new List<string>();int index=0;
        foreach(var r in rows){index++;object code;string name="НЕ ПРОВЕРЕНО";if(AB.TryGet(r,"code",out code)&&code!=null){int value=AB.Int(code);name=value==1?"UP":value==2?"DOWN":value==0?"NOT_BEND_LINE":"НЕ ПРОВЕРЕНО";}lines.Add("Native line "+index+": "+name);}
        return String.Join("\n",lines);
    }
    public static Dictionary<string,List<object[]>> Tables(List<MPRow> rows,List<object> checks){
        var result=new Dictionary<string,List<object[]>>();
        var tube=new List<object[]>{new object[]{"Cut List","Body","Qty (this body)","Bodies in folder","Material (Cut List resolved)","Description cached","Actual OD mm","Wall mm","LENGTH raw","LENGTH resolved","Maximum axial span mm","ANGLE1 raw","ANGLE1 resolved","ANGLE2 raw","ANGLE2 resolved","Geometric end A angle deg (axis-relative)","Geometric end B angle deg (axis-relative)","Cut scheme (diagnostic)","H","Warnings","Axis/end-plane length mm (diagnostic)","Material raw","Body material API","DIR1 local","DIR2 local","Additional processing","Profile source","Cut List QUANTITY"}};
        foreach(var r in rows.Where(r=>r.kind=="TUBE").GroupBy(r=>r.cutList,StringComparer.Ordinal)
            .OrderBy(g=>g.Min(r=>Math.Min(r.tube.angleLeftDeg??double.PositiveInfinity,r.tube.angleRightDeg??double.PositiveInfinity)))
            .ThenBy(g=>g.Key,StringComparer.Ordinal).SelectMany(g=>g.OrderByDescending(r=>r.tube.axisPlaneIntersectionLengthMm??r.tube.maximumAxialSpanMm??0)))
            tube.Add(new object[]{r.cutList,r.body,1,r.folderPhysicalCount,r.material,r.description,r.tube.outerDiameterMm,r.tube.wallMm,r.lengthRaw,r.lengthResolved,r.tube.maximumAxialSpanMm,
                r.angle1Raw,r.angle1Resolved,r.angle2Raw,r.angle2Resolved,r.tube.angleLeftDeg,r.tube.angleRightDeg,r.tube.cutScheme+"\n"+ManufacturingGeometryV01.CuttingSchematic(r.tube),"N/A — ROUND",String.Join(" | ",r.warnings.Concat(r.tube.warnings)),r.tube.axisPlaneIntersectionLengthMm,r.materialRaw,r.bodyMaterial});
        ProductionProfiles.AddRows(tube,rows);tube[0][6]="Assigned profile / actual OD mm";result["TUBE CUTTING"]=tube;
        foreach(var kind in new[]{"SHEET METAL","PLATES"}){
            var sheet=new List<object[]>{new object[]{"Cut List","Body","Qty (this body)","Bodies in folder","Material","Thickness mm","DXF / SLDDRW / PDF","Warnings","BREP / production certification","Bend lines","Body material API","Native bend parameters","Native line directions"}};
            foreach(var r in rows.Where(r=>r.kind==kind)){
                object thickness=r.plate==null?null:(object)r.plate.thicknessMm;
                if(kind=="SHEET METAL"&&r.output!=null){object v;if(AB.TryGet(r.output,"thicknessMm",out v))thickness=v;}
                var paths=new List<string>();object bends=null;
                if(r.output!=null){foreach(var key in new[]{"dxf","exactPath","camDxf","bendDxf","drw","pdf"}){object path;if(AB.TryGet(r.output,key,out path)&&path!=null&&!paths.Contains(path.ToString()))paths.Add(path.ToString());}AB.TryGet(r.output,"bendLines",out bends);}
                object parameters=null,directions=null;if(r.output!=null){AB.TryGet(r.output,"bendParameters",out parameters);AB.TryGet(r.output,"lineDirections",out directions);}
                sheet.Add(new object[]{r.cutList,r.body,1,r.folderPhysicalCount,r.material,thickness,r.output==null?"NOT_CREATED":String.Join("\n",paths),String.Join(" | ",r.warnings),"NOT_CERTIFIED",bends,r.bodyMaterial,
                    BendText(parameters),DirectionText(directions)});
            }
            result[kind]=sheet;
        }
        result["CHECKS"]=new List<object[]>{new object[]{"Check / severity","Details"}};
        foreach(var c in checks)result["CHECKS"].Add(new object[]{"CHECK",CutListCoreV01.Json.Serialize(c)});
        foreach(var r in rows)foreach(var warning in r.warnings)result["CHECKS"].Add(new object[]{r.body,warning});
        foreach(var r in rows.Where(r=>r.kind=="TUBE")){
            foreach(var warning in r.tube.warnings)result["CHECKS"].Add(new object[]{r.body,warning});
            result["CHECKS"].Add(new object[]{r.body+" — GEOMETRY / CACHED SOURCES",CutListCoreV01.Json.Serialize(new{r.lengthRaw,r.lengthResolved,r.angle1Raw,r.angle1Resolved,r.angle2Raw,r.angle2Resolved,r.materialRaw,r.bodyMaterial,geometry=r.tube})});
        }
        result["CHECKS"].Add(new object[]{"ANGLE CONVENTION","SolidWorks cutting angles: direct cut=0°. '-' retained. No 90° complement substituted."});
        result["CHECKS"].Add(new object[]{"SOURCE","Existing Cut List grouping is not a BREP certificate; no folders or quantities merged."});
        return result;
    }
    static bool RequiredBodyFiles(object result,string cam){
        if(result==null)return false;foreach(string key in new[]{"exactPath","bendDxf","drw","referencePart","pdf"}.Concat(cam=="polyline"?new[]{"camDxf","dxf"}:new string[0])){object path;if(!AB.TryGet(result,key,out path)||!(path is string)||!File.Exists((string)path)||new FileInfo((string)path).Length==0)return false;}
        object publication,status;return AB.TryGet(result,"publication",out publication)&&AB.TryGet(publication,"status",out status)&&Convert.ToString(status)=="PORTABLE_VERIFIED";
    }
    [STAThread]
    public static int Run(string[] args){
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        if(args.Length==1&&args[0]=="--self-test"){int inherited=ManufacturingWorkspaceV13Tests.Run();int reopen=ProductionReopenChecksV13.Tests();int templates=ManufacturingTemplateSelectionTests.Run();int sourceBends=ManufacturingSourceBendTestsV13.Run();int perf=ManufacturingPerformanceV13.Tests();return reopen==0&&inherited==0&&templates==0&&sourceBends==0&&perf==0?0:1;}
        string workspace=null,modelPath=null;ModelDoc2 previousActive=null;bool openedWorkspace=false;string onlyBody=null;string[] requestedBodies=null;bool allBodies=false;double toleranceMm=.001;string camGeometry="polyline";bool create=false,dry=false;string expectedPath=null,expectedConfig=null,SourcePath=null,Config=null,diagnostics=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostics"),stage="ARGUMENTS";string output=null,template=null,macro=null;
        try{
            for(int i=0;i<args.Length;i++){
                if(args[i]=="--all")allBodies=true;else if(args[i]=="--create")create=true;else if(args[i]=="--dry-run")dry=true;
                else if(args[i]=="--workspace"&&i+1<args.Length)workspace=args[++i];
                else if(args[i]=="--model"&&i+1<args.Length)modelPath=args[++i];
                else if(args[i]=="--output")throw new InvalidOperationException("Output derives from --workspace; --output is not accepted");
                else if(args[i]=="--drawing-template"&&i+1<args.Length)template=args[++i];
                else if(args[i]=="--body"&&i+1<args.Length)onlyBody=args[++i];
                else if(args[i]=="--bodies"&&i+1<args.Length)requestedBodies=args[++i].Split(',');
                else if(args[i]=="--geometry-tolerance-mm"&&i+1<args.Length)toleranceMm=double.Parse(args[++i],System.Globalization.CultureInfo.InvariantCulture);
                else if(args[i]=="--cam-geometry"&&i+1<args.Length)camGeometry=args[++i];
                else if(args[i]=="--expected-path"&&i+1<args.Length)expectedPath=args[++i];
                else if(args[i]=="--expected-configuration"&&i+1<args.Length)expectedConfig=args[++i];
                else if(args[i]=="--diagnostics-dir"&&i+1<args.Length)diagnostics=args[++i];
                else if(args[i]=="--macro"&&i+1<args.Length)macro=args[++i];
                else throw new InvalidOperationException("Usage: ManufacturingPackage.exe --dry-run | --create --workspace PATH --model PATH (--body NAME | --bodies NAME,NAME | --all) [--drawing-template file.drwdot]");
            }
            if(template!=null)template=ManufacturingTemplateSelection.Validate(template);
            if(!NV.Finite(toleranceMm)||toleranceMm<=0||camGeometry!="exact"&&camGeometry!="polyline")throw new InvalidOperationException("Positive finite --geometry-tolerance-mm and --cam-geometry exact|polyline required");
            if(create==dry)throw new InvalidOperationException("Specify exactly one of --dry-run and --create");
            if(onlyBody!=null&&requestedBodies!=null||allBodies&&(onlyBody!=null||requestedBodies!=null))throw new InvalidOperationException("Use exactly one of --body, --bodies or --all");
            if(onlyBody!=null)requestedBodies=new[]{onlyBody};
            if(workspace==null||modelPath==null||requestedBodies==null&&!allBodies)throw new InvalidOperationException("Required: --workspace --model and --body, --bodies or --all");
            if(requestedBodies!=null&&(requestedBodies.Length==0||requestedBodies.Any(String.IsNullOrEmpty)||requestedBodies.Distinct(StringComparer.Ordinal).Count()!=requestedBodies.Length))throw new InvalidOperationException("Exact body names must be nonempty and unique");
            ManufacturingWorkspaceV13.ValidatePaths(workspace,modelPath);output=Directory.GetParent(Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar)).FullName;
            diagnostics=Path.Combine(output,"Reports");
        }catch(Exception ex){Console.Error.WriteLine(ex.Message);try{Console.Error.WriteLine("Diagnostic JSON: "+ManufacturingContextV13.SaveDiagnostic(diagnostics,new{program="ProductionPackageEntry",status="ARGUMENTS_REJECTED",stage,expectedPath,expectedConfigurationApiName=expectedConfig,error=ex.ToString()}));}catch(Exception write){Console.Error.WriteLine("Diagnostic write failed: "+write.Message);}return 64;}
        ManufacturingPerformanceV13.Start();var preparation=ManufacturingPerformanceV13.Begin("PACKAGE","PREPARATION_AND_INVENTORY");
        SldWorks sw=null;ModelDoc2 source=null;object baseline=null;string sourceHash=null;MPFiles files=null;ManufacturingSolidWorksV13 generation=null;
        var blockers=new List<string>();var checks=new List<object>();var rows=new List<MPRow>();var report=new Dictionary<string,object>{{"program","ProductionPackageEntry"},{"mode",create?"CREATE":"DRY_RUN"},
            {"sourceChangesMade",false},{"productionReadinessCertified",false},{"rows",rows},{"checks",checks},{"blockers",blockers}};
        report["fileOperations"]=ManufacturingSourceFilesV03.Operations;
        report["fileCreationStatus"]="NOT_STARTED";report["productionCertificationStatus"]="NEEDS_REVIEW";
        report["encodingPolicy"]=new{jsonFiles="UTF-8",comStrings="Unicode BSTR via installed interop",console="UTF-8",automaticNameRepair=false,exactUtf16Comparison=true};
        int code=0;
        report["sourceContext"]=ManufacturingContextV13.Check("BEFORE_COM_CONNECTION",expectedPath,expectedConfig,null,null);
        try{
            stage="CONNECT_EXISTING_SOLIDWORKS";
            sw=(SldWorks)System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application");previousActive=sw.ActiveDoc as ModelDoc2;
            stage="SELECT_DRAWING_TEMPLATE";template=ManufacturingPerformanceV13.Measure("PACKAGE","TEMPLATE_SELECTION_INCLUDING_USER_WAIT",()=>ManufacturingTemplateSelection.Select(sw,template));
            if(template==null){Console.WriteLine("Выбор шаблона отменён. Экспорт не запускался.");preparation.Dispose();ManufacturingPerformanceV13.Finish(false);return 0;}
            stage="AUDIT_PREPARED_WORKSPACE_SAVED_REFERENCES";report["workspaceSavedReferences"]=ManufacturingWorkspaceV13.SavedReferences(sw,workspace);
            stage="OPEN_EXACT_PREPARED_MODEL_READ_ONLY";
            source=ManufacturingWorkspaceV13.OpenExact(sw,modelPath,out openedWorkspace);
            int activationError=0;var activated=sw.ActivateDoc3(source.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref activationError);if(activated==null||sw.IsSame(source,activated)!=(int)swObjectEquality.swObjectSame)throw new InvalidOperationException("Cannot activate exact workspace model");
            report["workspaceLiveReferences"]=ManufacturingWorkspaceV13.LiveReferences(source,workspace);expectedPath=modelPath;
            report["workspace"]=workspace;report["originalPolicy"]="Original never saved/rebuilt/closed. Only generated Workspace documents are mutable.";
            stage="CAPTURE_AND_VALIDATE_SOURCE_CONTEXT";
            SourcePath=source==null?null:source.GetPathName();
            Config=source==null||source.ConfigurationManager.ActiveConfiguration==null?null:source.ConfigurationManager.ActiveConfiguration.Name;
            expectedPath=expectedPath??SourcePath;expectedConfig=expectedConfig??Config;
            var context=ManufacturingContextV13.Check(stage,expectedPath,expectedConfig,SourcePath,Config);report["sourceContext"]=context;
            report["expectationPolicy"]="Explicit CLI expectations when supplied; otherwise pin active COM document path/configuration for entire execution";
            if(source!=null)report["availableConfigurationApiNames"]=source.GetConfigurationNames();
            if(source==null||!(source is PartDoc)||!(bool)context["passed"])throw new InvalidOperationException("SOURCE_DOCUMENT_OR_CONFIGURATION_MISMATCH");
            baseline=ManufacturingInventoryV01.Snapshot(source);
            stage="READ_INVENTORY";
            string revision=sw.RevisionNumber();report["solidWorksRevision"]=revision;
            if(!revision.StartsWith("33.",StringComparison.Ordinal))blockers.Add("Runtime is not the expected SolidWorks 2025 (revision 33.x)");
            var inventory=ManufacturingPerformanceV13.Measure("PACKAGE","CUT_LIST_INVENTORY",()=>ManufacturingInventoryV01.Capture(SourcePath,Config));report["inventory"]=inventory;baseline=inventory.ContainsKey("before")?inventory["before"]:null;report["sourceStateBeforeExports"]=ManufacturingStateV09.Capture(sw,source);
            if(sw.IsSame(source,sw.ActiveDoc)!=(int)swObjectEquality.swObjectSame||!ManufacturingContextV13.Matches(SourcePath,Config,source.GetPathName(),source.ConfigurationManager.ActiveConfiguration.Name))throw new InvalidOperationException("SOURCE_CONTEXT_CHANGED_DURING_INVENTORY");
            if(!inventory.ContainsKey("observedStateUnchanged")||!(inventory["observedStateUnchanged"] is bool)||!(bool)inventory["observedStateUnchanged"])blockers.Add("Inventory source-state check did not pass");
            var audit=AB.Obj(CutListCoreV01.Json.DeserializeObject(CutListCoreV01.Json.Serialize(inventory["membershipAudit"])));
            if(!AB.Bool(audit["membershipConsistent"]))blockers.Add("Incomplete/ambiguous Cut List membership");
            if(((List<object>)inventory["errors"]).Count>0)blockers.Add("Inventory API errors: inspect report");
            if(source.GetSaveFlag())blockers.Add("Source has unsaved changes: safe filesystem copy cannot represent active unsaved geometry; program will not save source");
            if(source.Extension.NeedsRebuild2!=(int)swModelRebuildStatus_e.swModelRebuildStatus_FullyRebuilt||source.ConfigurationManager.ActiveConfiguration.NeedsRebuild)
                blockers.Add("Source reports pending rebuild; a safely rebuilt copy cannot be assumed to match the current source geometry. Source will not be rebuilt.");
            stage="READ_SOURCE_DISK_HASH";
            if(ManufacturingSourceFilesV03.TryHash(SourcePath,stage,out sourceHash)){report["sourceFileSHA256Before"]=sourceHash;report["sourceDiskHashAvailable"]=true;}
            else {report["sourceDiskHashAvailable"]=false;blockers.Add("SOURCE_DISK_HASH_UNAVAILABLE: COM inventory/state checks remain available; creation prohibited without saved-file verification. Inspect fileOperations.");}
            stage="CHECK_PREFLIGHT_DEPENDENCIES";
            if(macro!=null&&(!File.Exists(macro)||Hash(macro)!=ReviewedMacroHash))blockers.Add("Reviewed macro unavailable or hash differs; no unknown VBA code is executed");
            report["macro"]=new{path=macro,optionalLegacyBinaryCheck=macro!=null,reviewedHash=ReviewedMacroHash,executed=false,
                portedLogic="End-plane zones .35 / axial dot .15 / largest area; slope classification; min-angle group ordering and descending lengths, retaining distinct Cut List folders. Round tubes bypass H. 90-ANGLE schematic labels are not used."};
            template=ManufacturingTemplateSelection.Validate(template);
            report["workbookWriter"]="OpenXML BCL; no Excel COM NumberFormat dependency";
            report["drawingTemplate"]=template;
            var all=ManufacturingGeometryV01.Items(((PartDoc)source).GetBodies2((int)swBodyType_e.swSolidBody,false)).Cast<Body2>().ToArray();
            var folders=(List<CLFolder>)inventory["folders"];var bodies=(List<CLBody>)inventory["bodies"];
            foreach(var b in bodies){
                var actual=all.Where(x=>x.Name==b.name).ToArray();if(actual.Length!=1){blockers.Add("Exact body name ambiguous/missing: "+b.name);continue;}
                if(b.cutListIds.Count!=1){blockers.Add("Body membership unresolved: "+b.name);continue;}
                var folder=folders.Single(f=>f.id==b.cutListIds[0]);var r=new MPRow{body=b.name,cutList=folder.name,folderPhysicalCount=folder.physicalSolidBodyCount,
                    material=Value(Property(folder,"MATERIAL","Материал")),description=Value(Property(folder,"DESCRIPTION","Описание"))};rows.Add(r);
                var materialProperty=Property(folder,"MATERIAL","Материал");r.materialRaw=materialProperty==null?null:materialProperty.rawValue;
                object bodyMaterial;if(AB.TryGet(b.material,"bodyMaterialName",out bodyMaterial))r.bodyMaterial=bodyMaterial as string;
                if(!String.IsNullOrEmpty(r.bodyMaterial)&&r.material!=null&&!String.Equals(r.bodyMaterial,r.material,StringComparison.OrdinalIgnoreCase))r.warnings.Add("MATERIAL_SOURCE_MISMATCH: cached Cut List material differs from direct body material API; no material replaced");
                var len=Property(folder,"LENGTH","ДЛИНА");r.lengthRaw=len==null?null:len.rawValue;r.lengthResolved=Value(len);
                var a1=Property(folder,"ANGLE1","УГОЛ1");r.angle1Raw=a1==null?null:a1.rawValue;r.angle1Resolved=Value(a1);
                var a2=Property(folder,"ANGLE2","УГОЛ2");r.angle2Raw=a2==null?null:a2.rawValue;r.angle2Resolved=Value(a2);
                if(b.sheetMetal==true){r.kind="SHEET METAL";r.warnings.Add("Native flat pattern, bend lines, thickness and DXF scale to be checked on owned copy during create; no source flattening in dry-run");}
                else if(ProductionProfiles.Has(b.name)){r.kind="RECTANGULAR PROFILE";r.warnings.Add("MATERIAL reference only; processing NEEDS_REVIEW; body kept separate");}
                else {
                    r.tube=ManufacturingPerformanceV13.Measure(b.name,"TUBE_GEOMETRY",()=>ManufacturingGeometryV01.Tube(actual[0]));
                    if(r.tube.status!="NOT_VERIFIED"){
                        r.kind="TUBE";if(r.description!=null&&r.description.IndexOf("R40x2",StringComparison.OrdinalIgnoreCase)>=0)r.warnings.Add("KNOWN_STALE_DESCRIPTION: R40x2 must not determine actual section");
                        
                        if(r.angle1Resolved==null||r.angle1Resolved.Trim()=="-"||r.angle2Resolved==null||r.angle2Resolved.Trim()=="-")r.warnings.Add("Missing cutting angle retained; no missing value replaced by zero");
                    }else{
                        r.plate=ManufacturingPerformanceV13.Measure(b.name,"PLATE_GEOMETRY",()=>ManufacturingGeometryV01.Plate(ManufacturingBrepV01.Capture(actual[0])));
                        r.kind=r.plate.accepted?"PLATES":"UNSUPPORTED";if(!r.plate.accepted)blockers.Add("Unsupported non-sheet body "+r.body+": "+r.plate.reason);
                    }
                }
                if(String.IsNullOrWhiteSpace(r.material))r.warnings.Add("Cut List material NOT_VERIFIED; no material guessed");
                r.warnings.Add("Cached Cut List values remain separate from geometric measurements; no BREP identity or production-readiness certificate");
            }
            int tubes=rows.Count(r=>r.kind=="TUBE"),sheets=rows.Count(r=>r.kind=="SHEET METAL"),plates=rows.Count(r=>r.kind=="PLATES");
            report["bodyExportPlan"]=rows.Select(r=>new{bodyName=r.body,type=r.kind,method=r.kind=="SHEET METAL"?"Native ExportToDWG2 selected FlatPattern":r.kind=="PLATES"?"Exact native planar face projection":"Tube cutting table",status="PENDING_CREATE",dxfPath=(string)null,validationStatus="NOT_YET_EXPORTED",warnings=r.warnings}).ToArray();
            report["nativeDxfPolicy"]=new{cutOptions=1,bendOptions=4,cutLayer="CUT_THROUGH",bendLayer="BEND_LINES",separateFiles=true,nativeSplinePolylineExportRequested=true,ownSplineApproximationAllowed=false,geometryToleranceMm=toleranceMm,camGeometry,endpointHealingAllowed=false,exportPreferencesRestoredAfterEachCall=true,limitedNativeCurveIdentityDoesNotBlockExport=true};
            checks.Add(new{check="ACTUAL_MODEL_COUNTS",solidBodies=bodies.Count,cutListFolders=folders.Count,tubes,sheets,plates,fixedCountRequirements=false});
            preparation.Success();preparation.Dispose();
            if(create)ManufacturingPerformanceV13.Plan(rows.Where(r=>r.kind=="SHEET METAL"&&(requestedBodies==null||requestedBodies.Contains(r.body))).Select(r=>r.body),rows.Where(r=>r.kind=="PLATES"&&requestedBodies==null).Select(r=>r.body));
            files=MPFiles.NewProductionRun(workspace);diagnostics=Path.Combine(files.OutputRoot,"Reports");report["outputFolder"]=files.OutputRoot;
            stage="PREFLIGHT_COMPLETE";
            if(requestedBodies!=null)foreach(string name in requestedBodies)if(rows.Count(r=>r.body==name&&r.kind=="SHEET METAL")!=1)blockers.Add("Exact Sheet Metal body missing/ambiguous: "+name);
            report["requestedBodies"]=requestedBodies;report["scope"]=allBodies?"FULL_PACKAGE":"SELECTED_SHEET_BODIES";
            report["singleBodyTest"]=onlyBody;
            report["preflightPassed"]=blockers.Count==0;
            if(dry){report["status"]=blockers.Count==0?"DRY_RUN_PASSED_COPY_EXPORT_CHECKS_PENDING":"DRY_RUN_BLOCKED";code=blockers.Count==0?0:2;}
            else {
                if(blockers.Count>0)throw new InvalidOperationException("CREATE_BLOCKED_BY_PREFLIGHT");
                files.BeginWorkspaceRun();report["fileCreationStatus"]="IN_PROGRESS";generation=ManufacturingPerformanceV13.Measure("PACKAGE","SAVED_SNAPSHOT_PREPARATION",()=>new ManufacturingSolidWorksV13(sw,source,files,sourceHash,toleranceMm,camGeometry));report["sheetBodyExports"]=generation.Diagnostics;
                foreach(var r in rows.Where(r=>r.kind=="PLATES"&&requestedBodies==null)){
                    string dxf=ManufacturingPerformanceV13.Measure(r.body,"PLATE_DXF",()=>files.Write("dxf/"+r.body+".dxf",r.plate.text));r.output=new{dxf,check=r.plate.reason,thicknessMm=r.plate.thicknessMm};checks.Add(new{body=r.body,plateDxf="EXACT_NATIVE_FACE_PROJECTION",type=r.kind,method="Exact native planar face projection",validationStatus="PASSED",dxf});
                }
                int exportedSheetIndex=0;
                foreach(var r in rows.Where(r=>r.kind=="SHEET METAL"&&(requestedBodies==null||requestedBodies.Contains(r.body))).OrderBy(r=>requestedBodies==null?0:Array.IndexOf(requestedBodies,r.body)).ThenBy(r=>r.body,StringComparer.Ordinal)){
                    int blockersBeforeBody=blockers.Count;
                    stage="EXPORT_BODY "+r.body;
                    checks.Add(new{operation="SHEET_BODY_BEGIN",body=r.body});
                    try{r.output=generation.Sheet(r.body,template,r.material??"НЕ ПРОВЕРЕНО");checks.Add(new{body=r.body,result=r.output});object camStatus;if(AB.TryGet(r.output,"camStatus",out camStatus)&&camStatus as string!="PASS")r.warnings.Add("CAM NEEDS_REVIEW: whole-contour approximation bound or mandatory DXF validation is unavailable; not approved for cutting.");}
                    catch(Exception ex){r.output=new{bodyName=r.body,type=r.kind,method="Native ExportToDWG2",status="FAILED",message=ex.Message,stackTrace=ex.StackTrace};checks.Add(new{body=r.body,result=r.output});blockers.Add("Sheet export failed: "+r.body+": "+ex.Message);if(ex.Message.Contains("DXF_EXPORT_PREFERENCE_RESTORE_FAILED"))throw;}
                    finally{generation.Restore();}
                    try{if(r.output!=null){object drw,part;if(AB.TryGet(r.output,"drw",out drw)&&drw is string&&AB.TryGet(r.output,"referencePart",out part)&&part is string){var publication=ManufacturingPerformanceV13.Measure(r.body,"PUBLISH_PORTABLE",()=>ManufacturingWorkspaceV13.PublishAndCheck(sw,files,(string)drw,(string)part,r.body,Convert.ToInt32(AB.Get(r.output,"bendLines"))));var data=AB.Obj(CutListCoreV01.Json.DeserializeObject(CutListCoreV01.Json.Serialize(r.output)));data["drw"]=publication["drawing"];data["referencePart"]=publication["model"];data["publication"]=publication;r.output=data;var bodyDiagnostic=generation.Diagnostics.Last(d=>(string)d["bodyName"]==r.body);bodyDiagnostic["result"]=data;bodyDiagnostic["drawingPath"]=publication["drawing"];bodyDiagnostic["referencePartPath"]=publication["model"];bodyDiagnostic["publication"]=publication;bodyDiagnostic["publicationStatus"]="PORTABLE_VERIFIED";checks.Add(new{body=r.body,publication});}}}
                    catch(Exception ex){var failure=new{body=r.body,publicationError=ex.ToString(),nativeDrawingOpen=ex.Data["nativeDrawingOpen"],publishedDrawing=ex.Data["publishedDrawing"],publishedModel=ex.Data["publishedModel"]};checks.Add(failure);var item=generation.Diagnostics.LastOrDefault(d=>(string)d["bodyName"]==r.body);if(item!=null){item["publicationStatus"]="INCOMPLETE";item["publicationError"]=failure;item["status"]="FILES_CREATED_PUBLICATION_INCOMPLETE";}blockers.Add("Published drawing INCOMPLETE: "+r.body+": "+ex.Message);}
                    finally{int activeError=0;var active=sw.ActivateDoc3(source.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref activeError);if(active==null||sw.IsSame(active,source)!=(int)swObjectEquality.swObjectSame)throw new InvalidOperationException("Workspace base activation failed after publication");}
                    bool complete=RequiredBodyFiles(r.output,camGeometry);if(!complete)blockers.Add("Required files missing for body: "+r.body);
                    if(exportedSheetIndex++==0&&(blockers.Count>blockersBeforeBody||!complete)){report["firstBodyFullCycleFailed"]=r.body;break;}
                    var afterBody=ManufacturingStateV09.Capture(sw,source);var diff=ManufacturingStateV09.Diff(baseline,afterBody["modelState"]);checks.Add(new{check="SOURCE_AFTER_BODY",body=r.body,state=afterBody,diff});
                    if(source.GetSaveFlag()||diff.Count>0||!ManufacturingContextV13.Matches(modelPath,Config,source.GetPathName(),source.ConfigurationManager.ActiveConfiguration.Name)||!(bool)afterBody["sourceIsActive"]){report["sourceSafetyStop"]=new{body=r.body,diff,afterBody};blockers.Add("SOURCE_STATE_CHANGED_AFTER_COPY_OPERATION");break;}
                    if(complete&&blockers.Count==blockersBeforeBody)ManufacturingPerformanceV13.Mark(r.body,"BODY_VERIFIED");
                }
                bool sourceOk=!source.GetSaveFlag()&&ManufacturingContextV13.Matches(modelPath,Config,source.GetPathName(),source.ConfigurationManager.ActiveConfiguration.Name)&&ManufacturingSourceFilesV03.Hash(SourcePath,"SOURCE_BEFORE_WORKBOOK")==sourceHash;
                checks.Add(new{check="SOURCE_STATE_BEFORE_WORKBOOK",unchanged=sourceOk,sourceFileSHA256=ManufacturingSourceFilesV03.Hash(SourcePath,"SOURCE_BEFORE_WORKBOOK")});
                report["sourceSafeBeforeWorkbook"]=sourceOk;
                stage="WRITE_OPENXML_WORKBOOK";
                string excel=files.Reserve("manufacturing.xlsx");files.BeforeSave(excel);try{ManufacturingPerformanceV13.Measure("PACKAGE","EXCEL_WRITE",()=>ManufacturingExcelV05.Write(excel,Tables(rows,checks)));}catch(Exception ex){ex.Data["operation"]="OpenXML XLSX CreateNew";ex.Data["path"]=excel;throw;}files.Created.Add(excel);report["workbook"]=excel;
                report["workbookAudit"]=ManufacturingPerformanceV13.Measure("PACKAGE","EXCEL_AUDIT",()=>ManufacturingWorkbookAuditV06.Verify(excel,rows));ManufacturingPerformanceV13.Mark("PACKAGE","EXCEL");
                var expectedFiles=new List<string>{excel};
                foreach(var r in rows.Where(r=>r.kind=="SHEET METAL"||r.kind=="PLATES")){
                    if((requestedBodies==null||requestedBodies.Contains(r.body))&&(r.kind=="PLATES"||camGeometry=="polyline"))expectedFiles.Add(files.PathFor("dxf/"+r.body+".dxf"));
                    if(r.kind=="SHEET METAL"&&(requestedBodies==null||requestedBodies.Contains(r.body))){object drawingPath;if(r.output!=null&&AB.TryGet(r.output,"drw",out drawingPath)&&drawingPath is string)expectedFiles.Add((string)drawingPath);else expectedFiles.Add(files.PathFor("drawings/"+r.body+".SLDDRW"));expectedFiles.Add(files.PathFor("pdf/"+r.body+".pdf"));object linkedPart;if(r.output!=null&&AB.TryGet(r.output,"referencePart",out linkedPart)&&linkedPart is string)expectedFiles.Add((string)linkedPart);}
                }
                foreach(var item in generation.Diagnostics)foreach(string field in new[]{"bendDxfPath","exactDxfPath","camDxfPath"}){object path;if(item.TryGetValue(field,out path)&&path is string)expectedFiles.Add((string)path);}
                foreach(var item in generation.Diagnostics){object count,path;if(item.TryGetValue("nativeDrawingBendLineCount",out count)&&Convert.ToInt32(count)>0&&(!item.TryGetValue("bendDxfPath",out path)||!(path is string)))expectedFiles.Add(files.PathFor("bends/"+(string)item["bodyName"]+"-BEND_LINES.dxf"));}
                foreach(var pending in rows.Where(r=>r.kind=="SHEET METAL"&&!generation.Diagnostics.Any(d=>(string)d["bodyName"]==r.body)))generation.Diagnostics.Add(new Dictionary<string,object>{{"bodyName",pending.body},{"status",requestedBodies!=null&&!requestedBodies.Contains(pending.body)?"NOT_REQUESTED":"NOT_ATTEMPTED_SAFETY_STOP"}});
                if(!sourceOk)throw new InvalidOperationException("SOURCE_STATE_OR_DISK_UNVERIFIED: workbook retained, no further model operations permitted");
                report["missingRequiredFiles"]=expectedFiles.Where(p=>!File.Exists(p)||new FileInfo(p).Length==0).ToArray();
                if(blockers.Count>0)throw new InvalidOperationException("PACKAGE_INCOMPLETE: individual sheet exports failed; inspect sheetBodyExports and workbook CHECKS");
                if(expectedFiles.Any(p=>!File.Exists(p)||new FileInfo(p).Length==0))throw new InvalidOperationException("Production output set incomplete");
                report["productionFiles"]=expectedFiles.Select(p=>new{path=p,sha256=Hash(p),bytes=new FileInfo(p).Length}).ToArray();
                report["producedProductionFileCount"]=expectedFiles.Count;
                report["fileCreationStatus"]="REQUIRED_FILES_CREATED";report["status"]="PACKAGE_FILES_CREATED_NEEDS_REVIEW";code=2;
            }
        }catch(Exception ex){report["status"]="STOPPED_OR_INCOMPLETE";report["error"]=new{exception=ex.GetType().FullName,hresult="0x"+ex.HResult.ToString("X8"),message=ex.Message,stage,operation=ex.Data["operation"],path=ex.Data["path"],operationStage=ex.Data["stage"],stackTrace=ex.StackTrace,details=ex.ToString()};code=1;}
        finally{
            if(generation!=null)foreach(var pending in rows.Where(r=>r.kind=="SHEET METAL"&&!generation.Diagnostics.Any(d=>(string)d["bodyName"]==r.body)))generation.Diagnostics.Add(new Dictionary<string,object>{{"bodyName",pending.body},{"status",requestedBodies!=null&&!requestedBodies.Contains(pending.body)?"NOT_REQUESTED":"NOT_ATTEMPTED_SAFETY_STOP"}});
            if(generation!=null)try{generation.Restore();}catch(Exception ex){report["restorationError"]=ex.Message;report["status"]="RESTORATION_FAILED";code=3;}
            if(source!=null&&baseline!=null)try{
                report["sourceStateAfterExports"]=ManufacturingStateV09.Capture(sw,source);report["sourceStateDiff"]=ManufacturingStateV09.Diff(baseline,((Dictionary<string,object>)report["sourceStateAfterExports"])["modelState"]);
                bool unchanged=sw.IsSame(source,sw.ActiveDoc)==(int)swObjectEquality.swObjectSame&&CutListCoreV01.Json.Serialize(baseline)==CutListCoreV01.Json.Serialize(ManufacturingInventoryV01.Snapshot(source));
                report["observedSourceStateUnchanged"]=unchanged;if(!unchanged)report["workspaceBaselineMetadataChanged"]=true;
            }catch(Exception ex){report["observedSourceStateUnchanged"]=null;report["sourceStateError"]=ex.Message;report["status"]="SOURCE_STATE_CHECK_UNAVAILABLE";code=3;}
            else report["observedSourceStateUnchanged"]=null;
            if(sourceHash==null){report["sourceFileUnchanged"]=null;report["sourceFileCheckLimitation"]="Saved file hash unavailable; COM state equality does not certify on-disk bytes or unsaved geometry";}
            if(sourceHash!=null)try{string after=ManufacturingSourceFilesV03.Hash(SourcePath,"SOURCE_HASH_AFTER_RUN");report["sourceFileSHA256After"]=after;report["sourceFileUnchanged"]=after==sourceHash;if(after!=sourceHash){report["status"]="SOURCE_FILE_CHANGED";code=3;}}
                catch(Exception ex){report["sourceFileUnchanged"]=null;report["sourceFileCheckError"]=ex.Message;report["status"]="SOURCE_FILE_CHECK_UNAVAILABLE";code=3;}
            report["sourceMutatingApiCallsMade"]=false;report["allMutableModelsFencedToWorkspace"]=true;
            object modelState,diskState;
            report["sourceChangesMade"]=AB.TryGet(report,"observedSourceStateUnchanged",out modelState)&&modelState is bool&&(bool)modelState&&
                AB.TryGet(report,"sourceFileUnchanged",out diskState)&&diskState is bool&&(bool)diskState?(object)false:null;
        }
        preparation.Dispose();ManufacturingPerformanceV13.EndStage(false);report["performance"]=ManufacturingPerformanceV13.Snapshot();
        if(files!=null&&create&&File.Exists(files.PathFor("PACKAGE_RUNNING.lock")))try{
            if((string)report["fileCreationStatus"]=="IN_PROGRESS")report["fileCreationStatus"]="PARTIAL_FILES_CREATED";
            report["createdFiles"]=files.Created.Where(p=>File.Exists(p)).Select(p=>new{path=p,bytes=new FileInfo(p).Length}).ToArray();
            files.Write("package-report.json",ManufacturingStateV09.Json(report));
            string marker=files.Reserve(code==0?"PACKAGE_FINISHED.txt":code==2&&report.ContainsKey("status")&&(string)report["status"]=="PACKAGE_FILES_CREATED_NEEDS_REVIEW"?"FILES_CREATED_NEEDS_REVIEW.txt":"PACKAGE_INCOMPLETE.txt");
            File.Move(files.PathFor("PACKAGE_RUNNING.lock"),marker);files.Created.Add(marker);
        }catch(Exception ex){report["status"]="REPORT_SAVE_FAILED";Console.Error.WriteLine("Cannot save package report: "+ex.Message);code=1;}
        report["lastStage"]=stage;
        try {string diagnosticPath=ManufacturingContextV13.SaveDiagnostic(diagnostics,report);report["diagnosticFile"]=diagnosticPath;Console.Error.WriteLine("Diagnostic JSON: "+diagnosticPath);}
        catch(Exception ex){report["diagnosticWriteError"]=ex.ToString();Console.Error.WriteLine("Diagnostic write failed: "+ex.Message);if(code==0)code=1;}
        if(previousActive!=null&&source!=null&&sw.IsSame(previousActive,source)!=(int)swObjectEquality.swObjectSame)try{int restoreError=0;sw.ActivateDoc3(previousActive.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref restoreError);}catch(Exception ex){Console.Error.WriteLine("Previous active document restoration: "+ex.Message);}
        ManufacturingPerformanceV13.Finish(create&&code==2&&(string)report["status"]=="PACKAGE_FILES_CREATED_NEEDS_REVIEW"&&!report.ContainsKey("diagnosticWriteError"));
        Console.WriteLine("ProductionPackageEntry | "+(report.ContainsKey("status")?report["status"]:"UNKNOWN")+" | exit="+code);
        Console.WriteLine("Diagnostic: "+(report.ContainsKey("diagnosticFile")?report["diagnosticFile"]:"UNAVAILABLE"));
        Console.WriteLine("Output folder: "+(report.ContainsKey("outputFolder")?report["outputFolder"]:"NOT_CREATED"));
        Console.WriteLine("Workbook: "+(report.ContainsKey("workbook")?report["workbook"]:"NOT_CREATED"));
        if(generation!=null)foreach(var item in generation.Diagnostics)Console.WriteLine((string)item["bodyName"]+": "+(item.ContainsKey("status")?item["status"]:"UNKNOWN"));
        Console.WriteLine("Workspace baseline metadata unchanged (diagnostic): "+(report.ContainsKey("observedSourceStateUnchanged")?report["observedSourceStateUnchanged"]:"UNVERIFIED"));
        foreach(var b in blockers)Console.WriteLine("- "+(b.Length>240?b.Substring(0,240)+"...":b));return code;
    }
}

