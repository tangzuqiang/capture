@echo off
setlocal
cd /d "%~dp0"

set "CSC="
if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe" (
  set "CSC=%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
)
if not defined CSC if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe" (
  set "CSC=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
)
if not defined CSC (
  echo 未找到 Roslyn 的 csc.exe。请安装 Visual Studio 2022 或“使用 C++ 的桌面开发 / MSBuild”生成工具。
  exit /b 1
)

set "FW=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319"
if not exist bin mkdir bin

"%CSC%" /nologo /t:winexe /platform:x64 /optimize+ /unsafe /langversion:latest /codepage:65001 /win32manifest:app.manifest /out:bin\Jietu.exe ^
  /r:"%FW%\System.dll" ^
  /r:"%FW%\System.Core.dll" ^
  /r:"%FW%\System.Drawing.dll" ^
  /r:"%FW%\System.Windows.Forms.dll" ^
  src\Program.cs ^
  src\NativeMethods.cs ^
  src\AppSettings.cs ^
  src\Theme.cs ^
  src\Markup.cs ^
  src\OcrText.cs ^
  src\OcrForm.cs ^
  src\ScreenGrab.cs ^
  src\ImageStitcher.cs ^
  src\ScrollCapture.cs ^
  src\MainForm.cs ^
  src\OverlayForm.cs ^
  src\ToolbarForm.cs ^
  src\ColorPanel.cs ^
  src\HintForm.cs ^
  src\ResultForm.cs ^
  src\SelfTest.cs ^
  src\UiTest.cs

if errorlevel 1 exit /b 1
echo.
echo 编译成功: %cd%\bin\Jietu.exe
if "%1"=="--test" (
  bin\Jietu.exe --selftest
  if errorlevel 1 exit /b 1
)
endlocal
