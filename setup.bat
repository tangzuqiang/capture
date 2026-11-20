@echo off
setlocal
cd /d "%~dp0"

set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%DOTNET%" (
  call :say "672A 627E 5230 0020 002E 004E 0045 0054 0020 0053 0044 004B 3002 8BF7 5148 5B89 88C5 0020 004D 0069 0063 0072 006F 0073 006F 0066 0074 0020 002E 004E 0045 0054 0020 0053 0044 004B 0020 0031 0030 3002"
  exit /b 1
)
if not exist "%ISCC%" (
  call :say "672A 627E 5230 0020 0049 006E 006E 006F 0020 0053 0065 0074 0075 0070 0020 0036 0020 7684 0020 0049 0053 0043 0043 002E 0065 0078 0065 3002"
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\fetch-models.ps1"
if errorlevel 1 exit /b 1

if exist "%~dp0publish\win-x64" rmdir /s /q "%~dp0publish\win-x64"
"%DOTNET%" publish "%~dp0Jietu.csproj" -c Release -o "%~dp0publish\win-x64"
if errorlevel 1 exit /b 1

"%ISCC%" "%~dp0installer\jietu.iss"
if errorlevel 1 exit /b 1

echo.
call :say "5B89 88C5 5305 003A 0020" "%~dp0dist\" "622A 957F 56FE 5B89 88C5 5305 002E 0065 0078 0065"
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
