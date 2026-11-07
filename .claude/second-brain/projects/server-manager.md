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

## Next session (handoff, 2026-11-07)
Where things stand: PR #3 (local servers) and PR #4 (hosted servers) are merged. **PR #5 (backups,
branch `server-backups`) is open**, waiting for the owner's review. Start there: follow-ups go onto that
branch until it's merged, and new work goes on a fresh branch from `main`.

**Roadmap still to do**
1. **G-Portal (item 3).** A preset on top of hosted servers: G-Portal's FTP layout (server folder, where
   `-savedir` points, the log), its panel link, and whether its BepInEx switch uses
   `start_server_bepinex.sh`. **Blocked:** it needs a real G-Portal server or its FTP details from the
   owner. Ask first.
2. **SSH (item 5).** Start, stop and update servers on a VPS: SteamCMD on the remote, a systemd user
   unit or a Windows scheduled task, `tail -F`. It also covers writing a start script for a VPS (today
   its owner starts `start_server_bepinex.sh` by hand) and the "SFTP alone can't start anything" gap.
3. **Hosting panel APIs.** Pterodactyl/Pelican (client API: websocket console, power, files) and Nitrado.
4. **The server plugin on Thunderstore**, so hosts' own mod installers can add it. The owner's call.

**Untested, needs the owner or real hardware**
- **Proton Drive backups.** Not tried with a real account (no Proton login here). The CLI's
  `--json` list format was read from its source, not seen. Ask the owner to sign in with
  `proton-drive auth login` and run a backup.
- **FTPS** against a real TLS server, and with it the certificate pinning.
- **Windows:**
  - starting a local server (Doorstop proxy in Steam's server folder),
  - stopping with Ctrl+C through `AttachConsole`,
  - `CreateNoWindow`, and the firewall prompt for UDP 2456–2457,
  - backups and the `backup` CLI on Windows.
- **Join** was never clicked in the real game: Valheim starting with the server's instance, the
  remembered character and instant start.
- **The crossplay join code** was never seen in a real log; it relies on the log line in Valheim's code.
- **"Show my internet address"** wasn't clicked against the real api.ipify.org (only a fake in tests).
- **The bottom of the hosted page** (Upload buttons, Server program) was never on screen: the screenshot
  mode can't scroll. Editing the built QML copy in `bin/` to scroll to a section works for one-off
  checks (done for Backups); a `LAUNCHHEIM_SCREENSHOT_PAGE` form like `server:<id>#backups` would make it
  repeatable.

**Missing features found along the way**
- **Backups:**
  - no cancel button (a big world on a slow FTP host takes a while);
  - no timer set up from the UI (the page shows the `launchheim backup` command);
  - no restore preview (which worlds, how old).
- **A firewall hint/rule for local servers**, as phone sync has (`Core/LocalSend/Firewall.cs`). It was
  planned for PR #3 and isn't built.
- **A keyring for passwords:** `server.json` holds the server and RCON passwords and the FTP/SFTP login,
  readable by the user only.
- **Picking an existing world for a server:** copying one of the game's worlds into the server's
  `saves/`. Today only "Worlds saved in" can point at another folder.

**Release prep (when the owner asks for a release)**
- **CHANGELOG.md has no entry yet** for local servers, hosted servers, backups, Join and the address
  list. It can't have an "Unreleased" section (`Scripts/changelog.py` and the site build reject it), so
  the entry gets written with the release's version and date.

**On the owner's machine**
- **Steam's library list** (`~/.local/share/Steam/steamapps/` and `config/libraryfolders.vdf`) was
  repaired after SteamCMD overwrote it (fixed in PR #4). The broken copies sit next to it as
  `*.launchheim-backup` and can go once Steam shows the `/mnt/games` games as installed.
- **`~/.local/share/LaunchHeim/dedicated-server/linux`** (2.1 GB) is a SteamCMD download from the
  owner's click. It's redundant if the servers use Steam's copy.
- **Podman test containers** `lh-sftp` and `lh-ftp` exist, stopped (`podman rm lh-sftp lh-ftp`). The
  commands to recreate them are under "Testing hosted servers locally". The scratch folders they
  mounted were session-only.
