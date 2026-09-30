---
tags: [project, dotnet, qml, gaming]
created: 2026-09-26
updated: 2026-09-30
status: active
---
# LaunchHeim

A Valheim mod launcher for the owner's CachyOS / Plasma 6 desktop, also packaged for Debian/Ubuntu
and Fedora/RHEL and ported to Windows. Code: `Applications/LaunchHeim/`. Behaviour, build and the Qt
runtime workarounds: `Applications/LaunchHeim/README.md`. Installing and packaging:
`Applications/LaunchHeim/packaging/README.md`.

## Shape in one breath
Instances (own BepInEx + plugins + configs under `~/.local/share/LaunchHeim/instances`) → Valheim is
started directly with Unity Doorstop pointed at the instance's preloader, so the game folder stays
vanilla. Mods come from Thunderstore (full index cached locally), Nexus (API key, `nxm://` for free
accounts) and CurseForge (API key).

## Key ideas
- MIT-licensed (owner's choice, 2026-09-26), `Applications/LaunchHeim/LICENSE`. Only LaunchHeim, not the
  whole monorepo.
- Third-party licenses (2026-09-30): `THIRD-PARTY-NOTICES.txt` + `licenses/` ship with every build (the
  csproj copies them), Settings → About names Qt and its LGPL and opens the file, and the download
  page links Qt 5.15.2's source next to the Windows zip. Qt's bundled third-party list is generated
  by `packaging/qt-third-party-notices.py`. The README's "Licenses" section has the checklist.
- Per-instance BepInEx through Doorstop instead of copying mods into the game folder: switching
  modpacks costs nothing, and the Steam install never has to be verified or repaired.
- Qml.Net (Qt 5.15) as the UI host. It's unmaintained since 2020, so it needs three workarounds
  (tar extraction, `libdl.so`, the native signal fix). They're listed in the README. Any upgrade of
  .NET, glibc or Qt should re-check them.
- Colours come from `kdeglobals` because Plasma 6 has no Qt 5 platform theme.
- Branding: accent `#DE5833` and Kode Mono are fixed (Theme.qml), matching the repo's logos. Logos are SVG
  from `Scripts/text_logo.py`, and raster files are always rendered from the SVG (2026-09-27).

- Distro packages use the system Qt ([[0005-launchheim-distro-packages-use-system-qt]]). Windows keeps
  the QML UI with a patched QmlNet.dll built in CI ([[0006-launchheim-windows-keeps-qml]]).

## Gotchas
- NetNativeLibLoader, pulled in by Qml.Net, is LGPL-3.0-or-later (Firwood Software), not MIT like
  Qml.Net. It ships in every build. Check transitive NuGet licenses, not only direct ones (2026-09-30).
- windeployqt also copies `vc_redist.x64.exe` (about 25 MB) into the Windows zip, although build.ps1
  copies the CRT DLLs itself. `--no-compiler-runtime` would drop it (2026-09-30, not changed yet).
- Qml.Net context properties get JS ownership and are garbage-collected, so the view model is a
  QML singleton.
- Qt 5.15's Material `ComboBox` logs a `foreground` binding loop. Setting `Material.foreground` on the
  instance silences it (2026-09-26).
- .NET's `Encoding.UTF8` writes a BOM. Files other tools parse (`.desktop`) must use the default
  BOM-less UTF-8 (2026-09-26).

- Qml.Net's `libQmlNet.so` carries a `/home/travis/...` RPATH, which is a library injection risk. The
  build overwrites it with `$ORIGIN`, and Fedora's rpmbuild refuses the unpatched file (2026-09-26).
- `grabToImage` (the screenshot mode) waits forever while the Plasma session is locked, because nothing
  renders frames. Use `QT_QPA_PLATFORM=offscreen QT_QUICK_BACKEND=software` (2026-09-26).
- GitHub's Windows runners check out with `core.autocrlf=true`, so `.patch` files need `eol=lf`
  (`.gitattributes`) or `git apply` fails.
- The window and taskbar showed placeholder icons on both systems (2026-09-28). Windows: Qt only
  loads an exe icon resource named `IDI_ICON1`, and .NET's `<ApplicationIcon>` has a numeric id.
  Plasma Wayland: Qt 5 derives the app_id from the organization domain (`local.cine.LaunchHeim`)
  unless a desktop file name is set. `native/app_icon.cpp` fixes both; check the app_id with a KWin
  script printing `workspace.windowList()` `desktopFileName`s.
- Valheim started without a logged-in Steam client shows a black window and no error, because the game
  is started directly rather than through Steam. `SteamClient` starts Steam and waits for the login
  first (2026-09-30). Linux Steam no longer writes `ActiveProcess/ActiveUser` to `registry.vdf` (only
  `HKLM/.../SteamPID`), so the first version waited forever. On Linux the login comes from
  `logs/connection_log.txt` (last state `Logged On`, newer than the `steam` process); Windows keeps
  the registry value. The pid Steam records is not trusted: Flatpak Steam writes its sandbox pid, and
  `~/.steam/steam.pid` is left stale after Steam exits.
- .NET can't marshal `string[]` as UTF-8 (`LPUTF8Str` isn't allowed as an `ArraySubType`). Pass
  `LPWStr` and take `const QChar*` natively (2026-09-28).

## Open
- A Windows installer (Inno Setup, MSIX or winget), and live theme switching on Windows.
- Moving Linux to the same patched qmlnet-native build as Windows would drop `signal_fix.cpp`, the
  g++/qt5-base build dependencies and the RPATH patch.
- Only Thunderstore mods are checked for updates (Nexus and CurseForge would cost one API call per mod).
- The Desktop project has no tests. The UI is checked with the `LAUNCHHEIM_SCREENSHOT` mode.
