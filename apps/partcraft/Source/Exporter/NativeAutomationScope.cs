using System;
using SolidWorks.Interop.sldworks;

// Only the sheet-metal worker uses this scope. Preserve the caller's state.
public sealed class NativeAutomationScope : IDisposable {
 readonly Func<bool> get; readonly Action<bool> set; readonly bool previous;
 public NativeAutomationScope(SldWorks sw):this(()=>sw.CommandInProgress,v=>sw.CommandInProgress=v){}
 public NativeAutomationScope(Func<bool> getter,Action<bool> setter){get=getter;set=setter;previous=get();try{set(true);if(!get())throw new InvalidOperationException("COMMAND_IN_PROGRESS_NOT_ENABLED");}catch{set(previous);throw;}}
 public void Dispose(){set(previous);if(get()!=previous)throw new InvalidOperationException("COMMAND_IN_PROGRESS_NOT_RESTORED");}
 public static void Test(){foreach(bool initial in new[]{false,true}){bool value=initial;try{using(var scope=new NativeAutomationScope(()=>value,v=>value=v)){if(!value)throw new Exception("Automation flag missing");throw new ApplicationException("fixture");}}catch(ApplicationException){}if(value!=initial)throw new Exception("Automation flag not restored after failure");}}
}
