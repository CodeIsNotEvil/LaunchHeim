; LaunchHeim's Windows setup, compiled by build.ps1 with Inno Setup 6 (ISCC.exe) from the folder it
; publishes. It installs that folder, optionally adds Start menu and desktop shortcuts, registers an
; uninstaller, and is also how an installed copy updates: "Update now" in LaunchHeim downloads the new
; version's setup and runs it with /SILENT (src/Desktop/ViewModels/UpdateViewModel.cs).
;
;   ISCC.exe /DAppVersion=0.5.3 /DSourceDir=<dist\LaunchHeim> /DOutputDir=<dist> launchheim.iss
;   add /DSign /Slaunchheim=<command> to sign the setup and its uninstaller (build.ps1 does)
;
; See packaging/README.md, "Windows 10 / 11".

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z (build.ps1 reads it from Directory.Build.props).
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<the published LaunchHeim folder>.
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; Never change the AppId: Windows finds the installed copy and its uninstall entry by it, so a newer
; setup updates that copy instead of installing a second one. LaunchHeim reads the same entry
; (src/Desktop/Hosting/WindowsSetup.cs) to know that the setup installed it. The doubled brace is how
; Inno Setup writes a literal "{".
AppId={{AA9F6EE2-43EF-4C30-A1B6-44AEFFC95ECB}
AppName=LaunchHeim
AppVersion={#AppVersion}
AppVerName=LaunchHeim {#AppVersion}
AppPublisher=CodeIsNotEvil
AppPublisherURL=https://codeisnotevil.github.io/MyMonoRepo/
AppSupportURL=https://github.com/CodeIsNotEvil/MyMonoRepo/issues
AppUpdatesURL=https://codeisnotevil.github.io/MyMonoRepo/download.html#launchheim
AppCopyright=Copyright (c) 2026 CodeIsNotEvil
VersionInfoVersion={#AppVersion}
VersionInfoDescription=LaunchHeim Setup

; For the current user by default: no UAC prompt, and {autopf} is %LOCALAPPDATA%\Programs. The first
; page offers "Install for all users" instead (C:\Program Files, needs an administrator), and
; /ALLUSERS or /CURRENTUSER choose on the command line. An update keeps whichever the first install
; used (UsePreviousPrivileges, on by default). LaunchHeim's data is per user either way
; (%LOCALAPPDATA%\LaunchHeim, %APPDATA%\LaunchHeim).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\LaunchHeim
; Asked on the first install only; an update goes where the first one went.
DisableDirPage=auto
; The shortcuts are the two tasks below, so there's no Start menu folder to choose.
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

WizardStyle=modern
SetupIconFile=..\..\src\Desktop\packaging\launchheim.ico
UninstallDisplayIcon={app}\LaunchHeim.exe
UninstallDisplayName=LaunchHeim

; Files in use by a running LaunchHeim are closed through the Restart Manager: the wizard asks first,
; a silent setup with /CLOSEAPPLICATIONS (what "Update now" passes) just does it. It isn't restarted
; that way: the [Run] entry below starts it, and only once.
CloseApplications=yes
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename=LaunchHeim-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes

#ifdef Sign
; The same certificate as LaunchHeim.exe (packaging/README.md, "Code signing"). ISCC runs the command
; for the setup and for the uninstaller it embeds.
SignTool=launchheim
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startmenu"; Description: "In the &Start menu"; GroupDescription: "Shortcuts to LaunchHeim:"
Name: "desktopicon"; Description: "On the &desktop"; GroupDescription: "Shortcuts to LaunchHeim:"; Flags: unchecked

[Files]
; windeployqt adds vc_redist.x64.exe, which nothing runs: build.ps1 ships the Visual C++ runtime's DLLs
; next to LaunchHeim.exe. Leaving it out saves 25 MB.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "vc_redist.x64.exe"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Straight in the Start menu's program list, without a folder of its own, as Windows 10 and 11 expect.
Name: "{autoprograms}\LaunchHeim"; Filename: "{app}\LaunchHeim.exe"; Tasks: startmenu
Name: "{autodesktop}\LaunchHeim"; Filename: "{app}\LaunchHeim.exe"; Tasks: desktopicon

[InstallDelete]
; A shortcut is never taken away by itself when its task is unticked on a later run, so remove it here.
Type: files; Name: "{autoprograms}\LaunchHeim.lnk"; Tasks: not startmenu
Type: files; Name: "{autodesktop}\LaunchHeim.lnk"; Tasks: not desktopicon

[Run]
; Phone sync needs incoming connections (Firewall.cs). An all-users install runs as an administrator, so
; it allows LaunchHeim.exe in Windows Firewall itself, for every network profile because Windows 11
; puts new networks in Public. Rules already there for the exe go first: a block rule left by a
; cancelled "Allow access" prompt would win over the allow rule. A per-user install has no administrator;
; there Windows asks on the first start with phone sync on, and Settings -> Phone sync can do it later.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=all program=""{app}\LaunchHeim.exe"""; Flags: runhidden; StatusMsg: "Allowing phone sync in Windows Firewall..."; Check: IsAdminInstallMode
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""LaunchHeim phone sync"" dir=in action=allow program=""{app}\LaunchHeim.exe"" enable=yes profile=any"; Flags: runhidden; StatusMsg: "Allowing phone sync in Windows Firewall..."; Check: IsAdminInstallMode
; The "Launch LaunchHeim" tick on the last page. A silent setup skips it unless /relaunch=yes, which
; LaunchHeim's "Update now" passes so it's back when the update is done. As the user who started the
; setup, not as the administrator an all-users install elevated to.
Filename: "{app}\LaunchHeim.exe"; Description: "{cm:LaunchProgram,LaunchHeim}"; Flags: nowait postinstall runasoriginaluser; Check: ShouldLaunch

[UninstallRun]
; Removes the rule the setup or Settings -> Phone sync added. Without an administrator (a per-user
; install whose rule came from Settings) netsh fails quietly and the rule stays, pointing at a removed exe.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=all program=""{app}\LaunchHeim.exe"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"

[Code]
function ShouldLaunch: Boolean;
begin
  Result := (not WizardSilent) or (CompareText(ExpandConstant('{param:relaunch|no}'), 'yes') = 0);
end;

// Settings -> Register makes LaunchHeim the nxm:// handler under HKCU\Software\Classes\nxm. Once the
// exe is gone that entry points nowhere, so it goes too, but only if it still points at this copy:
// another mod manager may have taken nxm:// over since. Instances and settings stay, as with the zip.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Classes\nxm\shell\open\command', '', Command) and
      (Pos(Lowercase(ExpandConstant('{app}\LaunchHeim.exe')), Lowercase(Command)) > 0) then
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\nxm');
  end;
end;
