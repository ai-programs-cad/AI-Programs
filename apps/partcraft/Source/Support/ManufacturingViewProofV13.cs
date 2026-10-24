using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public static class ManufacturingViewPolicyV09 {
    public static bool Proven(bool reference,bool config,bool isolated,bool geometry,bool state,int visibleEdges,int filterCount,int returnedBodies){return reference&&config&&isolated&&geometry&&state&&visibleEdges>0&&filterCount>=0&&filterCount<=1&&returnedBodies<=1;}
}
public partial class ManufacturingSolidWorksV13 {
    object VerifyRenderedBody(SolidWorks.Interop.sldworks.View view,ModelDoc2 model,string bodyName,bool flat){
        var diag=new Dictionary<string,object>{{"viewName",view.GetName2()},{"isFlatPattern",flat},{"referencePath",view.GetReferencedModelName()},{"viewConfiguration",LoadedViewConfiguration(view)},{"expectedSourceBodyName",bodyName}};
        ProductionReopenChecksV13.History(current,"viewVerificationAttempts").Add(diag);
        CopyOnly(model);Activate(model);var before=model.ConfigurationManager.ActiveConfiguration.Name;
        var activation=ManufacturingConfigurationV08.Activate(LoadedViewConfiguration(view),ManufacturingGeometryV01.Items(model.GetConfigurationNames()).Cast<string>(),()=>model.ConfigurationManager.ActiveConfiguration.Name,n=>model.ShowConfiguration2(n));diag["referenceConfigurationActivation"]=activation;
        if(!activation.success)throw new InvalidOperationException("VIEW_REFERENCE_CONFIGURATION_UNVERIFIED: "+view.GetName2());
        try{
        if(!model.EditRebuild3())throw new InvalidOperationException("Owned view reference rebuild failed");
        var own=One(model);var actualSignature=Signature(own);diag["ownedBody"]=new{name=own.Name,visible=own.Visible,sheetMetal=own.IsSheetMetal(),faceCount=own.GetFaceCount(),edgeCount=own.GetEdgeCount()};diag["ownedSignature"]=actualSignature;
        var target=((PartDoc)model).FeatureByName((string)current["flatPatternFeature"]) as Feature;
        bool state=target!=null&&Suppressed(target,LoadedViewConfiguration(view))==!flat;
        bool geometry;
        if(flat){object baseline;geometry=current.TryGetValue("nativeFlatSignature",out baseline)&&((FoldedSignatureV08)baseline).SameFlatGeometry(actualSignature);}
        else geometry=own.Name==bodyName&&sourceFoldedSignature!=null&&sourceFoldedSignature.SameGeometryAndBends(actualSignature);
        if(activeDrawing!=null)Activate(activeDrawing);
        int count=view.GetBodiesCount();var returned=ManufacturingGeometryV01.Items(view.Bodies).Cast<Body2>().ToArray();diag["getBodiesCount"]=count;
        diag["returnedBodies"]=returned.Select(b=>{object identity;try{identity=sw.IsSame(b,own);}catch(Exception ex){identity=ex.Message;}return new{name=b.Name,visible=b.Visible,sheetMetal=b.IsSheetMetal(),faceCount=b.GetFaceCount(),edgeCount=b.GetEdgeCount(),identityToOwnedBodyDiagnosticOnly=identity,nativeBodyTypeCode=b.GetType()};}).ToArray();
        int edges=0;var nativeErrors=new List<object>();var components=ManufacturingGeometryV01.Items(view.GetVisibleComponents()).Cast<Component2>().ToArray();
        foreach(var component in components){try{string path=component.GetPathName();if(!ManufacturingContextV02.SamePath(path,model.GetPathName()))throw new InvalidOperationException("Visible component references another model: "+path);edges+=ManufacturingGeometryV01.Items(view.GetVisibleEntities2(component,(int)swViewEntityType_e.swViewEntityType_Edge)).Length;}catch(Exception ex){nativeErrors.Add(ex.ToString());}}
        if(components.Length==0)try{edges=ManufacturingGeometryV01.Items(view.GetVisibleEntities2(null,(int)swViewEntityType_e.swViewEntityType_Edge)).Length;}catch(Exception ex){nativeErrors.Add(ex.ToString());}
        diag["visibleNativeEdgeCount"]=edges;diag["visibleEntityErrors"]=nativeErrors;diag["configurationStateVerified"]=state;diag["geometryEvidenceMatched"]=geometry;
        bool pass=ManufacturingViewPolicyV09.Proven(true,activation.success,own.IsSheetMetal(),geometry,state,edges,count,returned.Length)&&nativeErrors.Count==0;diag["ownershipVerified"]=pass;
        if(!pass)throw new InvalidOperationException("NATIVE_VIEW_OWNERSHIP_INCOMPLETE: "+view.GetName2()+" flat="+flat+" filterCount="+count+" returned="+returned.Length+" visibleEdges="+edges+" geometry="+geometry+" state="+state);
        // Keep the owned reference folded for subsequent save/reopen workflows.
        return diag;
        }finally{try{if(model.ConfigurationManager.ActiveConfiguration.Name!=before){Activate(model);var restore=ManufacturingConfigurationV08.Activate(before,ManufacturingGeometryV01.Items(model.GetConfigurationNames()).Cast<string>(),()=>model.ConfigurationManager.ActiveConfiguration.Name,n=>model.ShowConfiguration2(n));diag["ownedConfigurationRestoration"]=restore;if(!restore.success)throw new InvalidOperationException("OWNED_VIEW_CONFIGURATION_RESTORE_FAILED");}}finally{if(activeDrawing!=null)Activate(activeDrawing);}}
    }
}
