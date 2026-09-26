---
tags: [decision, launchheim, windows, qt]
created: 2026-09-26
updated: 2026-09-26
status: active
supersedes:
---
# 0006: LaunchHeim on Windows keeps the QML UI instead of a WinUI app

**Context.** The owner asked whether LaunchHeim can run on Windows, and suggested a second WinUI front
end on the same Core, but only if the QML UI couldn't be made to work there.

**Decision.** Keep the QML UI. Qml.Net supports Windows. The only blocker was the signal fix: on
Windows `QmlNet.dll` exports just its C API, not the `NetValue` methods `native/signal_fix.cpp` calls,
so the fix can't be applied from outside. The Windows build therefore compiles qmlnet-native (MIT) at
the commit Qml.Net 0.11.0 came from, with the same change as a patch
(`packaging/windows/qmlnet-signal-fix.patch`), against Qt 5.15.2 MSVC. It ships that Qt next to
`LaunchHeim.exe`. A GitHub Actions Windows runner builds it and smoke-tests it.

**Alternatives.** A WinUI app would mean two UIs to keep in sync for every feature, and WinUI can't be
built or run from the owner's Linux machine at all. Byte-patching the short-circuit in the shipped
`QmlNet.dll` was rejected: the fix also re-checks that each wrapper is still alive, because handling a
signal can delete the others, and a byte patch can't add that check (use-after-free risk).

**Consequences.** Windows builds need MSVC, so they come from CI (or a Windows machine with Visual
Studio and Qt), not from Linux. The Linux build could move to the same patched source build to drop
`signal_fix.cpp` and the RPATH patch, but that isn't done yet. On Windows the game folder gets
Doorstop's `winhttp.dll` plus a disabled config (like r2modman), because Windows only loads the proxy
from there.

Related: [[launchheim]], [[0005-launchheim-distro-packages-use-system-qt]]
