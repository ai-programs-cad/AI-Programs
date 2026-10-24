param(
 [Parameter(Mandatory=$true)][string]$CompilerDirectory,
 [Parameter(Mandatory=$true)][string]$CmakeDirectory,
 [Parameter(Mandatory=$true)][string]$NinjaExecutable,
 [Parameter(Mandatory=$true)][string]$OcctSourceDirectory,
 [int]$Parallel=6
)
$ErrorActionPreference='Stop'
$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path
$CmakeDirectory=(Resolve-Path -LiteralPath $CmakeDirectory).Path
$NinjaExecutable=(Resolve-Path -LiteralPath $NinjaExecutable).Path
$OcctSourceDirectory=(Resolve-Path -LiteralPath $OcctSourceDirectory).Path
$projectRoot=Split-Path $PSScriptRoot -Parent
$build=Join-Path $projectRoot 'build'
$occt=Join-Path $build 'occt'
$app=Join-Path $build 'app'
$cmake=Join-Path $CmakeDirectory 'cmake.exe'
$env:PATH=(Join-Path $CompilerDirectory 'bin')+';'+(Join-Path $CompilerDirectory 'x86_64-w64-mingw32\bin')+';'+(Join-Path $occt 'win64\clang\bin')+';'+$env:PATH
& $cmake -S $OcctSourceDirectory -B $occt -G Ninja "-DCMAKE_MAKE_PROGRAM=$NinjaExecutable" "-DCMAKE_C_COMPILER=$CompilerDirectory/bin/clang.exe" "-DCMAKE_CXX_COMPILER=$CompilerDirectory/bin/clang++.exe" '-DCMAKE_BUILD_TYPE=Release' '-DBUILD_MODULE_Draw=OFF' '-DBUILD_MODULE_Visualization=OFF' '-DUSE_FREETYPE=OFF' '-DUSE_TK=OFF' '-DUSE_TCL=OFF' '-DUSE_OPENGL=OFF' '-DUSE_TBB=OFF' '-DCMAKE_EXPORT_NO_PACKAGE_REGISTRY=ON'
if($LASTEXITCODE){throw 'OCCT configure failed'}
& $cmake --build $occt --target TKDESTEP TKXCAF TKMesh TKBool --parallel $Parallel
if($LASTEXITCODE){throw 'OCCT compilation failed'}
& $cmake -S $projectRoot -B $app -G Ninja "-DCMAKE_MAKE_PROGRAM=$NinjaExecutable" "-DCMAKE_CXX_COMPILER=$CompilerDirectory/bin/clang++.exe" "-DCADPREVIEW_OCCT_BUILD_ROOT=$occt" '-DCADPREVIEW_WITH_OCCT=ON' '-DCMAKE_BUILD_TYPE=Release'
if($LASTEXITCODE){throw 'Application configure failed'}
& $cmake --build $app --parallel $Parallel
if($LASTEXITCODE){throw 'Application compilation failed'}
& (Join-Path $app 'CADPreview.StepFixtures.exe') (Join-Path $projectRoot 'tests\fixtures')
if($LASTEXITCODE){throw 'STEP fixture generation failed'}
& (Join-Path $CmakeDirectory 'ctest.exe') --test-dir $app --output-on-failure
if($LASTEXITCODE){throw 'Tests failed'}
