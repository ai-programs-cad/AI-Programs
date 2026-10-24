# Building and testing

## Python tests

Install CPython 3.12 or 3.13. No pip dependencies are needed for these test suites.

```powershell
python tools/run_tests.py cnc
python tools/run_tests.py partcraft
```

## CNC File Doctor

Windows and .NET Framework 4.x C# compiler are needed for the GUI. Place a complete Python distribution in apps/cnc-file-doctor/runtime, or make that directory point to your own runtime. The source repository does not bundle an interpreter.

```powershell
cd apps/cnc-file-doctor
./build.ps1
```

The engine can also be run directly with Python: `python src/doctor.py --help`.

## PartCraft

Windows x64, .NET Framework 4.8, CPython 3.13 runtime directory and licensed SOLIDWORKS 2025 x64 with its API are required. The build script takes the API assemblies from your SOLIDWORKS installation; never commit these DLLs.

```powershell
cd apps/partcraft
./InitializeBuild.ps1 -PythonRuntimeDirectory 'C:\Python313'
./Source/BuildStopCandidate.ps1
# Or provide -InteropDirectory pointing to your SOLIDWORKS api/redist directory.
```

InitializeBuild copies the Python runtime and project resources into a generated Release directory. It compiles the CAM module and creates the entry wrapper. Source algorithms are unchanged. Run `Release/PartCraft.exe` with a saved part open in SOLIDWORKS. BuildInstaller.ps1 can package a complete Release directory; installation must be verified on a separate Windows environment before a public binary release.

## CAD Preview

CMake 3.24+, a C++17 compiler and Open CASCADE are needed for the full Windows product. scripts/build.ps1 describes the original LLVM/MinGW + Ninja toolchain. scripts/build-existing-occt.ps1 accepts a prebuilt OCCT tree. Installer packaging uses NSIS and expects a complete runtime payload.

For geometry-only tests without STEP/Explorer integration:

```powershell
cmake -S apps/cad-preview -B build/cad-core -DCADPREVIEW_WITH_OCCT=OFF
cmake --build build/cad-core --config Release
ctest --test-dir build/cad-core -C Release --output-on-failure
```

The no-OCCT build is a partial test build: it does not validate STEP or Explorer integration. Full native and STEP tests require the Windows toolchain and OCCT.

## Macros

Each macros subdirectory contains the actual SWP project and the VBA module text extracted from it without execution. Edit the SWP in the SOLIDWORKS VBA editor; commit the exported module text alongside it for review. Attribute lines are VBA export metadata. StileDXF is a plain-text layer mapping, not a macro. Start by testing on a model copy.
