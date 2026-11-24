@echo off
setlocal
set "ROOT=%~dp0"
if "%ROOT:~0,4%"=="\\?\" set "ROOT=%ROOT:~4%"
cd /d "%ROOT%" || exit /b 1
set "ROOT="

rem ============= release target: edit these lines only =============
set "PUB_HOST=root@111.230.247.111"
set "PUB_DIR=/usr/share/nginx/html/shiying"
set "GIT_REMOTE=origin"
set "GIT_BRANCH=master"
set "PUB_HOSTNAME=%PUB_HOST:*@=%"
set "PUB_BASEURL=http://%PUB_HOSTNAME%/shiying"
rem ==========================================================================

set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%DOTNET%" (
  echo !! .NET SDK not found: %DOTNET%
  exit /b 1
)
if not exist "%ISCC%" (
  echo !! Inno Setup 6 not found ^(ISCC.exe^)
  exit /b 1
)

echo [1/6] bump version
set "NEWVER="
for /f "delims=" %%v in ('powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bump-version.ps1"') do set "NEWVER=%%v"
if not defined NEWVER (
  echo !! bump version failed
  exit /b 1
)
echo     version =^> %NEWVER%

echo [2/6] fetch models
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\fetch-models.ps1"
if errorlevel 1 goto :fail

echo [3/6] dotnet publish
if exist "publish\win-x64" rmdir /s /q "publish\win-x64"
"%DOTNET%" publish "Shiying.csproj" -c Release -o "publish\win-x64"
if errorlevel 1 goto :fail
>"publish\win-x64\version.txt" echo %NEWVER%

echo [4/6] build installer
if exist "dist\*.exe" del /q "dist\*.exe"
if exist "dist\latest.json" del /q "dist\latest.json"
"%ISCC%" /DAppVersion=%NEWVER% "installer\shiying.iss"
if errorlevel 1 goto :fail

echo      write latest.json
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\make-latest.ps1" -Version %NEWVER% -DistDir "dist" -BaseUrl "%PUB_BASEURL%"
if errorlevel 1 goto :fail

echo [5/6] git commit + push
set "GIT_TERMINAL_PROMPT=0"
git add -A
git commit -m "release: v%NEWVER%"
if errorlevel 1 echo     nothing to commit
git push %GIT_REMOTE% %GIT_BRANCH%
if errorlevel 1 echo !! git push FAILED - please push manually
set "COMMITTED=1"

echo [6/6] upload to %PUB_HOST%
ssh -n -o BatchMode=yes -o StrictHostKeyChecking=accept-new %PUB_HOST% "mkdir -p %PUB_DIR%"
if errorlevel 1 goto :fail
set "UPFAIL="
for %%f in ("dist\*.exe") do (
  echo     upload %%~nxf
  scp -o BatchMode=yes -o StrictHostKeyChecking=accept-new "%%~ff" %PUB_HOST%:%PUB_DIR%/
  if errorlevel 1 set "UPFAIL=1"
)
echo     upload latest.json
scp -o BatchMode=yes -o StrictHostKeyChecking=accept-new "dist\latest.json" %PUB_HOST%:%PUB_DIR%/
if errorlevel 1 set "UPFAIL=1"
if defined UPFAIL (
  echo !! upload FAILED
  goto :fail
)
ssh -n -o BatchMode=yes %PUB_HOST% "ls -lh %PUB_DIR%"
if errorlevel 1 goto :fail

del /q "%TEMP%\Shiying.csproj.releasebak" 2>nul
echo.
echo DONE - Shiying v%NEWVER% published
exit /b 0

:fail
echo.
if defined COMMITTED (
  echo !! FAILED after commit - Shiying.csproj stays at v%NEWVER%
) else (
  echo !! FAILED - rolling back version
  if exist "%TEMP%\Shiying.csproj.releasebak" copy /y "%TEMP%\Shiying.csproj.releasebak" "Shiying.csproj" >nul
)
exit /b 1
