@echo off
rem ============================================================
rem  Build_Install.cmd - installers + portable EXEs for x64 AND ARM64
rem  Requires Inno Setup 6 (https://jrsoftware.org/isdl.php).
rem  Output (dist\):
rem    M2_APEX-<version>-win-x64.exe          (portable)
rem    M2_APEX-<version>-win-arm64.exe        (portable)
rem    M2_APEX-<version>-Setup-x64.exe        (installer)
rem    M2_APEX-<version>-Setup-arm64.exe      (installer)
rem ============================================================
setlocal EnableExtensions
cd /d "%~dp0"
echo %cmdcmdline%| find /i "%~nx0" >nul && set "DBLCLICK=1"

rem --- ensure a usable .NET SDK is available (auto-installs if missing) ---
call "%~dp0_ensure_dotnet.cmd"
if errorlevel 1 (
  if defined DBLCLICK pause
  exit /b 1
)

set "PROJECT=M2_APEX.csproj"
set "ISS=installer\M2_APEX.iss"

rem --- locate the Inno Setup compiler ---
set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo [ERROR] Inno Setup 6 not found ^(ISCC.exe^).
  echo         Install it from https://jrsoftware.org/isdl.php
  if defined DBLCLICK pause
  exit /b 1
)

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "[regex]::Match((Get-Content '%PROJECT%' -Raw),'<Version>(.*?)</Version>').Groups[1].Value"`) do set "VER=%%v"

if not exist "dist" mkdir "dist"

for %%R in (win-x64 win-arm64) do (
  call :build %%R || (
    echo.
    echo [ERROR] Failed on %%R.
    if defined DBLCLICK pause
    exit /b 1
  )
)

echo.
echo === Output (dist) ===
dir /b "dist\M2_APEX-%VER%-*.exe"
if defined DBLCLICK pause
exit /b 0

:build
set "RID=%1"
set "ARCH=%RID:win-=%"

echo.
echo === Publishing %RID%  (v%VER%) ===
dotnet publish "%PROJECT%" -c Release -r %RID% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "publish\%RID%"
if errorlevel 1 exit /b 1
copy /y "publish\%RID%\M2_APEX.exe" "dist\M2_APEX-%VER%-%RID%.exe" >nul

echo === Building installer (%ARCH%) ===
"%ISCC%" "/DAppVersion=%VER%" "/DArch=%ARCH%" "/DSourceExe=%~dp0publish\%RID%\M2_APEX.exe" "/DOutDir=%~dp0dist" "%ISS%"
if errorlevel 1 exit /b 1
exit /b 0
