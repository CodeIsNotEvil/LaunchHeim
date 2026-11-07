using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Logging;

namespace CINE.LaunchHeim.Core.Servers.Backups;

/// <summary>One backup somewhere: in the local folder, in the cloud, or both.</summary>
/// <param name="Label">Set for a safety copy (<c>before-restore</c>), which retention never removes.</param>
public sealed record BackupEntry(string Name, DateTimeOffset CreatedAt, long Size, string? Label, bool InFolder, bool InCloud);

/// <summary>Where backups are kept: a folder, an rclone remote, or Proton Drive.</summary>
public interface IBackupTarget
{
  /// <summary>Where it is, for messages.</summary>
  string Describe { get; }

  Task PutAsync(string localFile, string name, CancellationToken cancellationToken);

  /// <summary>The backup files there (names and sizes); the caller picks the ones that are this server's.</summary>
  Task<IReadOnlyList<(string Name, long Size)>> ListAsync(CancellationToken cancellationToken);

  Task GetAsync(string name, string localFile, CancellationToken cancellationToken);

  Task RemoveAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Which backups to remove: all but the newest few and the newest of each recent day.</summary>
/// <remarks>
/// Day boundaries are UTC, like the names. Safety copies made before a restore (labelled) are never
/// removed here; they're the way back from a restore that went wrong.
/// </remarks>
public static class BackupRetention
{
  public static IReadOnlyList<BackupEntry> ToRemove(IEnumerable<BackupEntry> backups, DateTimeOffset now, int keepLast, int keepDays)
  {
    var regular = backups.Where(b => b.Label is null).OrderByDescending(b => b.CreatedAt).ToList();
    var keep = new HashSet<string>(regular.Take(Math.Max(1, keepLast)).Select(b => b.Name), StringComparer.Ordinal);
    var since = now.UtcDateTime.Date.AddDays(-(Math.Max(0, keepDays) - 1));
    foreach (var day in regular.Where(b => b.CreatedAt.UtcDateTime >= since).GroupBy(b => b.CreatedAt.UtcDateTime.Date))
    {
      keep.Add(day.First().Name);
    }

    return regular.Where(b => !keep.Contains(b.Name)).ToList();
  }
}

/// <summary>A folder on this PC: LaunchHeim's own, another drive, or a mounted NAS share.</summary>
public sealed class FolderBackupTarget(string folder) : IBackupTarget
{
  public string Folder => folder;

  public string Describe => folder;

  public Task PutAsync(string localFile, string name, CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(folder);
    var target = Path.Combine(folder, name);
    if (Path.GetFullPath(localFile) != Path.GetFullPath(target))
    {
      File.Copy(localFile, target + ".tmp", overwrite: true);
      File.Move(target + ".tmp", target, overwrite: true);
    }

    return Task.CompletedTask;
  }

  public Task<IReadOnlyList<(string Name, long Size)>> ListAsync(CancellationToken cancellationToken)
  {
    IReadOnlyList<(string, long)> files = Directory.Exists(folder)
      ? new DirectoryInfo(folder).EnumerateFiles("*.zip").Select(f => (f.Name, f.Length)).ToList()
      : [];
    return Task.FromResult(files);
  }

  public Task GetAsync(string name, string localFile, CancellationToken cancellationToken)
  {
    File.Copy(Path.Combine(folder, name), localFile, overwrite: true);
    return Task.CompletedTask;
  }

  public Task RemoveAsync(string name, CancellationToken cancellationToken)
  {
    File.Delete(Path.Combine(folder, name));
    return Task.CompletedTask;
  }
}

/// <summary>An rclone remote, <c>remote:path</c>, set up beforehand with <c>rclone config</c>.</summary>
/// <remarks>
/// rclone reaches some 70 storage systems (Google Drive, OneDrive, Dropbox, Nextcloud/WebDAV, S3, a NAS
/// over SFTP or SMB …) with the user's own configuration, so LaunchHeim handles no cloud logins. Its
/// Proton Drive backend is marked beta and had login trouble in 2026; Proton's own CLI is the target for
/// that (<see cref="ProtonDriveBackupTarget"/>).
/// </remarks>
public sealed class RcloneBackupTarget(string remote, string tool) : IBackupTarget
{
  public string Describe => remote;

  public Task PutAsync(string localFile, string name, CancellationToken cancellationToken) =>
    BackupTool.RunAsync(tool, ["copyto", localFile, Join(name)], cancellationToken);

  public async Task<IReadOnlyList<(string Name, long Size)>> ListAsync(CancellationToken cancellationToken)
  {
    // A folder that doesn't exist yet lists as an error on some backends and empty on others.
    var (code, output, _) = await BackupTool.TryRunAsync(tool, ["lsjson", "--files-only", remote], cancellationToken);
    return code == 0 ? ParseList(output) : [];
  }

