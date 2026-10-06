# LaunchHeim

A Valheim mod launcher for Linux (built on CachyOS with KDE Plasma 6) and Windows. It manages
**instances**: self-contained modded setups, each with its own BepInEx, plugins and configs. It finds
and installs mods from Thunderstore, Nexus Mods and CurseForge, and starts Valheim with one of them,
or vanilla, optionally straight into one of your servers or worlds. On Linux the game folder is never modified. On Windows it gets Doorstop's `winhttp.dll`
(see [Windows](#windows)).

.NET 10 with a Qt Quick (QML) front end hosted through [Qml.Net](https://github.com/qmlnet/qmlnet).
MIT-licensed, see [`LICENSE`](LICENSE). It bundles third-party software, including Qt under the LGPL v3;
see [Licenses](#licenses).

## Install

| System | How |
|---|---|
| Arch, CachyOS | the release package (download it, then `sudo pacman -U ./<file>`), or `makepkg -si` in `packaging/arch` (pacman package `launchheim-git`) |
| Debian 13, Ubuntu 24.04+ | `sudo apt install ./launchheim_*_amd64.deb` from `packaging/build-packages.sh` |
| Fedora, RHEL 9/10 | `sudo dnf install ./launchheim-*.x86_64.rpm` (RHEL 10 needs EPEL) |
| Any Linux, per user | `./install.sh` (and `./install.sh --uninstall`) |
| Windows 10/11 | unzip `LaunchHeim-*-win-x64.zip` from the *LaunchHeim Windows* workflow and run `LaunchHeim.exe` |

[`packaging/README.md`](packaging/README.md) covers each of these: dependencies, upgrading, removing,
and how the packages are built and tested.

`install.sh` publishes a Release build to `~/.local/opt/LaunchHeim` (override with `LAUNCHHEIM_PREFIX`),
links `~/.local/bin/launchheim` and registers `launchheim.desktop`, which adds the app to the
launcher and makes it the `nxm://` handler. Run it again to update. Instances and settings are kept.
It needs `dotnet`, `g++` and the Qt 5 headers from `qt5-base`. See [Qt runtime](#qt-runtime).

## Develop

```fish
dotnet build
dotnet test                                     # Core tests (xUnit); the UI has none
dotnet run --project src/Desktop
cd android && ./gradlew :app:testDebugUnitTest :app:assembleDebug   # the Android app, see android/README.md

# render one page to a PNG and quit, handy for checking QML changes without clicking around
set -x LAUNCHHEIM_SCREENSHOT /tmp/shot.png
set -x LAUNCHHEIM_SCREENSHOT_PAGE browse         # library | play | instance | browse | settings | console
dotnet run --project src/Desktop
```

QML is loaded from disk next to the binary (`bin/.../qml`), so a QML edit only needs `dotnet build`,
not a C# change.

## Layout

| Project | Holds |
|---|---|
| `src/Core` | Everything testable, with no UI: Steam/Valheim discovery (`Game/`), Valheim's own characters, worlds, servers and prefs (`Saves/`), instances, the mod installer and dependency resolution (`Mods/`), modpack export and import (`Packs/`), the three catalogs (`Catalogs/`), logging and log following (`Logging/`), downloads and XDG storage. |
| `src/Desktop` | The Qml.Net host, view models, QML pages and components, the Plasma (or Windows) colour scheme and desktop integration (single instance, `nxm://`). |
| `packaging/` | The pacman, deb, rpm and Windows builds. See [`packaging/README.md`](packaging/README.md). |
| `android/` | LaunchHeim Companion, the Android app (Kotlin, Compose). See [`android/README.md`](android/README.md). |
| `tests/Core.Tests` | xUnit tests for Core. |

The namespaces sit under `CINE.` (set in `Directory.Build.props`), like every .NET app in the repo.

## How it works

**Files.** Following the XDG base directory spec: instances in `~/.local/share/LaunchHeim/instances`,
settings in `~/.config/LaunchHeim/settings.json`, the Thunderstore index and downloads in
`~/.cache/LaunchHeim`, and LaunchHeim's own log in `~/.local/state/LaunchHeim/launchheim.log`. Clearing
the cache never costs a modpack.

**Finding the game.** `SteamLibraryLocator` reads Steam's `libraryfolders.vdf` and the Valheim app
manifest, so libraries on other drives (such as `/mnt/games/SteamLibrary`) and Flatpak Steam are found.
The path can also be set in Settings.

**Launching.** `LaunchPlan` does what BepInExPack's `start_game_bepinex.sh` does: it preloads Unity
Doorstop and points it at the *instance's* BepInEx preloader. BepInEx derives its root from that path,
so plugins, configs and logs all come from the instance. The game is started directly with `SteamAppId`
set rather than through `steam -applaunch`, because Steam launch options can't change per launch. The
Qt variables LaunchHeim sets on itself are removed from the game's environment.

**Steam first.** Started without a logged-in Steam client, Valheim only shows a black window and never
an error. So before every launch `SteamClient` checks that a `steam` process runs and that a user is
logged in: the last state in Steam's `logs/connection_log.txt` has to be `Logged On` and newer than
the running `steam` process. On Linux the native and the Flatpak log are checked; on Windows the log in
the Steam folder the registry's `SteamPath` names (or `Program Files (x86)\Steam`). Steam's
`ActiveProcess/ActiveUser` registry value isn't used: Linux Steam no longer writes it to
`registry.vdf`, and on Windows it didn't follow the login either. If Steam isn't running, LaunchHeim
starts it with `-silent` (`steam` on `PATH`, then
`flatpak run com.valvesoftware.Steam`, and on Windows the `SteamExe` from the registry). It then waits
up to three minutes for the login, plus a few seconds for the Steam API to settle, and the sidebar
shows "Starting Steam" or "Waiting for Steam" meanwhile. After the timeout the launch is cancelled with
an error.

**Servers and worlds.** The Play page lists what Valheim manages itself, read with the game's own
rules (`Core/Saves`, taken from `SaveSystem` and `LocalServerList` in `assembly_valheim.dll`):
characters and worlds from Steam Cloud (`userdata/<account>/892970/remote` of the Steam account that
logged in last), the local folders (`characters_local`, `worlds_local`) and the pre-cloud ones, both
world formats (`<Name>.fwl` and the newer `<Name>/_main.<n>.fwl2` folders), and the dedicated
servers from the game's Favorites and Recent lists. Backups and Steam-friend or crossplay entries,
which have no address, are left out. Nothing there is ever changed. For each server or world the
user picks a character and a setup (vanilla or an instance), and LaunchHeim remembers both, plus a
server's password, in `settings.json` (`playChoices`), so the next Play needs no choices. Clicking
a row's tile sets a picture for it (PNG, JPEG, WebP, GIF or BMP), which is copied into
`play-images/` in the data folder and named in `settings.json` (`playImages`); each copy gets a new
random name because QML's `Image` caches by URL and would keep showing a replaced picture.

**Play again from the sidebar.** The Play button on the sidebar's status card starts whatever was
started last (`lastPlay` in `settings.json`): a server or world with its remembered character, setup
and password, an instance, or vanilla before anything was played. The card names it, destination
first because it elides the end. If the server or world is gone from Valheim's lists, or its character
or instance is, the button opens the Play page instead.

**Players online.** Each server row shows how many players are on it ("2/10 online", or "Offline" when
it doesn't answer), asked with Steam's server query (A2S, `Core/Saves/ServerQuery`) on the query port,
the game port + 1. The servers are asked when the page loads and every 30 seconds while it is shown.
Player names aren't available: Valheim's server lists its players to Steam with empty names (checked
against a live server), so the hover tooltip only shows names a server shares some other way.

What a launch can do there is up to the game. A server is joined with `+connect host:port`, the
argument Steam's "Join game" uses, and `-password` answers the password prompt. Valheim then opens
at the character selection, and Start joins. Before the start LaunchHeim sets the game's PlayerPrefs
`profile` (and `world` for a world), which its menus select: the `prefs` XML file in the game's data
folder on Linux (strings in base64, edited as text so nothing else changes) and
`HKCU\Software\IronGate\Valheim` on Windows (`<key>_h<djb2 hash>`, binary UTF-8). Valheim can't
load a world from the command line, so a world opens with its character and world selected and the
player clicks Start. The password never reaches LaunchHeim's log. Valheim's
`-joinserverwithcharacter` would skip the character selection too, but it runs before Steam is up and
only sees characters kept off the cloud, so it isn't used.

**Installing mods.** The file layout follows r2modman's BepInEx rules: BepInExPack goes into the
instance root, and each plugin gets its own `BepInEx/plugins/<Mod>/` folder. Configs go into
`BepInEx/config/` and an existing config is never overwritten. Every placed file is recorded, so
uninstalling removes exactly those files. Dependencies are resolved to the newest version, not the exact
one listed, so two mods asking for different versions of a library both work. Removing a mod also
removes dependencies that were only pulled in for it. Disabling renames its assemblies to
`*.dll.disabled`. BepInExPack is added to any instance that lacks it.

**Catalogs.**
- *Thunderstore* has no search API for third-party apps, so the whole Valheim package list is
  downloaded (about 17 MB compressed), trimmed to a few MB, cached for 6 hours and searched in memory.
  A stale copy is used offline. Updates are only checked for Thunderstore mods, because that is free.
- *Nexus Mods* browsing works anonymously through the GraphQL API. Downloading needs a personal API
  key. Even then, only Premium accounts get direct links. Free accounts click "Mod Manager Download"
  on the website, which opens an `nxm://` link that LaunchHeim receives.
- *CurseForge* needs an API key from console.curseforge.com for everything, including search. Files
  whose authors opted out of third-party distribution open the website instead.

**Importing.** "Import game folder" turns a BepInEx install made directly in the game folder into an
instance. It copies the game folder's files and never changes them. Plugins with a Thunderstore
`manifest.json` are recognised so they can be updated later. "Install from file" on an instance takes
a mod downloaded by hand (a Thunderstore zip, an r2modman export or a plain dll). A zip's `manifest.json`
gives the mod its name and dependencies.

**Modpacks.** An instance exports as an `.r2z` file (instance page → Export) and a pack imports as
a new instance (Library → Import modpack). The file is an r2modman profile export: a zip with
`export.r2x` (the Thunderstore mods and their versions, in r2modman's YAML) and the configs under
`BepInEx/config/`. r2modman and the Thunderstore Mod Manager import it, and LaunchHeim imports theirs.
LaunchHeim adds `launchheim.json`, which r2modman ignores, for what its format can't hold: Nexus and
CurseForge mods by id and file id, which mods were only pulled in as dependencies, and the launch
arguments. Mods are listed, not packed, and downloaded again on import at the exact version, which
keeps packs small and respects that Nexus and CurseForge files may not be passed on. Only local mods
travel as files. On import every mod is resolved first and installed in dependency order, so the
versions the pack pins win over "newest dependency". A version that's gone gets the newest one with
a warning, and mods a site only hands out through its website (Nexus without Premium, CurseForge
opt-outs) are listed to install from Browse mods. The pack's configs are copied last, over the mods'
defaults.

**Phone sync.** [LaunchHeim Companion](android/README.md), the Android app, keeps mod lists on the
phone, browses Thunderstore, Nexus and CurseForge and shows who's online on the servers. Its APK is
released with every `launchheim-v*` tag. LaunchHeim talks to it with the
LocalSend protocol (`Core/LocalSend`): Library → Send to phone (or an instance's Send to phone) finds
devices on the LAN and sends the chosen instances as `.r2z` packs plus Valheim's server list as
`launchheim-servers.json`. Receiving is off until Settings → Phone sync switches it on, since it opens
a port (53317, or the next free one when the LocalSend app has it). Every transfer is accepted in a
dialog, and only `.r2z` files are taken. A pack carries the id of the instance it was exported from
(`launchheim.json` `instanceId`), so one that comes back edited is compared with that instance and the
changes are shown: Update instance removes, installs (at the pack's versions) and switches mods, and
leaves configs and launch options alone; a dependency the list doesn't name stays while a mod in it
needs it. Any other pack is imported as a new instance. The HTTP server
is a small one on `TcpListener`, because `HttpListener` needs administrator rights on Windows for
anything but localhost.

**Console and logs.** The console window (the terminal button on an instance, or in the sidebar while
the game runs) follows one of three logs live: the instance's BepInEx `LogOutput.log`, Unity's
`Player.log` (`~/.config/unity3d/IronGate/Valheim`, or `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim`)
and LaunchHeim's own log. Errors and warnings are coloured and counted, lines can be filtered, and
the shown lines copied for a bug report. It follows the files rather than piping the game's stdout,
because a pipe ties the game to the launcher: closing LaunchHeim would leave Valheim writing into a
pipe nobody reads. A file that got shorter or has a new first line is a new session. LaunchHeim's log
records every launch (executable, arguments and the Doorstop environment), Steam's state, installs,
imports and every error toast; it moves to `launchheim.log.1` once it passes 2 MB. On Windows the
instance's launch options can also switch on BepInEx's own console window (`[Logging.Console]` in
`BepInEx.cfg`). On Linux that console writes to the game's stdout, which nobody sees, so the option
is hidden there.

**Single instance.** A second start, usually the browser handing over an `nxm://` link, forwards its
arguments over a Unix socket in `$XDG_RUNTIME_DIR` and exits.

**Theme.** The window, text and card colours come from the active Plasma colour scheme
(`~/.config/kdeglobals`), and change live when it changes. Breeze Dark is the fallback outside Plasma.
The accent is always LaunchHeim's own orange `#DE5833`, and the font is always Kode Mono (SIL OFL 1.1,
shipped in `qml/fonts`), so the app matches its icon and the repo's other logos. Qt 5 can't read
woff2 or pick weights from a variable font, so `Scripts/kodemono_static.py` cuts static Regular and
Bold TTFs out of the variable font GroceryTracker ships. The logos come from `Scripts/text_logo.py`
as SVG, and the `.ico` and the PNGs in `packaging/icons` (16 to 256 px) are rendered from that SVG.

**Window icon.** Qml.Net can't set it, so `native/app_icon.cpp` (called from `Hosting/AppIcon.cs`)
does two things before the window opens. It sets the window icon from `packaging/icons`, which X11 and
Windows show in the title bar and taskbar. The `.exe` icon alone only reaches Explorer, because Qt looks
for a resource named `IDI_ICON1` and .NET stores `<ApplicationIcon>` under a number. It also sets the
desktop file name to `launchheim`. Qt 5 can't hand an icon to a Wayland compositor, so Plasma takes it
from the desktop entry named by the window's app_id, and without this Qt calls the window
`local.cine.LaunchHeim`. On Wayland a dev build therefore only shows the icon once `launchheim.desktop`
is installed (a package, `install.sh` or `--register-desktop`).

## Qt runtime

Qml.Net 0.11 (2020) is the last release and is built against Qt 5.15. Plasma 6 systems only ship parts
of Qt 5 (Arch has no `qt5-quickcontrols2` by default). So on first start the matching Qt runtime that
Qml.Net publishes is downloaded once into `~/.qmlnet-qt-runtimes` (about 60 MB). Set
`LAUNCHHEIM_QT=system` to use the distribution's Qt 5 instead, which needs `qt5-base`, `qt5-declarative`,
`qt5-quickcontrols2` and `qt5-wayland`.

Three workarounds keep Qml.Net working on a current system:

1. **Tar extraction** (`Hosting/QtRuntime.cs`). Its extractor assumes a read fills the buffer, and
   since .NET 6 `GZipStream` may return less ("Couldn't ready block"). `System.Formats.Tar` replaces it.
2. **`libdl.so`** (`Hosting/QtRuntime.cs`). glibc 2.34 folded libdl into libc and only keeps
   `libdl.so.2`, so the unversioned name its native loader asks for no longer exists.
3. **Signals** (`native/signal_fix.cpp`, `Hosting/QmlNetSignalFix.cs`). Qml.Net raises a signal on only
   the first of an object's QML wrappers, so other bindings show stale values. A small native library,
   built against the Qt 5 headers during `dotnet build`, replaces that function and notifies all of them.

The view model is registered as a QML singleton (`import LaunchHeim 1.0`, `App`), not a context
property. Context-property objects get JavaScript ownership, and the GC eventually deletes them.

## Windows

The same QML UI runs on Windows. The differences:

- **Qml.Net's native library is built from source.** On Windows `QmlNet.dll` doesn't export the
  functions the signal fix calls, so `packaging/windows/build.ps1` compiles qmlnet-native (the commit
  Qml.Net 0.11.0 was built from) with the same change as a patch. It ships Qt 5.15.2 and the Visual C++
  runtime next to `LaunchHeim.exe`, so nothing needs installing or downloading. At startup the app
  checks that the patch is present and logs an error if it isn't. `native/app_icon.cpp` (the window
  icon) is compiled there with MSVC as well, so a plain `dotnet build` on Windows has no window icon.
- **Launching.** Windows only loads Doorstop's `winhttp.dll` proxy from the game folder. Before the
  first modded launch LaunchHeim copies it there, with a `doorstop_config.ini` that keeps it
  **disabled**. Every modded launch then enables it and points it at the instance on the command line
  (`--doorstop-target-assembly`), so starting Valheim from Steam stays vanilla. r2modman does the same.
  An existing manual BepInEx install in the game folder is left alone. To remove LaunchHeim's trace,
  delete those two files.
- **Folders.** Instances and caches go in `%LOCALAPPDATA%\LaunchHeim`, settings in
  `%APPDATA%\LaunchHeim`. Steam is found through the registry, then `libraryfolders.vdf` as on Linux.
- **nxm://** is registered per user under `HKCU\Software\Classes\nxm` (Settings → Register). No
  administrator rights are needed.
- **Theme.** Breeze Light or Dark to match Windows' app mode, with LaunchHeim's orange accent and Kode Mono. It's read
  at start.

Building needs Visual Studio 2022 with C++, Qt 5.15.2 `msvc2019_64`, the .NET 10 SDK and git:
`packaging\windows\build.ps1 -QtDir C:\Qt\5.15.2\msvc2019_64` (add `-Smoke` to take screenshots). The
GitHub workflow `.github/workflows/launchheim-windows.yml` runs the same script on every change and
keeps the zip as an artifact. It also runs the Core tests on Windows.

## Licenses

LaunchHeim is MIT. What it's built on keeps its own license, and every build carries the notices:
[`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt) and the full texts in [`licenses/`](licenses)
are copied next to the app by the csproj, so the Windows zip, the three Linux packages and
`install.sh` all contain them. Settings → About shows the Qt notice and opens the file.

Two parts are **LGPL-3.0**, which is what drives most of this: Qt (only the Windows zip ships it) and
NetNativeLibLoader, a Qml.Net dependency that every build ships. The LGPL asks for a prominent notice,
the LGPL and GPL texts, directions to the source, and that users can swap in their own build. Both
are separate DLLs, so the last point holds as long as nothing merges or trims them (no single-file
or trimmed publish). The rest is MIT or BSD (notices only), plus the Microsoft Visual C++ runtime on
Windows, which is redistributable but not open source.

When a dependency changes:
- **NuGet package added or upgraded.** Update its section in `THIRD-PARTY-NOTICES.txt` and its text in
  `licenses/`, and check transitive packages (`dotnet list src/Desktop package --include-transitive`).
  That's how NetNativeLibLoader's LGPL turned up.
- **Qt version or shipped Qt modules changed** (`packaging/windows/build.ps1`). Run
  `packaging/qt-third-party-notices.py`, which regenerates `licenses/Qt-third-party.txt` from Qt's own
  `qt_attribution.json` files, and update the version and source link in the notices and on the
  download page (`site/src/download.html`).
