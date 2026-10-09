---
tags: [project, dotnet, qml, gaming]
created: 2026-09-26
updated: 2026-10-09
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

- Modpacks (2026-09-30) are r2modman `.r2z` exports with LaunchHeim's `launchheim.json` beside
  `export.r2x` ([[0009-launchheim-modpacks-are-r2z]]), so they work in r2modman and theirs import here.
- The console window follows log files (BepInEx `LogOutput.log`, Unity `Player.log`, LaunchHeim's own
  `~/.local/state/LaunchHeim/launchheim.log`) instead of piping the game's stdout, so closing LaunchHeim
  can never block or kill a running game (2026-09-30). Lines reach QML as JSON batches appended to a
  ListModel; a replaced Qml.Net list would reset the scroll position.
- Direct play (2026-10-04): the Play page lists Valheim's own servers and worlds and starts one with a
  remembered character and setup, through `+connect` and the game's PlayerPrefs
  ([[0010-launchheim-direct-play-uses-the-games-own-options]]). The README's "Servers and worlds" has
  the file locations and formats.
- LaunchHeim Companion (2026-10-05): the Android app in `Applications/LaunchHeim/android/` (Kotlin,
  Compose). It edits mod lists, browses Thunderstore and shows player counts; packs and the server list
  travel over LocalSend, and a returning pack updates its instance after a confirmation
  ([[0011-launchheim-companion-syncs-packs-over-localsend]]). Its README has the SDK setup.
- Phone sync and firewalls (2026-10-09): the packages ship a ufw profile and a firewalld service for
  UDP 53317 and TCP 53317-53326; Settings → Phone sync shows the ufw command when ufw would drop
  phones and adds the Windows Firewall rule on Windows (`Core/LocalSend/Firewall.cs`). Each send button
  sends one thing: an instance from its page, the server list from Play. A list made on the phone
  carries the phone's id, which LaunchHeim keeps as `Instance.LinkId`, so it updates instead of
  duplicating ([[0011-launchheim-companion-syncs-packs-over-localsend]]).
- Sharing with a friend (2026-10-09): the share button on an instance's page writes its `.r2z` and
  offers it as a drag tile, Copy file (the native helper puts it on the clipboard as a file manager
  would) and Show in folder. No Discord or Steam integration; the README's "Sharing with a friend" has
  why the drag tile comes first.
- Update reminder (2026-10-06): GitHub's releases API, newest `launchheim-v*` by version, at start
  and every 12 hours; a sidebar line and a dialog with per-install commands (`Core/Updates`). The
  install kind comes from the package build's switch plus `/etc/os-release`, the Windows zip, or
  install.sh's default prefix; anything else only gets the download page. Skip / never are
  `skippedUpdateVersion` / `updateRemindersDisabled` in settings.json.
- Distro packages use the system Qt ([[0005-launchheim-distro-packages-use-system-qt]]). Windows keeps
  the QML UI with a patched QmlNet.dll built in CI ([[0006-launchheim-windows-keeps-qml]]).
- Code signing (2026-10-08): `build.ps1` Authenticode-signs the exe and the DLLs it compiles with a
  self-signed `CN=CodeIsNotEvil` certificate, separate from the APK key
  ([[0012-launchheim-windows-self-signed-code-signing]]). Packaging README "Code signing".
- Windows setup (2026-10-08, for 0.5.3): Inno Setup, per user by default, optional Start menu and
  desktop shortcuts; an installed copy updates itself with **Update now**, which runs the new setup
  silently ([[0013-launchheim-windows-setup-with-inno-setup]]). The zip stays as the portable option.
- Steam overlay and screenshots (2026-10-08, for 0.5.4): Linux preloads Steam's overlay into the
  direct start, Windows starts Valheim through `steam.exe -applaunch` and follows its process by name
  ([[0014-launchheim-steam-overlay]]). The Screenshots page lists Steam's F12 pictures of Valheim with
  a large preview, "Show in folder" (Explorer `/select`, D-Bus `FileManager1.ShowItems` on Linux) and
  "Open".

## Gotchas
- A PC that shows up on the phone for a few seconds and then vanishes, with sends failing, is a
  firewall dropping incoming traffic, not a LocalSend bug: LaunchHeim's own announcements go out, the
  phone's answers and uploads don't come in. CachyOS turns ufw on with a dropping default (2026-10-09).
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
- A control stretches its `contentItem` to the available width, so a `Row` contentItem with
  `anchors.centerIn` still lays out from x = 0. Give the control padding instead (2026-10-02).
