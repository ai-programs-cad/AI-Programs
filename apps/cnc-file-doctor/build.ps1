$ErrorActionPreference='Stop'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/win32icon:$PSScriptRoot\assets\cnc-file-doctor.ico" "/out:$PSScriptRoot\CNC File Doctor.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\src\Launcher.cs" "$PSScriptRoot\src\Interface.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& "$PSScriptRoot\runtime\python.exe" -m compileall -q "$PSScriptRoot\src"
if ($LASTEXITCODE -ne 0) { throw 'Python compilation failed' }
