# Changelog

What changed in each LaunchHeim release, newest first. The download page and the GitHub release
notes are built from this file, so write for users: what they notice, not how it was done.

Format: a `## <version> - <yyyy-mm-dd>` heading per release (the version must match `<Version>` in
`src/Desktop/LaunchHeim.Desktop.csproj`), then `### Added`, `### Changed` or `### Fixed` with one
bullet per change.

## 0.5.3 - 2026-10-08

### Added
- A setup for Windows. It installs LaunchHeim for you or for all users, and asks whether you want a
  Start menu entry and a desktop shortcut. It also adds LaunchHeim to Windows' list of apps, so you
  can uninstall it there; your instances and settings stay.
- On Windows, **Update now** in the update reminder downloads the new version, installs it and starts
  LaunchHeim again. This works for copies installed with the setup; the portable zip still shows
  the commands.
- The Windows version is signed: the setup, `LaunchHeim.exe` and LaunchHeim's own DLLs carry a signature from
  `CodeIsNotEvil`, certificate thumbprint `284F5384B189984492629622EDC4A40646C5D8F6`. It shows the
  files come from LaunchHeim's build and weren't changed since. The certificate is self-signed, so
  Windows still calls the publisher unknown. The download page shows how to check it.

### Fixed
- The update reminder's dialog now has its background behind all of its text and buttons, not just
  the title.

## 0.5.2 - 2026-10-06

### Added
- LaunchHeim tells you when a new version is out, with one small line in the sidebar. Click it for
  the download page and the commands that update your install (Arch, Debian/Ubuntu, Fedora, Windows,
  or install.sh). Skip a version, or turn the reminder off and on again under Settings → About.

### Fixed
- On Windows, LaunchHeim now sees that Steam is running and logged in before it starts Valheim. It
  read a registry value Steam no longer writes, so it started Steam again and then gave up.
- A Steam started as administrator no longer counts as not running.

## 0.5.1 - 2026-10-05

### Fixed
- Send to phone no longer closes LaunchHeim when it sends only the server list, which is always the
  case while the library has no instances.
- Phone sync starts on Windows even when Windows has reserved its usual network port (Hyper-V and WSL
  do that); it then uses another free one, which phones still find.

## 0.5.0 - 2026-10-05

### Added
- LaunchHeim Companion for Android: keep your instances' mod lists on the phone, browse Thunderstore,
  Nexus Mods and CurseForge and add mods with their dependencies, and see how many players are on your
  servers. Download the APK from the release or the download page.
- Send to phone (Library or an instance) sends your instances and Valheim's server list to the
  companion over your Wi-Fi, with no account and no cloud. It also works with the LocalSend app.
- Mod lists edited on the phone come back the same way: LaunchHeim lists what changed, and Update
  instance installs, removes and switches mods while your configs stay as they are. A list started on
  the phone becomes a new instance.
- Settings → Phone sync switches receiving on and sets the name phones see for this PC.

## 0.4.0 - 2026-10-04

### Added
- A Play page with the servers from Valheim's Favorites and Recent lists and your worlds. Pick a
  character and a setup (vanilla or one of your instances) once, and the next time one click on Play
  starts it again with the same choice, and a server's password.
- Joining a server opens Valheim at the character selection with your character already picked, so
  Start takes you in. A world opens with its character and world selected in the menus.
- Give every server and world its own picture: click its icon on the Play page and choose an image.
- See how many players are on each server, updated every 30 seconds while the Play page is open.
  Valheim servers don't share player names, so only the number is shown.
- The sidebar shows which server or world you are playing.

### Changed
- The Play button in the sidebar starts what you played last, including a server or world with its
  character and setup, instead of always starting vanilla Valheim.

## 0.3.0 - 2026-09-30

### Added
- Modpacks: export an instance as an `.r2z` file and share it. Importing one creates a new instance
  with the same mods at the same versions, the same configs and the same launch options. The files
  also work with r2modman and the Thunderstore Mod Manager, and their profile exports import into
  LaunchHeim.
- A console window that follows the game's logs live while you play: BepInEx's log for the instance,
  Unity's Player.log and LaunchHeim's own log, with errors and warnings highlighted and counted, a
  filter, and a button to copy the lines. It can open by itself every time the game starts.
- LaunchHeim keeps a log of its own, with every launch, install and error, for reporting problems.
- On Windows, an instance can show BepInEx's own console window next to the game.

### Fixed
- On Linux, "Install from file" and choosing the game folder in Settings did nothing when LaunchHeim
  ran with its own copy of Qt (installed with `install.sh`): the file dialog never opened.

## 0.2.0 - 2026-09-30

### Added
- Steam is started for you. If Steam isn't running when you click Play, LaunchHeim starts it, waits
  until you are logged in and then starts Valheim. The sidebar shows "Starting Steam" or "Waiting for
  Steam" meanwhile.
- Settings → About shows the license and the software LaunchHeim is built on, with a button that
  opens the full third-party notices. Every download now includes them.

### Changed
- The window and taskbar show the LaunchHeim logo on Linux and Windows instead of a generic icon.

### Fixed
- Starting Valheim while Steam was closed showed only a black window, with no error.
- Settings → About showed version 1.0.0 instead of the real version, and LaunchHeim sent the same
  wrong version to Thunderstore and Nexus Mods.

## 0.1.0 - 2026-09-27

### Added
- First release: separate modded instances for Valheim, each with its own BepInEx, mods and configs,
  while the game folder stays vanilla.
- Mods from Thunderstore, Nexus Mods and CurseForge, with dependencies and BepInEx installed
  automatically, and update checks for Thunderstore mods.
- Downloads for Windows 10/11, Arch and CachyOS, Debian and Ubuntu, and Fedora and RHEL.
