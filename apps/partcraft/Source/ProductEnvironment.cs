using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using System.Xml;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Win32;

static class AppStorage {
 public const string ProductId="PartCraft.Personal";
 public const string Version="1.0.0-personal";
 static bool migrated;
 public static string DataRoot {get{string test=Environment.GetEnvironmentVariable("SOLIDWORKSBREP_TEST_DATA");string path=String.IsNullOrWhiteSpace(test)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PartCraft"):Path.GetFullPath(test);Directory.CreateDirectory(path);if(!migrated){migrated=true;if(String.IsNullOrWhiteSpace(test)){string previous=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SolidWorksBREP-Test");foreach(string name in new[]{"languages.json","unified-model.txt","last-result.txt","duration-history.json"}){string input=Path.Combine(previous,name),output=Path.Combine(path,name);if(File.Exists(input)&&!File.Exists(output))File.Copy(input,output,false);}}}return path;}}
 public static string FilePath(string name){if(Path.GetFileName(name)!=name)throw new ArgumentException("Settings file must be a basename");return Path.Combine(DataRoot,name);}
}
static class ProductEnvironment {
 public static readonly string[] Interops={"sldworks","swconst","swcommands"};
 static readonly string[] Tokens={"7c4797c3e4eeac03","19f43e188e4269d8","0fda92720ba7919f"};
 public static string Hash(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","");}
 public static bool FrameworkAvailable(){using(var k=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))return k!=null&&Convert.ToInt32(k.GetValue("Release",0))>=528040;}
 public static string SolidWorksRoot(){
  var candidates=new List<string>();using(var k=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SOLIDWORKS\SOLIDWORKS 2025\Setup")){if(k!=null)candidates.Add(Convert.ToString(k.GetValue("SolidWorks Folder")));}
  using(var k=Registry.ClassesRoot.OpenSubKey(@"SldWorks.Application.33\CLSID")){if(k!=null)using(var server=Registry.ClassesRoot.OpenSubKey(@"CLSID\"+Convert.ToString(k.GetValue(null))+@"\LocalServer32")){if(server!=null){string s=Environment.ExpandEnvironmentVariables(Convert.ToString(server.GetValue(null)));var m=System.Text.RegularExpressions.Regex.Match(s,"^\\\"([^\\\"]+\\.exe)\\\"",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(m.Success)candidates.Add(Path.GetDirectoryName(m.Groups[1].Value));}}}
  foreach(string root in candidates.Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)){string exe=Path.Combine(root,"SLDWORKS.exe");if(File.Exists(exe)&&FileVersionInfo.GetVersionInfo(exe).FileMajorPart==33)return Path.GetFullPath(root);}
  throw new InvalidOperationException("SOLIDWORKS 2025 x64 не найден. Установите лицензированный SOLIDWORKS 2025 перед установкой приложения.");
 }
 public static Dictionary<string,string> Configure(string applicationRoot){
  if(!Environment.Is64BitOperatingSystem||!Environment.Is64BitProcess)throw new Exception("Требуется Windows x64 и 64-разрядная программа.");if(!FrameworkAvailable())throw new Exception("Требуется .NET Framework 4.8 или новее.");
  string sw=SolidWorksRoot(),bin=Path.Combine(applicationRoot,"Program","bin");Directory.CreateDirectory(bin);var imported=new Dictionary<string,string>();var assemblies=new List<AssemblyName>();
  for(int i=0;i<Interops.Length;i++){
   string name="SolidWorks.Interop."+Interops[i]+".dll",source=Path.Combine(sw,"api","redist",name);if(!File.Exists(source))throw new Exception("В установленном SOLIDWORKS не найден API interop: "+source);
   var a=AssemblyName.GetAssemblyName(source);string token=BitConverter.ToString(a.GetPublicKeyToken()).Replace("-","").ToLowerInvariant();if(a.Version.Major!=33||a.Name!="SolidWorks.Interop."+Interops[i]||token!=Tokens[i])throw new Exception("Неожиданная версия/идентичность interop: "+name);
   string destination=Path.Combine(bin,name);if(!File.Exists(destination)||Hash(destination)!=Hash(source))File.Copy(source,destination,true);assemblies.Add(a);imported[name]=Hash(destination);
  }
  foreach(string executable in Directory.GetFiles(bin,"*.exe"))WriteConfig(executable+".config",assemblies);
  string runtime=Path.Combine(applicationRoot,"Runtime","python.exe");if(!File.Exists(runtime))throw new Exception("Автономный CAM runtime отсутствует.");File.WriteAllText(Path.Combine(bin,"cam-python-path.txt"),runtime,new UTF8Encoding(false));
  return imported;
 }
 static void WriteConfig(string path,IEnumerable<AssemblyName> assemblies){
  var settings=new XmlWriterSettings{Indent=true,Encoding=new UTF8Encoding(false)};using(var x=XmlWriter.Create(path,settings)){x.WriteStartDocument();x.WriteStartElement("configuration");x.WriteStartElement("startup");x.WriteStartElement("supportedRuntime");x.WriteAttributeString("version","v4.0");x.WriteAttributeString("sku",".NETFramework,Version=v4.8");x.WriteEndElement();x.WriteEndElement();x.WriteStartElement("runtime");x.WriteStartElement("assemblyBinding","urn:schemas-microsoft-com:asm.v1");foreach(var a in assemblies){x.WriteStartElement("dependentAssembly");x.WriteStartElement("assemblyIdentity");x.WriteAttributeString("name",a.Name);x.WriteAttributeString("publicKeyToken",BitConverter.ToString(a.GetPublicKeyToken()).Replace("-","").ToLowerInvariant());x.WriteAttributeString("culture","neutral");x.WriteEndElement();x.WriteStartElement("bindingRedirect");x.WriteAttributeString("oldVersion","33.0.0.0-33.65535.65535.65535");x.WriteAttributeString("newVersion",a.Version.ToString());x.WriteEndElement();x.WriteEndElement();}x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();x.WriteEndDocument();}
 }
}
interface ILicenseService {LicenseDecision Check(string module);}
sealed class LicenseDecision {public bool Allowed;public string Mode,Message;}
sealed class PersonalUseLicenseService:ILicenseService {
 public LicenseDecision Check(string module){return new LicenseDecision{Allowed=true,Mode="PERSONAL_USE",Message="Personal edition; no payments, activation or enforced trial period"};}
}
