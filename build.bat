@echo off
setlocal
set "ROOT=%~dp0"
if "%ROOT:~0,4%"=="\\?\" set "ROOT=%ROOT:~4%"
cd /d "%ROOT%" || exit /b 1
set "ROOT="

rem Development build: writes bin\Shiying.exe. Pass --test to run self-tests.
rem Voice dependencies require the dotnet SDK; release publishing uses setup.bat.

set "DOTNET="
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles(x86)%\dotnet\dotnet.exe"
if not defined DOTNET for /f "delims=" %%d in ('where dotnet 2^>nul') do if not defined DOTNET set "DOTNET=%%d"
if not defined DOTNET (
  call :say "672A 627E 5230 0020 0064 006F 0074 006E 0065 0074 3002 8BF7 5B89 88C5 0020 002E 004E 0045 0054 0020 0031 0030 0020 0053 0044 004B 0020 0028 0064 006F 0074 006E 0065 0074 002E 006D 0069 0063 0072 006F 0073 006F 0066 0074 002E 0063 006F 006D 002F 0064 006F 0077 006E 006C 006F 0061 0064 0029 3002"
  exit /b 1
)

if exist bin rmdir /s /q bin
"%DOTNET%" publish "Shiying.csproj" -c Release -o "bin"
if errorlevel 1 exit /b 1

rem Read the current version without incrementing it.
set "VER="
for /f "delims=" %%v in ('powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bump-version.ps1" -Print') do set "VER=%%v"
if defined VER >"bin\version.txt" echo %VER%

echo.
call :say "7F16 8BD1 6210 529F 003A 0020" "%cd%\bin\Shiying.exe"
call :say "8BED 97F3 52A9 624B 9700 8981 6A21 578B 003A 0020 6258 76D8 83DC 5355 300C 4E0B 8F7D 8BED 97F3 6A21 578B 300D"
if "%1"=="--test" (
  bin\Shiying.exe --selftest
  if errorlevel 1 exit /b 1
)
endlocal
exit /b 0

rem Print Unicode from code points. The bat stays ASCII, so a cp936 console shows Chinese correctly.
:say
setlocal
set "SHIYING_SAY_PRE=%~1"
set "SHIYING_SAY_MID=%~2"
set "SHIYING_SAY_SUF=%~3"
powershell -NoProfile -Command "$p=''; foreach($c in $env:SHIYING_SAY_PRE.Split(' ')){ if($c){ $p += [char]([Convert]::ToInt32($c,16)) } }; $s=''; if($env:SHIYING_SAY_SUF){ foreach($c in $env:SHIYING_SAY_SUF.Split(' ')){ if($c){ $s += [char]([Convert]::ToInt32($c,16)) } } }; Write-Host ($p + $env:SHIYING_SAY_MID + $s)"
endlocal
exit /b 0
