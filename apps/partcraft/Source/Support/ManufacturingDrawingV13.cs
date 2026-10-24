using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public static class ManufacturingDrawingChecksV13 {
    public static double RotationError(double[] actual,double[] expected){
        if(actual==null||expected==null||actual.Length<9||expected.Length<9||actual.Any(v=>!NV.Finite(v))||expected.Any(v=>!NV.Finite(v)))throw new InvalidOperationException("Missing/nonfinite native view transform");
        double max=0;for(int i=0;i<9;i++)max=Math.Max(max,Math.Abs(actual[i]-expected[i]));return max;
    }
    public static bool Nonempty(double[] outline){return outline!=null&&outline.Length==4&&outline.All(NV.Finite)&&outline[2]>outline[0]&&outline[3]>outline[1];}
    public static bool SameReference(string expected,string actual){return ManufacturingContextV02.SamePath(expected,actual);}
    public static bool PdfComplete(byte[] bytes){if(bytes==null||bytes.Length<16)return false;return System.Text.Encoding.ASCII.GetString(bytes.Take(5).ToArray())=="%PDF-"&&System.Text.Encoding.ASCII.GetString(bytes.Skip(Math.Max(0,bytes.Length-2048)).ToArray()).Contains("%%EOF");}
}
public partial class ManufacturingSolidWorksV13 {
    ModelDoc2 activeDrawing;string activeDrawingPath;
    SolidWorks.Interop.sldworks.View InsertIsometric(DrawingDoc dd,ModelDoc2 drawing,ModelDoc2 copy,Feature flat,string path,string bodyName,string named,double x,double y){
        var attempts=new List<object>();current["viewInsertionAttempts"]=attempts;
        for(int mode=0;mode<2;mode++){
            string api=mode==0?"CreateDrawViewFromModelView3 (native named isometric)":"CreateDrawViewFromModelView2 (documented legacy native fallback)";
            try{
                PrepareFoldedCopy(copy,flat,path,bodyName);copy.ShowNamedView2("",(int)swStandardViews_e.swIsometricView);copy.NameView(named);Save(copy,path);
                Activate(drawing);drawing.ClearSelection2(true);dd.ActivateView("");
                SolidWorks.Interop.sldworks.View view;
                if(mode==0)view=dd.CreateDrawViewFromModelView3(path,named,x,y,0);
                else {
#pragma warning disable 0618
                    view=dd.CreateDrawViewFromModelView2(path,named,x,y,0);
#pragma warning restore 0618
                }
                attempts.Add(new{api,modelPath=path,configurationApiName=config,namedOrientation=named,result=view==null?"NULL_RETURN":"VIEW_CREATED",hresult=(string)null});
                if(view!=null){view.ReferencedConfiguration=config;current["isometricApi"]=api;CaptureViews(dd);return view;}
            }catch(Exception ex){attempts.Add(new{api,modelPath=path,configurationApiName=config,exception=ex.GetType().FullName,hresult="0x"+ex.HResult.ToString("X8"),message=ex.Message,stackTrace=ex.StackTrace});}
            CaptureViews(dd);
            // Never blindly retry if a failed call nevertheless inserted a view.
            if(ManufacturingGeometryV01.Items(dd.GetViews()).SelectMany(s=>ManufacturingGeometryV01.Items(s)).Count()>1)throw new InvalidOperationException("Native view insertion left an unexpected view after failure; no duplicate retry");
        }
        return null;
    }
    void CaptureViews(DrawingDoc dd){
        current["drawingViews"]=ManufacturingGeometryV01.Items(dd.GetViews()).SelectMany(s=>ManufacturingGeometryV01.Items(s)).Cast<SolidWorks.Interop.sldworks.View>().Select(v=>new{name=v.GetName2(),modelPath=v.GetReferencedModelName(),configurationApiName=v.ReferencedConfiguration,isFlatPattern=v.IsFlatPatternView()}).ToArray();
        current["modelViewCount"]=ManufacturingGeometryV01.Items(dd.GetViews()).SelectMany(s=>ManufacturingGeometryV01.Items(s)).Cast<SolidWorks.Interop.sldworks.View>().Count(v=>!String.IsNullOrEmpty(v.GetReferencedModelName()));
    }
    object VerifyView(SolidWorks.Interop.sldworks.View view,string path,string bodyName,string foldedConfig,bool flat,double[] expectedRotation){return ManufacturingPerformanceV13.Measure(bodyName,"VERIFY_VIEW",()=>VerifyViewCore(view,path,bodyName,foldedConfig,flat,expectedRotation));}
    object VerifyViewCore(SolidWorks.Interop.sldworks.View view,string path,string bodyName,string foldedConfig,bool flat,double[] expectedRotation){
        if(!current.ContainsKey("viewVerificationAttempts"))current["viewVerificationAttempts"]=new List<object>();
        ProductionReopenChecksV13.History(current,"viewVerificationAttempts").Add(new{phase="BEGIN",name=view==null?null:view.GetName2(),requestedFlat=flat,modelPath=path,expectedBodyName=bodyName,expectedFoldedConfiguration=foldedConfig});
        if(view==null)throw new InvalidOperationException("Drawing view missing");
        // Persist each getter independently BEFORE any reference assertion.
        var reference=new Dictionary<string,object>{{"phase","REFERENCE_BINDING"},{"expectedPath",path},{"viewName",view.GetName2()}};
        ProductionReopenChecksV13.History(current,"viewVerificationAttempts").Add(reference);
        ModelDoc2 model=null;string declared=null,loaded=null;
        try{declared=view.GetReferencedModelName();reference["GetReferencedModelName"]=declared;}catch(Exception ex){reference["declaredReferenceError"]=ex.ToString();}
        try{model=view.ReferencedDocument;loaded=model==null?null:model.GetPathName();reference["ReferencedDocument.GetPathName"]=loaded;}catch(Exception ex){reference["loadedReferenceError"]=ex.ToString();}
        try{reference["viewConfiguration"]=view.ReferencedConfiguration;}catch(Exception ex){reference["configurationReadError"]=ex.ToString();}
        if(model==null){model=LoadedViewModel(view);loaded=model==null?null:model.GetPathName();reference["alternativeReferenceApi"]="IView.RootDrawingComponent.Component.GetModelDoc2";}
        string viewConfig=LoadedViewConfiguration(view);reference["effectiveLoadedModelPath"]=loaded;reference["effectiveViewConfiguration"]=viewConfig;reference["isModelLoaded"]=view.IsModelLoaded();reference["isLightweight"]=view.IsLightweight();
        reference["openDocuments"]=OpenDocumentPaths();
        reference["declaredPathMatches"]=ManufacturingDrawingChecksV13.SameReference(path,declared);
        reference["loadedPathMatches"]=ManufacturingDrawingChecksV13.SameReference(path,loaded);
        reference["actualBinding"]=model==null?"NO_LOADED_DOCUMENT":(bool)reference["loadedPathMatches"]?"EXPECTED_PORTABLE_DOCUMENT":"OTHER_LOADED_DOCUMENT";
        string binding=ProductionReopenChecksV13.Binding(path,declared,loaded);reference["bindingDecision"]=binding;
        if(binding!="EXPECTED_PORTABLE_DOCUMENT")throw new InvalidOperationException(binding+": loaded="+loaded+" declared="+declared+" expected="+path+"; not a geometry mismatch");
        if(!ProductionReopenChecksV13.FullyLoaded(activeDrawing!=null&&((DrawingDoc)activeDrawing).IsDetailingMode(),view.IsModelLoaded(),view.IsLightweight(),viewConfig))throw new InvalidOperationException("Native view model/configuration not fully loaded");
        if(view.IsFlatPatternView()!=flat||!flat&&viewConfig!=foldedConfig)throw new InvalidOperationException("Native view configuration/flat state differs");
        if(model.GetConfigurationByName(viewConfig)==null)throw new InvalidOperationException("View references absent configuration");
        var proof=VerifyRenderedBody(view,model,bodyName,flat);
        var bodies=new[]{One(model)};string ownership="Exact model/configuration + isolated model geometry/state + visible native view entities";
        var outline=ManufacturingGeometryV01.Items(view.GetOutline()).Select(Convert.ToDouble).ToArray();if(!ManufacturingDrawingChecksV13.Nonempty(outline)||!view.GetVisible())throw new InvalidOperationException("Native model view is empty/hidden");
        double? rotationError=null;
        if(!flat){
            rotationError=ManufacturingDrawingChecksV13.RotationError(ManufacturingGeometryV01.Items(view.ModelToViewTransform.ArrayData).Select(Convert.ToDouble).ToArray(),expectedRotation);
            if(rotationError>1e-8)throw new InvalidOperationException("Native drawing isometric rotation differs from saved standard isometric orientation: "+rotationError);
            // Verify the exact bound flat feature is suppressed in the view's
            // configuration without switching the source or the model view.
            var target=((PartDoc)model).FeatureByName((string)current["flatPatternFeature"]) as Feature;
            if(target==null)throw new InvalidOperationException("Bound FlatPattern feature absent in drawing reference");
            var states=ManufacturingGeometryV01.Items(target.IsSuppressed2((int)swInConfigurationOpts_e.swSpecifyConfiguration,new[]{foldedConfig}));
            if(states.Length!=1||!Convert.ToBoolean(states[0]))throw new InvalidOperationException("Isometric view references an unfolded configuration");
        }
        return new{name=view.GetName2(),modelPath=model.GetPathName(),configurationApiName=viewConfig,bodyName=bodies[0].Name,bodyCount=1,ownership,proof,flat,outline,orientationName=view.GetOrientationName(),rotationError,nonempty=true};
    }
    void SaveIncompleteDrawing(Exception cause){
        if(activeDrawing==null||activeDrawingPath==null)return;
        current["drawingStatus"]="INCOMPLETE";
        try{Activate(activeDrawing);try{CaptureViews((DrawingDoc)activeDrawing);}catch(Exception ex){current["incompleteViewInventoryError"]=ex.Message;}try{activeDrawing.InsertNote("INCOMPLETE — NOT FOR PRODUCTION\n"+cause.Message);}catch(Exception ex){current["incompleteNoteError"]=ex.Message;}activeDrawing.EditRebuild3();Save(activeDrawing,activeDrawingPath);current["drawingPath"]=activeDrawingPath;}
        catch(Exception ex){current["incompleteDrawingSaveError"]=ManufacturingSourceFilesV03.Error(ex,"Save owned incomplete SLDDRW","DRAWING_INCOMPLETE_SAVE",activeDrawingPath);}
    }
    Tuple<string,string> PackageAndReopen(ModelDoc2 drawing,ModelDoc2 copy,string drw,string part,string bodyName,double[] expectedRotation){
        var pack=drawing.Extension.GetPackAndGo();pack.IncludeDrawings=true;pack.IncludeSimulationResults=false;pack.IncludeToolboxComponents=false;
        object docs;if(!pack.GetDocumentNames(out docs))throw new InvalidOperationException("PackAndGo document enumeration failed");
        var names=ManufacturingGeometryV01.Items(docs).Cast<string>().ToArray();
        if(names.Length!=2||!names.Any(n=>ManufacturingContextV02.SamePath(n,part))||!names.Any(n=>ManufacturingContextV02.SamePath(n,drw)))throw new InvalidOperationException("Portable drawing has unexpected/external dependencies; source documents will not be saved");
        string stageFolder="working/pack-stage-"+Guid.NewGuid().ToString("N");
        var targets=names.Select(n=>files.Reserve(stageFolder+"/"+Path.GetFileName(n))).ToArray();
        if(!pack.SetDocumentSaveToNames(targets))throw new InvalidOperationException("PackAndGo remapping failed");
        var codes=ManufacturingGeometryV01.Items(ManufacturingPerformanceV13.Measure(bodyName,"PACK_AND_GO_NATIVE",()=>drawing.Extension.SavePackAndGo(pack))).Select(Convert.ToInt32).ToArray();
        if(codes.Length!=targets.Length||codes.Any(c=>c!=(int)swPackAndGoSaveStatus_e.swPackAndGoSaveStatus_Succeed)||targets.Any(p=>!File.Exists(p)||new FileInfo(p).Length==0))throw new InvalidOperationException("PackAndGo save failed: "+String.Join(",",codes));
        foreach(var p in targets)files.Created.Add(p);
        CloseOwnedForReopen(drw);activeDrawing=null;CloseOwnedForReopen(part);
        var moved=names.Select(n=>files.Reserve("portable/"+bodyName+"/"+Path.GetFileName(n))).ToArray();
        for(int i=0;i<targets.Length;i++){files.RequireOwned(targets[i]);files.RequireOwned(moved[i]);File.Move(targets[i],moved[i]);files.Created.Remove(targets[i]);files.Created.Add(moved[i]);}
        string finalPart=moved[Array.FindIndex(names,n=>ManufacturingContextV02.SamePath(n,part))],finalDrw=moved[Array.FindIndex(names,n=>ManufacturingContextV02.SamePath(n,drw))];
        int error=0,warning=0;var reopened=sw.OpenDoc6(finalPart,(int)swDocumentTypes_e.swDocPART,(int)swOpenDocOptions_e.swOpenDocOptions_Silent,config,ref error,ref warning);
        if(reopened==null||error!=0||!ManufacturingContextV02.SamePath(reopened.GetPathName(),finalPart))throw new InvalidOperationException("Portable model reopen failed: "+error);created.Add(reopened);CopyOnly(reopened);
        if(One(reopened).Name!=bodyName||reopened.ConfigurationManager.ActiveConfiguration.Name!=config)throw new InvalidOperationException("Portable part folded context differs");
        var rd=OpenResolvedPortableDrawing(finalDrw);activeDrawing=rd;activeDrawingPath=null;ResolvePortableViews((DrawingDoc)rd);
        var views=ManufacturingGeometryV01.Items(((DrawingDoc)rd).GetViews()).SelectMany(s=>ManufacturingGeometryV01.Items(s)).Cast<SolidWorks.Interop.sldworks.View>().Where(v=>!String.IsNullOrEmpty(v.GetReferencedModelName())).ToArray();
        if(views.Length!=2||views.Count(v=>v.IsFlatPatternView())!=1)throw new InvalidOperationException("Portable drawing native view set differs");
        var verified=new List<object>();var errors=new List<string>();current["portableViewVerification"]=verified;
        foreach(var view in views)try{verified.Add(VerifyView(view,finalPart,bodyName,config,view.IsFlatPatternView(),view.IsFlatPatternView()?null:expectedRotation));}catch(Exception ex){errors.Add(view.GetName2()+": "+ex.ToString());}
        int actualBends=views.Single(v=>v.IsFlatPatternView()).GetBendLineCount(),expectedBends=Convert.ToInt32(current["nativeDrawingBendLineCount"]);current["reopenedBendLines"]=actualBends;
        if(actualBends!=expectedBends||expectedBends>0&&!ProductionReopenChecksV13.BendsVerified(actualBends,expectedBends))errors.Add("Reopened native bend line count differs: expected="+expectedBends+" actual="+actualBends);
        current["portableVerificationErrors"]=errors;if(errors.Count>0)throw new InvalidOperationException("PORTABLE_VERIFICATION_INCOMPLETE: "+String.Join(" | ",errors));current["portableStatus"]="PORTABLE_VERIFIED";
        current["portablePackage"]=new{drawing=finalDrw,model=finalPart,referenceRemapping="Native PackAndGo",reopenedAndChecked=true,folderRelocationTested=true,keepTogether=true};
        return Tuple.Create(finalDrw,finalPart);
    }
}
