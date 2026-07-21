@echo off
setlocal EnableExtensions

rem ============================================================
rem  M2_APEX - build script   ->   dist\M2_APEX.exe
rem
rem  Usage:
rem    BUILD.CMD            Self-contained single-file EXE for this PC
rem                         (portable; runs without installing .NET).
rem    BUILD.CMD fdd        Framework-dependent EXE (small; needs the
rem                         .NET 9 Desktop Runtime installed to run).
rem    BUILD.CMD win-arm64  Target a specific RID (win-x64 / win-arm64 / win-x86).
rem                         Args may be combined, e.g.  BUILD.CMD fdd win-x64
rem ============================================================

cd /d "%~dp0"

set "PROJECT=M2_APEX.csproj"
set "CONFIG=Release"
set "OUTDIR=%~dp0dist"
set "SELFCONTAINED=true"

rem --- default RID from this machine's CPU architecture ---
set "RID=win-x64"
if /I "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "RID=win-arm64"
if /I "%PROCESSOR_ARCHITECTURE%"=="x86"   set "RID=win-x86"

rem --- optional args (order independent): [fdd|self] and/or a RID ---
for %%A in (%*) do (
  if /I "%%~A"=="fdd" (
    set "SELFCONTAINED=false"
  ) else if /I "%%~A"=="self" (
    set "SELFCONTAINED=true"
  ) else (
    set "RID=%%~A"
  )
)

set "EXTRA="
if /I "%SELFCONTAINED%"=="true" set "EXTRA=-p:EnableCompressionInSingleFile=true"

rem --- keep the window open when launched by double-click ---
set "DBLCLICK="
echo(%cmdcmdline%| find /i "%~nx0" >nul 2>&1 && set "DBLCLICK=1"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] The .NET SDK ^(dotnet^) was not found on PATH.
  echo         Install the .NET 9 SDK: https://dotnet.microsoft.com/download
  if defined DBLCLICK pause
  exit /b 1
)

echo.
echo === Building M2_APEX =========================================
echo   Configuration : %CONFIG%
echo   Runtime ^(RID^) : %RID%
echo   Self-contained: %SELFCONTAINED%
echo   Output folder : %OUTDIR%
echo ==============================================================
echo.

if exist "%OUTDIR%" rd /s /q "%OUTDIR%" 2>nul

dotnet publish "%PROJECT%" -c %CONFIG% -r %RID% --self-contained %SELFCONTAINED% -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true %EXTRA% -o "%OUTDIR%"
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed. See the messages above.
  if defined DBLCLICK pause
  exit /b 1
)

echo.
echo === Build succeeded ==========================================
for %%I in ("%OUTDIR%\M2_APEX.exe") do echo   EXE : %%~fI  ^(%%~zI bytes^)
echo   Run it by double-clicking M2_APEX.exe in the dist folder.
echo ==============================================================
if defined DBLCLICK pause
exit /b 0
