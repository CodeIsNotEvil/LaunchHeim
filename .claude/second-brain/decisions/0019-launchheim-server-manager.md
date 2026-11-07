---
tags: [decision, launchheim, servers, valheim]
created: 2026-11-06
updated: 2026-11-06
status: active
supersedes:
---
# 0019: A server manager for Valheim's dedicated server, local first, files as the common ground

**Context.** The owner asked (2026-11-06) for a server manager with a console and mod support:
- local servers, and servers installed through Steam,
- servers on a machine reached over SSH,
- room for the big hosting providers' consoles and status, with a server plugin where a provider
  offers no way in from outside,
- deploying Valheim's dedicated server (Steam app 896660, Linux and Windows builds) even to a VPS with
  only SFTP.

Valheim's dedicated server has no remote console: it doesn't read stdin, and admin commands only exist
in the game (decompiled with ilspycmd, `FejdStartup.ParseServerArguments`, `Terminal`). Hosts differ
widely: some have an API (Nitrado; Pterodactyl/Pelican panels), G-Portal has none and gives only FTP.

**Decision.** (2026-11-06, the owner chose the order)
- **Roadmap, one PR each:**
  1. Base and local servers (this PR).
  2. A generic FTP/FTPS/SFTP host.
  3. G-Portal on top of it.
  4. Backups.
  5. Later: SSH (running the server remotely), the Pterodactyl/Pelican and Nitrado APIs.
- **A server is a folder** `servers/<id>/`: `server.json` (user-only permissions, it holds passwords),
  its own BepInEx, `server.log`, `run.json` and `saves/` by default. Copying the folder copies the
  server, like an instance.
- **Mods come from a linked instance**, copied into the server's folder before each start
  (`ServerMods`), instead of a second mod list per server. This changes the plan, which had each
  server's mod set as an `Instance` driven by `ModService`.
  - Why: players and server need the same mods, and one list can't drift.
  - The default leaves out mods Thunderstore lists as Client-side without Server-side; a per-mod
    switch overrides it.
  - `synced.json` tracks what was copied, so only those files are ever removed.
  - Configs are copied only while missing, because the server's config is authoritative for
    ServerSync-style mods.
  - A copy and not a link: the running server keeps its dlls (Windows locks them) while the instance
    changes.
- **A vanilla server with the console** gets BepInEx from the vanilla setup instant start already
  keeps ([[0016-launchheim-instant-start-plugin]]), so BepInExPack is downloaded once.
- **The launch reuses the game's Doorstop code** (`GameLauncher.AddDoorstop`, `WindowsDoorstopArguments`,
  `PrepareGameFolder` made internal) rather than copying it.
- **Servers run detached:** `setsid` plus `sh -c exec` keeps the pid and drops the output, and the log
  comes from `-logFile`, the same reasoning as the console following files. A recorded pid counts as
  the server only while the process start time matches, since pids get reused.
- **Stopping:**
  1. the plugin's `stop`,
  2. otherwise SIGINT on Linux (the lloesche/valheim-server image stops it that way too),
  3. otherwise a Ctrl+C sent by attaching to the server's console on Windows,
  4. a kill only after a confirmation.
- **The common ground for remote servers is file access** (`IServerFiles`): the plugin's file channel,
  then backups and FTP deployment all build on it. Only `LocalServerFiles` exists so far.
- **SteamCMD** (anonymous, `+@sSteamCmdForcePlatformType`) downloads the server where Steam doesn't
  have it, and will fetch the other OS's build for remote servers. Steam's own copy is preferred,
  since Steam keeps it updated.

**Alternatives.**
- A mod list per server: the most flexible, but players would have to keep two lists in step by hand.
- Symlinking the instance into the server: a running server would see the instance change under it,
  and Windows needs privileges for symlinks.
- Piping the server's stdout: it would tie the server's life to LaunchHeim.

**Consequences.**
- With SFTP only (no shell, no panel), LaunchHeim can install and update files and stop the server
  through the plugin, but it cannot *start* a process. Starting needs SSH or the host's panel. Say so
  in the UI when PR 2 lands.
- Secrets sit in `server.json`; a keyring is open.
- The Windows paths (Ctrl+C, the proxy in Steam's server folder, `CreateNoWindow`) are only built by CI
  so far.

Related: [[0020-launchheim-server-plugin-speaks-source-rcon]], [[launchheim]]
