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
  call :say "672A 627E 5230 0020 0052 006F 0073 006C 0079 006E 0020 7684 0020 0063 0073 0063 002E 0065 0078 0065 3002 8BF7 5B89 88C5 0020 0056 0069 0073 0075 0061 006C 0020 0053 0074 0075 0064 0069 006F 0020 0032 0030 0032 0032 0020 6216 201C 4F7F 7528 0020 0043 002B 002B 0020 7684 684C 9762 5F00 53D1 0020 002F 0020 004D 0053 0042 0075 0069 006C 0064 201D 751F 6210 5DE5 5177 3002"
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
  src\PaddleOcr.cs ^
  src\LlmTranslator.cs ^
  src\ModelForm.cs ^
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
call :say "7F16 8BD1 6210 529F 003A 0020" "%cd%\bin\Jietu.exe"
if "%1"=="--test" (
  bin\Jietu.exe --selftest
  if errorlevel 1 exit /b 1
)
endlocal
exit /b 0

rem Print Unicode from code points. The bat stays ASCII, so a cp936 console shows Chinese correctly.
:say
setlocal
set "JIETU_SAY_PRE=%~1"
set "JIETU_SAY_MID=%~2"
set "JIETU_SAY_SUF=%~3"
powershell -NoProfile -Command "$p=''; foreach($c in $env:JIETU_SAY_PRE.Split(' ')){ if($c){ $p += [char]([Convert]::ToInt32($c,16)) } }; $s=''; if($env:JIETU_SAY_SUF){ foreach($c in $env:JIETU_SAY_SUF.Split(' ')){ if($c){ $s += [char]([Convert]::ToInt32($c,16)) } } }; Write-Host ($p + $env:JIETU_SAY_MID + $s)"
endlocal
exit /b 0
