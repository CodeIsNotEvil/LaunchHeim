---
tags: [decision, launchheim, servers, backups]
created: 2026-11-07
updated: 2026-11-07
status: active
supersedes:
---
# 0022: Server backups are zips through the server's files, to a folder and optionally rclone or Proton Drive

**Context.** Item 4 of the server manager ([[server-manager]]). The owner asked for a backup plan for
local servers: a zipped copy on another drive or a NAS, or a cloud such as Proton Drive through its
CLI. It should work on every server whose files LaunchHeim can reach. In June 2026 Proton released an
official Drive CLI (`proton-drive`, built on the Drive SDK). rclone's Proton Drive backend is still
marked beta, and its forum showed login and key-unlock failures in 2026.

**Decision.** (2026-11-07)
- **One zip per backup**, read through `IServerFiles`, so local, FTP and SFTP servers back up alike.
  - Contents: `saves/` (the worlds in `worlds_local`, both the `<World>.fwl/.db` pair and the
    `<World>/` folder; the admin, ban and permitted lists), optionally `BepInEx/config/`, and
    `launchheim-backup.json`.
  - Valheim's `_backup_` copies stay out.
  - A save folder that may be shared (a chosen one, such as the game's own) only gets the server's
    world.
- **Names are `<serverId>-<yyyyMMdd-HHmmss>[-label].zip` in UTC.** They carry the time, so every target
  can be listed and thinned out without metadata.
- **Consistent copies.** A running server is told to `save` first. LaunchHeim's plugin saves
  synchronously and answers "The world is saved." For other RCON mods, LaunchHeim waits for the log's
  "World save (5/5) done", or copies after 60 s and says so.
- **Targets.**
  - A folder is always written first: LaunchHeim's `backups/<server>`, or another drive or a mounted
    NAS share.
  - Then, optionally, an rclone remote (`copyto`, `lsjson`, `deletefile`), so LaunchHeim handles no
    cloud logins.
  - Or Proton Drive through Proton's CLI: `filesystem upload/list --json/download/trash`, read from the
    CLI's source (ProtonDriveApps/sdk). Old backups go to Proton's trash, because the CLI only deletes
    from the trash.
  - A tool's path can be chosen; otherwise PATH and `~/.local/bin` are searched.
- **Retention.** The newest N, plus the newest of each of the last D days (UTC), per target. Safety
  copies made before a restore (`-before-restore`) are never removed.
- **When.**
  - Back up now.
  - A schedule checked every minute while the window is open, for servers that run.
  - After every clean local stop.
  - `launchheim backup <server>|--all` for a systemd timer or a scheduled task.
  - One at a time across the app (`BackupGate`).
- **Restore.** It refuses a running server, makes a safety backup, and deletes the restored worlds'
  current files before writing the backup's. Valheim loads the highest `_main.<n>`, so a newer save
  left in place would win. Zip entries with `..` are refused.

**Alternatives.**
- rclone's protondrive backend for Proton: beta, with 2026 reports of login failures. Proton's own CLI
  is the supported way.
- Valheim's own `-backups`: it stays on, but it lives on the same disk.
- Incremental or deduplicated backups (restic, borg): a world is a few hundred KB to a few hundred MB,
  so plain zips with retention are simpler to restore by hand.

**Consequences.**
- The Proton Drive target hasn't been tried against a real account: no Proton login on the test
  machine. The rclone target was tried with rclone 1.76 against a local remote.
- With the window closed, backups need the user's own timer. The UI shows the command.

Related: [[0019-launchheim-server-manager]], [[0021-launchheim-hosted-servers-through-their-files]], [[server-manager]]
