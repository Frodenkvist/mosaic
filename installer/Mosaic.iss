; Mosaic installer script — compiled with Inno Setup 6 (ISCC.exe).
; The product version is injected by installer\package.ps1 via /DMyAppVersion=<version>.
; A default is defined so the script can also be opened/compiled directly from the IDE.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "Mosaic"
#define MyAppPublisher "Mosaic"
#define MyAppExeName "Mosaic.exe"

[Setup]
; Stable AppId — this GUID MUST stay constant across versions so installing a newer
; version upgrades the existing one in place (rather than installing side-by-side).
AppId={{8F3A1C7E-2B4D-4E9A-9C1F-7A6B5D2E3F40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
WizardStyle=modern

; Per-user install: no administrator rights and no UAC elevation prompt. This also means a
; future auto-updater can replace files in a user-writable location without elevation.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Mosaic
DefaultGroupName=Mosaic

; Keep the standard wizard pages the spec calls for (welcome, install location, tasks, ready, finish).
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes

UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\Mosaic.ico

Compression=lzma2/max
SolidCompression=yes
OutputBaseFilename=MosaicSetup-{#MyAppVersion}
OutputDir=dist

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; If Mosaic is running during an upgrade or uninstall, prompt to close it so files aren't locked.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Pack the entire self-contained publish folder (installer\publish) produced by package.ps1.
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; Start Menu shortcut is always created; the desktop shortcut is gated on the opt-in task.
Name: "{group}\Mosaic"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Mosaic"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Mosaic}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DeleteUserData: Boolean;

// Asked once before uninstalling: should we also wipe the user's data directory?
// Defaults to NO (keep data) so reinstall/upgrade preserves the game library.
// During a silent uninstall (e.g. a future auto-updater) we never prompt and always keep data.
function InitializeUninstall(): Boolean;
begin
  DeleteUserData := False;
  if not UninstallSilent() then
  begin
    if MsgBox('Do you also want to delete your Mosaic data ' +
              '(game library, artwork, and settings)?' + #13#10 + #13#10 +
              'Choose No to keep your library for a future reinstall.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DeleteUserData := True;
  end;
  Result := True;
end;

// After program files are removed, optionally delete %LocalAppData%\Mosaic.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if (CurUninstallStep = usPostUninstall) and DeleteUserData then
  begin
    DataDir := ExpandConstant('{localappdata}\Mosaic');
    if DirExists(DataDir) then
      DelTree(DataDir, True, True, True);
  end;
end;
