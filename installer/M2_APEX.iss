; Inno Setup script for M2_APEX.
; Wraps the self-contained single-file EXE into a per-user installer (no admin required):
; Start Menu shortcut, optional desktop icon, uninstaller, and launch-after-install.
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
