using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public static class NativeExportComparison {
    static string Hash(string p){return ManufacturingSourceFilesV03.Hash(p,"NATIVE_EXPORT_COMPARISON");}
    static void Save(string p,object r){File.WriteAllText(p,CutListCoreV01.Json.Serialize(r),new UTF8Encoding(false));}
    static string BodyName(Feature feature){dynamic entity=((FlatPatternFeatureData)feature.GetDefinition()).FixedFace2;if(entity==null)throw new Exception("FIXED_ENTITY_UNAVAILABLE: "+feature.Name);Body2 body=entity.GetBody();if(body==null)throw new Exception("FIXED_ENTITY_BODY_UNAVAILABLE: "+feature.Name);return body.Name;}
    static object Prefs(SldWorks sw){return new{splines=sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDxfExportSplinesAsSplines),merge=sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDxfEndPointMerge),mapping=sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDxfMapping)};}
    public static Dictionary<string,object> Run(SldWorks sw,string source,string results){
        var original=sw.GetOpenDocumentByName(source) as ModelDoc2;
        if(original==null||original.GetSaveFlag())throw new Exception("SAVED_ORIGINAL_MUST_BE_OPEN");
        string hash=Hash(source),config=original.ConfigurationManager.ActiveConfiguration.Name;
        int stamp=original.GetUpdateStamp();
        if(NativeWeldmentAdapter.Items(sw.GetDocumentDependencies2(source,false,false,false)).Length!=0)throw new Exception("PACK_AND_GO_REQUIRED_FOR_DEPENDENCIES");
        string time=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time")).ToString("yyyyMMdd_HHmmss");
        string root=Path.Combine(Path.GetDirectoryName(source),"_Manufacturing","NativeExportComparison_"+time+"_"+Guid.NewGuid().ToString("N").Substring(0,8));
        Directory.CreateDirectory(root);
        var trials=new List<object>();var report=new Dictionary<string,object>{{"status","RUNNING"},{"source",source},{"sourceSHA256Before",hash},{"folder",root},{"trials",trials},{"productionCertified",false},{"initialPreferences",Prefs(sw)}};
        var all=Stopwatch.StartNew();
        try{
            for(int n=0;n<4;n++){
                ManufacturingAgent.Boundary("NATIVE_COMPARISON_"+(n+1)+"_OF_4");
                if(original.GetSaveFlag()||original.GetUpdateStamp()!=stamp||original.ConfigurationManager.ActiveConfiguration.Name!=config||Hash(source)!=hash)throw new Exception("ORIGINAL_STATE_CHANGED");
                string name=new[]{"SELECT2_USER_PREFS","MULTISELECT2_USER_PREFS","SELECT2_DOTTED_FOLDER","SELECT2_EXACT_PREFS"}[n];
                string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);
                string path=Path.Combine(dir,"Test.SLDPRT"),outdir=Path.Combine(dir,n==2?"Native.tmp":"Native");Directory.CreateDirectory(outdir);
                string destination=Path.Combine(outdir,"NativeBatch.dxf");
                ModelDoc2 copy=null;var timer=Stopwatch.StartNew();var audit=new List<object>();
                var trial=new Dictionary<string,object>{{"name",name},{"model",path},{"destination",destination},{"preferenceAudit",audit},{"exactPreferences",n==3},{"selectionMethod",n==1?"MultiSelect2":"Select2"},{"selectionMark",-1},{"sheetMetalOptions",5},{"exportToSingleFile",false},{"alignment",null},{"views",null}};trials.Add(trial);
                try{
                    ManufacturingSourceFilesV03.CopySavedSource(source,path,hash,false);File.SetAttributes(path,FileAttributes.Normal);
                    int error=0,warning=0;copy=sw.OpenDoc6(path,1,1,config,ref error,ref warning);
                    if(copy==null||error!=0)throw new Exception("COPY_OPEN_FAILED: "+error);
                    int activation=0;sw.ActivateDoc3(copy.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref activation);
                    if(sw.IsSame(sw.ActiveDoc,copy)!=(int)swObjectEquality.swObjectSame)throw new Exception("COPY_NOT_ACTIVE");
                    var features=MacroBindingCoreV03.Collect(copy.FirstFeature() as Feature,f=>f.GetNextFeature() as Feature,f=>f.GetFirstSubFeature() as Feature,f=>f.GetNextSubFeature() as Feature,f=>f.GetID(),10000).Where(f=>f.GetTypeName2()=="FlatPattern").ToArray();
                    if(features.Length==0)throw new Exception("NO_FLAT_PATTERNS");
                    trial["features"]=features.Select(f=>new{name=f.Name,body=BodyName(f)}).ToArray();
                    using(var prefs=n==3?new ManufacturingDxfPreferencesV05(sw,true,audit):null)
                    using(var mapping=new ProductionMappingScope(sw,dir,audit)){
                        trial["effectivePreferences"]=Prefs(sw);
                        copy.ClearSelection2(true);
                        if(n==1){var data=((SelectionMgr)copy.SelectionManager).CreateSelectData();data.Mark=-1;int count=copy.Extension.MultiSelect2(features.Cast<object>().ToArray(),false,data);trial["multiSelectReturned"]=count;}
                        else foreach(var f in features)if(!f.Select2(true,-1))throw new Exception("SELECT2_FAILED: "+f.Name);
                        var manager=(SelectionMgr)copy.SelectionManager;
                        if(manager.GetSelectedObjectCount2(-1)!=features.Length)throw new Exception("SELECTION_COUNT_DIFFERS");
                        var selection=new List<object>();
                        for(int i=1;i<=features.Length;i++){
                            object selected=manager.GetSelectedObject6(i,-1);
                            if(features.Count(f=>sw.IsSame(selected,f)==(int)swObjectEquality.swObjectSame)!=1)throw new Exception("SELECTION_IDENTITY_DIFFERS");
                            selection.Add(new{name=((Feature)selected).Name,mark=manager.GetSelectedObjectMark(i)});
                        }
                        trial["selection"]=selection;
                        if(sw.IsSame(sw.ActiveDoc,copy)!=(int)swObjectEquality.swObjectSame||copy.ConfigurationManager.ActiveConfiguration.Name!=config)throw new Exception("EXPORT_CONTEXT_CHANGED");
                        ManufacturingAgent.Boundary("EXPORT_"+name);var native=Stopwatch.StartNew();
                        try{trial["apiReturned"]=((PartDoc)copy).ExportToDWG2(destination,path,1,false,null,false,false,5,null);}
                        finally{trial["nativeSeconds"]=native.Elapsed.TotalSeconds;}
                    }
                    var files=Directory.GetFiles(outdir,"*.dxf",SearchOption.AllDirectories);
                    trial["files"]=files.Select(p=>new{path=p,bytes=new FileInfo(p).Length,sha256=Hash(p),inspection=ManufacturingNativeDxfV05.Inspect(File.ReadAllText(p))}).ToArray();
                    trial["expectedCount"]=features.Length;trial["actualCount"]=files.Length;
                    trial["status"]=(bool)trial["apiReturned"]&&files.Length==features.Length?"API_TRUE_FILES_CREATED_GEOMETRY_NOT_CERTIFIED":"API_FALSE_OR_INCOMPLETE";
                }catch(OperationCanceledException){throw;}
                catch(Exception e){trial["status"]="FAILED";trial["exception"]=e.ToString();trial["hresult"]="0x"+e.HResult.ToString("X8");if(e.Message.Contains("RESTORE")||e.Message.Contains("ORIGINAL_MAPPING_FILE_CHANGED"))throw;}
                finally{
                    if(copy!=null)try{sw.CloseDoc(copy.GetTitle());}catch(Exception e){trial["cleanupError"]=e.ToString();throw;}
                    trial["elapsedSeconds"]=timer.Elapsed.TotalSeconds;trial["preferencesAfter"]=Prefs(sw);
                    Save(Path.Combine(root,"native-comparison.json"),report);
                }
            }
            report["status"]="COMPARISON_COMPLETED_NEEDS_REVIEW";
        }catch(Exception e){report["status"]="FAILED";report["exception"]=e.ToString();}
        finally{
            report["sourceSHA256After"]=Hash(source);report["sourceFileUnchanged"]=Hash(source)==hash;
            report["sourceMemoryUnchanged"]=!original.GetSaveFlag()&&original.GetUpdateStamp()==stamp&&original.ConfigurationManager.ActiveConfiguration.Name==config;
            report["elapsedSeconds"]=all.Elapsed.TotalSeconds;report["finalPreferences"]=Prefs(sw);
            if(!(bool)report["sourceFileUnchanged"]||!(bool)report["sourceMemoryUnchanged"])report["status"]="SOURCE_STATE_CHANGED";
            Save(Path.Combine(root,"native-comparison.json"),report);Save(Path.Combine(results,"native-comparison.json"),report);
        }
        return report;
    }
}
