#define AppName "Lieth Organigramme Assistant"
#define AppVersion "1.0.1"
#define AppExeName "LiethOrganigrammeAssistant.exe"

[Setup]
AppId={{0F0D93B9-5D7F-4C28-BB51-FBBC6F698F03}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=LiethOrganigrammeAssistant-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Lancer {#AppName}"; Flags: nowait postinstall skipifsilent
