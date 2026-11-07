using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Servers.Backups;

/// <summary>Backs servers up, lists their backups and restores one: what the window and the command line call.</summary>
/// <remarks>
/// <para>
/// A backup is written to the local folder first (<see cref="BackupSettings.Folder"/>, LaunchHeim's own
/// by default), then copied to the cloud when one is set, and both are thinned out afterwards
/// (<see cref="BackupRetention"/>). A running server is asked to <c>save</c> first, and the copy waits for
/// the log's "World save … done", so the zip never catches a world halfway through a save. Without the
/// plugin, or when the save doesn't show up in time, it copies what's on disk and says so.
/// </para>
/// <para>
/// A restore refuses a running server: its next save would overwrite the restored world. It makes a
/// safety backup first (labelled <c>before-restore</c>, kept by retention), so a restore can be undone.
/// </para>
/// </remarks>
public sealed class BackupService(ServerManager manager, AppPaths paths)
{
  public const string BeforeRestore = "before-restore";

  /// <summary>How long a save may take before the backup copies without waiting for it.</summary>
  private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(60);

  public string FolderOf(ServerProfile server) =>
    string.IsNullOrWhiteSpace(server.Backup.Folder) ? Path.Combine(paths.DataDirectory, "backups", server.Id) : server.Backup.Folder;

  /// <summary>The cloud target, or null for none.</summary>
  /// <exception cref="BackupToolException">The program for it isn't installed.</exception>
  public IBackupTarget? CloudOf(ServerProfile server)
  {
    var settings = server.Backup;
    if (settings.Cloud == BackupCloud.None)
    {
      return null;
    }

    if (string.IsNullOrWhiteSpace(settings.CloudPath))
    {
      throw new BackupToolException(settings.Cloud == BackupCloud.Rclone
        ? "Enter the rclone remote to copy backups to, such as gdrive:Valheim."
        : "Enter the Proton Drive folder to copy backups to, such as /my-files/Valheim backups.");
    }

    var name = settings.Cloud == BackupCloud.Rclone ? "rclone" : "proton-drive";
    var tool = BackupTool.Find(name, settings.ToolPath)
      ?? throw new BackupToolException(settings.Cloud == BackupCloud.Rclone
        ? "rclone isn't installed (or not where the settings say). Install it from rclone.org or your package manager, and set up the remote with rclone config."
        : "Proton's proton-drive program isn't installed (or not where the settings say). Download it from proton.me/download/drive/cli and sign in once with: proton-drive auth login");
    return settings.Cloud == BackupCloud.Rclone
      ? new RcloneBackupTarget(settings.CloudPath.Trim(), tool)
      : new ProtonDriveBackupTarget(settings.CloudPath.Trim().TrimEnd('/'), tool);
  }

  public async Task<BackupEntry> BackupAsync(
    ServerProfile server,
    string reason,
    IProgress<DeployProgress>? progress,
    CancellationToken cancellationToken,
    string? label = null,
    bool saveFirst = true)
  {
    var cloud = CloudOf(server);
    var saved = saveFirst && await SaveAndWaitAsync(server, cancellationToken);
    var folder = FolderOf(server);
    Directory.CreateDirectory(folder);
    var now = DateTimeOffset.UtcNow;
    var name = ServerBackup.FileName(server.Id, now, label);
    var file = Path.Combine(folder, name);
    var (saves, configs, world) = await OpenAsync(server, cancellationToken);
    BackupManifest manifest;
    await using (saves)
    await using (configs)
    {
      await using (var output = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write))
      {
        manifest = await ServerBackup.CreateAsync(saves, server.Backup.IncludeConfigs ? configs : null, world, server.Name, reason, saved, output, progress, cancellationToken);
      }

      File.Move(file + ".tmp", file, overwrite: true);
    }

    var size = new FileInfo(file).Length;
    Log.Info($"Backed up {server.Name} ({reason}): {manifest.Files} files, {string.Join(", ", manifest.Worlds)}, {size / 1024} KB into {file}{(saved ? "" : ", without asking the server to save first")}");
    if (cloud is not null)
    {
      await cloud.PutAsync(file, name, cancellationToken);
    }

