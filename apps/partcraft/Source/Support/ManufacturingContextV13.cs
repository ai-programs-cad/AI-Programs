using System;
using System.IO;
using System.Collections.Generic;
public static class ManufacturingContextV13 {
    public static bool SamePath(string expected,string actual) {
        if(String.IsNullOrWhiteSpace(expected)||String.IsNullOrWhiteSpace(actual))return false;
        try {return String.Equals(Path.GetFullPath(expected),Path.GetFullPath(actual),StringComparison.OrdinalIgnoreCase);}catch{return false;}
    }
    public static bool Matches(string expectedPath,string expectedConfiguration,string actualPath,string actualConfiguration) {
        return SamePath(expectedPath,actualPath)&&!String.IsNullOrEmpty(expectedConfiguration)&&String.Equals(expectedConfiguration,actualConfiguration,StringComparison.Ordinal);
    }
    public static Dictionary<string,object> Check(string stage,string expectedPath,string expectedConfiguration,string actualPath,string actualConfiguration) {
        return new Dictionary<string,object>{{"stage",stage},{"expectedPath",expectedPath},{"actualPath",actualPath},{"expectedConfigurationApiName",expectedConfiguration},{"actualConfigurationApiName",actualConfiguration},
            {"pathMatches",SamePath(expectedPath,actualPath)},{"configurationMatches",String.Equals(expectedConfiguration,actualConfiguration,StringComparison.Ordinal)},
            {"passed",Matches(expectedPath,expectedConfiguration,actualPath,actualConfiguration)},
            {"configurationPolicy","Exact API Name; no stripping display suffixes, trimming, case folding or configuration activation"}};
    }
    public static string SaveDiagnostic(string folder,object report) {
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"manufacturing-workspace-v13-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".json");
        var dictionary=report as Dictionary<string,object>;if(dictionary!=null)dictionary["diagnosticFile"]=path;
        using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
        using(var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false)))writer.Write(ManufacturingStateV09.Json(report));
        return path;
    }
}

