Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
Name "Rooznegar - روزنگار"
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
!define MUI_WELCOMEPAGE_TITLE "به روزنگار خوش آمدید"
!define MUI_WELCOMEPAGE_TEXT "ثبت زمان پروژه‌ها با تاریخ شمسی، پوستهٔ روشن و تیره، گزارش‌های تصویری و تایمر شناور.$\r$\n$\r$\nنصب برای همین کاربر انجام می‌شود و به دسترسی مدیر نیاز ندارد.$\r$\n$\r$\nپروژه‌ها و زمان‌های شما هنگام ارتقا حفظ می‌شوند."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\Rooznegar.exe"
!define MUI_FINISHPAGE_RUN_TEXT "اجرای روزنگار"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Farsi"

Function .onInit
  ReadEnvStr $1 "USERNAME"
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Rooznegar-1-$1") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONINFORMATION "روزنگار در حال اجراست. از منوی کنار ساعت ویندوز، «خروج کامل» را بزنید و نصب را دوباره اجرا کنید."
    Abort
  ${EndIf}
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 461808
    MessageBox MB_OK|MB_ICONINFORMATION "این برنامه به .NET Framework 4.7.2 یا جدیدتر نیاز دارد. در ویندوز ۱۰ به‌روز و ویندوز ۱۱ معمولاً موجود است. راهنمای نصب: https://dotnet.microsoft.com/download/dotnet-framework"
    Abort
  ${EndIf}
FunctionEnd

Section "Rooznegar" SEC_MAIN
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File "build/Rooznegar.exe"
  File "build/Rooznegar.exe.config"
  File "build/Rooznegar.ico"
  File "build/Guide-FA.html"
  File "build/Vazirmatn-OFL.txt"
  SetOutPath "$INSTDIR\fonts"
  File "fonts/Vazirmatn-Regular.ttf"
  File "fonts/Vazirmatn-Medium.ttf"
  File "fonts/Vazirmatn-Bold.ttf"
  File "fonts/Vazirmatn-ExtraBold.ttf"
  SetOutPath "$INSTDIR"
  CreateDirectory "$SMPROGRAMS\Rooznegar"
  CreateShortcut "$SMPROGRAMS\Rooznegar\روزنگار.lnk" "$INSTDIR\Rooznegar.exe" "" "$INSTDIR\Rooznegar.ico"
  CreateShortcut "$SMPROGRAMS\Rooznegar\راهنمای استفاده.lnk" "$INSTDIR\Guide-FA.html"
  CreateShortcut "$DESKTOP\روزنگار.lnk" "$INSTDIR\Rooznegar.exe" "" "$INSTDIR\Rooznegar.ico"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Rooznegar" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "DisplayName" "Rooznegar - روزنگار"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "DisplayVersion" "2.0.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "Publisher" "Rooznegar"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "DisplayIcon" "$INSTDIR\Rooznegar.ico"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "NoRepair" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar" "EstimatedSize" 1400
SectionEnd

Function un.onInit
  ReadEnvStr $1 "USERNAME"
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Rooznegar-1-$1") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONINFORMATION "ابتدا از منوی روزنگار کنار ساعت ویندوز، «خروج کامل» را انتخاب کنید."
    Abort
  ${EndIf}
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$INSTDIR\Rooznegar.exe"
  Delete "$INSTDIR\Rooznegar.exe.config"
  Delete "$INSTDIR\Rooznegar.ico"
  Delete "$INSTDIR\Guide-FA.html"
  Delete "$INSTDIR\Vazirmatn-OFL.txt"
  Delete "$INSTDIR\fonts\Vazirmatn-Regular.ttf"
  Delete "$INSTDIR\fonts\Vazirmatn-Medium.ttf"
  Delete "$INSTDIR\fonts\Vazirmatn-Bold.ttf"
  Delete "$INSTDIR\fonts\Vazirmatn-ExtraBold.ttf"
  RMDir "$INSTDIR\fonts"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$DESKTOP\روزنگار.lnk"
  Delete "$SMPROGRAMS\Rooznegar\روزنگار.lnk"
  Delete "$SMPROGRAMS\Rooznegar\راهنمای استفاده.lnk"
  RMDir "$SMPROGRAMS\Rooznegar"
  RMDir "$INSTDIR"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Rooznegar"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Rooznegar"
  DeleteRegKey HKCU "Software\Rooznegar"
  ; User timesheets in LocalAppData\Rooznegar are deliberately retained.
SectionEnd
