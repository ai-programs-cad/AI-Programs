$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$payload=Join-Path $root Release
$out=Join-Path $root Distribution
[IO.Directory]::CreateDirectory($out) | Out-Null
$msi=Join-Path $out PartCraft.msi
if(Test-Path $msi){throw 'Use a new candidate folder rather than overwrite an MSI'}
$installer=New-Object -ComObject WindowsInstaller.Installer
$db=$installer.OpenDatabase($msi,3)
function SQL([string]$query){try{$v=$db.OpenView($query);$v.Execute();$v.Close()}catch{throw ($query+"`n"+$_.Exception.Message)}}
function Row([string]$table,[object[]]$values){$fields=@($values|ForEach-Object{if($null -eq $_){'NULL'}elseif($_ -is [int]){[string]$_}else{"'"+([string]$_).Replace("'","''")+"'"}}); $cv=$db.OpenView('SELECT `Name` FROM `_Columns` WHERE `Table` = '''+$table+''' ORDER BY `Number`');$cv.Execute();$cols=@();while($cr=$cv.Fetch()){$cols+=('`'+$cr.StringData(1)+'`')};$cv.Close();SQL ('INSERT INTO `'+$table+'` ('+($cols -join ',')+') VALUES ('+($fields -join ',')+')')}
SQL 'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`)'
SQL 'CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)'
SQL 'CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)'
SQL 'CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)'
SQL 'CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)'
SQL 'CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` LONG NOT NULL PRIMARY KEY `File`)'
SQL 'CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` LONG NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)'
SQL 'CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)'
SQL 'CREATE TABLE `Shortcut` (`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) NOT NULL, `Arguments` CHAR(255), `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72) PRIMARY KEY `Shortcut`)'
SQL 'CREATE TABLE `RemoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`)'
SQL 'CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)'
SQL 'CREATE TABLE `AdminExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)'
SQL 'CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`)'
SQL 'CREATE TABLE `Icon` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)'
SQL 'CREATE TABLE `Upgrade` (`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`)'
$product='{FC308E39-7149-4830-B802-0BD03CFA7C28}'
foreach($pair in @(@('ProductCode',$product),@('UpgradeCode','{648359BA-4FC8-4DB4-AD95-4B0169F13EC6}'),@('ProductName','PartCraft'),@('ProductVersion','1.0.0'),@('SecureCustomProperties','OLDPRODUCTS'),@('ProductLanguage','1033'),@('Manufacturer','PartCraft'),@('INSTALLLEVEL','1'),@('MSIINSTALLPERUSER','1'),@('ARPNOMODIFY','1'),@('ARPNOREPAIR','1'),@('ARPHELPLINK',''),@('ARPCOMMENTS','Personal edition. User settings and model results are preserved on removal.'))){if($pair[1]){Row Property $pair}}
Row Upgrade @('{648359BA-4FC8-4DB4-AD95-4B0169F13EC6}','0.0.0','1.0.0',$null,256,'ALL','OLDPRODUCTS')
Row LaunchCondition @('VersionNT64','64-bit Windows is required.')
Row Directory @('TARGETDIR',$null,'SourceDir')
Row Directory @('LocalAppDataFolder','TARGETDIR','.')
Row Directory @('ProgramsDir','LocalAppDataFolder','Programs')
Row Directory @('INSTALLDIR','ProgramsDir','PartCraft')
Row Directory @('ProgramMenuFolder','TARGETDIR','.')
Row Directory @('MenuDir','ProgramMenuFolder','PartCraft')
Row Feature @('Main',$null,'PartCraft','Production documentation tools',1,1,'INSTALLDIR',0)
$dirs=@{''='INSTALLDIR'}
$files=@([IO.Directory]::GetFiles($payload,'*',[IO.SearchOption]::AllDirectories)|Sort-Object)
$ddf=New-Object Collections.Generic.List[string]
$ddf.Add('.OPTION EXPLICIT');$ddf.Add('.Set CabinetNameTemplate=payload.cab');$ddf.Add('.Set DiskDirectoryTemplate="'+$out+'"');$ddf.Add('.Set CompressionType=MSZIP');$ddf.Add('.Set Cabinet=on');$ddf.Add('.Set Compress=on');$ddf.Add('.Set MaxDiskSize=0')
$i=0;$manifest=@{}
foreach($file in $files){
 $relative=$file.Substring($payload.Length+1)
 if($relative -match '(?i)(^|\\)(SolidWorks.Interop\..*\.dll|cam-python-path.txt|.*\.exe.config)$' -or $relative -match '(?i)\\bridge\\'){throw ('Generated local dependency must not be distributed: '+$relative)}
 $parent=Split-Path $relative -Parent
 if(!$dirs.ContainsKey($parent)){
  $walk='';$prev='INSTALLDIR'
  foreach($part in ($parent -split '\\')){if(!$part){continue};$walk=if($walk){$walk+'\'+$part}else{$part};if(!$dirs.ContainsKey($walk)){$id='D'+$dirs.Count;Row Directory @($id,$prev,$part);$dirs[$walk]=$id};$prev=$dirs[$walk]}
 }
 $i++;$id='F'+$i;$component='C'+$i;$reg='R'+$i
 Row Component @($component,('{'+[guid]::NewGuid().ToString().ToUpper()+'}'),$dirs[$parent],260,$null,$reg)
 Row FeatureComponents @('Main',$component)
 Row Registry @($reg,1,'Software\PartCraft\Components',$component,'1',$component)
 Row File @($id,$component,[IO.Path]::GetFileName($file),[int](Get-Item -LiteralPath $file).Length,$null,$null,512,$i)
 $ddf.Add('"'+$file+'" '+$id)
 $manifest[$relative]=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
 if($relative -eq 'PartCraft.exe'){$launcherComponent=$component}
}
$iconView=$db.OpenView('INSERT INTO `Icon` (`Name`,`Data`) VALUES (?,?)');$iconRecord=$installer.CreateRecord(2);$iconRecord.StringData(1)='PartCraft.ico';$iconRecord.SetStream(2,(Join-Path $payload 'Assets\PartCraft.ico'));$iconView.Execute($iconRecord);$iconView.Close();Row Property @('ARPPRODUCTICON','PartCraft.ico')
Row Shortcut @('MainShortcut','MenuDir','PartCraft',$launcherComponent,'[INSTALLDIR]PartCraft.exe',$null,'Production documentation for SOLIDWORKS 2025',$null,'PartCraft.ico',0,1,'INSTALLDIR')
Row RemoveFile @('MenuCleanup',$launcherComponent,$null,'MenuDir',2)
$generated=@('cam-python-path.txt','SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll','SolidWorks.Interop.swcommands.dll')+@($files|Where-Object{[IO.Path]::GetExtension($_) -eq '.exe' -and $_ -match '\\Program\\bin\\'}|ForEach-Object{[IO.Path]::GetFileName($_)+'.config'})
$j=0;foreach($name in $generated){$j++;Row RemoveFile @(('Generated'+$j),$launcherComponent,$name,$dirs['Program\bin'],2)}
foreach($action in @(@('FindRelatedProducts',25),@('RemoveExistingProducts',1520),@('LaunchConditions',100),@('ValidateProductID',700),@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('InstallValidate',1400),@('InstallInitialize',1500),@('ProcessComponents',1600),@('UnpublishFeatures',1800),@('RemoveShortcuts',3200),@('RemoveFiles',3500),@('RemoveFolders',3600),@('CreateFolders',3700),@('InstallFiles',4000),@('CreateShortcuts',4500),@('RemoveRegistryValues',2600),@('WriteRegistryValues',5000),@('RegisterUser',6000),@('RegisterProduct',6100),@('PublishFeatures',6300),@('PublishProduct',6400),@('InstallFinalize',6600))){Row InstallExecuteSequence @($action[0],$null,[int]$action[1])}
foreach($action in @(@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('InstallValidate',1400),@('InstallInitialize',1500),@('InstallAdminPackage',3900),@('InstallFiles',4000),@('InstallFinalize',6600))){Row AdminExecuteSequence @($action[0],$null,[int]$action[1])}
$ddfPath=Join-Path $out payload.ddf
[IO.File]::WriteAllLines($ddfPath,$ddf,[Text.Encoding]::Default)
& "$env:WINDIR\System32\makecab.exe" /F $ddfPath | Out-Null
if($LASTEXITCODE){throw 'Cabinet build failed'}
Row Media @(1,$i,$null,'#payload.cab',$null,$null)
$v=$db.OpenView('INSERT INTO `_Streams` (`Name`,`Data`) VALUES (?,?)');$record=$installer.CreateRecord(2);$record.StringData(1)='payload.cab';$record.SetStream(2,(Join-Path $out payload.cab));$v.Execute($record);$v.Close()
$summary=$db.SummaryInformation(20)
$summary.Property(2)='PartCraft installer';$summary.Property(3)='Personal edition for SOLIDWORKS 2025';$summary.Property(4)='SolidWorksBREP';$summary.Property(7)='x64;1033';$summary.Property(9)='{'+[guid]::NewGuid().ToString().ToUpper()+'}';$summary.Property(14)=200;$summary.Property(15)=2;$summary.Persist();$db.Commit()
$manifest|ConvertTo-Json|Set-Content (Join-Path $out payload-manifest.json) -Encoding UTF8
Write-Output ('MSI_CREATED files='+$i+' product='+$product)
foreach($com in @($summary,$record,$v,$iconRecord,$iconView,$db,$installer)){if($null -ne $com -and [Runtime.InteropServices.Marshal]::IsComObject($com)){[Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) | Out-Null}}
[GC]::Collect()
[GC]::WaitForPendingFinalizers()
