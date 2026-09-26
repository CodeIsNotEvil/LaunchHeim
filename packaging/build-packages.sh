#!/usr/bin/env bash
# Builds the Debian/Ubuntu .deb and the Fedora/RHEL .rpm into packaging/dist/.
# (Arch uses packaging/arch/PKGBUILD with makepkg instead. See packaging/README.md.)
#
#   packaging/build-packages.sh            # both
#   packaging/build-packages.sh deb        # or just one: deb | rpm
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
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Desktop/LaunchHeim.Desktop.csproj)
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
  cp packaging/rpm/launchheim.spec "$work/rpm/"

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

for format in "${formats[@]}"; do
  case "$format" in
    deb) build_deb ;;
    rpm) build_rpm ;;
    *) echo "Unknown format '$format'. Use deb or rpm." >&2; exit 2 ;;
  esac
done
