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

`arch/PKGBUILD` builds `launchheim-git` from this repository. It's a `-git` package because it builds
whatever commit it's given, not only release tags. The version is the app version plus the commit
(`0.1.0.r47.g315cedb`), so pacman sees every new commit as an upgrade. The releases ship the same
package, built at the tag.

### Install from a release

Each `launchheim-v*` release has the built package. The
[download page](https://codeisnotevil.github.io/MyMonoRepo/download.html#launchheim) shows these
commands with the current file name and its SHA-256 filled in:

```fish
curl -LO https://github.com/CodeIsNotEvil/MyMonoRepo/releases/download/launchheim-v0.3.0/launchheim-git-0.3.0.r106.g81bca63-1-x86_64.pkg.tar.zst
echo "<sha256>  launchheim-git-0.3.0.r106.g81bca63-1-x86_64.pkg.tar.zst" | sha256sum -c
sudo pacman -U ./launchheim-git-0.3.0.r106.g81bca63-1-x86_64.pkg.tar.zst
```

Download it first. `sudo pacman -U <url>` fails with a 404 on `<file>.sig`: for a package it downloads
itself, pacman applies `RemoteFileSigLevel`, which defaults to `Required`, and the release packages
aren't signed. A local file falls under `LocalFileSigLevel = Optional` instead, so the checksum is
what vouches for it. Don't pass the `release-assets.githubusercontent.com` link GitHub redirects to
either: pacman names the file after the whole query string and fails with "File name too long".
To update, do the same with the newer release.

### Build and install

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
| Upgrade to a new release | download, check and `sudo pacman -U ./<file>` as above |
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

1. Download `LaunchHeim-<version>-win-x64.zip` from the
   [download page](https://codeisnotevil.github.io/MyMonoRepo/download.html#launchheim) (or the
   `launchheim-v*` release on GitHub) and unzip it anywhere, for example
   `%LOCALAPPDATA%\Programs\LaunchHeim`. Between releases, every change's zip is an artifact of the
   *LaunchHeim Windows* workflow run.
2. Start `LaunchHeim.exe`. .NET, Qt and the Visual C++ runtime are included.
3. Settings → *Handle "Mod Manager Download" links* → Register, so Nexus links open LaunchHeim.
4. To update, replace the folder. Instances live in `%LOCALAPPDATA%\LaunchHeim` and are kept. To
   uninstall, delete the folder and the `HKCU\Software\Classes\nxm` registry key.

It's built by `windows/build.ps1` (see the app README's Windows section), which needs MSVC, so it can't
be built from Linux.

### Code signing

`LaunchHeim.exe`, `LaunchHeim.dll`, `LaunchHeim.Core.dll`, `QmlNet.dll` and `LaunchHeimAppIcon.dll`
carry an Authenticode signature from a **self-signed** certificate, so anyone can see who built them
and that nothing changed them since:

| | |
|---|---|
| Subject | `CN=CodeIsNotEvil` (RSA 4096, code signing only, not a CA) |
| Thumbprint | `284F5384B189984492629622EDC4A40646C5D8F6` |
| Valid | 2026-10-08 to 2036-10-05 |
| Public certificate | [`windows/launchheim-codesign.cer`](windows/launchheim-codesign.cer) |

