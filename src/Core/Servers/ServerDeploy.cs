using System.Text.Json;
using CINE.LaunchHeim.Core.Logging;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>A log at a host, read through its files for the console (<see cref="LogFollower"/>).</summary>
/// <remarks>Read every few seconds, not every 300 ms: each look is a round trip, and a host may limit them.</remarks>
public sealed class ServerFilesLogSource(IServerFiles files, string path) : ILogSource
{
  public string Path => $"{files.Describe.TrimEnd('/')}/{path}";

  public TimeSpan PollInterval => TimeSpan.FromSeconds(3);

  public long? Length() => files.SizeAsync(path, CancellationToken.None).GetAwaiter().GetResult();

  public byte[] Read(long offset, int count) => files.ReadRangeAsync(path, offset, count, CancellationToken.None).GetAwaiter().GetResult();
}

/// <summary>One file to put on a server: where it goes, and the local file it comes from.</summary>
/// <param name="Path">Inside the server's folder, forward slashes.</param>
/// <param name="IsConfig">Only uploaded while the server has no file of that name, unless configs are replaced.</param>
public sealed record DeployFile(string Path, string Source, bool Executable = false, bool IsConfig = false);

/// <summary>What a deploy did.</summary>
public sealed record DeployReport(int Uploaded, int Unchanged, int Removed, long Bytes);

/// <summary>Progress of a deploy: files done of all, and the file at hand.</summary>
public readonly record struct DeployProgress(int Done, int Total, string File);

/// <summary>Brings a server's files at a host in line with a list of local files, uploading only what changed.</summary>
/// <remarks>
/// <para>
/// FTP can't hash a file on the server, and hosts' clocks and time zones make modification times
/// unreliable. So LaunchHeim keeps its own record at the host, <c>.launchheim/&lt;name&gt;.json</c>: for
/// every file it uploaded, the local file's size and modification time. A file is uploaded again when
/// its local stamp differs, and a file in the record that's no longer wanted (a mod taken out of the
/// instance) is deleted. Nothing LaunchHeim didn't upload is ever deleted. A file changed at the host
/// behind LaunchHeim's back isn't noticed; "Upload everything again" (<paramref name="force"/>) covers that.
/// </para>
/// <para>
/// Mods and the server program keep separate records, so updating one never removes the other.
/// Configs follow <see cref="ServerMods"/>' rule: they're only uploaded while the host has none of that
/// name, because the server's config is the one that counts.
/// </para>
/// </remarks>
public static class ServerDeploy
{
  public const string RecordDirectory = ".launchheim";

  public static async Task<DeployReport> RunAsync(
    IServerFiles files,
    string recordName,
    IReadOnlyList<DeployFile> wanted,
    bool replaceConfigs,
    bool force,
    IProgress<DeployProgress>? progress,
    CancellationToken cancellationToken)
  {
    var recordPath = $"{RecordDirectory}/{recordName}.json";
    var record = await ReadRecordAsync(files, recordPath, cancellationToken);
    var next = new Dictionary<string, string>(StringComparer.Ordinal);
    int uploaded = 0, unchanged = 0, removed = 0, done = 0;
    long bytes = 0;

    foreach (var file in wanted)
    {
      cancellationToken.ThrowIfCancellationRequested();
      progress?.Report(new DeployProgress(done++, wanted.Count, file.Path));
      var info = new FileInfo(file.Source);
      if (!info.Exists)
      {
        continue;
      }

      if (file.IsConfig)
      {
        // Never recorded: a config stays when its mod goes, as BepInEx leaves them.
        if (replaceConfigs || await files.SizeAsync(file.Path, cancellationToken) is null)
        {
          await files.UploadAsync(file.Source, file.Path, file.Executable, cancellationToken);
          uploaded++;
          bytes += info.Length;
        }
        else
        {
          unchanged++;
        }

        continue;
      }

      var stamp = Stamp(info);
      next[file.Path] = stamp;
      if (!force && record.GetValueOrDefault(file.Path) == stamp)
      {
        unchanged++;
        continue;
      }

      await files.UploadAsync(file.Source, file.Path, file.Executable, cancellationToken);
      uploaded++;
      bytes += info.Length;
      // Saved now and then, so an interrupted upload of the 2 GB server program resumes instead of restarting.
      if (uploaded % 50 == 0)
      {
        await WriteRecordAsync(files, recordPath, Merge(record, next), cancellationToken);
      }
    }

    foreach (var stale in record.Keys.Where(path => !next.ContainsKey(path)).ToList())
    {
      cancellationToken.ThrowIfCancellationRequested();
      await files.DeleteAsync(stale, cancellationToken);
      removed++;
    }

    progress?.Report(new DeployProgress(wanted.Count, wanted.Count, ""));
    await WriteRecordAsync(files, recordPath, next, cancellationToken);
    Log.Info($"Deployed {recordName} to {files.Describe}: {uploaded} uploaded ({bytes / 1024} KB), {unchanged} unchanged, {removed} removed");
    return new DeployReport(uploaded, unchanged, removed, bytes);
  }

  /// <summary>Every file in a local folder, for uploading the whole dedicated server.</summary>
  /// <remarks>
  /// Steam's own bookkeeping (<c>steamapps/</c>) stays home. Linux files with an execute bit keep it,
  /// so the host can run <c>valheim_server.x86_64</c> and the start scripts.
  /// </remarks>
  public static IReadOnlyList<DeployFile> Folder(string directory)
  {
    return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
      .Select(file => (File: file, Relative: Path.GetRelativePath(directory, file).Replace('\\', '/')))
      .Where(f => !f.Relative.StartsWith("steamapps/", StringComparison.Ordinal))
      .OrderBy(f => f.Relative, StringComparer.Ordinal)
      .Select(f => new DeployFile(f.Relative, f.File, IsExecutable(f.File)))
      .ToList();
  }

  /// <summary>Size and time to the second; a mod update or a server update changes at least one.</summary>
  internal static string Stamp(FileInfo info) => $"{info.Length}:{info.LastWriteTimeUtc.Ticks / TimeSpan.TicksPerSecond}";

  private static bool IsExecutable(string file) =>
    !OperatingSystem.IsWindows() && (File.GetUnixFileMode(file) & UnixFileMode.UserExecute) != 0;

  private static Dictionary<string, string> Merge(Dictionary<string, string> previous, Dictionary<string, string> next)
  {
    var merged = new Dictionary<string, string>(previous, StringComparer.Ordinal);
    foreach (var (path, stamp) in next)
    {
      merged[path] = stamp;
    }

    return merged;
  }

  private static async Task<Dictionary<string, string>> ReadRecordAsync(IServerFiles files, string path, CancellationToken cancellationToken)
  {
    try
    {
      return await files.ReadTextAsync(path, cancellationToken) is { } json
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []
        : [];
    }
    catch (JsonException)
    {
      // A damaged record only means everything is uploaded once more.
      return [];
    }
  }

  private static Task WriteRecordAsync(IServerFiles files, string path, Dictionary<string, string> record, CancellationToken cancellationToken) =>
    files.WriteTextAsync(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
}
