---
tags: [decision, launchheim, packaging, qt]
created: 2026-09-26
updated: 2026-09-26
status: active
supersedes:
---
# 0005: LaunchHeim's distribution packages use the system Qt

**Context.** Qml.Net normally downloads its own Qt 5.15.1 runtime (about 60 MB) into
`~/.qmlnet-qt-runtimes` on first start, because Plasma 6 systems only ship parts of Qt 5. The owner
wants to install LaunchHeim with pacman, and also wants .deb and .rpm packages.

**Decision.** Packages are built with `-p:LaunchHeimDistroPackage=true`, which writes a switch into
`LaunchHeim.runtimeconfig.json`. With it, LaunchHeim uses the distribution's Qt 5.15 (declared as
package dependencies) and leaves the desktop entry to the package. Arch, Fedora and RHEL depend on the
distro's .NET 10 runtime. The .deb is self-contained, because Debian has no .NET 10.

**Alternatives.** Keeping the first-start download means a system package writes 60 MB into every
user's home and the package manager doesn't know about it. Bundling Qml.Net's Qt runtime inside the
package means 175 MB and pruning that has to be maintained. A wrapper script that sets
`LAUNCHHEIM_QT=system` is bypassed when the desktop entry or the `nxm://` handler start the binary
directly.

**Consequences.** It only works because libQmlNet uses public Qt 5.15 API plus `QMetaObjectBuilder`,
which is stable across 5.15.x. It's verified on 5.15.9 (RHEL 9) through 5.15.19 (Arch). When a distro
drops Qt 5 (RHEL 10 already needs EPEL for it), that distro needs a bundled Qt or the Qt 6 question
([[launchheim]]). `install.sh` and dev builds keep the download.

Related: [[launchheim]], [[0006-launchheim-windows-keeps-qml]]
