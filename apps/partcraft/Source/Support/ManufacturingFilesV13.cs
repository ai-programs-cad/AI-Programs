using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
public class MPFiles {
    string preparedWorkspace,generatedSession,outputPrefix="";
    public string OutputRoot {get{return Path.Combine(Root,outputPrefix);}}
    public string Root;public HashSet<string> Owned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Created=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public MPFiles(string root,bool create){
        Root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(Directory.Exists(Root)||File.Exists(Root))throw new InvalidOperationException("Output path already exists; overwrite is forbidden: "+Root);
        if(create){Directory.CreateDirectory(Root);Write("PACKAGE_RUNNING.lock","Created by this new package run only");}
    }
    MPFiles(string workspace){
        preparedWorkspace=Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);Root=Directory.GetParent(preparedWorkspace).FullName;
        if(Path.GetFileName(preparedWorkspace)!="Workspace")throw new InvalidOperationException("Prepared copy folder must be named Workspace");
        foreach(string name in new[]{"DXF","Drawings"})if(Directory.Exists(Path.Combine(Root,name))||File.Exists(Path.Combine(Root,name)))throw new InvalidOperationException("Previous outputs exist; prepare a NEW Run folder: "+Path.Combine(Root,name));
        string reports=Path.Combine(Root,"Reports");if(File.Exists(reports)||Directory.Exists(reports)&&(Directory.GetDirectories(reports).Length>0||Directory.GetFiles(reports).Any(p=>!Path.GetFileName(p).StartsWith("manufacturing-workspace-v13-",StringComparison.Ordinal))))throw new InvalidOperationException("Reports already contains production outputs; new Run required");
        generatedSession="Workspace/_Generated_"+Guid.NewGuid().ToString("N");
    }
    public static MPFiles ValidateWorkspaceRun(string workspace){return new MPFiles(workspace);}
    MPFiles(string workspace,string uniqueOutput){
        preparedWorkspace=Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);Root=Directory.GetParent(preparedWorkspace).FullName;
        if(Path.GetFileName(preparedWorkspace)!="Workspace")throw new InvalidOperationException("Prepared copy folder must be named Workspace");
        outputPrefix=uniqueOutput;if(Directory.Exists(OutputRoot)||File.Exists(OutputRoot))throw new InvalidOperationException("Output already exists: "+OutputRoot);
        generatedSession="Workspace/_Generated_"+Guid.NewGuid().ToString("N");
    }
    public static MPFiles NewProductionRun(string workspace){return new MPFiles(workspace,"Package_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));}
    public void BeginWorkspaceRun(){Write("PACKAGE_RUNNING.lock","Prepared workspace run only; no automatic Pack and Go");}
    public string PathFor(string relative){
        if(preparedWorkspace!=null){
            string rel=relative.Replace('\\','/');
            string[,] maps={{"dxf/","DXF/"},{"exact/","DXF/EXACT/"},{"cam/","DXF/CAM/"},{"bends/","DXF/BEND/"},{"drawings/",generatedSession+"/Drawings/"},{"portable/",generatedSession+"/Portable/"},{"models/",generatedSession+"/Models/"},{"working/",generatedSession+"/Working/"},{"pdf/","Drawings/PDF/"}};
            bool mapped=false;for(int i=0;i<maps.GetLength(0);i++)if(rel.StartsWith(maps[i,0],StringComparison.Ordinal)){relative=maps[i,1]+rel.Substring(maps[i,0].Length);mapped=true;break;}
            if(!mapped){if(rel.StartsWith("final/",StringComparison.Ordinal))relative="Drawings/"+rel.Substring(6);else if(rel.IndexOf('/')<0)relative="Reports/"+rel;else throw new InvalidOperationException("Unmapped prepared-workspace output: "+rel);}
        }
        if(outputPrefix!=""&&!relative.Replace('\\','/').StartsWith("Workspace/",StringComparison.Ordinal))relative=Path.Combine(outputPrefix,relative);
        string p=Path.GetFullPath(Path.Combine(Root,relative));
        if(!p.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Output path escapes package root");
        return p;
    }
    public string Reserve(string relative){
        string p=PathFor(relative);if(File.Exists(p)||Directory.Exists(p))throw new InvalidOperationException("Refusing to overwrite "+p);
        Directory.CreateDirectory(Path.GetDirectoryName(p));Owned.Add(p);return p;
    }
    public string Write(string relative,string text){
        string p=Reserve(relative);using(var s=new FileStream(p,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var w=new StreamWriter(s,new UTF8Encoding(false)))w.Write(text);Created.Add(p);return p;
    }
    public void RequireOwned(string path){if(!Owned.Contains(Path.GetFullPath(path)))throw new InvalidOperationException("Attempt to save a non-owned output document");}
    public void BeforeSave(string path){RequireOwned(path);if(File.Exists(path)&&!Created.Contains(path))throw new InvalidOperationException("Output appeared after reservation; refusing overwrite");}
}
