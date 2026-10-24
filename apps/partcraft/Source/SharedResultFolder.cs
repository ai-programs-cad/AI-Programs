using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

// Shared output numbering for Profile and DXF; no geometry or COM operations.
static class SharedResultFolder {
 static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=int.MaxValue};}
 static string Meta(string folder){return Path.Combine(folder,"PACKAGE.json");}
 static void Write(string path,object value){File.WriteAllText(path,Json().Serialize(value),new UTF8Encoding(false));}
 static Dictionary<string,object> Read(string p){return Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(p));}
 static void Role(string role){if(role!="Profile"&&role!="DXF"&&role!="Faces")throw new Exception("Unknown result role");}
 static string RolePath(string root,string role){return role=="Faces"?Path.Combine(root,"DXF","Faces"):Path.Combine(root,role);}
 static string Project(string folder){return Directory.GetParent(Path.GetFullPath(folder)).FullName;}
 static string Temp(string project){string p=Path.Combine(project,new DirectoryInfo(project).Name+"_temp");Directory.CreateDirectory(p);return p;}
 public static string Reserve(string project,string source,string hash,string config,string role){
  Role(role);project=Path.GetFullPath(project);source=Path.GetFullPath(source);
  if(!String.Equals(Path.GetDirectoryName(source),project,StringComparison.OrdinalIgnoreCase)||String.IsNullOrWhiteSpace(hash)||String.IsNullOrWhiteSpace(config))throw new Exception("Result context incomplete");
  string name=new DirectoryInfo(project).Name;
  using(var guard=new FileStream(Path.Combine(Temp(project),"layout.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   var candidates=Directory.GetDirectories(project,name+"_*").Select(p=>new{path=p,suffix=Path.GetFileName(p).Substring(name.Length+1)}).Select(x=>new{ x.path,number=ParseNumber(x.suffix)}).Where(x=>x.number>0&&File.Exists(Meta(x.path))).OrderByDescending(x=>x.number);
   string reusableDxf=null;foreach(var candidate in candidates){
    var m=Read(Meta(candidate.path));
    if(!String.Equals(Convert.ToString(m["source"]),source,StringComparison.OrdinalIgnoreCase)||Convert.ToString(m["sourceSHA256"])!=hash||Convert.ToString(m["configuration"])!=config)continue;
    var roles=(Dictionary<string,object>)m["roles"];
    if(role=="Profile"&&roles.ContainsKey("DXF")&&Convert.ToString(roles["DXF"])=="READY")reusableDxf=candidate.path;
    // Only the newest matching package is considered. Never fill a historical gap.
    if(!roles.ContainsKey(role)&&roles.Values.Any(v=>Convert.ToString(v)=="READY")&&(!Directory.Exists(RolePath(candidate.path,role))||!Directory.EnumerateFileSystemEntries(RolePath(candidate.path,role)).Any())){
     if(role=="DXF"&&Directory.Exists(RolePath(candidate.path,role))&&Directory.EnumerateFileSystemEntries(RolePath(candidate.path,role)).Any())continue;
     JobOwnership.BackupPackage(Meta(candidate.path),project,role);JobOwnership.BackupFile(Path.Combine(candidate.path,role=="Profile"?"PROFILES_RESULT.json":role=="Faces"?"FACES_RESULT.json":"RESULT.json"),project);foreach(string lang in LanguageCatalog.Codes)JobOwnership.BackupFile(Path.Combine(candidate.path,"READ_ME_"+role+"_"+lang+".txt"),project);JobOwnership.OwnDirectory(RolePath(candidate.path,role),project);
     roles[role]="RESERVED";Write(Meta(candidate.path),m);return candidate.path;
    }
    break;
   }
   int n=1;string dest;do{dest=Path.Combine(project,name+"_"+n++);}while(Directory.Exists(dest)||File.Exists(dest));
   JobOwnership.OwnDirectory(dest,project);Directory.CreateDirectory(Path.Combine(dest,"Profile"));Directory.CreateDirectory(Path.Combine(dest,"DXF"));
   var newRoles=new Dictionary<string,object>{{role,"RESERVED"}};var metadata=new Dictionary<string,object>{{"schema",1},{"source",source},{"sourceSHA256",hash},{"configuration",config},{"roles",newRoles},{"createdUtc",DateTime.UtcNow.ToString("o")}};
   if(reusableDxf!=null){var copied=new List<string>();try{
    var manifest=Read(Path.Combine(reusableDxf,"RESULT.json"));int count=0;
    foreach(var raw in (System.Collections.IEnumerable)manifest["files"]){var file=(Dictionary<string,object>)raw;string fileName=Convert.ToString(file["file"]),expected=Convert.ToString(file["sha256"]).Replace("-","").ToUpperInvariant();if(Path.GetFileName(fileName)!=fileName||String.IsNullOrWhiteSpace(fileName))throw new Exception("Invalid DXF manifest name");string input=Path.Combine(reusableDxf,"DXF",fileName),output=Path.Combine(dest,"DXF",fileName);if(Digest(input)!=expected)throw new Exception("Prior DXF changed");File.Copy(input,output,false);copied.Add(output);if(Digest(output)!=expected)throw new Exception("DXF reuse hash differs");count++;}
    if(count==0)throw new Exception("No previous DXF files");Write(Path.Combine(dest,"RESULT.json"),manifest);newRoles["DXF"]="READY";metadata["reusedDXFFrom"]=reusableDxf;
   }catch(Exception e){foreach(string p in copied)File.Delete(p);metadata["dxfReuseSkippedReason"]=e.Message;}}
   Write(Meta(dest),metadata);return dest;
  }
 }
 static int ParseNumber(string s){int n;return int.TryParse(s,out n)?n:0;}
 static string Digest(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");}
 public static void Complete(string folder,string role,bool success){
  Role(role);using(var guard=new FileStream(Path.Combine(Temp(Project(folder)),"layout.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   var m=Read(Meta(folder));var roles=(Dictionary<string,object>)m["roles"];if(!roles.ContainsKey(role)||Convert.ToString(roles[role])!="RESERVED")throw new Exception("Result role not reserved");roles[role]=success?"READY":"FAILED";Write(Meta(folder),m);
  }
 }
 public static void Tests(string root){
  if(Directory.Exists(root))throw new Exception("Use a fresh test directory");Directory.CreateDirectory(root);string source=Path.Combine(root,"part.SLDPRT");
  string a=Reserve(root,source,"HASH-A","Default","Profile");Complete(a,"Profile",true);string b=Reserve(root,source,"HASH-A","Default","DXF");if(a!=b)throw new Exception("Matching outputs separated");Complete(b,"DXF",true);
  string c=Reserve(root,source,"HASH-A","Default","Profile");if(c==a)throw new Exception("Existing role overwritten");Complete(c,"Profile",true);
  string d=Reserve(root,source,"HASH-B","Default","DXF");if(d==c||d==a)throw new Exception("Different model hash combined");Complete(d,"DXF",false);
  string e=Reserve(root,source,"HASH-B","Default","Profile");if(e==d)throw new Exception("Failed output reused");Complete(e,"Profile",true);
  string f=Reserve(root,source,"HASH-B","DifferentConfig","DXF");if(f==e)throw new Exception("Different configuration combined");
  Console.WriteLine("Shared Profile/DXF numbering: 6/6 PASS; no COM");
 }
}
