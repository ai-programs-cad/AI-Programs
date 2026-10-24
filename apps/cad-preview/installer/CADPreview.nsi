Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "WinVer.nsh"
!ifndef VERSION
  !define VERSION "0.4.0"
!endif
!ifndef PAYLOAD
  !define PAYLOAD "..\dist\portable"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\dist\CADPreview-${VERSION}-x64-Setup.exe"
!endif
!define THUMB "{70D346A5-7204-45EE-A827-7501B84494E1}"
!define PREVIEW "{62564304-3EA6-4B9E-970E-452A8ACBC5B3}"
!define APPID "{268DC765-AD72-4A8E-A834-7BD4505A6F78}"
!define THUMBKEY "{e357fccd-a995-4576-b01f-234630154e96}"
!define PREVIEWKEY "{8895b1c6-b41f-4c1c-a562-0d564250836f}"
Name "CAD Preview for Windows ${VERSION}"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "CAD Preview for Windows"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "CAD Preview for Windows x64 installer"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\CAD Preview for Windows"
InstallDirRegKey HKLM "Software\CADPreview" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Russian"

Function .onInit
 ${IfNot} ${AtLeastWin10}
   MessageBox MB_ICONSTOP "Windows 10 or Windows 11 is required."
   Abort
 ${EndIf}
 ${IfNot} ${RunningX64}
   MessageBox MB_ICONSTOP "Windows x64 is required."
   Abort
 ${EndIf}
 SetRegView 64
 SetShellVarContext all
FunctionEnd
Function un.onInit
 SetRegView 64
 SetShellVarContext all
FunctionEnd

!macro Associate EXT KIND CLSID DESCRIPTION
 ReadRegStr $0 HKCR "${EXT}\shellex\${KIND}" ""
 ${If} $0 != ""
 ${AndIf} $0 != "${CLSID}"
   ReadRegStr $2 HKCR "CLSID\$0" ""
   ${If} $2 == ""
     StrCpy $2 "Other application / Другая программа"
   ${EndIf}
   MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "${DESCRIPTION}: ${EXT}$\r$\n$\r$\nCurrent / Сейчас: $2$\r$\n$\r$\nЗаменить на CAD Preview? «Да» включает наш просмотр для этого формата. «Нет» сохраняет прежний обработчик.$\r$\nПриложение для открытия файлов не изменится. Прежняя машинная настройка сохраняется для удаления программы.$\r$\n$\r$\nReplace with CAD Preview? Yes enables our handler; No keeps the existing one. Your default opening application stays unchanged." IDYES +2
   Goto skip_${EXT}_${KIND}
 ${EndIf}
 ReadRegStr $1 HKLM "Software\CADPreview\Backup\${EXT}\${KIND}" "Saved"
 ${If} $1 != "1"
   ClearErrors
   ReadRegStr $0 HKLM "Software\Classes\${EXT}\shellex\${KIND}" ""
   ${If} ${Errors}
     WriteRegDWORD HKLM "Software\CADPreview\Backup\${EXT}\${KIND}" "Present" 0
   ${Else}
     WriteRegDWORD HKLM "Software\CADPreview\Backup\${EXT}\${KIND}" "Present" 1
     WriteRegStr HKLM "Software\CADPreview\Backup\${EXT}\${KIND}" "Value" "$0"
   ${EndIf}
   WriteRegStr HKLM "Software\CADPreview\Backup\${EXT}\${KIND}" "Saved" "1"
 ${EndIf}
 WriteRegStr HKLM "Software\Classes\${EXT}\shellex\${KIND}" "" "${CLSID}"
 skip_${EXT}_${KIND}:
!macroend
!macro Restore EXT KIND CLSID
 ReadRegStr $0 HKLM "Software\Classes\${EXT}\shellex\${KIND}" ""
 ${If} $0 == "${CLSID}"
   ReadRegDWORD $1 HKLM "Software\CADPreview\Backup\${EXT}\${KIND}" "Present"
   ${If} $1 == 1
     ReadRegStr $0 HKLM "Software\CADPreview\Backup\${EXT}\${KIND}" "Value"
     WriteRegStr HKLM "Software\Classes\${EXT}\shellex\${KIND}" "" "$0"
   ${Else}
     DeleteRegValue HKLM "Software\Classes\${EXT}\shellex\${KIND}" ""
     DeleteRegKey /ifempty HKLM "Software\Classes\${EXT}\shellex\${KIND}"
   ${EndIf}
 ${EndIf}
