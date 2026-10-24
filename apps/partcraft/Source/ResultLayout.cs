using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Security.Cryptography;using System.Web.Script.Serialization;using System.Runtime.InteropServices;
static class ResultLayout {
 static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=int.MaxValue};}
 static string Hash(string p){using(var h=SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","");}
 static string Index(string folder){string key;using(var h=SHA256.Create())key=BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).TrimEnd('\\').ToUpperInvariant()))).Replace("-","");string root=Path.Combine(AppStorage.DataRoot,"DxfResults");Directory.CreateDirectory(root);return Path.Combine(root,key+".json");}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateDirectory(string path,IntPtr attributes);
 static string Reserve(string project){for(int i=0;i<100000;i++){string target=Path.Combine(project,i==0?"DXF":"DXF"+i);if(CreateDirectory(target,IntPtr.Zero))return target;int error=Marshal.GetLastWin32Error();if(error!=183&&error!=80)throw new IOException("Cannot create DXF output folder: "+target+"; Windows error="+error);}throw new IOException("DXF output numbering exhausted");}
 public static string Publish(string run){
  run=Path.GetFullPath(run);var r=Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,@"Reports\native-batch-dxf.json")));
  if(Convert.ToString(r["status"])!="DXF_EXACT_AND_CAM_ARCS_CREATED_NEEDS_REVIEW"||!Convert.ToBoolean(r["sourceFileUnchanged"])||!Convert.ToBoolean(r["sourceMemoryUnchanged"]))throw new Exception("Публикация остановлена: запуск не прошёл обязательные проверки.");
  string project=Path.GetDirectoryName(Path.GetFullPath(Convert.ToString(r["source"])));
  // Both supported layouts are immutable service inputs. New jobs use AppData only.
  string work=Path.Combine(AppStorage.DataRoot,"DxfWork")+Path.DirectorySeparatorChar;
  string legacy=Path.Combine(project,"_Manufacturing")+Path.DirectorySeparatorChar;
  if(!run.StartsWith(work,StringComparison.OrdinalIgnoreCase)&&!run.StartsWith(legacy,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected export service folder");
  var outputs=((System.Collections.IEnumerable)r["outputs"]).Cast<Dictionary<string,object>>().ToArray();var files=Directory.GetFiles(Path.Combine(run,@"CAM_Preparation\CAM_ARCS"),"*.dxf");
  if(outputs.Length==0||files.Length!=outputs.Length)throw new Exception("Processed DXF count mismatch");
  var plans=new List<Tuple<string,string>>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var row in outputs){string body=Convert.ToString(row["body"]);int qty=Convert.ToInt32(row["quantity"]);if(qty<1||String.IsNullOrWhiteSpace(body)||body.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new Exception("Invalid output name/quantity");string input=Path.Combine(run,@"CAM_Preparation\CAM_ARCS",body+".dxf"),name=body+"_Q"+qty+".dxf";if(!File.Exists(input)||!names.Add(name))throw new Exception("Missing/colliding processed DXF");plans.Add(Tuple.Create(input,name));}
  string dest=Reserve(project);var created=new List<Tuple<string,string>>();
  try{
   foreach(var plan in plans){string target=Path.Combine(dest,plan.Item2);File.Copy(plan.Item1,target,false);string hash=Hash(target);created.Add(Tuple.Create(target,hash));if(hash!=Hash(plan.Item1))throw new Exception("DXF copy verification failed");}
   File.WriteAllText(Index(dest),Json().Serialize(new{serviceFolder=run,outputFolder=dest,files=plans.Select(p=>p.Item2).ToArray(),quantityInFilename=true}),new UTF8Encoding(false));
   return dest;
  }catch{foreach(var item in created)if(File.Exists(item.Item1)&&Hash(item.Item1)==item.Item2)File.Delete(item.Item1);if(!Directory.EnumerateFileSystemEntries(dest).Any())Directory.Delete(dest,false);throw;}
 }
 public static string Working(string folder){string index=Index(folder);if(File.Exists(index))return folder;if(File.Exists(Path.Combine(folder,"RESULT.json")))return Path.Combine(folder,"DXF");return Path.Combine(folder,@"CAM_Preparation\CAM_ARCS");}
 public static string Service(string folder){string index=Index(folder);if(File.Exists(index))return Convert.ToString(Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(index))["serviceFolder"]);string legacy=Path.Combine(folder,"RESULT.json");return File.Exists(legacy)?Convert.ToString(Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(legacy))["serviceFolder"]):folder;}
}

