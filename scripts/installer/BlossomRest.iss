; Blossom Rest (internal code name: TransitShelf)
; Inno Setup 6 script template.
;
; Prerequisites:
;   1. Run scripts\publish.ps1 first. It creates dist\BlossomRest-win-x64.
;   2. Install Inno Setup 6 on this machine, then compile with ISCC.exe.
;      This repository never downloads or installs the packaging tool.
;
; Compile:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" scripts\installer\BlossomRest.iss
;
; Design notes:
;   - Per-user install under %LOCALAPPDATA%, no administrator rights required.
;   - No Run registry value and no startup task: autostart is controlled by
;     the application's own settings and is unchecked by default.
;   - Built-in English is used for the wizard (no external .isl files).
;   - User data in %LOCALAPPDATA%\TransitShelf is left untouched on uninstall.

#define AppName "花托 · Blossom Rest"
#define AppVersion "0.1.0"
#define AppExeName "TransitShelf.exe"
#define PublishDir "..\..\dist\BlossomRest-win-x64"

[Setup]
; Stable application id; keep it unchanged across releases.
AppId={{7C2E5A94-0D3B-4F68-9A1E-5B8C7D2F4A61}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Blossom Rest
DefaultDirName={localappdata}\Programs\BlossomRest
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
PrivilegesRequired=lowest
OutputDir=..\..\dist
OutputBaseFilename=BlossomRest-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
