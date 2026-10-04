; Instalador de CUTLIFLY (Inno Setup 6)
; Compilar: iscc /DAppVersion=1.0.0 installer\CUTLIFLY.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{A7D3F2B1-5C4E-4F8A-9B61-2E7C3D9F1A40}
AppName=CUTLIFLY
AppVersion={#AppVersion}
AppVerName=CUTLIFLY {#AppVersion}
AppPublisher=CUTLIFLY
AppPublisherURL=https://github.com/cristiancordova1207/cutlifly
AppSupportURL=https://github.com/cristiancordova1207/cutlifly/issues
AppUpdatesURL=https://github.com/cristiancordova1207/cutlifly/releases
AppComments=Screen Capture & Recording
DefaultDirName={localappdata}\Programs\CUTLIFLY
DefaultGroupName=CUTLIFLY
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\releases
OutputBaseFilename=CUTLIFLY-Setup
SetupIconFile=..\icons\cutlifly.ico
UninstallDisplayIcon={app}\CUTLIFLY.exe
UninstallDisplayName=CUTLIFLY
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName=CUTLIFLY
VersionInfoDescription=CUTLIFLY Setup

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "startup"; Description: "Iniciar CUTLIFLY con Windows (en la bandeja)"; GroupDescription: "Inicio:"

[Files]
Source: "..\publish\CUTLIFLY.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\CUTLIFLY"; Filename: "{app}\CUTLIFLY.exe"; Comment: "Screen Capture & Recording"
Name: "{userdesktop}\CUTLIFLY"; Filename: "{app}\CUTLIFLY.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CUTLIFLY"; ValueData: """{app}\CUTLIFLY.exe"" --tray"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\CUTLIFLY.exe"; Description: "Abrir CUTLIFLY"; Flags: nowait postinstall skipifsilent
; Actualización silenciosa desde la app: volver a abrir CUTLIFLY al terminar.
Filename: "{app}\CUTLIFLY.exe"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM CUTLIFLY.exe"; Flags: runhidden; RunOnceId: "KillCutlifly"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\CUTLIFLY\Temp"
Type: filesandordirs; Name: "{localappdata}\CUTLIFLY\Updates"
