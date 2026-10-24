using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

class SelectedFacesDxf {
 static JavaScriptSerializer J=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
 class Pick {public byte[] reference;public string body,bodyIdentity;public double[] normal;public double area;public int loops;}
 class Group {public string body,file;public Pick[] faces;}
 static bool Parallel(double[] a,double[] b){if(a==null||b==null||a.Length!=3||b.Length!=3)return false;double aa=Math.Sqrt(a.Sum(x=>x*x)),bb=Math.Sqrt(b.Sum(x=>x*x));if(aa<1e-12||bb<1e-12)return false;return Math.Abs(a.Zip(b,(x,y)=>x*y).Sum()/(aa*bb))>=1-1e-10;}
 static Group[] Groups(List<Pick> picks){var groups=picks.GroupBy(p=>p.bodyIdentity).Select(g=>new Group{body=g.First().body,faces=g.ToArray()}).ToArray();var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var g in groups){if(g.faces.Any(p=>!Parallel(g.faces[0].normal,p.normal)))throw new Exception("У тела «"+g.body+"» выбраны непараллельные грани. Для одного DXF выбирайте параллельные грани.");string stem=Safe(g.body),name=stem+".dxf";int n=2;while(!used.Add(name))name=stem+"__body_"+(n++).ToString("00")+".dxf";g.file=name;}return groups;}
 static string Safe(string name){var chars=name.Select(c=>c<32||"<>:\"/\\|?*".Contains(c)?'_':c).ToArray();string s=new string(chars).Trim().TrimEnd('.');if(s.Length==0)s="Body";if(s.Length>100)s=s.Substring(0,100);if(System.Text.RegularExpressions.Regex.IsMatch(s,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))s="_"+s;return s;}
 static void Stage(int n,string s){Console.WriteLine("@@PROFILE|"+n+"|"+s);}
 [STAThread] static int Main(string[] args){
  Console.OutputEncoding=Encoding.UTF8;
  if(args.Length==1&&args[0]=="--self-test"){if(Safe("A/B")!="A_B"||Safe("CON")!="_CON"||Safe("Body 1")!="Body 1"||Safe("...")!="Body")throw new Exception("Filename tests failed");Console.WriteLine("Face DXF filename tests: 4/4 PASS; no COM");var samples=new List<Pick>{new Pick{body="A/B",bodyIdentity="one",normal=new double[]{0,0,1}},new Pick{body="A/B",bodyIdentity="one",normal=new double[]{0,0,-1}},new Pick{body="A_B",bodyIdentity="two",normal=new double[]{0,0,1}}};var grouped=Groups(samples);if(grouped.Length!=2||grouped[0].faces.Length!=2||grouped[0].file==grouped[1].file||Parallel(new double[]{0,0,1},new double[]{0,1,0}))throw new Exception("Body grouping failed");samples[1].normal=new double[]{1,0,0};bool rejected=false;try{Groups(samples);}catch{rejected=true;}if(!rejected)throw new Exception("Nonparallel group accepted");Console.WriteLine("Parallel/opposite faces, native body identity, filename collision and nonparallel rejection: PASS; no COM");return 0;}
  if(args.Length==2&&args[0]=="--doc-lang"){if(!LanguageCatalog.Valid(args[1]))throw new ArgumentException("Invalid document language");LanguageCatalog.DocumentLanguage=args[1];}
  SldWorks sw=null;ModelDoc2 original=null,copy=null;string source=null,hash=null,config=null,result=null,service=null,output=null,model=null;NativeDisplayScope display=null;NativeFaceWorkspace bundle=null;int stamp=0,exit=1;var clock=Stopwatch.StartNew();var picks=new List<Pick>();var files=new List<object>();var report=new Dictionary<string,object>{{"status","FAILED"},{"mode","SELECTED_PLANAR_FACES_NATIVE_DXF"},{"files",files}};
  try{
   Stage(2,"Чтение выбранных граней");sw=ExistingSolidWorksConnection.Attach();original=sw.ActiveDoc as ModelDoc2;
   if(original==null||original.GetType()!=1)throw new Exception("Активируйте деталь SLDPRT и выделите плоские грани через Ctrl.");
   source=original.GetPathName();if(String.IsNullOrWhiteSpace(source)||original.GetSaveFlag())throw new Exception("Сначала сохраните деталь, затем выделите грани.");
   hash=ManufacturingSourceFilesV03.Hash(source,"FACE_DXF");config=original.ConfigurationManager.ActiveConfiguration.Name;stamp=original.GetUpdateStamp();report["source"]=source;report["sourceSHA256"]=hash;report["configuration"]=config;
   var manager=(SelectionMgr)original.SelectionManager;
   for(int i=1;i<=manager.GetSelectedObjectCount2(-1);i++){
    var face=manager.GetSelectedObject6(i,-1) as Face2;if(face==null||!((Surface)face.GetSurface()).IsPlane())throw new Exception("Выделение должно содержать только плоские грани. Позиция: "+i);
    byte[] reference=(byte[])original.Extension.GetPersistReference3(face);if(reference==null||reference.Length==0)throw new Exception("Не получен идентификатор выбранной грани.");
    if(picks.Any(p=>p.reference.SequenceEqual(reference)))continue;
    var body=(Body2)face.GetBody();picks.Add(new Pick{reference=reference,body=body.Name,bodyIdentity=Convert.ToBase64String((byte[])original.Extension.GetPersistReference3(body)),normal=(double[])face.Normal,area=face.GetArea(),loops=face.GetLoopCount()});
   }
   if(picks.Count==0)throw new Exception("Нет выбранных граней. Выделите грани через Ctrl в SolidWorks.");
   var groups=Groups(picks);report["selectedBodyCount"]=groups.Length;report["grouping"]="One DXF per native body; all selected parallel planar faces projected together, including pocket contours";
   string project=Path.GetDirectoryName(source);
   service=Path.Combine(SheetMetalStorage.WorkRoot,"Faces_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(SheetMetalStorage.WorkRoot);JobOwnership.OwnDirectory(service,SheetMetalStorage.WorkRoot);output=Path.Combine(service,"DXF");Directory.CreateDirectory(output);model=Path.Combine(service,"FacesWork_"+Guid.NewGuid().ToString("N").Substring(0,8)+".SLDPRT");
   bundle=NativeFaceWorkspace.Create(sw,original,service,report);model=bundle.Model;File.SetAttributes(model,FileAttributes.Normal);int e=0,w=0;copy=sw.OpenDoc6(model,1,1,config,ref e,ref w);if(copy==null||e!=0||copy.ConfigurationManager.ActiveConfiguration.Name!=config)throw new Exception("Не удалось открыть рабочую копию: "+e);
   int activation=0;var active=sw.ActivateDoc3(copy.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref activation);if(active==null||sw.IsSame(active,copy)!=1)throw new Exception("Рабочая копия не активирована.");
   if(copy.Extension.ListExternalFileReferencesCount()>0){copy.LockAllExternalReferences();report["referencesLockedOnTemporaryCopyOnly"]=true;}report["workspaceReferences"]=ManufacturingWorkspaceV13.LiveReferences(copy,service);
   display=new NativeDisplayScope(copy);
   for(int i=0;i<groups.Length;i++){
    var g=groups[i];Stage(10+80*i/groups.Length,"Экспорт тела "+(i+1)+" из "+groups.Length+": "+g.body+", граней: "+g.faces.Length);copy.ClearSelection2(true);
    Body2 resolvedBody=null;foreach(var p in g.faces){int error=0;var face=copy.Extension.GetObjectByPersistReference3(p.reference,out error) as Face2;
     if(face==null||error!=0||!((Surface)face.GetSurface()).IsPlane()||((Body2)face.GetBody()).Name!=p.body||!Parallel(p.normal,(double[])face.Normal)||Math.Abs(face.GetArea()-p.area)>Math.Max(1e-12,p.area*1e-9)||face.GetLoopCount()!=p.loops)throw new Exception("Не подтверждено соответствие грани рабочей копии телу "+p.body);
     var body=(Body2)face.GetBody();if(resolvedBody==null)resolvedBody=body;else if(sw.IsSame(resolvedBody,body)!=1)throw new Exception("Грани группы принадлежат разным телам.");
     if(!((Entity)face).Select4(true,null))throw new Exception("Не удалось выбрать грань "+p.body);
    }
    if(((SelectionMgr)copy.SelectionManager).GetSelectedObjectCount2(-1)!=g.faces.Length)throw new Exception("Не все выбранные грани тела переданы экспортёру.");
    string path=Path.Combine(output,g.file);var t=Stopwatch.StartNew();if(!((PartDoc)copy).ExportToDWG2(path,model,(int)swExportToDWG_e.swExportToDWG_ExportSelectedFacesOrLoops,true,new double[12],false,false,0,null)||!File.Exists(path)||new FileInfo(path).Length==0)throw new Exception("SolidWorks не создал DXF: "+g.file);
    files.Add(new{file=g.file,body=g.body,selectedFaces=g.faces.Select(p=>new{faceReference=Convert.ToBase64String(p.reference),areaSquareMetres=p.area,loopCount=p.loops,normal=p.normal}).ToArray(),faceCount=g.faces.Length,sha256=ManufacturingSourceFilesV03.Hash(path,"FACE_DXF_OUTPUT"),elapsedSeconds=t.Elapsed.TotalSeconds});
   }
   report["documentationLanguage"]=LanguageCatalog.DocumentLanguage;report["status"]="FACE_DXF_CREATED_NEEDS_REVIEW";exit=2;
  }catch(Exception ex){report["error"]=ex.ToString();Console.Error.WriteLine(ex.Message);}
  finally{
   try{bool disk=source!=null&&hash!=null&&ManufacturingSourceFilesV03.Hash(source,"FACE_DXF_FINAL")==hash;bool memory=original!=null&&!original.GetSaveFlag()&&original.GetUpdateStamp()==stamp&&original.ConfigurationManager.ActiveConfiguration.Name==config;report["sourceFileUnchanged"]=disk;report["sourceMemoryUnchanged"]=memory;if(hash!=null&&(!disk||!memory)){exit=1;report["status"]="ORIGINAL_STATE_CHECK_FAILED";}
    if(display!=null){display.Dispose();display=null;}if(copy!=null&&service!=null&&NativeWeldmentAdapter.Inside(service,copy.GetPathName())){sw.CloseDoc(copy.GetTitle());if(sw.GetOpenDocumentByName(model)!=null)throw new Exception("Temporary face document remains open");if(bundle!=null)bundle.Cleanup(report);else{JobOwnership.Validate(model,service);File.Delete(model);report["temporaryModelCopyDeleted"]=!File.Exists(model);}}if(original!=null){int e=0;sw.ActivateDoc3(original.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref e);}
   }catch(Exception ex){report["cleanupError"]=ex.Message;exit=1;report["status"]="FINAL_CHECK_FAILED";}
   report["elapsedSeconds"]=clock.Elapsed.TotalSeconds;report["selectedFaceCount"]=picks.Count;report["exportedBodyCount"]=files.Count;
   if(service!=null){
    if(exit==2)try{result=DxfOnlyFolder.Publish(Path.GetDirectoryName(source),output);report["resultFolder"]=result;}catch(Exception ex){report["publicationError"]=ex.ToString();report["status"]="PUBLICATION_FAILED";exit=1;}
    report["serviceFolder"]=service;File.WriteAllText(Path.Combine(service,"faces-result.json"),J.Serialize(report),new UTF8Encoding(false));
    if(exit==2)Console.WriteLine("@@RESULT|"+result);
   }if(exit==2)Stage(100,"DXF выбранных граней созданы. Проверьте масштаб и контуры в CAM.");
  }return exit;
 }
}


