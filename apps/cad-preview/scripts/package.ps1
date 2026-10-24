param(
 [Parameter(Mandatory=$true)][string]$BuildDirectory,
 [Parameter(Mandatory=$true)][string]$OcctBuildDirectory,
 [Parameter(Mandatory=$true)][string]$OcctSourceDirectory,
 [Parameter(Mandatory=$true)][string]$CompilerDirectory,
 [Parameter(Mandatory=$true)][string]$NsisDirectory
)
$ErrorActionPreference='Stop'
$BuildDirectory=(Resolve-Path -LiteralPath $BuildDirectory).Path
$OcctBuildDirectory=(Resolve-Path -LiteralPath $OcctBuildDirectory).Path
$OcctSourceDirectory=(Resolve-Path -LiteralPath $OcctSourceDirectory).Path
$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path
$NsisDirectory=(Resolve-Path -LiteralPath $NsisDirectory).Path
$projectRoot=Split-Path $PSScriptRoot -Parent
$payload=Join-Path $projectRoot 'dist\portable'
New-Item -ItemType Directory -Force $payload | Out-Null
foreach($name in @('CADPreview.Viewer.exe','CADPreview.Worker.exe','CADPreview.Shell.dll')) {
 Copy-Item -LiteralPath (Join-Path $BuildDirectory $name) -Destination $payload
}
Get-ChildItem -LiteralPath (Join-Path $OcctBuildDirectory 'win64\clang\bin') -Filter '*.dll' | Copy-Item -Destination $payload
foreach($name in @('libc++.dll','libunwind.dll','libwinpthread-1.dll')) {
 Copy-Item -LiteralPath (Join-Path $CompilerDirectory "x86_64-w64-mingw32\bin\$name") -Destination $payload
}
$licenses=Join-Path $payload 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
foreach($name in @('LICENSE_LGPL_21.txt','OCCT_LGPL_EXCEPTION.txt')) {
 Copy-Item -LiteralPath (Join-Path $OcctSourceDirectory $name) -Destination $licenses
}
Copy-Item -LiteralPath (Join-Path $OcctSourceDirectory 'LICENSE_LGPL_21.txt') -Destination (Join-Path $licenses 'OCCT-LICENSE.txt')
Get-ChildItem -LiteralPath (Join-Path $CompilerDirectory 'share') -Recurse -File | Where-Object { $_.Name -match 'LICENSE|COPYING|copyright' } | ForEach-Object {
 $relative=$_.FullName.Substring($CompilerDirectory.Length).TrimStart('\').Replace('\','-')
 Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenses $relative)
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\DEPENDENCIES-RU.txt') -Destination $licenses
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'third_party\licenses') -File | Copy-Item -Destination $licenses
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\NSIS-COPYING.txt') -Destination $licenses
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\USER-GUIDE-RU.txt') -Destination (Join-Path $payload 'README-RU.txt')
# OCCT STEP resource files are supplied beside the binaries.
$resources=Join-Path $payload 'resources'
New-Item -ItemType Directory -Force $resources | Out-Null
foreach($name in @('SHMessage','XSMessage','XSTEPResource','UnitsAPI','StdResource','StdLite')) {
 $source=Join-Path $OcctSourceDirectory "src\$name"
 if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $resources -Recurse -Force}
}
# Generate exact uninstall entries. No wildcard or recursive deletion of the installation directory.
$entries=@()
Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
 $relative=$_.FullName.Substring($payload.Length).TrimStart('\')
 $entries+='Delete /REBOOTOK "$INSTDIR\'+$relative+'"'
}
Get-ChildItem -LiteralPath $payload -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
 $relative=$_.FullName.Substring($payload.Length).TrimStart('\')
 $entries+='RMDir "$INSTDIR\'+$relative+'"'
}
Set-Content -LiteralPath (Join-Path $projectRoot 'installer\uninstall-files.nsh') -Value $entries -Encoding utf8
$manifest=Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
 [PSCustomObject]@{File=$_.FullName.Substring($payload.Length+1);Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'dist\SHA256-manifest.json') -Encoding utf8
Push-Location (Join-Path $projectRoot 'installer')
try {
 & (Join-Path $NsisDirectory 'makensis.exe') "/DPAYLOAD=$payload" 'CADPreview.nsi'
 if($LASTEXITCODE){throw 'Installer compilation failed'}
}finally {Pop-Location}
