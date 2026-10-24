using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;

public class SolidWorksSessionCandidate {
 public int pid;public bool containsTarget;public bool hasSelectedPlanarFaces;public int selectedCount;public string activeDocument;public string revision;public string[] documents;public SldWorks instance;
}
public static class SolidWorksSessionPolicy {
 public static SolidWorksSessionCandidate Choose(IList<SolidWorksSessionCandidate> sessions,bool targetRequired){return Choose(sessions,targetRequired,false);}
 public static SolidWorksSessionCandidate Choose(IList<SolidWorksSessionCandidate> sessions,bool targetRequired,bool facesRequired){
  if(facesRequired){
   var compatible=sessions.Where(s=>s.revision!=null&&s.revision.StartsWith("33.",StringComparison.Ordinal)).ToArray();
   var selected=compatible.Where(s=>s.hasSelectedPlanarFaces).ToArray();
   if(selected.Length==1)return selected[0];
   if(selected.Length>1)throw new InvalidOperationException("FACES_SELECTION_AMBIGUOUS");
   if(compatible.Length==1)return compatible[0];
   throw new InvalidOperationException("FACES_SELECTION_NOT_FOUND");
  }

  var candidates=sessions.Where(s=>s.revision!=null&&s.revision.StartsWith("33.",StringComparison.Ordinal)&&( !targetRequired||s.containsTarget)).ToArray();
  if(candidates.Length==1)return candidates[0];
  if(candidates.Length>1)throw new InvalidOperationException("SOLIDWORKS_SESSION_AMBIGUOUS: "+candidates.Length+" matching SolidWorks 2025 instances. Select a model opened in only one instance.");
  throw new InvalidOperationException(targetRequired?"SELECTED_MODEL_NOT_OPEN_IN_SOLIDWORKS_2025":"SOLIDWORKS_2025_SESSION_NOT_FOUND");
 }
}
public static class ExistingSolidWorksConnection {
 [DllImport("ole32.dll")]static extern int CreateBindCtx(uint reserved,out IBindCtx context);
 static IEnumerable<object> Items(object value){var a=value as Array;if(a==null)return new object[0];return a.Cast<object>();}
 static bool SamePath(string a,string b){try{return !String.IsNullOrWhiteSpace(a)&&!String.IsNullOrWhiteSpace(b)&&String.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 public static SldWorks Attach(){
  string target=System.Environment.GetEnvironmentVariable("SOLIDWORKSBREP_SOURCE_MODEL");bool required=!String.IsNullOrWhiteSpace(target);
  if(required)target=Path.GetFullPath(target);bool facesMode=System.Environment.GetEnvironmentVariable("SOLIDWORKSBREP_JOB_MODE")=="faces";
  var found=new Dictionary<int,SolidWorksSessionCandidate>();var failures=new List<string>();
  Action<SldWorks> add=sw=>{if(sw==null)return;int pid=sw.GetProcessID();using(var process=Process.GetProcessById(pid))if(process.SessionId!=Process.GetCurrentProcess().SessionId)return;if(found.ContainsKey(pid))return;string revision=sw.RevisionNumber();var docs=Items(sw.GetDocuments()).Cast<ModelDoc2>().Select(d=>d.GetPathName()).ToArray();var entry=new SolidWorksSessionCandidate{pid=pid,revision=revision,documents=docs,containsTarget=required&&docs.Any(p=>SamePath(p,target)),instance=sw};
   if(facesMode&&revision.StartsWith("33.",StringComparison.Ordinal)){
    var doc=sw.ActiveDoc as ModelDoc2;
    if(doc!=null&&doc.GetType()==1){entry.activeDocument=doc.GetPathName();var selection=doc.SelectionManager as SelectionMgr;entry.selectedCount=selection==null?0:selection.GetSelectedObjectCount2(-1);bool planar=entry.selectedCount>0;
     for(int i=1;i<=entry.selectedCount&&planar;i++){var face=selection.GetSelectedObject6(i,-1) as Face2;var surface=face==null?null:face.GetSurface() as Surface;planar=surface!=null&&surface.IsPlane();}
     entry.hasSelectedPlanarFaces=planar;
    }
   }
   found[pid]=entry;};
  try{add(Marshal.GetActiveObject("SldWorks.Application") as SldWorks);}catch(Exception ex){failures.Add("Generic: "+ex.Message);}
  IBindCtx context=null;IRunningObjectTable table=null;IEnumMoniker enumerator=null;
  try{
   Marshal.ThrowExceptionForHR(CreateBindCtx(0,out context));context.GetRunningObjectTable(out table);table.EnumRunning(out enumerator);var moniker=new IMoniker[1];
   while(enumerator.Next(1,moniker,IntPtr.Zero)==0){try{
    string name;moniker[0].GetDisplayName(context,null,out name);if(!name.StartsWith("SolidWorks_PID_",StringComparison.Ordinal))continue;
    int pid;if(!int.TryParse(name.Substring("SolidWorks_PID_".Length),out pid))continue;using(var process=Process.GetProcessById(pid))if(process.SessionId!=Process.GetCurrentProcess().SessionId)continue;
    object value;table.GetObject(moniker[0],out value);add(value as SldWorks);
   }catch(Exception ex){failures.Add(ex.Message);}finally{if(moniker[0]!=null)Marshal.ReleaseComObject(moniker[0]);}}
  }finally{if(enumerator!=null)Marshal.ReleaseComObject(enumerator);if(table!=null)Marshal.ReleaseComObject(table);if(context!=null)Marshal.ReleaseComObject(context);}
  SolidWorksSessionCandidate selected=null;string error=null;try{selected=SolidWorksSessionPolicy.Choose(found.Values.ToArray(),required,facesMode);JobOwnership.Session(selected.pid);return selected.instance;}
  catch(Exception ex){error=ex.Message;throw;}
  finally{
   string path=System.Environment.GetEnvironmentVariable("SOLIDWORKSBREP_CONNECTION_LOG");if(!String.IsNullOrWhiteSpace(path))try{var data=new{target,facesMode,selectedPid=selected==null?(int?)null:selected.pid,error,instances=found.Values.Select(s=>new{s.pid,s.revision,s.documents,s.containsTarget,s.activeDocument,s.selectedCount,s.hasSelectedPlanarFaces}).ToArray(),enumerationWarnings=failures.ToArray(),newSolidWorksInstanceCreated=false,documentsOpenedOrSaved=false};File.WriteAllText(path,new JavaScriptSerializer().Serialize(data));}catch{}
  }
 }
}
