# Packaging LaunchHeim

LaunchHeim installs as a normal system package on Arch (pacman), Debian and Ubuntu (apt) and
Fedora/RHEL (dnf). Every package puts the same files in the same places (`stage.sh` defines them):

| Path | What |
|---|---|
| `/usr/lib/launchheim/` (`/usr/lib64/` on Fedora/RHEL) | the app |
| `/usr/bin/launchheim` | symlink to start it from a terminal |
| `/usr/share/applications/launchheim.desktop` | launcher entry, also handles `nxm://` links |
| `/usr/share/icons/hicolor/scalable/apps/launchheim.svg` | icon |
| `/usr/share/metainfo/io.github.codeisnotevil.LaunchHeim.metainfo.xml` | listing in Discover / GNOME Software |

Your instances, settings and caches stay in `~/.local/share/LaunchHeim`, `~/.config/LaunchHeim` and
`~/.cache/LaunchHeim`. No package manager touches them, so removing or upgrading the package never
costs a modpack.

**Moving from `install.sh`?** Remove the per-user install first with `../install.sh --uninstall`.
Otherwise its launcher entry in `~/.local/share/applications` hides the packaged one.

## Arch Linux / CachyOS (pacman)

`arch/PKGBUILD` builds `launchheim-git` from this repository. It's a `-git` package because LaunchHeim
has no tagged releases. The version is the app version plus the commit (`0.1.0.r47.g315cedb`), so
pacman sees every new commit as an upgrade.

### Install

```fish
cd Applications/LaunchHeim/packaging/arch
makepkg -si        # -s installs the build dependencies, -i installs the package
```

makepkg clones the repository from GitHub, builds, runs the Core tests (`check()`) and hands the package
to `pacman -U`. The dependencies all come from the official repos:

- **Runtime:** `dotnet-runtime` (10), `qt5-base`, `qt5-declarative`, `qt5-quickcontrols2`, `qt5-svg`,
  `qt5-wayland`, `hicolor-icon-theme`, `xdg-utils`
- **Build only:** `dotnet-sdk` (10), `git`, `gcc` (`base-devel`)

To build a branch or your local checkout instead of GitHub's `main`:

```fish
env LAUNCHHEIM_GIT="file://$HOME/repos/MyMonoRepo#branch=launchheim-valheim-launcher" makepkg -si
```

Once installed, it shows up in the application launcher, and `nxm://` links from Nexus open it. There's
nothing to register by hand.

### Everyday management

| Task | Command |
|---|---|
| Upgrade after new commits | `makepkg -si` again in `packaging/arch` (it pulls, rebuilds and upgrades) |
| Show what's installed | `pacman -Qi launchheim-git` |
| List its files | `pacman -Ql launchheim-git` |
| Check the files are intact | `pacman -Qkk launchheim-git` |
| Which package owns a file | `pacman -Qo /usr/bin/launchheim` |
| Remove it | `sudo pacman -Rns launchheim-git` (`-s` also removes Qt 5 and .NET if nothing else needs them) |
| Also remove your data | `rm -rf ~/.local/share/LaunchHeim ~/.config/LaunchHeim ~/.cache/LaunchHeim` |
| Clean the build folder | `git clean -fdX packaging/arch` (`src/`, `pkg/`, the clone and old packages) |

`pacman -Syu` doesn't upgrade it by itself, because it isn't in a repository. That's normal for
self-built and AUR packages. An AUR helper (`paru`, `yay`) could handle it once it's published on the
AUR. `paru -S --devel` then catches new commits. Publishing needs a `.SRCINFO`
(`makepkg --printsrcinfo > .SRCINFO`).

### A local pacman repository (optional)

To have `pacman -Syu` pick up new builds, put them into a local repository once:

```fish
mkdir -p ~/.local/share/pacman-local
cp launchheim-git-*.pkg.tar.zst ~/.local/share/pacman-local/
repo-add ~/.local/share/pacman-local/local.db.tar.zst ~/.local/share/pacman-local/launchheim-git-*.pkg.tar.zst
```

Then add this to `/etc/pacman.conf`:

```ini
[local]
SigLevel = Optional TrustAll
Server = file:///home/<you>/.local/share/pacman-local
```

After that, `makepkg` plus `repo-add` publishes a new build, and `pacman -Syu` installs it.

## Debian 13 / Ubuntu 24.04 and newer (apt)

```sh
sudo apt install ./launchheim_0.1.0-1_amd64.deb     # resolves Qt 5 from the distribution
sudo apt remove launchheim                           # or purge; your data stays in ~ either way
dpkg -L launchheim                                   # list its files
```

Debian doesn't package .NET 10, so this package **includes the .NET runtime** (27 MB download,
86 MB installed). Qt 5 still comes from the distribution.