  public Task GetAsync(string name, string localFile, CancellationToken cancellationToken) =>
    BackupTool.RunAsync(tool, ["copyto", Join(name), localFile], cancellationToken);

  public Task RemoveAsync(string name, CancellationToken cancellationToken) =>
    BackupTool.RunAsync(tool, ["deletefile", Join(name)], cancellationToken);

  /// <summary>rclone's <c>lsjson</c>: an array of objects with <c>Name</c>, <c>Size</c> and <c>IsDir</c>.</summary>
  internal static IReadOnlyList<(string Name, long Size)> ParseList(string json)
  {
    try
    {
      using var document = JsonDocument.Parse(json);
      return document.RootElement.EnumerateArray()
        .Where(item => !(item.TryGetProperty("IsDir", out var dir) && dir.GetBoolean()))
        .Select(item => (item.GetProperty("Name").GetString() ?? "", item.TryGetProperty("Size", out var size) ? size.GetInt64() : 0L))
        .ToList();
    }
    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
    {
      throw new BackupToolException($"rclone's list couldn't be read: {ex.Message}");
    }
  }

  private string Join(string name) => remote.EndsWith(':') || remote.EndsWith('/') ? remote + name : $"{remote}/{name}";
}

/// <summary>Proton Drive through Proton's own CLI, <c>proton-drive</c> (released June 2026).</summary>
/// <remarks>
/// <para>
/// The player installs the CLI and signs in once with <c>proton-drive auth login</c> (a browser flow; the
/// session goes to the system keychain), so LaunchHeim never sees the Proton password. Commands from the
/// CLI's source (ProtonDriveApps/sdk, <c>cli/src/commands/fileSystem</c>, 2026-11-07):
/// <c>filesystem upload &lt;local&gt; &lt;parent&gt;</c>, <c>filesystem list &lt;path&gt; --json</c>,
/// <c>filesystem download &lt;path&gt; &lt;localDir&gt;</c>, <c>filesystem create-folder &lt;parent&gt; &lt;name&gt;</c>,
/// <c>filesystem trash &lt;path&gt;</c>. Conflicts are settled with <c>--file-conflict-strategy replace</c>.
/// </para>
/// <para>
/// Old backups are moved to Proton Drive's trash rather than deleted: the CLI only deletes from the
/// trash, and a trash the player empties is the safer way round anyway. Not yet tried against a real
/// account (the second brain's server-manager note).
/// </para>
/// </remarks>
public sealed class ProtonDriveBackupTarget(string folder, string tool) : IBackupTarget
{
  private bool _folderReady;

  public string Describe => "Proton Drive " + folder;

  public async Task PutAsync(string localFile, string name, CancellationToken cancellationToken)
  {
    await EnsureFolderAsync(cancellationToken);
    // The CLI names the upload after the local file, so it has to carry the backup's name.
    var named = Path.Combine(Path.GetDirectoryName(localFile)!, name);
    var copied = !string.Equals(Path.GetFileName(localFile), name, StringComparison.Ordinal);
    if (copied)
    {
      File.Copy(localFile, named, overwrite: true);
    }

    try
    {
      await BackupTool.RunAsync(tool, ["filesystem", "upload", named, folder, "--file-conflict-strategy", "replace", "--skip-thumbnails", "--json"], cancellationToken);
    }
    finally
    {
      if (copied)
      {
        File.Delete(named);
      }
    }
  }

  public async Task<IReadOnlyList<(string Name, long Size)>> ListAsync(CancellationToken cancellationToken)
  {
    var (code, output, _) = await BackupTool.TryRunAsync(tool, ["filesystem", "list", folder, "--json"], cancellationToken);
    return code == 0 ? ParseList(output) : [];
  }

  public async Task GetAsync(string name, string localFile, CancellationToken cancellationToken)
  {
    var directory = Path.GetDirectoryName(localFile)!;
    await BackupTool.RunAsync(tool, ["filesystem", "download", $"{folder}/{name}", directory, "--file-conflict-strategy", "replace", "--json"], cancellationToken);
    var downloaded = Path.Combine(directory, name);
    if (downloaded != localFile)
    {
      File.Move(downloaded, localFile, overwrite: true);
    }
  }

  public Task RemoveAsync(string name, CancellationToken cancellationToken) =>
    BackupTool.RunAsync(tool, ["filesystem", "trash", $"{folder}/{name}", "--json"], cancellationToken);

  /// <summary>
  /// The CLI's <c>--json</c> list: an array of Drive nodes. A node's name comes as a string, or as the
  /// SDK's result object <c>{"ok": true, "value": "…"}</c>; its size as <c>claimedSize</c>, at the top or in
  /// <c>activeRevision</c>. Read leniently, since the format isn't documented.
  /// </summary>
  internal static IReadOnlyList<(string Name, long Size)> ParseList(string json)
  {
    var start = json.IndexOf('[');
    if (start < 0)
    {
      return [];
    }

    try
    {
      return ParseNodes(json[start..]);
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new BackupToolException($"Proton Drive's list couldn't be read: {ex.Message}");
    }
  }

