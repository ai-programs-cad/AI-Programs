using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

partial class UnifiedLauncher:Form {
 string source="";TextBox path=new TextBox();Button choose=new Button();ComboBox uiLanguage=new ComboBox(),docLanguage=new ComboBox();Label filesHint=new Label();Label hint=new Label(),title=new Label(),footer=new Label(),modelCaption=new Label(),destination=new Label();Panel workspace=new Panel(),heading=new Panel();TableLayoutPanel guide=new TableLayoutPanel();Label[] instructions=new Label[3];Button help=new Button();bool guideVisible=true;ToolTip tips=new ToolTip();Form[] modules;Button[] navigation;DxfLauncher dxf;Timer timer=new Timer();int selected;
 static Color Ink=Color.FromArgb(29,43,61),Blue=Color.FromArgb(31,101,171),Pale=Color.FromArgb(237,244,252);
 static bool Working(){return JobControl.Stopping|| new[]{"SheetMetalDxfBatch","ProfilesCutTables","SelectedFacesDxf","JobRunner","ManufacturingPackage","PackageHost"}.Any(n=>Process.GetProcessesByName(n).Length>0);}
 public UnifiedLauncher(){
  DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Text="PartCraft";Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);ClientSize=new Size(1100,Math.Min(900,Screen.PrimaryScreen.WorkingArea.Height-55));MinimumSize=new Size(1000,700);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.White;Font=new Font("Segoe UI",9);AutoScaleMode=AutoScaleMode.Dpi;
  var top=new Panel{Dock=DockStyle.Top,Height=160,BackColor=Color.White,Padding=new Padding(22)};Controls.Add(top);
  top.Controls.Add(new Label{Text="PartCraft",Font=new Font("Segoe UI",18,FontStyle.Bold),ForeColor=Ink,Location=new Point(22,12),AutoSize=true});
  modelCaption.Text="НАЧНИТЕ ЗДЕСЬ  ·  Откройте и сохраните деталь в SolidWorks, затем выберите тот же файл";modelCaption.ForeColor=Blue;modelCaption.SetBounds(24,83,1000,22);top.Controls.Add(modelCaption);
  path.SetBounds(24,107,820,28);path.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;path.ReadOnly=true;path.BackColor=Color.FromArgb(247,249,252);path.Text="Выберите сохранённую деталь SLDPRT";top.Controls.Add(path);
  choose.Text="Выбрать модель…";choose.SetBounds(862,105,194,32);choose.Anchor=AnchorStyles.Top|AnchorStyles.Right;choose.FlatStyle=FlatStyle.Flat;choose.ForeColor=Blue;top.Controls.Add(choose);choose.Click+=(s,e)=>PickModel();
  var bottom=new Panel{Dock=DockStyle.Bottom,Height=34,BackColor=Color.FromArgb(246,248,251)};Controls.Add(bottom);footer.Dock=DockStyle.Fill;footer.Padding=new Padding(22,8,0,0);footer.ForeColor=Color.DimGray;footer.Text=LocalizedUi.T("Готово к работе. Выберите функцию слева.");bottom.Controls.Add(footer);
  var body=new Panel{Dock=DockStyle.Fill};Controls.Add(body);body.BringToFront();
  var side=new Panel{AutoScroll=true,Dock=DockStyle.Left,Width=225,BackColor=Color.FromArgb(246,248,251),Padding=new Padding(14,22,14,14)};body.Controls.Add(side);
  var area=new Panel{Dock=DockStyle.Fill,Padding=new Padding(14,12,14,0)};body.Controls.Add(area);area.BringToFront();
  heading.Dock=DockStyle.Top;heading.Height=205;area.Controls.Add(heading);title.SetBounds(4,4,590,30);title.Font=new Font("Segoe UI",14,FontStyle.Bold);title.ForeColor=Ink;heading.Controls.Add(title);hint.SetBounds(4,39,790,33);hint.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;hint.ForeColor=Color.DimGray;heading.Controls.Add(hint);
  help.Text="Скрыть инструкцию";help.SetBounds(630,3,170,28);help.Anchor=AnchorStyles.Top|AnchorStyles.Right;help.FlatStyle=FlatStyle.Flat;help.FlatAppearance.BorderSize=0;help.ForeColor=Blue;heading.Controls.Add(help);help.Click+=(s,e)=>{guideVisible=!guideVisible;guide.Visible=guideVisible;heading.Height=guideVisible?205:101;destination.Top=guideVisible?181:76;help.Text=guideVisible?"Скрыть инструкцию":"Как пользоваться";};
  guide.SetBounds(4,76,796,96);guide.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;guide.ColumnCount=3;guide.RowCount=1;guide.Margin=Padding.Empty;guide.Padding=Padding.Empty;heading.Controls.Add(guide);
  for(int i=0;i<3;i++){guide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));var card=new Panel{Dock=DockStyle.Fill,Size=new Size(250,96),BackColor=Pale,Margin=new Padding(i==0?0:5,0,i==2?0:5,0)};var number=new Label{Text=(i+1).ToString("00"),ForeColor=Blue,Font=new Font("Segoe UI",11,FontStyle.Bold),Location=new Point(9,9),Size=new Size(30,25)};var instruction=new Label{Location=new Point(43,10),Size=new Size(197,80),ForeColor=Ink};instructions[i]=instruction;card.Controls.Add(instruction);card.Controls.Add(number);card.Resize+=(s,e)=>{instruction.Width=Math.Max(80,card.ClientSize.Width-51);};guide.Controls.Add(card,i,0);}
  destination.SetBounds(4,181,796,23);destination.ForeColor=Color.FromArgb(58,102,77);destination.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;heading.Controls.Add(destination);
  workspace.Dock=DockStyle.Fill;workspace.AutoScroll=true;area.Controls.Add(workspace);workspace.BringToFront();
  dxf=new DxfLauncher();dxf.EmbedMode();dxf.ModelProvider=GetModel;modules=new Form[]{dxf,new ProfilesJobWindow(GetModel),new FacesJobWindow()};
  string[] names={"Развёртки DXF\nЛистовые детали","Нарезка профилей\nКарточки резки и ведомость","DXF выбранных граней\nКонтуры, карманы и выборки"};navigation=new Button[modules.Length];
  for(int i=0;i<modules.Length;i++){int index=i;var button=new Button{Text=names[i],TextAlign=ContentAlignment.MiddleLeft,FlatStyle=FlatStyle.Flat,Size=new Size(197,72),Location=new Point(14,22+i*84),Padding=new Padding(10,0,0,0)};button.FlatAppearance.BorderSize=0;button.Click+=(s,e)=>SelectModule(index);navigation[i]=button;side.Controls.Add(button);
   var form=modules[i];form.TopLevel=false;form.FormBorderStyle=FormBorderStyle.None;form.BackColor=Color.White;form.Dock=DockStyle.Fill;form.AutoScroll=true;workspace.Controls.Add(form);form.Show();
   foreach(Control control in form.Controls){var action=control as Button;if(action!=null&&(action.Text.StartsWith("Создать")||action.Text.StartsWith("Экспортировать"))){action.FlatStyle=FlatStyle.Flat;action.BackColor=Blue;action.ForeColor=Color.White;action.FlatAppearance.BorderSize=0;}}
   if(i>0)ModuleLayout.Configure(form);
  }
  side.Controls.Add(filesHint=new Label{Text="ГДЕ ИСКАТЬ ФАЙЛЫ\n\nПапка модели →\nимя проекта_1, _2…\n\nDXF — файлы для работы\nProfile — чертёж и PDF\nDXF\\Faces — выбранные грани\n\nВременные файлы:\nимя проекта_temp",ForeColor=Color.DimGray,Location=new Point(25,405),Size=new Size(185,220)});
  tips.SetToolTip(choose,"Выберите тот же файл SLDPRT, который открыт и сохранён в SolidWorks.");tips.SetToolTip(path,"Общий файл для развёрток и профилей. Выбранные грани берутся из активной детали SolidWorks.");
  timer.Interval=500;timer.Tick+=(s,e)=>{bool busy=Working();choose.Enabled=!busy&&selected!=2;docLanguage.Enabled=!busy;footer.Text=LocalizedUi.T(busy?"Выполняется обработка SolidWorks. Второй запуск станет доступен после её завершения.":selected==2?"Выделите грани в SolidWorks через Ctrl, затем нажмите «Экспортировать выбранные грани».":String.IsNullOrWhiteSpace(source)?"Шаг 1: откройте и сохраните деталь в SolidWorks. Шаг 2: нажмите «Выбрать модель».":LanguageCatalog.T("Выбранная модель",LanguagePreferences.Ui)+": "+Path.GetFileName(source));LocalizedUi.Apply(this);};timer.Start();
  FormClosing+=(s,e)=>{if(Working()){e.Cancel=true;LocalizedUi.Show("Дождитесь завершения обработки SolidWorks. Принудительное прерывание может оставить неполный результат.",Text);}};
  FormClosed+=(s,e)=>{timer.Stop();timer.Dispose();foreach(var form in modules)form.Dispose();};
  string state=AppStorage.FilePath("unified-model.txt");if(File.Exists(state)){string saved=File.ReadAllText(state).Trim();if(File.Exists(saved))SetModel(saved);}SelectModule(0);InstallLanguages(top);LocalizedUi.Apply(this);
 }
 void InstallLanguages(Panel top){
  var uiCaption=new Label{Text="Язык интерфейса",Location=new Point(630,10),Size=new Size(180,20),ForeColor=Color.DimGray,Anchor=AnchorStyles.Top|AnchorStyles.Right};var docCaption=new Label{Text="Язык документации",Location=new Point(850,10),Size=new Size(205,20),ForeColor=Color.DimGray,Anchor=AnchorStyles.Top|AnchorStyles.Right};top.Controls.Add(uiCaption);top.Controls.Add(docCaption);
  uiLanguage.SetBounds(630,34,190,28);docLanguage.SetBounds(850,34,205,28);foreach(var box in new[]{uiLanguage,docLanguage}){box.DropDownStyle=ComboBoxStyle.DropDownList;box.Anchor=AnchorStyles.Top|AnchorStyles.Right;box.Items.AddRange(LanguageCatalog.Names);top.Controls.Add(box);}
  uiLanguage.SelectedIndex=Array.IndexOf(LanguageCatalog.Codes,LanguagePreferences.Ui);docLanguage.SelectedIndex=Array.IndexOf(LanguageCatalog.Codes,LanguagePreferences.Documents);
  uiLanguage.SelectedIndexChanged+=(s,e)=>{LanguagePreferences.Ui=LanguageCatalog.Codes[uiLanguage.SelectedIndex];LanguagePreferences.Save();SelectModule(selected);LocalizedUi.Apply(this);};
  docLanguage.SelectedIndexChanged+=(s,e)=>{LanguagePreferences.Documents=LanguageCatalog.Codes[docLanguage.SelectedIndex];LanguagePreferences.Save();};
 }
 string GetModel(){if(String.IsNullOrWhiteSpace(source))PickModel();return source;}
 void SetModel(string value){source=value;path.Text=value;dxf.SetModel(value);File.WriteAllText(AppStorage.FilePath("unified-model.txt"),value);}
 void PickModel(){using(var dialog=new OpenFileDialog{Title=LocalizedUi.T("Выберите открытую и сохранённую деталь SolidWorks"),Filter="SolidWorks (*.sldprt)|*.sldprt",RestoreDirectory=true})if(dialog.ShowDialog(this)==DialogResult.OK)SetModel(dialog.FileName);}
 void SelectModule(int index){
  selected=index;for(int i=0;i<modules.Length;i++){modules[i].Visible=i==index;navigation[i].BackColor=i==index?Blue:Color.FromArgb(246,248,251);navigation[i].ForeColor=i==index?Color.White:Ink;navigation[i].Font=new Font("Segoe UI",9,i==index?FontStyle.Bold:FontStyle.Regular);}modules[index].BringToFront();
  title.Text=new[]{"Развёртки листовых деталей","Нарезка профилей","DXF по выбранным граням"}[index];
  hint.Text=new[]{"Получите плоские контуры листовых тел. Сплайны заменяются дугами с допуском 0,01 мм.","Получите длины, количество, H и схемы углов для резки профилей, а также общую ведомость.","Получите один DXF на тело со всеми выбранными параллельными гранями и контурами карманов."}[index];
  string[][] steps={
   new[]{"Откройте нужную деталь в SolidWorks и сохраните изменения.","Нажмите «Выбрать модель» вверху и укажите этот же файл SLDPRT.","Нажмите «Создать DXF». Дождитесь завершения и откройте «DXF для работы»."},
   new[]{"Откройте и сохраните деталь с профилями в SolidWorks.","Выберите этот же файл вверху. Создавать таблицу вручную не нужно.","Нажмите «Создать таблицы профилей», затем «Открыть результат»."},
   new[]{"В SolidWorks активируйте сохранённую деталь, из которой нужны контуры.","Через Ctrl выделите плоские грани. Грани одного тела должны быть параллельны.","Нажмите «Экспортировать выбранные грани», затем «Открыть результат»."}
  };
  for(int i=0;i<3;i++)instructions[i].Text=steps[index][i];
  filesHint.Text=(index==0||index==2)?"ГДЕ ИСКАТЬ ФАЙЛЫ\n\nПапка модели →\nDXF, DXF1, DXF2…\n\nDXF — файлы для работы":"ГДЕ ИСКАТЬ ФАЙЛЫ\n\nПапка модели →\nимя проекта_1, _2…\n\nDXF — файлы для работы\nProfile — чертёж и PDF\nDXF\\Faces — выбранные грани\n\nВременные файлы:\nимя проекта_temp";
  destination.Text=new[]{"Результат: папка DXF, DXF1… с обработанными файлами; Q — количество.","Результат: Profile — чертёж с карточками и ведомостью + PDF.","Результат: DXF, DXF1… — файлы с именами тел SolidWorks."}[index];
  path.Enabled=index!=2;choose.Enabled=index!=2&&!Working();
  modelCaption.Text=index==2?"ИСТОЧНИК — активная деталь и выделенные грани в SolidWorks. Путь ниже в этом режиме не используется.":"НАЧНИТЕ ЗДЕСЬ  ·  Откройте и сохраните деталь в SolidWorks, затем выберите тот же файл";
  dxf.RefreshLanguage();LocalizedUi.Apply(this);
 }
 [STAThread]static int Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);if(args.Length==1&&args[0]=="--self-test"){try{using(var f=new UnifiedLauncher()){f.Show();Application.DoEvents();UiRegressionChecks.Run(f.dxf);f.AddCandidateTools();UiAcceptance.Run(f);LayoutChecks.Run(f);for(int i=0;i<f.modules.Length;i++){f.SelectModule(i);if(f.instructions.Any(l=>String.IsNullOrWhiteSpace(l.Text))||f.destination.Text.Length<20||f.path.Enabled!=(i!=2))throw new Exception("Instruction/context check failed");}f.help.PerformClick();if(f.guide.Visible||f.heading.Height!=101)throw new Exception("Guide collapse failed");f.help.PerformClick();if(!f.guide.Visible||f.heading.Height!=205)throw new Exception("Guide expand failed");}return 0;}catch(Exception ex){File.WriteAllText(AppStorage.FilePath("self-test-error.txt"),ex.ToString());return 1;}}if((args.Length==2||args.Length==3)&&args[0]=="--preview"){if(args.Length==3){if(!LanguageCatalog.Valid(args[2]))return 1;LanguagePreferences.Ui=args[2];}using(var form=new UnifiedLauncher()){form.Show();Application.DoEvents();for(int i=0;i<form.modules.Length;i++){form.SelectModule(i);Application.DoEvents();form.Refresh();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(args[1]+"-"+i+".png");}}}return 0;}try{ProductEnvironment.Configure(AppDomain.CurrentDomain.BaseDirectory);var app=new UnifiedLauncher();app.AddCandidateTools();Application.Run(app);return 0;}catch(Exception ex){MessageBox.Show(ex.Message,"SolidWorksBREP — проверка запуска",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}}
}
