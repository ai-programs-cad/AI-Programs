using System;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
static class LayoutChecks {
 public static void Run(Form form){var type=form.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var select=type.GetMethod("SelectModule",flags);var modules=(Form[])type.GetField("modules",flags).GetValue(form);var old=form.ClientSize;
  foreach(var size in new[]{new Size(1000,660),new Size(1100,730),new Size(1366,768),new Size(1920,1000)}){
   form.ClientSize=size;form.PerformLayout();Application.DoEvents();
   for(int i=1;i<modules.Length;i++){select.Invoke(form,new object[]{i});form.PerformLayout();Application.DoEvents();var m=modules[i];m.AutoScrollPosition=Point.Empty;
    var button=m.Controls.OfType<Button>().Single(b=>b.Top>300);var journal=m.Controls.OfType<TextBox>().Single();
    if(!m.AutoScroll||button.Parent!=journal.Parent||button.Top<=journal.Bottom)throw new Exception("Result button not part of scrolling content");
    if(m.Controls.OfType<Panel>().Any(p=>p.Dock==DockStyle.Bottom))throw new Exception("Unexpected fixed result panel");
    m.ScrollControlIntoView(button);Application.DoEvents();var rect=button.RectangleToScreen(button.ClientRectangle);
    for(Control p=button.Parent;p!=null;p=p.Parent)if(!p.RectangleToScreen(p.ClientRectangle).Contains(rect))throw new Exception("Cannot scroll to full button: module="+i+" size="+size+" parent="+p.GetType().Name);
    var api=form.Controls.OfType<Button>().Single(b=>b.Text.Contains("API"));if(api.RectangleToScreen(api.ClientRectangle).IntersectsWith(rect))throw new Exception("Result button overlaps diagnostics");
    m.AutoScrollPosition=Point.Empty;
   }
  }form.ClientSize=old;select.Invoke(form,new object[]{0});
  var status=new RawJobStatus("Таблицы созданы в рабочей копии. Статус: NEEDS_REVIEW.",TimeSpan.FromSeconds(30.9));string previous=LanguagePreferences.Ui;LanguagePreferences.Ui="de";string de=status.Render();LanguagePreferences.Ui="en";string en=status.Render();LanguagePreferences.Ui=previous;if(en.Contains("Tabellen")||!en.Contains("30"))throw new Exception("Status language cached incorrectly");
 }
}
