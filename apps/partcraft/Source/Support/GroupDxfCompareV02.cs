using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Web.Script.Serialization;
public static class GroupDxfCompareV02 {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=100};
    static Dictionary<string,object> Obj(object v){return (Dictionary<string,object>)v;}static object[] Arr(object v){return v==null?new object[0]:((Array)v).Cast<object>().ToArray();}
    static string Text(Dictionary<string,object> v,string key){object item;return v.TryGetValue(key,out item)?Convert.ToString(item):null;}
    static string InsideFile(string root,string file){if(!ManufacturingWorkspaceV13.Inside(root,file)||!File.Exists(file)||!String.Equals(Path.GetExtension(file),".dxf",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("DXF outside explicitly supplied report package: "+file);return file;}
    static object Layers(IEnumerable<GE> entities){return entities.GroupBy(e=>e.layer).ToDictionary(g=>g.Key,g=>g.Count());}
    static bool NoDuplicates(List<GE> entities){var id=new GT{a=1,d=1};for(int i=0;i<entities.Count;i++)for(int j=i+1;j<entities.Count;j++){var bound=DxfGeometryV02.Bound(entities[i],entities[j],id);if(bound.HasValue&&bound.Value.Eq(0))return false;}return true;}
    static string Hash(string path){return ManufacturingSourceFilesV03.Hash(path,"OFFLINE_DXF_GEOMETRY");}
    public static object CompareAndName(string groupReport,string controlReport,string output){
        var clock=Stopwatch.StartNew();if(Directory.Exists(output)||File.Exists(output))throw new InvalidOperationException("Comparison output exists; overwrite prohibited");Directory.CreateDirectory(output);
        var group=Obj(Json.DeserializeObject(File.ReadAllText(groupReport,Encoding.UTF8)));var control=Obj(Json.DeserializeObject(File.ReadAllText(controlReport,Encoding.UTF8)));
        string groupRoot=Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(groupReport))).FullName,controlRoot=Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(controlReport))).FullName;
        var bindings=Arr(group["flatPatternBindings"]).Select(Obj).ToArray();var refs=Arr(control["sheetBodyExports"]).Select(Obj).ToDictionary(r=>Text(r,"bodyName"),StringComparer.Ordinal);
        var physicalRows=Arr(control["rows"]).Select(Obj).ToDictionary(r=>Text(r,"body"),StringComparer.Ordinal);var hashes=Arr(control["productionFiles"]).Select(Obj).ToDictionary(r=>Text(r,"path"),r=>Text(r,"sha256"),StringComparer.OrdinalIgnoreCase);
        var records=new List<Dictionary<string,object>>();var sourceHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var names=new HashSet<string>(StringComparer.Ordinal);
        bool nativeEvidence=group.ContainsKey("protectedDocumentsUnchanged")&&(bool)group["protectedDocumentsUnchanged"]&&Convert.ToInt32(group["nativeCallCount"])==1&&Arr(group["nativeCalls"]).Length==1&&Convert.ToInt32(Obj(Arr(group["nativeCalls"])[0])["sheetMetalOptions"])==5&&!(bool)Obj(Arr(group["nativeCalls"])[0])["exportToSingleFile"]&&(bool)Obj(Arr(group["nativeCalls"])[0])["returned"];
        foreach(var file in Arr(group["files"]).Select(Obj)){
            string path=InsideFile(groupRoot,Text(file,"path"));var row=new Dictionary<string,object>{{"groupDxf",path},{"status","NEEDS_REVIEW"},{"productionReady",false}};records.Add(row);var timer=Stopwatch.StartNew();
            try{
                string sourceHash=Hash(path);sourceHashes[path]=sourceHash;if(sourceHash!=Text(file,"sha256"))throw new InvalidOperationException("Group DXF hash differs from original native report");
                var hints=bindings.Where(b=>Path.GetFileName(path).StartsWith(Text(b,"flatPattern")+" - ",StringComparison.Ordinal)).ToArray();if(hints.Length!=1)throw new InvalidOperationException("Native filename feature token is not uniquely linked to a verified binding; no name guessed");
                var binding=hints[0];string body=Text(binding,"bodyName");row["bodyName"]=body;row["nativeBinding"]=binding;if(!refs.ContainsKey(body)||!physicalRows.ContainsKey(body))throw new InvalidOperationException("Body absent from control");var reference=refs[body];
                if(!nativeEvidence||!(bool)binding["faceOwnerVerified"]||Text(binding,"cutList")!=Text(physicalRows[body],"cutList")||Text(reference,"publicationStatus")!="PORTABLE_VERIFIED")throw new InvalidOperationException("Native body/CutList/control verification evidence incomplete");
                string cutPath=InsideFile(controlRoot,Text(reference,"exactDxfPath")),bendPath=InsideFile(controlRoot,Text(reference,"bendDxfPath"));foreach(string p in new[]{cutPath,bendPath}){sourceHashes[p]=Hash(p);if(!hashes.ContainsKey(p)||hashes[p]!=sourceHashes[p])throw new InvalidOperationException("Control DXF hash does not match successful package manifest: "+p);}
                var cut=DxfGeometryV02.Read(cutPath);var bend=DxfGeometryV02.Read(bendPath);var actual=DxfGeometryV02.Read(path);var combined=cut.entities.Concat(bend.entities).ToList();row["nativeStyleTables"]=actual.nativeStyleTables;row["entityStyles"]=actual.entities.Select(e=>new{index=e.index,handle=e.handle,type=e.rawType,layer=e.layer,colorCode=e.entityColor,linetype=e.entityLinetype,inheritance="256/BYLAYER uses layer definition; 0/BYBLOCK requires block context"}).ToArray();
                var match=DxfGeometryV02.Match(combined,actual.entities);row["searchedCandidates"]=match.candidates;row["matchingReason"]=match.reason;row["cutReference"]=cutPath;row["bendReference"]=bendPath;
                if(!match.matched)throw new InvalidOperationException(match.reason+"; arbitrary non-axis rotations, changed knot bases or ARC/SPLINE conversions remain unsupported rather than DIFFERENT");
                row["transformReferenceToGroup"]=match.transform.Evidence();row["wholeCurveBoundMmApprox"]=match.maximum.Number;row["wholeCurveBoundMmExactRational"]=match.maximum.ToString();row["geometricToleranceMm"]=.001;
                row["boundMethod"]="Exact rational decimal arithmetic. LINE endpoints bound complete segments. ARC center/radius/angular span Lipschitz bound uses rational pi upper bound. Positive-weight SPLINEs: identical normalized knot/weight basis, convex hull of control-point differences; reversed parameterization considered. No finite point sampling.";
                var cutIndices=match.map.Take(cut.entities.Count).ToArray();var bendIndices=match.map.Skip(cut.entities.Count).ToArray();var actualCut=cutIndices.Select(i=>actual.entities[i]).ToList();var actualBend=bendIndices.Select(i=>actual.entities[i]).ToList();
                bool refClosed,actualClosed;int refLoops,actualLoops;List<int[]> refComponents,actualComponents;
                row["referenceCutTopology"]=DxfGeometryV02.Topology(cut.entities,out refClosed,out refLoops,out refComponents);row["groupCutTopology"]=DxfGeometryV02.Topology(actualCut,out actualClosed,out actualLoops,out actualComponents);
                bool sameComponents=refComponents.Select(c=>String.Join(",",c.OrderBy(i=>i))).OrderBy(s=>s).SequenceEqual(actualComponents.Select(c=>String.Join(",",c.OrderBy(i=>i))).OrderBy(s=>s));
                int nativeLoops=Convert.ToInt32(reference["nativeTopologyContours"]),nativeBends=Convert.ToInt32(reference["nativeDrawingBendLineCount"]);
                row["matchedCutEntities"]=cutIndices.Length;row["matchedBendEntities"]=bendIndices.Length;row["nativeBendLineCount"]=nativeBends;row["nativeCutContourCount"]=nativeLoops;
                row["allCutAndBendEntitiesBijection"]=match.map.Distinct().Count()==actual.entities.Count;row["cutLayers"]=Layers(actualCut);row["bendLayers"]=Layers(actualBend);
                row["cutEntityMap"]=cutIndices.Select((j,i)=>new{referenceIndex=i,groupIndex=j,referenceRepresentation=cut.entities[i].rawType,groupRepresentation=actual.entities[j].rawType,boundExactRational=match.bounds[i].ToString()}).ToArray();
                row["preservedClosedComponents"]=refComponents.Select(component=>new{referenceCutIndices=component,groupEntityIndices=component.Select(i=>cutIndices[i]).ToArray(),wholeCurveBoundsExact=component.Select(i=>match.bounds[i].ToString()).ToArray()}).ToArray();
                row["bendEntityMap"]=bendIndices.Select((j,i)=>new{referenceIndex=i,groupIndex=j,boundExactRational=match.bounds[cut.entities.Count+i].ToString()}).ToArray();
                bool unique=NoDuplicates(actual.entities);row["noExactDuplicateWholeCurves"]=unique;
                if(!refClosed||!actualClosed||refLoops!=nativeLoops||actualLoops!=nativeLoops||!sameComponents||bendIndices.Length!=nativeBends||!unique)throw new InvalidOperationException("Contour closure/component bijection, native bend count or duplicate check failed");
                if(body.IndexOfAny(Path.GetInvalidFileNameChars())>=0||!names.Add(body))throw new InvalidOperationException("Unsafe or duplicate exact output body name");
                row["scaleOneToOneVerified"]=true;row["holeContoursPreservedByFullCurveAndComponentMapping"]=true;row["internalContourCountFromSingleNativeSheetReference"]=nativeLoops-1;
                row["status"]="EXACT_AND_BEND_GEOMETRY_MATCH_IN_TOLERANCE";row["fileOwnershipEvidence"]="FixedFace2.GetBody/CutList native binding + native filename feature token + complete bounded geometry and topology comparison, not filename or counts alone";
                row["upDownDirectionVerified"]=false;row["camAccuracyCertification"]="NEEDS_REVIEW; this compares exact CUT+BEND geometry, not CAM approximation";
                row["layerScheme0UpDownObserved"]=actualCut.All(e=>e.layer=="0")&&actualBend.All(e=>String.Equals(e.layer,"UP",StringComparison.OrdinalIgnoreCase)||String.Equals(e.layer,"DOWN",StringComparison.OrdinalIgnoreCase));
                row["layerReview"]="Actual layer names recorded; UP/DOWN physical bend directions not inferred from layer names";
            }catch(Exception ex){row["reason"]=ex.Message;row["error"]=ex.ToString();}
            row["elapsedSeconds"]=timer.Elapsed.TotalSeconds;
        }
        // Verify all inputs before publishing any named copies. Never rename/overwrite native inputs.
        foreach(var entry in sourceHashes)if(Hash(entry.Key)!=entry.Value)throw new InvalidOperationException("Input changed during offline comparison: "+entry.Key);
        foreach(var row in records.Where(r=>(string)r["status"]=="EXACT_AND_BEND_GEOMETRY_MATCH_IN_TOLERANCE")){
            string target=Path.Combine(output,(string)row["bodyName"]+".dxf");using(var input=new FileStream((string)row["groupDxf"],FileMode.Open,FileAccess.Read,FileShare.Read))using(var dest=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(dest);if(Hash(target)!=sourceHashes[(string)row["groupDxf"]])throw new InvalidOperationException("Named output hash differs from verified native DXF");row["namedDxf"]=target;
        }
        var summary=new{program="GroupDxfCompareV02",sourceGroupReport=groupReport,controlReport,elapsedSeconds=clock.Elapsed.TotalSeconds,productionReadinessCertified=false,camStatus="NEEDS_REVIEW",inputFilesUnchanged=true,
            geometryMatched=records.Count(r=>(string)r["status"]=="EXACT_AND_BEND_GEOMETRY_MATCH_IN_TOLERANCE"),total=records.Count,files=records,
            limitations=new[]{"Unit-scale axis rotations searched; arbitrary rotations, general changed knot bases, split curves and nontrivial ARC/SPLINE conversions require review rather than DIFFERENT","Closure checks use 0.000001 mm endpoint connectivity; this is not a general self-intersection or 3D BREP certificate","UP/DOWN layer directions and CAM approximation accuracy remain unverified"}};
        File.WriteAllText(Path.Combine(output,"geometry-comparison.json"),Json.Serialize(summary),new UTF8Encoding(false));
        var csv=new StringBuilder("Body;Status;CUT;BEND;Contours;WholeCurveBoundMm;NamedDXF\r\n");foreach(var row in records){Func<string,string> value=k=>row.ContainsKey(k)?Convert.ToString(row[k],System.Globalization.CultureInfo.InvariantCulture):"";csv.Append(String.Join(";",new[]{value("bodyName"),value("status"),value("matchedCutEntities"),value("matchedBendEntities"),value("nativeCutContourCount"),value("wholeCurveBoundMmApprox"),value("namedDxf")}.Select(s=>"\""+s.Replace("\"","\"\"")+"\""))).Append("\r\n");}File.WriteAllText(Path.Combine(output,"geometry-comparison.csv"),csv.ToString(),new UTF8Encoding(true));return summary;
    }
    public static int Main(string[] args){if(args.SequenceEqual(new[]{"--self-test"}))return DxfGeometryTestsV02.Run();string group=null,control=null,output=null;try{for(int i=0;i<args.Length;i++){if(args[i]=="--group-report")group=args[++i];else if(args[i]=="--control-report")control=args[++i];else if(args[i]=="--output")output=args[++i];else throw new ArgumentException("Unknown argument");}if(group==null||control==null||output==null)throw new ArgumentException("--group-report --control-report --output required");var summary=Obj(Json.DeserializeObject(Json.Serialize(CompareAndName(group,control,output))));Console.WriteLine(Json.Serialize(summary));return Convert.ToInt32(summary["geometryMatched"])==Convert.ToInt32(summary["total"])?0:2;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