Windows doesn't know the certificate, so SmartScreen and the file's properties still say the
publisher is unknown, and "Windows protected your PC" still needs *More info → Run anyway*. Only a
certificate from a commercial CA (or Microsoft's Trusted Signing) changes that. What it does give:

```powershell
# True for an untouched file from a LaunchHeim build (in the unzipped folder)
(Get-AuthenticodeSignature .\LaunchHeim\LaunchHeim.exe).SignerCertificate.Thumbprint -eq "284F5384B189984492629622EDC4A40646C5D8F6"
```

Explorer shows the same under *Properties → Digital Signatures → Details → View Certificate →
Details → Thumbprint*. A file someone modified reports `HashMismatch` instead. To make Windows
show "CodeIsNotEvil" as a verified publisher on your own PC, import the `.cer` into *Trusted Root
Certification Authorities* and *Trusted Publishers* (`certlm.msc`). It's marked as no CA, so it
can't vouch for anything but files it signed itself.

Only what `build.ps1` compiles is signed. Qt's DLLs and the Visual C++ runtime stay as their makers
shipped them, and .NET's files already have Microsoft's signature. Signatures are time stamped
(DigiCert, Sectigo as the fallback), so they stay valid after the certificate expires.

`build.ps1` signs when `LAUNCHHEIM_SIGNING_PFX` names the `.pfx` and `LAUNCHHEIM_SIGNING_PASSWORD`
holds its password. It refuses a `.pfx` whose certificate isn't the committed `.cer`, and checks
every signature afterwards. In CI the `.pfx` and its password are the secrets
`LAUNCHHEIM_WINDOWS_SIGNING_PFX` (base64) and `LAUNCHHEIM_WINDOWS_SIGNING_PASSWORD`. Builds without
them are unsigned, and a tagged release fails without them. The key was made once with OpenSSL:

```fish
openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -subj "/CN=CodeIsNotEvil" \
  -addext "basicConstraints=critical,CA:FALSE" -addext "keyUsage=critical,digitalSignature" \
  -addext "extendedKeyUsage=critical,codeSigning" -addext "subjectKeyIdentifier=hash" \
  -keyout key.pem -out launchheim-codesign.cer
openssl pkcs12 -export -name "LaunchHeim code signing" -inkey key.pem -in launchheim-codesign.cer -out launchheim-codesign.pfx
base64 -w0 launchheim-codesign.pfx | gh secret set LAUNCHHEIM_WINDOWS_SIGNING_PFX
gh secret set LAUNCHHEIM_WINDOWS_SIGNING_PASSWORD
```

It's a separate key from the Android companion's APK key: losing one shouldn't cost the other.
A new key means a new thumbprint, so replace `windows/launchheim-codesign.cer` and the thumbprint
here, on the download page (`site/src/download.html`) and in the release notes
(`launchheim-release.yml`).

## Building the packages

```fish
cd Applications/LaunchHeim
packaging/build-packages.sh          # .deb and .rpm into packaging/dist/ (or pass deb / rpm / arch)
packaging/test-packages.sh           # install them in clean containers and take screenshots
```

`build-packages.sh` publishes the app on this machine and then runs `dpkg-deb` and `rpmbuild` inside
Debian and Fedora containers (podman, or `CONTAINER_ENGINE=docker`), so neither tool has to be
installed. `test-packages.sh` installs each package with apt or dnf in a clean Debian 13, Ubuntu
24.04, Fedora 44, AlmaLinux 9 and AlmaLinux 10 container, starts LaunchHeim offscreen, and saves a
screenshot of the Settings page to `packaging/dist/test-*.png`. It fails if the app downloads its own
Qt or writes a desktop entry into the home folder.

`build-packages.sh arch` runs the PKGBUILD with `makepkg` in a clean `docker.io/library/archlinux`
container, from the current commit (uncommitted changes aren't in it), and puts the
`.pkg.tar.zst` into `packaging/dist/`. `namcap` then reports only the expected noise (it can't map
QML imports to packages, and it can't see libraries that are loaded at runtime rather than linked).

## Releases

```fish
# 1. bump <Version> in Directory.Build.props, add its section to CHANGELOG.md, and merge it
git tag launchheim-v0.5.3
git push origin launchheim-v0.5.3
```

The tag starts `.github/workflows/launchheim-release.yml`:
1. It checks that the tag matches `<Version>` in `Directory.Build.props` and that `CHANGELOG.md` has
   a section for it. That section opens the release notes, and the download page shows every version.
2. It builds the Windows zip (through the Windows workflow, signed and smoke-tested), plus the Arch
   package, the .deb and the .rpm (`build-packages.sh deb rpm arch` on Ubuntu), and the Android
   companion's APK, signed with the release key (`android/README.md` "Releases").
3. It installs the .deb and .rpm in Debian and Fedora containers.
4. It publishes all five files as the GitHub release `launchheim-v0.5.3`.

The download page picks the new files up by itself. Starting the workflow by hand (*Run workflow*)
does the same builds and tests as a dry run, without a release.

The released Arch package is `launchheim-git` built at the tagged commit, so installing it installs
exactly that release. It isn't signed, so it has to be downloaded before `pacman -U` (see
[Install from a release](#install-from-a-release)). The release notes and the download page list the
SHA-256 of every file; the page takes them from the `digest` GitHub stores for each release asset.

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
