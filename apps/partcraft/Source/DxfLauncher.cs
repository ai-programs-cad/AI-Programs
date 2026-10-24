using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Windows.Forms;

class DxfLauncher : Form {
 static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
 const string Expected="4E0CB10ED50453F86EB6D8C2BF568D07B47507A3646A5A0B1C078122891F7860";
 readonly string exe=Path.Combine(Root,@"Program\bin\SheetMetalDxfBatch.exe");
 readonly string state=AppStorage.FilePath("last-result.txt");
 ProgressBar activity=new ProgressBar();StageProgressBar bar=new StageProgressBar(); Label stageList=new Label(); Label eta=new Label(); Label detailLabel=new Label();
 ExportTimingJournal timingJournal=new ExportTimingJournal();DataGridView timingGrid=new DataGridView();Button timingButton=new Button();Button benchmarkButton=new Button();DateTime lastTimingSave=DateTime.MinValue;string lastTimingStage="";
 RunProgress progress=new RunProgress(); List<string> events=new List<string>(); Stopwatch nativeClock=new Stopwatch(); int nativeGeneration=0; DateTime lastProgressWrite=DateTime.MinValue; Stopwatch stageClock=new Stopwatch(); string loadedContext=""; double[] previous;
 CheckBox firstBatch=new CheckBox();TextBox details=new TextBox(); TextBox modelPath=new TextBox(); Label status=new Label(); Button start=new Button(); Button choose=new Button();
 List<Button> links=new List<Button>(); Process child; int lastExitCode=0; Stopwatch elapsed; Timer timer=new Timer();
 StringBuilder output=new StringBuilder(); object gate=new object(); string folder=""; bool busy;
 public DxfLauncher(){
  DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Text="PartCraft — Sheet metal DXF"; ClientSize=new Size(720,750); StartPosition=FormStartPosition.CenterScreen;
  var intro=new Label{Text="1. Откройте и сохраните нужную деталь в SolidWorks.\n2. Выберите тот же файл кнопкой «Выбрать модель». Агент закройте.\nДопуск CAM_ARCS: 0,01 мм. Оригинал и предыдущие результаты сохраняются.",Location=new Point(18,16),Size=new Size(680,65)}; Controls.Add(intro);
  choose.Text="Выбрать модель…";choose.SetBounds(18,86,160,32);choose.Click+=(s,e)=>ChooseModel();Controls.Add(choose);
  modelPath.SetBounds(190,90,508,28);modelPath.ReadOnly=true;modelPath.Text="Модель не выбрана";Controls.Add(modelPath);
  firstBatch.Text="Пробный запуск: только первые 4 листовых тела (одна группа)";firstBatch.Checked=false;firstBatch.SetBounds(18,122,680,22);firstBatch.Visible=false;Controls.Add(firstBatch);
  start.Text="Создать DXF";start.SetBounds(18,150,160,38);start.Click+=(s,e)=>Launch();Controls.Add(start);
  status.SetBounds(540,150,158,48);Controls.Add(status);
  var profiles=new Button{Text="Нарезка профилей"};profiles.SetBounds(190,150,160,38);profiles.Click+=(s,e)=>{if(busy){LocalizedUi.Show("Дождитесь завершения DXF.",Text);return;}if(modelPath.Tag==null)ChooseModel();if(modelPath.Tag==null)return;string selected=Convert.ToString(modelPath.Tag);if(selected.IndexOf((char)34)>=0||!File.Exists(selected)){LocalizedUi.Show("Выберите существующий файл SLDPRT.",Text);return;}new ProfilesJobWindow(selected).Show(this);};Controls.Add(profiles);
  var faces=new Button{Text="DXF выбранных граней"};faces.SetBounds(370,150,160,38);faces.Click+=(s,e)=>{if(busy){LocalizedUi.Show("Дождитесь завершения обработки SolidWorks.",Text);return;}new FacesJobWindow().Show(this);};Controls.Add(faces);
  AddLink("DXF для работы",18,()=>ResultLayout.Working(folder));
  AddLink("EXACT",188,()=>Path.Combine(ResultLayout.Service(folder),@"DXF\EXACT_NATIVE"));
  AddLink("Отчёт",358,()=>Path.Combine(ResultLayout.Service(folder),@"Reports\native-batch-dxf.json"));
  AddLink("Папка результата",528,()=>folder);
  details.SetBounds(18,575,680,155);details.Multiline=true;details.ReadOnly=true;details.ScrollBars=ScrollBars.Vertical;Controls.Add(details);
  activity.SetBounds(18,329,680,5);activity.Style=ProgressBarStyle.Marquee;activity.MarqueeAnimationSpeed=30;activity.Visible=false;Controls.Add(activity);bar.SetBounds(18,280,680,22);Controls.Add(bar);eta.SetBounds(18,306,680,25);Controls.Add(eta);detailLabel.SetBounds(18,204,680,70);detailLabel.Font=new Font(SystemFonts.MessageBoxFont.FontFamily,11,FontStyle.Bold);detailLabel.AutoEllipsis=true;Controls.Add(detailLabel);stageList.SetBounds(18,370,680,150);Controls.Add(stageList);stageList.Text=LocalizedUi.T(progress.Checklist().Replace("Групповой native DXF","Экспорт развёрток в DXF").Replace("Model Space и проверка файлов","Подготовка и проверка DXF").Replace("CAM_ARCS: дуги и топология","Замена сплайнов дугами и проверка контуров"));
  timingGrid.ReadOnly=true;timingGrid.AllowUserToAddRows=false;timingGrid.AllowUserToDeleteRows=false;timingGrid.RowHeadersVisible=false;timingGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;timingGrid.BackgroundColor=Color.White;timingGrid.BorderStyle=BorderStyle.FixedSingle;timingGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;timingGrid.MultiSelect=false;timingGrid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
  timingGrid.Columns.Add("stage","");timingGrid.Columns.Add("state","");timingGrid.Columns.Add("seconds","");timingGrid.Columns[0].FillWeight=65;timingGrid.Columns[1].FillWeight=20;timingGrid.Columns[2].FillWeight=15;
  timingGrid.SetBounds(18,370,680,260);Controls.Add(timingGrid);stageList.Visible=false;
  benchmarkButton.Text="Тест скорости: 2 позиции";benchmarkButton.SetBounds(18,48,320,28);benchmarkButton.Click+=(a,e)=>Launch(2);Controls.Add(benchmarkButton);
  timingButton.Text="Время по этапам";timingButton.Enabled=false;timingButton.SetBounds(535,150,160,34);timingButton.Click+=(a,e)=>{if(timingJournal.Folder!=null)Process.Start(Path.Combine(timingJournal.Folder,"timings.html"));};Controls.Add(timingButton);
  if(File.Exists(state)) folder=File.ReadAllText(state).Trim();
  RefreshResult();timer.Interval=500;timer.Tick+=(s,e)=>Poll();
  FormClosing+=(s,e)=>{if(busy){e.Cancel=true;LocalizedUi.Show("Дождитесь завершения: принудительное закрытие COM-экспорта небезопасно.",Text);}};
 }
 public void SetModel(string source){modelPath.Text=source;modelPath.Tag=source;}
 public Func<string> ModelProvider;
 public void EmbedMode(){
  AutoScroll=true;AutoScrollMinSize=new Size(720,680);SuspendLayout();
  choose.Visible=false;modelPath.Visible=false;firstBatch.Visible=false;benchmarkButton.Text=LocalizedUi.T("Тест скорости: 2 позиции");benchmarkButton.SetBounds(18,48,320,26);
  foreach(Control c in Controls){if(c is Label&&c.Top==16)c.Visible=false;if(c is Button&&(c.Text=="Нарезка профилей"||c.Text=="DXF выбранных граней"))c.Visible=false;}
  start.SetBounds(18,12,200,34);status.SetBounds(235,12,280,40);timingButton.SetBounds(535,12,160,34);
  detailLabel.SetBounds(18,76,680,38);bar.SetBounds(18,120,680,22);
  eta.SetBounds(18,148,680,32);activity.SetBounds(18,186,680,5);
  stageList.SetBounds(18,202,680,155);stageList.Visible=false;timingGrid.SetBounds(18,194,680,300);
  for(int i=0;i<links.Count;i++)links[i].SetBounds(18+i*170,510,160,34);
  foreach(Control c in Controls)if(c is Button&&c.Text=="EXACT")c.Text="Исходные DXF";
  details.SetBounds(18,558,680,110);
  detailLabel.Text=LocalizedUi.T("Обработка ещё не запущена. Здесь появится текущий этап.");
  ResumeLayout(false);
 }
 void AddLink(string text,int x,Func<string> path){var b=new Button{Text=text};b.SetBounds(x,528,160,34);b.Click+=(s,e)=>{try{string p=path();if(!File.Exists(p)&&!Directory.Exists(p))throw new Exception("Результат пока не создан: "+p);Process.Start("explorer.exe","\""+p+"\"");}catch(Exception ex){LocalizedUi.Show(ex.Message,Text);}};Controls.Add(b);links.Add(b);}
 void RefreshResult(){foreach(var b in links)b.Enabled=!busy&&Directory.Exists(folder);if(!busy){details.Text=Directory.Exists(folder)?"Последний результат:\r\n"+folder:"Результат появится после первого запуска.";}}
 void ChooseModel(){using(var d=new OpenFileDialog{Title="Выберите модель SolidWorks для экспорта DXF",Filter="Деталь SolidWorks (*.sldprt)|*.sldprt",CheckFileExists=true,Multiselect=false,RestoreDirectory=true}){if(d.ShowDialog(this)==DialogResult.OK){modelPath.Text=d.FileName;modelPath.Tag=d.FileName;status.Text=LocalizedUi.T("Выбрана модель для следующего запуска");}}}
 static string SourceArguments(string source){if(String.IsNullOrWhiteSpace(source)||source.IndexOf((char)34)>=0||source.IndexOfAny(Path.GetInvalidPathChars())>=0||!String.Equals(Path.GetExtension(source),".sldprt",StringComparison.OrdinalIgnoreCase))throw new Exception("Выберите файл SLDPRT.");return "--tolerance-mm 0.01 --source \""+source+"\"";}
 void Launch(int positionLimit=0){try{
  if(positionLimit!=0&&positionLimit!=2)throw new ArgumentException("Invalid diagnostic position limit");
  if(ModelProvider!=null){string supplied=ModelProvider();if(String.IsNullOrWhiteSpace(supplied))return;SetModel(supplied);}
  if(modelPath.Tag==null)ChooseModel();
  if(modelPath.Tag==null)return;
  string source=Convert.ToString(modelPath.Tag);
  if(!File.Exists(source))throw new Exception("Выбранная модель не найдена: "+source);
  if(Process.GetProcessesByName("SheetMetalDxfBatch").Length!=0||Process.GetProcessesByName("ProfilesCutTables").Length!=0||Process.GetProcessesByName("SelectedFacesDxf").Length!=0||Process.GetProcessesByName("JobRunner").Length!=0||Process.GetProcessesByName("ManufacturingPackage").Length!=0)throw new Exception("DXF-экспорт уже выполняется. Дождитесь завершения предыдущего запуска.");
  if(!File.Exists(exe))throw new Exception("Не найдена контрольная сборка: "+exe);
  using(var sha=SHA256.Create())using(var f=File.OpenRead(exe))if(BitConverter.ToString(sha.ComputeHash(f)).Replace("-","")!=Expected)throw new Exception("SHA256 контрольной сборки отличается. Запуск остановлен.");
  var info=new ProcessStartInfo(Path.Combine(Root,@"Program\bin\JobRunner.exe"),"--mode "+(positionLimit==2?"dxf-benchmark":"dxf")+" --doc-lang "+LanguagePreferences.Documents+" "+SourceArguments(source)){WorkingDirectory=Path.GetDirectoryName(exe),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  timingJournal=new ExportTimingJournal();timingJournal.Start(source,AppStorage.FilePath("TimingReports"));timingButton.Enabled=true;lastTimingSave=DateTime.MinValue;
  output.Clear();lock(gate)events.Clear();progress=new RunProgress();nativeGeneration=0;nativeClock.Reset();previous=null;loadedContext="";bar.Value=0;stageList.Text=LocalizedUi.T(progress.Checklist().Replace("Групповой native DXF","Экспорт развёрток в DXF").Replace("Model Space и проверка файлов","Подготовка и проверка DXF").Replace("CAM_ARCS: дуги и топология","Замена сплайнов дугами и проверка контуров"));eta.Text=LocalizedUi.T("Оставшееся время неизвестно: native API не сообщает внутренний процент.");child=new Process{StartInfo=info};child.OutputDataReceived+=(s,e)=>Append(e.Data);child.ErrorDataReceived+=(s,e)=>Append(e.Data);
  JobControl.Prepare(info);child.Start();JobControl.Started(child);activity.Visible=true;busy=true;choose.Enabled=false;firstBatch.Enabled=false;benchmarkButton.Enabled=false;start.Enabled=false;RefreshResult();elapsed=Stopwatch.StartNew();child.BeginOutputReadLine();child.BeginErrorReadLine();timer.Start();status.Text=LocalizedUi.T("Экспорт позиции");progress.Apply("@@STAGE|0|BEGIN|0");details.Text="Native DXF → Model Space → CAM_ARCS.\r\nОжидайте завершения проверок.";
 }catch(Exception ex){LocalizedUi.Show(ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}}
 void Append(string line){if(line!=null)lock(gate){output.AppendLine(line);events.Add(line);}}
 void Poll(){try{PollCore();}catch(Exception ex){timer.Stop();bool running=child!=null&&!child.HasExited;busy=running;activity.Visible=running;choose.Enabled=!running;firstBatch.Enabled=!running;benchmarkButton.Enabled=!running;start.Enabled=!running;details.Text="Ошибка окна результатов: "+ex.Message+"\r\nЭкспорт не перезапускается автоматически.";status.Text=LocalizedUi.T(running?"Экспорт ещё выполняется":"Экспорт завершён; ошибка отображения результата");if(running)timer.Start();}}
 void PollCore(){ConsumeProgress();status.Text=LocalizedUi.T("Прошло "+elapsed.Elapsed.ToString(@"hh\:mm\:ss")+"; завершено "+progress.Completed+" из 8 этапов ("+progress.Percent+"%)");if(!child.HasExited)return;
  timer.Stop();child.WaitForExit();activity.Visible=false;ConsumeProgress();int code=child.ExitCode;lastExitCode=code;string text;lock(gate)text=output.ToString();busy=false;choose.Enabled=true;firstBatch.Enabled=true;benchmarkButton.Enabled=true;start.Enabled=true;
  string found=null;foreach(var line in text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){string p=line.Trim();if(IsResultFolder(p))found=p;}
  if(found!=null&&code==2){try{JobDisplay.ImportTimings(progress,Path.Combine(found,@"Reports\native-batch-dxf.json"));}catch(Exception timingError){text+="\r\nTiming report: "+timingError.Message;}for(int i=0;i<7;i++){if(!progress.Done[i]){progress.Done[i]=true;progress.Seconds[i]=-1;}}progress.Apply("@@STAGE|7|BEGIN|0");stageClock.Restart();RenderProgress();status.Text=LocalizedUi.T("Раскладка файлов и проверка SHA256…");Refresh();var publishing=Stopwatch.StartNew();found=ResultLayout.Publish(found);progress.Apply("@@STAGE|7|DONE|"+publishing.Elapsed.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));RenderProgress();SaveHistory();eta.Text=LocalizedUi.T("Готово. Статус качества: NEEDS_REVIEW");}else{eta.Text=LocalizedUi.T("Остановлено: завершение не подтверждено. См. сообщение ниже.");}
  if(found!=null){folder=found;Directory.CreateDirectory(Path.GetDirectoryName(state));File.WriteAllText(state,folder,Encoding.UTF8);}
  elapsed.Stop();timingJournal.Finish(code==2||code==3);RefreshResult();status.Text=LocalizedUi.T("Завершено за "+elapsed.Elapsed.TotalSeconds.ToString("F1")+" с; exit="+code);
  if(text.Contains("SAVED_ORIGINAL_MUST_BE_OPEN"))text="Выбранный файл должен быть открыт в SolidWorks и не иметь несохранённых изменений. Программа не сохраняет модель автоматически.\r\n"+text;
  details.Text="Текущий запуск: "+(found??"папка результата не получена; кнопки относятся к предыдущему результату")+"\r\n"+text;
  if(found!=null){try{var report=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(ResultLayout.Service(found),@"Reports\native-batch-dxf.json")));details.Text=(report.ContainsKey("partialSelection")&&Convert.ToBoolean(report["partialSelection"])?"ПРОБНЫЙ ЗАПУСК: обработана только выбранная часть листовых тел.\r\n":"")+"Статус: "+report["status"]+"\r\nМодель: "+report["source"]+"\r\nКонфигурация: "+report["configuration"]+"\r\nРезультат: "+found+"\r\nТочность CAM требует подтверждения в целевой CAM-системе.";}catch(Exception ex){details.AppendText("\r\nОшибка чтения отчёта: "+ex.Message);}}
  if(code==3){details.Text="Speed test completed. Diagnostic files only; no production DXF published.\r\n"+text;eta.Text="Speed test completed";}
  progress.NativePending=false;lastProgressWrite=DateTime.MinValue;RenderProgress();JobControl.Finish();child.Dispose();child=null;
 }
 void BaselineDone(int i){if(!progress.Done[i]){progress.Done[i]=true;progress.Seconds[i]=-1;}}
 void BaselineProgress(string line){
  var measured=System.Text.RegularExpressions.Regex.Match(line,@"^END ONE_GROUP_EXPORT_OPTIONS_5 ([0-9]+(?:\.[0-9]+)?)s$");
  if(measured.Success){double seconds;if(double.TryParse(measured.Groups[1].Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out seconds)&&seconds>=0){progress.Seconds[2]=seconds;progress.Done[2]=true;}}

  if(line=="BEGIN CONNECT_EXISTING_SOLIDWORKS"){BaselineDone(0);BaselineDone(1);progress.Current=2;progress.Detail="Подготовка native-экспорта";stageClock.Restart();}
  if(line=="BEGIN ONE_GROUP_EXPORT_OPTIONS_5"){progress.Current=2;progress.Detail="ONE_GROUP_EXPORT_OPTIONS_5";progress.NativePending=true;progress.NativeGeneration++;}
  if(line=="END CUT_LIST_EXPORT"){progress.NativePending=false;BaselineDone(2);progress.Current=3;stageClock.Restart();}
  if(line.StartsWith("END ONE_GROUP_EXPORT_OPTIONS_5 ")){progress.NativePending=false;BaselineDone(2);progress.Current=3;stageClock.Restart();}
  if(line.StartsWith("[")&&line.Contains("] CAM_ARCS ")){for(int i=0;i<5;i++)BaselineDone(i);progress.Current=5;progress.Detail="CAM_ARCS";var match=System.Text.RegularExpressions.Regex.Match(line,@"^\[(\d+)/(\d+)\]");if(match.Success)progress.CamDone=Math.Max(0,int.Parse(match.Groups[1].Value)-1);}
 }
 string HistoryPath {get{return AppStorage.FilePath("duration-history.json");}}
 string HistoryKey {get{return Expected+"|"+progress.Context;}}
 void ConsumeProgress(){
  string[] lines;lock(gate){lines=events.ToArray();events.Clear();}
  foreach(string line in lines){timingJournal.Apply(line);BaselineProgress(line);int before=progress.Current;progress.Apply(line);if(before!=progress.Current)stageClock.Restart();}
  if(progress.NativeGeneration!=nativeGeneration){nativeGeneration=progress.NativeGeneration;nativeClock.Restart();}
  if(progress.Context!=""&&loadedContext!=progress.Context){loadedContext=progress.Context;try{if(File.Exists(HistoryPath)){var history=new JavaScriptSerializer().Deserialize<Dictionary<string,double[]>>(File.ReadAllText(HistoryPath));if(history.ContainsKey(HistoryKey))previous=history[HistoryKey];if(previous!=null&&(previous.Length!=8||Array.Exists(previous,v=>v<0||Double.IsNaN(v)||Double.IsInfinity(v))))previous=null;}}catch{previous=null;}}
  RenderProgress();
 }
 public void RefreshLanguage(){RenderProgress();if(elapsed!=null&&!busy)status.Text=LocalizedUi.T("Завершено за "+elapsed.Elapsed.TotalSeconds.ToString("F1")+" с; exit="+lastExitCode);}
 void RenderProgress(){
  bar.Value=progress.Percent;stageList.Text=LocalizedUi.T(progress.Checklist().Replace("Групповой native DXF","Экспорт развёрток в DXF").Replace("Model Space и проверка файлов","Подготовка и проверка DXF").Replace("CAM_ARCS: дуги и топология","Замена сплайнов дугами и проверка контуров"));
  string sub=progress.Detail;
  var labels=new System.Collections.Generic.Dictionary<string,string>{
   {"COPY_SAVED_WORKSPACE","Копирование модели"},{"OPEN_EXPERIMENTAL_COPY","Открытие рабочей модели"},{"ACTIVATE_EXPERIMENTAL_COPY","Подготовка окна модели"},
   {"BIND/SNAPSHOT_BODIES_ONCE","Чтение списка тел"},{"BIND/COLLECT_FEATURES_MACRO_ORDER","Поиск развёрток"},{"BIND/BUILD_CUT_LIST_CACHE","Чтение папок Cut List"},{"BIND/FIXED_FACE_AND_CACHED_MEMBERSHIP","Связь развёрток с Cut List"}};
  if(labels.ContainsKey(sub))sub=labels[sub];

  if(sub=="ONE_GROUP_EXPORT_OPTIONS_5")sub="Один групповой экспорт всех листовых тел";
  else if(sub=="BIND_FLAT_PATTERNS_TO_BODIES_AND_CUT_LIST")sub="Поиск Flat-Pattern → тело → Cut List";
  else if(sub.Contains("EXPORT"))sub="Штатный экспорт SolidWorks (внутренний процент API недоступен)";
  detailLabel.Text=progress.Current>=0?LocalizedUi.T("Сейчас")+": "+LocalizedUi.T(RunProgress.Names[progress.Current])+(sub==""?"":" / "+LocalizedUi.T(sub)):LocalizedUi.T("Ожидание подключения к SolidWorks…");
  if(busy&&previous!=null&&previous.Length==8&&progress.Completed<8){double left=0;for(int i=0;i<8;i++)if(!progress.Done[i])left+=previous[i];if(progress.Current>=0&&!progress.Done[progress.Current])left-=Math.Min(stageClock.Elapsed.TotalSeconds,previous[progress.Current]);eta.Text=LocalizedUi.T(left>1?"Осталось примерно "+Math.Ceiling(left)+" с — по предыдущему запуску той же модели и конфигурации":"Предыдущая оценка исчерпана; ожидаем завершения текущего этапа");}
  if(busy&&previous==null&&progress.Current==2&&progress.BatchDone>0&&progress.BatchDone<progress.BatchTotal&&progress.BatchSeconds>0)eta.Text=LocalizedUi.T("Native DXF: ещё примерно "+Math.Ceiling(progress.BatchSeconds/progress.BatchDone*(progress.BatchTotal-progress.BatchDone))+" с по завершённым группам. CAM_ARCS ещё не оценён.");
  detailLabel.ForeColor=SystemColors.ControlText;
  if(progress.NativePending){
   detailLabel.Text=LocalizedUi.T(sub)+" — "+LocalizedUi.T("Ожидание ответа SolidWorks")+": "+Math.Floor(nativeClock.Elapsed.TotalSeconds)+" s";
   if(nativeClock.Elapsed.TotalSeconds>=120)detailLabel.ForeColor=Color.Firebrick;
   eta.Text=LocalizedUi.T("Готово позиций")+": "+progress.BatchDone+" / "+progress.BatchTotal+". "+LocalizedUi.T("Внутренний процент SolidWorks недоступен");
  }
  RenderTimingJournal();
  if(elapsed!=null&&DateTime.UtcNow-lastProgressWrite>TimeSpan.FromSeconds(2)){lastProgressWrite=DateTime.UtcNow;try{File.WriteAllText(AppStorage.FilePath("live-progress.json"),new JavaScriptSerializer().Serialize(new{utc=DateTime.UtcNow.ToString("o"),running=busy,source=modelPath.Text,stage=progress.Current,stageName=progress.Current<0?"Подключение":RunProgress.Names[progress.Current],percent=progress.Percent,batch=progress.BatchCurrent,batches=progress.BatchTotal,completedBatches=progress.BatchDone,camFile=progress.CamCurrent,camFiles=progress.CamTotal,nativeCallWaiting=progress.NativePending,nativeCallSeconds=progress.NativePending?nativeClock.Elapsed.TotalSeconds:0,elapsedSeconds=elapsed.Elapsed.TotalSeconds}),Encoding.UTF8);}catch{}}
 }
 void RenderTimingJournal(){
  timingGrid.Columns[0].HeaderText=LocalizedUi.T("Этап");timingGrid.Columns[1].HeaderText=LocalizedUi.T("Состояние");timingGrid.Columns[2].HeaderText=LocalizedUi.T("Время, с");
  while(timingGrid.Rows.Count<timingJournal.Entries.Count)timingGrid.Rows.Add();
  for(int i=0;i<timingJournal.Entries.Count;i++){
   var row=timingJournal.Entries[i];var seconds=timingJournal.Seconds(row);string[] values={timingJournal.Label(row),timingJournal.State(row),seconds.HasValue?seconds.Value.ToString("F1",System.Globalization.CultureInfo.CurrentCulture):"—"};
   for(int c=0;c<3;c++)if(Convert.ToString(timingGrid.Rows[i].Cells[c].Value)!=values[c])timingGrid.Rows[i].Cells[c].Value=values[c];
   timingGrid.Rows[i].DefaultCellStyle.BackColor=row.state=="running"?Color.FromArgb(225,240,250):row.state=="complete"?Color.FromArgb(238,247,241):Color.White;
  }
  var current=timingJournal.Entries.Where(e=>e.state=="running").OrderByDescending(e=>e.started).FirstOrDefault();if(current!=null&&current.id!=lastTimingStage){lastTimingStage=current.id;int index=timingJournal.Entries.IndexOf(current);if(index>=0&&index<timingGrid.Rows.Count)timingGrid.FirstDisplayedScrollingRowIndex=index;}
  if(timingJournal.Folder!=null&&DateTime.UtcNow-lastTimingSave>TimeSpan.FromSeconds(2)){lastTimingSave=DateTime.UtcNow;timingJournal.Save();}
 }
 void SaveHistory(){if(progress.Context==""||progress.Completed!=8||Array.Exists(progress.Seconds,v=>v<0||Double.IsNaN(v)||Double.IsInfinity(v)))return;try{var json=new JavaScriptSerializer();var history=File.Exists(HistoryPath)?json.Deserialize<Dictionary<string,double[]>>(File.ReadAllText(HistoryPath)):new Dictionary<string,double[]>();history[HistoryKey]=progress.Seconds;File.WriteAllText(HistoryPath,json.Serialize(history),Encoding.UTF8);}catch(Exception e){details.AppendText("\r\nНе удалось сохранить историю времени: "+e.Message);}}
 static bool IsAbsoluteFilePath(string p){
  if(String.IsNullOrWhiteSpace(p)||p.IndexOfAny(Path.GetInvalidPathChars())>=0)return false;
  // Console output also contains JSON and diagnostic messages, not just paths.
  return (p.Length>=3&&Char.IsLetter(p[0])&&p[1]==':'&&(p[2]==(char)92||p[2]=='/'))||p.StartsWith(@"\\");
 }
 static bool IsResultFolder(string p){
  try{return IsAbsoluteFilePath(p)&&Directory.Exists(p)&&File.Exists(Path.Combine(p,@"Reports\native-batch-dxf.json"));}
  catch(ArgumentException){return false;}catch(NotSupportedException){return false;}catch(IOException){return false;}catch(System.Security.SecurityException){return false;}catch(UnauthorizedAccessException){return false;}
 }
 static void CheckPathParser(){
  string[] rejected={"", "Native export complete", "{\"path\":\"C:\\test\"}", "C:\\bad\"path", "C:\\bad\0path", "relative\\folder", "DXF_EXACT_AND_CAM_ARCS_CREATED_NEEDS_REVIEW"};
  foreach(string s in rejected)if(IsAbsoluteFilePath(s)||IsResultFolder(s))throw new Exception("Path parser accepted diagnostic text");
  foreach(string s in new[]{@"C:\Папка проекта\Run_123",@"\\server\share\Run_123"})if(!IsAbsoluteFilePath(s))throw new Exception("Path parser rejected absolute path");
 }
 [STAThread]static int Main(string[] args){if(args.Length==2&&args[0]=="--preview"){Application.EnableVisualStyles();using(var f=new DxfLauncher()){f.progress=new RunProgress();for(int i=0;i<5;i++){f.progress.Apply("@@STAGE|"+i+"|BEGIN|0");f.progress.Apply("@@STAGE|"+i+"|DONE|2");}f.progress.Apply("@@STAGE|5|BEGIN|0");f.progress.Apply("[2/4] CAM_ARCS Деталь.dxf");f.RenderProgress();f.status.Text=LocalizedUi.T("Предпросмотр: завершено 5 из 8 этапов (62%)");f.eta.Text=LocalizedUi.T("Оставшееся время неизвестно: native API не сообщает внутренний процент.");f.details.Text="Предпросмотр интерфейса. SolidWorks не вызывается, экспорт не запущен.";f.Show();Application.DoEvents();f.Refresh();using(var bitmap=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bitmap,new Rectangle(Point.Empty,f.Size));bitmap.Save(args[1]);}}return 0;}if(args.Length==2&&args[0]=="--organize"){Console.WriteLine(ResultLayout.Publish(args[1]));return 0;}if(args.Length==1&&args[0]=="--self-test"){RunProgress.Test();CheckPathParser();string p=Path.Combine(Root,@"Program\bin\SheetMetalDxfBatch.exe");using(var h=SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","")==Expected?0:1;}Application.EnableVisualStyles();Application.Run(new DxfLauncher());return 0;}
}
