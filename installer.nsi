Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
Name "Rooznegar"
OutFile "../deliverables/Rooznegar-Setup-2.0.0.exe"
InstallDir "$LOCALAPPDATA\Programs\Rooznegar"
InstallDirRegKey HKCU "Software\Rooznegar" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "Rooznegar 2.0.0"
VIProductVersion "2.0.0.0"
VIAddVersionKey "ProductName" "Rooznegar"
VIAddVersionKey "FileDescription" "Rooznegar Windows Setup"
VIAddVersionKey "FileVersion" "2.0.0"
VIAddVersionKey "ProductVersion" "2.0.0"
VIAddVersionKey "LegalCopyright" "Rooznegar"
!define MUI_ICON "build/Rooznegar.ico"
!define MUI_UNICON "build/Rooznegar.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Welcome to Rooznegar"
!define MUI_WELCOMEPAGE_TEXT "Project time tracking with light and dark themes, visual reports and a floating timer.$\r$\n$\r$\nRooznegar installs for the current user only and does not need administrator rights.$\r$\n$\r$\nYour projects and times are kept when you upgrade."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\Rooznegar.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Run Rooznegar"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ReadEnvStr $1 "USERNAME"
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Rooznegar-1-$1") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONINFORMATION "Rooznegar is running. Choose Quit from its menu next to the Windows clock, then run the setup again."
    Abort
  ${EndIf}
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 461808
    MessageBox MB_OK|MB_ICONINFORMATION "Rooznegar needs .NET Framework 4.7.2 or later. It is usually already present on up-to-date Windows 10 and Windows 11. Download: https://dotnet.microsoft.com/download/dotnet-framework"
    Abort
  ${EndIf}
FunctionEnd

Section "Rooznegar" SEC_MAIN
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File "build/Rooznegar.exe"
  File "build/Rooznegar.exe.config"
  File "build/Rooznegar.ico"
  File "build/Guide.html"
  File "build/Inter-OFL.txt"
  SetOutPath "$INSTDIR\fonts"
  File "fonts/Inter-Regular.ttf"
  File "fonts/Inter-Medium.ttf"
  File "fonts/Inter-Bold.ttf"
  File "fonts/Inter-ExtraBold.ttf"
  SetOutPath "$INSTDIR"
  CreateDirectory "$SMPROGRAMS\Rooznegar"
  CreateShortcut "$SMPROGRAMS\Rooznegar\Rooznegar.lnk" "$INSTDIR\Rooznegar.exe" "" "$INSTDIR\Rooznegar.ico"
  CreateShortcut "$SMPROGRAMS\Rooznegar\Rooznegar Guide.lnk" "$INSTDIR\Guide.html"
  CreateShortcut "$DESKTOP\Rooznegar.lnk" "$INSTDIR\Rooznegar.exe" "" "$INSTDIR\Rooznegar.ico"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Rooznegar" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "DisplayName" "Rooznegar"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "DisplayVersion" "2.0.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "Publisher" "Rooznegar"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "DisplayIcon" "$INSTDIR\Rooznegar.ico"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "NoRepair" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "EstimatedSize" 2600
SectionEnd

Function un.onInit
  ReadEnvStr $1 "USERNAME"
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Rooznegar-1-$1") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONINFORMATION "First choose Quit from the Rooznegar menu next to the Windows clock."
    Abort
  ${EndIf}
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$INSTDIR\Rooznegar.exe"
  Delete "$INSTDIR\Rooznegar.exe.config"
  Delete "$INSTDIR\Rooznegar.ico"
  Delete "$INSTDIR\Guide.html"
  Delete "$INSTDIR\Inter-OFL.txt"
  Delete "$INSTDIR\fonts\Inter-Regular.ttf"
  Delete "$INSTDIR\fonts\Inter-Medium.ttf"
  Delete "$INSTDIR\fonts\Inter-Bold.ttf"
  Delete "$INSTDIR\fonts\Inter-ExtraBold.ttf"
  RMDir "$INSTDIR\fonts"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$DESKTOP\Rooznegar.lnk"
  Delete "$SMPROGRAMS\Rooznegar\Rooznegar.lnk"
  Delete "$SMPROGRAMS\Rooznegar\Rooznegar Guide.lnk"
  RMDir "$SMPROGRAMS\Rooznegar"
  RMDir "$INSTDIR"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Rooznegar"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar"
  DeleteRegKey HKCU "Software\Rooznegar"
  ; User timesheets in LocalAppData\Rooznegar are deliberately retained.
SectionEnd
