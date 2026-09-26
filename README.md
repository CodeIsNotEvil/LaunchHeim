# LaunchHeim

A Valheim mod launcher for Linux desktops (built on CachyOS with KDE Plasma 6). It manages
**instances**: self-contained modded setups, each with its own BepInEx, plugins and configs. It finds
and installs mods from Thunderstore, Nexus Mods and CurseForge, and starts Valheim with one of them,
or vanilla. The game folder is never modified.

.NET 10 with a Qt Quick (QML) front end hosted through [Qml.Net](https://github.com/qmlnet/qmlnet).

## Install

```fish
./install.sh
```

The script publishes a Release build to `~/.local/opt/LaunchHeim` (override with `LAUNCHHEIM_PREFIX`),
links `~/.local/bin/launchheim` and registers `launchheim.desktop`, which adds the app to the
launcher and makes it the `nxm://` handler. Run it again to update. Instances and settings are kept.

The build needs `dotnet`, `g++` and the Qt 5 headers from `qt5-base`. See [Qt runtime](#qt-runtime).

## Develop

```fish
dotnet build
dotnet test                                     # Core tests (xUnit); the UI has none
dotnet run --project src/Desktop

# render one page to a PNG and quit, handy for checking QML changes without clicking around
set -x LAUNCHHEIM_SCREENSHOT /tmp/shot.png
set -x LAUNCHHEIM_SCREENSHOT_PAGE browse         # library | browse | settings
dotnet run --project src/Desktop
```

QML is loaded from disk next to the binary (`bin/.../qml`), so a QML edit only needs `dotnet build`,
not a C# change.

## Layout

| Project | Holds |
|---|---|
| `src/Core` | Everything testable, with no UI: Steam/Valheim discovery (`Game/`), instances, the mod installer and dependency resolution (`Mods/`), the three catalogs (`Catalogs/`), downloads and XDG storage. |
| `src/Desktop` | The Qml.Net host, view models, QML pages and components, the Plasma colour scheme reader and desktop integration (single instance, `nxm://`). |
| `tests/Core.Tests` | xUnit tests for Core. |

The namespaces sit under `CINE.` (set in `Directory.Build.props`), like every .NET app in the repo.

## How it works

**Files.** Following the XDG base directory spec: instances in `~/.local/share/LaunchHeim/instances`,
settings in `~/.config/LaunchHeim/settings.json`, and the Thunderstore index and downloads in
`~/.cache/LaunchHeim`. Clearing the cache never costs a modpack.

**Finding the game.** `SteamLibraryLocator` reads Steam's `libraryfolders.vdf` and the Valheim app
manifest, so libraries on other drives (such as `/mnt/games/SteamLibrary`) and Flatpak Steam are found.
The path can also be set in Settings.

**Launching.** `LaunchPlan` does what BepInExPack's `start_game_bepinex.sh` does: it preloads Unity
Doorstop and points it at the *instance's* BepInEx preloader. BepInEx derives its root from that path,
so plugins, configs and logs all come from the instance. The game is started directly with `SteamAppId`
set rather than through `steam -applaunch`, because Steam launch options can't change per launch. The
Qt variables LaunchHeim sets on itself are removed from the game's environment.

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

**Single instance.** A second start, usually the browser handing over an `nxm://` link, forwards its
arguments over a Unix socket in `$XDG_RUNTIME_DIR` and exits.

**Theme.** The colours come from the active Plasma colour scheme (`~/.config/kdeglobals`), including the
accent colour, and change live when it changes. Breeze Dark is the fallback outside Plasma.

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
