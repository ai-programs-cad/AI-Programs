using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public static class ManufacturingWorkspaceV13 {
    static object[] Items(object x){return ManufacturingGeometryV01.Items(x);}
    public static bool IsProjectDocumentCandidate(string path){return !Path.GetFileName(path).StartsWith("~$",StringComparison.Ordinal)&&new[]{".SLDPRT",".SLDASM",".SLDDRW",".SLDLFP"}.Contains(Path.GetExtension(path).ToUpperInvariant());}
    public static bool Inside(string root,string path){if(String.IsNullOrWhiteSpace(root)||String.IsNullOrWhiteSpace(path))return false;try{return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    static void NoReparse(string root,string path){string p=Path.GetFullPath(path),limit=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);while(p!=null&&(Inside(limit,p)||String.Equals(p,limit,StringComparison.OrdinalIgnoreCase))){if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Workspace reparse points are not accepted: "+p);p=Path.GetDirectoryName(p);}}
    public static void ValidatePaths(string workspace,string model){
        if(!Directory.Exists(workspace)||!File.Exists(model)||!Inside(workspace,model))throw new InvalidOperationException("--model must be an existing exact file inside --workspace");
        var dir=new DirectoryInfo(workspace);if(dir.Name!="Workspace"||dir.Parent==null||!dir.Parent.Name.StartsWith("Run_",StringComparison.Ordinal)||dir.Parent.Parent==null||dir.Parent.Parent.Name!="_Manufacturing")throw new InvalidOperationException("Expected Project\\_Manufacturing\\Run_...\\Workspace layout");
        if(!String.Equals(Path.GetExtension(model),".SLDPRT",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("First stage processes one multibody SLDPRT only");NoReparse(workspace,model);
    }
    public static List<object> CheckPairs(string owner,object raw,string workspace){
        var a=Items(raw);if(a.Length%2!=0)throw new InvalidOperationException("Unexpected native dependency array for "+owner);var result=new List<object>();
        for(int i=0;i<a.Length;i+=2){string path=Convert.ToString(a[i+1]);if(!Path.IsPathRooted(path))path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(owner),path));
            if(!Inside(workspace,path)||!File.Exists(path))throw new InvalidOperationException("WORKSPACE_REFERENCE_NOT_ISOLATED: owner="+owner+" reference="+path+"; native prepared copy must be fixed before processing");NoReparse(workspace,path);result.Add(new{owner,name=Convert.ToString(a[i]),resolvedPath=path,insideWorkspace=true});}
        return result;
    }
    public static List<object> SavedReferences(SldWorks sw,string workspace){
        var result=new List<object>();var files=new List<string>();var pending=new Queue<string>();pending.Enqueue(workspace);while(pending.Count>0){string dir=pending.Dequeue();NoReparse(workspace,dir);files.AddRange(Directory.GetFiles(dir));foreach(string child in Directory.GetDirectories(dir)){NoReparse(workspace,child);if(!Path.GetFileName(child).StartsWith("_Generated_",StringComparison.Ordinal))pending.Enqueue(child);}}
        // Discovery excludes lock/owner files. CheckPairs still validates every
        // explicit native dependency, including any path beginning with ~$.
        foreach(string file in files.Where(IsProjectDocumentCandidate)){
            if(file.Substring(Path.GetFullPath(workspace).Length).Split(Path.DirectorySeparatorChar).Any(s=>s.StartsWith("_Generated_",StringComparison.Ordinal)))continue;
            NoReparse(workspace,file);var deps=sw.GetDocumentDependencies2(file,false,false,false);result.Add(new{file,reader="ISldWorks.GetDocumentDependencies2, no search redirection",dependencies=CheckPairs(file,deps,workspace),returnedNull=deps==null});}
        return result;
    }
    public static object LiveReferences(ModelDoc2 doc,string workspace){
        if(!Inside(workspace,doc.GetPathName()))throw new InvalidOperationException("Live reference audit requires owned model: "+doc.GetPathName());
        var deps=CheckPairs(doc.GetPathName(),doc.GetDependencies2(false,false,false),workspace);int count=doc.GetType()==(int)swDocumentTypes_e.swDocDRAWING?0:doc.Extension.ListExternalFileReferencesCount();
        object models=null,components=null,features=null,types=null,status=null,entities=null,com=null,options=null,configs=null;
        if(count>0)doc.Extension.ListExternalFileReferences2(out models,out components,out features,out types,out status,out entities,out com,out options,out configs);
        var paths=Items(models);if(count!=paths.Length)throw new InvalidOperationException("External reference count/data mismatch for "+doc.GetPathName());
        var statuses=Items(status);if(statuses.Length!=count)throw new InvalidOperationException("External feature reference statuses incomplete");foreach(var value in statuses){int s=Convert.ToInt32(value);if(s!=(int)swExternalReferenceStatus_e.swExternalReferenceInContext&&s!=(int)swExternalReferenceStatus_e.swExternalReferenceLocked)throw new InvalidOperationException("External feature reference not verifiable in prepared copy: status="+s+" document="+doc.GetPathName());}
        foreach(var value in paths){string p=Convert.ToString(value);if(!Path.IsPathRooted(p))p=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc.GetPathName()),p));if(!Inside(workspace,p)||!File.Exists(p))throw new InvalidOperationException("EXTERNAL_FEATURE_REFERENCE_NOT_ISOLATED: "+doc.GetPathName()+" -> "+p);NoReparse(workspace,p);}
        return new{model=doc.GetPathName(),dependencies=deps,externalFeatureReferenceCount=count,modelPaths=models,componentPaths=components,features,status,configNames=configs,referenceEditsMade=false,breakAllCalled=false};
    }
    public static ModelDoc2 OpenExact(SldWorks sw,string path,out bool opened){
        opened=false;path=Path.GetFullPath(path);var existing=sw.GetOpenDocumentByName(path) as ModelDoc2;if(existing!=null){if(!ManufacturingContextV02.SamePath(existing.GetPathName(),path))throw new InvalidOperationException("Open document lookup returned wrong file");return existing;}
        foreach(ModelDoc2 doc in Items(sw.GetDocuments()).Cast<ModelDoc2>())if(String.Equals(Path.GetFileName(doc.GetPathName()),Path.GetFileName(path),StringComparison.OrdinalIgnoreCase)&&!ManufacturingContextV02.SamePath(doc.GetPathName(),path))throw new InvalidOperationException("OPEN_DOCUMENT_NAME_COLLISION: "+doc.GetPathName()+"; original was not closed. Close the conflicting document manually after preserving your work, then retry.");
        int error=0,warning=0;int type=String.Equals(Path.GetExtension(path),".SLDDRW",StringComparison.OrdinalIgnoreCase)?(int)swDocumentTypes_e.swDocDRAWING:(int)swDocumentTypes_e.swDocPART;
        var model=sw.OpenDoc6(path,type,(int)swOpenDocOptions_e.swOpenDocOptions_Silent|(int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly,"",ref error,ref warning);
        if(model==null||error!=0||!ManufacturingContextV02.SamePath(model.GetPathName(),path))throw new InvalidOperationException("Exact read-only workspace open failed: "+path+" errors="+error+" warnings="+warning);opened=true;return model;
    }
    public static Dictionary<string,object> PublishAndCheck(SldWorks sw,MPFiles files,string drawing,string model,string body,int expectedBends){
        string workspace=Path.Combine(files.Root,"Workspace");if(!Inside(workspace,drawing)||!Inside(workspace,model))throw new InvalidOperationException("Publication inputs must be verified Workspace documents");
        string targetDrawing=files.Reserve("final/"+body+"/"+Path.GetFileName(drawing)),targetModel=files.Reserve("final/"+body+"/"+Path.GetFileName(model));
        files.RequireOwned(drawing);files.RequireOwned(model);File.Move(model,targetModel);files.Created.Remove(model);files.Created.Add(targetModel);File.Move(drawing,targetDrawing);files.Created.Remove(drawing);files.Created.Add(targetDrawing);
        string mh=ManufacturingSourceFilesV03.Hash(targetModel,"PUBLICATION_READ_ONLY_BEFORE"),dh=ManufacturingSourceFilesV03.Hash(targetDrawing,"PUBLICATION_READ_ONLY_BEFORE");
        ModelDoc2 part=null,drw=null;bool openedPart=false,openedDrawing=false;var checks=new List<object>();var openAudit=new Dictionary<string,object>();int actualBends=-1;
        try{part=OpenExact(sw,targetModel,out openedPart);LiveReferences(part,Path.GetDirectoryName(targetModel));
            CheckPairs(targetDrawing,sw.GetDocumentDependencies2(targetDrawing,false,false,false),Path.GetDirectoryName(targetModel));files.RequireOwned(targetDrawing);
            drw=NativeDrawingModeV13.Open(sw,targetDrawing,Path.GetDirectoryName(targetDrawing),openAudit,d=>{drw=d;openedDrawing=true;});LiveReferences(drw,Path.GetDirectoryName(targetDrawing));
            var views=Items(((DrawingDoc)drw).GetViews()).SelectMany(Items).Cast<SolidWorks.Interop.sldworks.View>().Where(v=>!String.IsNullOrEmpty(v.GetReferencedModelName())).ToArray();
            if(views.Length!=2||views.Count(v=>v.IsFlatPatternView())!=1)throw new InvalidOperationException("Final editable drawing must contain native folded and flat views");
            foreach(var v in views){if(v.IsLightweight())v.SetLightweightToResolved();if(!v.IsModelLoaded())v.LoadModel();}
            views=Items(((DrawingDoc)drw).GetViews()).SelectMany(Items).Cast<SolidWorks.Interop.sldworks.View>().Where(v=>!String.IsNullOrEmpty(v.GetReferencedModelName())).ToArray();
            if(views.Length!=2||views.Count(v=>v.IsFlatPatternView())!=1)throw new InvalidOperationException("Published native view set changed after model load");
            int error=0;sw.ActivateDoc3(drw.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref error);
            foreach(var view in views){var linked=ManufacturingSolidWorksV13.LoadedViewModel(view);string cfg=ManufacturingSolidWorksV13.LoadedViewConfiguration(view);if(!view.IsModelLoaded()||view.IsLightweight()||!ManufacturingContextV02.SamePath(view.GetReferencedModelName(),targetModel)||linked==null||!ManufacturingContextV02.SamePath(linked.GetPathName(),targetModel)||String.IsNullOrEmpty(cfg)||linked.GetConfigurationByName(cfg)==null)throw new InvalidOperationException("Published drawing reference/configuration mismatch");
                var outline=Items(view.GetOutline()).Select(Convert.ToDouble).ToArray();if(!ManufacturingDrawingChecksV13.Nonempty(outline))throw new InvalidOperationException("Published native view empty");
                int visible=0;var components=Items(view.GetVisibleComponents()).Cast<Component2>().ToArray();foreach(var c in components){if(!ManufacturingContextV02.SamePath(c.GetPathName(),targetModel))throw new InvalidOperationException("Published view references foreign component");visible+=Items(view.GetVisibleEntities2(c,(int)swViewEntityType_e.swViewEntityType_Edge)).Length;}if(components.Length==0)visible=Items(view.GetVisibleEntities2(null,(int)swViewEntityType_e.swViewEntityType_Edge)).Length;
                if(visible==0)throw new InvalidOperationException("Published native view has no visible geometry");checks.Add(new{name=view.GetName2(),flat=view.IsFlatPatternView(),model=view.GetReferencedModelName(),configuration=cfg,visibleEdges=visible});}
            actualBends=views.Single(v=>v.IsFlatPatternView()).GetBendLineCount();if(actualBends!=expectedBends||expectedBends>0&&actualBends<=0)throw new InvalidOperationException("Published native bend line count differs: "+actualBends+" expected="+expectedBends);
        }catch(Exception ex){ex.Data["nativeDrawingOpen"]=ManufacturingStateV09.Json(openAudit);ex.Data["publishedDrawing"]=targetDrawing;ex.Data["publishedModel"]=targetModel;throw;}
        finally{if(openedDrawing&&drw!=null)ManufacturingPerformanceV13.Measure(body,"CLOSE_DOCUMENT",()=>sw.CloseDoc(drw.GetTitle()));if(openedPart&&part!=null)ManufacturingPerformanceV13.Measure(body,"CLOSE_DOCUMENT",()=>sw.CloseDoc(part.GetTitle()));}
        if(ManufacturingSourceFilesV03.Hash(targetModel,"PUBLICATION_READ_ONLY_AFTER")!=mh||ManufacturingSourceFilesV03.Hash(targetDrawing,"PUBLICATION_READ_ONLY_AFTER")!=dh)throw new InvalidOperationException("Read-only publication audit changed file bytes");
        return new Dictionary<string,object>{{"status","PORTABLE_VERIFIED"},{"drawing",targetDrawing},{"model",targetModel},{"views",checks},{"bendLines",actualBends},{"resolvedDrawingOpen",openAudit},{"relocatedAndReopenedReadOnly",true},{"outputFilesSavedOrRebuiltDuringAudit",false},{"nativeResolveMayUpdateInMemoryViews",true},{"productionCertified",false}};
    }
}
