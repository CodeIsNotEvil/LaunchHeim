---
tags: [decision, launchheim, steam, cli]
created: 2026-10-14
updated: 2026-10-14
status: active
supersedes:
---
# 0015: Steam's Play button starts LaunchHeim's setups through `launchheim steam %command%`

**Context.** The owner wanted to start Valheim from Steam and get LaunchHeim's setup anyway: the
instance played last, with its world selected or its server joined, without opening LaunchHeim first.
Also a command line for LaunchHeim in general. Steam's only hook into a game's start is the launch
options, where `%command%` stands for Steam's own command (on Linux `valheim.x86_64`, plus
`-force-glcore` for the OpenGL entry, possibly behind `gamemoderun` or Steam's reaper).

**Decision.** (2026-10-14)
- LaunchHeim got a command line (`Core/Cli/CommandLine.cs`, runner `Desktop/Cli/CliRunner.cs`, no
  Qt): `play`, `steam`, `list`, `help`, `--version`. Only a known verb as the first argument makes a
  command, so `nxm://` links and plain starts still open the window.
- `launchheim steam [options] %command%` is a wrapper, like BepInEx's `start_game_bepinex.sh
  %command%`: it resolves the last play (`PlayTargets`, the sidebar's rules), adds Doorstop, the
  instance's arguments, `+connect` and the PlayerPrefs to Steam's command
  (`GameLauncher.WrapSteamCommand`), runs it and waits. Steam's environment reaches the game, so the
  overlay needs nothing from LaunchHeim there.
- LaunchHeim's own Windows launches go through `steam.exe -applaunch`, so they come back through the
  launch options. They always carry `--doorstop-enabled` (vanilla ones `false`), which marks them to
  run unchanged. Steam's own `+connect` (joining a friend) wins over the remembered destination and
  doesn't become the last play.
- When the launch can't be planned nothing starts, and the reason goes to the open window (a toast
  over the single-instance socket), else `notify-send` / a message box. A vanilla fallback could open
  a modded world without its mods, and Valheim drops modded items on save.
- The command line and Steam starts log to `launchheim-cli.log`, not the window's log.
- Settings → "Start from Steam" shows the line to paste. It's `launchheim steam %command%` when
  `/usr/bin/launchheim` is this program (Steam's PATH has `/usr/bin`, not necessarily `~/.local/bin`),
  else the full path.

**Alternatives.**
- Writing the launch options into Steam's `localconfig.vdf` for the user: Steam overwrites the file
  from memory while it runs, and editing another program's config unasked is rude. Copying is one paste.
- Starting vanilla when the plan fails: see above, it can cost a modded world its items.
- A LaunchHeim-only Steam entry (non-Steam game shortcut): Steam's Valheim page, play time and
  "Join game" wouldn't apply, and the owner asked for Valheim's own Play button.

**Consequences.** Not yet tried with the real Steam client and game when written: the wrapper was run
by hand inside Steam's runtime (`steam-runtime/run.sh`, overlay preloaded) against a stand-in game,
and the error toast with the offscreen window. To check: Shift+Tab in a Steam-started modded game on
Linux, and on Windows whether Steam's overlay reaches `valheim.exe` started by `LaunchHeim.exe` (Steam
hooks child processes, but that wasn't seen). Doesn't work with Flatpak Steam (its sandbox can't run a
LaunchHeim installed outside it) or a compatibility tool forced for Valheim. A window open meanwhile
saves its own copy of `settings.json` later and can overwrite the last play a Steam start recorded.

Related: [[launchheim]], [[0010-launchheim-direct-play-uses-the-games-own-options]], [[0014-launchheim-steam-overlay]]