    await PruneAsync(server, cloud, now, cancellationToken);
    server.Backup.LastBackupAt = now;
    manager.Store.Save(server);
    return new BackupEntry(name, now, size, label, InFolder: true, InCloud: cloud is not null);
  }

  /// <summary>Every backup of the server, in the folder and in the cloud, newest first.</summary>
  /// <param name="includeCloud">Also ask the cloud, which takes a moment; off for a quick look.</param>
  public async Task<IReadOnlyList<BackupEntry>> ListAsync(ServerProfile server, bool includeCloud, CancellationToken cancellationToken)
  {
    var entries = new Dictionary<string, BackupEntry>(StringComparer.Ordinal);
    foreach (var (name, size) in await new FolderBackupTarget(FolderOf(server)).ListAsync(cancellationToken))
    {
      if (ServerBackup.TimeOf(server.Id, name) is var (at, label))
      {
        entries[name] = new BackupEntry(name, at, size, label, InFolder: true, InCloud: false);
      }
    }

    if (includeCloud && CloudOf(server) is { } cloud)
    {
      foreach (var (name, size) in await cloud.ListAsync(cancellationToken))
      {
        if (ServerBackup.TimeOf(server.Id, name) is var (at, label))
        {
          entries[name] = entries.TryGetValue(name, out var local)
            ? local with { InCloud = true }
            : new BackupEntry(name, at, size, label, InFolder: false, InCloud: true);
        }
      }
    }

    return entries.Values.OrderByDescending(e => e.CreatedAt).ToList();
  }

  public async Task<RestoreReport> RestoreAsync(ServerProfile server, string name, IProgress<DeployProgress>? progress, CancellationToken cancellationToken)
  {
    var state = await manager.StateAsync(server, cancellationToken);
    if (state.Running)
    {
      throw new BackupException($"{server.Name} is running. Stop it first: its next save would overwrite the restored world.");
    }

    var folder = FolderOf(server);
    var file = Path.Combine(folder, name);
    var temporary = Path.Combine(paths.CacheDirectory, "tmp", "restore-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporary);
    try
    {
      if (!File.Exists(file))
      {
        var cloud = CloudOf(server) ?? throw new BackupException($"{name} isn't in {folder}.");
        file = Path.Combine(temporary, name);
        await cloud.GetAsync(name, file, cancellationToken);
      }

      if (ServerBackup.ReadManifest(file) is null)
      {
        throw new BackupException($"{name} isn't a LaunchHeim backup.");
      }

      // The way back, should this restore be the wrong one.
      await BackupAsync(server, "before restoring " + name, null, cancellationToken, BeforeRestore, saveFirst: false);

      var (saves, configs, _) = await OpenAsync(server, cancellationToken);
      await using (saves)
      await using (configs)
      {
        var report = await ServerBackup.RestoreAsync(file, saves, configs, Path.Combine(temporary, "files"), progress, cancellationToken);
        Log.Info($"Restored {name} to {server.Name}: {report.Files} files, {string.Join(", ", report.Worlds)}");
        return report;
      }
    }
    finally
    {
      try
      {
        Directory.Delete(temporary, recursive: true);
      }
      catch (IOException)
      {
      }
    }
  }

  /// <summary>Looks for a hosted server's save folder and stores it in the profile when found.</summary>
  public async Task<string?> FindSaveDirectoryAsync(ServerProfile server, CancellationToken cancellationToken)
  {
    var opened = new List<IServerFiles>();
    try
    {
      var found = await ServerBackup.FindSaveDirectoryAsync(path =>
      {
        var files = manager.HostFilesAt(server, path);
        opened.Add(files);
        return files;
      }, cancellationToken);
      if (found is not null && server.Host is { } host)
      {
        host.SaveDirectory = found;
        manager.Store.Save(server);
      }

      return found;
    }
    finally
    {
      foreach (var files in opened)
      {
        await files.DisposeAsync();
      }
    }
  }

  /// <summary>Asks a running server to save and waits for the log to say it's done. False when it couldn't be confirmed.</summary>
  private async Task<bool> SaveAndWaitAsync(ServerProfile server, CancellationToken cancellationToken)
  {
    if (!server.ConsoleEnabled || !(await manager.StateAsync(server, cancellationToken)).Running)
    {
      return false;
    }

    try
    {
      var log = manager.LogSourceOf(server);
      var start = await Task.Run(log.Length, cancellationToken) ?? 0;
      await manager.SendAsync(server, "save", cancellationToken);
      var deadline = DateTime.UtcNow + SaveTimeout;
      while (DateTime.UtcNow < deadline)
      {
        await Task.Delay(TimeSpan.FromSeconds(server.IsHosted ? 3 : 1), cancellationToken);
        var length = await Task.Run(log.Length, cancellationToken) ?? 0;
        if (length > start)
        {
          var text = System.Text.Encoding.UTF8.GetString(await Task.Run(() => log.Read(start, (int)Math.Min(length - start, 1024 * 1024)), cancellationToken));
          // "World save (5/5) done. Total time [13ms]" in Valheim 1.0.21; "World saved" in older versions.
          if (text.Contains("World save (5/5) done", StringComparison.Ordinal) || text.Contains("World saved", StringComparison.Ordinal))
          {
            return true;
          }
        }
      }

      Log.Warning($"{server.Name} didn't log a finished save within {SaveTimeout.TotalSeconds} s; backing up what's on disk.");
    }
    catch (Exception ex) when (ex is RconException or IOException)
    {
      Log.Warning($"Couldn't ask {server.Name} to save before the backup: {ex.Message}");
    }

    return false;
  }

  /// <summary>
  /// The save folder, the server's folder (for configs) and the world to back up: only the profile's
  /// world when the save folder may be shared with others (a chosen folder, such as the game's own).
  /// </summary>
  private async Task<(IServerFiles Saves, IServerFiles Configs, string? World)> OpenAsync(ServerProfile server, CancellationToken cancellationToken)
  {
    if (!server.IsHosted)
    {
      var shared = !string.IsNullOrWhiteSpace(server.SaveDirectory);
      return (new LocalServerFiles(manager.Store.SaveDirectoryOf(server)), new LocalServerFiles(manager.Store.DirectoryOf(server)), shared ? server.World : null);
    }

    var host = server.Host ?? throw new BackupException("The server has no connection settings.");
    var saveDirectory = host.SaveDirectory.Length > 0 || host.SaveDirectory == "" && await IsSaveDirectoryAsync(server, "", cancellationToken)
      ? host.SaveDirectory
      : await FindSaveDirectoryAsync(server, cancellationToken)
        ?? throw new BackupException("LaunchHeim didn't find the worlds at the host. Enter the folder with worlds_local in it under the server's Backups.");
    return (manager.HostFilesAt(server, saveDirectory), manager.HostFiles(server), null);
  }

  private async Task<bool> IsSaveDirectoryAsync(ServerProfile server, string folder, CancellationToken cancellationToken)
  {
    await using var files = manager.HostFilesAt(server, folder);
    return (await files.ListEntriesAsync("worlds_local", cancellationToken)).Count > 0;
  }

  private async Task PruneAsync(ServerProfile server, IBackupTarget? cloud, DateTimeOffset now, CancellationToken cancellationToken)
  {
    var settings = server.Backup;
    foreach (var target in new IBackupTarget?[] { new FolderBackupTarget(FolderOf(server)), cloud }.OfType<IBackupTarget>())
    {
      try
      {
        var mine = (await target.ListAsync(cancellationToken))
          .Select(f => (f.Name, f.Size, Time: ServerBackup.TimeOf(server.Id, f.Name)))
          .Where(f => f.Time is not null)
          .Select(f => new BackupEntry(f.Name, f.Time!.Value.At, f.Size, f.Time.Value.Label, InFolder: true, InCloud: false));
        foreach (var old in BackupRetention.ToRemove(mine, now, settings.KeepLast, settings.KeepDays))
        {
          await target.RemoveAsync(old.Name, cancellationToken);
          Log.Info($"Removed old backup {old.Name} from {target.Describe}");
        }
      }
      catch (IOException ex)
      {
        // Thinning out can wait for the next backup; the backup itself is done.
        Log.Warning($"Couldn't remove old backups from {target.Describe}: {ex.Message}");
      }
    }
  }
}
