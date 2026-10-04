# Changelog

What changed in each LaunchHeim release, newest first. The download page and the GitHub release
notes are built from this file, so write for users: what they notice, not how it was done.

Format: a `## <version> - <yyyy-mm-dd>` heading per release (the version must match `<Version>` in
`src/Desktop/LaunchHeim.Desktop.csproj`), then `### Added`, `### Changed` or `### Fixed` with one
bullet per change.

## 0.4.0 - 2026-10-04

### Added
- A Play page with the servers from Valheim's Favorites and Recent lists and your worlds. Pick a
  character and a setup (vanilla or one of your instances) once, and the next time one click on Play
  starts it again with the same choice, and a server's password.
- Joining a server opens Valheim at the character selection with your character already picked, so
  Start takes you in. A world opens with its character and world selected in the menus.
- Give every server and world its own picture: click its icon on the Play page and choose an image.
- The sidebar shows which server or world you are playing.

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
