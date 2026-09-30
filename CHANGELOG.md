# Changelog

What changed in each LaunchHeim release, newest first. The download page and the GitHub release
notes are built from this file, so write for users: what they notice, not how it was done.

Format: a `## <version> - <yyyy-mm-dd>` heading per release (the version must match `<Version>` in
`src/Desktop/LaunchHeim.Desktop.csproj`), then `### Added`, `### Changed` or `### Fixed` with one
bullet per change.

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
