@echo off
rem ============================================================
rem  Build_X64.cmd - portable, self-contained single-file EXE (x64)
rem  Output: dist\M2_APEX-<version>-win-x64.exe
rem ============================================================
setlocal EnableExtensions
cd /d "%~dp0"
echo %cmdcmdline%| find /i "%~nx0" >nul && set "DBLCLICK=1"

set "PROJECT=M2_APEX.csproj"
set "RID=win-x64"

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "[regex]::Match((Get-Content '%PROJECT%' -Raw),'<Version>(.*?)</Version>').Groups[1].Value"`) do set "VER=%%v"

echo === Building portable %RID%  (v%VER%) ===
dotnet publish "%PROJECT%" -c Release -r %RID% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "publish\%RID%"
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed.
  if defined DBLCLICK pause
  exit /b 1
)

if not exist "dist" mkdir "dist"
copy /y "publish\%RID%\M2_APEX.exe" "dist\M2_APEX-%VER%-%RID%.exe" >nul
echo.
echo Done: dist\M2_APEX-%VER%-%RID%.exe
if defined DBLCLICK pause
exit /b 0
