#define AppName "ASA: AntiSpermAttack"
#define AppVersion "1.0.0"
#define AppExe "ASA.exe"

[Setup]
AppId={{6B0F2C9E-4E0B-4C61-9B7A-A5A0C0FFEE01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ASA
DefaultDirName={autopf}\ASA
DefaultGroupName=ASA AntiSpermAttack
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=ASA-Setup-{#AppVersion}
SetupIconFile=..\src\ASA.App\asa.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.17763
WizardStyle=modern
CloseApplications=force

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ASA AntiSpermAttack"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\ASA AntiSpermAttack"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить ASA"; Flags: nowait postinstall

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; RunOnceId: "AsaCleanup"; Flags: waituntilterminated runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\ASA\clamav"
Type: filesandordirs; Name: "{commonappdata}\ASA\logs"
