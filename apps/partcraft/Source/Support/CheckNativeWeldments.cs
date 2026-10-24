using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;

class AgentNativeReader {
    static JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue,RecursionLimit=150};
    static SldWorks sw;static List<object> errors=new List<object>();
    static object[] A(object x){return NativeWeldmentAdapter.Items(x);}
    static string Cell(object o){return "\""+Convert.ToString(o,System.Globalization.CultureInfo.InvariantCulture).Replace("\"","\"\"")+"\"";}
    static void WriteCutsCsv(string path,object rows){
        var cols=new[]{"body","cutList","Description","LENGTH","ANGLE1","ANGLE2","QUANTITY","H","DIR1","DIR2","HStatus","directionStatus","machiningStatus"};
        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write))using(var w=new StreamWriter(file,new UTF8Encoding(true))){
            w.WriteLine(String.Join(";",cols.Select(Cell)));
            foreach(var raw in A(rows)){
                var row=json.Deserialize<Dictionary<string,object>>(json.Serialize(raw));
                var p=row["commonFolderProperties"] as Dictionary<string,object>;
                w.WriteLine(String.Join(";",cols.Select(k=>{
                    if(k=="cutList")return Cell(String.Join(",",A(row[k]).Select(Convert.ToString)));
                    object value;if(row.TryGetValue(k,out value))return Cell(value);
                    if(p!=null&&p.TryGetValue(k,out value)&&value is Dictionary<string,object>){object resolved;((Dictionary<string,object>)value).TryGetValue("resolved",out resolved);return Cell(resolved);}
                    return Cell(null);
                })));
            }
        }
    }
    static object Read(string api,Func<object> fn){try{return fn();}catch(Exception e){errors.Add(new{api,hresult="0x"+e.HResult.ToString("X8"),exception=e.ToString()});return null;}}
    static string Hash(string path){using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","");}
    static object Dimensions(Feature feature){
        var rows=new List<object>();var visited=new List<DisplayDimension>();
        for(var dd=feature.GetFirstDisplayDimension() as DisplayDimension;dd!=null;dd=feature.GetNextDisplayDimension(dd) as DisplayDimension){
            if(visited.Any(x=>sw.IsSame(x,dd)==1)||visited.Count>10000)throw new Exception("Display dimension cycle/limit");visited.Add(dd);
            var dim=dd.GetDimension2(0);if(dim!=null)rows.Add(new{name=dim.FullName,nativeDimensionType=dim.GetType(),nativeDisplayDimensionType=dd.Type2,systemValue=dim.SystemValue,units="SolidWorks native SI: length=m, angle=rad; interpret using native dimension type, never assume all values are mm",roleNotInferredFromDNumber=true});
        }return rows;
    }
    static object State(ModelDoc2 doc,Body2[] bodies){return new{path=doc.GetPathName(),config=doc.ConfigurationManager.ActiveConfiguration.Name,dirty=doc.GetSaveFlag(),stamp=doc.GetUpdateStamp(),bodies=bodies.Select(b=>new{name=b.Name,stamp=b.GetUpdateStamp(),visible=b.Visible}).ToArray()};}
    static object SheetThickness(Feature[] related){
        var values=new List<double>();var evidence=new List<object>();
        foreach(var feature in related.Where(f=>f.GetTypeName2()=="SheetMetal")){
            var raw=Read(feature.Name+".SheetMetalFeatureData.Thickness",()=>((SheetMetalFeatureData)feature.GetDefinition()).Thickness);
            if(raw!=null){double meters=Convert.ToDouble(raw);if(!Double.IsNaN(meters)&&!Double.IsInfinity(meters)&&meters>0){values.Add(meters);evidence.Add(new{feature=feature.Name,thicknessMeters=meters,thicknessMm=meters*1000});}}
        }
        var distinct=values.Distinct().ToArray();return new{source="Native SheetMetalFeatureData.Thickness only; no material/Description inference",thicknessMm=distinct.Length==1?(double?)(distinct[0]*1000):null,evidence,status=distinct.Length==1?"NATIVE_SHEET_METAL_VALUE_READ":"NEEDS_REVIEW"};
    }
    static object Definition(Feature f){
        object def=f.GetDefinition();string kind=f.GetTypeName2();
        if(kind=="WeldMemberFeat"){
            var member=(StructuralMemberFeatureData)def;
            return new{profilePath=Read(f.Name+".WeldmentProfilePath",()=>member.WeldmentProfilePath),libraryMaterial=Read(f.Name+".LibraryProfileMaterial",()=>member.LibraryProfileMaterial),rotationRadians=Read(f.Name+".RotationAngle",()=>member.RotationAngle),groupCount=Read(f.Name+".GetGroupsCount",()=>member.GetGroupsCount()),pathSegmentCount=Read(f.Name+".GetPathSegmentsCount",()=>member.GetPathSegmentsCount()),drivingDimensions=Read(f.Name+".GetFirstDisplayDimension",()=>Dimensions(f)),profileDimensionsVerified=false,remarks="Library profile path is native provenance, not proof of final trimmed profile dimensions. Path length is not substituted for final LENGTH."};
        }
        if(kind=="WeldCornerFeat"){
            var trim=(WeldmentTrimExtendFeatureData)def;
            return new{cornerType=Read(f.Name+".CornerType",()=>trim.CornerType),trimmedBodyCount=Read(f.Name+".GetBodiesToBeTrimmedCount",()=>trim.GetBodiesToBeTrimmedCount()),boundaryCount=Read(f.Name+".GetTrimmingBoundaryCount",()=>trim.GetTrimmingBoundaryCount()),selectionAccessCalled=false,remarks="AccessSelections would roll back the model; not called by read-only checker"};
        }
        if(kind=="FlatPattern"){
            var face=((FlatPatternFeatureData)def).FixedFace2 as Face2;
            return new{owner=face==null?null:((Body2)face.GetBody()).Name,association="native FixedFace2.GetBody"};
        }
        return null;
    }
    static Dictionary<string,NativeCutListValue> Properties(Feature f){
        var result=new Dictionary<string,NativeCutListValue>(StringComparer.Ordinal);var manager=f.CustomPropertyManager;
        foreach(string name in A(manager.GetNames()).Select(Convert.ToString)){
            result[name]=Read(f.Name+".Get6("+name+")",()=>{string raw,resolved;bool wasResolved,linked;int code=manager.Get6(name,true,out raw,out resolved,out wasResolved,out linked);
                return new NativeCutListValue{name=name,raw=raw,resolved=resolved,wasResolved=wasResolved,linked=linked,resultCode=code};}) as NativeCutListValue;
        }return result;
    }
    static IEnumerable<Feature> Walk(ModelDoc2 doc){
        var result=new List<Feature>();var stack=new Stack<Feature>();var top=new List<Feature>();
        for(var f=(Feature)doc.FirstFeature();f!=null;f=(Feature)f.GetNextFeature()){if(top.Any(x=>sw.IsSame(x,f)==1))throw new Exception("Feature sibling cycle");top.Add(f);if(top.Count>10000)throw new Exception("Feature limit");}
        foreach(var f in top.AsEnumerable().Reverse())stack.Push(f);
        while(stack.Count>0){var f=stack.Pop();if(result.Any(x=>sw.IsSame(x,f)==1))continue;result.Add(f);if(result.Count>10000)throw new Exception("Feature traversal limit");var children=new List<Feature>();for(var c=(Feature)f.GetFirstSubFeature();c!=null;c=(Feature)c.GetNextSubFeature()){if(children.Any(x=>sw.IsSame(x,c)==1))throw new Exception("Feature child cycle");children.Add(c);}foreach(var c in children.AsEnumerable().Reverse())stack.Push(c);}
        return result;
    }
    public static int Run(string[] args){ errors=new List<object>();
        Console.OutputEncoding=new UTF8Encoding(false);if(args.SequenceEqual(new[]{"--self-test"})){NativeWeldmentAdapter.Tests();NativeSectionCheck.Tests();return NativeEndCutCheck.Tests();}
        string expected=null,output=null,sectionEvidence=null;for(int i=0;i<args.Length;i++){if(args[i]=="--source")expected=Path.GetFullPath(args[++i]);else if(args[i]=="--section-evidence")sectionEvidence=Path.GetFullPath(args[++i]);else if(args[i]=="--output")output=Path.GetFullPath(args[++i]);else throw new ArgumentException("--source exact.SLDPRT --output NEW.json");}
        if(expected==null||output==null||File.Exists(output))throw new ArgumentException("Exact --source and new --output JSON required; overwrite forbidden");
        var report=new Dictionary<string,object>{{"program","CheckNativeWeldments"},{"status","NEEDS_REVIEW"},{"sourceMutatingCallsMade",false},{"readOnly",true},{"productionReady",false},{"errors",errors}};int exit=2;ModelDoc2 doc=null;object before=null;string hash=null;
        try{
            sw=ManufacturingAgent.Connection();doc=sw.ActiveDoc as ModelDoc2;
            if(doc==null||doc.GetType()!=1||!String.Equals(Path.GetFullPath(doc.GetPathName()),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("ACTIVE_SOURCE_DOCUMENT_MISMATCH");
            var bodies=A(((PartDoc)doc).GetBodies2(0,false)).Cast<Body2>().ToArray();hash=Hash(expected);report["sha256Before"]=hash;before=State(doc,bodies);report["before"]=before;
            if(bodies.Length==0)throw new InvalidOperationException("NO_SOLID_BODIES_IN_CURRENT_CONTEXT: stop; do not infer a valid empty production model or change rollback/configuration state");
            var features=Walk(doc).ToArray();var folders=new List<object>();var profiles=new Dictionary<string,AuthoritativeFolderProfile>();var membership=new Dictionary<string,List<string>>();foreach(var b in bodies){if(membership.ContainsKey(b.Name))throw new Exception("Ambiguous exact body name "+b.Name);membership[b.Name]=new List<string>();}
            foreach(var f in features.Where(f=>f.GetTypeName2()=="CutListFolder")){
                var bf=(BodyFolder)f.GetSpecificFeature2();var members=A(bf.GetBodies()).Cast<Body2>().ToArray();
                foreach(var b in members){var matches=bodies.Where(x=>sw.IsSame(x,b)==1).ToArray();if(matches.Length!=1)throw new Exception("Unresolved Cut List native owner "+f.Name);membership[matches[0].Name].Add(f.Name);}
                var properties=Properties(f);var profile=NativeWeldmentAdapter.Profile(f.Name,properties,members.Length);profiles.Add(f.Name,profile);
                folders.Add(new{name=f.Name,physicalCount=members.Length,bodies=members.Select(b=>b.Name).ToArray(),properties,authoritativeProfile=profile,propertyReadMode="CustomPropertyManager.Get6 UseCached=true; raw/resolved values preserved",identityWithinFolderCertified=false});
            }
            if(doc.GetSaveFlag())throw new Exception("SOURCE_HAS_UNSAVED_CHANGES: cached section evidence cannot certify current in-memory geometry");
            var cachedSections=new Dictionary<string,SectionComparison>(StringComparer.Ordinal);
            if(sectionEvidence!=null){var saved=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(sectionEvidence,Encoding.UTF8));if(!String.Equals(Convert.ToString(saved["sha256After"]),hash,StringComparison.Ordinal)||Convert.ToString(((Dictionary<string,object>)saved["before"])["config"])!=doc.ConfigurationManager.ActiveConfiguration.Name)throw new Exception("SECTION_EVIDENCE_SOURCE_OR_CONFIGURATION_MISMATCH");if(json.Serialize(saved["finalState"])!=json.Serialize(before))throw new Exception("SECTION_EVIDENCE_MODEL_STATE_MISMATCH");foreach(var item in A(saved["bodies"])){var row=(Dictionary<string,object>)item;if(row["sectionComparison"]!=null)cachedSections.Add(Convert.ToString(row["body"]),json.ConvertToType<SectionComparison>(row["sectionComparison"]));} report["reusedVerifiedSectionEvidence"]=sectionEvidence;}
            var rows=new List<object>();
            foreach(var b in bodies){
                var related=A(b.GetFeatures()).Cast<Feature>().ToArray();bool sheet=b.IsSheetMetal();var structural=related.Where(f=>f.GetTypeName2()=="WeldMemberFeat").ToArray();
                var common=membership[b.Name].Count==1?profiles[membership[b.Name][0]]:null;
                ManufacturingAgent.BodyProgress(rows.Count,bodies.Length); var section=sheet?null:sectionEvidence!=null?(cachedSections.ContainsKey(b.Name)?cachedSections[b.Name]:null):NativeSectionCheck.Read(sw,b,related);
                var cuts=sheet?null:NativeEndCutCheck.Read(sw,b,section,common);
                rows.Add(new{endCutCheck=cuts,body=b.Name,visible=b.Visible,sheetMetal=sheet,cutList=membership[b.Name],physicalBodyQuantity=1,commonFolderProperties=common,MATERIAL_ASSIGNED=common==null?null:common.MATERIAL,materialRole="REFERENCE_ONLY_COLOR_ASSIGNMENT; actual material NOT_CONFIRMED",ACTUAL_PROFILE=new ActualProfileEvidence(),sectionComparison=section,PROFILE_TYPE=section==null?null:section.PROFILE_TYPE,NOMINAL_WIDTH=section==null?null:section.NOMINAL_WIDTH,NOMINAL_HEIGHT=section==null?null:section.NOMINAL_HEIGHT,NOMINAL_WALL_THICKNESS=section==null?null:section.NOMINAL_WALL_THICKNESS,BOUNDING_BOX_WIDTH=section==null?null:section.BOUNDING_BOX_WIDTH,BOUNDING_BOX_HEIGHT=section==null?null:section.BOUNDING_BOX_HEIGHT,CROSS_SECTION_VERIFICATION_STATUS=section==null?"NOT_APPLICABLE_SHEET_METAL":section.CROSS_SECTION_VERIFICATION_STATUS,actualProfileStatus=sheet?"SHEET_METAL_BRANCH":"NEEDS_REVIEW: final bounded cross-section certification pending",sheetThickness=sheet?SheetThickness(related):null,kind=sheet?"SHEET_METAL":structural.Length>0?"NATIVE_STRUCTURAL_MEMBER":"NON_SHEET_BODY_ORIGIN_NEEDS_REVIEW",nativeFeatureEvidence=related.Select(f=>new{name=f.Name,type=f.GetTypeName2()}).ToArray(),profileSource="Nominal: assigned Structural Member API profile; verification: native body path / oriented section geometry",H=cuts==null?null:cuts.H,HStatus=cuts==null?"NOT_APPLICABLE_SHEET_METAL":cuts.HStatus,DIR1=cuts==null?null:cuts.DIR1,DIR2=cuts==null?null:cuts.DIR2,directionStatus=cuts==null?"NOT_APPLICABLE_SHEET_METAL":cuts.directionStatus,additionalMachiningCertifiedAbsent=cuts!=null&&cuts.additionalMachiningCertifiedAbsent,machiningStatus=cuts==null?"NOT_APPLICABLE_SHEET_METAL":cuts.machiningStatus,productionGroupKey=(string)null,materialUsedForGrouping=false,mergeAllowed=false});
            }
            report["bodies"]=rows;report["solidBodyCount"]=bodies.Length;report["sheetMetalCount"]=bodies.Count(b=>b.IsSheetMetal());report["nonSheetCount"]=bodies.Count(b=>!b.IsSheetMetal());report["cutListFolders"]=folders;report["membershipConsistent"]=membership.All(kv=>kv.Value.Count==1);
            report["nativeDefinitions"]=features.Where(f=>new[]{"WeldMemberFeat","WeldCornerFeat","FlatPattern"}.Contains(f.GetTypeName2())).Select(f=>new{name=f.Name,type=f.GetTypeName2(),data=Read(f.Name+".GetDefinition",()=>Definition(f))}).ToArray();
            report["profileAuthority"]="NOMINAL PROFILE: assigned Structural Member WeldmentProfilePath and ConfigurationName, not display feature name or material. Verification: body-aligned native extents and section geometry. LENGTH/ANGLE1/ANGLE2/QUANTITY: Cut List.";
            report["materialPolicy"]="Assigned MATERIAL is reference-only color styling; does not certify actual material, dimensions, wall thickness or production grouping";
            report["unverifiedConditions"]=new[]{"Actual final body section must be confirmed; MATERIAL/Description are not a geometry fallback","Missing/unresolved LENGTH/ANGLE1/ANGLE2/QUANTITY remain missing","H, DIR1/DIR2 and additional machining require individual body verification","Unknown section/orientation/machining blocks grouping; MATERIAL is never part of group key","Native drawing Weldment Cut List table not created by read-only checker"};
            var after=State(doc,bodies);report["after"]=after;report["observedStateUnchanged"]=json.Serialize(before)==json.Serialize(after);report["sha256After"]=Hash(expected);report["sourceFileUnchanged"]=(string)report["sha256After"]==hash;
            if(!(bool)report["observedStateUnchanged"]||!(bool)report["sourceFileUnchanged"])throw new Exception("READ_ONLY_SOURCE_STATE_CHANGED");
            report["status"]="NATIVE_READ_COMPLETED_SUPPORT_NOT_YET_CERTIFIED";
        }catch(Exception e){report["status"]="STOPPED_OR_INCOMPLETE";report["error"]=new{hresult="0x"+e.HResult.ToString("X8"),exception=e.ToString()};exit=1;}
        finally{
            if(hash!=null)report["finalFileHashCheck"]=Read("SHA256 final original",()=>Hash(expected)==hash);
            if(before!=null&&doc!=null){object final=Read("Model state in finally",()=>State(doc,A(((PartDoc)doc).GetBodies2(0,false)).Cast<Body2>().ToArray()));report["finalState"]=final;report["observedStateUnchanged"]=final!=null&&json.Serialize(before)==json.Serialize(final);if(!(bool)report["observedStateUnchanged"]){report["status"]="STOPPED_OR_INCOMPLETE";exit=1;}}
            Directory.CreateDirectory(Path.GetDirectoryName(output));using(var f=new FileStream(output,FileMode.CreateNew,FileAccess.Write))using(var w=new StreamWriter(f,new UTF8Encoding(false)))w.Write(json.Serialize(report));
            if(report.ContainsKey("bodies"))try{WriteCutsCsv(Path.ChangeExtension(output,"csv"),report["bodies"]);}catch(Exception e){Console.Error.WriteLine("Diagnostic CSV write failed; JSON preserved: "+e.ToString());exit=1;}
        }
        Console.WriteLine(report["status"]+" | "+output);return exit;
    }
}
