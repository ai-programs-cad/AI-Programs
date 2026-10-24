using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
class Setup {
 [STAThread] static int Main(string[] args){Application.EnableVisualStyles();try{
  if(!Environment.Is64BitOperatingSystem||!ProductEnvironment.FrameworkAvailable())throw new Exception("Требуются Windows x64 и .NET Framework 4.8 или новее.");
  string sw=ProductEnvironment.SolidWorksRoot();
  string folder=Path.Combine(Path.GetTempPath(),"PartCraft-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string msi=Path.Combine(folder,"PartCraft.msi");using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("package.msi"))using(var target=File.Create(msi))source.CopyTo(target);
  if(args.Length==1&&args[0]=="--verify-only"){Console.WriteLine("PREREQUISITES_OK; MSI="+ProductEnvironment.Hash(msi));File.Delete(msi);Directory.Delete(folder);return 0;}
  if(args.Length!=0)throw new Exception("Unknown installer argument");
  string log=Path.Combine(Path.GetTempPath(),"PartCraft-install.log");
  var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),"/i \""+msi+"\" /passive /norestart /l*v \""+log+"\""){UseShellExecute=true};int exit;using(var p=Process.Start(info)){p.WaitForExit();exit=p.ExitCode;}
  if(exit!=0&&exit!=3010)throw new Exception("Установка не завершена. Код Windows Installer: "+exit+"\nЖурнал: "+log);
  File.Delete(msi);Directory.Delete(folder);
  MessageBox.Show("PartCraft установлен.\n\nОткройте SolidWorks 2025, затем запустите PartCraft из меню «Пуск».\n\nУдаление: Параметры Windows → Приложения. Пользовательские настройки и результаты сохраняются.","PartCraft");return 0;
 }catch(Exception ex){if(args.Length>0)Console.Error.WriteLine(ex.Message);else MessageBox.Show(ex.Message,"PartCraft — установка",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}}
}
