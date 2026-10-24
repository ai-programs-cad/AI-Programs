using System;
using Environment = System.Environment;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Diagnostics;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public static class ProfilesAuto {
 const string MacroHash="741E1216878BD4C150F88223C27B781E5053E291F362F7DEDCA450ACE614E2AB";
 static JavaScriptSerializer J=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=150};
 static object[] A(object x){return NativeWeldmentAdapter.Items(x);}
 static string Hash(string p){return ManufacturingSourceFilesV03.Hash(p,"PROFILES_AUTO");}
 static string Number(double n){return n.ToString("0.###",CultureInfo.InvariantCulture);}
 static void Stage(int n,string s){Console.WriteLine("@@PROFILE|"+n+"|"+s);}
 static void Save(string p,object o){File.WriteAllText(p,J.Serialize(o),new UTF8Encoding(false));}
 static string Q(string s){return "\""+(s??"").Replace("\"","\"\"")+"\"";}
 static void Csv(string p,IEnumerable<string[]> rows){File.WriteAllLines(p,rows.Select(r=>String.Join(";",r.Select(Q))),new UTF8Encoding(true));}
 static void SaveDoc(ModelDoc2 d,string p){int e=0,w=0;if(!d.Extension.SaveAs(p,0,1,null,ref e,ref w)||e!=0||!File.Exists(p))throw new Exception("SAVE_FAILED: "+p+" errors="+e+" warnings="+w);}
 static void SavePdf(SldWorks sw,ModelDoc2 drawing,string path){var sheets=A(((DrawingDoc)drawing).GetSheetNames()).Select(Convert.ToString).ToArray();if(sheets.Length==0||sheets.Any(String.IsNullOrWhiteSpace))throw new Exception("PDF_DRAWING_SHEETS_MISSING");var data=(ExportPdfData)sw.GetExportFileData((int)swExportDataFileType_e.swExportPdfData);data.ViewPdfAfterSaving=false;if(!data.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportSpecifiedSheets,sheets)||data.GetWhichSheets()!=(int)swExportDataSheetsToExport_e.swExportData_ExportSpecifiedSheets||!A(data.GetSheets()).Select(Convert.ToString).SequenceEqual(sheets))throw new Exception("PDF_ALL_SHEETS_SELECTION_FAILED");int e=0,w=0;if(!drawing.Extension.SaveAs(path,0,1,data,ref e,ref w)||e!=0||!File.Exists(path))throw new Exception("PDF_ALL_SHEETS_EXPORT_FAILED: "+e);}
 // A native drawing can display more decimal places than Get6. Never accept
 // a coarser table value or a difference in length, quantity or direction.
 static bool AngleDisplayAgrees(string property,string table){
  var p=Regex.Match(property??"",@"^\s*(\d+)(?:[.,](\d+))?°\s*$");var t=Regex.Match(table??"",@"^\s*(\d+)(?:[.,](\d+))?°\s*$");if(!p.Success||!t.Success)return false;
  int digits=p.Groups[2].Value.Length;if(t.Groups[2].Value.Length<digits||digits>10)return false;
  decimal pv,tv;if(!Decimal.TryParse(property.Replace("°","").Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out pv)||!Decimal.TryParse(table.Replace("°","").Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out tv)||pv>90||tv>90)return false;
  return Decimal.Round(tv,digits,MidpointRounding.AwayFromZero)==pv;
 }
 static Feature[] Walk(ModelDoc2 d){
  var list=new List<Feature>();var seen=new HashSet<int>();var stack=new Stack<Feature>();var top=new List<Feature>();
  for(var f=(Feature)d.FirstFeature();f!=null;f=(Feature)f.GetNextFeature()){top.Add(f);if(top.Count>10000)throw new Exception("Feature root cycle/limit");}
  foreach(var f in top.AsEnumerable().Reverse())stack.Push(f);
  while(stack.Count>0){var f=stack.Pop();if(!seen.Add(f.GetID()))continue;list.Add(f);if(list.Count>10000)throw new Exception("Feature limit");int count=0;for(var c=(Feature)f.GetFirstSubFeature();c!=null;c=(Feature)c.GetNextSubFeature()){stack.Push(c);if(++count>10000)throw new Exception("Feature child cycle/limit");}}
  return list.ToArray();
 }
 static Dictionary<string,NativeCutListValue> Properties(Feature f){
  var p=new Dictionary<string,NativeCutListValue>(StringComparer.OrdinalIgnoreCase);var m=f.CustomPropertyManager;
  foreach(string n in A(m.GetNames()).Select(Convert.ToString)){string raw,resolved;bool was,linked;int code=m.Get6(n,false,out raw,out resolved,out was,out linked);p[n]=new NativeCutListValue{name=n,raw=raw,resolved=resolved,wasResolved=was,linked=linked,resultCode=code};}return p;
 }
 static string Read(Dictionary<string,NativeCutListValue> p,params string[] aliases){
  foreach(var n in aliases){NativeCutListValue v;if(p.TryGetValue(n,out v))return v.resolved;}
  // Localized display names can still expose an invariant native link.
  foreach(var n in aliases)foreach(var v in p.Values)if((v.raw??"").IndexOf(n+"@@@",StringComparison.OrdinalIgnoreCase)>=0)return v.resolved;
  return null;
 }
 static string PropertyName(Dictionary<string,NativeCutListValue> p,params string[] aliases){
  foreach(var name in aliases)if(p.ContainsKey(name))return p[name].name;
  foreach(var name in aliases)foreach(var value in p.Values)if((value.raw??"").IndexOf(name+"@@@",StringComparison.OrdinalIgnoreCase)>=0)return value.name;
  throw new Exception("NATIVE_CUT_LIST_PROPERTY_NOT_FOUND: "+String.Join(" / ",aliases));
 }
 static void ConfigureColumn(TableAnnotation table,WeldmentCutListAnnotation native,int column,string property,string header){
  if(column==0&&property==null){
   table.SetColumnType2(column,(int)swTableColumnTypes_e.swWeldTableColumnType_CutListName,true);
   if(table.GetColumnType2(column,true)!=(int)swTableColumnTypes_e.swWeldTableColumnType_CutListName)throw new Exception("NATIVE_CUT_LIST_NAME_COLUMN_BINDING_FAILED");
   table.SetColumnTitle2(column,header,true);if(table.GetColumnTitle2(column,true)!=header)throw new Exception("NATIVE_CUT_LIST_NAME_HEADER_FAILED");return;
  }
  bool set=native.SetColumnCustomProperty(column,property);
  if(!set||native.GetColumnCustomProperty(column)!=property){table.SetColumnType2(column,(int)swTableColumnTypes_e.swWeldTableColumnType_CustomProperty,true);set=native.SetColumnCustomProperty(column,property);}
  if(!set||native.GetColumnCustomProperty(column)!=property)throw new Exception("NATIVE_PROPERTY_BINDING_FAILED: column="+column+" property="+property+" type="+table.GetColumnType2(column,true));
  table.SetColumnTitle2(column,header,true);if(table.GetColumnTitle2(column,true)!=header)throw new Exception("NATIVE_HEADER_READBACK_FAILED: "+column);
 }
 static string Template(SldWorks sw){
  string preferred=sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);if(File.Exists(preferred))return preferred;
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SOLIDWORKS");
  if(Directory.Exists(root)){var candidates=Directory.GetFiles(root,"*.drwdot",SearchOption.AllDirectories).OrderByDescending(x=>x.IndexOf("2025",StringComparison.OrdinalIgnoreCase)>=0).ThenByDescending(x=>String.Equals(Path.GetFileName(x),"Draw.drwdot",StringComparison.OrdinalIgnoreCase)).ToArray();if(candidates.Length>0)return candidates[0];}
  throw new Exception("Шаблон чертежа DRWDOT не найден. Настройте стандартный шаблон чертежа в SolidWorks.");
 }
 static TableAnnotation[] Tables(ModelDoc2 d){return A(((DrawingDoc)d).GetViews()).SelectMany(A).Cast<SolidWorks.Interop.sldworks.View>().SelectMany(v=>A(v.GetTableAnnotations())).Cast<TableAnnotation>().ToArray();}
 static WeldmentCutListAnnotation InsertNativeTable(SldWorks sw,SolidWorks.Interop.sldworks.View view,string ownedRoot,string model,string config,string drawingTemplate){
  // Preserve the old seam's ownership/reference protections without its literal Workspace folder name.
  if(!Directory.Exists(ownedRoot)||!NativeWeldmentAdapter.Inside(ownedRoot,model)||!File.Exists(model))throw new Exception("OWNED_PROFILE_MODEL_REQUIRED");
  for(string p=Path.GetFullPath(ownedRoot);p!=null;p=Path.GetDirectoryName(p))if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new Exception("REPARSE_PROFILE_FOLDER_REJECTED");
  var drawing=sw.ActiveDoc as ModelDoc2;var loaded=sw.GetOpenDocumentByName(model) as ModelDoc2;
  if(drawing==null||drawing.GetType()!=3||!NativeWeldmentAdapter.Inside(ownedRoot,drawing.GetPathName())||loaded==null||loaded.GetConfigurationByName(config)==null)throw new Exception("OWNED_DRAWING_AND_MODEL_REQUIRED");
  if(!ManufacturingContextV02.SamePath(view.GetReferencedModelName(),model)||view.ReferencedConfiguration!=config||!A(((DrawingDoc)drawing).GetViews()).SelectMany(A).Any(v=>sw.IsSame(v,view)==1))throw new Exception("EXACT_NATIVE_VIEW_OWNERSHIP_REQUIRED");
  string folder=Path.GetDirectoryName(drawingTemplate);var candidates=Directory.GetFiles(folder,"*.sldwldtbt",SearchOption.TopDirectoryOnly);
  if(candidates.Length==0){string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"SOLIDWORKS");if(Directory.Exists(root))candidates=Directory.GetFiles(root,"*.sldwldtbt",SearchOption.AllDirectories);}
  string nativeTemplate=candidates.OrderByDescending(p=>p.IndexOf("2025",StringComparison.OrdinalIgnoreCase)>=0).ThenBy(p=>p,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
  if(nativeTemplate==null)throw new Exception("Штатный шаблон Weldment Cut List (*.sldwldtbt) не найден.");
  var table=view.InsertWeldmentTable(false,.02,.25,1,config,nativeTemplate);if(table==null)throw new Exception("NATIVE_WELDMENT_TABLE_CREATE_FAILED: "+nativeTemplate);return table;
 }
 static string[][] Cells(TableAnnotation t){return Enumerable.Range(0,t.RowCount).Select(r=>Enumerable.Range(0,t.ColumnCount).Select(c=>t.get_DisplayedText2(r,c,true)).ToArray()).ToArray();}
 static void AddReadableSchedule(SldWorks sw,ModelDoc2 drawing,string root,string model,string config,string template,string isoName,string[] properties,string[] headers,string[][] expected,Dictionary<string,object> report){
  var dd=(DrawingDoc)drawing;double width=.420,height=Math.Max(.297,.085+expected.Length*.012);
  string name=LanguageCatalog.T("Ведомость профилей",LanguageCatalog.DocumentLanguage);
  if(!dd.NewSheet3(name,(int)swDwgPaperSizes_e.swDwgPapersUserDefined,(int)swDwgTemplates_e.swDwgTemplateNone,1,1,false,"",width,height,"")||!dd.ActivateSheet(name))throw new Exception("PROFILE_SCHEDULE_SHEET_CREATE_FAILED");
  ((Sheet)dd.GetCurrentSheet()).SheetFormatVisible=false;
  var view=dd.CreateDrawViewFromModelView3(model,isoName,width+.2,height/2,0);if(view==null)throw new Exception("PROFILE_SCHEDULE_VIEW_CREATE_FAILED");view.ReferencedConfiguration=config;
  var native=InsertNativeTable(sw,view,root,model,config,template);var table=(TableAnnotation)(object)native;
  while(table.ColumnCount<10)if(!table.InsertColumn2((int)swTableItemInsertPosition_e.swTableItemInsertPosition_Last,0,"",0))throw new Exception("SCHEDULE_COLUMN_INSERT_FAILED");
  while(table.ColumnCount>10)if(!table.DeleteColumn(table.ColumnCount-1))throw new Exception("SCHEDULE_COLUMN_DELETE_FAILED");
  var displayHeaders=LanguageCatalog.ScheduleHeaders(LanguageCatalog.DocumentLanguage);for(int c=0;c<10;c++)ConfigureColumn(table,native,c,properties[c],displayHeaders[c]);
  var font=table.GetTextFormat();font.TypeFaceName="Arial";font.CharHeight=.0035;font.Bold=false;if(!table.SetTextFormat(false,font))throw new Exception("SCHEDULE_TEXT_FORMAT_FAILED");
  double[] widths={.032,.062,.022,.043,.043,.052,.025,.030,.025,.052};
  for(int c=0;c<10;c++)table.SetColumnWidth(c,widths[c],0);
  for(int r=0;r<table.RowCount;r++){
   table.SetRowHeight(r,r==0?.028:.012,0);
   for(int c=0;c<10;c++){table.set_CellTextHorizontalJustification(r,c,c==1?(int)swTextJustification_e.swTextJustificationLeft:(int)swTextJustification_e.swTextJustificationCenter);if(r==0){var f=table.GetCellTextFormat(r,c);f.Bold=true;f.CharHeight=.0035;if(!table.SetCellTextFormat(r,c,false,f))throw new Exception("SCHEDULE_HEADER_FORMAT_FAILED");}}
  }
  if(!table.GetAnnotation().SetPosition2(.015,height-.040,0))throw new Exception("SCHEDULE_POSITION_FAILED");
  string lang=LanguageCatalog.DocumentLanguage;var title=(Note)drawing.InsertNote(LanguageCatalog.T("ВЕДОМОСТЬ ПРОФИЛЕЙ",lang)+"\n"+LanguageCatalog.T("Конфигурация",lang)+": "+config+". "+LanguageCatalog.T("Размеры — мм; углы — исходные ANGLE модели.",lang)+"\n"+LanguageCatalog.T("Направление реза",lang)+": Противоположный = "+LanguageCatalog.T("Противоположный",lang)+"; Такой же = "+LanguageCatalog.T("Такой же",lang));if(title==null||!((Annotation)title.GetAnnotation()).SetPosition2(.015,height-.012,0))throw new Exception("SCHEDULE_TITLE_FAILED");
  drawing.ForceRebuild3(false);var actual=Cells(table);if(actual.Length!=expected.Length||!actual.Skip(1).SelectMany(x=>x).SequenceEqual(expected.Skip(1).SelectMany(x=>x)))throw new Exception("SCHEDULE_NATIVE_VALUES_DIFFER_AFTER_FORMATTING");
  double tw=Enumerable.Range(0,table.ColumnCount).Sum(c=>table.GetColumnWidth(c)),th=Enumerable.Range(0,table.RowCount).Sum(r=>table.GetRowHeight(r));if(tw>width-.030+.001||th>height-.055)throw new Exception("SCHEDULE_TABLE_EXCEEDS_SHEET");
  report["readableSchedule"]=new{sheet=name,widthMm=width*1000,heightMm=height*1000,fontHeightMm=3.5,nativeLinkedTable=true,dataRows=table.RowCount-1,valuesUnchanged=true,documentationLanguage=lang,displayHeaders};
 }
 public static string TranslateProductionCell(int row,string before,string language){
  if(String.IsNullOrEmpty(before)||(row!=2&&row!=3))return before;string after=before;
  if(row==2)after=before.Replace("PROFILE ANGLE",LanguageCatalog.T("PROFILE ANGLE",language));else if(new[]{"CUT LIST","LENGTH","QTY"}.Contains(before.Trim()))after=LanguageCatalog.T(before.Trim(),language);
  if(!Regex.Matches(before,@"\d+(?:[.,]\d+)?").Cast<Match>().Select(m=>m.Value).SequenceEqual(Regex.Matches(after,@"\d+(?:[.,]\d+)?").Cast<Match>().Select(m=>m.Value)))throw new Exception("DOCUMENT_TRANSLATION_CHANGED_NUMBERS");return after;
 }
 static void TranslateProductionTables(IEnumerable<TableAnnotation> tables){foreach(var table in tables)for(int r=0;r<table.RowCount;r++)for(int c=0;c<table.ColumnCount;c++){
  // Names, section designations and numeric rows must remain byte-identical.
  if(r!=2&&r!=3)continue;string before=table.get_Text(r,c),after=TranslateProductionCell(r,before,LanguageCatalog.DocumentLanguage);
  if(after!=before)table.set_Text(r,c,after);
 }}
 static string Reserve(string project){string name=new DirectoryInfo(project).Name,temp=Path.Combine(project,name+"_temp");Directory.CreateDirectory(temp);using(var g=new FileStream(Path.Combine(temp,"layout.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){int n=1;string p;do{p=Path.Combine(project,name+"_"+n++);}while(Directory.Exists(p)||File.Exists(p));Directory.CreateDirectory(p);return p;}}
 static void Activate(SldWorks sw,ModelDoc2 d){int e=0;var a=sw.ActivateDoc3(d.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref e);if(a==null||sw.IsSame(a,d)!=1)throw new Exception("ACTIVATION_FAILED: "+e);}
 static string WeldedConfiguration(ModelDoc2 part,string requested){
  var source=(Configuration)part.GetConfigurationByName(requested);if(source.Type==(int)swConfigurationType_e.swConfiguration_AsWelded)return source.Name;
  var candidates=A(part.GetConfigurationNames()).Select(Convert.ToString).Select(n=>(Configuration)part.GetConfigurationByName(n)).Where(c=>c.Type==(int)swConfigurationType_e.swConfiguration_AsWelded&&c.GetParent()!=null&&c.GetParent().Name==requested).ToArray();
  if(candidates.Length!=1)throw new Exception("NATIVE_AS_WELDED_CONFIGURATION_NOT_UNIQUE_FOR_REQUESTED_PARENT: "+requested);return candidates[0].Name;
 }
 const string SquareHStatus="ASSIGNED_SQUARE_WIDTH_HEIGHT_CONFIRMED_FOR_H_ONLY; WALL_AND_MACHINING_NOT_CERTIFIED";
 static void RecognizeSquareForH(SectionComparison s){
  if(s.NOMINAL_WIDTH.HasValue||s.NOMINAL_HEIGHT.HasValue||!s.bodyPathAssociationVerified||!s.orientedFrameVerified)return;
  string designation=String.IsNullOrWhiteSpace(s.PROFILE_CONFIGURATION)?Path.GetFileNameWithoutExtension(s.PROFILE_PATH):s.PROFILE_CONFIGURATION;
  var match=Regex.Match(designation??"",@"^\s*(\d+(?:[.,]\d+)?)\s*[xXхХ×]\s*(\d+(?:[.,]\d+)?)\s+(?:square|квадрат)\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);if(!match.Success)return;
  double width=Double.Parse(match.Groups[1].Value.Replace(',','.'),CultureInfo.InvariantCulture),height=Double.Parse(match.Groups[2].Value.Replace(',','.'),CultureInfo.InvariantCulture);
  if(Math.Abs(width-height)>.001||!s.BOUNDING_BOX_WIDTH.HasValue||!s.BOUNDING_BOX_HEIGHT.HasValue||!NativeSectionCheck.ExtentsMatch(s.BOUNDING_BOX_WIDTH.Value,s.BOUNDING_BOX_HEIGHT.Value,width,height,.001))return;
  s.NOMINAL_WIDTH=width;s.NOMINAL_HEIGHT=height;s.NOMINAL_WALL_THICKNESS=null;s.CROSS_SECTION_VERIFICATION_STATUS=SquareHStatus;
  s.warnings.Add("Square WxH from assigned StructuralMember API profile designation verified against native transverse extrema. This authorizes H/table input only: square orientation does not depend on wall thickness. Wall thickness, reliefs and additional machining are not certified; review the part drawing.");
 }
 static bool SuppliesH(SectionComparison s){return s.sectionChecksPassed||s.CROSS_SECTION_VERIFICATION_STATUS==SquareHStatus;}
 class Item {
  public Feature folder;public Body2[] bodies;public string name,section,length,angle1,angle2,quantity,material,direction,rotation;public string[] nativeCells;public Dictionary<string,NativeCutListValue> properties;public List<object> bodyEvidence=new List<object>();public bool eligible=true;public List<string> issues=new List<string>();public double width,height;public double? lengthNumber;
 }
 public static string GroupTitle(string section,string folder){return section+" | Cut List "+folder+" | обработка: проверить";}
 public static bool DirectionRequired(double? first,double? second){return !first.HasValue||!second.HasValue||(Math.Abs(first.Value)>.000001&&Math.Abs(second.Value)>.000001);}
 public static void SelfTest(){
  if(DirectionRequired(0,0)||DirectionRequired(45,0)||DirectionRequired(0,45)||!DirectionRequired(45,45)||!DirectionRequired(null,0))throw new Exception("Cut direction requirement failed");
  Console.WriteLine("Straight/slanted cut direction policy: 5/5 PASS; no COM");
  if(GroupTitle("40x20x1.5","1")==GroupTitle("40x20x1.5","2"))throw new Exception("Unverified machining merged");
  if(NativeWeldmentAdapter.NativeNumber("-",true)!=null)throw new Exception("Undefined angle coerced");
  if(NativeWeldmentAdapter.NativeNumber("0°",true)!=0)throw new Exception("Straight angle changed");
  if(!AngleDisplayAgrees("54.2°","54.22°")||!AngleDisplayAgrees("36.9°","36.85°")||!AngleDisplayAgrees("45°","45°")||AngleDisplayAgrees("54.2°","54.26°")||AngleDisplayAgrees("54.22°","54.2°")||AngleDisplayAgrees("-","0°")||AngleDisplayAgrees("90°","91°"))throw new Exception("Native angle precision validation failed");
  Console.WriteLine("Native angle precision: 7/7 PASS; no COM");
  Console.WriteLine("Automatic profiles grouping/undefined angles: 3/3 PASS; no COM");
  var square=new SectionComparison{PROFILE_PATH=@"C:\NativeProfiles\15x15 square.sldlfp",bodyPathAssociationVerified=true,orientedFrameVerified=true,BOUNDING_BOX_WIDTH=15,BOUNDING_BOX_HEIGHT=15,PLANAR_WALL_SPACING_U=1.5,PLANAR_WALL_SPACING_V=1.5};RecognizeSquareForH(square);
  if(!SuppliesH(square)||square.NOMINAL_WIDTH!=15||square.NOMINAL_HEIGHT!=15||square.NOMINAL_WALL_THICKNESS.HasValue||square.sectionChecksPassed)throw new Exception("Square H scope or nominal wall invented");
  var mismatch=new SectionComparison{PROFILE_PATH=@"C:\NativeProfiles\15x15 square.sldlfp",bodyPathAssociationVerified=true,orientedFrameVerified=true,BOUNDING_BOX_WIDTH=15,BOUNDING_BOX_HEIGHT=16,PLANAR_WALL_SPACING_U=1.5,PLANAR_WALL_SPACING_V=1.5};RecognizeSquareForH(mismatch);if(SuppliesH(mismatch))throw new Exception("Mismatched square accepted");
  Console.WriteLine("Square H provenance/unspecified nominal wall: 2/2 PASS; no COM");
  var relief=new SectionComparison{PROFILE_PATH=@"C:\NativeProfiles\15x15 square.sldlfp",bodyPathAssociationVerified=true,orientedFrameVerified=true,BOUNDING_BOX_WIDTH=15,BOUNDING_BOX_HEIGHT=15,PLANAR_WALL_SPACING_U=1.5,PLANAR_WALL_SPACING_V=null};RecognizeSquareForH(relief);
  if(!SuppliesH(relief)||relief.sectionChecksPassed||relief.NOMINAL_WALL_THICKNESS.HasValue)throw new Exception("Square H requires unrelated wall certification");
  Console.WriteLine("Square H with unverified wall/relief: PASS; machining remains unverified");
 }
 public static int Run(string source){
  SldWorks sw=null;ModelDoc2 original=null,copy=null,drawing=null;string result=null,reports=null,hash=null,config=null,ownedDrawingPath=null;int stamp=0;var clock=Stopwatch.StartNew();var report=new Dictionary<string,object>{{"status","FAILED"},{"productionReady",false},{"mode","AUTO_PART_NATIVE_WELDMENT_CUT_LIST_THEN_ORIGINAL_PROFILES_H"},{"macroSHA256",MacroHash},{"originalMacroModified",false},{"materialUsedForGrouping",false}};int exit=1;
  try {
   Stage(3,"Подключение к SolidWorks и проверка исходника");sw=ExistingSolidWorksConnection.Attach();source=Path.GetFullPath(source);original=sw.GetOpenDocumentByName(source) as ModelDoc2;
   if(original==null)original=A(sw.GetDocuments()).Cast<ModelDoc2>().SingleOrDefault(d=>ManufacturingContextV02.SamePath(d.GetPathName(),source));
   if(original==null||original.GetType()!=1)throw new Exception("Откройте выбранную деталь SLDPRT в SolidWorks.");if(original.GetSaveFlag())throw new Exception("Сохраните изменения исходной детали перед запуском. Программа оригинал не сохраняет.");
   hash=Hash(source);config=original.ConfigurationManager.ActiveConfiguration.Name;stamp=original.GetUpdateStamp();report["source"]=source;report["sourceSHA256Before"]=hash;report["configuration"]=config;
   if(A(sw.GetDocumentDependencies2(source,false,false,false)).Length>0)throw new Exception("EXTERNAL_DEPENDENCIES_REQUIRE_PACK_AND_GO: автоматическое копирование этой детали со внешними ссылками пока не подтверждено.");
   string macro=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"Resources\Profiles+h.swp");if(Hash(macro)!=MacroHash)throw new Exception("Profiles+h.swp checksum differs");
   string template=Template(sw),project=Path.GetDirectoryName(source);result=SharedResultFolder.Reserve(project,source,hash,config,"Profile");string work=Path.Combine(result,"Profile"),models=Path.Combine(work,"Models"),temp=Path.Combine(project,new DirectoryInfo(project).Name+"_temp","ProfilesAuto_"+Guid.NewGuid().ToString("N"));reports=Path.Combine(temp,"Reports");JobOwnership.OwnDirectory(temp,project);Directory.CreateDirectory(models);Directory.CreateDirectory(reports);report["resultFolder"]=result;report["serviceFolder"]=temp;
   Stage(10,"Создание рабочей копии модели");string model=Path.Combine(models,"Profiles_"+Guid.NewGuid().ToString("N").Substring(0,8)+".SLDPRT");ManufacturingSourceFilesV03.CopySavedSource(source,model,hash,false);File.SetAttributes(model,FileAttributes.Normal);
   int e=0,w=0;copy=sw.OpenDoc6(model,1,1,config,ref e,ref w);if(copy==null||e!=0||copy.ConfigurationManager.ActiveConfiguration.Name!=config)throw new Exception("WORK_MODEL_OPEN_FAILED: "+e);Activate(sw,copy);
   string productionConfig=WeldedConfiguration(copy,config);report["productionConfiguration"]=productionConfig;
   if(copy.ConfigurationManager.ActiveConfiguration.Name!=productionConfig&&(!copy.ShowConfiguration2(productionConfig)||copy.ConfigurationManager.ActiveConfiguration.Name!=productionConfig))throw new Exception("WORK_COPY_AS_WELDED_SWITCH_FAILED");
   var allBodies=A(((PartDoc)copy).GetBodies2(0,false)).Cast<Body2>().ToArray();if(allBodies.Length==0)throw new Exception("NO_SOLID_BODIES");
   var items=new List<Item>();var skipped=new List<object>();var cachedBodies=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);
   string cachePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles-evidence-cache.json");
   if(File.Exists(cachePath))try{
    var cache=J.Deserialize<Dictionary<string,object>>(File.ReadAllText(cachePath));var previous=AB.Obj(cache["report"]);
    if(Convert.ToString(cache["supportSHA256"])==Hash(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ManufacturingAgentExportCompare.exe"))&&Convert.ToString(previous["sourceSHA256Before"])==hash&&Convert.ToString(previous["configuration"])==config&&Convert.ToString(previous.ContainsKey("productionConfiguration")?previous["productionConfiguration"]:previous["configuration"])==productionConfig&&AB.Bool(previous["sourceFileUnchanged"])&&AB.Bool(previous["sourceMemoryUnchanged"])){
     foreach(var folder in A(previous["folders"]))foreach(var body in A(AB.Obj(folder)["bodyEvidence"])){var row=AB.Obj(body);if(AB.Bool(AB.Obj(row["section"])["sectionChecksPassed"]))cachedBodies.Add(Convert.ToString(row["body"]),row);}
     report["reusedProfileEvidenceFrom"]=cache["sourceReport"];
    }
   }catch(Exception cacheError){report["cacheIgnoredReason"]=cacheError.Message;cachedBodies.Clear();}
   foreach(var f in Walk(copy).Where(f=>f.GetTypeName2()=="CutListFolder")){
    var bodies=A(((BodyFolder)f.GetSpecificFeature2()).GetBodies()).Cast<Body2>().ToArray();var profiles=bodies.Where(b=>!b.IsSheetMetal()&&A(b.GetFeatures()).Cast<Feature>().Any(x=>x.GetTypeName2()=="WeldMemberFeat")).ToArray();
    if(profiles.Length==0){skipped.Add(new{folder=f.Name,reason="NOT_A_STRUCTURAL_MEMBER_PROFILE",bodyCount=bodies.Length});continue;}
    var p=Properties(f);var item=new Item{folder=f,bodies=bodies,name=f.Name,properties=p,section=Read(p,"Description","Описание"),length=Read(p,"LENGTH","ДЛИНА"),angle1=Read(p,"ANGLE1","УГОЛ1"),angle2=Read(p,"ANGLE2","УГОЛ2"),quantity=Read(p,"QUANTITY","КОЛИЧЕСТВО"),material=Read(p,"MATERIAL","Материал"),direction=Read(p,"ANGLE DIRECTION","Направление угла"),rotation=Read(p,"ANGLE ROTATION","Вращение угла")};
    var groupClock=Stopwatch.StartNew();
    if(bodies.Any(b=>b.IsSheetMetal()))item.issues.Add("MIXED_SHEET_METAL_AND_PROFILE_BODIES");
    var representative=profiles[0];
    // Compare complete geometry; patterned bodies need not own a WeldMember feature.
    foreach(var other in bodies.Where(b=>!Object.ReferenceEquals(b,representative))){
     MathTransform transform=null;bool coincident=false;string reason=null;
     try{coincident=representative.GetCoincidenceTransform2(other,out transform)&&transform!=null;}catch(Exception ex){reason=ex.Message;}
     item.bodyEvidence.Add(new{body=other.Name,representative=representative.Name,geometryCoincidenceVerified=coincident,error=reason});
     if(!coincident)item.issues.Add("BODY_GEOMETRY_NOT_CONFIRMED_IDENTICAL: "+other.Name);
    }
    var nativeQuantity=NativeWeldmentAdapter.NativeNumber(item.quantity,false);
    if(!nativeQuantity.HasValue||Math.Abs(nativeQuantity.Value-bodies.Length)>.000001)item.issues.Add("NATIVE_QUANTITY_DIFFERS_FROM_BODY_COUNT");
    foreach(var b in new[]{representative}){Stage(18+Math.Min(24,items.Count),(cachedBodies.ContainsKey(b.Name)?"Подтверждённые данные профиля ":"Проверка профиля ")+b.Name);SectionComparison section;EndCutResult end;
     Dictionary<string,object> cached;if(cachedBodies.TryGetValue(b.Name,out cached)){section=J.ConvertToType<SectionComparison>(cached["section"]);end=J.ConvertToType<EndCutResult>(cached["endCuts"]);}else{var related=A(b.GetFeatures()).Cast<Feature>().ToArray();section=NativeSectionCheck.Read(sw,b,related);RecognizeSquareForH(section);var common=NativeWeldmentAdapter.Profile(f.Name,p,bodies.Length);end=NativeEndCutCheck.Read(sw,b,section,common);}item.bodyEvidence.Add(new{body=b.Name,section,Hdiagnostic=end.H,endCuts=end,additionalProcessing=end.machiningStatus,materialReference=item.material});
     RecognizeSquareForH(section);if(!SuppliesH(section)||!section.NOMINAL_WIDTH.HasValue||!section.NOMINAL_HEIGHT.HasValue){item.issues.Add("SECTION_NOT_VERIFIED: "+b.Name);continue;}item.width=section.NOMINAL_WIDTH.Value;item.height=section.NOMINAL_HEIGHT.Value;
    }
    if(String.IsNullOrWhiteSpace(item.section)||!NativeWeldmentAdapter.NativeNumber(item.length,false).HasValue||!NativeWeldmentAdapter.NativeNumber(item.angle1,true).HasValue||!NativeWeldmentAdapter.NativeNumber(item.angle2,true).HasValue||!NativeWeldmentAdapter.NativeNumber(item.quantity,false).HasValue)item.issues.Add("MISSING_NATIVE_DESCRIPTION_LENGTH_ANGLE_OR_QUANTITY");
    var angleOne=NativeWeldmentAdapter.NativeNumber(item.angle1,true);var angleTwo=NativeWeldmentAdapter.NativeNumber(item.angle2,true);
    if((String.IsNullOrWhiteSpace(item.direction)||item.direction.Trim()=="-")&&DirectionRequired(angleOne,angleTwo))item.issues.Add("NATIVE_ANGLE_DIRECTION_UNDEFINED");
    item.bodyEvidence.Add(new{groupValidationSeconds=groupClock.Elapsed.TotalSeconds,bodyCount=bodies.Length,representative=representative.Name});
    var boxWidth=NativeWeldmentAdapter.NativeNumber(Read(p,"3D-Ширина граничной рамки","3D-Bounding Box Width"),false);var boxHeight=NativeWeldmentAdapter.NativeNumber(Read(p,"3D-Высота граничной рамки","3D-Bounding Box Height"),false);
    if(!boxWidth.HasValue||!boxHeight.HasValue||!NativeSectionCheck.ExtentsMatch(boxWidth??0,boxHeight??0,item.width,item.height,.75))item.issues.Add("NATIVE_BOUNDING_BOX_DIMENSIONS_NOT_VERIFIED_FOR_MACRO");
    if(boxWidth.HasValue&&boxHeight.HasValue){item.width=boxWidth.Value;item.height=boxHeight.Value;}
    item.lengthNumber=NativeWeldmentAdapter.NativeNumber(item.length,false);item.eligible=item.issues.Count==0;
    items.Add(item);
   }
   report["folders"]=items.Select(x=>new{x.name,x.section,x.length,x.angle1,x.angle2,x.quantity,MATERIAL_ASSIGNED=x.material,ANGLE_DIRECTION=x.direction,ANGLE_ROTATION=x.rotation,x.eligible,x.issues,x.bodyEvidence,nativeProperties=x.properties}).ToArray();report["skippedNonProfiles"]=skipped;report["discoveredProfileFolders"]=items.Count;
   if(items.Count==0)throw new Exception("В модели не найдены профили Structural Member в актуальных папках Cut List.");SaveDoc(copy,model);
   Stage(45,"Создание чертежа и штатной Weldment Cut List");drawing=sw.NewDocument(template,0,0,0) as ModelDoc2;if(drawing==null)throw new Exception("DRAWING_CREATE_FAILED");string drawingPath=Path.Combine(work,"ProfileCutTables_"+Guid.NewGuid().ToString("N").Substring(0,8)+".SLDDRW");ownedDrawingPath=drawingPath;SaveDoc(drawing,drawingPath);
   var dd=(DrawingDoc)drawing;double paperWidth=0,paperHeight=0;((Sheet)dd.GetCurrentSheet()).GetSize(ref paperWidth,ref paperHeight);
   Activate(sw,copy);string isoName="ProfilesIso_"+Guid.NewGuid().ToString("N");copy.ShowNamedView2("",(int)swStandardViews_e.swIsometricView);copy.NameView(isoName);SaveDoc(copy,model);Activate(sw,drawing);
   report["isometricViewMethod"]="Native swIsometricView -> NameView on owned copy -> CreateDrawViewFromModelView3 with exact saved name";
   var view=dd.CreateDrawViewFromModelView3(model,isoName,paperWidth+.2,paperHeight*.5,0);if(view==null)throw new Exception("NAMED_ISOMETRIC_VIEW_CREATE_FAILED");view.ReferencedConfiguration=productionConfig;var refDoc=view.ReferencedDocument as ModelDoc2;if(refDoc==null||!ManufacturingContextV02.SamePath(refDoc.GetPathName(),model)||view.ReferencedConfiguration!=productionConfig)throw new Exception("VIEW_COPY_REFERENCE_NOT_CONFIRMED");
   var native=InsertNativeTable(sw,view,result,model,productionConfig,template);var table=(TableAnnotation)(object)native;if(!table.GetAnnotation().SetPosition2(paperWidth+.05,paperHeight-.02,0))throw new Exception("NATIVE_SOURCE_TABLE_POSITION_FAILED");
   while(table.ColumnCount<10)if(!table.InsertColumn2((int)swTableItemInsertPosition_e.swTableItemInsertPosition_Last,0,"",0))throw new Exception("NATIVE_TABLE_COLUMN_INSERT_FAILED");while(table.ColumnCount>10)if(!table.DeleteColumn(table.ColumnCount-1))throw new Exception("NATIVE_TABLE_COLUMN_DELETE_FAILED");
   var columnSource=items[0].properties;
   string[] propCols={null,PropertyName(columnSource,"MATERIAL","Материал"),PropertyName(columnSource,"QUANTITY","КОЛИЧЕСТВО"),PropertyName(columnSource,"3D-Ширина граничной рамки","3D-Bounding Box Width"),PropertyName(columnSource,"3D-Высота граничной рамки","3D-Bounding Box Height"),PropertyName(columnSource,"ANGLE DIRECTION","Направление угла"),PropertyName(columnSource,"ANGLE1","УГОЛ1"),PropertyName(columnSource,"LENGTH","ДЛИНА"),PropertyName(columnSource,"ANGLE2","УГОЛ2"),PropertyName(columnSource,"ANGLE DIRECTION","Направление угла")};
   string[] headers={"Имя списка вырезов","MATERIAL","Количество","3D-Ширина граничной рамки","3D-Высота граничной рамки","Направление угла","УГОЛ1","LENGTH","УГОЛ2","Направление угла"};
   report["positionIdentitySource"]="Native Cut List folder name via swWeldTableColumnType_CutListName; custom property N is not read or required";
   report["nativeColumnPlan"]=headers.Select((h,c)=>new{column=c,title=h,property=propCols[c]}).ToArray();Save(Path.Combine(reports,"native-table-before-columns.json"),NativeWeldmentAdapter.ReadNativeTable(native));
   for(int c=0;c<10;c++)ConfigureColumn(table,native,c,propCols[c],headers[c]);
   drawing.ForceRebuild3(false);var cells=Cells(table);Save(Path.Combine(reports,"native-weldment-table.json"),NativeWeldmentAdapter.ReadNativeTable(native));Csv(Path.Combine(reports,"native-weldment-table.csv"),cells);
   foreach(var item in items){var matches=cells.Where(row=>row.Length==10&&row[0]==item.name).ToArray();if(matches.Length!=1){item.eligible=false;item.issues.Add("NATIVE_TABLE_ROW_NAME_NOT_UNIQUE");continue;}item.nativeCells=matches[0];string[] wanted={item.material,item.quantity,Number(item.width),Number(item.height),item.direction,item.angle1,item.length,item.angle2,item.direction};if(!item.nativeCells.Skip(1).Select((value,index)=>index==5||index==7?AngleDisplayAgrees(wanted[index],value):value==(wanted[index]??"-")).All(x=>x)){item.eligible=false;item.issues.Add("NATIVE_TABLE_VALUE_READBACK_DIFFERS");}}
   report["nativeAngleDisplayAudit"]=items.Where(x=>x.nativeCells!=null).Select(x=>new{x.name,propertyAngle1=x.angle1,propertyAngle2=x.angle2,tableAngle1=x.nativeCells[6],tableAngle2=x.nativeCells[8],accepted=x.eligible}).ToArray();
   var eligible=items.Where(x=>x.eligible).OrderBy(x=>x.section,StringComparer.Ordinal).ThenBy(x=>x.angle1,StringComparer.Ordinal).ThenBy(x=>x.angle2,StringComparer.Ordinal).ThenByDescending(x=>x.lengthNumber).ToArray();
   report["processedProfileFolders"]=eligible.Length;report["reviewFolders"]=items.Where(x=>!x.eligible).Select(x=>new{x.name,x.issues}).ToArray();
   if(eligible.Length==0)throw new Exception("Ни одна строка не прошла проверку данных штатной таблицы. См. отчёт; чертёж и исходная таблица сохранены в рабочей папке.");
   var firstSheet=(Sheet)dd.GetCurrentSheet();firstSheet.SheetFormatVisible=false;var sheetProps=A(firstSheet.GetProperties2()).Select(Convert.ToDouble).ToArray();string sheetFormat=firstSheet.GetTemplateName();
   int columns=Math.Max(1,(int)Math.Floor((paperWidth-.03+.012)/.092)),rowsPerColumn=Math.Max(1,(int)Math.Floor((paperHeight-.03+.012)/.075));int pageCapacity=Math.Max(1,Math.Min(6,columns*rowsPerColumn));
   var pageAudit=new List<object>();var allProductionTables=new List<TableAnnotation>();
   for(int offset=0,page=1;offset<eligible.Length;offset+=pageCapacity,page++){
    var pageRows=eligible.Skip(offset).Take(pageCapacity).ToArray();
    if(page>1){string sheetName="ProfileCuts_"+page;if(!dd.NewSheet3(sheetName,(int)sheetProps[0],(int)sheetProps[1],sheetProps[2],sheetProps[3],sheetProps[4]!=0,sheetFormat,paperWidth,paperHeight,"")||!dd.ActivateSheet(sheetName))throw new Exception("NEXT_PROFILE_SHEET_CREATE_FAILED");((Sheet)dd.GetCurrentSheet()).SheetFormatVisible=false;var nextView=dd.CreateDrawViewFromModelView3(model,isoName,paperWidth+.2,paperHeight*.5,0);if(nextView==null)throw new Exception("NEXT_SHEET_VIEW_CREATE_FAILED");nextView.ReferencedConfiguration=productionConfig;if(!ManufacturingContextV02.SamePath(nextView.GetReferencedModelName(),model)||nextView.ReferencedConfiguration!=productionConfig)throw new Exception("NEXT_SHEET_MODEL_REFERENCE_FAILED");}
    Stage(60+Math.Min(20,offset),"Нарезка профилей: лист "+page+", позиций "+pageRows.Length);
    var input=dd.InsertTableAnnotation2(false,paperWidth+.05,-.1,1,"",pageRows.Length+1,10);if(input==null)throw new Exception("MACRO_INPUT_TABLE_CREATE_FAILED");
    for(int c=0;c<10;c++)input.set_Text(0,c,c==0?"NAME":headers[c]);
    for(int r=0;r<pageRows.Length;r++)for(int c=0;c<10;c++)input.set_Text(r+1,c,c==1?GroupTitle(pageRows[r].section,pageRows[r].name):pageRows[r].nativeCells[c]);
    Csv(Path.Combine(reports,"macro-input-page-"+page+".csv"),Cells(input));report["macroMethods"]=A(sw.GetMacroMethods(macro,0));drawing.ClearSelection2(true);var selectionManager=(SelectionMgr)drawing.SelectionManager;var selectionData=selectionManager.CreateSelectData();selectionData.SetCellRange(0,pageRows.Length,0,9);if(!input.GetAnnotation().Select3(false,selectionData))throw new Exception("MACRO_CELL_SELECTION_FAILED");var selected=selectionManager.GetSelectedObject6(1,-1) as TableAnnotation;if(selected==null||sw.IsSame(selected,input)!=1)throw new Exception("SELECTED_MACRO_TABLE_IDENTITY_DIFFERS");int r1=-1,r2=-1,c1=-1,c2=-1;selected.GetCellRange(ref r1,ref r2,ref c1,ref c2);if(r1!=0||r2!=pageRows.Length||c1!=0||c2!=9)throw new Exception("ACTUAL_SELECTED_CELL_RANGE_READBACK_FAILED");
    var beforeTables=Tables(drawing);int macroError;var nativeClock=Stopwatch.StartNew();bool ok=sw.RunMacro2(macro,"Profiles_h1","main",0,out macroError);Activate(sw,drawing);var after=Tables(drawing);var productionTables=after.Where(t=>!beforeTables.Any(old=>sw.IsSame(old,t)==1)).ToArray();pageAudit.Add(new{page,sheet=((Sheet)dd.GetCurrentSheet()).GetName(),expected=pageRows.Length,created=productionTables.Length,macroReturned=ok,macroError,elapsedSeconds=nativeClock.Elapsed.TotalSeconds});report["pageAudit"]=pageAudit;
    if(!ok||macroError!=0||productionTables.Length!=pageRows.Length)throw new Exception("MACRO_RESULT_TABLE_COUNT_NOT_CONFIRMED_ON_PAGE: "+page);
    foreach(var t in productionTables){string heading=t.get_Text(1,0);if(heading!=null&&heading.Contains(" | Cut List "))t.set_Text(1,0,heading.Split(new[]{" | Cut List "},StringSplitOptions.None)[0]);var position=A(t.GetAnnotation().GetPosition()).Select(Convert.ToDouble).ToArray();double tw=Enumerable.Range(0,t.ColumnCount).Sum(i=>t.GetColumnWidth(i)),th=Enumerable.Range(0,t.RowCount).Sum(i=>t.GetRowHeight(i));if(position.Length<2||position[0]<-.001||position[0]+tw>paperWidth+.001||position[1]>paperHeight+.001||position[1]-th<-.001)throw new Exception("PRODUCTION_TABLE_OUTSIDE_SHEET: "+page);}
    allProductionTables.AddRange(productionTables);
   }
   report["productionTablesInsideSheet"]=true;report["createdProductionTableCount"]=allProductionTables.Count;report["productionSheetCount"]=pageAudit.Count;report["tablesAfterMacro"]=allProductionTables.Select(Cells).ToArray();report["anglePolicy"]="Original Profiles+h profile angles 90 minus ANGLE; original Cut List ANGLE retained separately";report["HSource"]="Original unchanged Profiles+h.swp on owned AsWelded model";report["additionalProcessingStatus"]="NEEDS_REVIEW; Cut List folders kept separate";
   TranslateProductionTables(allProductionTables);report["documentationLanguage"]=LanguageCatalog.DocumentLanguage;report["translatedProductionTables"]=allProductionTables.Select(Cells).ToArray();
   Stage(85,"Оформление отдельного листа «Ведомость профилей»");AddReadableSchedule(sw,drawing,result,model,productionConfig,template,isoName,propCols,headers,cells,report);
   Stage(88,"Сохранение чертежа, PDF и контрольного отчёта");SaveDoc(drawing,drawingPath);string pdf=Path.Combine(work,"ProfileCutTables.pdf");SavePdf(sw,drawing,pdf);if(!ManufacturingDrawingChecksV13.PdfComplete(File.ReadAllBytes(pdf)))throw new Exception("PDF_INCOMPLETE");
   LanguageCatalog.WriteDeliveryNote(result,LanguageCatalog.DocumentLanguage,"Profile");report["drawing"]=drawingPath;report["pdf"]=pdf;report["status"]="TABLES_CREATED_NEEDS_REVIEW";report["additionalProcessingStatus"]="NEEDS_REVIEW; each Cut List kept separate, no merging across folders";exit=2;
  }catch(Exception ex){report["error"]=ex.ToString();Console.Error.WriteLine(ex.Message);if(drawing!=null&&ownedDrawingPath!=null){try{SaveDoc(drawing,ownedDrawingPath);report["partialDrawingSaved"]=true;}catch(Exception saveError){report["partialDrawingSaveError"]=saveError.ToString();}}}
  finally {
   bool disk=false,memory=false;try{disk=hash!=null&&Hash(source)==hash;memory=original!=null&&!original.GetSaveFlag()&&original.GetUpdateStamp()==stamp&&original.ConfigurationManager.ActiveConfiguration.Name==config;}catch(Exception ex){report["sourceCheckError"]=ex.ToString();}report["sourceFileUnchanged"]=disk;report["sourceMemoryUnchanged"]=memory;if(hash!=null&&(!disk||!memory)){report["status"]="ORIGINAL_STATE_CHECK_FAILED";exit=1;}
   // Only documents created by this run may be closed. A successful final
   // drawing remains visible; saved failed drawings remain available on disk.
   try{
    if(drawing!=null&&exit!=2&&ownedDrawingPath!=null&&File.Exists(ownedDrawingPath)&&!drawing.GetSaveFlag()&&ManufacturingContextV02.SamePath(drawing.GetPathName(),ownedDrawingPath)){sw.CloseDoc(drawing.GetTitle());report["ownedPartialDrawingClosed"]=true;}
    if(copy!=null&&result!=null&&NativeWeldmentAdapter.Inside(Path.Combine(result,"Profile","Models"),copy.GetPathName())&&!copy.GetSaveFlag()){sw.CloseDoc(copy.GetTitle());report["ownedWorkingPartWindowClosed"]=true;}
    if(exit==2&&drawing!=null)Activate(sw,drawing);else if(original!=null)Activate(sw,original);
   }catch(Exception cleanupError){report["documentCleanupWarning"]=cleanupError.Message;}
   report["elapsedSeconds"]=clock.Elapsed.TotalSeconds;if(reports!=null)Save(Path.Combine(reports,"profiles-report.json"),report);if(result!=null){Save(Path.Combine(result,"PROFILES_RESULT.json"),report);try{SharedResultFolder.Complete(result,"Profile",exit==2);}catch(Exception layoutError){report["layoutError"]=layoutError.ToString();report["status"]="OUTPUT_LAYOUT_FAILED";exit=1;Save(Path.Combine(result,"PROFILES_RESULT.json"),report);}Console.WriteLine("@@RESULT|"+result);}if(exit==2)Stage(100,"Таблицы созданы. Исходная модель не изменена.");
  }
  return exit;
 }
}
