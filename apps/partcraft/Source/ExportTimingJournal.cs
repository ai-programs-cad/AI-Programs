using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Web.Script.Serialization;

// This journal lives outside job-owned output, so cancellation cannot erase it.
class ExportTimingJournal {
 public class Entry {public string id,label,state="waiting";public DateTime? started;public double? seconds;}
 public readonly List<Entry> Entries=new List<Entry>();
 public string Source,Folder;public bool Running;public DateTime Started;
 static readonly Dictionary<string,string> Labels=new Dictionary<string,string>{
  {"SOURCE_INVENTORY","Чтение тел исходной модели"},{"PREPARE_WORKSPACE","Рабочая копия"},{"CONNECT_EXISTING_SOLIDWORKS","Подключение и проверка исходника"},
  {"CHECK_SAVED_REFERENCES","Проверка ссылок модели"},{"COPY_SAVED_WORKSPACE","Копирование модели"},
  {"OPEN_EXPERIMENTAL_COPY","Открытие рабочей модели"},{"ACTIVATE_EXPERIMENTAL_COPY","Подготовка окна модели"},
  {"CHECK_LIVE_REFERENCES","Проверка ссылок модели"},{"READ_COPY_REBUILD_STATUS","Проверка состояния модели"},
  {"BIND_FLAT_PATTERNS_TO_BODIES_AND_CUT_LIST","Связь развёрток с Cut List"},
  {"BIND/SNAPSHOT_BODIES_ONCE","Чтение тел рабочей копии"},{"BIND/COLLECT_FEATURES_MACRO_ORDER","Поиск развёрток"},
  {"BIND/BUILD_CUT_LIST_CACHE","Чтение папок Cut List"},{"BIND/FIXED_FACE_AND_CACHED_MEMBERSHIP","Связь развёрток с Cut List"},
  {"MODEL_SPACE_AND_FILE_CHECKS","Подготовка и проверка DXF"},{"CLOSE_WORKSPACE","Закрытие рабочей копии"},
  {"CAM_ARCS_PROCESS_AND_CHECKS","Замена сплайнов дугами и проверка контуров"},{"FINAL_SOURCE_CHECK","Контроль оригинала и отчёт"}
 };
 public ExportTimingJournal(){foreach(string id in new[]{"SOURCE_INVENTORY","PREPARE_WORKSPACE","CONNECT_EXISTING_SOLIDWORKS","COPY_SAVED_WORKSPACE","OPEN_EXPERIMENTAL_COPY","BIND/SNAPSHOT_BODIES_ONCE","BIND/COLLECT_FEATURES_MACRO_ORDER","BIND/BUILD_CUT_LIST_CACHE","BIND/FIXED_FACE_AND_CACHED_MEMBERSHIP","MODEL_SPACE_AND_FILE_CHECKS","CLOSE_WORKSPACE","CAM_ARCS_PROCESS_AND_CHECKS","FINAL_SOURCE_CHECK"})Get(id);}
 Entry Get(string id){var row=Entries.FirstOrDefault(e=>e.id==id);if(row!=null)return row;string label;Labels.TryGetValue(id,out label);row=new Entry{id=id,label=label??id};Entries.Add(row);return row;}
 public void Start(string source,string root){Source=source;Started=DateTime.UtcNow;Running=true;Folder=Path.Combine(root,Started.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(Folder);Save();}
 public void Apply(string line){
  if(line.StartsWith("@@BATCH|")){var p=line.Split('|');if(p.Length>=5&&p[3]=="BEGIN")Get("EXPORT_BATCH_"+p[1]).label="Экспорт позиции "+p[1]+" / "+p[2]+" — "+p[4];return;}
  if(line.StartsWith("BEGIN ")){var row=Get(line.Substring(6));row.state="running";row.started=DateTime.UtcNow;return;}
  var m=System.Text.RegularExpressions.Regex.Match(line,@"^(END|FAIL) (.+) ([0-9]+(?:\.[0-9]+)?)s$");
  if(m.Success){var row=Get(m.Groups[2].Value);row.state=m.Groups[1].Value=="END"?"complete":"stopped";row.seconds=Double.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture);}
 }
 public void Finish(bool success){Running=false;foreach(var row in Entries.Where(e=>e.state=="running")){row.seconds=Seconds(row);row.state=success?"complete":"stopped";}Save();}
 public double? Seconds(Entry row){return row.state=="running"&&row.started.HasValue?(double?)(DateTime.UtcNow-row.started.Value).TotalSeconds:row.seconds;}
 public string State(Entry row){return LocalizedUi.T(row.state=="running"?"Выполняется":row.state=="complete"?"Выполнено":row.state=="stopped"?"Остановлено":Folder!=null&&!Running?"Не выполнялся":"Ожидает");}
 public string Label(Entry row){return LocalizedUi.T(row.label);}
 public void Save(){if(Folder==null)return;
  var rows=Entries.Select(e=>new{id=e.id,stage=Label(e),state=e.state,seconds=Seconds(e)}).ToArray();
  File.WriteAllText(Path.Combine(Folder,"timings.json"),new JavaScriptSerializer().Serialize(new{source=Source,startedUtc=Started,running=Running,elapsedSeconds=(DateTime.UtcNow-Started).TotalSeconds,stages=rows,note="Nested stages overlap; durations must not be summed."}),new UTF8Encoding(true));
  Func<string,string> h=WebUtility.HtmlEncode;var html=new StringBuilder("<!doctype html><html><meta charset='utf-8'>"+(Running?"<meta http-equiv='refresh' content='2'>":"")+"<title>PartCraft</title><style>body{font:16px Segoe UI,Arial;margin:32px;color:#20354a;background:#f3f7fa}table{border-collapse:collapse;background:white;width:100%}td,th{padding:9px 14px;border-bottom:1px solid #dbe4eb;text-align:left}h1{font-size:24px}p{overflow-wrap:anywhere}</style><h1>PartCraft — "+h(LocalizedUi.T("Время по этапам"))+"</h1><p>"+h(Source)+"</p><table><tr><th>"+h(LocalizedUi.T("Этап"))+"</th><th>"+h(LocalizedUi.T("Состояние"))+"</th><th>"+h(LocalizedUi.T("Время, с"))+"</th></tr>");
  foreach(var row in Entries)html.Append("<tr><td>"+h(Label(row))+"</td><td>"+h(State(row))+"</td><td>"+(Seconds(row).HasValue?Seconds(row).Value.ToString("F1",CultureInfo.InvariantCulture):"—")+"</td></tr>");
  html.Append("</table><p>"+h(LocalizedUi.T("Время вложенных этапов не суммируется."))+"</p></html>");File.WriteAllText(Path.Combine(Folder,"timings.html"),html.ToString(),new UTF8Encoding(true));
 }
 public static void Test(){var j=new ExportTimingJournal();j.Apply("BEGIN BIND/COLLECT_FEATURES_MACRO_ORDER");var row=j.Entries.Single(e=>e.id=="BIND/COLLECT_FEATURES_MACRO_ORDER");if(row.state!="running"||!j.Seconds(row).HasValue)throw new Exception("Live stage timer missing");j.Apply("END BIND/COLLECT_FEATURES_MACRO_ORDER 12.345s");if(row.state!="complete"||row.seconds!=12.345)throw new Exception("Measured stage duration changed");j.Apply("@@BATCH|1|3|BEGIN|Panel (4)");j.Apply("BEGIN EXPORT_BATCH_1");j.Finish(false);if(j.Entries.Single(e=>e.id=="EXPORT_BATCH_1").state!="stopped")throw new Exception("Cancelled stage remains running");var saved=new ExportTimingJournal();saved.Start("fixture.sldprt",AppStorage.FilePath("TimingJournalTests"));saved.Apply("BEGIN COPY_SAVED_WORKSPACE");saved.Apply("END COPY_SAVED_WORKSPACE 2.500s");saved.Apply("BEGIN EXPORT_BATCH_1");saved.Save();saved.Finish(false);string data=File.ReadAllText(Path.Combine(saved.Folder,"timings.json"));if(!data.Contains("\"running\":false")||!data.Contains("\"state\":\"stopped\"")||!File.Exists(Path.Combine(saved.Folder,"timings.html")))throw new Exception("Cancelled run timing report missing");}
}
