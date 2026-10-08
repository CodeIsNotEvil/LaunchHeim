---
tags: [decision, launchheim, steam, linux, windows]
created: 2026-10-08
updated: 2026-10-08
status: active
supersedes:
---
# 0014: LaunchHeim gives Valheim Steam's overlay: preloaded on Linux, started through Steam on Windows

**Context.** The owner played 0.5.3 on CachyOS and found Shift+Tab and F12 dead: Steam injects its
overlay only into games it starts, and LaunchHeim started `valheim.x86_64` directly (Steam's log showed
the process tracked through the Steam API, but nothing more). The Windows launch was direct too.

**Decision.** (2026-10-08)
- Linux keeps the direct start (Doorstop needs `LD_PRELOAD`, which Steam's own environment would
  drop) and adds what Steam adds: `<Steam>/ubuntu12_64/gameoverlayrenderer.so` in `LD_PRELOAD` after
  Doorstop, `ENABLE_VK_LAYER_VALVE_steam_overlay_1=1` (Valheim renders with Vulkan on Linux; the
  layer's manifest is in `~/.local/share/vulkan/implicit_layer.d`), and `SteamOverlayGameId`. The
  Steam folder is the first root holding `ubuntu12_32/steam` (`SteamLibraryLocator.ClientDirectory`).
- Windows starts `steam.exe -applaunch 892970 <Doorstop and join arguments>` like r2modman, since
  Doorstop takes its settings from the command line there. The game is then found by process name
  (`GameProcess`), after refusing a launch while a Valheim already runs. Without `steam.exe` it
  falls back to the direct start.
- A Screenshots page reads Steam's `userdata/<account>/760/remote/892970/screenshots` (account that
  logged in last) and never touches `screenshots.vdf`.

**Alternatives.**
- `steam -applaunch` on Linux too: the preload and Doorstop's variables wouldn't reach the game unless
  the user put `start_game_bepinex.sh %command%` in Steam's launch options, which also mods every
  start from Steam.
- Injecting `GameOverlayRenderer64.dll` into a directly started game on Windows: DLL injection from a
  self-signed launcher is what antivirus heuristics look for, and loading it late (after the D3D
  device exists) isn't documented to work.
- Keeping the Windows start direct: `SteamAPI_Init` runs after Unity created its window, too late for
  the overlay (a Minecraft overlay mod has to init Steam before the window for the same reason).

**Consequences.** Not yet verified in a real game on Linux or Windows when written: check Shift+Tab
and F12 after the merge. Valheim's launch options from Steam's game properties now apply on Windows.
Steam warns about launches with custom arguments from `steam://` links (BG3 Mod Manager, 2024);
r2modman's `-applaunch` with arguments has no such reports, but if Steam asks, the sidebar says
"Answer Steam if it asks" and LaunchHeim waits three minutes for the game.

Related: [[launchheim]], [[0010-launchheim-direct-play-uses-the-games-own-options]]
