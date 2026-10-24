using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class LanguagePreferences {
 public static string Ui="ru",Documents="ru";
 static string FilePath {get{return AppStorage.FilePath("languages.json");}}
 static LanguagePreferences(){try{if(File.Exists(FilePath)){var p=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(FilePath));if(p.ContainsKey("interface")&&LanguageCatalog.Valid(p["interface"]))Ui=p["interface"];if(p.ContainsKey("documents")&&LanguageCatalog.Valid(p["documents"]))Documents=p["documents"];}}catch{}}
 public static void Save(){File.WriteAllText(FilePath,new JavaScriptSerializer().Serialize(new Dictionary<string,string>{{"interface",Ui},{"documents",Documents}}));}
}
static class LocalizedUi {
 class Value {public string source,last,language;}
 static readonly string[] orderedKeys=LanguageCatalog.Keys.OrderByDescending(k=>k.Length).ToArray();
 static Dictionary<Control,Value> values=new Dictionary<Control,Value>();
 public static string T(string text){
  string result=LanguageCatalog.T(text,LanguagePreferences.Ui);if(result!=text||LanguagePreferences.Ui=="ru")return result;
  return String.Join(System.Environment.NewLine,text.Replace("\r\n","\n").Split('\n').Select(Line));
 }
 static string Line(string line){
  var sheet=System.Text.RegularExpressions.Regex.Match(line,@"^(?:Profiles\+h|Нарезка профилей): лист (\d+), позиций (\d+)$");if(sheet.Success)return LanguageCatalog.T("Нарезка профилей",LanguagePreferences.Ui)+": "+LanguageCatalog.T("Лист",LanguagePreferences.Ui)+" "+sheet.Groups[1].Value+", "+LanguageCatalog.T("Позиций",LanguagePreferences.Ui)+" "+sheet.Groups[2].Value;
  var position=System.Text.RegularExpressions.Regex.Match(line,@"^Экспорт группы (\d+) из (\d+) — (.*)$");
  if(position.Success)return LanguageCatalog.T("Экспорт позиции",LanguagePreferences.Ui)+" "+position.Groups[1].Value+" / "+position.Groups[2].Value+" — "+position.Groups[3].Value;
  string code=LanguagePreferences.Ui,direct=LanguageCatalog.T(line,code);if(direct!=line)return direct;
  var elapsed=System.Text.RegularExpressions.Regex.Match(line,@"^Прошло (.+); завершено (\d+) из 8 этапов \((\d+)%\)$");if(elapsed.Success)return LanguageCatalog.T("Прошло ",code)+elapsed.Groups[1].Value+"; "+LanguageCatalog.T("Этапы",code)+": "+elapsed.Groups[2].Value+"/8 ("+elapsed.Groups[3].Value+"%)";
  var finished=System.Text.RegularExpressions.Regex.Match(line,@"^Завершено за (.+) с; exit=(\d+)$");if(finished.Success)return LanguageCatalog.T("Завершено за ",code)+finished.Groups[1].Value+" s — "+LanguageCatalog.T(finished.Groups[2].Value=="2"?"Выполнено":"Завершено с ошибкой",code);
  var body=System.Text.RegularExpressions.Regex.Match(line,@"^Экспорт тела (\d+) из (\d+): (.*), граней: (\d+)$");if(body.Success)return LanguageCatalog.T("Экспорт тела ",code)+body.Groups[1].Value+"/"+body.Groups[2].Value+": "+body.Groups[3].Value+" ("+LanguageCatalog.T("Грани",code)+": "+body.Groups[4].Value+")";
  var match=System.Text.RegularExpressions.Regex.Match(line,@"^([^\p{L}\p{N}]*)(.*)$");string prefix=match.Groups[1].Value,content=match.Groups[2].Value;
  if(content.StartsWith("Сейчас: "))return prefix+LanguageCatalog.T("Сейчас",code)+": "+Line(content.Substring(8));
  foreach(string key in orderedKeys)if(key.Length>3&&content.StartsWith(key,StringComparison.Ordinal))return prefix+LanguageCatalog.T(key,code)+content.Substring(key.Length);
  return line;
 }
 public static void Apply(Control root){
  var status=root.Tag as RawJobStatus;if(status!=null){string rendered=status.Render();if(root.Text!=rendered)root.Text=rendered;return;}
  var raw=root.Tag as RawJobLog;if(raw!=null&&root is TextBox){string rendered=JobDisplay.Log(raw.Text);if(root.Text!=rendered)root.Text=rendered;return;}

  if(root is Label||root is Button||root is Form||root is TextBox){
   bool editable=root is TextBox;
   if(!editable||((TextBox)root).ReadOnly){
    Value entry;if(!values.TryGetValue(root,out entry)){entry=new Value{source=root.Text,last=root.Text};values[root]=entry;}
    if(root.Text!=entry.last)entry.source=root.Text;
    if(root.Text!=entry.last||entry.language!=LanguagePreferences.Ui){string translated=T(entry.source);if(root.Text!=translated)root.Text=translated;entry.last=translated;entry.language=LanguagePreferences.Ui;}
   }
  }
  foreach(Control c in root.Controls)Apply(c);
 }
 public static DialogResult Show(string text,string caption){return MessageBox.Show(T(text),T(caption));}
 public static DialogResult Show(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon){return MessageBox.Show(T(text),T(caption),buttons,icon);}
}
