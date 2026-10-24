using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using System.Web.Script.Serialization;
static class UiAcceptance {
 static IEnumerable<Control> All(Control c){yield return c;foreach(Control child in c.Controls)foreach(var item in All(child))yield return item;}
 public static void Run(Form form){
  var modules=(Form[])form.GetType().GetField("modules",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);if(modules.Length!=3)throw new Exception("Expected exactly DXF, Profiles and Faces modules");
  string old=LanguagePreferences.Ui;var failures=new List<string>();var select=form.GetType().GetMethod("SelectModule",BindingFlags.Instance|BindingFlags.NonPublic);
  string raw="@@PROFILE|3|Подключение к SolidWorks и проверка исходника\n@@PROFILE|18|Проверка профиля 6-1\n@@PROFILE|60|Profiles+h: лист 1, позиций 6\n@@PROFILE|100|Таблицы созданы. Исходная модель не изменена.\n@@RESULT|C:\\Example\\1111_5";
  foreach(string code in LanguageCatalog.Codes){LanguagePreferences.Ui=code;
   string rendered=JobDisplay.Log(raw);if(rendered.Contains("@@PROFILE")||rendered.Contains("@@RESULT")||!rendered.Contains(@"C:\Example\1111_5"))throw new Exception("Protocol/path presentation failed");
   if(code!="ru"&&code!="uk"&&System.Text.RegularExpressions.Regex.IsMatch(rendered,"[А-Яа-яЁё]"))failures.Add(code+": "+rendered);
   for(int i=0;i<3;i++){select.Invoke(form,new object[]{i});LocalizedUi.Apply(form);foreach(var c in All(form).Where(c=>c.Visible&&(c is Label||c is Button)))if(code!="ru"&&code!="uk"&&System.Text.RegularExpressions.Regex.IsMatch(c.Text,"[А-Яа-яЁё]"))failures.Add(code+" module="+i+": "+c.Text);}
   if(!JobDisplay.Elapsed(TimeSpan.FromSeconds(3723.4)).Contains("01:02:03"))throw new Exception("Elapsed hours dropped");
  }
  File.WriteAllText(AppStorage.FilePath("translation-audit.json"),new JavaScriptSerializer().Serialize(failures));
  LanguagePreferences.Ui=old;select.Invoke(form,new object[]{0});LocalizedUi.Apply(form);
  if(failures.Count>0)throw new Exception("UI translation audit failed: "+failures[0]);
  string test=AppStorage.FilePath("timing-fixture.json");File.WriteAllText(test,"{\"timings\":[{\"stage\":\"PREPARE_WORKSPACE\",\"elapsedSeconds\":2.5},{\"stage\":\"NATIVE_GROUP_TOTAL\",\"elapsedSeconds\":12.3},{\"stage\":\"CAM_ARCS_PROCESS_AND_CHECKS\",\"elapsedSeconds\":4.7}]}");var p=new RunProgress();JobDisplay.ImportTimings(p,test);if(p.Seconds[1]!=2.5||p.Seconds[2]!=12.3||p.Seconds[5]!=4.7||!p.Done[5])throw new Exception("Report timings not preserved");
 }
}
