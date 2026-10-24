using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
// Only the disposable export document is passed here. User documents are never changed.
public sealed class NativeDisplayScope : IDisposable {
 readonly List<Action> restore=new List<Action>();
 public NativeDisplayScope(ModelDoc2 document):this(document.FeatureManager,document.ActiveView as ModelView){}
 NativeDisplayScope(FeatureManager manager,ModelView view){
  try{
   bool tree=manager.EnableFeatureTree; restore.Add(()=>manager.EnableFeatureTree=tree);manager.EnableFeatureTree=false;
   bool window=manager.EnableFeatureTreeWindow;restore.Add(()=>manager.EnableFeatureTreeWindow=window);manager.EnableFeatureTreeWindow=false;
   if(view!=null){bool graphics=view.EnableGraphicsUpdate;restore.Add(()=>view.EnableGraphicsUpdate=graphics);view.EnableGraphicsUpdate=false;}
  }catch{Dispose();throw;}
 }
 public void Dispose(){var errors=new List<string>();for(int i=restore.Count-1;i>=0;i--)try{restore[i]();}catch(Exception e){errors.Add(e.Message);}restore.Clear();if(errors.Count>0)throw new InvalidOperationException("EXPORT_DISPLAY_RESTORE_FAILED: "+String.Join(" | ",errors));}
}
