using System;using System.IO;
public static class SheetMetalStorage {
 public static string DataRoot {get {string test=Environment.GetEnvironmentVariable("SOLIDWORKSBREP_TEST_DATA");return String.IsNullOrWhiteSpace(test)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PartCraft"):Path.GetFullPath(test);}}
 public static string WorkRoot {get {return Path.Combine(DataRoot,"DxfWork");}}
 public static void ValidatePreparedSource(string workspace,string source){
  workspace=Path.GetFullPath(workspace);source=Path.GetFullPath(source);
  JobOwnership.Validate(workspace,WorkRoot);
  if(!Directory.Exists(workspace)||Path.GetFileName(workspace)!="Workspace"||!File.Exists(source)||!String.Equals(Path.GetExtension(source),".SLDPRT",StringComparison.OrdinalIgnoreCase))throw new Exception("INVALID_OWNED_EXPORT_WORKSPACE");
  if(ManufacturingWorkspaceV13.Inside(workspace,source))throw new Exception("Expected immutable original outside disposable workspace");
 }
}