- A `RowLayout` can't shrink below its fixed-width children added together, and it pushes its parent
  wider instead. Put shrinkable parts in a `fillWidth` item with `Layout.minimumWidth: 0` (2026-10-02).
- QML's `Image` caches by URL, so a file replaced under the same name keeps showing the old picture
  until a restart. Give each new picture a new file name, as the Play page's `PlayImageStore` does
  (2026-10-04).
- Valheim's dedicated server answers A2S on the game port + 1, but A2S_PLAYER lists every player with
  an empty name, so only the count is known (2026-10-04, checked on a live server).
- The Windows workflow checks out with `core.autocrlf=true`, which also changes the line ends inside C#
  raw string literals in tests. Normalize fixtures with `ReplaceLineEndings("\n")`, and build expected
  paths with `Path.Combine`, even for the Linux code path (2026-10-04).
- A dev run while the installed app is open just forwards to it over the IPC socket. Set
  `XDG_RUNTIME_DIR` to a short private directory (sockets are capped at 108 chars) (2026-10-02).
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
  `logs/connection_log.txt` (last state `Logged On`, newer than the `steam` process). Windows read
  the registry value until 2026-10-06, when the owner found the check only worked on Linux; it now
  reads the same log under `SteamPath`. The pid Steam records is not trusted: Flatpak Steam writes its
  sandbox pid, and `~/.steam/steam.pid` is left stale after Steam exits. A Steam run as administrator
  may refuse its start time to Windows' `Process.StartTime`, so that counts as running, with any login
  in the log accepted.
- Windows draws the title bar light in dark mode unless each window sets `DWMWA_USE_IMMERSIVE_DARK_MODE`
  (2026-10-08). `native/app_icon.cpp` does it for every window as it's created, following the QML theme.
  The QML screenshots never show the title bar; CI checks it with `DwmGetWindowAttribute`.
- .NET can't marshal `string[]` as UTF-8 (`LPUTF8Str` isn't allowed as an `ArraySubType`). Pass
  `LPWStr` and take `const QChar*` natively (2026-09-28).

- On Linux `Environment.SetEnvironmentVariable` only changes .NET's managed copy; Qt reads the C
  environment with getenv and never sees it. So `QT_QPA_PLATFORMTHEME=xdgdesktopportal` was never
  applied, Qt.labs.platform had no file dialog ("No native FileDialog implementation available ...
  requires Qt Widgets"), and every file dialog silently did nothing on the bundled runtime.
  `QtRuntime.SetForQt` also calls libc `setenv` (2026-09-30). `QT_QUICK_CONTROLS_MATERIAL_VARIANT=Dense`
  in `Program.cs` has the same problem and is still not applied; switching it on would change the
  look. Qt 5.15.1's portal dialog sends no suggested file name, so the export dialog starts empty.
- A Unix socket path may be at most 108 characters. Screenshot runs with every XDG folder pointed at
  Claude's long scratchpad path crash in `SingleInstance.Listen`; keep `XDG_RUNTIME_DIR` short
  (2026-09-30).
- `grabToImage` on a window's `contentItem` fails with "item has no QML engine"; grab an item declared
  in QML instead (the console window has `screenshotRoot`) (2026-09-30).

- Valheim's save and server formats are best read in the game itself: `ilspycmd` (a dotnet tool,
  install it into a scratch folder) on `valheim_Data/Managed/assembly_valheim.dll` and
  `assembly_utils.dll`. `FejdStartup` has the command-line options, `SaveSystem`/`SaveCollection` the
  save rules, `LocalServerList` the server list layout, `PlatformPrefs` the prefs (plain Unity
  PlayerPrefs on Steam) (2026-10-04).
- Current Valheim saves a world as a folder `<Name>/_main.<n>.fwl2` plus chunk files, next to older
  `<Name>.fwl`/`.db` pairs. A same-named folder in `worlds_local` that only holds `cacheMinimap*` files
  is a minimap cache, not a world (2026-10-04).
- A Qt Quick `Popup` (every `LhDialog`) always renders in the window's overlay, whatever its `parent`,
  and neither the overlay nor the root item can be grabbed ("item has no QML engine"). The screenshot
  mode never shows dialogs. To check one, grab an item declared inside the dialog in QML for a one-off
  run (2026-10-04).
