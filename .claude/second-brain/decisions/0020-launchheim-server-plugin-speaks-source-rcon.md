---
tags: [decision, launchheim, servers, bepinex, rcon]
created: 2026-11-06
updated: 2026-11-06
status: active
supersedes:
---
# 0020: LaunchHeim's server plugin speaks Source RCON and takes commands as files

**Context.** A console for Valheim's dedicated server needs something inside the server
([[0019-launchheim-server-manager]]). The Valheim mods that do this (Tristan-ValheimRcon, AviiNL-rcon,
ferrinius-RCON_Next, JereKuusela-Rcon_Commands in the Thunderstore index on 2026-11-06) all speak
Source RCON. Hosts like G-Portal give FTP and nothing else, so a TCP port for RCON may not be
available at all.

**Decision.** (2026-11-06, the owner chose LaunchHeim's own plugin over relying on an existing one)
- `src/ServerPlugin` (net472, BepInEx, reflection like the game plugin), GUID
  `io.github.codeisnotevil.launchheim.server`. It does nothing unless `Application.isBatchMode`, so
  it never opens a port on a player's PC.
- **Source RCON** on a configurable address and port. LaunchHeim uses `127.0.0.1` and game port + 2
  with a generated password. Without a password RCON stays off.
- **The protocol lives in one file** (`src/Shared/SourceRcon.cs`), compiled into Core (client) and the
  plugin (server), so both ends stay in step and the tests run the real server code. The client also
  works with RCON mods that don't mirror Source's end-of-answer packet: an answer ends after 400 ms of
  quiet.
- **The file channel:** `BepInEx/config/LaunchHeim.Server/commands/<id>.cmd` in,
  `responses/<id>.txt` out, both written as a temporary file and renamed, polled every 2 s. It works
  through anything that moves files. Locally it is the fallback when RCON can't connect.
- **Commands run on Unity's main thread** (a queue drained in `Update`): help, status, players (with
  platform IDs), say (the `ShowMessage` routed RPC), save, stop (`Application.Quit`, which saves),
  kick, ban, unban, banned, admins, permitted. Anything else goes to Valheim's `Terminal.commands`,
  except cheats.

**Alternatives.**
- Installing Tristan-ValheimRcon from Thunderstore: less code, but no file channel for FTP-only hosts,
  and its commands and life are someone else's. LaunchHeim's client still talks to it, since it is
  standard RCON.
- stdin: Valheim's server doesn't read it, and a pipe ties the server to LaunchHeim.

**Consequences.**
- RCON sends its password in the clear. Binding to anything but localhost needs a long random password,
  which is what the config's description says.
- Publishing the plugin on Thunderstore (so hosts' own mod installers can add it) is the owner's call,
  not done.
- Not yet tried against a real server on Windows.

Related: [[0019-launchheim-server-manager]], [[0016-launchheim-instant-start-plugin]], [[launchheim]]
