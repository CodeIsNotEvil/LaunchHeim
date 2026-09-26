---
tags: [project, dotnet, qml, gaming]
created: 2026-09-26
updated: 2026-09-26
status: active
---
# LaunchHeim

A Valheim mod launcher for the owner's CachyOS / Plasma 6 desktop. Code: `Applications/LaunchHeim/`.
Behaviour, build and the Qt runtime workarounds: `Applications/LaunchHeim/README.md`.

## Shape in one breath
Instances (own BepInEx + plugins + configs under `~/.local/share/LaunchHeim/instances`) → Valheim is
started directly with Unity Doorstop pointed at the instance's preloader, so the game folder stays
vanilla. Mods come from Thunderstore (full index cached locally), Nexus (API key, `nxm://` for free
accounts) and CurseForge (API key).

## Key ideas
- Per-instance BepInEx through Doorstop instead of copying mods into the game folder: switching
  modpacks costs nothing, and the Steam install never has to be verified or repaired.
- Qml.Net (Qt 5.15) as the UI host. It's unmaintained since 2020, so it needs three workarounds
  (tar extraction, `libdl.so`, the native signal fix). They're listed in the README. Any upgrade of
  .NET, glibc or Qt should re-check them.
- Colours come from `kdeglobals` because Plasma 6 has no Qt 5 platform theme.

## Gotchas
- Qml.Net context properties get JS ownership and are garbage-collected, so the view model is a
  QML singleton.
- Qt 5.15's Material `ComboBox` logs a `foreground` binding loop. Setting `Material.foreground` on the
  instance silences it (2026-09-26).
- .NET's `Encoding.UTF8` writes a BOM. Files other tools parse (`.desktop`) must use the default
  BOM-less UTF-8 (2026-09-26).

## Open
- Only Thunderstore mods are checked for updates (Nexus and CurseForge would cost one API call per mod).
- The Desktop project has no tests. The UI is checked with the `LAUNCHHEIM_SCREENSHOT` mode.
