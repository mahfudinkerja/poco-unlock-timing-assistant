; Inno Setup script for POCO Unlock Timing Assistant.
;
; Build the payload first (from the repository root):
;   dotnet publish src/PocoUnlockAssistant.App/PocoUnlockAssistant.App.csproj ^
;     -c Release -r win-x64 --self-contained true ^
;     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
;     -o artifacts/win-x64
;
; Then compile this script on a Windows host with Inno Setup 6.3+:
;   iscc installer\PocoUnlockAssistant.iss
; Output: installer/Output/PocoUnlockAssistantSetup-<version>.exe
;
; User data lives in %APPDATA%\PocoUnlockAssistant (history database, settings,
; logs, evidence screenshots). It is intentionally NOT removed on uninstall and
; is untouched by upgrades, so history and the duplicate-submission guard
; survive reinstalling the application.

#define MyAppName "POCO Unlock Timing Assistant"
#define MyAppVersion "0.1.1"
#define MyAppPublisher "POCO Unlock Timing Assistant"
#define MyAppExeName "PocoUnlockAssistant.exe"
#define PayloadDir "..\artifacts\win-x64"

[Setup]
; A stable AppId is what makes upgrades clean: the new installer replaces the
; previous version instead of installing side by side.
AppId={{7C1B2F4A-5D3E-4A8B-9C6F-2E7D1A0B3C54}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\PocoUnlockAssistant
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=PocoUnlockAssistantSetup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#PayloadDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PayloadDir}\*.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme skipifsourcedoesntexist
Source: "..\KNOWN-LIMITATIONS.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\ARCHITECTURE.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\IMPLEMENTATION-PLAN.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\QA-REPORT.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; No [UninstallDelete] entries: user data in %APPDATA%\PocoUnlockAssistant is
; deliberately preserved, and everything else lives under {app}.
