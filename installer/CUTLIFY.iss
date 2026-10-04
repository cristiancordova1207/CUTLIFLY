; Instalador de CUTLIFY (Inno Setup 6)
; Compilar: iscc /DAppVersion=1.1.0 installer\CUTLIFY.iss
; Con firma del desinstalador: añadir /DSignUninstaller y /Scutlifysign="..." (lo hace GitHub Actions).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{A7D3F2B1-5C4E-4F8A-9B61-2E7C3D9F1A40}
AppName=CUTLIFY
AppVersion={#AppVersion}
AppVerName=CUTLIFY {#AppVersion}
AppPublisher=CUTLIFY
AppComments=Screen Capture & Recording
DefaultDirName={localappdata}\Programs\CUTLIFY
DefaultGroupName=CUTLIFY
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
OutputDir=..\releases
OutputBaseFilename=CUTLIFY-Setup
SetupIconFile=..\icons\cutlify.ico
UninstallDisplayIcon={app}\CUTLIFY.exe
UninstallDisplayName=CUTLIFY
LicenseFile=..\LICENSE.txt
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName=CUTLIFY
VersionInfoDescription=CUTLIFY Setup
VersionInfoCopyright=Copyright (C) 2026 CUTLIFY
#ifdef SignUninstaller
SignTool=cutlifysign
SignedUninstaller=yes
#endif

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "startup"; Description: "Iniciar CUTLIFY con Windows (en la bandeja)"; GroupDescription: "Inicio:"; Flags: unchecked

[Files]
Source: "..\publish\CUTLIFY.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\CUTLIFY"; Filename: "{app}\CUTLIFY.exe"; Comment: "Screen Capture & Recording"
Name: "{userdesktop}\CUTLIFY"; Filename: "{app}\CUTLIFY.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CUTLIFY"; ValueData: """{app}\CUTLIFY.exe"" --tray"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\CUTLIFY.exe"; Description: "Abrir CUTLIFY"; Flags: nowait postinstall skipifsilent
; Actualización silenciosa desde la app: volver a abrir CUTLIFY al terminar.
Filename: "{app}\CUTLIFY.exe"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM CUTLIFY.exe"; Flags: runhidden; RunOnceId: "KillCutlify"

[UninstallDelete]
; Solo cachés internas regenerables. La configuración, el historial y los archivos del usuario se preguntan abajo.
Type: filesandordirs; Name: "{localappdata}\CUTLIFY\Temporales"
Type: filesandordirs; Name: "{localappdata}\CUTLIFY\Updates"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "CUTLIFY"; ValueType: none; Flags: dontcreatekey uninsdeletevalue

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir, DocsDir: string;
begin
  if (CurUninstallStep <> usPostUninstall) or UninstallSilent then exit;
  DataDir := ExpandConstant('{localappdata}\CUTLIFY');
  DocsDir := ExpandConstant('{userdocs}\CUTLIFY');
  if DirExists(DataDir) then
    if MsgBox('¿Eliminar también la configuración, el historial y las miniaturas de CUTLIFY?' + #13#10#13#10 +
              'Elige «No» para conservarlos (útil si vas a reinstalar).' + #13#10 + DataDir,
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DataDir, True, True, True);
  if DirExists(DocsDir) then
    if MsgBox('¿Eliminar también las capturas y grabaciones guardadas por CUTLIFY?' + #13#10#13#10 +
              DocsDir + #13#10 + 'Esta acción no se puede deshacer. Elige «No» para conservarlas.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DocsDir, True, True, True);
end;
