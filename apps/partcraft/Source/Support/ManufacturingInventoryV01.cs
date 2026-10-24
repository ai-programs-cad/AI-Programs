using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

class ManufacturingInventoryV01 {
    class FolderWatch {public Feature Feature;public BodyFolder Folder;public string Name;public int Stamp;public object[] Members;public int? ApiCount;}
    static SldWorks sw;
    static List<object> errors=new List<object>();
    static List<string> warnings=new List<string>();
    static object[] Items(object x){return x==null?new object[0]:((Array)x).Cast<object>().ToArray();}
    static double[] Numbers(object x){return x==null?null:Items(x).Select(Convert.ToDouble).ToArray();}
    static object Read(string label,Func<object> f){
        try{return f();}catch(Exception ex){errors.Add(new{api=label,exception=ex.GetType().FullName,hresult="0x"+ex.HResult.ToString("X8"),message=ex.Message});return null;}
    }
    static bool Same(object a,object b){
        if(a==null||b==null)return a==null&&b==null;int code=sw.IsSame(a,b);
        if(code==(int)swObjectEquality.swObjectSame)return true;
        if(code==(int)swObjectEquality.swObjectNotSame)return false;
        throw new InvalidOperationException("SolidWorks IsSame could not establish COM object identity; code="+code);
    }
    static List<Feature> Chain(Feature first,bool sub){
        var result=new List<Feature>();var f=first;
        while(f!=null){
            if(result.Any(x=>Same(x,f)))throw new InvalidOperationException("Cycle in feature sibling chain; traversal incomplete");
            if(result.Count>=10000)throw new InvalidOperationException("Feature sibling chain safety limit exceeded");
            result.Add(f);f=(Feature)(sub?f.GetNextSubFeature():f.GetNextFeature());
        }
        return result;
    }
    static object State(ModelDoc2 doc,PartDoc part){
        return new{path=doc.GetPathName(),configuration=doc.ConfigurationManager.ActiveConfiguration.Name,dirty=doc.GetSaveFlag(),
            unitSystem=doc.Extension.GetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitSystem,(int)swUserPreferenceOption_e.swDetailingNoOptionSpecified),
            updateStamp=doc.GetUpdateStamp(),needsRebuild=doc.Extension.NeedsRebuild2,configurationNeedsRebuild=doc.ConfigurationManager.ActiveConfiguration.NeedsRebuild,
            selectionCount=((SelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1),
            bodies=Items(part.GetBodies2((int)swBodyType_e.swSolidBody,false)).Cast<Body2>().Select(b=>new{name=b.Name,visible=b.Visible,stamp=b.GetUpdateStamp(),
                faceCount=b.GetFaceCount(),edgeCount=b.GetEdgeCount(),vertexCount=b.GetVertexCount()}).OrderBy(b=>b.name,StringComparer.Ordinal).ThenBy(b=>b.stamp).ToArray()};
    }
    static void Context(ModelDoc2 doc,string path,string config){
        if(!Same(doc,sw.ActiveDoc)||doc.GetPathName()!=path||doc.ConfigurationManager.ActiveConfiguration.Name!=config)
            throw new InvalidOperationException("ACTIVE_DOCUMENT_OR_CONFIGURATION_CHANGED; no corrective action");
    }
    static List<CLProperty> Properties(Feature feature,out bool enumerationSucceeded){
        var list=new List<CLProperty>();enumerationSucceeded=false;
        try{
            var manager=feature.CustomPropertyManager;
            if(manager==null)throw new InvalidOperationException("Feature.CustomPropertyManager returned null");
            var names=Items(manager.GetNames()).Select(x=>(string)x).ToArray();
            if(manager.Count!=names.Length)throw new InvalidOperationException("Custom property Count/GetNames mismatch; property enumeration incomplete");
            enumerationSucceeded=true;
            foreach(var name in names){
                var p=new CLProperty{name=name};list.Add(p);
                p.typeCode=(int?)Read(feature.Name+".property["+name+"].GetType2",()=>manager.GetType2(name));
                try{
                    string raw,resolved;bool wasResolved,linked;
                    // Only active configuration, UseCached=true. Never force property
                    // evaluation/configuration activation by using UseCached=false.
                    int code=manager.Get6(name,true,out raw,out resolved,out wasResolved,out linked);
                    p.resultCode=code;p.wasResolved=wasResolved;p.linkedToProperty=linked;
                    if(code!=(int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent){p.rawValue=raw;p.resolvedValue=resolved;}
                }catch(Exception ex){p.error=new{exception=ex.GetType().FullName,hresult="0x"+ex.HResult.ToString("X8"),message=ex.Message};
                    errors.Add(new{api=feature.Name+".Get6("+name+", UseCached=true)",error=p.error});}
            }
        }catch(Exception ex){errors.Add(new{api="CutList custom property enumeration",message=ex.Message,exception=ex.GetType().FullName});}
        return list;
    }
    static CLBody Body(Body2 b,string config){
        string id=Guid.NewGuid().ToString("N"),name=b.Name;
        var box=Read(name+".GetBodyBox",()=>Numbers(b.GetBodyBox()));
        object extents=null;
        if(box is double[]&&((double[])box).Length==6){var a=(double[])box;extents=new[]{a[3]-a[0],a[4]-a[1],a[5]-a[2]};}
        var material=Read(name+".GetMaterialPropertyName(active configuration)",()=>{
            string database;string value=b.GetMaterialPropertyName(config,out database);
            return new{bodyMaterialName=value,database,source="Body2.GetMaterialPropertyName(active configuration)",inheritedMaterialNotSubstituted=true};
        });
        return new CLBody{id=id,name=name,visible=(bool?)Read(name+".Visible",()=>b.Visible),sheetMetal=(bool?)Read(name+".IsSheetMetal",()=>b.IsSheetMetal()),material=material,
            geometry=new{lengthUnit="metre",faceCount=Read(name+".GetFaceCount",()=>b.GetFaceCount()),edgeCount=Read(name+".GetEdgeCount",()=>b.GetEdgeCount()),
                vertexCount=Read(name+".GetVertexCount",()=>b.GetVertexCount()),bodyBoxM=box,approximateAxisAlignedExtentsM=extents,
                boundingBoxIsApproximate=true,manufacturingLengthNotInferred=true,brepEquivalenceCertified=false}};
    }
    public static object Snapshot(ModelDoc2 doc){return State(doc,(PartDoc)doc);}
    public static Dictionary<string,object> Capture(string expectedPath,string expectedConfig){
        errors.Clear();warnings.Clear();
        int? expectedBodies=null,expectedFolders=null;
        var bodies=new List<CLBody>();var folders=new List<CLFolder>();var watches=new List<FolderWatch>();
        var result=new Dictionary<string,object>{{"version","ReadCutListV01"},{"readOnly",true},{"changesMade",false},{"connected",false},
            {"bodies",bodies},{"folders",folders},{"errors",errors},{"warnings",warnings},{"propertyReadMode","Get6 UseCached=true; raw and returned resolved strings retained without forced update"},
            {"groupingIsNotBrepCertificate",true},{"assemblyInstanceQuantityNotRead",true},{"folderOrderDoesNotDetermineMembership",true}};
        ModelDoc2 doc=null;PartDoc part=null;object before=null;string path=null,config=null;bool traversalComplete=false;int exit=0;
        try{
            sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");result["connected"]=true;
            doc=(ModelDoc2)sw.ActiveDoc;if(doc==null)throw new InvalidOperationException("NO_ACTIVE_DOCUMENT");
            part=doc as PartDoc;if(part==null)throw new InvalidOperationException("ACTIVE_DOCUMENT_NOT_PART");
            path=doc.GetPathName();config=doc.ConfigurationManager.ActiveConfiguration.Name;
            result["activePath"]=path;result["activeConfiguration"]=config;
            if(expectedPath!=null&&!String.Equals(expectedPath,path,StringComparison.Ordinal))throw new InvalidOperationException("EXPECTED_DOCUMENT_PATH_MISMATCH");
            if(expectedConfig!=null&&!String.Equals(expectedConfig,config,StringComparison.Ordinal))throw new InvalidOperationException("EXPECTED_CONFIGURATION_MISMATCH");
            before=State(doc,part);result["before"]=before;
            var rawBodies=Items(part.GetBodies2((int)swBodyType_e.swSolidBody,false)).Cast<Body2>().ToArray();
            // De-duplicate actual objects, never names or tree position.
            var uniqueBodies=new List<Body2>();
            foreach(var b in rawBodies){if(uniqueBodies.Any(x=>Same(x,b))){warnings.Add("GetBodies2 returned duplicate COM body reference");continue;}
                Context(doc,path,config);uniqueBodies.Add(b);bodies.Add(Body(b,config));}
            var features=CutListCoreV01.Traverse(Chain((Feature)doc.FirstFeature(),false),
                f=>Chain((Feature)f.GetFirstSubFeature(),true),(a,b)=>Same(a,b));
            traversalComplete=true;result["featuresVisited"]=features.Count;
            foreach(var feature in features){
                Context(doc,path,config);string type=feature.GetTypeName2();
                if(type!="CutListFolder")continue;
                var folder=new CLFolder{id=Guid.NewGuid().ToString("N"),name=feature.Name};folders.Add(folder);
                folder.suppressed=Read(folder.name+".IsSuppressed",()=>feature.IsSuppressed());
                folder.excludedFromCutList=Read(folder.name+".ExcludeFromCutList",()=>feature.ExcludeFromCutList);
                var specific=(BodyFolder)Read(folder.name+".GetSpecificFeature2",()=>feature.GetSpecificFeature2());
                if(specific!=null){
                    folder.apiBodyCount=(int?)Read(folder.name+".GetBodyCount",()=>specific.GetBodyCount());
                    folder.freshness=new{automaticCutList=Read(folder.name+".GetAutomaticCutList",()=>specific.GetAutomaticCutList()),
                        automaticUpdate=Read(folder.name+".GetAutomaticUpdate",()=>specific.GetAutomaticUpdate()),cutListType=Read(folder.name+".GetCutListType",()=>specific.GetCutListType()),
                        bodyFolderType=Read(folder.name+".Type",()=>specific.Type),featureUpdateStamp=Read(folder.name+".GetUpdateStamp",()=>feature.GetUpdateStamp()),
                        currentnessCertified=false};
                    try{
                        object memberResult=specific.GetBodies();
                        if(memberResult==null&&folder.apiBodyCount!=0)throw new InvalidOperationException("GetBodies returned null without confirmed empty GetBodyCount");
                        var members=Items(memberResult);folder.bodyEnumerationSucceeded=true;folder.enumeratedBodyCount=members.Length;
                        foreach(var member in members){
                            int index=uniqueBodies.FindIndex(x=>Same(x,member));
                            if(index>=0)folder.bodyIds.Add(bodies[index].id);
                            else {var ub=member as Body2;folder.unknownBodies.Add(new{name=ub==null?null:Read("unknown folder body.Name",()=>ub.Name),
                                bodyType=ub==null?null:Read("unknown folder body.GetType",()=>ub.GetType()),reason="GetBodies reference not found among active solid bodies; no membership inferred"});}
                        }
                        watches.Add(new FolderWatch{Feature=feature,Folder=specific,Name=feature.Name,Stamp=feature.GetUpdateStamp(),Members=members,ApiCount=folder.apiBodyCount});
                    }catch(Exception ex){folder.bodyEnumerationSucceeded=false;errors.Add(new{api=folder.name+".GetBodies/identity",exception=ex.GetType().FullName,hresult="0x"+ex.HResult.ToString("X8"),message=ex.Message});}
                }else folder.warnings.Add("BodyFolder unavailable; membership NOT_VERIFIED");
                bool propsRead;folder.properties=Properties(feature,out propsRead);folder.propertiesEnumerationSucceeded=propsRead;
                if(!propsRead)folder.warnings.Add("Property enumeration incomplete");
                if(folder.properties.Any(p=>p.error!=null||p.wasResolved==false||p.resultCode==(int)swCustomInfoGetResult_e.swCustomInfoGetResult_CachedValue))
                    folder.warnings.Add("Some property values are unresolved/cached or unreadable; freshness not certified");
            }
            result["status"]="READ_COMPLETE";
        }catch(Exception ex){result["status"]="STOPPED_OR_INCOMPLETE";errors.Add(new{api="Main",exception=ex.GetType().FullName,hresult="0x"+ex.HResult.ToString("X8"),message=ex.Message});exit=1;}
        finally{
            if(before!=null)try{
                Context(doc,path,config);var after=State(doc,part);result["after"]=after;
                bool unchanged=CutListCoreV01.Json.Serialize(before)==CutListCoreV01.Json.Serialize(after);
                var folderControls=new List<object>();bool folderChecksKnown=watches.Count==folders.Count;
                foreach(var watch in watches){
                    try{
                        var members=Items(watch.Folder.GetBodies());var used=new bool[members.Length];
                        bool equal=members.Length==watch.Members.Length;
                        foreach(var old in watch.Members){int match=-1;for(int i=0;i<members.Length;i++)if(!used[i]&&Same(old,members[i])){match=i;break;}if(match<0)equal=false;else used[match]=true;}
                        string currentName=watch.Feature.Name;int stamp=watch.Feature.GetUpdateStamp(),count=watch.Folder.GetBodyCount();
                        bool passed=equal&&currentName==watch.Name&&stamp==watch.Stamp&&watch.ApiCount.HasValue&&watch.ApiCount.Value==count;
                        folderChecksKnown&=watch.ApiCount.HasValue;unchanged&=passed;
                        folderControls.Add(new{folder=watch.Name,afterName=currentName,beforeStamp=watch.Stamp,afterStamp=stamp,membershipUnchanged=equal,passed});
                    }catch(Exception ex){folderChecksKnown=false;errors.Add(new{api="folder after-state",folder=watch.Name,message=ex.Message});}
                }
                result["folderStateChecks"]=folderControls;result["folderStateChecksComplete"]=folderChecksKnown;
                result["observedStateUnchanged"]=!unchanged?(object)false:folderChecksKnown?(object)true:null;
                if(!unchanged||!folderChecksKnown){result["status"]=!unchanged?"STATE_CHECK_FAILED":"STATE_CHECK_UNAVAILABLE";warnings.Add("Observed model/folder state changed or could not be verified; no corrective action attempted");exit=3;}
            }catch(Exception ex){result["observedStateUnchanged"]=null;result["status"]="STATE_CHECK_UNAVAILABLE_OR_CONTEXT_CHANGED";errors.Add(new{api="after-state",message=ex.Message,exception=ex.GetType().FullName});exit=3;}
            else result["observedStateUnchanged"]=null;
        }
        result["membershipAudit"]=CutListCoreV01.Audit(bodies,folders,traversalComplete);
        var audit=CutListCoreV01.Json.Deserialize<Dictionary<string,object>>(CutListCoreV01.Json.Serialize(result["membershipAudit"]));
        var state=before==null?null:CutListCoreV01.Json.Deserialize<Dictionary<string,object>>(CutListCoreV01.Json.Serialize(before));
        result["freshnessAssessment"]=new{cutListCurrentnessCertified=false,
            documentNeedsRebuildStatus=state==null?null:state["needsRebuild"],configurationNeedsRebuild=state==null?null:state["configurationNeedsRebuild"],
            membershipInconsistencyDetected=!(bool)audit["membershipConsistent"],
            interpretation="Rebuild flags, unresolved/cached values, membership/count inconsistencies are warning evidence only. Folder reordering does not explain or define membership.",
            updateOrRebuildPerformed=false,automaticUpdateSettingIsNotFreshnessProof=true};
        result["controlExpectations"]=new{expectedBodies,actualBodies=bodies.Count,bodiesMatch=expectedBodies.HasValue?(object)(expectedBodies.Value==bodies.Count):null,
            expectedFolders,actualFolders=folders.Count,foldersMatch=expectedFolders.HasValue?(object)(expectedFolders.Value==folders.Count):null};
        if(expectedBodies.HasValue&&expectedBodies.Value!=bodies.Count)warnings.Add("Optional reference body count differs: expected "+expectedBodies.Value+", observed "+bodies.Count+"; not used to assign membership");
        if(expectedFolders.HasValue&&expectedFolders.Value!=folders.Count)warnings.Add("Optional reference folder count differs: expected "+expectedFolders.Value+", observed "+folders.Count+"; no folders invented or merged");
        warnings.Add("Automatic-update settings and resolved property flags do not certify current Cut List geometry/grouping. Full freshness verification may require an update/rebuild; neither was performed.");
        warnings.Add("ANGLE1/ANGLE2 and localized equivalents are retained exactly as returned SolidWorks cutting-angle properties. '-' is not replaced with 0 degrees.");
        result["utc"]=DateTime.UtcNow.ToString("O");result["readOnly"]=true;result["changesMade"]=false;
        result["inventoryReturnCode"]=exit;return result;
    }
}
