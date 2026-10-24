using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public class FoldedSignatureV08 {
    public string bodyName;public int faces,edges,vertices,planes,cylinders;public double[] massPropertiesDensityOne;public string[] activeSheetMetalFeatures,activeBendFeatures;
    public bool SameGeometryAndBends(FoldedSignatureV08 other){
        if(other==null||bodyName!=other.bodyName||faces!=other.faces||edges!=other.edges||vertices!=other.vertices||planes!=other.planes||cylinders!=other.cylinders||!activeSheetMetalFeatures.SequenceEqual(other.activeSheetMetalFeatures)||!activeBendFeatures.SequenceEqual(other.activeBendFeatures)||massPropertiesDensityOne.Length!=other.massPropertiesDensityOne.Length)return false;
        return massPropertiesDensityOne.Select((x,i)=>Math.Abs(x-other.massPropertiesDensityOne[i])<=Math.Max(1e-12,Math.Abs(x)*1e-10)).All(x=>x);
    }
    public bool SameFlatGeometry(FoldedSignatureV08 other){
        if(other==null||faces!=other.faces||edges!=other.edges||vertices!=other.vertices||planes!=other.planes||cylinders!=other.cylinders||massPropertiesDensityOne.Length!=other.massPropertiesDensityOne.Length)return false;
        return massPropertiesDensityOne.Select((x,i)=>Math.Abs(x-other.massPropertiesDensityOne[i])<=Math.Max(1e-12,Math.Abs(x)*1e-10)).All(x=>x);
    }
}
public partial class ManufacturingSolidWorksV13 {
    string selectedFoldedConfiguration;FoldedSignatureV08 sourceFoldedSignature;
    static readonly string[] BendTypesV08={"OneBend","UiBend","SketchedBend","EdgeFlange"};
    FoldedSignatureV08 Signature(Body2 body){
        var features=ManufacturingGeometryV01.Items(body.GetFeatures()).Cast<Feature>().ToArray();var faces=ManufacturingGeometryV01.Items(body.GetFaces()).Cast<Face2>().ToArray();
        var mass=ManufacturingGeometryV01.Items(body.GetMassProperties(1d)).Select(Convert.ToDouble).ToArray();if(mass.Length==0||mass.Any(v=>!NV.Finite(v)))throw new InvalidOperationException("Native folded body mass properties unavailable; no geometry preservation claim");
        return new FoldedSignatureV08{bodyName=body.Name,faces=body.GetFaceCount(),edges=body.GetEdgeCount(),vertices=body.GetVertexCount(),planes=faces.Count(f=>((Surface)f.GetSurface()).IsPlane()),cylinders=faces.Count(f=>((Surface)f.GetSurface()).IsCylinder()),massPropertiesDensityOne=mass,
            activeSheetMetalFeatures=features.Where(f=>f.GetTypeName2()=="SheetMetal"&&!f.IsSuppressed()).Select(f=>f.Name).OrderBy(n=>n,StringComparer.Ordinal).ToArray(),activeBendFeatures=features.Where(f=>BendTypesV08.Contains(f.GetTypeName2())&&!f.IsSuppressed()).Select(f=>f.Name).OrderBy(n=>n,StringComparer.Ordinal).ToArray()};
    }
    bool? Suppressed(Feature feature,string name){if(feature==null)return null;var values=ManufacturingGeometryV01.Items(feature.IsSuppressed2((int)swInConfigurationOpts_e.swSpecifyConfiguration,new[]{name}));return values.Length==1?(bool?)Convert.ToBoolean(values[0]):null;}
    List<ConfigurationStateV08> ConfigurationTable(ModelDoc2 copy,Feature flat,FoldedSignatureV08 source){
        var list=new List<ConfigurationStateV08>();
        foreach(string name in ManufacturingGeometryV01.Items(copy.GetConfigurationNames()).Cast<string>()){
            var row=new ConfigurationStateV08{name=name};list.Add(row);
            try{var cfg=copy.GetConfigurationByName(name) as Configuration;if(cfg==null||cfg.Name!=name)throw new InvalidOperationException("Configuration enumeration/lookup name differs");row.derived=cfg.IsDerived();var parent=cfg.GetParent();row.parent=parent==null?null:parent.Name;row.type=cfg.Type;row.needsRebuild=cfg.NeedsRebuild;
                row.boundFlatSuppressed=Suppressed(flat,name);
                if(source!=null){
                    Func<string,bool> active=featureName=>{var f=((PartDoc)copy).FeatureByName(featureName) as Feature;bool? state=Suppressed(f,name);row.featureStates.Add(new{name=featureName,suppressed=state});return state==false;};
                    var sheetStates=source.activeSheetMetalFeatures.Select(active).ToArray();var bendStates=source.activeBendFeatures.Select(active).ToArray();
                    row.sheetMetalActive=sheetStates.Length>0&&sheetStates.All(state=>state);
                    row.sourceBendsActive=bendStates.All(state=>state);
                }
            }catch(Exception ex){row.errors.Add(ex.ToString());}
        }
        return list;
    }
    void PrepareFoldedCopy(ModelDoc2 copy,Feature flat,string path,string bodyName){
        CopyOnly(copy);Activate(copy);
        string sourceName=(string)current["sourceConfigurationApiName"];
        if(sourceFoldedSignature==null)throw new InvalidOperationException("SAVED_SNAPSHOT_FOLDED_SIGNATURE_REQUIRED");
        var table=ConfigurationTable(copy,flat,sourceFoldedSignature);current["copyConfigurations"]=table;current["copyConfigurationNameEvidence"]=table.Select(r=>ManufacturingConfigurationV08.Evidence(r.name)).ToArray();
        var candidates=ManufacturingConfigurationV08.Order(sourceName,table);
        if(selectedFoldedConfiguration!=null)candidates=candidates.Where(r=>r.name==selectedFoldedConfiguration).ToList();
        if(candidates.Count==0)throw new InvalidOperationException("NO_FOLDED_API_CONFIGURATION: no enumerated configuration has bound FlatPattern suppressed, SheetMetal and source bend features active");
        var operations=(List<object>)current["configurationOperations"];
        var failures=new List<string>();
        foreach(var candidate in candidates){
            var operation=ManufacturingConfigurationV08.Activate(candidate.name,table.Select(r=>r.name),()=>copy.ConfigurationManager.ActiveConfiguration.Name,n=>SourceCall("Activate owned folded configuration: "+n,()=>copy.ShowConfiguration2(n)));operations.Add(operation);
            if(!operation.success){failures.Add(candidate.name+": "+operation.error);continue;}
            if(!flat.IsSuppressed()||!SourceCall("Rebuild owned folded candidate",()=>copy.EditRebuild3())){failures.Add(candidate.name+": folded state/rebuild not established");continue;}
            var body=One(copy);if(body.Name!=bodyName||!body.IsSheetMetal()){failures.Add(candidate.name+": isolated body identity differs");continue;}
            var signature=Signature(body);current["lastFoldedCopySignature"]=signature;
            if(!sourceFoldedSignature.SameGeometryAndBends(signature)){failures.Add(candidate.name+": source geometry or active bend features differ after isolation");continue;}
            selectedFoldedConfiguration=candidate.name;config=candidate.name;
            current["chosenFoldedConfigurationApiName"]=config;current["configurationChoiceReason"]=config==sourceName?"Exact source API configuration already represents verified folded state; no extra configuration needed":"Enumerated folded state with source geometry and native bends preserved; no name suffix inference";
            current["foldedState"]=new{path,configurationApiName=config,bodyName,solidBodies=1,flatPatternSuppressed=true,nativeBendsPreserved=true,geometryCheck="Native feature/topology/mass-property preservation diagnostics, not full BREP certification"};
            Save(copy,path);return;
        }
        current["foldedCandidateFailures"]=failures;throw new InvalidOperationException("FOLDED_CONFIGURATION_NOT_VERIFIED: "+String.Join(" | ",failures));
    }
}
