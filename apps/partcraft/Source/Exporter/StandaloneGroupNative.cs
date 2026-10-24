using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Separate experiment: one native grouped call, no manufacturing pipeline entry point.
public static class StandaloneGroupNative {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=100};
    static readonly List<object> times=new List<object>();
    static readonly List<object> calls=new List<object>();
    static SldWorks sw;static ModelDoc2 copy;static string stage="ARGUMENTS";
    static Dictionary<string,object> report=new Dictionary<string,object>{{"program","StandaloneGroupNative"},{"status","STOPPED_OR_INCOMPLETE"},{"productionReadinessCertified",false},{"nativeCalls",calls},{"timings",times}};
    static object[] Items(object v){return v==null?new object[0]:((Array)v).Cast<object>().ToArray();}
    static bool Same(object a,object b){int c=BindingProfileV02.Call("ISldWorks.IsSame","body/selection identity",()=>sw.IsSame(a,b));if(c==(int)swObjectEquality.swObjectSame)return true;if(c==(int)swObjectEquality.swObjectNotSame)return false;throw new InvalidOperationException("COM identity unavailable, code="+c);}
    static List<ModelDoc2> protectedDocs=new List<ModelDoc2>();static string protectedState;
    static object State(ModelDoc2 doc){return new{path=doc.GetPathName(),configuration=doc.GetType()==(int)swDocumentTypes_e.swDocDRAWING?null:doc.ConfigurationManager.ActiveConfiguration.Name,dirty=doc.GetSaveFlag(),updateStamp=doc.GetUpdateStamp(),selectionCount=((SelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1)};}
    static string ProtectedState(){return Json.Serialize(protectedDocs.Select(State).ToArray());}
    static T Timed<T>(string name,Func<T> operation){
        ManufacturingAgent.Boundary(name);stage=name;Console.WriteLine("BEGIN "+name);var timer=Stopwatch.StartNew();bool ok=false;
        try{if(protectedState!=null&&ProtectedState()!=protectedState)throw new InvalidOperationException("PROTECTED_DOCUMENT_CHANGED_BEFORE: "+name);T result=operation();ok=true;return result;}
        finally{timer.Stop();times.Add(new{stage=name,elapsedSeconds=timer.Elapsed.TotalSeconds,callCompleted=ok});Console.WriteLine((ok?"END ":"FAIL ")+name+" "+timer.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s");
            if(protectedState!=null&&ProtectedState()!=protectedState)throw new InvalidOperationException("PROTECTED_DOCUMENT_CHANGED_DURING: "+name);}
    }
    sealed class FeatureRow {public string type,name;public Feature feature;}
    sealed class Binding {public string bodyName,flatPattern,cutList,nativeQuantity;public int quantity,folderBodyCount;public bool accessSelectionsUsed=false,faceOwnerVerified;public bool suppressed;[ScriptIgnore]public Feature feature;}
    static List<Binding> Representatives(List<Binding> bindings){
        var result=new List<Binding>();
        foreach(var group in bindings.GroupBy(b=>b.cutList,StringComparer.Ordinal)){
            var first=group.First();var qty=NativeWeldmentAdapter.NativeNumber(first.nativeQuantity,false);
            if(String.IsNullOrWhiteSpace(first.cutList)||!qty.HasValue||qty.Value!=group.Count()||first.folderBodyCount!=group.Count())throw new InvalidOperationException("CUT_LIST_QUANTITY_MISMATCH: "+group.Key);
            first.quantity=group.Count();result.Add(first);
        }return result;
    }
    static List<FeatureRow> FeatureSnapshot(){
        var first=BindingProfileV02.Call("IModelDoc2.FirstFeature","CollectFlatPatterns/BuildCutListCache",()=>copy.FirstFeature() as Feature);
        var all=MacroBindingCoreV03.Collect(first,
            f=>BindingProfileV02.Call("IFeature.GetNextFeature","macro root chain",()=>f.GetNextFeature() as Feature),
            f=>BindingProfileV02.Call("IFeature.GetFirstSubFeature","macro child chain",()=>f.GetFirstSubFeature() as Feature),
            f=>BindingProfileV02.Call("IFeature.GetNextSubFeature","macro child chain",()=>f.GetNextSubFeature() as Feature),
            f=>BindingProfileV02.Call("IFeature.GetID","linear native ID guard",()=>f.GetID()),10000);
        if(all.Count==0)throw new InvalidOperationException("Native feature tree empty");
        return all.Select(f=>{string type=BindingProfileV02.Call("IFeature.GetTypeName2","macro snapshot",()=>f.GetTypeName2());return new FeatureRow{feature=f,type=type,name=type=="FlatPattern"||type=="CutListFolder"?BindingProfileV02.Call("IFeature.Name",type,()=>f.Name):null};}).ToList();
    }
    static IEnumerable<FeatureRow> CollectFlatPatterns(List<FeatureRow> snapshot){return snapshot.Where(f=>f.type=="FlatPattern");}
    sealed class CacheMember {public string bodyName,folderName,nativeQuantity;public int folderBodyCount;public Body2 body;}
    static List<CacheMember> BuildCutListCache(List<FeatureRow> snapshot){
        var cache=new List<CacheMember>();foreach(var row in snapshot.Where(f=>f.type=="CutListFolder")){
            var folder=BindingProfileV02.Call("IFeature.GetSpecificFeature2",row.name,()=>row.feature.GetSpecificFeature2() as BodyFolder);
            if(folder==null)throw new InvalidOperationException("CutListFolder BodyFolder unavailable: "+row.name);
            var members=BindingProfileV02.Call("IBodyFolder.GetBodies",row.name,()=>Items(folder.GetBodies()).Cast<Body2>().ToArray());
            string raw,value;bool resolved,linked;var props=row.feature.CustomPropertyManager;
            props.Get6("QUANTITY",false,out raw,out value,out resolved,out linked);
            if(String.IsNullOrWhiteSpace(value))props.Get6("КОЛИЧЕСТВО",false,out raw,out value,out resolved,out linked);
            foreach(var body in members)cache.Add(new CacheMember{body=body,bodyName=BindingProfileV02.Call("IBody2.Name","cached CutList member",()=>body.Name),folderName=row.name,nativeQuantity=value,folderBodyCount=members.Length});
        }return cache;
    }
    static Body2 GetBodyFromFlatPattern(FeatureRow row){
        var data=BindingProfileV02.Call("IFeature.GetDefinition",row.name,()=>row.feature.GetDefinition() as FlatPatternFeatureData);
        if(data==null)throw new InvalidOperationException("FlatPatternFeatureData unavailable: "+row.name);
        var face=BindingProfileV02.Call("IFlatPatternFeatureData.FixedFace2",row.name,()=>data.FixedFace2 as Face2);
        if(face==null)throw new InvalidOperationException("FixedFace2 unavailable without changing model state: "+row.name);
        var body=BindingProfileV02.Call("IFace2.GetBody",row.name,()=>face.GetBody() as Body2);
        if(body==null)throw new InvalidOperationException("FixedFace2.GetBody null: "+row.name);return body;
    }
    static CacheMember FindCutListFeatureCached(Body2 body,string name,List<CacheMember> cache){
        // Name narrows candidates; native IsSame proves membership, never the name alone.
        return MacroBindingCoreV03.UniqueCached(cache.Where(c=>c.bodyName==name),c=>Same(c.body,body),name);
    }
    static bool LegacySame(object a,object b,string category){
        int value=BindingProfileV02.Call("ISldWorks.IsSame("+category+")","legacy feature traversal",()=>sw.IsSame(a,b));
        if(value==(int)swObjectEquality.swObjectSame)return true;if(value==(int)swObjectEquality.swObjectNotSame)return false;throw new InvalidOperationException("Legacy native identity unavailable: "+value);
    }
    static List<Feature> LegacyChain(Feature first,bool sub){
        var result=new List<Feature>();for(var f=first;f!=null;f=BindingProfileV02.Call(sub?"IFeature.GetNextSubFeature":"IFeature.GetNextFeature","legacy chain",()=> (Feature)(sub?f.GetNextSubFeature():f.GetNextFeature()))){
            if(result.Count>=10000||result.Any(x=>LegacySame(x,f,"feature-chain")))throw new InvalidOperationException("Legacy cycle/limit");result.Add(f);
        }return result;
    }
    static void ProfileLegacyTraversal(int passes){
        BindingProfileV02.Reset();BindingProfileV02.Enabled=true;
        try{for(int i=0;i<passes;i++)Timed("LEGACY_TRAVERSAL_PASS_"+(i+1),()=>{
            var roots=LegacyChain(BindingProfileV02.Call("IModelDoc2.FirstFeature","legacy roots",()=>copy.FirstFeature() as Feature),false);
            var result=CutListCoreV01.Traverse(roots,f=>LegacyChain(BindingProfileV02.Call("IFeature.GetFirstSubFeature","legacy children",()=>f.GetFirstSubFeature() as Feature),true),
                (a,b)=>LegacySame(a,b,"feature-global"));
            report["legacyUniqueFeaturesLastPass"]=result.Count;return true;
        });}finally{BindingProfileV02.Enabled=false;report["legacyTraversalComProfile"]=BindingProfileV02.Snapshot();BindingProfileV02.Print();}
    }
    static List<Binding> Bind(string[] requested){
        report["bindingCopyStateBefore"]=State(copy);
        BindingProfileV02.Reset();BindingProfileV02.Enabled=true;
        try{
            var bodies=Timed("BIND/SNAPSHOT_BODIES_ONCE",()=>{
                var all=BindingProfileV02.Call("IPartDoc.GetBodies2","one solid-body snapshot",()=>Items(((PartDoc)copy).GetBodies2((int)swBodyType_e.swSolidBody,false)).Cast<Body2>().ToArray());
                var dict=new Dictionary<string,Body2>(StringComparer.Ordinal);foreach(var body in all){string name=BindingProfileV02.Call("IBody2.Name","snapshot",()=>body.Name);if(dict.ContainsKey(name))throw new InvalidOperationException("Ambiguous exact body name: "+name);dict.Add(name,body);}return dict;
            });
            foreach(string name in requested)if(!bodies.ContainsKey(name))throw new InvalidOperationException("Exact body missing: "+name);
            var features=Timed("BIND/COLLECT_FEATURES_MACRO_ORDER",()=>FeatureSnapshot());
            var cache=Timed("BIND/BUILD_CUT_LIST_CACHE",()=>BuildCutListCache(features));
            var result=Timed("BIND/FIXED_FACE_AND_CACHED_MEMBERSHIP",()=>{
                var rows=new List<Binding>();foreach(var row in CollectFlatPatterns(features)){
                    var body=GetBodyFromFlatPattern(row);string name=BindingProfileV02.Call("IBody2.Name",row.name,()=>body.Name);
                    if(!requested.Contains(name,StringComparer.Ordinal))continue;
                    if(!Same(body,bodies[name]))throw new InvalidOperationException("Fixed face owner differs from exact body snapshot: "+name);
                    var membership=FindCutListFeatureCached(body,name,cache);
                    if(!BindingProfileV02.Call("IBody2.IsSheetMetal",name,()=>body.IsSheetMetal()))throw new InvalidOperationException("Not native Sheet Metal: "+name);
                    rows.Add(new Binding{bodyName=name,flatPattern=row.name,cutList=membership.folderName,nativeQuantity=membership.nativeQuantity,folderBodyCount=membership.folderBodyCount,faceOwnerVerified=true,feature=row.feature,suppressed=BindingProfileV02.Call("IFeature.IsSuppressed",row.name,()=>row.feature.IsSuppressed())});
                }return rows;
            });
            foreach(string name in requested)if(result.Count(r=>r.bodyName==name)!=1)throw new InvalidOperationException("Nonunique FlatPattern/body link: "+name);
            report["bindingSnapshot"]=new{algorithm="Macro CollectFlatPatterns + BuildCutListCache + FixedFace2.GetBody + FindCutListFeatureCached",solidBodies=bodies.Count,features=features.Count,cutListFolders=features.Count(f=>f.type=="CutListFolder"),cachedCutListBodies=cache.Count,GetBodies2Calls=1,featureTreePasses=1,cutListPasses=1,featurePairwiseIsSameCalls=0,AccessSelectionsCalls=0,explicitRebuildCalls=0,brepAnalysisCalls=0,cutListDeduplication=false};
            report["flatPatternBindings"]=result;return requested.Select(n=>result.Single(r=>r.bodyName==n)).ToList();
        }finally{BindingProfileV02.Enabled=false;report["bindingComProfile"]=BindingProfileV02.Snapshot();BindingProfileV02.Print();report["bindingCopyStateAfter"]=State(copy);}
    }
    public static string[] ParseBodies(string text){var names=text.Split(',');if(names.Length==0||names.Any(String.IsNullOrWhiteSpace)||names.Distinct(StringComparer.Ordinal).Count()!=names.Length)throw new ArgumentException("Body names must be exact, nonempty and unique");return names;}
    public static bool ExpectedFiles(int expected,int actual,bool returned){return returned&&actual==expected;}
    public static bool AllowedOptions(int options){return options==1||options==4||options==5;}
    static int Tests(){NativeAutomationScope.Test();int passed=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Regression failed at "+(passed+1));passed++;};
        check(ParseBodies("10-1,9-1,9-2,9-3").Length==4);check(ParseBodies("Деталь 1,Body<2>")[0]=="Деталь 1");
        bool rejected=false;try{ParseBodies("A,A");}catch(ArgumentException){rejected=true;}check(rejected);
        rejected=false;try{ParseBodies("A,");}catch(ArgumentException){rejected=true;}check(rejected);
        check(AllowedOptions(5)&&AllowedOptions(1)&&AllowedOptions(4)&&!AllowedOptions(0));check(ExpectedFiles(4,4,true));check(!ExpectedFiles(4,1,true));check(!ExpectedFiles(4,4,false));
        var sample=new List<Binding>{new Binding{cutList="Left panel",bodyName="a",nativeQuantity="2",folderBodyCount=2},new Binding{cutList="Left panel",bodyName="b",nativeQuantity="2",folderBodyCount=2},new Binding{cutList="Right panel",bodyName="c",nativeQuantity="1",folderBodyCount=1}};
        var unique=Representatives(sample);check(unique.Count==2&&unique.Sum(x=>x.quantity)==3&&unique[0].bodyName=="a"&&unique[1].cutList=="Right panel");
        sample[0].nativeQuantity="1";rejected=false;try{Representatives(sample);}catch(InvalidOperationException){rejected=true;}check(rejected);
        check(typeof(IPartDoc).GetMethod("ExportToDWG2").GetParameters().Length==9);Console.WriteLine("GroupDxfProbe: "+passed+"/"+passed+" tests PASS (no COM)");return 0;
    }
    public static int PreparedBatchSize=1;
    public static int PreparedPositionLimit=0;
    public static object RunPrepared(string workspace,string model,string[] names){
        if(names.Length==0)throw new InvalidOperationException("NO_SHEET_METAL_BODIES");
        times.Clear();calls.Clear();protectedDocs.Clear();protectedState=null;
        report=new Dictionary<string,object>{{"program","GroupDxfV03 integrated native stage"},{"status","STOPPED_OR_INCOMPLETE"},{"productionReadinessCertified",false},{"nativeCalls",calls},{"timings",times}};
        int code=Main(new[]{"--workspace",workspace,"--model",model,"--bodies-json",Json.Serialize(names),"--options","5","--prepared-source","--unique-cut-list","--batch-size",PreparedBatchSize.ToString()});
        if(code!=2)throw new InvalidOperationException("NATIVE_GROUP_EXPORT_INCOMPLETE: "+Json.Serialize(report));
        return report;
    }
    [STAThread]public static int Main(string[] args){
        Console.OutputEncoding=new UTF8Encoding(false);if(args.SequenceEqual(new[]{"--self-test"})){int basic=Tests();int map=GroupMappingV02.Tests();int macro=MacroBindingCoreV03.Tests();return basic==0&&map==0&&macro==0?0:1;}
        string workspace=null,model=null,controlReport=null,mappingFile=null;string[] requested=ParseBodies("10-1,9-1,9-2,9-3");int options=5,batchSize=1;bool profileLegacy=false,uniqueCutList=false,preparedSource=false;
        string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"work","attempt-"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        string sourceHash=null,copyPath=null;ModelDoc2 previous=null;bool opened=false;NativeDisplayScope displayScope=null;int code=1;var wall=Stopwatch.StartNew();
        try{
            for(int i=0;i<args.Length;i++){
                if(args[i]=="--workspace"&&i+1<args.Length)workspace=Path.GetFullPath(args[++i]);else if(args[i]=="--model"&&i+1<args.Length)model=Path.GetFullPath(args[++i]);
                else if(args[i]=="--prepared-source")preparedSource=true;else if(args[i]=="--unique-cut-list")uniqueCutList=true;else if(args[i]=="--batch-size"&&i+1<args.Length)batchSize=int.Parse(args[++i]);else if(args[i]=="--profile-legacy-traversal")profileLegacy=true;else if(args[i]=="--control-report"&&i+1<args.Length)controlReport=Path.GetFullPath(args[++i]);else if(args[i]=="--mapping-file"&&i+1<args.Length)mappingFile=Path.GetFullPath(args[++i]);else if(args[i]=="--bodies-json"&&i+1<args.Length)requested=Json.Deserialize<string[]>(args[++i]);else if(args[i]=="--bodies"&&i+1<args.Length)requested=ParseBodies(args[++i]);else if(args[i]=="--options"&&i+1<args.Length)options=int.Parse(args[++i]);else throw new ArgumentException("Usage: StandaloneGroupNative.exe --workspace PATH --model PATH [--bodies 10-1,9-1,9-2,9-3] [--options 5|1|4]");
            }
            if(controlReport!=null&&!File.Exists(controlReport))throw new FileNotFoundException("Control report not found",controlReport);if(mappingFile!=null&&!File.Exists(mappingFile))throw new FileNotFoundException("Native mapping file not found",mappingFile);
            if(!AllowedOptions(options))throw new ArgumentException("Only native options 5, 1 and 4 allowed");if(preparedSource)SheetMetalStorage.ValidatePreparedSource(workspace,model);else ManufacturingWorkspaceV13.ValidatePaths(workspace,model);
            output=Path.Combine(Directory.GetParent(workspace).FullName,"GroupDxf_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));
            Directory.CreateDirectory(Path.Combine(output,"DXF"));report["outputFolder"]=output;report["sourceModel"]=model;report["requestedBodies"]=requested;
            var signature=typeof(IPartDoc).GetMethod("ExportToDWG2");report["installedInteropSignature"]=new{assembly=typeof(IPartDoc).Assembly.FullName,method=signature.Name,returnType=signature.ReturnType.FullName,parameters=signature.GetParameters().Select(p=>new{name=p.Name,type=p.ParameterType.FullName}).ToArray()};
            sw=Timed("CONNECT_EXISTING_SOLIDWORKS",()=> ExistingSolidWorksConnection.Attach());report["connected"]=true;
            report["solidWorksRevision"]=sw.RevisionNumber();if(!sw.RevisionNumber().StartsWith("33.",StringComparison.Ordinal))throw new InvalidOperationException("SolidWorks 2025 revision 33.x required");
            previous=sw.ActiveDoc as ModelDoc2;protectedDocs=Items(sw.GetDocuments()).Cast<ModelDoc2>().ToList();protectedState=ProtectedState();report["protectedDocumentsBefore"]=Json.DeserializeObject(protectedState);
            var existing=sw.GetOpenDocumentByName(model) as ModelDoc2;if(existing!=null&&!ManufacturingContextV02.SamePath(existing.GetPathName(),model))throw new InvalidOperationException("Workspace lookup returned another document: "+existing.GetPathName());if(existing!=null&&existing.GetSaveFlag())throw new InvalidOperationException("Workspace source dirty: filesystem snapshot would lose unsaved geometry; not saved by program");
            sourceHash=ManufacturingSourceFilesV03.Hash(model,"GROUP_EXPERIMENT_SOURCE_BEFORE");
            report["savedDependencies"]=Timed("CHECK_SAVED_REFERENCES",()=>ManufacturingWorkspaceV13.CheckPairs(model,sw.GetDocumentDependencies2(model,false,false,false),workspace));
            string ownDir=Path.Combine(workspace,"_Generated_GroupDxf_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(ownDir);copyPath=Path.Combine(ownDir,"GroupDxfProbe_"+Guid.NewGuid().ToString("N")+".SLDPRT");
            Timed("COPY_SAVED_WORKSPACE",()=>{ManufacturingSourceFilesV03.CopySavedSource(model,copyPath,sourceHash,false);File.SetAttributes(copyPath,FileAttributes.Normal);return true;});report["experimentalModel"]=copyPath;
            report["workspaceSourceComStateAvailable"]=existing!=null;string config=existing==null?"":existing.ConfigurationManager.ActiveConfiguration.Name;int error=0,warning=0;
            Timed("OPEN_EXPERIMENTAL_COPY",()=>{copy=sw.OpenDoc6(copyPath,(int)swDocumentTypes_e.swDocPART,(int)swOpenDocOptions_e.swOpenDocOptions_Silent,config,ref error,ref warning);opened=copy!=null;if(copy==null||error!=0||!ManufacturingContextV02.SamePath(copy.GetPathName(),copyPath))throw new InvalidOperationException("OpenDoc6 copy failed: error="+error+" warning="+warning);return true;});
            report["configurationApiName"]=copy.ConfigurationManager.ActiveConfiguration.Name;
            Timed("ACTIVATE_EXPERIMENTAL_COPY",()=>{int e=0;var doc=sw.ActivateDoc3(copy.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref e);if(doc==null||!Same(doc,copy))throw new InvalidOperationException("Copy activation failed: "+e);return true;});
            displayScope=new NativeDisplayScope(copy);report["temporaryCopyDisplayUpdatesDisabled"]=true;
            report["copyReferences"]=Timed("CHECK_LIVE_REFERENCES",()=>ManufacturingWorkspaceV13.LiveReferences(copy,workspace));
            report["copyNeedsRebuildBeforeBinding"]=Timed("READ_COPY_REBUILD_STATUS",()=>copy.Extension.NeedsRebuild2);
            if(profileLegacy)ProfileLegacyTraversal(1+requested.Length);
            report["legacyTraversalProfilingRequested"]=profileLegacy;
            var bindings=Timed("BIND_FLAT_PATTERNS_TO_BODIES_AND_CUT_LIST",()=>Bind(requested));
            bool returned;
            if(uniqueCutList){
                if(batchSize<1||batchSize>1000)throw new InvalidOperationException("INVALID_BATCH_SIZE");
                var representatives=Representatives(bindings);
                report["totalUniquePositions"]=representatives.Count;report["diagnosticPositionLimit"]=PreparedPositionLimit;
                if(PreparedPositionLimit>0)representatives=representatives.Take(PreparedPositionLimit).ToList();
                report["allSheetBodyCount"]=requested.Length;report["uniqueCutListExport"]=true;report["batchSize"]=batchSize;
                bindings=representatives;requested=bindings.Select(b=>b.bodyName).ToArray();report["flatPatternBindings"]=bindings;
                report["cutListQuantities"]=bindings.Select(b=>new{b.cutList,b.bodyName,b.quantity,b.flatPattern}).ToArray();
                int total=(bindings.Count+batchSize-1)/batchSize;
                returned=true;var audit=new List<object>();report["exportPreferenceAudit"]=audit;
                for(int offset=0,index=1;offset<bindings.Count;offset+=batchSize,index++){
                    var batch=bindings.Skip(offset).Take(batchSize).ToList();string label=String.Join(", ",batch.Select(b=>b.cutList+" ("+b.quantity+")"));
                    Console.WriteLine("@@BATCH|"+index+"|"+total+"|BEGIN|"+label);
                    string batchDir=Path.Combine(output,"DXF","Batch_"+index.ToString("D4"));Directory.CreateDirectory(batchDir);
                    bool singleFile=batch.Count==1;
                    string destination=Path.Combine(batchDir,(singleFile?batch[0].flatPattern+" - ":"")+"group-options-"+options+".dxf");
                    Timed("SELECT_BATCH_"+index,()=>{copy.ClearSelection2(true);foreach(var b in batch)if(!b.feature.Select2(true,-1))throw new InvalidOperationException("FLAT_PATTERN_SELECTION_FAILED: "+b.cutList);
                        var selection=(SelectionMgr)copy.SelectionManager;if(selection.GetSelectedObjectCount2(-1)!=batch.Count||batch.Where((b,i)=>!Same(selection.GetSelectedObject6(i+1,-1),b.feature)).Any())throw new InvalidOperationException("BATCH_SELECTION_IDENTITY_FAILED");return true;});
                    var nativeTimer=Stopwatch.StartNew();bool ok;
                    using(var preferences=new ManufacturingDxfPreferencesV05(sw,true,audit))using(var mappingScope=new ProductionMappingScope(sw,batchDir,audit)){
                        var call=new Dictionary<string,object>{{"api","IPartDoc.ExportToDWG2"},{"selectedCount",batch.Count},{"exportToSingleFile",singleFile},{"batch",index},{"cutLists",batch.Select(b=>b.cutList).ToArray()},{"filePath",destination}};calls.Add(call);
                        Console.WriteLine("NATIVE_ENTER Batch_"+index);
                        try{ok=Timed("EXPORT_BATCH_"+index,()=>((PartDoc)copy).ExportToDWG2(destination,copyPath,(int)swExportToDWG_e.swExportToDWG_ExportSheetMetal,singleFile,null,false,false,options,null));call["returned"]=ok;}
                        finally{call["nativeApiElapsedSeconds"]=nativeTimer.Elapsed.TotalSeconds;Console.WriteLine("NATIVE_RETURN Batch_"+index);}
                    }
                    int count=Directory.GetFiles(batchDir,"*.dxf",SearchOption.AllDirectories).Length;
                    if(!ok||count!=batch.Count)throw new InvalidOperationException("BATCH_OUTPUT_COUNT_MISMATCH: "+index+" expected="+batch.Count+" actual="+count);
                    Console.WriteLine("@@BATCH|"+index+"|"+total+"|DONE|"+nativeTimer.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                }
                Console.WriteLine("END CUT_LIST_EXPORT");
            }else{
            Timed("SELECT_ALL_NATIVE_FLAT_PATTERNS",()=>{
                copy.ClearSelection2(true);foreach(var b in bindings)if(!b.feature.Select2(true,-1))throw new InvalidOperationException("FlatPattern Select2 failed: "+b.flatPattern);
                var mgr=(SelectionMgr)copy.SelectionManager;if(mgr.GetSelectedObjectCount2(-1)!=bindings.Count)throw new InvalidOperationException("Selected feature count differs");
                for(int i=0;i<bindings.Count;i++)if(!Same(mgr.GetSelectedObject6(i+1,-1),bindings[i].feature))throw new InvalidOperationException("Native selection identity/order differs");
                report["selectedFeatures"]=bindings.Select(b=>new{b.bodyName,b.flatPattern,b.cutList,b.suppressed}).ToArray();return true;
            });
            string destination=Path.Combine(output,"DXF","group-options-"+options+".dxf");var audit=new List<object>();report["exportPreferenceAudit"]=audit;
            report["mappingFileRequested"]=mappingFile;using(var preferences=new ManufacturingDxfPreferencesV05(sw,true,audit))using(var mappingScope=new ProductionMappingScope(sw,Path.GetDirectoryName(destination),audit)){
                var call=new Dictionary<string,object>{{"api","IPartDoc.ExportToDWG2"},{"filePath",destination},{"modelName",copyPath},{"action",(int)swExportToDWG_e.swExportToDWG_ExportSheetMetal},{"exportToSingleFile",false},{"alignment",null},{"flipX",false},{"flipY",false},{"sheetMetalOptions",options},{"views",null},{"selectedCount",bindings.Count},{"configurationApiName",copy.ConfigurationManager.ActiveConfiguration.Name}};
                returned=Timed("ONE_GROUP_EXPORT_OPTIONS_"+options,()=>{if(sw.ActiveDoc==null||!Same(sw.ActiveDoc,copy)||!ManufacturingContextV02.SamePath(copy.GetPathName(),copyPath)||copy.ConfigurationManager.ActiveConfiguration.Name!=(string)report["configurationApiName"])throw new InvalidOperationException("Experimental copy/context not active before native group export");var selection=(SelectionMgr)copy.SelectionManager;if(selection.GetSelectedObjectCount2(-1)!=bindings.Count||bindings.Where((b,i)=>!Same(selection.GetSelectedObject6(i+1,-1),b.feature)).Any())throw new InvalidOperationException("Native selection changed before group export");calls.Add(call);var nativeTimer=Stopwatch.StartNew();try{bool value=((PartDoc)copy).ExportToDWG2(destination,copyPath,(int)swExportToDWG_e.swExportToDWG_ExportSheetMetal,false,null,false,false,options,null);call["returned"]=value;return value;}finally{call["nativeApiElapsedSeconds"]=nativeTimer.Elapsed.TotalSeconds;}});
            }
            }
            report["copyStateAfterNativeExport"]=State(copy);var paths=Directory.GetFiles(Path.Combine(output,"DXF"),"*.dxf",SearchOption.AllDirectories).Concat(Directory.GetFiles(ownDir,"*.dxf",SearchOption.AllDirectories)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();report["outputFileCount"]=paths.Length;report["expectedFileCount"]=requested.Length;report["allRequestedPositionsProduced"]=ExpectedFiles(requested.Length,paths.Length,returned);if(!uniqueCutList)report["nativeOneCallProducedExpectedFileCount"]=ExpectedFiles(requested.Length,paths.Length,returned);
            var inspections=new List<object>();foreach(string path in paths){
                string text=File.ReadAllText(path);var inspection=ManufacturingNativeDxfV05.Inspect(text);
                inspections.Add(new{path,bytes=new FileInfo(path).Length,sha256=ManufacturingSourceFilesV03.Hash(path,"GROUP_EXPERIMENT_OUTPUT"),inspection,
                    openPrimitiveDiagnostic=inspection.splineCount==0?(object)ManufacturingDxfV04.Check(text,null,.001,false,inspection.units==4):null,
                    fileToBodyMapping="NEEDS_REVIEW: filename is evidence only, not native ownership or full-geometry proof",cutContoursVerified=false,bendLineCountVerified=false,scaleOneToOneVerified=false,
                    status="NEEDS_REVIEW",reason="Group call/count/selection can be tested independently; bounded output geometry and per-file ownership require validation against verified individual exports"});
            }report["files"]=inspections;
            report["status"]=ExpectedFiles(requested.Length,paths.Length,returned)?"GROUP_FILES_CREATED_NEEDS_REVIEW":"GROUP_EXPORT_INCOMPLETE";code=ExpectedFiles(requested.Length,paths.Length,returned)?2:1;
        }catch(Exception ex){report["error"]=new{stage,exception=ex.GetType().FullName,hresult="0x"+ex.HResult.ToString("X8"),message=ex.Message,stackTrace=ex.StackTrace};Console.Error.WriteLine(ex.ToString());}
        finally{
            if(displayScope!=null)try{displayScope.Dispose();report["temporaryCopyDisplayRestored"]=true;}catch(Exception ex){report["displayRestoreError"]=ex.ToString();code=1;report["status"]="DISPLAY_RESTORE_FAILED";}
            if(opened&&copy!=null)try{string title=copy.GetTitle();if(Items(sw.GetDocuments()).Cast<ModelDoc2>().Any(d=>d.GetTitle()==title&&!ManufacturingContextV02.SamePath(d.GetPathName(),copyPath)))throw new InvalidOperationException("Ambiguous close title: "+title);sw.CloseDoc(title);if(sw.GetOpenDocumentByName(copyPath)!=null)throw new InvalidOperationException("Experimental copy remains open");report["experimentalCopyClosedWithoutSave"]=true;if(!ManufacturingWorkspaceV13.Inside(workspace,copyPath)||ManufacturingContextV02.SamePath(copyPath,model))throw new InvalidOperationException("TEMP_COPY_DELETE_GUARD");JobOwnership.Validate(copyPath,workspace);File.Delete(copyPath);report["temporaryModelCopyDeleted"]=!File.Exists(copyPath);}catch(Exception ex){report["cleanupError"]=ex.ToString();code=1;report["status"]="CLEANUP_FAILED";}
            if(sw!=null&&previous!=null)try{int e=0;sw.ActivateDoc3(previous.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref e);}catch(Exception ex){report["activationRestoreError"]=ex.ToString();code=1;}
            try{if(protectedState!=null){string after=ProtectedState();report["protectedDocumentsAfter"]=Json.DeserializeObject(after);report["protectedDocumentsUnchanged"]=after==protectedState;if(after!=protectedState){report["status"]="PROTECTED_STATE_CHANGED";code=1;}}if(sourceHash!=null){string after=ManufacturingSourceFilesV03.Hash(model,"GROUP_EXPERIMENT_SOURCE_AFTER");report["sourceFileSHA256Before"]=sourceHash;report["sourceFileSHA256After"]=after;if(after!=sourceHash){report["status"]="SOURCE_FILE_CHANGED";code=1;}}}catch(Exception ex){report["safetyCheckError"]=ex.ToString();code=1;}
            report["retainedDxfFiles"]=Directory.Exists(Path.Combine(output,"DXF"))?Directory.GetFiles(Path.Combine(output,"DXF"),"*.dxf",SearchOption.AllDirectories):new string[0];report["sourceMutatingApiCallsMade"]=false;report["explicitExperimentalCopySaveCallsMade"]=false;report["nativeCallCount"]=calls.Count;report["elapsedSeconds"]=wall.Elapsed.TotalSeconds;report["exitCode"]=code;
            try{Directory.CreateDirectory(Path.Combine(output,"Reports"));string path=Path.Combine(output,"Reports","group-dxf-report.json");using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(file,new UTF8Encoding(false)))writer.Write(Json.Serialize(report));Console.WriteLine("Report: "+path);}catch(Exception ex){Console.Error.WriteLine("Report write failed: "+ex);code=1;}
        }
        if(code==2&&controlReport!=null&&options==5){try{
            var compareTimer=Stopwatch.StartNew();var comparison=GroupDxfCompareV02.CompareAndName(Path.Combine(output,"Reports","group-dxf-report.json"),controlReport,Path.Combine(output,"Validated_DXF"));
            var summary=(Dictionary<string,object>)Json.DeserializeObject(Json.Serialize(comparison));report["geometryComparison"]=summary;
            report["status"]=Convert.ToInt32(summary["geometryMatched"])==requested.Length&&Convert.ToInt32(summary["total"])==requested.Length?"GROUP_EXACT_AND_BEND_VERIFIED_CAM_NEEDS_REVIEW":"GROUP_FILES_CREATED_GEOMETRY_NEEDS_REVIEW";
            times.Add(new{stage="OFFLINE_FULL_CURVE_GEOMETRY_COMPARISON",elapsedSeconds=compareTimer.Elapsed.TotalSeconds,callCompleted=true});
        }catch(Exception ex){report["geometryComparisonError"]=ex.ToString();report["status"]="GROUP_FILES_CREATED_GEOMETRY_NEEDS_REVIEW";}
            report["elapsedSeconds"]=wall.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(output,"Reports","group-dxf-report.json"),Json.Serialize(report),new UTF8Encoding(false));
        }
        var durations=times.Select(t=>(Dictionary<string,object>)Json.DeserializeObject(Json.Serialize(t))).ToArray();
        Func<string,double?> duration=name=>{var row=durations.LastOrDefault(t=>(string)t["stage"]==name);return row==null?(double?)null:Convert.ToDouble(row["elapsedSeconds"]);};
        report["performanceSummary"]=new{bindingSeconds=duration("BIND_FLAT_PATTERNS_TO_BODIES_AND_CUT_LIST"),nativeExportSeconds=calls.Count==1&&((Dictionary<string,object>)calls[0]).ContainsKey("nativeApiElapsedSeconds")?((Dictionary<string,object>)calls[0])["nativeApiElapsedSeconds"]:null,exportStageSeconds=duration("ONE_GROUP_EXPORT_OPTIONS_"+options),offlineComparisonSeconds=duration("OFFLINE_FULL_CURVE_GEOMETRY_COMPARISON"),totalSeconds=wall.Elapsed.TotalSeconds,legacyProfilerEnabled=profileLegacy,comparisonWithUserMacro="Macro exported two Cut List representatives; this experiment requests four bodies. No measured speedup claimed until live COM run."};
        if(Directory.Exists(Path.Combine(output,"Reports")))File.WriteAllText(Path.Combine(output,"Reports","group-dxf-report.json"),Json.Serialize(report),new UTF8Encoding(false));
        Console.WriteLine("PERFORMANCE "+Json.Serialize(report["performanceSummary"]));
        Console.WriteLine("StandaloneGroupNative | "+report["status"]+" | exit="+code);return code;
    }
}

