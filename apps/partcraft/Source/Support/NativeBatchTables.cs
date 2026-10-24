using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
public static class NativeBatchTables {
    static object[] A(object v){return NativeWeldmentAdapter.Items(v);}
    public static object Read(ModelDoc2 model){
        var features=MacroBindingCoreV03.Collect(model.FirstFeature() as Feature,f=>f.GetNextFeature() as Feature,f=>f.GetFirstSubFeature() as Feature,f=>f.GetNextSubFeature() as Feature,f=>f.GetID(),10000);
        var rows=new List<object>();
        foreach(var f in features.Where(f=>f.GetTypeName2()=="CutListFolder")){
            ManufacturingAgent.Boundary("READ_NATIVE_CUT_LIST_PROPERTIES");
            var folder=f.GetSpecificFeature2() as BodyFolder;if(folder==null)throw new Exception("CUT_LIST_BODY_FOLDER_UNAVAILABLE: "+f.Name);
            var bodies=A(folder.GetBodies()).Cast<Body2>().Select(b=>b.Name).ToArray();if(bodies.Length==0)continue;
            var manager=f.CustomPropertyManager;var values=new Dictionary<string,object>(StringComparer.Ordinal);
            foreach(string name in A(manager.GetNames()).Select(Convert.ToString)){
                string raw,resolved;bool wasResolved,linked;int code=manager.Get6(name,true,out raw,out resolved,out wasResolved,out linked);
                values.Add(name,new{name,raw,resolved,wasResolved,linked,resultCode=code,api="ICustomPropertyManager.Get6 UseCached=true"});
            }
            rows.Add(new{folder=f.Name,bodies,physicalBodyCount=bodies.Length,properties=values,source="Native SolidWorks CutListFolder; all actual property names retained",missingValuesSynthesized=false,anglesRecomputed=false,geometricDirectionsUsedAsTableValues=false});
        }
        return rows;
    }
}
