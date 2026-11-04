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
rem Inter is embedded in the executable, so nothing has to be installed on the user's PC.
set "FONTS=/resource:fonts\Inter-Regular.ttf,Rooznegar.Fonts.Inter-Regular.ttf /resource:fonts\Inter-Medium.ttf,Rooznegar.Fonts.Inter-Medium.ttf /resource:fonts\Inter-Bold.ttf,Rooznegar.Fonts.Inter-Bold.ttf /resource:fonts\Inter-ExtraBold.ttf,Rooznegar.Fonts.Inter-ExtraBold.ttf"
"%CSC_PATH%" /nologo /target:winexe /platform:anycpu /optimize+ /win32icon:build\Rooznegar.ico /win32manifest:src\app.manifest /out:build\Rooznegar.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll %FONTS% src\*.cs
if errorlevel 1 exit /b 1
"%CSC_PATH%" /nologo /target:exe /out:build\Tests.exe /reference:System.Web.Extensions.dll src\Core.cs tests\Tests.cs
if errorlevel 1 exit /b 1
build\Tests.exe
if errorlevel 1 exit /b 1
copy /y fonts\Inter-OFL.txt build\Inter-OFL.txt >nul
if not exist ..\deliverables mkdir ..\deliverables
where makensis >nul 2>nul
if errorlevel 1 (
  echo App built successfully: build\Rooznegar.exe
  echo Install NSIS and run "makensis installer.nsi" to create the Setup.
  exit /b 0
)
makensis installer.nsi
