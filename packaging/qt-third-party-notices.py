#!/usr/bin/env python3
"""Writes licenses/Qt-third-party.txt: the code inside the Qt DLLs that the Windows build ships.

Qt compiles third-party code into its own libraries (FreeType, libjpeg, zlib, HarfBuzz, ...). Their
licenses travel with every copy of those DLLs, and FreeType and libjpeg even require a credit in the
documentation. Qt records each of them in a qt_attribution.json, so this reads those files for the Qt
version and modules that windeployqt ships (packaging/windows/build.ps1) instead of keeping the list
by hand. Only the Windows zip ships Qt; Linux packages use the distribution's Qt, which brings its own
licenses.

Run it again after changing the Qt version or the modules: python3 packaging/qt-third-party-notices.py
Needs network access and the gh CLI (for the repository trees).
"""
import json
import pathlib
import posixpath
import subprocess
import urllib.request

QT_VERSION = "5.15.2"
# The modules whose DLLs or plugins end up in the Windows zip. qtgraphicaleffects and qtremoteobjects
# have no third-party code.
MODULES = ["qtbase", "qtdeclarative", "qtquickcontrols2", "qtsvg", "qtimageformats"]
# Code that is not in a Windows build of the shipped modules: other platforms, Qt SQL, Qt D-Bus and
# Qt Test are not shipped, pixman is ARM only, forkfd is Unix only.
NOT_SHIPPED = {
  "android-native-style", "android-gradle-wrapper", "forkfd", "pixman", "sqlite", "vera_font",
  "dejayvu", "xcb-xinput", "qeventdispatcher_cf", "libdbus-1-headers", "cocoa-platform-plugin",
  "valgrind", "cycle", "linuxperf",
}

OUTPUT = pathlib.Path(__file__).resolve().parent.parent / "licenses" / "Qt-third-party.txt"


def fetch(module: str, path: str) -> str:
  url = f"https://raw.githubusercontent.com/qt/{module}/v{QT_VERSION}/{path}"
  with urllib.request.urlopen(url) as response:
    return response.read().decode("utf-8", "replace")


def attribution_files(module: str) -> list[str]:
  tree = subprocess.run(
    ["gh", "api", f"repos/qt/{module}/git/trees/v{QT_VERSION}?recursive=1", "-q", ".tree[].path"],
    check=True, capture_output=True, text=True).stdout.split()
  return [p for p in tree if p.endswith("qt_attribution.json") and not any(d in p for d in ("/tests/", "/examples/", "/doc/"))]


def main() -> None:
  sections = []
  for module in MODULES:
    for path in attribution_files(module):
      # Some files contain raw tabs inside strings, which strict JSON rejects.
      data = json.loads(fetch(module, path), strict=False)
      for entry in data if isinstance(data, list) else [data]:
        if entry.get("Id") in NOT_SHIPPED:
          continue

        files = entry.get("LicenseFile") or []
        texts = [fetch(module, posixpath.normpath(posixpath.join(posixpath.dirname(path), f))).strip()
                 for f in ([files] if isinstance(files, str) else files)]
        copyright = entry.get("Copyright", "")
        copyright = "\n".join(copyright) if isinstance(copyright, list) else copyright
        header = [
          entry.get("Name", entry.get("Id")),
          f"  Part of: Qt {QT_VERSION} {module} ({entry.get('QtUsage', '').strip()})",
          f"  Version: {entry.get('Version', 'unspecified')}",
          f"  License: {entry.get('License', '')} ({entry.get('LicenseId', '')})",
          f"  Homepage: {entry.get('Homepage', '')}",
        ]
        body = "\n".join(header) + "\n\n" + copyright.strip()
        if texts:
          body += "\n\n" + "\n\n".join(texts)
        sections.append(body)

  OUTPUT.write_text(
    f"Third-party code inside Qt {QT_VERSION}\n"
    f"{'=' * 40}\n\n"
    "The Windows build of LaunchHeim ships Qt's DLLs. Qt itself is covered by the LGPL-3.0 (see\n"
    "THIRD-PARTY-NOTICES.txt); the code below is compiled into those DLLs under its own license.\n"
    "Generated from Qt's qt_attribution.json files by packaging/qt-third-party-notices.py.\n\n"
    + "\n\n\n".join(("-" * 78) + "\n" + s for s in sections) + "\n",
    encoding="utf-8")
  print(f"Wrote {len(sections)} entries to {OUTPUT}")


if __name__ == "__main__":
  main()
