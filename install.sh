#!/usr/bin/env bash
# Builds LaunchHeim and installs it for the current user:
#   ~/.local/opt/LaunchHeim   the published app (override with LAUNCHHEIM_PREFIX)
#   ~/.local/bin/launchheim   a symlink, to start it from a terminal
#   launchheim.desktop        in the application launcher, also registered for nxm:// links
# Run it again to update. Instances, settings and caches live in the XDG folders and are kept.
#
# ./install.sh --uninstall removes all of that again (but never instances or settings). Do this before
# switching to the pacman, deb or rpm package: the desktop entry in ~/.local/share/applications would
# otherwise hide the packaged one. See packaging/README.md.
set -euo pipefail
cd "$(dirname "$0")"

prefix="${LAUNCHHEIM_PREFIX:-$HOME/.local/opt/LaunchHeim}"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"

if [ "${1:-}" = "--uninstall" ]; then
  # Only ever delete a folder that holds a LaunchHeim install.
  [ -f "$prefix/LaunchHeim.dll" ] && rm -rf "$prefix"
  [ -L "$HOME/.local/bin/launchheim" ] && rm -f "$HOME/.local/bin/launchheim"
  rm -f "$data_home/applications/launchheim.desktop" "$data_home/icons/hicolor/scalable/apps/launchheim.svg"
  command -v update-desktop-database >/dev/null && update-desktop-database "$data_home/applications" || true
  echo "Removed the LaunchHeim install from $prefix. Instances and settings in ~/.local/share/LaunchHeim and ~/.config/LaunchHeim are kept."
  exit 0
fi

for tool in dotnet g++; do
  command -v "$tool" >/dev/null || { echo "LaunchHeim needs $tool to build." >&2; exit 1; }
done
if [ ! -f /usr/include/qt/QtCore/qstring.h ]; then
  echo "The Qt 5 headers are missing. Install qt5-base (the Qml.Net signal fix is compiled against them)." >&2
  exit 1
fi

# Only ever delete a folder that holds a previous LaunchHeim install.
if [ -f "$prefix/LaunchHeim.dll" ]; then
  rm -rf "$prefix"
fi

dotnet publish src/Desktop -c Release -o "$prefix"

mkdir -p "$HOME/.local/bin"
ln -sf "$prefix/LaunchHeim" "$HOME/.local/bin/launchheim"
"$prefix/LaunchHeim" --register-desktop

echo "LaunchHeim is installed in $prefix. Start it from the application launcher or with 'launchheim'."
