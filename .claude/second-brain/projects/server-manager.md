---
tags: [project, launchheim, servers]
created: 2026-11-06
updated: 2026-11-07
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
2. **A generic FTP/FTPS/SFTP host** (`server-manager-ftp-hosts`, 2026-11-07,
   [[0021-launchheim-hosted-servers-through-their-files]]). Built as planned, plus uploading the server
   program to a VPS and the plugin's heartbeat for status. The plan was:
   - `IServerFiles` over FluentFTP and SSH.NET (both MIT; update THIRD-PARTY-NOTICES and `licenses/`),
   - deploy mods, configs and the plugin with a hash diff,
   - follow the log by reading from an offset (`LogFollower` needs a source other than a local path),
   - the console through the file channel, or RCON when the host has a free port,
   - "Open the host's panel" for starting,
   - tested against podman `vsftpd` and `atmoz/sftp` containers.
3. **G-Portal:** a preset over item 2, with its FTP layout, log path and panel link. It has no public
   API. Needs a real G-Portal server to find the paths.
4. **Backups** (`server-backups`, 2026-11-07, [[0022-launchheim-server-backups]]). Built from the plan
   below. Proton Drive goes through Proton's own CLI (released June 2026) instead of rclone's beta
   backend. Item 3 (G-Portal) waits for a real G-Portal login.
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

## Testing hosted servers locally
- SFTP: `podman run -d --name lh-sftp --user root --userns=keep-id:uid=1000,gid=1000 -p 127.0.0.1:2222:22 -v <dir>:/home/foo/valheim:Z docker.io/atmoz/sftp foo:pass:1000:1000`.
- FTP: `podman run -d --name lh-ftp --user root --userns=keep-id:uid=1000,gid=1000 -p 127.0.0.1:2121:21 -p 127.0.0.1:21000-21010:21000-21010 -e USERS="foo|pass|/home/foo|1000" -e ADDRESS=127.0.0.1 -v <dir>:/home/foo/valheim:Z docker.io/delfer/alpine-ftp-server`.
- `keep-id` maps the container's uid 1000 to the owner's, so uploaded files are theirs. Without
  `--user root` the images' entrypoints fail: sshd and vsftpd need root inside the namespace.
- The contract tests run against them with
  `LAUNCHHEIM_TEST_SFTP=127.0.0.1:2222:foo:pass:valheim LAUNCHHEIM_TEST_FTP=127.0.0.1:2121:foo:pass dotnet test tests/Core.Tests --filter ServerFilesTests`.
- A "VPS" run starts the uploaded server from the mounted folder on the PC (ServerLauncher with that
  folder as both the program and BepInEx root).

## Open
- **Backups:**
  - Proton Drive is untested with a real account. The CLI's `--json` list format was read from its
    source, not seen.
  - There's no timer setup from the UI (it shows the command).
  - A backup of a big world on a slow FTP host has no cancel button.
- **Hosted servers:**
  - FTPS hasn't been tried against a real TLS server; the pinning code path is untested.
  - No start script is written for a VPS; its owner starts `start_server_bepinex.sh` themselves.
  - A VPS with only SFTP still can't be started from LaunchHeim (the SSH item).
- **Not yet tried on a real Windows PC:**
  - the server start (Doorstop proxy in Steam's server folder),
  - Ctrl+C through `AttachConsole`,
  - `CreateNoWindow` and the firewall prompt for UDP 2456–2457.
- **A keyring for `server.json`'s passwords.**
- **A firewall hint/rule for local servers**, as phone sync has (`Core/LocalSend/Firewall.cs`). The plan
  had it; it isn't built yet.
- **Picking an existing world:** the server's own `saves/` starts empty; "Worlds saved in" can point
  at the game's own save folder, but copying one world over isn't offered yet.
