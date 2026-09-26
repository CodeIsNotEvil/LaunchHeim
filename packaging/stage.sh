#!/usr/bin/env bash
# Lays out a published LaunchHeim in a package root. The PKGBUILD, the deb and the rpm all call this,
# so the three packages install exactly the same files in the same places:
#
#   <libdir>/launchheim/                  the published app
#   /usr/bin/launchheim                   a relative symlink to it (.NET resolves it to find its files)
#   /usr/share/applications/              the desktop entry, which also claims nxm:// links
#   /usr/share/icons/hicolor/scalable/    the icon
#   /usr/share/metainfo/                  AppStream data for software centres
#
# Usage: stage.sh <publish-dir> <package-root> [libdir]
# libdir defaults to /usr/lib; Fedora and RHEL pass /usr/lib64.
set -euo pipefail

publish="$1"
root="$2"
libdir="${3:-/usr/lib}"
packaging="$publish/packaging"

install -d "$root$libdir/launchheim" "$root/usr/bin"
cp -a "$publish/." "$root$libdir/launchheim/"
# Relative, so the link also resolves when the package root is mounted somewhere else.
ln -sf "..${libdir#/usr}/launchheim/LaunchHeim" "$root/usr/bin/launchheim"

install -Dm644 "$packaging/launchheim.desktop" "$root/usr/share/applications/launchheim.desktop"
install -Dm644 "$packaging/launchheim.svg" "$root/usr/share/icons/hicolor/scalable/apps/launchheim.svg"
install -Dm644 "$packaging/io.github.codeisnotevil.LaunchHeim.metainfo.xml" \
  "$root/usr/share/metainfo/io.github.codeisnotevil.LaunchHeim.metainfo.xml"