## Fedora / RHEL / AlmaLinux / Rocky (dnf)

```sh
sudo dnf install ./launchheim-0.1.0-1.x86_64.rpm    # pulls dotnet-runtime-10.0 and Qt 5 from the repos
sudo dnf remove launchheim
rpm -ql launchheim
```

- **Fedora 44 and RHEL 9:** everything is in the base repositories.
- **RHEL 10:** Qt 5 is gone from AppStream, so enable EPEL first (`sudo dnf install epel-release`).

## Windows 10 / 11

There's no installer yet. The Windows build is a portable folder:

1. Download the `LaunchHeim-win-x64` artifact of the latest *LaunchHeim Windows* workflow run (GitHub
   → Actions) and unzip `LaunchHeim-<version>-win-x64.zip` anywhere, for example
   `%LOCALAPPDATA%\Programs\LaunchHeim`.
2. Start `LaunchHeim.exe`. .NET, Qt and the Visual C++ runtime are included.
3. Settings → *Handle "Mod Manager Download" links* → Register, so Nexus links open LaunchHeim.
4. To update, replace the folder. Instances live in `%LOCALAPPDATA%\LaunchHeim` and are kept. To
   uninstall, delete the folder and the `HKCU\Software\Classes\nxm` registry key.

It's built by `windows/build.ps1` (see the app README's Windows section), which needs MSVC, so it can't
be built from Linux.

## Building the packages

```fish
cd Applications/LaunchHeim
packaging/build-packages.sh          # .deb and .rpm into packaging/dist/ (or pass deb / rpm)
packaging/test-packages.sh           # install them in clean containers and take screenshots
```

`build-packages.sh` publishes the app on this machine and then runs `dpkg-deb` and `rpmbuild` inside
Debian and Fedora containers (podman, or `CONTAINER_ENGINE=docker`), so neither tool has to be
installed. `test-packages.sh` installs each package with apt or dnf in a clean Debian 13, Ubuntu
24.04, Fedora 44, AlmaLinux 9 and AlmaLinux 10 container, starts LaunchHeim offscreen, and saves a
screenshot of the Settings page to `packaging/dist/test-*.png`. It fails if the app downloads its own
Qt or writes a desktop entry into the home folder.

To test the Arch package the same way, run `makepkg` in a `docker.io/library/archlinux` container as a
non-root user. That's how it was verified. `namcap` then reports only the expected noise (it can't map
QML imports to packages, and it can't see libraries that are loaded at runtime rather than linked).

## Why the packages look like this

- **System Qt instead of the Qml.Net download.** Packaged builds are published with
  `-p:LaunchHeimDistroPackage=true`. That writes a switch into `LaunchHeim.runtimeconfig.json`
  (`src/Desktop/Hosting/DistroPackage.cs`), and then the app uses the distribution's Qt 5.15 and never
  downloads 60 MB into the home folder. libQmlNet only uses public Qt 5.15 API plus
  `QMetaObjectBuilder`, which hasn't changed across 5.15.x. It's verified against 5.15.9 (RHEL 9)
  through 5.15.19 (Arch). `LAUNCHHEIM_QT=bundled` still forces the download.
- **The package owns the desktop entry.** With the switch set, "Register" in Settings (and
  `launchheim --register-desktop`) only makes LaunchHeim the default `nxm://` handler. It doesn't write a
  second entry into `~/.local/share/applications` that would outlive an uninstall.
- **Framework-dependent where the distro has .NET 10** (Arch, Fedora, RHEL): 6 MB, and .NET security
  updates arrive through the package manager. **Self-contained on Debian/Ubuntu**, because there's no
  .NET 10 there without Microsoft's apt repository.
- **One rpm for all Fedora and RHEL releases.** The build uses Microsoft's portable runtime pack (glibc
  2.27 or newer), and the signal fix only needs baseline glibc symbols, so `%{dist}` is left empty.
- **libQmlNet's RPATH is neutralized at build time.** Qml.Net ships it with the RPATH of its CI machine
  (`/home/travis/...`). The loader would search that folder before the system libraries, so whoever
  could create it could inject a Qt library. The build overwrites it with `$ORIGIN`
  (`LaunchHeim.Desktop.csproj`). rpmbuild refuses the unpatched file.

## License

LaunchHeim is MIT-licensed (`../LICENSE`). Each package installs the text where its distribution looks
for it: `/usr/share/licenses/launchheim-git/LICENSE` (Arch), `/usr/share/doc/launchheim/copyright`
(Debian), `/usr/share/licenses/launchheim/LICENSE` (rpm, via `%license`), and `LICENSE.txt` next to
`LaunchHeim.exe` on Windows. The metainfo says `MIT` too.
