---
tags: [project, launchheim, servers]
created: 2026-11-06
updated: 2026-11-06
status: active
---
# Server manager

LaunchHeim manages Valheim dedicated servers as well as the game: local ones first, then servers
reached over FTP/SFTP, G-Portal, SSH and the hosting panels' APIs. The why is in
[[0019-launchheim-server-manager]] and [[0020-launchheim-server-plugin-speaks-source-rcon]]. How it
works: README "Dedicated servers".

## Roadmap (owner's order, 2026-11-06; one PR each)
1. **Base and local servers** (`server-manager-base`):
   - the Servers page and each server's own page,
   - Steam's or SteamCMD's copy of the server,
   - mods from a linked instance,
   - the server plugin with RCON and the file channel,
   - the console's Server tab.
2. **A generic FTP/FTPS/SFTP host:**
   - `IServerFiles` over FluentFTP and SSH.NET (both MIT; update THIRD-PARTY-NOTICES and `licenses/`),
   - deploy mods, configs and the plugin with a hash diff,
   - follow the log by reading from an offset (`LogFollower` needs a source other than a local path),
   - the console through the file channel, or RCON when the host has a free port,
   - "Open the host's panel" for starting,
   - tested against podman `vsftpd` and `atmoz/sftp` containers.
3. **G-Portal:** a preset over item 2, with its FTP layout, log path and panel link. It has no public
   API. Needs a real G-Portal server to find the paths.
4. **Backups** (below).
5. **Later:**
   - SSH: SteamCMD on the remote, a systemd user unit or a Windows scheduled task, `tail -F`.
   - The Pterodactyl/Pelican client API (websocket console, power, files) and Nitrado's API.
   - The plugin on Thunderstore.
   - A `launchheim server …` command line.

## Backup plan (item 4; the seams are in item 1)
- **What goes in:**
  - the world (`worlds_local/<World>.fwl` and `.db`, or the newer `<World>/` folder, see the
    ValheimSaves gotcha in [[launchheim]]),
  - `adminlist.txt`, `bannedlist.txt`, `permittedlist.txt`,
  - optionally `BepInEx/config` and the mod list,
  - all zipped as `<server>-<world>-<UTC timestamp>.zip`.
- **Consistent copies:** send `save` through RCON or the file channel first, and wait for the log's
  "World saved" line. Without the plugin, copy as-is and say so.
- **Every server whose files LaunchHeim reaches:** the source is `IServerFiles`, so local, FTP and SFTP
  servers back up the same way.
- **Destinations:**
  1. A **folder**: another drive, or a NAS mounted over SMB/NFS (UNC paths on Windows). No dependency,
     so it's the default.
  2. **rclone** (`rclone copyto`, if installed): one integration for Proton Drive (rclone's
     `protondrive` backend), Google Drive, OneDrive, Nextcloud/WebDAV, S3 and SFTP. Recommended over a
     Proton-specific tool: Proton has no official Drive CLI to build on, and rclone's Proton backend is
     marked beta. Check its state when item 4 starts.
- **Retention:** the last N, plus one a day for D days.
- **When:**
  - on an interval while LaunchHeim runs,
  - after every clean stop,
  - `launchheim server backup <name>` for a systemd timer or a Windows scheduled task while the window
    is closed.
- **Restore:** stop, write the files back, and keep the replaced files as a zip first.
- **Valheim's own `-backups`** (kept at 4 by default in the profile) is the first line of defence, but
  it lives on the same disk as the world.

## Open
- **Not yet tried on a real Windows PC:**
  - the server start (Doorstop proxy in Steam's server folder),
  - Ctrl+C through `AttachConsole`,
  - `CreateNoWindow` and the firewall prompt for UDP 2456–2457.
- **A keyring for `server.json`'s passwords.**
- **A firewall hint/rule for local servers**, as phone sync has (`Core/LocalSend/Firewall.cs`). The plan
  had it; it isn't built yet.
- **Picking an existing world:** the server's own `saves/` starts empty; "Worlds saved in" can point
  at the game's own save folder, but copying one world over isn't offered yet.
