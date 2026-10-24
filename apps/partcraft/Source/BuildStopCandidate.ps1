param([string]$InteropDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$src=Join-Path $root Source
$bin=Join-Path $root 'Release\Program\bin'
$uiPath=[IO.Path]::GetFullPath((Join-Path $root 'Release\PartCraft.exe'))
if(Get-Process PartCraft -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $uiPath }){throw 'Close this development PartCraft window before rebuilding. No binaries were changed.'}
$csc="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$refs=@('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Numerics.dll','/r:System.IO.Compression.dll','/r:Microsoft.CSharp.dll')
if(!$InteropDirectory){$sw=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\SOLIDWORKS\SOLIDWORKS 2025\Setup';$InteropDirectory=Join-Path $sw.'SolidWorks Folder' 'api\redist'}
$dev=[IO.Path]::GetFullPath($InteropDirectory)
foreach($n in 'sldworks','swconst','swcommands'){$refs+="/r:$dev\SolidWorks.Interop.$n.dll"}
function Compile($main,$out,$files,$references){ & $csc /nologo /codepage:65001 /platform:x64 /optimize+ /debug- /target:exe "/main:$main" "/out:$out" $references $files;if($LASTEXITCODE){throw "Compile failed: $main"}}
$support=@([IO.Directory]::GetFiles((Join-Path $src Support),'*.cs'))+@((Join-Path $src JobOwnership.cs))
Compile 'ManufacturingAgent' "$bin\ManufacturingAgentExportCompare.exe" $support $refs
$workerRefs=$refs+@("/r:$bin\ManufacturingAgentExportCompare.exe")
Compile 'NativeDxfEntry' "$bin\SheetMetalDxfBatch.exe" @([IO.Directory]::GetFiles((Join-Path $src Exporter),'*.cs')) $workerRefs
$profiles=@('Workers\ProfilesAuto.cs','Workers\ProfilesCutTables.cs','SharedResultFolder.cs','LanguageCatalog.cs') | ForEach-Object{Join-Path $src $_}
Compile 'ProfilesCutTables' "$bin\ProfilesCutTables.exe" $profiles $workerRefs
$faces=@('Workers\SelectedFacesDxf.cs','SharedResultFolder.cs','LanguageCatalog.cs') | ForEach-Object{Join-Path $src $_}
Compile 'SelectedFacesDxf' "$bin\SelectedFacesDxf.exe" $faces $workerRefs
Compile 'PreflightCheck' "$bin\PreflightCheck.exe" (@('PreflightCheck.cs','ProductEnvironment.cs')|ForEach-Object{Join-Path $src $_}) $workerRefs
Compile 'JobRunner' "$bin\JobRunner.exe" (@('JobRunner.cs','ProductEnvironment.cs','LanguageCatalog.cs')|ForEach-Object{Join-Path $src $_}) $refs
$ui=@('UnifiedLauncher','DxfLauncher','ProfilesJobWindow','FacesJobWindow','RunProgress','ExportTimingJournal','ResultLayout','SharedResultFolder','LanguageCatalog','LocalizedUi','ProductEnvironment','CandidateTools','UiRegressionChecks','JobDisplay','UiAcceptance','ModuleLayout','LayoutChecks','JobControl','JobOwnership','PartCraftAssembly')|ForEach-Object{Join-Path $src ($_+'.cs')}
$hash=(Get-FileHash "$bin\SheetMetalDxfBatch.exe" -Algorithm SHA256).Hash
$p=Join-Path $src DxfLauncher.cs;$text=[IO.File]::ReadAllText($p);$text=[regex]::Replace($text,'const string Expected="[A-F0-9]+";',('const string Expected="'+$hash+'";'));[IO.File]::WriteAllText($p,$text,[Text.UTF8Encoding]::new($true))
& $csc /nologo /codepage:65001 /platform:x64 /optimize+ /debug- /target:winexe /main:UnifiedLauncher "/win32icon:$root\Release\Assets\PartCraft.ico" "/out:$root\Release\PartCraft.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll $ui
if($LASTEXITCODE){throw 'UI compile failed'}
