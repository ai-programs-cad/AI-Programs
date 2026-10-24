param(
 [Parameter(Mandatory=$true)][string]$CompilerDirectory,
 [Parameter(Mandatory=$true)][string]$OcctBuildDirectory,
 [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$taskCompiler=(Resolve-Path (Join-Path $CompilerDirectory 'bin/clang++.exe')).Path
$occt=(Resolve-Path $OcctBuildDirectory).Path
if(!$OutputDirectory){$OutputDirectory=Join-Path $project 'build/app'}; $out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $out | Out-Null
$flags=@('-std=gnu++17','-O2','-DNDEBUG','-DUNICODE','-D_UNICODE','-DNOMINMAX','-DWIN32_LEAN_AND_MEAN','-D_WIN32_WINNT=0x0A00','-DCADPREVIEW_WITH_OCCT',"-I$project/include","-I$occt/inc")
$common=@('core','dxf','stl','scene_io','step','renderer','worker_client','viewport','settings','help_text')
$objects=@()
foreach($name in $common){
 $obj=Join-Path $out "$name.obj"
 & $taskCompiler @flags -c "$project/src/$name.cpp" -o $obj
 if($LASTEXITCODE){throw "Compile failed: $name"}
 $objects+=$obj
}
$libs=@("-L$occt/win64/clang/lib",'-lTKernel','-lTKMath','-lTKG2d','-lTKG3d','-lTKGeomBase','-lTKBRep','-lTKGeomAlgo','-lTKTopAlgo','-lTKMesh','-lTKShHealing','-lTKCDF','-lTKLCAF','-lTKCAF','-lTKXCAF','-lTKDE','-lTKDESTEP','-lTKXSBase','-lgdi32','-luser32','-lcomctl32','-lcomdlg32','-lshell32','-lole32','-ladvapi32','-luuid','-lshlwapi')
$targets=@(
 @{Name='CADPreview.Worker.exe';Source='src/worker.cpp';Extra=@('-municode')},
 @{Name='CADPreview.Viewer.exe';Source='src/viewer.cpp';Extra=@('-municode','-mwindows')},
 @{Name='CADPreview.Shell.dll';Source='src/shell.cpp';Extra=@('-shared',"$project/src/shell.def")},
 @{Name='CADPreview.Tests.exe';Source='tests/tests.cpp';Extra=@()},
 @{Name='CADPreview.NativeTests.exe';Source='tests/native_tests.cpp';Extra=@()}
)
foreach($target in $targets){
 $extra=$target.Extra
 & $taskCompiler @flags "$project/$($target.Source)" @objects @libs @extra -o "$out/$($target.Name)"
 if($LASTEXITCODE){throw "Link failed: $($target.Name)"}
}
Write-Output "Built all targets: $out"


