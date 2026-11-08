; Blossom Rest (internal code name: TransitShelf)
; Inno Setup 6 script template.
;
; Prerequisites:
;   1. Run scripts\publish.ps1 first and note its new output directory.
;   2. Install Inno Setup 6 on this machine, then compile with ISCC.exe.
;      This repository never downloads or installs the packaging tool.
;
; Compile:
;   scripts\build-installer.ps1 -PublishRoot <output> -Compiler <ISCC.exe>
;
; Design notes:
;   - Per-user install under %LOCALAPPDATA%, no administrator rights required.
;   - No Run registry value and no startup task: autostart is controlled by
;     the application's own settings and is unchecked by default.
;   - Simplified Chinese messages are bundled beside this script.
;   - User data in %LOCALAPPDATA%\TransitShelf is left untouched on uninstall.

#define AppName "花托 · Blossom Rest"
#ifndef AppVersion
  #error Use scripts/build-installer.ps1 to read the published executable version.
#endif
#define AppExeName "TransitShelf.exe"
#ifndef PublishDir
  #error Supply the absolute PublishDir through scripts/build-installer.ps1.
#endif

[Setup]
; Stable application id; keep it unchanged across releases.
AppId={{7C2E5A94-0D3B-4F68-9A1E-5B8C7D2F4A61}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#GetVersionNumbersString(AddBackslash(PublishDir) + AppExeName)}
AppPublisher=Blossom Rest
DefaultDirName={localappdata}\Programs\BlossomRest
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
PrivilegesRequired=lowest
MinVersion=10.0.17763
AllowNoIcons=yes
AppMutex=Local\TransitShelf.SingleInstance
CloseApplications=no
RestartApplications=no
SetupIconFile=..\..\src\TransitShelf.App\Assets\TransitShelf.ico
OutputDir=..\..\dist
OutputBaseFilename=BlossomRest-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
