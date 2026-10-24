using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
public static class BindingProfileV02 {
    sealed class Row {public string api;public int calls,failed;public double totalSeconds,maxSeconds;public string slowestContext;}
    static readonly Dictionary<string,Row> rows=new Dictionary<string,Row>();
    public static bool Enabled;public static void Reset(){rows.Clear();}
    public static T Call<T>(string api,string context,Func<T> action){
        if(!Enabled)return action();var sw=Stopwatch.StartNew();bool ok=false;
        try{T value=action();ok=true;return value;}finally{sw.Stop();Row row;if(!rows.TryGetValue(api,out row)){row=new Row{api=api};rows.Add(api,row);}row.calls++;if(!ok)row.failed++;double seconds=sw.Elapsed.TotalSeconds;row.totalSeconds+=seconds;if(seconds>=row.maxSeconds){row.maxSeconds=seconds;row.slowestContext=context;}}
    }
    public static object Snapshot(){return new{calls=rows.Values.Sum(r=>r.calls),apis=rows.Values.OrderByDescending(r=>r.totalSeconds).Select(r=>new{r.api,r.calls,r.failed,r.totalSeconds,r.maxSeconds,r.slowestContext}).ToArray(),nestedTimingPolicy="Individual binding calls; these times are contained in outer binding substage times"};}
    public static void Print(){foreach(var row in rows.Values.OrderByDescending(r=>r.totalSeconds).Take(8))Console.WriteLine("BIND API "+row.api+" calls="+row.calls+" total="+row.totalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s max="+row.maxSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"s context="+row.slowestContext);}
}
