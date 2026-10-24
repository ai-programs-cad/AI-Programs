using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

public static class ManufacturingTemplateSelection {
    public static string SettingsPath {get{return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"SolidWorksBREP","ManufacturingPackage.settings.json");}}
    public static string Validate(string path){
        if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("Шаблон чертежа не задан: укажите существующий файл .drwdot.");
        string full;try{full=Path.GetFullPath(path);}catch(Exception ex){throw new ArgumentException("Некорректный путь шаблона чертежа: "+path,ex);}
        if(!String.Equals(Path.GetExtension(full),".drwdot",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Шаблон чертежа должен иметь расширение .drwdot: "+full);
        if(!File.Exists(full))throw new ArgumentException("Файл шаблона чертежа не найден: "+full);
        return full;
    }
    static Dictionary<string,object> ReadSettings(string path){
        if(!File.Exists(path))return new Dictionary<string,object>();
        try{return AB.Obj(CutListCoreV01.Json.DeserializeObject(File.ReadAllText(path,Encoding.UTF8)));}
        catch(Exception ex){Console.Error.WriteLine("Настройки выбора шаблона не прочитаны: "+ex.Message);return new Dictionary<string,object>();}
    }
    static void Remember(string settings,string selected){
        string temporary=null;
        try{
            var data=ReadSettings(settings);data["lastDrawingTemplate"]=selected;
            Directory.CreateDirectory(Path.GetDirectoryName(settings));temporary=settings+"."+Guid.NewGuid().ToString("N")+".tmp";
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(file,new UTF8Encoding(false)))writer.Write(CutListCoreV01.Json.Serialize(data));
            if(File.Exists(settings))File.Replace(temporary,settings,null);else File.Move(temporary,settings);temporary=null;
        }catch(Exception ex){Console.Error.WriteLine("Не удалось сохранить выбранный шаблон в настройках: "+ex.Message);}
        finally{if(temporary!=null&&File.Exists(temporary))try{File.Delete(temporary);}catch{}}
    }
    static string DirectoryOf(string path){
        if(String.IsNullOrWhiteSpace(path))return null;
        try{string folder=Directory.Exists(path)?path:Path.GetDirectoryName(path);return Directory.Exists(folder)?folder:null;}catch{return null;}
    }
    public static string Pick(string explicitPath,string settings,Func<IEnumerable<string>> apiLocations,Func<string,string,string> dialog){
        if(explicitPath!=null){string direct=Validate(explicitPath);Remember(settings,direct);return direct;}
        var data=ReadSettings(settings);object value;string last=data.TryGetValue("lastDrawingTemplate",out value)?value as string:null;
        string folder=DirectoryOf(last);
        if(folder==null)folder=apiLocations().Select(DirectoryOf).FirstOrDefault(p=>p!=null);
        string selected=dialog(folder,last!=null&&File.Exists(last)?Path.GetFileName(last):null);
        if(selected==null)return null;
        selected=Validate(selected);Remember(settings,selected);return selected;
    }
    public static string Select(SldWorks sw,string explicitPath){
        return Pick(explicitPath,SettingsPath,()=>{
            var locations=new List<string>();
            try{locations.Add(sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing));}catch{}
            try{locations.AddRange((sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsDocumentTemplates)??"").Split(';'));}catch{}
            return locations;
        },(folder,name)=>{
            using(var dialog=new OpenFileDialog()){
                dialog.Title="Выберите шаблон чертежа SolidWorks";
                dialog.Filter="Шаблоны чертежей SolidWorks (*.drwdot)|*.drwdot";
                dialog.DefaultExt="drwdot";dialog.AddExtension=true;dialog.CheckFileExists=true;dialog.CheckPathExists=true;
                dialog.Multiselect=false;dialog.RestoreDirectory=true;dialog.AutoUpgradeEnabled=true;
                if(folder!=null)dialog.InitialDirectory=folder;if(name!=null)dialog.FileName=name;
                return dialog.ShowDialog()==DialogResult.OK?dialog.FileName:null;
            }
        });
    }
}
