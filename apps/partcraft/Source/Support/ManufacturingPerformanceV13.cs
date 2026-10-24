using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
public static class ManufacturingPerformanceV13 {
    static readonly object gate=new object();static readonly Stopwatch total=Stopwatch.StartNew();
    static readonly List<Dictionary<string,object>> records=new List<Dictionary<string,object>>();
    static readonly HashSet<string> planned=new HashSet<string>(),done=new HashSet<string>();
    static readonly List<Scope> active=new List<Scope>();static Scope stage;static Timer pulse;static bool enabled,finished;
    public static readonly string[] SheetSteps={"SOURCE_ENTRY","SOURCE_GUARD_AND_COPY","ISOLATED_BODY_NATIVE_FLAT_PATTERN","NATIVE_FLAT_TOPOLOGY_AND_SCALE_REFERENCE","EXPORT_NATIVE_CUT_GEOMETRY_OPTIONS_1","EXPORT_NATIVE_POLYLINE_CAM_SPLINE_TOGGLE_FALSE","EXPORT_NATIVE_BEND_LINES_OPTIONS_4","EXPORT_AND_VALIDATE_SEPARATE_NATIVE_BEND_DXF","RESTORE_COPY_FOLDED_AND_CREATE_NATIVE_DRAWING","CREATE_FOLDED_NATIVE_ISOMETRIC_FIRST","CREATE_NATIVE_FLAT_PATTERN_VIEW","EXPORT_PDF_OF_VALIDATED_DRAWING","PACK_AND_REOPEN_LINKED_EDITABLE_DRAWING","PUBLISH_PORTABLE","BODY_VERIFIED"};
    static string Key(string body,string name){return body+"|"+name;}
    public static int Percent(int completed,int count,bool complete){return count<=0?0:complete&&completed==count?100:Math.Min(99,(int)(100L*completed/count));}
    public static void Start(){lock(gate){enabled=true;finished=false;records.Clear();planned.Clear();done.Clear();active.Clear();stage=null;total.Restart();pulse=new Timer(_=>{lock(gate){Print();}},null,5000,5000);}}
    public static void Plan(IEnumerable<string> sheets,IEnumerable<string> plates){lock(gate){foreach(string body in sheets)foreach(string s in SheetSteps)planned.Add(Key(body,s));foreach(string body in plates)planned.Add(Key(body,"PLATE_DXF"));planned.Add(Key("PACKAGE","EXCEL"));planned.Add(Key("PACKAGE","FINAL_CHECKS"));Print();}}
    public static string Body{get{lock(gate){return active.Count==0?"PACKAGE":active.Last().body;}}}
    public sealed class Scope:IDisposable {
        internal string body,name,kind;internal readonly Stopwatch watch=Stopwatch.StartNew();internal bool ok,disposed;internal double childSeconds;
        internal Scope(string b,string n,string k){body=b;name=n;kind=k;}
        public void Success(){ok=true;}
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;watch.Stop();if(!enabled)return;active.Remove(this);double seconds=watch.Elapsed.TotalSeconds;
            if(active.Count>0)active.Last().childSeconds+=seconds;
            records.Add(new Dictionary<string,object>{{"body",body},{"stage",name},{"kind",kind},{"elapsedSeconds",seconds},{"exclusiveSeconds",Math.Max(0,seconds-childSeconds)},{"completed",ok}});
            if(ok&&planned.Contains(Key(body,name)))done.Add(Key(body,name));Print();}}
    }
    public static Scope Begin(string body,string name,string kind="operation"){lock(gate){var s=new Scope(body,name,kind);if(enabled){active.Add(s);Print();}return s;}}
    public static T Measure<T>(string body,string name,Func<T> action){using(var s=Begin(body,name)){T result=action();s.Success();return result;}}
    public static void Measure(string body,string name,Action action){Measure<object>(body,name,()=>{action();return null;});}
    public static void Stage(string body,string name){lock(gate){EndStage(true);if(name!="COMPLETE")stage=Begin(body,name,"stage");}}
    public static void EndStage(bool success){lock(gate){if(stage==null)return;if(success)stage.Success();stage.Dispose();stage=null;}}
    public static void Mark(string body,string name){lock(gate){if(planned.Contains(Key(body,name)))done.Add(Key(body,name));Print();}}
    static void Print(){if(!enabled)return;var s=active.LastOrDefault();string percent=planned.Count==0?"--":Percent(done.Count,planned.Count,finished).ToString();try{Console.WriteLine("["+percent+"%] body="+(s==null?"PACKAGE":s.body)+" stage="+(s==null?(finished?"FINISHED":"BETWEEN_STAGES"):s.name)+" stage="+(s==null?0:s.watch.Elapsed.TotalSeconds).ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"s elapsed="+total.Elapsed.TotalSeconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"s steps="+done.Count+"/"+planned.Count);}catch(IOException){}}
    public static object Snapshot(){lock(gate){return new{elapsedSeconds=total.Elapsed.TotalSeconds,completedSteps=done.Count,plannedSteps=planned.Count,progressPercentAtReportWrite=Percent(done.Count,planned.Count,finished),finalConsoleProgressAfterReportSave=true,progressMeaning="Completed planned processing steps, not time estimate or production certification",records=records.ToArray(),summary=records.GroupBy(r=>(string)r["stage"]).Select(g=>new{stage=g.Key,calls=g.Count(),elapsedSeconds=g.Sum(r=>(double)r["elapsedSeconds"]),exclusiveSeconds=g.Sum(r=>(double)r["exclusiveSeconds"]),failed=g.Count(r=>!(bool)r["completed"])}).ToArray(),timingPolicy="Stopwatch monotonic elapsed; nested inclusive times overlap; exclusive times subtract measured child scopes",heartbeatCallsCOM=false};}}
    public static void Finish(bool success){lock(gate){EndStage(false);finished=success;if(success)Mark("PACKAGE","FINAL_CHECKS");if(pulse!=null)pulse.Dispose();pulse=null;Print();enabled=false;
        foreach(var group in records.Where(r=>!((string)r["stage"]).StartsWith("COM_BOUNDARY:",StringComparison.Ordinal)).GroupBy(r=>(string)r["stage"]))Console.WriteLine("TIMING "+group.Key+": calls="+group.Count()+" total="+group.Sum(r=>(double)r["elapsedSeconds"]).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s (inclusive)");}}
    public static int Tests(){int failed=0;Action<bool> check=x=>{if(!x)failed++;};check(Percent(0,0,false)==0);check(Percent(5,10,false)==50);check(Percent(10,10,false)==99);check(Percent(10,10,true)==100);check(Percent(9,10,true)==90);int calls=0;try{Measure("test","throw",()=>{calls++;throw new InvalidOperationException();});}catch(InvalidOperationException){}check(calls==1);check(Measure("test","value",()=>42)==42);Console.WriteLine("Performance policy tests: "+(7-failed)+"/7 PASS (no COM)");return failed==0?0:1;}
}
