using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public static class ProfilesCutTables {
 const string MacroHash="741E1216878BD4C150F88223C27B781E5053E291F362F7DEDCA450ACE614E2AB";
 static JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue,RecursionLimit=100};
 static object[] A(object o){return NativeWeldmentAdapter.Items(o);}
 static string Hash(string path){return ManufacturingSourceFilesV03.Hash(path,"PROFILES_BUTTON");}
 static string[][] Cells(TableAnnotation t){return Enumerable.Range(0,t.RowCount).Select(r=>Enumerable.Range(0,t.ColumnCount).Select(c=>t.get_DisplayedText2(r,c,true)).ToArray()).ToArray();}
 static TableAnnotation[] Tables(ModelDoc2 d){return A(((DrawingDoc)d).GetViews()).SelectMany(A).Cast<SolidWorks.Interop.sldworks.View>().SelectMany(v=>A(v.GetTableAnnotations())).Cast<TableAnnotation>().ToArray();}
 static void Stage(int p,string text){Console.WriteLine("@@PROFILE|"+p+"|"+text);}
 static void Save(string path,object value){File.WriteAllText(path,json.Serialize(value),new UTF8Encoding(false));}
 static string Q(string s){return "\""+(s??"").Replace("\"","\"\"")+"\"";}
 static void SaveTable(string path,string[][] cells){File.WriteAllLines(path,cells.Select(r=>String.Join(";",r.Select(Q))),new UTF8Encoding(true));}
 static Dictionary<string,object> State(ModelDoc2 d){return new Dictionary<string,object>{{"path",d.GetPathName()},{"configuration",d.ConfigurationManager.ActiveConfiguration.Name},{"dirty",d.GetSaveFlag()},{"updateStamp",d.GetUpdateStamp()}};}
 static string Reserve(string project){
  string name=new DirectoryInfo(project).Name,temp=Path.Combine(project,name+"_temp");Directory.CreateDirectory(temp);
  using(var guard=new FileStream(Path.Combine(temp,"layout.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   int n=1;string p;do{p=Path.Combine(project,name+"_"+n++);}while(Directory.Exists(p)||File.Exists(p));Directory.CreateDirectory(p);return p;
  }
 }
 static void SaveDoc(ModelDoc2 doc,string path){int errors=0,warnings=0;if(!doc.Extension.SaveAs(path,0,1,null,ref errors,ref warnings)||errors!=0||!File.Exists(path))throw new Exception("SAVE_RESULT_FAILED: "+path+" errors="+errors+" warnings="+warnings);}
 static void Activate(SldWorks sw,ModelDoc2 d){int error=0;var active=sw.ActivateDoc3(d.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref error);if(active==null||sw.IsSame(active,d)!=1)throw new Exception("ACTIVATE_COPY_FAILED: "+error);}
 public static void CheckRange(int r1,int r2,int c1,int c2,int rows,int cols){if(r1<0||r2<r1||r2>=rows||c1<0||c2<c1||c2>=cols)throw new Exception("Выделите ячейки исходной Weldment Cut List, как при запуске Profiles+h.");if(c2-c1+1!=10)throw new Exception("В исходном макросе проверена таблица из 10 столбцов. Выделите N, MATERIAL, количество, размеры, направления, углы и LENGTH.");}
 public static void SelfTest(){CheckRange(1,6,0,9,9,10);int n=1;foreach(var x in new[]{new[]{-1,6,0,9,9,10},new[]{1,9,0,9,9,10},new[]{1,6,0,8,9,10}}){bool fail=false;try{CheckRange(x[0],x[1],x[2],x[3],x[4],x[5]);}catch{fail=true;}if(!fail)throw new Exception("Invalid selection accepted");n++;}if(Q("a\"b")!="\"a\"\"b\"")throw new Exception("CSV escaping");Console.WriteLine("Profiles button selection/CSV: "+(n+1)+"/"+(n+1)+" PASS; no COM");}
 [STAThread]public static int Main(string[] args){
  Console.OutputEncoding=new UTF8Encoding(false);if(args.Length==2&&args[0]=="--layout-test"){SharedResultFolder.Tests(args[1]);return 0;}if(args.SequenceEqual(new[]{"--self-test"})){SelfTest();ProfilesAuto.SelfTest();return 0;}
  if((args.Length==2||args.Length==4)&&args[0]=="--source"){try{if(args.Length==4){if(args[2]!="--doc-lang"||!LanguageCatalog.Valid(args[3]))throw new Exception("Invalid document language");LanguageCatalog.DocumentLanguage=args[3];}if(Process.GetProcessesByName("SheetMetalDxfBatch").Length!=0||Process.GetProcessesByName("ProfilesCutTables").Length>1)throw new Exception("Другая обработка SolidWorks уже выполняется.");string home=AppDomain.CurrentDomain.BaseDirectory;Directory.CreateDirectory(Path.Combine(home,"bridge"));using(var guard=new FileStream(Path.Combine(home,"bridge","agent.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))return ProfilesAuto.Run(args[1]);}catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}}
  SldWorks sw=null;ModelDoc2 original=null,copy=null;string reports=null,result=null;var originals=new List<ModelDoc2>();var states=new List<string>();var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var report=new Dictionary<string,object>{{"status","FAILED"},{"productionReady",false},{"sourceMacroSHA256",MacroHash},{"macroWasModified",false},{"mode","SELECTED_DRAWING_TABLE_TO_OWNED_COPY"}};var clock=Stopwatch.StartNew();int exit=1;
  try {
   if(Process.GetProcessesByName("SheetMetalDxfBatch").Length!=0||Process.GetProcesses().Any(p=>p.ProcessName.StartsWith("ManufacturingAgent",StringComparison.OrdinalIgnoreCase)))throw new Exception("Закройте Manufacturing Agent и дождитесь завершения DXF-экспорта.");
   string home=AppDomain.CurrentDomain.BaseDirectory;Directory.CreateDirectory(Path.Combine(home,"bridge"));
   using(var guard=new FileStream(Path.Combine(home,"bridge","agent.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
    string macro=Path.Combine(home,@"Resources\Profiles+h.swp");if(Hash(macro)!=MacroHash)throw new Exception("Контрольная сумма Profiles+h.swp отличается. Запуск остановлен.");
    Stage(5,"Проверка чертежа и выделенных ячеек");sw=ExistingSolidWorksConnection.Attach();original=sw.ActiveDoc as ModelDoc2;
    if(original==null||original.GetType()!=3)throw new Exception("Откройте чертёж SolidWorks и выделите ячейки Weldment Cut List. Для этой кнопки выбирать SLDPRT не нужно.");
    if(String.IsNullOrWhiteSpace(original.GetPathName())||original.GetSaveFlag())throw new Exception("Сначала сохраните чертёж. Программа создаёт копию сохранённого состояния и не сохраняет оригинал автоматически.");
    var selection=(SelectionMgr)original.SelectionManager;if(selection.GetSelectedObjectCount2(-1)!=1)throw new Exception("Выделите один диапазон ячеек исходной Weldment Cut List.");
    var sourceTable=selection.GetSelectedObject6(1,-1) as TableAnnotation;if(sourceTable==null)throw new Exception("В выделении не найдена таблица. Выделите её ячейки.");
    if(!(sourceTable is WeldmentCutListAnnotation))throw new Exception("Требуется штатная Weldment Cut List, а не обычная таблица.");
    int r1=-1,r2=-1,c1=-1,c2=-1;sourceTable.GetCellRange(ref r1,ref r2,ref c1,ref c2);CheckRange(r1,r2,c1,c2,sourceTable.RowCount,sourceTable.ColumnCount);
    var sourceCells=Cells(sourceTable);string fingerprint=json.Serialize(sourceCells);report["selectedRange"]=new{firstRow=r1,lastRow=r2,firstColumn=c1,lastColumn=c2};report["nativeSourceTable"]=sourceCells;
    foreach(var d in A(sw.GetDocuments()).Cast<ModelDoc2>()){originals.Add(d);states.Add(json.Serialize(State(d)));}
    var pg=original.Extension.GetPackAndGo();pg.IncludeDrawings=true;object namesRaw;if(!pg.GetDocumentNames(out namesRaw))throw new Exception("PACK_AND_GO_ENUM_FAILED");
    string[] names=A(namesRaw).Select(Convert.ToString).ToArray();if(names.Length==0||!names.Any(n=>ManufacturingContextV02.SamePath(n,original.GetPathName())))throw new Exception("PACK_AND_GO_DRAWING_NOT_INCLUDED");
    foreach(string name in names){if(!File.Exists(name))throw new Exception("Не найдена ссылка чертежа: "+name);var loaded=sw.GetOpenDocumentByName(name) as ModelDoc2;if(loaded!=null&&loaded.GetSaveFlag())throw new Exception("Связанная модель имеет несохранённые изменения: "+name);hashes[name]=Hash(name);}
    string project=Path.GetDirectoryName(original.GetPathName());result=Reserve(project);string work=Path.Combine(result,"Profiles"),refs=Path.Combine(work,"Models");Directory.CreateDirectory(refs);string temp=Path.Combine(project,new DirectoryInfo(project).Name+"_temp","Profiles_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));reports=Path.Combine(temp,"Reports");Directory.CreateDirectory(reports);report["resultFolder"]=result;report["serviceFolder"]=temp;
    SaveTable(Path.Combine(reports,"native-source-table.csv"),sourceCells);Save(Path.Combine(reports,"selected-range.json"),report["selectedRange"]);
    string[] targets=names.Select(n=>ManufacturingContextV02.SamePath(n,original.GetPathName())?Path.Combine(work,"ProfileCutTables.SLDDRW"):Path.Combine(refs,Path.GetFileName(n))).ToArray();if(targets.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=targets.Length)throw new Exception("PACK_AND_GO_DUPLICATE_FILENAMES");
    Stage(15,"Создание копии чертежа и связанных моделей");if(!pg.SetDocumentSaveToNames(targets))throw new Exception("PACK_AND_GO_TARGETS_REFUSED");
    var codes=A(original.Extension.SavePackAndGo(pg)).Select(Convert.ToInt32).ToArray();if(codes.Length!=targets.Length||codes.Any(x=>x!=0)||targets.Any(p=>!File.Exists(p)))throw new Exception("PACK_AND_GO_FAILED");
    report["packAndGoFiles"]=names.Zip(targets,(a,b)=>new{original=a,copy=b}).ToArray();string drawingPath=targets[Array.FindIndex(names,n=>ManufacturingContextV02.SamePath(n,original.GetPathName()))];
    int error=0,warning=0;copy=sw.OpenDoc6(drawingPath,3,1,"",ref error,ref warning);if(copy==null||error!=0||!ManufacturingContextV02.SamePath(copy.GetPathName(),drawingPath))throw new Exception("WORK_DRAWING_OPEN_FAILED: "+error);Activate(sw,copy);
    // Resolve only copied models before invoking VBA. Never let a copied view bind to an original.
    var views=A(((DrawingDoc)copy).GetViews()).SelectMany(A).Cast<SolidWorks.Interop.sldworks.View>().Where(v=>!String.IsNullOrWhiteSpace(v.GetReferencedModelName())).ToArray();
    foreach(var v in views){string path=v.GetReferencedModelName();if(!targets.Any(t=>ManufacturingContextV02.SamePath(t,path)))throw new Exception("COPIED_VIEW_STILL_REFERENCES_ORIGINAL: "+path);var m=v.ReferencedDocument as ModelDoc2;if(m==null||!targets.Any(t=>ManufacturingContextV02.SamePath(t,m.GetPathName())))throw new Exception("COPIED_VIEW_MODEL_NOT_RESOLVED: "+path);}
    var matching=Tables(copy).Where(t=>json.Serialize(Cells(t))==fingerprint).ToArray();if(matching.Length!=1)throw new Exception("SOURCE_TABLE_COPY_NOT_UNIQUE_OR_VALUES_CHANGED");
    var table=matching[0];copy.ClearSelection2(true);if(!table.GetAnnotation().Select3(false,null))throw new Exception("TABLE_SELECTION_FAILED");table.SetCellRange(r1,r2,c1,c2);int a1=-1,a2=-1,b1=-1,b2=-1;table.GetCellRange(ref a1,ref a2,ref b1,ref b2);if(a1!=r1||a2!=r2||b1!=c1||b2!=c2)throw new Exception("COPIED_CELL_SELECTION_READBACK_FAILED");
    int before=Tables(copy).Length;Stage(40,"Profiles+h: создание таблиц резки и H");int macroError=0;bool ok=sw.RunMacro2(macro,"Profiles_h1","main",0,out macroError);report["runMacro2"]=new{returned=ok,error=macroError};if(!ok||macroError!=0)throw new Exception("RUN_MACRO_FAILED: "+macroError);
    Activate(sw,copy);var after=Tables(copy);if(after.Length<=before)throw new Exception("Макрос не создал новые таблицы. Проверьте его сообщение в SolidWorks.");
    if(json.Serialize(Cells(table))!=fingerprint)throw new Exception("SOURCE_TABLE_CHANGED_BY_MACRO");
    report["createdTableCount"]=after.Length-before;report["tablesAfterMacro"]=after.Select(Cells).ToArray();report["anglePolicy"]="ORIGINAL_MACRO_PROFILE_ANGLE_90_MINUS_NATIVE; native ANGLE retained in native-source-table.csv";report["limitations"]=new[]{"Original macro groups by its MATERIAL text; this compatibility mode does not certify production grouping.","Original macro uses first body of Cut List folder; individual-body H for multi-body folders is not certified.","Additional machining is not certified by original macro.","Output requires manufacturing review; original source table values retained separately."};
    Stage(80,"Сохранение редактируемого чертежа и PDF");SaveDoc(copy,drawingPath);string pdf=Path.Combine(work,"ProfileCutTables.pdf");SaveDoc(copy,pdf);if(!ManufacturingDrawingChecksV13.PdfComplete(File.ReadAllBytes(pdf)))throw new Exception("PDF_INCOMPLETE");
    report["drawing"]=drawingPath;report["pdf"]=pdf;report["status"]="TABLES_CREATED_NEEDS_REVIEW";exit=2;
   }
  }catch(Exception ex){report["error"]=ex.ToString();Console.Error.WriteLine(ex.Message);}
  finally {
   bool unchanged=true;try{foreach(var p in hashes)if(Hash(p.Key)!=p.Value)unchanged=false;for(int i=0;i<originals.Count;i++)if(json.Serialize(State(originals[i]))!=states[i])unchanged=false;}catch(Exception ex){unchanged=false;report["originalAuditError"]=ex.ToString();}
   report["originalsUnchanged"]=unchanged;if(!unchanged){report["status"]="ORIGINAL_STATE_CHECK_FAILED";exit=1;}
   report["elapsedSeconds"]=clock.Elapsed.TotalSeconds;
   if(reports!=null)Save(Path.Combine(reports,"profiles-report.json"),report);
   if(result!=null){Save(Path.Combine(result,"PROFILES_RESULT.json"),report);Console.WriteLine("@@RESULT|"+result);}
   // Leave the created drawing open for inspection. Never save or close user originals.
   if(exit==2)Stage(100,"Таблицы созданы в копии чертежа; требуется проверка");
  }
  return exit;
 }
}
