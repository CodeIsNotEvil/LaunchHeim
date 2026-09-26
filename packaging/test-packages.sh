#!/usr/bin/env bash
# Installs the packages from packaging/dist/ into clean containers the way a user would (apt or dnf,
# resolving dependencies from the distribution), starts LaunchHeim offscreen and saves a screenshot of
# its Settings page to packaging/dist/test-<distro>.png. Also checks that the packaged build uses the
# system Qt (nothing is downloaded into the home folder) and that --register-desktop leaves the home
# folder alone.
#
#   packaging/test-packages.sh             # every distro below
#   packaging/test-packages.sh debian:13   # or just some
set -euo pipefail
cd "$(dirname "$0")/dist"

engine="${CONTAINER_ENGINE:-podman}"
deb=$(ls launchheim_*_amd64.deb | tail -1)
rpm=$(ls launchheim-*.x86_64.rpm | tail -1)
targets=("$@")
[ ${#targets[@]} -gt 0 ] || targets=(debian:13 ubuntu:24.04 fedora:44 almalinux:9 almalinux:10)

# Runs inside the container after the package is installed. Everything except the PNG goes to stderr.
read -r -d '' smoke <<'SH' || true
set -e
export HOME=/tmp/home XDG_RUNTIME_DIR=/tmp/run QT_QPA_PLATFORM=offscreen QT_QUICK_BACKEND=software
mkdir -p "$HOME" "$XDG_RUNTIME_DIR" && chmod 700 "$XDG_RUNTIME_DIR"
launchheim --register-desktop >&2 || echo "register-desktop exited $?" >&2
[ ! -e "$HOME/.local/share/applications/launchheim.desktop" ] || { echo "FAIL: wrote a user desktop entry" >&2; exit 1; }
LAUNCHHEIM_SCREENSHOT=/tmp/shot.png LAUNCHHEIM_SCREENSHOT_PAGE=settings timeout 90 launchheim >/tmp/app.log 2>&1 || true
grep -v '^$' /tmp/app.log >&2 || true
[ ! -e "$HOME/.qmlnet-qt-runtimes" ] || { echo "FAIL: downloaded the bundled Qt" >&2; exit 1; }
[ -s /tmp/shot.png ] || { echo "FAIL: no screenshot" >&2; exit 1; }
cat /tmp/shot.png
SH

for target in "${targets[@]}"; do
  name="test-${target//[:\/]/-}.png"
  case "$target" in
    debian:*|ubuntu:*)
      package="$deb"
      install='apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq fonts-dejavu-core "./$1" >/dev/null' ;;
    almalinux:10*|rockylinux:10*)
      # RHEL 10 dropped Qt 5 from AppStream. EPEL 10 still has it.
      package="$rpm"
      install='dnf -y -q install epel-release && dnf -y -q install dejavu-sans-fonts "./$1"' ;;
    *)
      package="$rpm"
      install='dnf -y -q install dejavu-sans-fonts "./$1"' ;;
  esac

  echo "== $target ($package)" >&2
  # $1 is the package file, $2 the install command (which refers to $1), $3 the smoke test.
  if tar -c "$package" | "$engine" run --rm -i "docker.io/library/$target" sh -c \
      'cd /tmp && tar -x && eval "$2" >&2 && sh -c "$3"' sh "$package" "$install" "$smoke" > "$name"; then
    echo "   ok, screenshot in packaging/dist/$name" >&2
  else
    echo "   FAILED" >&2
    rm -f "$name"
  fi
done
