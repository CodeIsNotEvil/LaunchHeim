---
tags: [decision, launchheim, servers, ftp, sftp]
created: 2026-11-07
updated: 2026-11-07
status: active
supersedes:
---
# 0021: Hosted servers are managed through their files: FTP, FTPS or SFTP

**Context.** Item 2 of the server manager's roadmap ([[0019-launchheim-server-manager]],
[[server-manager]]): a server at a game host or on a VPS. The owner's original ask included a VPS with
only SFTP. Game hosts like G-Portal give FTP and a web panel, nothing else. Nothing from outside can
see a private Valheim server's process, and a private server doesn't answer Steam's query (found
2026-11-06).

**Decision.** (2026-11-07)
- **The profile.** A server is `Kind = Hosted` with a `HostAccess`:
  - protocol, address, port, user, password or SSH key, the server's folder,
  - the log file (default `BepInEx/LogOutput.log`, which carries the game's own lines too),
  - the address players join, the panel's URL, an optional RCON port,
  - the host's OS, and a pinned fingerprint.
  Valheim's world, port and password are the host's business; only the password is kept, for Join.
- **One interface for files, `IServerFiles`.** Local, FluentFTP (FTP/explicit FTPS) and SSH.NET (SFTP,
  password, key or keyboard-interactive) all implement it. One connection each, calls serialized,
  reconnecting after a drop. Writes go to `<name>.tmp` and are renamed, so the plugin and LaunchHeim
  never read half a file.
- **Trust on first use, like `known_hosts`.** The SSH host key, and an FTPS certificate the system
  doesn't trust (game hosts use self-signed ones), are pinned in `HostAccess.Fingerprint` on first
  contact. A different one is refused until the player clicks Forget. Changing the address, port or
  protocol drops the pin.
- **Deploying is driven by a record at the host.** `.launchheim/mods.json` and `server-program.json`
  map each uploaded file to the local file's size and mtime:
  - Only changed files go up, and files LaunchHeim uploaded but no longer wants are deleted.
  - Nothing else at the host is touched.
  - FTP can't hash remote files, and host clocks can't be trusted.
  - Configs only go up when missing, as for local servers.
  - The plugin's config always goes up, with RCON off unless the host forwards a port.
- **Uploading the server program.** For a VPS, LaunchHeim uploads its SteamCMD download for the host's
  OS and keeps the execute bits (SFTP `chmod`, FTP `SITE CHMOD`). It never starts anything: FTP and
  SFTP can't, so the UI points to the host's panel or a shell.
- **The plugin writes a heartbeat.** `BepInEx/config/LaunchHeim.Server/status.json` is written every
  15 s once the world is loaded, and with `stopped: true` when the server quits. A hosted server is
  online if Steam's query answers, else if the heartbeat is younger than 75 s. That is wide enough for
  clock skew between host and PC.
- **Console and log.** Commands go over RCON when there's a port, else through the file channel with
  60 s patience. The log is followed through the files every 3 s (`ILogSource`/`ServerFilesLogSource`,
  the same `LogFollower` as for local logs).

**Alternatives.**
- Hashing remote files: FTP has no standard way (XCRC/HASH are optional), SFTP neither without a shell.
- Comparing remote modification times: host time zones and FTP's `MDTM` handling vary.
- Asking for SSH exec on a VPS: deferred to the SSH item. Many VPS logins are SFTP-only (chrooted).
- Accepting any certificate or host key silently: convenient, but one DNS mistake would hand the login
  to a stranger.

**Consequences.**
- Status of a hosted private server lags up to 15 s, and depends on the plugin being loaded. Without
  the plugin only a public server's Steam query shows it.
- A host whose panel replaces BepInEx or the server folder needs the mods uploaded again; "Everything
  again" ignores the record.
- G-Portal (item 3) is a preset on top of this: its FTP layout, log path and panel.
- Tested on 2026-11-07 against podman containers (atmoz/sftp, delfer/alpine-ftp-server, rootless with
  `--user root --userns=keep-id`). The real dedicated server was uploaded (993 files, 2.1 GB) and run
  from the container's folder, then managed only through SFTP and then FTP: status, commands, log and
  stop.

Related: [[0019-launchheim-server-manager]], [[0020-launchheim-server-plugin-speaks-source-rcon]], [[server-manager]]
