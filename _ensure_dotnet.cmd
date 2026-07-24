@echo off
rem ============================================================
rem  _ensure_dotnet.cmd - make sure a usable .NET SDK is on PATH.
rem  If none is found, downloads Microsoft's official installer
rem  (https://dot.net/v1/dotnet-install.ps1) and installs the
rem  matching SDK per-user into %LOCALAPPDATA%\Microsoft\dotnet
rem  (no admin required). Called by the Build_*.cmd scripts.
rem  Returns errorlevel 1 if a usable SDK could not be provided.
rem  NOTE: intentionally no setlocal - PATH changes must reach the caller.
rem ============================================================

rem eWDK / Visual Studio shells export MSBuildSDKsPath pointing at the .NET
rem Framework SDK (e.g. NETFXSDK\4.7.2). That path takes priority over the
rem .NET Core SDK resolver, so every build fails with:
rem   error MSB4236: The SDK 'Microsoft.NET.Sdk' specified could not be found
rem even though dotnet --list-sdks shows a valid 9.0.x SDK. Clear it here so
rem the Core SDK resolves. No setlocal above, so this reaches the calling
rem Build_*.cmd and its dotnet publish/build step.
set "MSBuildSDKsPath="

rem Required SDK major version, read from <TargetFramework> netX.0 in the csproj.
set "_EDN_MAJOR=9"
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "[regex]::Match((Get-Content '%~dp0M2_APEX.csproj' -Raw),'net(\d+)\.').Groups[1].Value" 2^>nul`) do if not "%%v"=="" set "_EDN_MAJOR=%%v"

rem If dotnet isn't on PATH but exists in the default location, make it reachable.
where dotnet >nul 2>&1
if errorlevel 1 if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"

call :_edn_check
if not errorlevel 1 goto :_edn_done

echo.
echo [setup] .NET SDK %_EDN_MAJOR%.0+ not found - installing per-user (no admin required)...
set "_EDN_DIR=%LOCALAPPDATA%\Microsoft\dotnet"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $s=Join-Path $env:TEMP 'dotnet-install.ps1'; Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $s -UseBasicParsing; & $s -Channel '%_EDN_MAJOR%.0' -InstallDir '%_EDN_DIR%'"
if errorlevel 1 (
  echo [ERROR] Automatic .NET SDK install failed.
  echo         Install the .NET %_EDN_MAJOR% SDK manually: https://dotnet.microsoft.com/download
  set "_EDN_MAJOR="
  set "_EDN_DIR="
  exit /b 1
)
set "PATH=%_EDN_DIR%;%PATH%"

call :_edn_check
if not errorlevel 1 goto :_edn_done
echo [ERROR] .NET SDK still not detected after installation.
set "_EDN_MAJOR="
set "_EDN_DIR="
exit /b 1

:_edn_done
set "_EDN_VER="
for /f "usebackq delims=" %%s in (`dotnet --version 2^>nul`) do set "_EDN_VER=%%s"
echo [setup] .NET SDK %_EDN_VER% ready.
set "_EDN_MAJOR="
set "_EDN_DIR="
set "_EDN_VER="
exit /b 0

rem --- Succeeds (errorlevel 0) when dotnet resolves and its newest SDK major >= required. ---
:_edn_check
powershell -NoProfile -Command "$m=((& dotnet --list-sdks) 2>$null | ForEach-Object { [int]($_.Split('.')[0]) } | Measure-Object -Maximum).Maximum; if ($m -ge %_EDN_MAJOR%) { exit 0 }; exit 1"
exit /b %errorlevel%
