using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections.Generic;
class FacesJobWindow:Form {
 Label stage=new Label();ProgressBar bar=new ProgressBar();TextBox log=new TextBox();Button open=new Button();Timer timer=new Timer();Process child;StringBuilder output=new StringBuilder();object gate=new object();string result="";bool running;Stopwatch clock=new Stopwatch();
 public FacesJobWindow(){
  Text="DXF выбранных граней";ClientSize=new Size(710,400);StartPosition=FormStartPosition.CenterParent;
  var intro=new Label{Text="В SolidWorks активируйте сохранённую деталь и выделите плоские грани через Ctrl.\nОдин DXF на тело: все выбранные параллельные грани и контуры карманов вместе.",Location=new Point(15,15),Size=new Size(680,45)};Controls.Add(intro);
  var start=new Button{Text="Экспортировать выбранные грани",Location=new Point(15,70),Size=new Size(260,35)};Controls.Add(start);
  stage.SetBounds(15,115,680,45);Controls.Add(stage);bar.SetBounds(15,165,680,22);Controls.Add(bar);
  log.SetBounds(15,200,680,135);log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;Controls.Add(log);
  open.Text="Открыть результат";open.SetBounds(15,350,180,32);open.Enabled=false;open.Click+=(s,e)=>Process.Start("explorer.exe","\""+result+"\"");Controls.Add(open);
  start.Click+=(s,e)=>{
   if(running)return;
   if(Process.GetProcessesByName("SheetMetalDxfBatch").Length!=0||Process.GetProcessesByName("ProfilesCutTables").Length!=0||Process.GetProcessesByName("SelectedFacesDxf").Length!=0||Process.GetProcessesByName("JobRunner").Length!=0||Process.GetProcessesByName("ManufacturingPackage").Length!=0){LocalizedUi.Show("Другая обработка SolidWorks уже выполняется.",Text);return;}
   lock(gate)output.Clear();result="";bar.Value=0;
   var info=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"Program\bin\JobRunner.exe")){Arguments="--mode faces --doc-lang "+LanguagePreferences.Documents,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
   try{child=new Process{StartInfo=info};child.OutputDataReceived+=(a,b)=>Append(b.Data);child.ErrorDataReceived+=(a,b)=>Append(b.Data);JobControl.Prepare(info);child.Start();JobControl.Started(child);running=true;clock.Restart();start.Enabled=false;open.Enabled=false;child.BeginOutputReadLine();child.BeginErrorReadLine();timer.Start();}catch(Exception ex){LocalizedUi.Show(ex.Message,Text);}
  };
  timer.Interval=400;timer.Tick+=(s,e)=>{
   string text;lock(gate)text=output.ToString();log.Tag=new RawJobLog(text);string display=JobDisplay.Log(text);if(log.Text!=display)log.Text=display;
   int latestPercent=-1;string latestStage=null;
   foreach(string line in text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
    if(line.StartsWith("@@PROFILE|")){var p=line.Split('|');int n;if(p.Length>=3&&int.TryParse(p[1],out n)){latestPercent=Math.Max(0,Math.Min(100,n));latestStage=p[2];}}
    if(line.StartsWith("@@RESULT|"))result=line.Substring(9);
   }
   if(latestPercent>=0){if(bar.Value!=latestPercent)bar.Value=latestPercent;stage.Tag=new RawJobStatus(latestStage,clock.Elapsed);stage.Text=((RawJobStatus)stage.Tag).Render();}
   else stage.Text=JobDisplay.Elapsed(clock.Elapsed);
   if(!child.HasExited)return;child.WaitForExit();clock.Stop();timer.Stop();running=false;JobControl.Finish();start.Enabled=true;open.Enabled=Directory.Exists(result);stage.Tag=new RawJobStatus(child.ExitCode==2?"DXF созданы. Проверьте контуры и масштаб в CAM.":"Обработка остановлена. Причина указана ниже.",clock.Elapsed);stage.Text=((RawJobStatus)stage.Tag).Render();if(child.ExitCode==2)bar.Value=100;
  };
  FormClosing+=(s,e)=>{if(running){e.Cancel=true;LocalizedUi.Show("Дождитесь завершения вызова SolidWorks. Окно не прерывает COM-вызов принудительно.",Text);}};
 }
 void Append(string line){if(line!=null)lock(gate)output.AppendLine(line);}
}
