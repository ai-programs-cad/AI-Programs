using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
static class UiRegressionChecks {
 public static void Run(DxfLauncher form){
  RunProgress.Test();ExportTimingJournal.Test();
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var type=typeof(DxfLauncher);var progress=(RunProgress)type.GetField("progress",flags).GetValue(form);
  var baseline=type.GetMethod("BaselineProgress",flags);var done=type.GetMethod("BaselineDone",flags);var render=type.GetMethod("RenderProgress",flags);
  baseline.Invoke(form,new object[]{"END ONE_GROUP_EXPORT_OPTIONS_5 12.345s"});done.Invoke(form,new object[]{2});
  if(!progress.Done[2]||Math.Abs(progress.Seconds[2]-12.345)>0.00001)throw new Exception("Measured native duration overwritten");
  done.Invoke(form,new object[]{0});if(progress.Checklist().Contains("-1"))throw new Exception("Unknown time exposed");
  string language=LanguagePreferences.Ui;try{
   LanguagePreferences.Ui="en";render.Invoke(form,null);LocalizedUi.Apply(form);
   var labels=new[]{"stageList","eta","detailLabel"}.Select(n=>(Label)type.GetField(n,flags).GetValue(form)).ToArray();int changes=0;foreach(var l in labels)l.TextChanged+=(s,e)=>changes++;
   for(int i=0;i<20;i++){render.Invoke(form,null);LocalizedUi.Apply(form);}
   if(changes!=0)throw new Exception("Static progress text repainted due to language oscillation: "+changes);
   var status=(Label)type.GetField("status",flags).GetValue(form);var detail=(Label)type.GetField("detailLabel",flags).GetValue(form);if(status.Bounds.IntersectsWith(detail.Bounds))throw new Exception("Duration/status overlaps step heading");
  }finally{LanguagePreferences.Ui=language;}
 }
}