- Current Steam's `config/loginusers.vdf` no longer marks `MostRecent`; the newest `Timestamp` is the
  account that logged in last (2026-10-04).

- The Android SDK lives in `~/.local/share/android-sdk` on the owner's machine (cmdline-tools,
  platforms 36 and 37, an `lh-test` AVD). OkHttp 5.5 needs compileSdk 37; AGP 9 compiles Kotlin itself
  and takes the Kotlin version from the Compose compiler plugin declared in the root build (2026-10-05).
- A list from the phone lacks the dependencies LaunchHeim pulled in itself (a Nexus mod's
  requirements); `PackService.Compare` first removed them as "dropped". It now keeps a dependency
  while a mod the pack keeps needs it (2026-10-05).
- Qml.Net hands an empty QML string to .NET as `null`, not `""`. `Send to phone` with no instance
  ticked (an empty library) crashed 0.5.0 on `instanceIds.Split`; an `async void` method called from
  QML takes the whole app down on any exception before its `try`. Treat every string parameter from
  QML as nullable (2026-10-05).
- Windows reserves blocks of TCP ports at boot (Hyper-V, WSL, Docker; `netsh int ipv4 show
  excludedportrange protocol=tcp`), and binding one fails with WSAEACCES "access forbidden", not "in
  use". Fixed test ports (53510+) failed the Windows CI intermittently, and a block can cover 53317 on
  a user's PC. Tests bind port 0, and both MiniHttpServers fall back to any free port (2026-10-05).
- A Qt Quick Popup has no size in the offscreen screenshot mode ("grabToImage: item has invalid
  dimensions"), even open. To look at a dialog, copy its content into a plain Rectangle in a one-off
  copy of the built `qml/` folder and grab that; a `-p:Version=0.4.0` build makes the update dialog
  appear against the real API, and `LaunchHeim.DistroPackage` in its runtimeconfig.json pretends a
  package install (2026-10-06).
- The emulator sits behind NAT, so LocalSend discovery can't reach it. `adb forward tcp:53630 tcp:53317`
  makes its server reachable from the PC, and the host is `10.0.2.2` from inside, which is how the
  transfer, the accept dialog and A2S were checked there (2026-10-05).
- `pkill -f <pattern>` matches the shell running it when the pattern is part of the same command line,
  and kills that command (2026-10-05).

## Open
- Code signing (2026-10-08): signs fine in CI (secrets set, PR #29 run 37808678350). Not yet looked
  at on a real Windows PC: the Digital Signatures tab and the SmartScreen prompt for the signed zip.
- Phone sync on Windows (2026-10-09): the firewall rule (setup's netsh, Settings' elevated PowerShell)
  is untested on a real PC; CI only builds it.
- LaunchHeim Companion (2026-10-05): tried between the owner's phone and the desktop on 2026-10-09
  (works once ufw allows it); Android 14+ background behaviour still unchecked. Checked on an
  emulator through adb forwarding and with C#↔Kotlin tests on localhost. CurseForge on the phone is
  only covered by parsing tests (no key on the test machine). Also open: the release key secrets (a
  tag fails without them since 2026-10-05), registering with Google's developer verification before
  it's enforced globally in 2027, IzzyOnDroid, adding a device by address when discovery fails, and
  Android 17's local network permission before targetSdk 37.
- Direct play hasn't been tried in a real game yet (2026-10-04): joining with `+connect` and a saved
  password, the preselected character and world, and the Windows registry prefs. A helper plugin
  could make world launches fully direct for modded instances
  ([[0010-launchheim-direct-play-uses-the-games-own-options]]).
- A Windows installer (Inno Setup, MSIX or winget), and live theme switching on Windows.
- Moving Linux to the same patched qmlnet-native build as Windows would drop `signal_fix.cpp`, the
  g++/qt5-base build dependencies and the RPATH patch.
- Only Thunderstore mods are checked for updates (Nexus and CurseForge would cost one API call per mod).
- The Desktop project has no tests. The UI is checked with the `LAUNCHHEIM_SCREENSHOT` mode.
- Modpack follow-ups: publishing a pack to Thunderstore as a modpack package (needs a 256 px icon and a
  README), and r2modman's profile codes (Thunderstore's legacy profile API).
- The modpack export/import and console need a check against a real r2modman import and a real
  Valheim session; only unit tests and screenshots covered them on 2026-09-30.
- BepInEx's console toggle is Windows-only in the UI and untested on Windows.
