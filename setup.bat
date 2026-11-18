@echo off
setlocal
cd /d "%~dp0"

set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%DOTNET%" (
  echo 未找到 .NET SDK。请先安装 Microsoft .NET SDK 10。
  exit /b 1
)
if not exist "%ISCC%" (
  echo 未找到 Inno Setup 6 的 ISCC.exe。
  exit /b 1
)

"%DOTNET%" publish "%~dp0Jietu.csproj" -c Release -o "%~dp0publish\win-x64"
if errorlevel 1 exit /b 1

"%ISCC%" "%~dp0installer\jietu.iss"
if errorlevel 1 exit /b 1

echo.
echo 安装包: %~dp0dist\截长图安装包.exe
endlocal
