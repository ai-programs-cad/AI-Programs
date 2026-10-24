using System;using System.IO;using System.Linq;using System.Collections.Generic;using SolidWorks.Interop.sldworks;using SolidWorks.Interop.swconst;
public sealed class NativeFaceWorkspace {
 public string Model;readonly SldWorks sw;readonly string root;readonly List<string> owned=new List<string>();readonly Dictionary<string,string> hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);readonly Dictionary<ModelDoc2,string> states=new Dictionary<ModelDoc2,string>();
 static string State(ModelDoc2 d){return CutListCoreV01.Json.Serialize(new{path=d.GetPathName(),dirty=d.GetSaveFlag(),stamp=d.GetUpdateStamp(),configuration=d.GetType()==3?null:d.ConfigurationManager.ActiveConfiguration.Name});}
 static object[] Items(object a){return a==null?new object[0]:((Array)a).Cast<object>().ToArray();}
 NativeFaceWorkspace(SldWorks application,string directory){sw=application;root=directory;}
 public static NativeFaceWorkspace Create(SldWorks sw,ModelDoc2 original,string root,Dictionary<string,object> report){
  var w=new NativeFaceWorkspace(sw,root);string source=original.GetPathName();foreach(var d in Items(sw.GetDocuments()).Cast<ModelDoc2>())w.states[d]=State(d);
  w.Model=Path.Combine(root,"FacesWork_"+Guid.NewGuid().ToString("N").Substring(0,8)+".SLDPRT");
  var dependencies=Items(sw.GetDocumentDependencies2(source,false,false,false));
  if(dependencies.Length==0){string hash=ManufacturingSourceFilesV03.Hash(source,"FACE_COPY_BEFORE");w.hashes[source]=hash;ManufacturingSourceFilesV03.CopySavedSource(source,w.Model,hash,false);w.owned.Add(w.Model);report["temporaryCopyMethod"]="One saved-source copy";return w;}
  var pack=original.Extension.GetPackAndGo();pack.IncludeDrawings=false;pack.IncludeSimulationResults=false;pack.IncludeToolboxComponents=false;
  object raw;if(!pack.GetDocumentNames(out raw))throw new Exception("FACE_PACK_AND_GO_ENUM_FAILED");string[] names=Items(raw).Select(Convert.ToString).ToArray();int index=Array.FindIndex(names,n=>ManufacturingContextV02.SamePath(n,source));if(index<0||names.Length==0)throw new Exception("FACE_PACK_SOURCE_MISSING");
  foreach(string name in names){if(!File.Exists(name))throw new Exception("Referenced file unavailable: "+name);var loaded=sw.GetOpenDocumentByName(name) as ModelDoc2;if(loaded!=null&&loaded.GetSaveFlag())throw new Exception("Referenced document has unsaved changes: "+name);w.hashes[name]=ManufacturingSourceFilesV03.Hash(name,"FACE_REFERENCE_BEFORE");}
  string[] targets=names.Select((n,i)=>i==index?w.Model:Path.Combine(root,"Ref_"+i.ToString("D4")+Path.GetExtension(n))).ToArray();if(targets.Any(File.Exists))throw new Exception("Temporary Pack and Go targets already exist");
  if(!pack.SetDocumentSaveToNames(targets))throw new Exception("FACE_PACK_TARGETS_REFUSED");var codes=Items(original.Extension.SavePackAndGo(pack)).Select(Convert.ToInt32).ToArray();
  if(codes.Length!=targets.Length||codes.Any(c=>c!=0)||targets.Any(p=>!File.Exists(p)||new FileInfo(p).Length==0))throw new Exception("FACE_PACK_AND_GO_FAILED: "+String.Join(",",codes));
  w.owned.AddRange(targets);w.VerifyOriginals();report["temporaryCopyMethod"]="Native Pack and Go; dependencies remapped to disposable workspace";report["temporaryReferenceFileCount"]=targets.Length-1;return w;
 }
 public void VerifyOriginals(){foreach(var pair in hashes)if(ManufacturingSourceFilesV03.Hash(pair.Key,"FACE_REFERENCE_AFTER")!=pair.Value)throw new Exception("Original reference file changed: "+pair.Key);foreach(var pair in states)if(State(pair.Key)!=pair.Value)throw new Exception("Original document state changed: "+pair.Key.GetPathName());}
 public void Cleanup(Dictionary<string,object> report){
  foreach(var d in Items(sw.GetDocuments()).Cast<ModelDoc2>().Where(d=>owned.Any(p=>ManufacturingContextV02.SamePath(p,d.GetPathName()))).ToArray())sw.CloseDoc(d.GetTitle());
  foreach(string p in owned){JobOwnership.Validate(p,root);if(sw.GetOpenDocumentByName(p)!=null)throw new Exception("Temporary reference remains open: "+p);File.Delete(p);}
  VerifyOriginals();report["temporaryModelCopyDeleted"]=owned.All(p=>!File.Exists(p));report["originalReferenceFilesUnchanged"]=true;report["originalOpenDocumentsUnchanged"]=true;
 }
}
