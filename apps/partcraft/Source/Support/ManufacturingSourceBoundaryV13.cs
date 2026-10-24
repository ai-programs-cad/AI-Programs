using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
public sealed class SourceBoundaryStateV13 {
    public bool dirty;public int updateStamp;public string path,configuration;
    public static SourceBoundaryStateV13 Read(ModelDoc2 doc){return new SourceBoundaryStateV13{dirty=doc.GetSaveFlag(),updateStamp=doc.GetUpdateStamp(),path=doc.GetPathName(),configuration=doc.ConfigurationManager.ActiveConfiguration.Name};}
    public bool Same(SourceBoundaryStateV13 other){return other!=null&&dirty==other.dirty&&updateStamp==other.updateStamp&&ManufacturingContextV02.SamePath(path,other.path)&&configuration==other.configuration;}
}
public partial class ManufacturingSolidWorksV13 {
    string savedSnapshotPath;SourceBoundaryStateV13 sourceBoundaryBaseline;
    T SourceCall<T>(string operation,Func<T> call){ ManufacturingAgent.Boundary("COM: "+operation);
        var before=SourceBoundaryStateV13.Read(original);var entry=new Dictionary<string,object>{{"operation",operation},{"before",before}};
        ProductionReopenChecksV13.History(current,"sourceBoundaryCalls").Add(entry);
        if(!sourceBoundaryBaseline.Same(before)||before.dirty){entry["firstChangeAlreadyPresentBeforeCall"]=true;if(!current.ContainsKey("firstSourceStateChange"))current["firstSourceStateChange"]=entry;throw new InvalidOperationException("SOURCE_STATE_CHANGED_BEFORE_CALL: "+operation);}
        try{return ManufacturingPerformanceV13.Measure((string)current["bodyName"],"COM_BOUNDARY: "+operation,call);}
        catch(Exception ex){entry["callException"]=ex.ToString();throw;}
        finally{var after=SourceBoundaryStateV13.Read(original);entry["after"]=after;entry["stateUnchanged"]=before.Same(after);
            if(!before.Same(after)||after.dirty){if(!current.ContainsKey("firstSourceStateChange"))current["firstSourceStateChange"]=entry;throw new InvalidOperationException("SOURCE_STATE_CHANGED_DURING_CALL: "+operation);}}
    }
    void SourceCall(string operation,Action call){SourceCall<object>(operation,()=>{call();return null;});}
    // Cleanup and preference restoration must still execute after a safety stop.
    // Observe them without preventing restoration or masking its own exception.
    void ObserveSourceCleanup(string operation,Action call){
        var before=SourceBoundaryStateV13.Read(original);var entry=new Dictionary<string,object>{{"operation",operation},{"before",before}};
        ProductionReopenChecksV13.History(current,"sourceBoundaryCalls").Add(entry);
        try{call();}finally{var after=SourceBoundaryStateV13.Read(original);entry["after"]=after;entry["stateUnchanged"]=before.Same(after);if((!before.Same(after)||after.dirty)&&!current.ContainsKey("firstSourceStateChange"))current["firstSourceStateChange"]=entry;}
    }
}
