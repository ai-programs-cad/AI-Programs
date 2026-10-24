using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public static class MacroTestWork {
    static string Hash(string p){return ManufacturingSourceFilesV03.Hash(p,"MACRO_TEST");}
    static void Save(string p,object o){File.WriteAllText(p,CutListCoreV01.Json.Serialize(o),new UTF8Encoding(false));}
    public static Dictionary<string,object> Run(SldWorks sw,string source,string result){
        string macro=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Resources","Flat_DXF.swp");
        var original=sw.GetOpenDocumentByName(source) as ModelDoc2;
        if(original!=null&&original.GetSaveFlag())throw new InvalidOperationException("SAVED_ORIGINAL_REQUIRED_FOR_MACRO_TEST");
        string hash=Hash(source),config=original==null?"":original.ConfigurationManager.ActiveConfiguration.Name;int stamp=original==null?0:original.GetUpdateStamp();
        string root=Path.Combine(Path.GetDirectoryName(source),"_Manufacturing","MacroTest_"+TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time")).ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));
        string workspace=Path.Combine(root,"Workspace");Directory.CreateDirectory(workspace);
        var report=new Dictionary<string,object>{{"status","RUNNING"},{"macroSource",macro},{"macroSourceSHA256",Hash(macro)},{"original",source},{"originalSHA256Before",hash},{"testFolder",root},{"productionCertified",false}};
        string copyPath=Path.Combine(workspace,"MacroTest_"+Guid.NewGuid().ToString("N")+".SLDPRT"),macroCopy=Path.Combine(root,"MacroUnderTest.swp");
        ModelDoc2 copy=null;var clock=Stopwatch.StartNew();
        try{
            ManufacturingAgent.Boundary("PREPARE_MACRO_TEST_COPY");
            var deps=NativeWeldmentAdapter.Items(sw.GetDocumentDependencies2(source,false,false,false));if(deps.Length!=0)throw new Exception("MACRO_TEST_REQUIRES_ISOLATED_DEPENDENCIES: Pack and Go required");
            ManufacturingSourceFilesV03.CopySavedSource(source,copyPath,hash,false);ManufacturingSourceFilesV03.CopySavedSource(macro,macroCopy,Convert.ToString(report["macroSourceSHA256"]),false);report["macroCopy"]=macroCopy;report["modelCopy"]=copyPath;
            if(Hash(macroCopy)!=Convert.ToString(report["macroSourceSHA256"]))throw new Exception("MACRO_COPY_HASH_MISMATCH");
            int error=0,warning=0;copy=sw.OpenDoc6(copyPath,1,1,config,ref error,ref warning);if(copy==null||error!=0)throw new Exception("MACRO_TEST_COPY_OPEN_FAILED: "+error);
            int activation=0;sw.ActivateDoc3(copy.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref activation);if(sw.IsSame(sw.ActiveDoc,copy)!=(int)swObjectEquality.swObjectSame)throw new Exception("MACRO_TEST_COPY_NOT_ACTIVE");
            var features=MacroBindingCoreV03.Collect(copy.FirstFeature() as Feature,f=>f.GetNextFeature() as Feature,f=>f.GetFirstSubFeature() as Feature,f=>f.GetNextSubFeature() as Feature,f=>f.GetID(),10000).Where(f=>f.GetTypeName2()=="FlatPattern").ToArray();
            var probes=new List<object>();report["selectionProbes"]=probes;
            var view=copy.IActiveView;bool previousGraphics=view.EnableGraphicsUpdate;
            try{
                foreach(bool graphics in new[]{false,true})foreach(bool firstAppend in new[]{true,false}){
                    ManufacturingAgent.Boundary("PROBE_SELECTION graphics="+graphics+" firstAppend="+firstAppend);
                    view.EnableGraphicsUpdate=graphics;copy.ClearSelection2(true);var selected=new List<object>();int i=0;
                    foreach(var feature in features){bool ok=feature.Select2(i++==0?firstAppend:true,-1);selected.Add(new{name=feature.Name,returned=ok,suppressed=feature.IsSuppressed()});if(!ok)break;}
                    probes.Add(new{graphicsEnabled=graphics,firstAppend,featuresAttempted=selected,selectedCount=((SelectionMgr)copy.SelectionManager).GetSelectedObjectCount2(-1),expectedCount=features.Length,activePath=((ModelDoc2)sw.ActiveDoc).GetPathName()});
                }
            }finally{copy.ClearSelection2(true);view.EnableGraphicsUpdate=previousGraphics;}
            var methods=NativeWeldmentAdapter.Items(sw.GetMacroMethods(macroCopy,(int)swMacroMethods_e.swMethodsWithoutArguments)).Select(Convert.ToString).ToArray();report["availableMacroMethods"]=methods;
            var entries=methods.Where(m=>m.EndsWith(".main",StringComparison.OrdinalIgnoreCase)).ToArray();if(entries.Length!=1)throw new Exception("MACRO_MAIN_ENTRY_NOT_UNIQUE: "+String.Join(", ",methods));
            string module=entries[0].Substring(0,entries[0].LastIndexOf('.'));
            Save(Path.Combine(root,"macro-test-report.json"),report);
            ManufacturingAgent.Boundary("RUN_USER_VBA_MACRO: close its result/error message with OK");
            int macroError;var timer=Stopwatch.StartNew();bool returned=sw.RunMacro2(macroCopy,module,"main",(int)swRunMacroOption_e.swRunMacroUnloadAfterRun,out macroError);
            report["RunMacro2"]=new{returned,error=macroError,elapsedSeconds=timer.Elapsed.TotalSeconds,module,procedure="main"};
            var outputs=Directory.GetFiles(workspace,"*.dxf",SearchOption.AllDirectories);report["createdDxfFiles"]=outputs;
            var summaries=Directory.GetFiles(workspace,"batch-report.txt",SearchOption.AllDirectories);report["macroSummaryFiles"]=summaries;
            bool summaryPassed=summaries.Length==1&&File.ReadAllText(summaries[0]).Contains("Preparation errors: 0");
            report["status"]=returned&&macroError==0&&outputs.Length>0&&summaryPassed?"MACRO_TEST_COMPLETED_NEEDS_REVIEW":"MACRO_TEST_FAILED_OR_INCOMPLETE";
        }catch(Exception e){report["status"]="MACRO_TEST_FAILED_OR_INCOMPLETE";report["error"]=new{exception=e.GetType().FullName,message=e.Message,hresult="0x"+e.HResult.ToString("X8"),stackTrace=e.StackTrace};}
        finally{
            if(copy!=null)try{sw.CloseDoc(copy.GetTitle());}catch(Exception e){report["cleanupError"]=e.ToString();}
            report["originalSHA256After"]=Hash(source);report["originalFileUnchanged"]=Hash(source)==hash;report["originalWasOpen"]=original!=null;report["originalMemoryStateUnchanged"]=original==null?sw.GetOpenDocumentByName(source)==null:!original.GetSaveFlag()&&original.GetUpdateStamp()==stamp&&original.ConfigurationManager.ActiveConfiguration.Name==config;
            report["macroSourceUnchanged"]=Hash(macro)==Convert.ToString(report["macroSourceSHA256"]);report["elapsedSeconds"]=clock.Elapsed.TotalSeconds;
            if(!(bool)report["originalFileUnchanged"]||!(bool)report["originalMemoryStateUnchanged"]||!(bool)report["macroSourceUnchanged"])report["status"]="MACRO_TEST_SOURCE_STATE_CHANGED";
            Save(Path.Combine(root,"macro-test-report.json"),report);Save(Path.Combine(result,"macro-test-report.json"),report);
        }return report;
    }
}
