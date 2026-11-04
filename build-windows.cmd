@echo off
setlocal
cd /d "%~dp0"
if not exist build mkdir build
set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC_PATH%" set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC_PATH%" (
  echo .NET Framework C# compiler was not found.
  exit /b 1
)
rem Vazirmatn is embedded in the executable, so nothing has to be installed on the user's PC.
set "FONTS=/resource:fonts\Vazirmatn-Regular.ttf,Rooznegar.Fonts.Vazirmatn-Regular.ttf /resource:fonts\Vazirmatn-Medium.ttf,Rooznegar.Fonts.Vazirmatn-Medium.ttf /resource:fonts\Vazirmatn-Bold.ttf,Rooznegar.Fonts.Vazirmatn-Bold.ttf /resource:fonts\Vazirmatn-ExtraBold.ttf,Rooznegar.Fonts.Vazirmatn-ExtraBold.ttf"
"%CSC_PATH%" /nologo /target:winexe /platform:anycpu /optimize+ /win32icon:build\Rooznegar.ico /win32manifest:src\app.manifest /out:build\Rooznegar.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll %FONTS% src\*.cs
if errorlevel 1 exit /b 1
"%CSC_PATH%" /nologo /target:exe /out:build\Tests.exe /reference:System.Web.Extensions.dll src\Core.cs tests\Tests.cs
if errorlevel 1 exit /b 1
build\Tests.exe
if errorlevel 1 exit /b 1
copy /y fonts\OFL.txt build\Vazirmatn-OFL.txt >nul
if not exist ..\deliverables mkdir ..\deliverables
where makensis >nul 2>nul
if errorlevel 1 (
  echo App built successfully: build\Rooznegar.exe
  echo Install NSIS and run "makensis installer.nsi" to create the Setup.
  exit /b 0
)
makensis installer.nsi
