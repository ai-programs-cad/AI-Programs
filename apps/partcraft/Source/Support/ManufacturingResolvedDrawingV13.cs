using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public partial class ManufacturingSolidWorksV13 {
    ModelDoc2 OpenResolvedPortableDrawing(string path){
        var audit=new Dictionary<string,object>();current["resolvedDrawingOpen"]=audit;
        return NativeDrawingModeV13.Open(sw,path,Path.Combine(files.Root,"Workspace"),audit,d=>{CopyOnly(d);created.Add(d);});
    }
    public static ModelDoc2 LoadedViewModel(SolidWorks.Interop.sldworks.View view){
        var direct=view.ReferencedDocument;if(direct!=null)return direct;
        var root=view.RootDrawingComponent;var component=root==null?null:root.Component;
        return component==null?null:component.GetModelDoc2() as ModelDoc2;
    }
    public static string LoadedViewConfiguration(SolidWorks.Interop.sldworks.View view){
        string direct=view.ReferencedConfiguration;if(!String.IsNullOrEmpty(direct))return direct;
        var root=view.RootDrawingComponent;var component=root==null?null:root.Component;
        return component==null?null:component.ReferencedConfiguration;
    }
    object ViewLoadState(SolidWorks.Interop.sldworks.View v){
        var data=new Dictionary<string,object>{{"name",v.GetName2()}};
        Action<string,Func<object>> read=(key,get)=>{try{data[key]=get();}catch(Exception ex){data[key+"Error"]=ex.ToString();}};
        read("IsModelLoaded",()=>v.IsModelLoaded());read("IsLightweight",()=>v.IsLightweight());read("GetReferencedModelName",()=>v.GetReferencedModelName());
        read("ReferencedDocument",()=>v.ReferencedDocument==null?null:v.ReferencedDocument.GetPathName());read("ReferencedConfiguration",()=>v.ReferencedConfiguration);
        read("rootComponentModel",()=>{var c=v.RootDrawingComponent;return c==null||c.Component==null?null:c.Component.GetPathName();});
        read("rootComponentConfiguration",()=>{var c=v.RootDrawingComponent;return c==null||c.Component==null?null:c.Component.ReferencedConfiguration;});
        read("effectiveLoadedModel",()=>{var m=LoadedViewModel(v);return m==null?null:m.GetPathName();});read("effectiveConfiguration",()=>LoadedViewConfiguration(v));read("bendLines",()=>v.GetBendLineCount());
        return data;
    }
    void ResolvePortableViews(DrawingDoc drawing){
        var audit=new List<object>();current["viewModelLoading"]=audit;
        foreach(var view in ManufacturingGeometryV01.Items(drawing.GetViews()).SelectMany(ManufacturingGeometryV01.Items).Cast<SolidWorks.Interop.sldworks.View>().Where(v=>!String.IsNullOrEmpty(v.GetReferencedModelName()))){
            var row=new Dictionary<string,object>{{"before",ViewLoadState(view)}};audit.Add(row);
            try{
                if(view.IsLightweight()){view.SetLightweightToResolved();row["SetLightweightToResolvedCalled"]=true;}
                if(!view.IsModelLoaded()||LoadedViewModel(view)==null||String.IsNullOrEmpty(LoadedViewConfiguration(view))){row["LoadModelReturnCode"]=view.LoadModel();}
            }catch(Exception ex){row["error"]=ex.ToString();}
            row["after"]=ViewLoadState(view);row["openDocuments"]=OpenDocumentPaths();
        }
        // VerifyView subsequently reads fresh view objects and independently
        // requires real configuration, geometry, material body and native edges.
    }
    object OpenDocumentPaths(){return ManufacturingGeometryV01.Items(sw.GetDocuments()).Cast<ModelDoc2>().Select(d=>new{path=d.GetPathName(),title=d.GetTitle(),dirty=d.GetSaveFlag()}).ToArray();}
    void CloseOwnedForReopen(string path){
        if(!ProductionReopenChecksV13.CanClose(Path.Combine(files.Root,"Workspace"),original.GetPathName(),path,false))throw new InvalidOperationException("Refusing to close Workspace base or outside document: "+path);
        var matches=ManufacturingGeometryV01.Items(sw.GetDocuments()).Cast<ModelDoc2>().Where(d=>ManufacturingContextV02.SamePath(d.GetPathName(),path)).ToList();
        var memory=sw.GetOpenDocumentByName(path) as ModelDoc2;
        if(memory!=null){if(!ManufacturingContextV02.SamePath(memory.GetPathName(),path))throw new InvalidOperationException("Ambiguous native document lookup: "+memory.GetPathName());if(!matches.Any(d=>sw.IsSame(d,memory)==(int)swObjectEquality.swObjectSame))matches.Add(memory);}
        foreach(var d in matches){if(d.GetSaveFlag())throw new InvalidOperationException("UNSAVED_OWNED_DOCUMENT_NOT_CLOSED: "+path);string title=d.GetTitle();
            if(ManufacturingGeometryV01.Items(sw.GetDocuments()).Cast<ModelDoc2>().Any(x=>x.GetTitle()==title&&!ManufacturingContextV02.SamePath(x.GetPathName(),path)))throw new InvalidOperationException("AMBIGUOUS_CLOSE_TITLE: "+title);
            ManufacturingPerformanceV13.Measure(ManufacturingPerformanceV13.Body,"CLOSE_DOCUMENT",()=>sw.CloseDoc(title));created.Remove(d);
        }
        bool stillOpen=ManufacturingGeometryV01.Items(sw.GetDocuments()).Cast<ModelDoc2>().Any(d=>ManufacturingContextV02.SamePath(d.GetPathName(),path));
        var after=sw.GetOpenDocumentByName(path) as ModelDoc2;string loadedPath=after==null?null:after.GetPathName();
        stillOpen=stillOpen||ManufacturingContextV02.SamePath(path,loadedPath);
        ProductionReopenChecksV13.History(current,"closeChecks").Add(new{path,previouslyOpen=matches.Count,stillOpen,memoryLookupAfterClose=loadedPath,openDocuments=OpenDocumentPaths()});
        if(stillOpen)throw new InvalidOperationException("TEMPORARY_DOCUMENT_REMAINS_OPEN: "+path);
    }

}
public static class NativeDrawingModeV13 {
    static object Documents(SldWorks sw){return ManufacturingGeometryV01.Items(sw.GetDocuments()).Cast<ModelDoc2>().Select(d=>new{path=d.GetPathName(),title=d.GetTitle(),dirty=d.GetSaveFlag()}).ToArray();}
    public static ModelDoc2 Open(SldWorks sw,string path,string ownedRoot,Dictionary<string,object> audit,Action<ModelDoc2> register){return ManufacturingPerformanceV13.Measure(ManufacturingPerformanceV13.Body,"OPEN_RESOLVED_DRAWING",()=>OpenCore(sw,path,ownedRoot,audit,register));}
    public static ModelDoc2 OpenCore(SldWorks sw,string path,string ownedRoot,Dictionary<string,object> audit,Action<ModelDoc2> register){
        if(!ManufacturingWorkspaceV13.Inside(ownedRoot,path))throw new InvalidOperationException("Native drawing open outside explicitly owned copy folder");
        audit["api"]="ISldWorks.OpenDoc7";audit["path"]=path;audit["DetailingMode"]=false;audit["LoadModel"]=true;audit["ReadOnly"]=true;audit["LightWeight"]=false;audit["ViewOnly"]=false;
        audit["openDocumentsBeforeOpenDoc7"]=Documents(sw);var prior=sw.GetOpenDocumentByName(path) as ModelDoc2;audit["memoryLookupBeforeOpenDoc7"]=prior==null?null:prior.GetPathName();
        if(prior!=null)throw new InvalidOperationException("Portable drawing already loaded before OpenDoc7: "+prior.GetPathName());
        var spec=(DocumentSpecification)sw.GetOpenDocSpec(path);spec.DocumentType=(int)swDocumentTypes_e.swDocDRAWING;spec.Silent=true;spec.ReadOnly=true;spec.DetailingMode=false;spec.LoadModel=true;spec.LightWeight=false;spec.UseLightWeightDefault=false;spec.Selective=false;spec.ViewOnly=false;
        var doc=sw.OpenDoc7(spec);audit["errorCode"]=spec.Error;audit["warningCode"]=spec.Warning;
        if(doc==null)throw new InvalidOperationException("Native drawing open returned null, error="+spec.Error);
        audit["actualPath"]=doc.GetPathName();if(!ManufacturingContextV02.SamePath(path,doc.GetPathName()))throw new InvalidOperationException("Native drawing open returned another document");
        register(doc);int error=0;var active=sw.ActivateDoc3(doc.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref error) as ModelDoc2;
        if(active==null||sw.IsSame(active,doc)!=(int)swObjectEquality.swObjectSame)throw new InvalidOperationException("Exact native drawing activation failed: "+error);
        audit["isDetailingModeAfterOpen"]=((DrawingDoc)doc).IsDetailingMode();if(spec.Error!=0)throw new InvalidOperationException("Native drawing open error="+spec.Error);
        if(((DrawingDoc)doc).IsDetailingMode()){using(var resolveTiming=ManufacturingPerformanceV13.Begin(ManufacturingPerformanceV13.Body,"RESOLVE_DRAWING")){
            int command=(int)SolidWorks.Interop.swcommands.swCommands_e.swCommands_Resolve_Drawing;audit["resolveCommand"]=new{api="ISldWorks.RunCommand",name="swCommands_Resolve_Drawing",id=command};
            audit["resolveCommandEnabled"]=sw.IsCommandEnabled(command);if(!(bool)audit["resolveCommandEnabled"])throw new InvalidOperationException("Native Resolve Drawing command disabled");
            audit["resolveCommandReturned"]=sw.RunCommand(command,"");var wait=System.Diagnostics.Stopwatch.StartNew();while(((DrawingDoc)doc).IsDetailingMode()&&wait.ElapsedMilliseconds<5000)System.Threading.Thread.Sleep(100);
            audit["resolvePostconditionWaitMs"]=wait.ElapsedMilliseconds;audit["isDetailingModeAfterResolve"]=((DrawingDoc)doc).IsDetailingMode();audit["openDocumentsAfterResolve"]=Documents(sw);if(!((DrawingDoc)doc).IsDetailingMode())resolveTiming.Success();}
        }
        audit["isDetailingModeFinal"]=((DrawingDoc)doc).IsDetailingMode();if((bool)audit["isDetailingModeFinal"])throw new InvalidOperationException("Native Resolve Drawing did not leave Detailing");return doc;
    }
}
public static class ProductionReopenChecksV13 {
    public static List<object> History(Dictionary<string,object> record,string key){
        object value;if(!record.TryGetValue(key,out value)||value==null){var empty=new List<object>();record[key]=empty;return empty;}
        var list=value as List<object>;if(list!=null)return list;
        var sequence=value as System.Collections.IEnumerable;if(sequence==null||value is string)throw new InvalidOperationException("Unsupported diagnostic history type for "+key+": "+value.GetType().FullName);
        var normalized=sequence.Cast<object>().ToList();record[key]=normalized;return normalized;
    }
    public static bool BendsVerified(int actual,int expected){return expected>0&&actual==expected;}
    public static bool FullyLoaded(bool detailing,bool loaded,bool lightweight,string configuration){return !detailing&&loaded&&!lightweight&&!String.IsNullOrEmpty(configuration);}
    public static bool CanClose(string workspace,string basePath,string candidate,bool dirty){return !dirty&&ManufacturingWorkspaceV13.Inside(workspace,candidate)&&!ManufacturingContextV02.SamePath(basePath,candidate);}
    public static string Binding(string expected,string declared,string loaded){return !ManufacturingContextV02.SamePath(expected,loaded)?"REFERENCE_BINDING_INCOMPLETE":!ManufacturingContextV02.SamePath(expected,declared)?"REFERENCE_METADATA_UNRESOLVED":"EXPECTED_PORTABLE_DOCUMENT";}
    public static int Tests(){
        string w=@"C:\Project\_Manufacturing\Run_test\Workspace",b=w+@"\Base.SLDPRT",p=w+@"\Portable\10-1\10-1.SLDPRT";int fail=0,total=0;
        Action<string,bool> test=(n,pass)=>{total++;if(!pass){fail++;Console.Error.WriteLine("FAIL: "+n);}};
        test("Base never closed",!CanClose(w,b,b,false));test("Original outside Workspace never closed",!CanClose(w,b,@"C:\Project\Base.SLDPRT",false));test("Dirty model never closed",!CanClose(w,b,p,true));test("Owned clean temporary model can close",CanClose(w,b,w+@"\Models\10-1.SLDPRT",false));test("Prefix collision refused",!CanClose(w,b,w+@"_other\10-1.SLDPRT",false));
        test("Exact portable binding accepted",Binding(p,p,p)=="EXPECTED_PORTABLE_DOCUMENT");test("Cached old loaded object detected",Binding(p,p,w+@"\Models\10-1.SLDPRT")=="REFERENCE_BINDING_INCOMPLETE");test("Stale metadata distinct from wrong binding",Binding(p,w+@"\Models\10-1.SLDPRT",p)=="REFERENCE_METADATA_UNRESOLVED");test("Null loaded object incomplete",Binding(p,p,null)=="REFERENCE_BINDING_INCOMPLETE");test("Cyrillic normalized Windows path accepted",Binding(w+@"\Деталь.SLDPRT",w+@"\Деталь.SLDPRT",w+@"\Деталь.sldprt")=="EXPECTED_PORTABLE_DOCUMENT");
        var array=new Dictionary<string,object>{{"restorationHistory",new object[]{"existing"}}};var normalized=History(array,"restorationHistory");normalized.Add("new");test("Restore Object[] regression preserves previous records",normalized.Count==2&&Convert.ToString(normalized[0])=="existing");
        var existing=new List<object>{1};test("Restore existing List is supported",Object.ReferenceEquals(existing,History(new Dictionary<string,object>{{"h",existing}},"h")));
        test("Restore absent history initializes empty",History(new Dictionary<string,object>(),"h").Count==0);
        test("Restore null history initializes empty",History(new Dictionary<string,object>{{"h",null}},"h").Count==0);
        var roundtrip=AB.Obj(CutListCoreV01.Json.DeserializeObject("{\"restorationHistory\":[{\"step\":1}]}"));test("Restore actual JSON deserialization regression",History(roundtrip,"restorationHistory").Count==1);
        bool rejected=false;try{History(new Dictionary<string,object>{{"h","wrong scalar"}},"h");}catch(InvalidOperationException){rejected=true;}test("Corrupt history is not silently discarded",rejected);
        test("Zero bend lines cannot pass",!BendsVerified(0,10));test("Real baseline ten bend lines match",BendsVerified(10,10));test("Missing one bend line fails",!BendsVerified(9,10));
        test("Resolved view has mandatory full-loading evidence",FullyLoaded(false,true,false,"Folded"));test("Detailing mode cannot pass",!FullyLoaded(true,true,false,"Folded"));test("Missing view configuration cannot pass",!FullyLoaded(false,true,false,""));
        Console.WriteLine("Portable reopen policy: "+(total-fail)+"/"+total+" passed; COM not tested");return fail==0?0:1;
    }
}
