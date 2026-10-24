using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public sealed class ManufacturingDxfPreferencesV05 : IDisposable {
    readonly Func<int,bool> get;readonly Action<int,bool> set;readonly List<object> audit;
    readonly List<KeyValuePair<int,bool>> previous=new List<KeyValuePair<int,bool>>();
    public ManufacturingDxfPreferencesV05(SldWorks sw,bool exact,List<object> records):this(k=>sw.GetUserPreferenceToggle(k),(k,v)=>sw.SetUserPreferenceToggle(k,v),exact,records){}
    public ManufacturingDxfPreferencesV05(Func<int,bool> getter,Action<int,bool> setter,bool exact,List<object> records){
        get=getter;set=setter;audit=records;
        try{Set((int)swUserPreferenceToggle_e.swDxfExportSplinesAsSplines,exact);Set((int)swUserPreferenceToggle_e.swDxfEndPointMerge,false);Set((int)swUserPreferenceToggle_e.swDxfMapping,true);}catch{Dispose();throw;}
    }
    void Set(int key,bool requested){bool old=get(key);previous.Add(new KeyValuePair<int,bool>(key,old));if(old!=requested)set(key,requested);bool observed=get(key);audit.Add(new{operation="SET_EXPORT_TOGGLE",preference=key,previousValue=old,requestedValue=requested,observedValue=observed});if(observed!=requested)throw new InvalidOperationException("DXF export preference readback differs: "+key);}
    public void Dispose(){var errors=new List<string>();for(int i=previous.Count-1;i>=0;i--){var p=previous[i];try{set(p.Key,p.Value);bool observed=get(p.Key);audit.Add(new{operation="RESTORE_EXPORT_TOGGLE",preference=p.Key,expectedValue=p.Value,observedValue=observed,restored=observed==p.Value});if(observed!=p.Value)throw new InvalidOperationException("Readback differs");}catch(Exception ex){errors.Add(p.Key+": "+ex.Message);}}previous.Clear();if(errors.Count>0)throw new InvalidOperationException("DXF_EXPORT_PREFERENCE_RESTORE_FAILED: "+String.Join(" | ",errors));}
}
