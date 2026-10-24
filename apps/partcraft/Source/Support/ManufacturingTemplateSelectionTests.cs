using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
public static class ManufacturingTemplateSelectionTests {
 public static int Run(){
  string root=Path.Combine("work","template-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string a=Path.GetFullPath(Path.Combine(root,"Шаблон.DRWDOT")),b=Path.GetFullPath(Path.Combine(root,"Other.drwdot")),settings=Path.Combine(root,"settings.json");
  File.WriteAllText(a,"path-validation fixture");File.WriteAllText(b,"path-validation fixture");int passed=0;
  Action<bool> check=ok=>{if(!ok)throw new Exception("Template regression failed at "+(passed+1));passed++;};
  try{
   check(ManufacturingTemplateSelection.Pick(a,settings,()=>{throw new Exception("API called for CLI");},(f,n)=>{throw new Exception("Dialog called for CLI");})==a);
   bool bad=false;try{ManufacturingTemplateSelection.Pick(a+".txt",settings,()=>new string[0],(f,n)=>{throw new Exception("Dialog unexpectedly called");});}catch(ArgumentException){bad=true;}check(bad);
   bad=false;try{ManufacturingTemplateSelection.Validate(Path.Combine(root,"missing.drwdot"));}catch(ArgumentException){bad=true;}check(bad);
   check(File.ReadAllText(settings,Encoding.UTF8).Contains("Шаблон"));
   int dialogs=0;string chosen=ManufacturingTemplateSelection.Pick(null,settings,()=>{throw new Exception("Last folder not reused");},(f,n)=>{dialogs++;check(f==Path.GetDirectoryName(a)&&n==Path.GetFileName(a));return b;});check(chosen==b&&dialogs==1);
   string before=File.ReadAllText(settings);check(ManufacturingTemplateSelection.Pick(null,settings,()=>new string[0],(f,n)=>null)==null&&File.ReadAllText(settings)==before);
   string fresh=Path.Combine(root,"fresh.json");check(ManufacturingTemplateSelection.Pick(null,fresh,()=>new[]{Path.GetDirectoryName(a)},(f,n)=>f==Path.GetDirectoryName(a)?a:null)==a);
   Console.WriteLine("Template selection: "+passed+" PASS (no UI / COM)");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
