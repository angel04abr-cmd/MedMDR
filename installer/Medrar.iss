; Instalador de Medrar (Inno Setup). Se compila con:  ISCC /DAppVersion=1.1.0 installer\Medrar.iss
; (build.ps1 -Installer lo hace solo). Necesita que antes exista dist\Medrar (build.ps1).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{8C3F1B8E-5D3A-4C2E-9B6A-4F0E7B2A91D5}
AppName=Medrar
AppVersion={#AppVersion}
AppVerName=Medrar {#AppVersion}
AppPublisher=Medrar
AppComments=Gestor de archivos comprimidos
DefaultDirName={autopf}\Medrar
DefaultGroupName=Medrar
UninstallDisplayIcon={app}\Medrar.exe
UninstallDisplayName=Medrar
SetupIconFile=..\src\Medrar\Resources\medrar.ico
OutputDir=..\dist
OutputBaseFilename=Medrar-Setup-{#AppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
; Por defecto se instala solo para el usuario (no pide permisos de administrador); el asistente permite elegir «para todos».
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=yes
CloseApplications=yes
DisableProgramGroupPage=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "assoc"; Description: "Asociar los archivos .mdr con Medrar"; GroupDescription: "Integración con Windows:"
Name: "contextmenu"; Description: "Añadir «Añadir a un archivo con Medrar» al menú del botón derecho"; GroupDescription: "Integración con Windows:"
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
Source: "..\dist\Medrar\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Medrar"; Filename: "{app}\Medrar.exe"
Name: "{autodesktop}\Medrar"; Filename: "{app}\Medrar.exe"; Tasks: desktopicon

[Registry]
; --- archivos .mdr ---
Root: HKA; Subkey: "Software\Classes\.mdr"; ValueType: string; ValueData: "Medrar.Archive"; Flags: uninsdeletekey; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive"; ValueType: string; ValueData: "Archivo Medrar"; Flags: uninsdeletekey; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\DefaultIcon"; ValueType: string; ValueData: """{app}\Medrar.exe"",0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\open"; ValueType: string; ValueData: "Abrir con Medrar"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Medrar.exe"",0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\open\command"; ValueType: string; ValueData: """{app}\Medrar.exe"" ""%1"""; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\extracthere"; ValueType: string; ValueData: "Extraer aquí"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\extracthere"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Medrar.exe"",0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\extracthere\command"; ValueType: string; ValueData: """{app}\Medrar.exe"" --extract-here ""%1"""; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\extractto"; ValueType: string; ValueData: "Extraer en una carpeta…"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\extractto"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Medrar.exe"",0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\extractto\command"; ValueType: string; ValueData: """{app}\Medrar.exe"" --extract-to ""%1"""; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\test"; ValueType: string; ValueData: "Comprobar el archivo"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\test"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Medrar.exe"",0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\Medrar.Archive\shell\test\command"; ValueType: string; ValueData: """{app}\Medrar.exe"" --test ""%1"""; Tasks: assoc
; --- menú del botón derecho ---
Root: HKA; Subkey: "Software\Classes\*\shell\Medrar.Add"; ValueType: string; ValueData: "Añadir a un archivo con Medrar…"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\*\shell\Medrar.Add"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Medrar.exe"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\*\shell\Medrar.Add\command"; ValueType: string; ValueData: """{app}\Medrar.exe"" --add ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\Medrar.Add"; ValueType: string; ValueData: "Añadir a un archivo con Medrar…"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\Medrar.Add"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Medrar.exe"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\Medrar.Add\command"; ValueType: string; ValueData: """{app}\Medrar.exe"" --add ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\Medrar.Open"; ValueType: string; ValueData: "Abrir esta carpeta en Medrar"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\Medrar.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Medrar.exe"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\Medrar.Open\command"; ValueType: string; ValueData: """{app}\Medrar.exe"" ""%V"""; Tasks: contextmenu

[Run]
Filename: "{app}\Medrar.exe"; Description: "Abrir Medrar ahora"; Flags: nowait postinstall skipifsilent

[Code]
{ Medrar necesita el .NET 10 Desktop Runtime (no se incluye en el instalador). Se avisa si falta, con el enlace de descarga. }
function HasDesktopRuntime10: Boolean;
var
  FindRec: TFindRec;
  Roots: array of String;
  I: Integer;
begin
  Result := False;
  SetArrayLength(Roots, 3);
  Roots[0] := ExpandConstant('{commonpf64}') + '\dotnet\shared\Microsoft.WindowsDesktop.App\';
  Roots[1] := ExpandConstant('{commonpf32}') + '\dotnet\shared\Microsoft.WindowsDesktop.App\';
  Roots[2] := ExpandConstant('{localappdata}') + '\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App\';
  for I := 0 to GetArrayLength(Roots) - 1 do
    if FindFirst(Roots[I] + '10.*', FindRec) then
    begin
      Result := True;
      FindClose(FindRec);
      Exit;
    end;
end;

function InitializeSetup: Boolean;
var
  ErrCode: Integer;
  Answer: Integer;
begin
  Result := True;
  if not HasDesktopRuntime10 then
  begin
    Answer := SuppressibleMsgBox(
      'Medrar necesita el «.NET 10 Desktop Runtime», que no está instalado en este equipo.' + #13#10#13#10 +
      'Sí: abrir la página de descarga y continuar con la instalación.' + #13#10 +
      'No: continuar sin abrir la página (instálalo después).' + #13#10 +
      'Cancelar: salir del instalador.',
      mbConfirmation, MB_YESNOCANCEL, IDYES);
    if Answer = IDCANCEL then
      Result := False
    else if Answer = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
  end;
end;
