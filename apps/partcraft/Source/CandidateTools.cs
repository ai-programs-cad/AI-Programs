using System;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
partial class UnifiedLauncher {
 void AddCandidateTools(){
  Text = "PartCraft Community";
  JobControl.RetryCleanup();
  var stop=new Button{Text=LocalizedUi.T("Прервать и удалить текущий результат"),Dock=DockStyle.Bottom,Height=36,ForeColor=System.Drawing.Color.Firebrick};Controls.Add(stop);stop.SendToBack();var stopTimer=new Timer{Interval=400};stopTimer.Tick+=(a,b)=>{stop.Enabled=JobControl.Running&&!JobControl.Stopping;stop.Text=LocalizedUi.T(JobControl.Stopping?"Остановка и очистка…":"Прервать и удалить текущий результат");};stopTimer.Start();FormClosed+=(a,b)=>{stopTimer.Stop();stopTimer.Dispose();};
  stop.Click+=async(a,b)=>{if(!JobControl.Running)return;if(MessageBox.Show(this,LocalizedUi.T("Обработка и связанный SolidWorks будут принудительно завершены. Несохранённые изменения всех документов этого экземпляра будут потеряны. Данные текущего запуска будут удалены. Продолжить?"),LocalizedUi.T("Принудительная остановка"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;stop.Enabled=false;string outcome=await JobControl.ForceStop();MessageBox.Show(this,(LanguagePreferences.Ui=="ru"?(outcome=="STOP_CLEANED"?"Обработка остановлена. Данные текущего запуска удалены; исходная модель и предыдущие результаты сохранены.":"Не удалось завершить остановку или очистку.\n"+outcome):LocalizedUi.T(outcome)),LocalizedUi.T("Принудительная остановка"));};
  var check=new Button{Text="Проверить SolidWorks / API",Dock=DockStyle.Bottom,Height=34};
  Controls.Add(check);check.SendToBack();
  check.Click+=(s,e)=>CheckEnvironment();
  Shown+=(s,e)=>{if(!File.Exists(AppStorage.FilePath("preflight.json")))CheckEnvironment();};
 }
 void CheckEnvironment(){
  if(Working()){MessageBox.Show(this,LocalizedUi.T("Дождитесь окончания обработки."));return;}
  try{var pi=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Program","bin","PreflightCheck.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
   if(File.Exists(source)&&source.IndexOf('"')<0)pi.Arguments="--source \""+source+"\"";using(var p=Process.Start(pi)){string output=p.StandardOutput.ReadToEnd();string error=p.StandardError.ReadToEnd();p.WaitForExit();MessageBox.Show(this,LocalizedUi.T(p.ExitCode==0?"SolidWorks 2025 и COM API доступны. Можно начинать работу.":"Проверка требует внимания. Откройте SolidWorks 2025 с теми же правами и повторите проверку.\n\n"+output+error),LocalizedUi.T("Проверка окружения"),MessageBoxButtons.OK,p.ExitCode==0?MessageBoxIcon.Information:MessageBoxIcon.Warning);}
  }catch(Exception ex){MessageBox.Show(this,ex.Message,"Проверка окружения");}
 }
}