  private static IReadOnlyList<(string Name, long Size)> ParseNodes(string json)
  {
    using var document = JsonDocument.Parse(json);
    var result = new List<(string, long)>();
    foreach (var node in document.RootElement.EnumerateArray())
    {
      var name = node.TryGetProperty("name", out var n)
        ? n.ValueKind == JsonValueKind.String ? n.GetString() : n.TryGetProperty("value", out var v) ? v.GetString() : null
        : null;
      if (name is null || node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "folder")
      {
        continue;
      }

      long size = 0;
      if (node.TryGetProperty("activeRevision", out var revision) && revision.ValueKind == JsonValueKind.Object && revision.TryGetProperty("claimedSize", out var claimed) && claimed.ValueKind == JsonValueKind.Number)
      {
        size = claimed.GetInt64();
      }
      else if (node.TryGetProperty("claimedSize", out var top) && top.ValueKind == JsonValueKind.Number)
      {
        size = top.GetInt64();
      }

      result.Add((name, size));
    }

    return result;
  }

  /// <summary>Creates the folder path step by step; an existing step answers with an error, which is fine.</summary>
  private async Task EnsureFolderAsync(CancellationToken cancellationToken)
  {
    if (_folderReady)
    {
      return;
    }

    var parts = folder.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    // The first part is the Drive's root (/my-files), which always exists.
    var parent = "/" + parts.FirstOrDefault();
    foreach (var part in parts.Skip(1))
    {
      await BackupTool.TryRunAsync(tool, ["filesystem", "create-folder", parent, part, "--json"], cancellationToken);
      parent = $"{parent}/{part}";
    }

    _folderReady = true;
  }
}

public sealed class BackupToolException(string message) : IOException(message);

/// <summary>Runs rclone or proton-drive and reads what they print.</summary>
public static class BackupTool
{
  public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

  /// <summary>Where a tool is: the path chosen in the settings, else on PATH or in the usual places.</summary>
  /// <returns>Null when it's nowhere to be found.</returns>
  public static string? Find(string name, string? chosen)
  {
    if (!string.IsNullOrWhiteSpace(chosen))
    {
      return File.Exists(chosen) ? chosen : null;
    }

    var executable = OperatingSystem.IsWindows() ? name + ".exe" : name;
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var places = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
      .Append(Path.Combine(home, ".local", "bin"))
      .Append(Path.Combine(home, "bin"))
      .Append(Path.Combine(home, "Downloads"));
    return places.Select(p => Path.Combine(p, executable)).FirstOrDefault(File.Exists);
  }

  public static async Task<string> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
  {
    var (code, output, errors) = await TryRunAsync(tool, arguments, cancellationToken);
    return code == 0 ? output : throw new BackupToolException($"{Path.GetFileName(tool)} {arguments.FirstOrDefault()} failed: {LastLines(errors + "\n" + output)}");
  }

  /// <returns>
  /// The exit code, standard output and standard error apart: rclone writes notices to standard error
  /// ("Config file not found") even when it succeeds, and those must not end up in the JSON it prints.
  /// </returns>
  public static async Task<(int Code, string Output, string Errors)> TryRunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
  {
    var info = new ProcessStartInfo(tool)
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = true,
      CreateNoWindow = true,
    };
    foreach (var argument in arguments)
    {
      info.ArgumentList.Add(argument);
    }

    foreach (var variable in GameLauncher.HostOnlyVariables)
    {
      info.Environment.Remove(variable);
    }

    Process process;
    try
    {
      process = Process.Start(info) ?? throw new BackupToolException($"{tool} didn't start.");
    }
    catch (System.ComponentModel.Win32Exception ex)
    {
      throw new BackupToolException($"{tool} can't be started: {ex.Message}");
    }

    using (process)
    {
      // Nothing to answer: a tool asking a question (a login, a conflict) gets end of input and gives up.
      process.StandardInput.Close();
      using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      limit.CancelAfter(Timeout);
      var output = process.StandardOutput.ReadToEndAsync(limit.Token);
      var error = process.StandardError.ReadToEndAsync(limit.Token);
      try
      {
        await process.WaitForExitAsync(limit.Token);
      }
      catch (OperationCanceledException)
      {
        process.Kill(entireProcessTree: true);
        throw new BackupToolException($"{Path.GetFileName(tool)} took longer than {Timeout.TotalMinutes} minutes and was stopped.");
      }

      Log.Info($"{Path.GetFileName(tool)} {string.Join(' ', arguments.Take(2))} exited with {process.ExitCode}");
      return (process.ExitCode, await output, await error);
    }
  }

  private static string LastLines(string output)
  {
    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return lines.Length == 0 ? "no output" : string.Join(" ", lines.TakeLast(3));
  }
}
