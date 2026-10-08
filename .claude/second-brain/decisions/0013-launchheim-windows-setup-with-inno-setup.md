---
tags: [decision, launchheim, windows, release]
created: 2026-10-08
updated: 2026-10-08
status: active
supersedes:
---
# 0013: LaunchHeim's Windows setup is Inno Setup, and installed copies update by running it

**Context.** The owner wanted a Windows installer for 0.5.3 so nobody has to put the app folder in
place and make a shortcut by hand, with a choice of Start menu entry and desktop shortcut, and
updates on Windows going through that installer. Until then Windows had only the portable zip, and
the update reminder showed PowerShell commands that copy a new zip over the folder.

**Decision.** (2026-10-08)
- Inno Setup 6 (`packaging/windows/launchheim.iss`), compiled by `build.ps1` after the zip from the
  same signed folder. GitHub's Windows runner image has it (6.7.1 at the time).
- Per user by default (`PrivilegesRequired=lowest`, `%LOCALAPPDATA%\Programs\LaunchHeim`), with
  Inno's dialog to install for all users instead. Updates keep the mode, folder and shortcuts.
- Two tasks: Start menu entry (ticked), desktop shortcut (unticked). `[InstallDelete]` removes a
  shortcut whose task was unticked on a later run.
- The AppId `{AA9F6EE2-43EF-4C30-A1B6-44AEFFC95ECB}` is fixed forever. LaunchHeim reads that uninstall
  entry's `InstallLocation` (`Hosting/WindowsSetup.cs`); only a copy running from it is
  `InstallKind.WindowsSetup` and gets **Update now**.
- Update now: download the release's `-win-x64-setup.exe` into the cache's `tmp`, require and check
  GitHub's asset `digest` (SHA-256), start it with `/SILENT /SUPPRESSMSGBOXES /NORESTART
  /CLOSEAPPLICATIONS /relaunch=yes`, quit. `/relaunch=yes` is the setup's own switch: a silent setup
  otherwise doesn't start the app.
- The uninstaller keeps the user's data and deletes `HKCU\Software\Classes\nxm` only if it points at
  the uninstalled exe.
- The zip stays, as "portable", and keeps its copy-over commands.
- The setup and its uninstaller are signed through `sign.ps1` (ISCC's `SignTool`), which reads the
  password from the environment because ISCC echoes the sign command.

**Alternatives.**
- WiX / MSI: optional shortcuts need a feature tree or custom UI, and upgrades need MajorUpgrade
  rules; more machinery for the same result.
- MSIX: needs a trusted signing certificate to install at all, which a self-signed one isn't
  ([[0012-launchheim-windows-self-signed-code-signing]]).
- Velopack / Squirrel-style self-updaters: they own the app's folder layout and start-up, and would
  sit badly with Qml.Net's native libraries; overkill for one exe.
- Dropping the zip: kept, it costs nothing extra to build and some people want portable.

**Consequences.** Every release has six files. The Windows smoke test installs, updates twice and
uninstalls the setup per user, so a broken setup fails CI. A copy run from anywhere else (an
unzipped one beside an installed one) never gets Update now, so it can't overwrite another folder.
Inno Setup's license allows any use; it asks for a commercial license only as a courtesy, and the
notices list it (`licenses/InnoSetup.txt`).

Related: [[launchheim]], [[0012-launchheim-windows-self-signed-code-signing]]
