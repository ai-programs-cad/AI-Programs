using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public partial class ManufacturingSolidWorksV13 {
    SldWorks sw;ModelDoc2 original;MPFiles files;string config;object originalState;List<ModelDoc2> created=new List<ModelDoc2>();
    readonly string expectedSourceHash;readonly double geometryToleranceMm;readonly string camGeometry;
    public ManufacturingSolidWorksV13(SldWorks s,ModelDoc2 source,MPFiles output,string sourceHash,double toleranceMm,string geometryMode){expectedSourceHash=sourceHash;geometryToleranceMm=toleranceMm;camGeometry=geometryMode;sw=s;original=source;files=output;config=source.ConfigurationManager.ActiveConfiguration.Name;originalState=ManufacturingInventoryV01.Snapshot(source);sourceBoundaryBaseline=SourceBoundaryStateV13.Read(source);if(sourceBoundaryBaseline.dirty)throw new InvalidOperationException("DIRTY_SOURCE_SNAPSHOT_FORBIDDEN");savedSnapshotPath=files.Reserve("working/SourceSnapshot.SLDPRT");ManufacturingSourceFilesV03.CopySavedSource(source.GetPathName(),savedSnapshotPath,expectedSourceHash,false);File.SetAttributes(savedSnapshotPath,FileAttributes.ReadOnly);files.Created.Add(savedSnapshotPath);}
    void CopyOnly(ModelDoc2 doc){if(doc==null||sw.IsSame(doc,original)!=(int)swObjectEquality.swObjectNotSame||!ManufacturingWorkspaceV13.Inside(Path.Combine(files.Root,"Workspace"),doc.GetPathName()))throw new InvalidOperationException("Operation is not fenced to a package-owned part copy");}
    public void Restore(){ManufacturingPerformanceV13.Measure(current==null?"PACKAGE":(string)current["bodyName"],"CLOSE_AND_RESTORE",()=>RestoreCore());}
    void RestoreCore(){
        var closeErrors=new List<string>();
        foreach(var d in created.AsEnumerable().Reverse().ToArray())try{
            string ownedPath=d.GetPathName();string ownedTitle=d.GetTitle();
            if(!String.IsNullOrEmpty(ownedPath)&&!ownedPath.StartsWith(files.Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Cleanup refused non-owned document: "+ownedPath);
            int identity=sw.IsSame(d,original);
            if(identity==(int)swObjectEquality.swObjectNotSame)ObserveSourceCleanup("Close owned document: "+ownedPath,()=>ManufacturingPerformanceV13.Measure((string)current["bodyName"],"CLOSE_DOCUMENT",()=>sw.CloseDoc(ownedTitle)));
            else if(identity!=(int)swObjectEquality.swObjectSame)throw new InvalidOperationException("Cannot prove owned document differs from source; not closed");
            if(!String.IsNullOrEmpty(ownedPath)&&sw.GetOpenDocumentByName(ownedPath)!=null)throw new InvalidOperationException("Owned document remains open after CloseDoc: "+ownedPath);
            created.Remove(d);
        }catch(Exception ex){closeErrors.Add(ex.Message);}
        if(created.Count==0){activeDrawing=null;activeDrawingPath=null;}
        ObserveSourceCleanup("Read source state before restoration",()=>current["sourceStateBeforeRestoration"]=ManufacturingStateV09.Capture(sw,original));
        int error=0;var active=sw.ActiveDoc as ModelDoc2;
        bool needsActivation=active==null||sw.IsSame(active,original)!=(int)swObjectEquality.swObjectSame;
        if(needsActivation)ObserveSourceCleanup("Restore active Workspace without rebuild",()=>active=(ModelDoc2)sw.ActivateDoc3(original.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref error));
        current["restorationActivatedSource"]=needsActivation;ObserveSourceCleanup("Read source state after restoration",()=>current["sourceStateAfterRestoration"]=ManufacturingStateV09.Capture(sw,original));
        ProductionReopenChecksV13.History(current,"restorationHistory").Add(new{before=current["sourceStateBeforeRestoration"],after=current["sourceStateAfterRestoration"],activationNeeded=needsActivation,closeErrors});
        if(active==null||sw.IsSame(active,original)!=(int)swObjectEquality.swObjectSame)throw new InvalidOperationException("SOURCE_ACTIVATION_RESTORE_FAILED, code="+error);
        if(!sourceBoundaryBaseline.Same(SourceBoundaryStateV13.Read(original))||original.GetSaveFlag())throw new InvalidOperationException("SOURCE_STATE_CHANGED_DURING_BODY_PROCESSING_OR_RESTORE");
        if(closeErrors.Count>0)throw new InvalidOperationException("Source reactivated but owned document cleanup failed: "+String.Join(" | ",closeErrors));
    }
    static Body2 One(ModelDoc2 doc){var all=ManufacturingGeometryV01.Items(((PartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody,false)).Cast<Body2>().ToArray();if(all.Length!=1)throw new InvalidOperationException("Isolated part must have exactly one solid body");return all[0];}
    public readonly List<Dictionary<string,object>> Diagnostics=new List<Dictionary<string,object>>();
    Dictionary<string,object> current;
    void Stage(string value){current["stage"]=value;ManufacturingPerformanceV13.Stage((string)current["bodyName"],value);}
    bool ExportNative(ModelDoc2 copy,Feature flat,string path,string modelPath,double[] alignment,int options,bool splinesAsSplines=true){return ManufacturingPerformanceV13.Measure((string)current["bodyName"],options==1?(splinesAsSplines?"EXACT_NATIVE":"CAM_NATIVE"):"BEND_NATIVE_OPTIONS_"+options,()=>ExportNativeCore(copy,flat,path,modelPath,alignment,options,splinesAsSplines));}
    bool ExportNativeCore(ModelDoc2 copy,Feature flat,string path,string modelPath,double[] alignment,int options,bool splinesAsSplines=true){
        CopyOnly(copy);Activate(copy);var beforeBody=One(copy);string beforeName=beforeBody.Name,beforeConfig=copy.ConfigurationManager.ActiveConfiguration.Name;
        var bound=SourceCall("Recheck native export FlatPattern binding",()=>FlatPattern(copy,beforeBody));if(sw.IsSame(bound,flat)!=(int)swObjectEquality.swObjectSame)throw new InvalidOperationException("EXPORT_FLAT_PATTERN_BINDING_MISMATCH");
        SourceCall("Clear native export selection",()=>copy.ClearSelection2(true));if(!SourceCall("Select native FlatPattern for export",()=>flat.Select2(false,-1)))throw new InvalidOperationException("Cannot select isolated native flat pattern");
        bool exported;
        using(var mapping=new ProductionMappingScope(sw,Path.GetDirectoryName(path),(List<object>)current["exportPreferenceAudit"]))
        using(var preferences=new ManufacturingDxfPreferencesV05(k=>sw.GetUserPreferenceToggle(k),(k,v)=>ObserveSourceCleanup("Set/restore native DXF preference "+k+"="+v,()=>sw.SetUserPreferenceToggle(k,v)),splinesAsSplines,(List<object>)current["exportPreferenceAudit"])){
            var call=new Dictionary<string,object>{{"operation","ExportToDWG2"},{"destination",path},{"modelPath",modelPath},{"action",(int)swExportToDWG_e.swExportToDWG_ExportSheetMetal},{"singleFile",true},{"alignment",alignment},{"flipX",false},{"flipY",false},{"options",options},{"splinesAsSplines",splinesAsSplines},{"activePath",((ModelDoc2)sw.ActiveDoc).GetPathName()},{"configuration",beforeConfig},{"flatPattern",flat.Name},{"flatSuppressed",flat.IsSuppressed()},{"selectionCount",((SelectionMgr)copy.SelectionManager).GetSelectedObjectCount2(-1)},{"destinationExisted",File.Exists(path)}};
            if(!current.ContainsKey("nativeExportCalls"))current["nativeExportCalls"]=new List<object>();((List<object>)current["nativeExportCalls"]).Add(call);
            var selected=(SelectionMgr)copy.SelectionManager;call["selectionTypes"]=Enumerable.Range(1,selected.GetSelectedObjectCount2(-1)).Select(i=>selected.GetSelectedObjectType3(i,-1)).ToArray();
            call["openDocuments"]=ManufacturingGeometryV01.Items(sw.GetDocuments()).Cast<ModelDoc2>().Select(d=>new{path=d.GetPathName(),title=d.GetTitle(),documentType=d.GetType()}).ToArray();
            try{using(var probe=new FileStream(path+".write-probe",FileMode.CreateNew,FileAccess.Write,FileShare.None)){}File.Delete(path+".write-probe");call["directoryWritable"]=true;
                exported=SourceCall("ExportToDWG2 options="+options,()=>((PartDoc)copy).ExportToDWG2(path,modelPath,(int)swExportToDWG_e.swExportToDWG_ExportSheetMetal,true,alignment,false,false,options,null));call["returned"]=exported;call["fileExistsAfter"]=File.Exists(path);call["bytesAfter"]=File.Exists(path)?new FileInfo(path).Length:0;
            }catch(Exception ex){call["error"]=ManufacturingSourceFilesV03.Error(ex,"ExportToDWG2","NATIVE_EXPORT",path);throw;}
        }
        if(One(copy).Name!=beforeName||copy.ConfigurationManager.ActiveConfiguration.Name!=beforeConfig)throw new InvalidOperationException("OWNED_EXPORT_CONTEXT_CHANGED");
        current["lastExportBinding"]=new BendBindingV06{bodyName=beforeName,configurationApiName=beforeConfig,flatPatternFeature=flat.Name,modelPath=modelPath,isolatedSolidCount=1,options=options,flatPatternBodyVerified=true,contextUnchanged=true};
        return exported;
    }
    public object Sheet(string bodyName,string template,string material){
        current=new Dictionary<string,object>{{"bodyName",bodyName},{"type","SHEET METAL"},{"method","IPartDoc.ExportToDWG2 / native selected FlatPattern"},{"status","RUNNING"},{"dxfPath",null},{"validationStatus","PENDING"},{"warnings",new List<string>()},{"exportPreferenceAudit",new List<object>()}};Diagnostics.Add(current);
        Stage("SOURCE_ENTRY");activeDrawing=null;activeDrawingPath=null;selectedFoldedConfiguration=null;sourceFoldedSignature=null;current["savedSourceSnapshot"]=new{path=savedSnapshotPath,sha256=expectedSourceHash,readOnlyFile=true,neverOpenedForModification=true};config=SourceCall("Read source active configuration",()=>original.ConfigurationManager.ActiveConfiguration.Name);current["sourceConfigurationApiName"]=config;current["sourceConfigurationEncoding"]=ManufacturingConfigurationV08.Evidence(config);current["configurationOperations"]=new List<object>();
        try {var result=SheetCore(bodyName,template,material);current["status"]=AB.Get(result,"bendDxf")==null?"INCOMPLETE_MISSING_BEND_DXF":"FILES_CREATED_NEEDS_REVIEW";current["fileCreationStatus"]=AB.Get(result,"bendDxf")==null?"PARTIAL":"CREATED";current["productionCertificationStatus"]="NEEDS_REVIEW";current["result"]=result;return result;}
        catch(Exception ex){ManufacturingPerformanceV13.EndStage(false);SaveIncompleteDrawing(ex);current["status"]="FAILED";current["error"]=new{bodyName,stage=current["stage"],exception=ex.GetType().FullName,message=ex.Message,hresult="0x"+ex.HResult.ToString("X8"),stackTrace=ex.StackTrace};ex.Data["bodyName"]=bodyName;ex.Data["operationStage"]=current["stage"];throw;}
    }
    class FlatBoundary {public int contours,fullCircles,curvedEdges;}
    FlatBoundary Boundary(Body2 body,NV normal,NV root){
        var edges=new List<Edge>();var uses=new List<int>();
        foreach(var face in ManufacturingGeometryV01.Items(body.GetFaces()).Cast<Face2>()){
            var s=(Surface)face.GetSurface();if(!s.IsPlane())continue;
            var p=ManufacturingGeometryV01.Items(s.PlaneParams).Select(Convert.ToDouble).ToArray();var n=new NV(p[0],p[1],p[2]).Unit();var o=new NV(p[3],p[4],p[5]);
            if(Math.Abs(NV.Dot(n,normal))<1-1e-10||Math.Abs(NV.Dot(normal,o-root))>1e-7)continue;
            foreach(var edge in ManufacturingGeometryV01.Items(face.GetEdges()).Cast<Edge>()){
                int index=edges.FindIndex(e=>sw.IsSame(e,edge)==(int)swObjectEquality.swObjectSame);
                if(index<0){edges.Add(edge);uses.Add(1);}else uses[index]++;
            }
        }
        var result=new FlatBoundary();var points=new List<NV>();var degree=new List<int>();var links=new List<int[]>();
        for(int i=0;i<edges.Count;i++){
            if(uses[i]==2)continue;if(uses[i]!=1)throw new InvalidOperationException("Nonmanifold coplanar native flat boundary");
            var curve=(Curve)edges[i].GetCurve();var va=edges[i].GetStartVertex() as Vertex;var vb=edges[i].GetEndVertex() as Vertex;
            bool curved=curve.IsCircle()||curve.IsEllipse();if(curved)result.curvedEdges++;
            // Topological closure only. No reconstruction/evaluation of native
            // spline/intersection curves is needed to count cap boundary loops.
            bool full=va==null&&vb==null||va!=null&&vb!=null&&sw.IsSame(va,vb)==(int)swObjectEquality.swObjectSame;
            if(full){result.contours++;if(curve.IsCircle())result.fullCircles++;continue;}
            if(va==null||vb==null)throw new InvalidOperationException("Native boundary vertex topology is incomplete");
            var a=NV.Read(va.GetPoint());var b=NV.Read(vb.GetPoint());
            if((a-b).Norm()<1e-10){
                throw new InvalidOperationException("Native boundary endpoints nearly coincide without topological closure evidence");
            }
            int[] pair=new int[2];int k=0;foreach(var q in new[]{a,b}){
                var matches=points.Select((v,j)=>new{v,j}).Where(v=>(v.v-q).Norm()<1e-10).ToArray();
                if(matches.Length>1)throw new InvalidOperationException("Ambiguous native contour endpoints");
                int id=matches.Length==1?matches[0].j:points.Count;if(id==points.Count){points.Add(q);degree.Add(0);}degree[id]++;pair[k++]=id;
            }links.Add(pair);
        }
        if(result.contours==0&&links.Count==0||degree.Any(d=>d!=2))throw new InvalidOperationException("Native flattened cap outline is empty/open/branching");
        var visited=new HashSet<int>();for(int i=0;i<points.Count;i++){if(visited.Contains(i))continue;result.contours++;var stack=new Stack<int>();stack.Push(i);
            while(stack.Count>0){int n=stack.Pop();if(!visited.Add(n))continue;foreach(var link in links.Where(l=>l.Contains(n)))foreach(int m in link)stack.Push(m);}}
        return result;
    }
    static List<object> BendParameters(Body2 body){
        var rows=new List<object>();
        var features=ManufacturingGeometryV01.Items(body.GetFeatures()).Cast<Feature>().ToArray();
        var defaults=features.Where(f=>f.GetTypeName2()=="SheetMetal").Select(f=>f.GetDefinition() as SheetMetalFeatureData).Where(d=>d!=null).Select(d=>d.BendRadius).Distinct().ToArray();
        foreach(var f in features){
            string type=f.GetTypeName2();if(!new[]{"OneBend","UiBend","SketchedBend","EdgeFlange"}.Contains(type))continue;
            try{
                var data=f.GetDefinition();double angle,radius;bool useDefault;object direction=null;
                if(data is OneBendFeatureData){var d=(OneBendFeatureData)data;angle=d.BendAngle;radius=d.BendRadius;useDefault=d.UseDefaultBendRadius;int code=d.BendDirection;direction=new{code,name=Enum.GetName(typeof(swBendDirection_e),code)};}
                else if(data is SketchedBendFeatureData){var d=(SketchedBendFeatureData)data;angle=d.BendAngle;radius=d.BendRadius;useDefault=d.UseDefaultBendRadius;direction=new{reverseDirection=d.ReverseDirection};}
                else if(data is EdgeFlangeFeatureData){var d=(EdgeFlangeFeatureData)data;angle=d.BendAngle;radius=d.BendRadius;useDefault=d.UseDefaultBendRadius;}
                else {rows.Add(new{feature=f.Name,type,status="NOT_VERIFIED",reason="Native bend definition type unsupported"});continue;}
                double? effective=useDefault?(defaults.Length==1?(double?)defaults[0]:null):(double?)radius;
                rows.Add(new{feature=f.Name,type,angleRadians=angle,angleDegrees=angle*180/Math.PI,rawRadiusM=radius,useDefaultRadius=useDefault,effectiveRadiusM=effective,effectiveRadiusMm=effective*1000,direction,
                    mappingToDrawingLineIndexVerified=false,source="Isolated body's native feature history; not a synthetic bend map"});
            }catch(Exception ex){rows.Add(new{feature=f.Name,type,status="NOT_VERIFIED",reason=ex.Message});}
        }
        return rows;
    }
    void Save(ModelDoc2 doc,string path){
        files.BeforeSave(path);Activate(doc);int error=0,warning=0;
        if(!doc.Extension.SaveAs(path,(int)swSaveAsVersion_e.swSaveAsCurrentVersion,(int)swSaveAsOptions_e.swSaveAsOptions_Silent,null,ref error,ref warning)||error!=0)throw new InvalidOperationException("SaveAs new output failed: errors="+error+", warnings="+warning);
        if(!File.Exists(path)||new FileInfo(path).Length==0)throw new InvalidOperationException("Empty saved document: "+path);
        files.Created.Add(path);
    }
    void Activate(ModelDoc2 doc){
        if(!created.Any(d=>sw.IsSame(d,doc)==(int)swObjectEquality.swObjectSame))throw new InvalidOperationException("Document was not created by this package run");
        int error=0;var active=SourceCall("Activate owned document: "+doc.GetPathName(),()=>sw.ActivateDoc3(doc.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref error));
        if(active==null||sw.IsSame(active,doc)!=(int)swObjectEquality.swObjectSame)throw new InvalidOperationException("Cannot activate owned document, code="+error);
    }
    Feature FlatPattern(ModelDoc2 doc,Body2 body){
        var roots=Chain((Feature)doc.FirstFeature(),false);
        var features=CutListCoreV01.Traverse(roots,f=>Chain((Feature)f.GetFirstSubFeature(),true),(a,b)=>{
            int code=sw.IsSame(a,b);if(code==(int)swObjectEquality.swObjectSame)return true;if(code==(int)swObjectEquality.swObjectNotSame)return false;throw new InvalidOperationException("Feature COM identity unsupported");});
        var matches=new List<Feature>();
        foreach(var f in features.Where(f=>f.GetTypeName2()=="FlatPattern")){
            var data=f.GetDefinition() as FlatPatternFeatureData;if(data==null)continue;bool access=false;
            try{
                // Proven Export_DXF sequence reads FixedFace2 directly. Request
                // selection access only if the installed API requires it.
                var fixedFace=data.FixedFace2 as Face2;
                if(fixedFace==null){access=data.AccessSelections(doc,null);if(!access)continue;fixedFace=data.FixedFace2 as Face2;}
                if(fixedFace!=null&&sw.IsSame(fixedFace.GetBody(),body)==(int)swObjectEquality.swObjectSame)matches.Add(f);
            }finally{if(access)data.ReleaseSelectionAccess();}
        }
        if(matches.Count!=1)throw new InvalidOperationException("Exact FlatPattern binding count="+matches.Count+" for isolated body "+body.Name);
        return matches[0];
    }
    static List<Feature> Chain(Feature f,bool sub){var list=new List<Feature>();while(f!=null){if(list.Count>10000)throw new InvalidOperationException("Feature traversal limit");list.Add(f);f=(Feature)(sub?f.GetNextSubFeature():f.GetNextFeature());}return list;}
    object SheetCore(string bodyName,string template,string material){
        Stage("SOURCE_GUARD_AND_COPY");
        if(original.GetSaveFlag()||sw.IsSame(original,sw.ActiveDoc)!=(int)swObjectEquality.swObjectSame)throw new InvalidOperationException("Source state changed before copying; stopped");
        string copyPath=files.Reserve("models/"+bodyName+".SLDPRT");SourceCall("Create independent per-body file from immutable saved snapshot",()=>ManufacturingSourceFilesV03.CopySavedSource(savedSnapshotPath,copyPath,expectedSourceHash,false));files.Created.Add(copyPath);File.SetAttributes(copyPath,FileAttributes.Normal);
        int error=0,warning=0;var copy=SourceCall("OpenDoc6 independent body copy",()=>{var opened=sw.OpenDoc6(copyPath,(int)swDocumentTypes_e.swDocPART,(int)swOpenDocOptions_e.swOpenDocOptions_Silent,config,ref error,ref warning);if(opened!=null)created.Add(opened);return opened;});
        if(copy==null||error!=0)throw new InvalidOperationException("Open safe part copy failed: "+error);CopyOnly(copy);Activate(copy);
        current["copyConfigurationsAtOpen"]=SourceCall("Read copy configuration table",()=>ConfigurationTable(copy,null,null));
        if(copy.ConfigurationManager.ActiveConfiguration.Name!=config)throw new InvalidOperationException("Owned copy configuration differs from source context");
        if(!SourceCall("Set owned copy units",()=>copy.Extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitSystem,(int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,(int)swUnitSystem_e.swUnitSystem_MMGS)))throw new InvalidOperationException("Cannot set owned copy document units to MMGS");
        var all=SourceCall("Read copy GetBodies2",()=>ManufacturingGeometryV01.Items(((PartDoc)copy).GetBodies2((int)swBodyType_e.swSolidBody,false)).Cast<Body2>().ToArray());
        var targets=all.Where(b=>b.Name==bodyName).ToArray();if(targets.Length!=1)throw new InvalidOperationException("Exact body name resolution failed in copy");
        sourceFoldedSignature=SourceCall("Read folded signature in independent unmodified copy",()=>Signature(targets[0]));current["sourceFoldedSignature"]=sourceFoldedSignature;current["sourceSignatureReadState"]=new{source="Independent per-body copy from SHA256-verified saved snapshot, before isolation",originalRead=false,snapshotPath=savedSnapshotPath};
        SourceCall("Show requested body in copy",()=>targets[0].HideBody(false)); // owned copy only; original visibility is untouched
        SourceCall("Clear copy selection before isolation",()=>copy.ClearSelection2(true));if(!SourceCall("Select copy body for isolation",()=>targets[0].Select2(false,null)))throw new InvalidOperationException("Cannot select copy body");
        var keep=SourceCall("InsertDeleteBody2 keep body in copy",()=>copy.FeatureManager.InsertDeleteBody2(true));
        if(keep==null||!SourceCall("Unsuppress keep body in copy configurations",()=>keep.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,(int)swInConfigurationOpts_e.swAllConfiguration,null)))throw new InvalidOperationException("Keep-body isolation failed on safe copy configurations");
        if(!SourceCall("Rebuild isolated copy",()=>copy.EditRebuild3()))throw new InvalidOperationException("Safe copy rebuild failed");
        Stage("ISOLATED_BODY_NATIVE_FLAT_PATTERN");
        current["isolatedCopyReferences"]=ManufacturingWorkspaceV13.LiveReferences(copy,Path.Combine(files.Root,"Workspace"));
        var body=One(copy);if(body.Name!=bodyName||!body.IsSheetMetal())throw new InvalidOperationException("Isolated body is not the requested sheet-metal body");
        var bendParameters=BendParameters(body);
        var thicknesses=ManufacturingGeometryV01.Items(body.GetFeatures()).Cast<Feature>().Where(f=>f.GetTypeName2()=="SheetMetal").Select(f=>f.GetDefinition() as SheetMetalFeatureData).Where(d=>d!=null).Select(d=>d.Thickness).Distinct().ToArray();
        if(thicknesses.Length!=1||!(thicknesses[0]>0))throw new InvalidOperationException("Unique native SheetMetal thickness not established for isolated body");
        var flat=SourceCall("Bind isolated FlatPattern",()=>FlatPattern(copy,body));
        if(!SourceCall("Unsuppress isolated FlatPattern",()=>flat.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,(int)swInConfigurationOpts_e.swThisConfiguration,null))||!SourceCall("Rebuild flattened copy",()=>copy.EditRebuild3()))throw new InvalidOperationException("Cannot obtain native flat pattern in safe copy");
        body=One(copy);
        Stage("NATIVE_FLAT_TOPOLOGY_AND_SCALE_REFERENCE");
        var nativeCurves=ManufacturingGeometryV01.Items(body.GetEdges()).Cast<Edge>().Select(e=>(Curve)e.GetCurve()).ToArray();
        var unsupportedCurves=nativeCurves.Where(c=>!c.IsLine()&&!c.IsCircle()&&!c.IsEllipse()).Select(c=>c.Identity()).ToArray();
        current["nonElementaryNativeCurveIdentities"]=unsupportedCurves;
        ((List<string>)current["warnings"]).Add("Native curve identities are diagnostic only. SolidWorks exports the flat pattern; no BREP curve reconstruction or fitting is performed.");
        var nativeCurveKinds=nativeCurves.GroupBy(c=>c.Identity()).Select(g=>new{identity=g.Key,count=g.Count()}).ToArray();
        var planes=ManufacturingGeometryV01.Items(body.GetFaces()).Cast<Face2>().Where(f=>((Surface)f.GetSurface()).IsPlane()).OrderByDescending(f=>f.GetArea()).ToArray();
        if(planes.Length==0)throw new InvalidOperationException("Flattened BREP has no planar sheet face");
        var parameters=ManufacturingGeometryV01.Items(((Surface)planes[0].GetSurface()).PlaneParams).Select(Convert.ToDouble).ToArray();var normal=new NV(parameters[0],parameters[1],parameters[2]).Unit();
        var boundary=Boundary(body,normal,new NV(parameters[3],parameters[4],parameters[5]));
        var helper=Math.Abs(normal.X)<.8?new NV(1,0,0):new NV(0,1,0);var x=NV.Cross(normal,helper).Unit();var y=NV.Cross(normal,x).Unit();
        double width=ManufacturingGeometryV01.Span(body,x)*1000,height=ManufacturingGeometryV01.Span(body,y)*1000,thickness=ManufacturingGeometryV01.Span(body,normal)*1000;
        if(!(width>0&&height>0&&thickness>0))throw new InvalidOperationException("Invalid flat BREP extents");
        if(Math.Abs(thickness-thicknesses[0]*1000)>.001)throw new InvalidOperationException("Flattened body slab thickness differs from native sheet thickness");
        current["nativeFlatSignature"]=Signature(body);
        var alignment=new[]{0d,0,0,x.X,x.Y,x.Z,y.X,y.Y,y.Z,normal.X,normal.Y,normal.Z};
        Stage("EXPORT_NATIVE_CUT_GEOMETRY_OPTIONS_1");
        current["isolatedSolidCount"]=ManufacturingGeometryV01.Items(((PartDoc)copy).GetBodies2((int)swBodyType_e.swSolidBody,false)).Length;
        current["flatPatternFeature"]=flat.Name;current["configurationApiName"]=copy.ConfigurationManager.ActiveConfiguration.Name;current["cutSheetMetalOptions"]=1;
        string exactPath=files.Reserve("exact/"+bodyName+"-EXACT.dxf");
        if(!ExportNative(copy,flat,exactPath,copyPath,alignment,1,true))throw new InvalidOperationException("Native exact ExportToDWG2 failed");
        if(!File.Exists(exactPath)||new FileInfo(exactPath).Length==0)throw new InvalidOperationException("Native exact DXF missing/empty");
        files.Created.Add(exactPath);current["exactDxfPath"]=exactPath;
        string exactText=File.ReadAllText(exactPath);
        var exactInspection=ManufacturingNativeDxfV05.Inspect(exactText);current["exactInspection"]=exactInspection;
        if(!exactInspection.structureAccepted)throw new InvalidOperationException("Native exact DXF structural check failed: "+exactInspection.reason);
        var exactCheck=ManufacturingDxfV04.Check(exactText,new[]{width,height},geometryToleranceMm);
        current["exactAnalyticValidation"]=exactCheck.accepted?"PASSED_SUPPORTED_ANALYTIC_SUBSET":"NEEDS_REVIEW: "+exactCheck.reason;
        Stage("EXPORT_NATIVE_POLYLINE_CAM_SPLINE_TOGGLE_FALSE");
        string camRaw=files.Reserve("working/"+bodyName+"-CAM-native.dxf");
        string camText=null;var camCheck=new MPDxfResult{accepted=false,reason="CAM native export did not produce data"};
        try {
            if(ExportNative(copy,flat,camRaw,copyPath,alignment,1,false)&&File.Exists(camRaw)&&new FileInfo(camRaw).Length>0){files.Created.Add(camRaw);camText=File.ReadAllText(camRaw);camCheck=ManufacturingDxfV04.Check(camText,new[]{width,height},geometryToleranceMm);}
        }catch(Exception ex){if(ex.Message.Contains("SOURCE_STATE_CHANGED"))throw;current["camExportError"]=ManufacturingSourceFilesV03.Error(ex,"ExportToDWG2 splinesAsSplines=false","CAM_NATIVE_EXPORT",camRaw);if(ex.Message.Contains("DXF_EXPORT_PREFERENCE_RESTORE_FAILED"))throw;}
        current["camNativeRawPath"]=File.Exists(camRaw)?camRaw:null;
        var camInspection=ManufacturingNativeDxfV05.Inspect(camText??"");current["camInspection"]=camInspection;
        current["nativeToggleEffect"]=ManufacturingNativeDxfV05.ToggleEffect(exactInspection,camInspection);
        if(camCheck.accepted&&(camCheck.closedCurves+camCheck.closedConnectedContours!=boundary.contours)) {camCheck.accepted=false;camCheck.reason="Native cap/DXF contour count mismatch: missing or additional cut/hole";}
        if(camCheck.accepted&&boundary.fullCircles>camCheck.entityTypes.Count(t=>t=="CIRCLE")){camCheck.accepted=false;camCheck.reason="Native circular holes were not preserved as circles";}
        var approximation=ManufacturingNativeDxfV05.Deviation(exactText,camText,geometryToleranceMm);current["approximation"]=approximation;
        string camDxf=null;
        if(camCheck.accepted)camDxf=files.Write("cam/"+bodyName+"-CAM.dxf",camCheck.normalizedText);
        current["camDxfPath"]=camDxf;current["camValidationReason"]=camCheck.reason;
        current["camStatus"]=camCheck.accepted&&approximation.certifiedUpperBoundMm.HasValue&&approximation.certifiedUpperBoundMm.Value<=geometryToleranceMm?"PASS":"NEEDS_REVIEW";
        ((List<string>)current["warnings"]).Add("CAM approval requires a certified whole-contour approximation bound; finite samples are not a maximum. Native conversion is being tested, not assumed.");
        string dxf=null;
        if(camGeometry=="exact")dxf=exactPath;
        else if(camDxf!=null)dxf=files.Write("dxf/"+bodyName+".dxf",camCheck.normalizedText);
        current["dxfPath"]=dxf;current["selectedGeometryMode"]=camGeometry;
        current["validationStatus"]=(string)current["camStatus"]=="PASS"?"SUPPORTED_IDENTICAL_RECORDS_VERIFIED":"NEEDS_REVIEW";
        current["nativeTopologyContours"]=boundary.contours;current["dxfContours"]=camCheck.closedCurves+camCheck.closedConnectedContours;
        var check=camCheck.accepted?camCheck:exactCheck;
        string bendDxf=null;
        Stage("EXPORT_NATIVE_BEND_LINES_OPTIONS_4");
        string rawBends=files.Reserve("working/"+bodyName+"-bends-raw.dxf");
        copy.ClearSelection2(true);if(!flat.Select2(false,-1))throw new InvalidOperationException("Cannot reselect native flat pattern for separate bend export");
        Stage("EXPORT_AND_VALIDATE_SEPARATE_NATIVE_BEND_DXF");
        try{
            bool bendExport=ExportNative(copy,flat,rawBends,copyPath,alignment,4,true);
            if(!bendExport||!File.Exists(rawBends)||new FileInfo(rawBends).Length==0||!ManufacturingBendDxfV06.Validate(File.ReadAllText(rawBends),exactText,(BendBindingV06)current["lastExportBinding"]).isBendOnlyProven){
                current["bendRetryReason"]="Native options=4 did not create independently separable bend data; retry proven Export_DXF geometry|bend-lines (1|4), same selected FlatPattern, configuration and alignment";
                rawBends=files.Reserve("working/"+bodyName+"-cut-and-bends-native.dxf");
                bendExport=ExportNative(copy,flat,rawBends,copyPath,alignment,5,true);
            }
            if(bendExport&&File.Exists(rawBends)&&new FileInfo(rawBends).Length>0){
                files.Created.Add(rawBends);current["nativeBendRawPath"]=rawBends;
                var bendCheck=ManufacturingBendDxfV06.Validate(File.ReadAllText(rawBends),exactText,(BendBindingV06)current["lastExportBinding"]);
                current["bendValidation"]=bendCheck;
                if(bendCheck.structureAccepted&&bendCheck.isBendOnlyProven){bendDxf=files.Write("bends/"+bodyName+(bendCheck.isBendOnlyProven?"-BEND_LINES.dxf":"-BEND_REVIEW.dxf"),bendCheck.outputText);current["bendDxfPath"]=bendDxf;}
                else ((List<string>)current["warnings"]).Add("BEND NEEDS_REVIEW: "+bendCheck.reason+"; native control file retained");
            }else {current["bendValidationStatus"]="NO_NATIVE_BEND_FILE";((List<string>)current["warnings"]).Add("Native bend DXF missing; no production approval");}
        }catch(Exception ex){if(ex.Message.Contains("SOURCE_STATE_CHANGED"))throw;current["bendExportError"]=ManufacturingSourceFilesV03.Error(ex,"Native BEND DXF export/validation","BEND_DXF",rawBends);if(ex.Message.Contains("DXF_EXPORT_PREFERENCE_RESTORE_FAILED")||ex.Message.Contains("OWNED_EXPORT_CONTEXT_CHANGED")||ex.Message.Contains("BINDING_MISMATCH"))throw;}
        current["bendSheetMetalOptions"]=((BendBindingV06)current["lastExportBinding"]).options;
        Stage("RESTORE_COPY_FOLDED_AND_CREATE_NATIVE_DRAWING");
        // The persisted reference copy is folded; flat drawing view manages its own
        // derived configuration without touching the original source.
        if(!SourceCall("Restore folded FlatPattern suppression in copy",()=>flat.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,(int)swInConfigurationOpts_e.swThisConfiguration,null))||!SourceCall("Rebuild restored folded copy",()=>copy.EditRebuild3()))throw new InvalidOperationException("Cannot restore folded state of owned reference copy");
        SourceCall("Show isolated folded body",()=>One(copy).HideBody(false));
        PrepareFoldedCopy(copy,flat,copyPath,bodyName);
        string isoName="MP_ISO_"+Guid.NewGuid().ToString("N");copy.ShowNamedView2("",(int)swStandardViews_e.swIsometricView);copy.NameView(isoName);
        var expectedIso=ManufacturingGeometryV01.Items(copy.IActiveView.Orientation3.ArrayData).Select(Convert.ToDouble).ToArray();
        current["expectedIsoRotation"]=expectedIso;current["availableModelViews"]=copy.GetModelViewNames();Save(copy,copyPath);
        var drawing=(ModelDoc2)sw.NewDocument(template,0,0,0);if(drawing==null)throw new InvalidOperationException("Drawing template could not create a new drawing");created.Add(drawing);activeDrawing=drawing;activeDrawingPath=files.Reserve("drawings/"+bodyName+".SLDDRW");current["drawingPath"]=activeDrawingPath;current["referencePartPath"]=copyPath;current["foldedConfigurationApiName"]=config;
        Activate(drawing);drawing.ClearSelection2(true);
        var dd=(DrawingDoc)drawing;double sheetW=0,sheetH=0;((Sheet)dd.GetCurrentSheet()).GetSize(ref sheetW,ref sheetH);
        if(!drawing.Extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitSystem,(int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,(int)swUnitSystem_e.swUnitSystem_MMGS))throw new InvalidOperationException("Cannot set new drawing units to MMGS");
        if(!(sheetW>0&&sheetH>0))throw new InvalidOperationException("Drawing template sheet dimensions unavailable");
        Stage("CREATE_FOLDED_NATIVE_ISOMETRIC_FIRST");
        var iso=InsertIsometric(dd,drawing,copy,flat,copyPath,bodyName,isoName,sheetW*.80,sheetH*.72);
        Stage("CREATE_NATIVE_FLAT_PATTERN_VIEW");
        Activate(drawing);drawing.ClearSelection2(true);
        var flatView=dd.CreateFlatPatternViewFromModelView3(copyPath,config,sheetW*.38,sheetH*.55,0,false,false);
        CaptureViews(dd);
        if(flatView==null||!flatView.IsFlatPatternView())throw new InvalidOperationException("Native flat-pattern drawing view failed");
        flatView.ShowSheetMetalBendNotes=true;
        if(iso==null)throw new InvalidOperationException("ISOMETRIC_VIEW_INCOMPLETE: native insertion attempts returned no view; flat drawing retained");
        iso.ReferencedConfiguration=config;
        Activate(drawing); bool incremental=drawing.EditRebuild3(); bool forced=false; if(!incremental){Activate(drawing);forced=drawing.ForceRebuild3(false);} current["drawingRebuild"]=new{activePath=((ModelDoc2)sw.ActiveDoc).GetPathName(),incremental,forced,ownedCopyOnly=true}; if(!incremental&&!forced)throw new InvalidOperationException("New drawing rebuild failed after explicit activation and ForceRebuild3");
        current["viewVerification"]=new[]{VerifyView(iso,copyPath,bodyName,config,false,expectedIso),VerifyView(flatView,copyPath,bodyName,null,true,null)};
        var drawingViews=ManufacturingGeometryV01.Items(dd.GetViews());
        if(drawingViews.Length!=1||ManufacturingGeometryV01.Items(drawingViews[0]).Length!=3)throw new InvalidOperationException("Drawing template introduced extra sheets/views; isolation not verified");
        foreach(var v in new[]{flatView,iso}){
            // Ownership and visible native entities were verified per view above. Multibody-filter counts are diagnostic, not sole ownership proof.
            var outline=ManufacturingGeometryV01.Items(v.GetOutline()).Select(Convert.ToDouble).ToArray();
            double allowedW=sheetW*(v==flatView?.58:.26),allowedH=sheetH*(v==flatView?.6:.30);
            double factor=Math.Min(allowedW/(outline[2]-outline[0]),allowedH/(outline[3]-outline[1]));
            v.ScaleDecimal=v.ScaleDecimal*Math.Min(1,factor);
        }
        int bendLines=flatView.GetBendLineCount();if(bendLines<=0&&bendParameters.Count>0)throw new InvalidOperationException("No native bend lines in requested bending drawing");
        if(bendLines>0&&bendDxf==null)((List<string>)current["warnings"]).Add("Bending drawing exists, but separate BEND DXF is unavailable; certification incomplete");
        current["nativeDrawingBendLineCount"]=bendLines;
        object validation;if(current.TryGetValue("bendValidation",out validation)&&validation is BendValidationV06){var b=(BendValidationV06)validation;current["bendOutputEntityCount"]=b.outputEntityCount;current["bendEntityCountMatchesDrawingLineCount"]=b.outputEntityCount==bendLines;if(bendDxf!=null&&b.outputEntityCount!=bendLines){current["unverifiedBendDxfPath"]=bendDxf;bendDxf=null;((List<string>)current["warnings"]).Add("Separate native BEND entity count differs from drawing bend-line count; body package INCOMPLETE, drawing verification independent");}}
        string bendNoteTextFormat=flatView.GetBendNoteTextFormat();
        var lineDirections=new List<object>();
        foreach(var segment in ManufacturingGeometryV01.Items(flatView.GetBendLines())){
            var line=segment as SketchLine;
            if(line==null){lineDirections.Add(new{status="NOT_VERIFIED",reason="Native bend segment does not expose SketchLine direction"});continue;}
            try{int direction=line.GetBendLineDirection();lineDirections.Add(new{code=direction,name=Enum.GetName(typeof(swBendLineDirection_e),direction)});}
            catch(Exception ex){lineDirections.Add(new{status="NOT_VERIFIED",reason=ex.Message});}
        }
        var note=(Note)drawing.InsertNote(bodyName+"\nThickness: "+thickness.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" mm\nMaterial: "+material+"\nTECHNOLOGICAL DIMENSIONS TO BE ADDED BY USER");
        if(note==null)throw new InvalidOperationException("Cannot add drawing title/material note");((Annotation)note.GetAnnotation()).SetPosition2(sheetW*.04,sheetH*.94,0);
        string drw=activeDrawingPath;Save(drawing,drw);
        Stage("EXPORT_PDF_OF_VALIDATED_DRAWING");
        string pdf=files.Reserve("pdf/"+bodyName+".pdf");var pdfData=(ExportPdfData)sw.GetExportFileData((int)swExportDataFileType_e.swExportPdfData);pdfData.ViewPdfAfterSaving=false;
        files.BeforeSave(pdf);int pe=0,pw=0;if(!drawing.Extension.SaveAs(pdf,0,(int)swSaveAsOptions_e.swSaveAsOptions_Silent,pdfData,ref pe,ref pw)||pe!=0||!File.Exists(pdf)||new FileInfo(pdf).Length==0)throw new InvalidOperationException("PDF output failed: "+pe);files.Created.Add(pdf);current["pdfPath"]=pdf;current["pdfExport"]=new{success=true,errors=pe,warnings=pw,bytes=new FileInfo(pdf).Length};
        if(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf).Take(5).ToArray())!="%PDF-")throw new InvalidOperationException("Saved PDF header invalid");
        if(!ManufacturingDrawingChecksV13.PdfComplete(File.ReadAllBytes(pdf)))throw new InvalidOperationException("Saved PDF is missing header or EOF marker");current["pdfStructureCheck"]="HEADER_AND_EOF_CHECKED_NOT_FULL_PDF_RENDER";
        // Native flat view may have added a derived configuration to this owned copy.
        PrepareFoldedCopy(copy,flat,copyPath,bodyName);Save(drawing,drw);
        Stage("PACK_AND_REOPEN_LINKED_EDITABLE_DRAWING");
        var portable=PackageAndReopen(drawing,copy,drw,copyPath,bodyName,expectedIso);
        drw=portable.Item1;copyPath=portable.Item2;current["drawingPath"]=drw;current["referencePartPath"]=copyPath;current["drawingStatus"]="EDITABLE_NATIVE_VIEWS_SAVED_AND_REOPENED";
        activeDrawing=null;
        Stage("COMPLETE");
        return new{bodyName,dxf,exactPath,camDxf,camStatus=current["camStatus"],approximation,bendDxf,exportMethod="Native ExportToDWG2",cutSheetMetalOptions=1,bendSheetMetalOptions=4,drw,pdf,referencePart=copyPath,thicknessMm=thickness,bendLines,bendParameters,lineDirections,bendNoteTextFormat,bendNotesRequested=true,
            nativeFlattenedCurveKinds=nativeCurveKinds,
            nativeBoundary=new{boundary.contours,boundary.fullCircles,boundary.curvedEdges},
            nativeThicknessM=thicknesses[0],bendParameterCompletenessVerified=false,
            alignment=new{originM=new[]{0d,0,0},xDirection=x.Data(),yDirection=y.Data(),normal=normal.Data()},
            check=new{check.accepted,check.reason,check.entityCount,check.closedCurves,check.closedConnectedContours,check.extentsMm,check.entityTypes},
            sourceIsolationVerified=true,productionReadinessCertified=false};
    }
}
