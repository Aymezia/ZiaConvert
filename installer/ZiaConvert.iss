; Script Inno Setup pour ZiaConvert.
;
; Compile le contenu deja publie dans dist/ (voir README.md « Publier un executable
; autonome ») : ce script ne fait que l'emballer, jamais lui-meme dotnet publish. La
; signature de l'installateur genere n'est pas geree ici mais par le compilateur
; appelant (installer/build.ps1), via l'option de ligne de commande /Ssigntool= —
; garder le script libre de tout chemin ou secret propre a une machine.

#define MyAppName "ZiaConvert"
#define MyAppVersion "0.3.0"
#define MyAppPublisher "ZiaConvert"
#define MyAppExeName "ZiaConvert.exe"
#define MyCliExeName "zia.exe"
#define DistDir "..\dist"

[Setup]
AppId={{8F1E1B0E-6C2A-4B3D-9E7F-2A6C1D4E8B90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile={#DistDir}\LICENSE.txt
OutputDir=output
OutputBaseFilename=ZiaConvert-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableWelcomePage=no
; Le nom "signtool" ici doit correspondre a celui defini par /Ssigntool=... sur la
; ligne de commande (voir installer/build.ps1) : sans cette directive, ISCC compile
; sans jamais invoquer l'outil, meme quand /S le definit.
SignTool=signtool
SignedUninstaller=yes
; Filet de securite pour la mise a jour silencieuse declenchee depuis l'application
; (voir AppUpdater.cs) : si ZiaConvert.exe n'a pas fini de se fermer par lui-meme au
; moment ou Setup doit remplacer ses fichiers, le Gestionnaire de redemarrage de
; Windows le ferme puis le relance a la fin de l'installation.
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Creer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"
Name: "addtopath"; Description: "Ajouter zia (ligne de commande) au PATH"; GroupDescription: "Ligne de commande :"; Flags: unchecked

[Files]
Source: "{#DistDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\{#MyCliExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DistDir}\LISEZ-MOI.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "{#DistDir}\tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  EnvironmentKey = 'Environment';

// Ajoute {app} au PATH utilisateur sans jamais dupliquer l'entree, et propage le
// changement aux fenetres deja ouvertes (sans WM_SETTINGCHANGE, seuls les nouveaux
// processus lanceraient a partir d'un explorateur relance verraient le PATH a jour).
procedure AddToUserPath(Dir: string);
var
  Path: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Path) then
    Path := '';

  if (Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Path) + ';') = 0) then
  begin
    if (Length(Path) > 0) and (Path[Length(Path)] <> ';') then
      Path := Path + ';';
    Path := Path + Dir;
    RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Path);
  end;
end;

procedure RemoveFromUserPath(Dir: string);
var
  Path: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Path) then
    exit;

  P := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Path) + ';');
  if P > 0 then
  begin
    Delete(Path, P, Length(Dir) + 1);
    RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Path);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToUserPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromUserPath(ExpandConstant('{app}'));
end;
