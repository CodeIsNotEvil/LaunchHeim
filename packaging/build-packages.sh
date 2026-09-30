#!/usr/bin/env bash
# Builds the Debian/Ubuntu .deb, the Fedora/RHEL .rpm and the Arch package into packaging/dist/.
#
#   packaging/build-packages.sh            # deb and rpm
#   packaging/build-packages.sh deb rpm arch
#
# arch runs packaging/arch/PKGBUILD with makepkg in an Arch container, from the current commit (the
# PKGBUILD builds from git, so uncommitted changes are not in it). For your own machine, makepkg -si
# in packaging/arch does the same and installs it. See packaging/README.md.
#
# The app is published once on this machine. dpkg-deb and rpmbuild then run in Debian and Fedora
# containers, so neither has to be installed here. The staged files go into the container over stdin
# and the package comes back over stdout, so there are no bind mounts (and no SELinux relabeling).
# The engine is podman, or whatever CONTAINER_ENGINE names (docker works too).
set -euo pipefail
cd "$(dirname "$0")/.."

engine="${CONTAINER_ENGINE:-podman}"
formats=("$@")
[ ${#formats[@]} -gt 0 ] || formats=(deb rpm)
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
dist="$PWD/packaging/dist"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$dist"

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

publish() { # <output> <self-contained: true|false>
  dotnet publish src/Desktop -c Release -r linux-x64 --self-contained "$2" \
    -p:LaunchHeimDistroPackage=true -o "$1" >/dev/null
}

build_deb() {
  # Self-contained: Debian doesn't package .NET 10, and depending on Microsoft's apt repository would
  # make the package uninstallable without extra setup.
  publish "$work/publish-deb" true
  packaging/stage.sh "$work/publish-deb" "$work/deb" /usr/lib
  # Debian keeps every license of the package in its copyright file: LaunchHeim's own, then the
  # third-party notices. Their full texts are in /usr/lib/launchheim/licenses.
  install -d "$work/deb/usr/share/doc/launchheim"
  { cat LICENSE; printf '\n\n'; cat THIRD-PARTY-NOTICES.txt; } > "$work/deb/usr/share/doc/launchheim/copyright"
  chmod 644 "$work/deb/usr/share/doc/launchheim/copyright"
  install -d "$work/deb/DEBIAN"
  install -m755 packaging/deb/postinst "$work/deb/DEBIAN/postinst"
  sed -e "s/@VERSION@/$version/" -e "s/@INSTALLED_SIZE@/$(du -sk --exclude=DEBIAN "$work/deb" | cut -f1)/" \
    packaging/deb/control.in > "$work/deb/DEBIAN/control"

  local out="launchheim_${version}-1_amd64.deb"
  tar -C "$work" --owner=0 --group=0 -c deb | "$engine" run --rm -i docker.io/library/debian:13 sh -c \
    'tar -x -C /tmp && dpkg-deb --root-owner-group -Zxz --build /tmp/deb /tmp/out.deb >&2 && cat /tmp/out.deb' \
    > "$dist/$out"
  echo "Built packaging/dist/$out"
}

build_rpm() {
  # Framework-dependent: Fedora and RHEL (AppStream) ship dotnet-runtime-10.0.
  publish "$work/publish-rpm" false
  packaging/stage.sh "$work/publish-rpm" "$work/rpm-root" /usr/lib64
  mkdir -p "$work/rpm"
  tar -C "$work/rpm-root" --owner=0 --group=0 -cf "$work/rpm/launchheim-root.tar" .
  cp packaging/rpm/launchheim.spec LICENSE THIRD-PARTY-NOTICES.txt "$work/rpm/"

  local out="launchheim-${version}-1.x86_64.rpm"
  # %{dist} is left empty so one rpm serves every Fedora and RHEL release.
  tar -C "$work" -c rpm | "$engine" run --rm -i docker.io/library/fedora:latest sh -c "
    dnf -y -q install rpm-build >&2 &&
    tar -x -C /tmp &&
    rpmbuild -bb --define '_topdir /tmp/rpmbuild' --define '_sourcedir /tmp/rpm' \
      --define 'app_version $version' --define 'dist %{nil}' /tmp/rpm/launchheim.spec >&2 &&
    cat /tmp/rpmbuild/RPMS/x86_64/$out" > "$dist/$out"
  echo "Built packaging/dist/$out"
}

build_arch() {
  local commit
  commit=$(git rev-parse HEAD)
  git diff --quiet HEAD || echo "warning: uncommitted changes are not in the Arch package (it builds commit ${commit:0:7})" >&2

  # A bare clone gives makepkg's git source the history pkgver counts (CI checks out with
  # fetch-depth: 0 for this). makepkg refuses to run as root, hence the builder user.
  mkdir -p "$work/arch"
  git clone -q --bare "$(git rev-parse --show-toplevel)" "$work/arch/repo.git"
  cp packaging/arch/PKGBUILD "$work/arch/"
  cat > "$work/arch/run.sh" <<'SCRIPT'
set -e
pacman -Syu --noconfirm --needed base-devel git >/dev/null
useradd -m builder
echo 'builder ALL=(ALL) NOPASSWD: ALL' > /etc/sudoers.d/builder
chown -R builder /tmp/arch
cd /tmp/arch
sudo -u builder env LAUNCHHEIM_GIT="file:///tmp/arch/repo.git#commit=$1" makepkg -s --noconfirm >&2
tar -c ./*.pkg.tar.zst
SCRIPT

  tar -C "$work" --owner=0 --group=0 -c arch \
    | "$engine" run --rm -i docker.io/library/archlinux:latest sh -c 'tar -x -C /tmp && sh /tmp/arch/run.sh "$1"' sh "$commit" \
    | tar -x -C "$dist"
  echo "Built packaging/dist/$(cd "$dist" && ls -t ./*.pkg.tar.zst | head -1 | cut -c3-)"
}

for format in "${formats[@]}"; do
  case "$format" in
    deb) build_deb ;;
    rpm) build_rpm ;;
    arch) build_arch ;;
    *) echo "Unknown format '$format'. Use deb, rpm or arch." >&2; exit 2 ;;
  esac
done
