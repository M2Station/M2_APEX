; Inno Setup script for M2_APEX.
; Wraps the self-contained single-file EXE into a per-user installer (no admin required to install):
; Start Menu shortcut, optional desktop icon, uninstaller, and launch-after-install.
;
; The app manifest requests highestAvailable, so M2_APEX elevates at runtime (a UAC prompt on launch
; for administrators); this lets its global hotkey fire over elevated windows. The install itself
; stays per-user / non-elevated. "Launch at startup" registers a per-user autostart: a highest-
; privileges scheduled task named "M2_APEX" when elevated, or an HKCU\...\Run value otherwise. The
; [UninstallRun] section below removes both on uninstall (best effort) so nothing points at a deleted EXE.
;
; Build (per architecture), e.g.:
;   ISCC.exe /DAppVersion=0.0.1 /DArch=x64 /DSourceExe=path\to\M2_APEX.exe /DOutDir=dist installer\M2_APEX.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\win-x64\M2_APEX.exe"
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif

[Setup]
AppId={{B2E9F0C1-7A4D-4C3E-9B6A-4D8E2F1A0C55}
AppName=M2_APEX
AppVerName=M2_APEX {#AppVersion}
AppVersion={#AppVersion}
AppPublisher=M2Station
AppPublisherURL=https://github.com/M2Station/M2_APEX
DefaultDirName={autopf}\M2_APEX
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\M2_APEX.exe
SetupIconFile=..\Assets\M2Logo.ico
OutputDir={#OutDir}
OutputBaseFilename=M2_APEX-{#AppVersion}-Setup-{#Arch}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Detects a running instance (the app's single-instance mutex) and asks to close it first.
AppMutex=M2_APEX.SingleInstance

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "M2_APEX.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\M2_APEX"; Filename: "{app}\M2_APEX.exe"
Name: "{autodesktop}\M2_APEX"; Filename: "{app}\M2_APEX.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\M2_APEX.exe"; Description: "{cm:LaunchProgram,M2_APEX}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the per-user "launch at startup" entries the app may have created, so uninstalling does not
; leave an autostart pointing at a deleted EXE. Both are best effort, hidden, and harmless if absent.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""M2_APEX"" /F"; Flags: runhidden; RunOnceId: "DelM2ApexTask"
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""M2_APEX"" /f"; Flags: runhidden; RunOnceId: "DelM2ApexRunValue"
