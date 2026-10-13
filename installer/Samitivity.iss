#define AppVersion "0.1.2"
[Setup]
AppId={{85169B43-476B-4788-BA4E-371D86AF384C}
AppName=Samitivity
AppVersion={#AppVersion}
AppPublisher=Samitivity
DefaultDirName={localappdata}\Programs\Samitivity
DefaultGroupName=Samitivity
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts\installer
OutputBaseFilename=Samitivity-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Samitivity.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=no

[Messages]
WindowsVersionNotSupported=Samitivity requires 64-bit Windows 10 or Windows 11. Run winver to check the Windows build. Compatibility layers such as CrossOver are not native Windows validation.

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Samitivity"; Filename: "{app}\Samitivity.exe"; Parameters: "--configure"
Name: "{autoprograms}\Uninstall Samitivity"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\Samitivity.exe"; Parameters: "--install-startup"; Flags: runhidden waituntilterminated; StatusMsg: "Setting up silent startup at sign-in…"
Filename: "{app}\Samitivity.exe"; Parameters: "--launch"; Flags: runhidden waituntilterminated; StatusMsg: "Starting Samitivity…"

[Code]
function ShutdownExisting(): Boolean;
var
  ExitCode: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\Samitivity.exe');
  Result := True;
  if FileExists(Exe) then
    Result := Exec(Exe, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not ShutdownExisting() then
    Result := 'Close Samitivity and resolve any unsaved changes before installing this update.';
end;

function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
begin
  Result := ShutdownExisting();
  if not Result then begin
    MsgBox('Close Samitivity and resolve any unsaved changes before uninstalling.', mbError, MB_OK);
    exit;
  end;
  if FileExists(ExpandConstant('{app}\Samitivity.exe')) then
    Result := Exec(ExpandConstant('{app}\Samitivity.exe'), '--uninstall-startup', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

// User data is intentionally absent from UninstallDelete. No implicit deletion of personal notes.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if not UninstallSilent then
      MsgBox('Samitivity has been removed. Your recordings and settings are preserved in ' + ExpandConstant('{localappdata}\LittleNotes') + '. You may delete that folder yourself if you no longer want them.', mbInformation, MB_OK);
end;