!macroend
Section "Viewer and shared components (required)" Core
 SectionIn RO
 SetOutPath "$INSTDIR"
 File /r "${PAYLOAD}\*.*"
 WriteUninstaller "$INSTDIR\Uninstall.exe"
 WriteRegStr HKLM "Software\CADPreview" "InstallDir" "$INSTDIR"
 WriteRegStr HKLM "Software\Classes\CLSID\${THUMB}" "" "CAD Preview Thumbnail Provider"
 WriteRegStr HKLM "Software\Classes\CLSID\${THUMB}\InprocServer32" "" "$INSTDIR\CADPreview.Shell.dll"
 WriteRegStr HKLM "Software\Classes\CLSID\${THUMB}\InprocServer32" "ThreadingModel" "Apartment"
 WriteRegStr HKLM "Software\Classes\CLSID\${PREVIEW}" "" "CAD Preview Handler"
 WriteRegStr HKLM "Software\Classes\CLSID\${PREVIEW}" "AppID" "${APPID}"
 WriteRegStr HKLM "Software\Classes\CLSID\${PREVIEW}\InprocServer32" "" "$INSTDIR\CADPreview.Shell.dll"
 WriteRegStr HKLM "Software\Classes\CLSID\${PREVIEW}\InprocServer32" "ThreadingModel" "Apartment"
 WriteRegStr HKLM "Software\Classes\AppID\${APPID}" "DllSurrogate" "Prevhost.exe"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\PreviewHandlers" "${PREVIEW}" "CAD Preview for Windows"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved" "${THUMB}" "CAD Preview Thumbnail Provider"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved" "${PREVIEW}" "CAD Preview Handler"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CADPreview" "DisplayName" "CAD Preview for Windows"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CADPreview" "DisplayVersion" "${VERSION}"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CADPreview" "UninstallString" '"$INSTDIR\Uninstall.exe"'
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CADPreview" "InstallLocation" "$INSTDIR"
 WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CADPreview" "NoModify" 1
 WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CADPreview" "NoRepair" 1
 CreateShortcut "$SMPROGRAMS\CAD Preview for Windows.lnk" "$INSTDIR\CADPreview.Viewer.exe"
SectionEnd
Section "Explorer thumbnails" Thumbnails
 !insertmacro Associate ".dxf" "${THUMBKEY}" "${THUMB}" "Миниатюры / Thumbnails"
 !insertmacro Associate ".stl" "${THUMBKEY}" "${THUMB}" "Миниатюры / Thumbnails"
 !insertmacro Associate ".step" "${THUMBKEY}" "${THUMB}" "Миниатюры / Thumbnails"
 !insertmacro Associate ".stp" "${THUMBKEY}" "${THUMB}" "Миниатюры / Thumbnails"
SectionEnd
Section "Explorer preview pane (Alt+P)" PreviewPane
 !insertmacro Associate ".dxf" "${PREVIEWKEY}" "${PREVIEW}" "Правая панель / Preview pane"
 !insertmacro Associate ".stl" "${PREVIEWKEY}" "${PREVIEW}" "Правая панель / Preview pane"
 !insertmacro Associate ".step" "${PREVIEWKEY}" "${PREVIEW}" "Правая панель / Preview pane"
 !insertmacro Associate ".stp" "${PREVIEWKEY}" "${PREVIEW}" "Правая панель / Preview pane"
SectionEnd
Function .onInstSuccess
 System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
 MessageBox MB_OK "CAD Preview for Windows installed. Enable the preview pane with Alt+P and use large icons for thumbnails. If Explorer retains an old handler, close and reopen its windows or sign out when convenient. No processes were terminated."
FunctionEnd
Section "Uninstall"
 !insertmacro Restore ".dxf" "${THUMBKEY}" "${THUMB}"
 !insertmacro Restore ".stl" "${THUMBKEY}" "${THUMB}"
 !insertmacro Restore ".step" "${THUMBKEY}" "${THUMB}"
 !insertmacro Restore ".stp" "${THUMBKEY}" "${THUMB}"
 !insertmacro Restore ".dxf" "${PREVIEWKEY}" "${PREVIEW}"
 !insertmacro Restore ".stl" "${PREVIEWKEY}" "${PREVIEW}"
 !insertmacro Restore ".step" "${PREVIEWKEY}" "${PREVIEW}"
 !insertmacro Restore ".stp" "${PREVIEWKEY}" "${PREVIEW}"
 DeleteRegKey HKLM "Software\Classes\CLSID\${THUMB}"
 DeleteRegKey HKLM "Software\Classes\CLSID\${PREVIEW}"
 DeleteRegKey HKLM "Software\Classes\AppID\${APPID}"
 DeleteRegValue HKLM "Software\Microsoft\Windows\CurrentVersion\PreviewHandlers" "${PREVIEW}"
 DeleteRegValue HKLM "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved" "${PREVIEW}"
 DeleteRegValue HKLM "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved" "${THUMB}"
 DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\CADPreview"
 DeleteRegKey HKLM "Software\CADPreview"
 Delete "$SMPROGRAMS\CAD Preview for Windows.lnk"
 ; Delete only installed product names. Never recursively delete the chosen directory.
 Delete /REBOOTOK "$INSTDIR\Uninstall.exe"
 ; License and resource files are enumerated by the packaging script below.
 !include "uninstall-files.nsh"
 RMDir "$INSTDIR"
 System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd


