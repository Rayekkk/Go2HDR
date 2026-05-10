#define AppName      "Go2HDR"
#define AppVersion   "2.1.0"
#define AppPublisher "Go2HDR"
#define AppExeName   "Go2HDR.exe"
#define SourceDir    "..\bin\Publish"

[Setup]
AppId={{B3C7A1F2-94DE-4E58-A021-6D3F80C5E49A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=Go2HDR-Setup-{#AppVersion}
SetupIconFile=..\Assets\Go2HDR.ico
WizardSmallImageFile=..\Assets\Go2HDR_About.png
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
MinVersion=10.0.17763
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*";           DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "redist\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}";   Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Run]
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; \
  StatusMsg: "Installing Visual C++ Runtime..."; Check: VCRedistNeedsInstall; \
  Flags: waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; \
  Flags: nowait postinstall skipifsilent

[Code]

// WinAPI — detect running instance via the app's named mutex.
function OpenMutex(dwDesiredAccess: DWORD; bInheritHandle: BOOL;
  lpName: String): THandle;
  external 'OpenMutexW@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

function IsAppRunning(): Boolean;
var
  Handle: THandle;
begin
  // SYNCHRONIZE = $00100000; just checks existence, does not wait.
  Handle := OpenMutex($00100000, False, 'Go2HDR_SingleInstance');
  Result := Handle <> 0;
  if Result then CloseHandle(Handle);
end;

// Called before the uninstall wizard starts.
// Warns the user if Go2HDR is running and offers to close it automatically.
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not IsAppRunning() then Exit;

  case MsgBox(
    'Go2HDR is currently running.' + #13#10#13#10 +
    'Click Yes to close it automatically and continue uninstalling.' + #13#10 +
    'Click No to abort — you can close Go2HDR manually first.',
    mbConfirmation, MB_YESNO) of
    IDYES:
      begin
        Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExeName}', '',
             SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Sleep(1000);
      end;
    IDNO:
      Result := False;
  end;
end;

// After uninstall: remove autostart and toast-notification AUMID registry entries.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'Go2HDR');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER,
      'SOFTWARE\Classes\AppUserModelId\Go2HDR.App');
  end;
end;

// VC++ check — used by [Run].
function VCRedistNeedsInstall: Boolean;
var
  Installed: Cardinal;
begin
  Result := not (RegQueryDWordValue(HKLM,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64',
    'Installed', Installed) and (Installed = 1));
end;
