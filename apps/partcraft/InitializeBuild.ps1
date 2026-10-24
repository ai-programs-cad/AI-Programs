param([Parameter(Mandatory=$true)][string]$PythonRuntimeDirectory)
$ErrorActionPreference='Stop'
$release=Join-Path $PSScriptRoot Release
if(Test-Path -LiteralPath $release){throw 'Release already exists; use a clean build directory.'}
$python=Join-Path $PythonRuntimeDirectory python.exe
if(!(Test-Path -LiteralPath $python)){throw 'Complete Python runtime with python.exe required'}
New-Item -ItemType Directory -Path (Join-Path $release 'Program/bin/production/cam-source') -Force | Out-Null
Copy-Item -LiteralPath $PythonRuntimeDirectory -Destination (Join-Path $release Runtime) -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot assets) -Destination (Join-Path $release Assets) -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot resources) -Destination (Join-Path $release 'Program/bin/Resources') -Recurse
$cam=Join-Path $release 'Program/bin/production/cam-source'
Copy-Item -Path (Join-Path $PSScriptRoot 'Source/Cam/*.py') -Destination $cam
Move-Item -LiteralPath (Join-Path $cam prepare_cam_arcs.py) -Destination (Join-Path $cam prepare_cam_arcs_impl.py)
[IO.File]::WriteAllText((Join-Path $cam prepare_cam_arcs.py),"import pathlib,runpy,sys`nsys.path.insert(0,str(pathlib.Path(__file__).parent))`nrunpy.run_path(str(pathlib.Path(__file__).parent/'prepare_cam_arcs_impl.py'),run_name='__main__')`n",[Text.UTF8Encoding]::new($false))
& (Join-Path $release 'Runtime/python.exe') -m compileall -q $cam
if($LASTEXITCODE){throw 'CAM compilation failed'}
