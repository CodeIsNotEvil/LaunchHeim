using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CINE.LaunchHeim.Core.Servers.Backups;

/// <summary>What a backup zip says about itself (<c>launchheim-backup.json</c> inside it).</summary>
public sealed record BackupManifest(string Server, IReadOnlyList<string> Worlds, DateTimeOffset CreatedAt, string Reason, bool SavedFirst, int Files, long Bytes);

/// <summary>What a restore put back.</summary>
public sealed record RestoreReport(IReadOnlyList<string> Worlds, int Files);

/// <summary>Zips a server's worlds and settings, and puts them back, through its files wherever they are.</summary>
/// <remarks>
/// <para>
/// Inside the zip, <c>saves/</c> mirrors the server's save folder (<c>-savedir</c>): the worlds in
/// <c>worlds_local/</c>, as the older <c>&lt;World&gt;.fwl</c>/<c>.db</c> pair or the current
/// <c>&lt;World&gt;/</c> folder, and the admin, ban and permitted lists. <c>BepInEx/config/</c> holds the mods'
/// settings when they're included. Valheim's own backups (<c>_backup_</c> in the name) stay out: they
/// are older copies of the same world and would double the size. Everything is read through
/// <see cref="IServerFiles"/>, so a local server, an FTP host and a VPS back up alike.
/// </para>
/// <para>
/// A restore first removes the world files the backup replaces. Valheim loads the highest-numbered
/// <c>_main.&lt;n&gt;</c> save in a world folder, so leaving a newer one there would make the restore do nothing.
/// The caller makes a safety backup before (<see cref="BackupService"/>).
/// </para>
/// </remarks>
public static partial class ServerBackup
{
  public const string ManifestName = "launchheim-backup.json";
  public const string SavesPrefix = "saves/";
  public const string ConfigPrefix = "BepInEx/config/";

  private const string WorldsFolder = "worlds_local";
  private static readonly string[] ListFiles = ["adminlist.txt", "bannedlist.txt", "permittedlist.txt"];

  /// <summary><c>&lt;server&gt;-&lt;yyyyMMdd-HHmmss&gt;[-label].zip</c>, in UTC, which sorts by time and carries it (<see cref="TimeOf"/>).</summary>
  public static string FileName(string serverId, DateTimeOffset at, string? label = null) =>
    $"{serverId}-{at.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{(label is null ? "" : "-" + label)}.zip";

  /// <summary>When a backup was made, from its name, and whether it carries a label (a safety copy before a restore).</summary>
  public static (DateTimeOffset At, string? Label)? TimeOf(string serverId, string fileName)
  {
    var match = NamePattern().Match(fileName);
    if (!match.Success || match.Groups["server"].Value != serverId
      || !DateTime.TryParseExact(match.Groups["time"].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
    {
      return null;
    }

    return (new DateTimeOffset(time, TimeSpan.Zero), match.Groups["label"].Success ? match.Groups["label"].Value : null);
  }

  /// <summary>Writes the backup zip into <paramref name="output"/>.</summary>
  /// <param name="saves">The server's save folder.</param>
  /// <param name="server">The server's own folder, for <c>BepInEx/config</c>; null to leave configs out.</param>
  /// <param name="world">Only this world, for a save folder shared with other worlds; null for all.</param>
  public static async Task<BackupManifest> CreateAsync(
    IServerFiles saves,
    IServerFiles? server,
    string? world,
    string serverName,
    string reason,
    bool savedFirst,
    Stream output,
    IProgress<DeployProgress>? progress,
    CancellationToken cancellationToken)
  {
    var files = new List<(string Entry, IServerFiles Source, string Path)>();
    var worlds = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var entry in await saves.ListEntriesAsync(WorldsFolder, cancellationToken))
    {
      var name = WorldName(entry.Name, entry.IsDirectory);
      if (name is null || (world is not null && name != world) || entry.Name.Contains("_backup_", StringComparison.Ordinal))
      {
        continue;
      }

      worlds.Add(name);
      var path = $"{WorldsFolder}/{entry.Name}";
      if (entry.IsDirectory)
      {
        foreach (var file in await ListRecursiveAsync(saves, path, cancellationToken))
        {
          files.Add((SavesPrefix + file, saves, file));
        }
      }
      else
      {
        files.Add((SavesPrefix + path, saves, path));
      }
    }

    if (worlds.Count == 0)
    {
      throw new BackupException(world is null
        ? $"No worlds in {saves.Describe}/{WorldsFolder}. Has the server saved one yet, and is the save folder right?"
        : $"The world {world} isn't in {saves.Describe}/{WorldsFolder} yet. Start the server once so it creates it.");
    }

    foreach (var list in ListFiles)
    {
      if (await saves.SizeAsync(list, cancellationToken) is not null)
      {
        files.Add((SavesPrefix + list, saves, list));
      }
    }

    if (server is not null)
    {
      foreach (var file in await ListRecursiveAsync(server, ConfigPrefix.TrimEnd('/'), cancellationToken))
      {
        // The plugin's command and answer files are traffic, not settings.
        if (!file.StartsWith(ServerCommandChannel.Directory + "/", StringComparison.Ordinal))
        {
          files.Add((file, server, file));
        }
      }
    }

    long bytes = 0;
    using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
    {
      for (var i = 0; i < files.Count; i++)
      {
        progress?.Report(new DeployProgress(i, files.Count, files[i].Path));
        var entry = zip.CreateEntry(files[i].Entry, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var counter = new CountingStream(stream);
        await files[i].Source.DownloadAsync(files[i].Path, counter, cancellationToken);
        bytes += counter.Count;
      }

      var manifest = new BackupManifest(serverName, [.. worlds], DateTimeOffset.UtcNow, reason, savedFirst, files.Count, bytes);
      var manifestEntry = zip.CreateEntry(ManifestName);
      await using (var stream = manifestEntry.Open())
      {
        await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
      }

      progress?.Report(new DeployProgress(files.Count, files.Count, ""));
      return manifest;
    }
  }

  /// <summary>The manifest of a backup zip, or null for a zip without one.</summary>
  public static BackupManifest? ReadManifest(string zipFile)
  {
    using var zip = ZipFile.OpenRead(zipFile);
    if (zip.GetEntry(ManifestName) is not { } entry)
    {
      return null;
    }

    using var stream = entry.Open();
    return JsonSerializer.Deserialize<BackupManifest>(stream);
  }

  /// <summary>Puts a backup's worlds, lists and configs back, replacing the worlds it contains.</summary>
  public static async Task<RestoreReport> RestoreAsync(
    string zipFile,
    IServerFiles saves,
    IServerFiles? server,
    string temporaryDirectory,
    IProgress<DeployProgress>? progress,
    CancellationToken cancellationToken)
  {
    using var zip = ZipFile.OpenRead(zipFile);
    var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/') && e.FullName != ManifestName).ToList();
    foreach (var entry in entries)
    {
      // Zip slip: an entry may only land inside the save or server folder.
      var name = entry.FullName.Replace('\\', '/');
      if (name.Split('/').Any(part => part is ".." or "") || Path.IsPathRooted(name))
      {
        throw new BackupException($"The backup contains an unsafe path: {entry.FullName}");
      }
    }

    var worlds = entries
      .Select(e => e.FullName)
      .Where(n => n.StartsWith(SavesPrefix + WorldsFolder + "/", StringComparison.Ordinal))
      .Select(n => n[(SavesPrefix + WorldsFolder + "/").Length..])
      .Select(rest => rest.Contains('/') ? rest[..rest.IndexOf('/')] : WorldName(rest, isDirectory: false))
      .OfType<string>()
      .Distinct(StringComparer.Ordinal)
      .ToList();

    // Out with the current saves of those worlds, so Valheim can't prefer a newer one over the restored.
    foreach (var entry in await saves.ListEntriesAsync(WorldsFolder, cancellationToken))
    {
      if (WorldName(entry.Name, entry.IsDirectory) is { } name && worlds.Contains(name) && !entry.Name.Contains("_backup_", StringComparison.Ordinal))
      {
        var path = $"{WorldsFolder}/{entry.Name}";
        foreach (var file in entry.IsDirectory ? await ListRecursiveAsync(saves, path, cancellationToken) : [path])
        {
          await saves.DeleteAsync(file, cancellationToken);
        }
      }
    }

    Directory.CreateDirectory(temporaryDirectory);
    var done = 0;
    foreach (var entry in entries)
    {
      progress?.Report(new DeployProgress(done++, entries.Count, entry.FullName));
      var (target, path) = entry.FullName.StartsWith(SavesPrefix, StringComparison.Ordinal)
        ? (saves, entry.FullName[SavesPrefix.Length..])
        : (server, entry.FullName);
      if (target is null)
      {
        continue;
      }

      var temp = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N"));
      try
      {
        entry.ExtractToFile(temp, overwrite: true);
        await target.UploadAsync(temp, path, executable: false, cancellationToken);
      }
      finally
      {
        File.Delete(temp);
      }
    }

    progress?.Report(new DeployProgress(entries.Count, entries.Count, ""));
    return new RestoreReport(worlds, entries.Count);
  }

  /// <summary>
  /// Looks for the server's save folder at a host: the folder with <c>worlds_local/</c> in it, among the
  /// usual places (inside the server folder, or Valheim's default under the login's home).
  /// </summary>
  /// <param name="open">Opens the host's files at a path in <see cref="HostAccess.SaveDirectory"/>'s form.</param>
  /// <returns>That path, or null when none of them has worlds.</returns>
  public static async Task<string?> FindSaveDirectoryAsync(Func<string, IServerFiles> open, CancellationToken cancellationToken)
  {
    string[] candidates = ["", "saves", "save", "data", "valheim_data", "~/.config/unity3d/IronGate/Valheim", "~/saves"];
    foreach (var candidate in candidates)
    {
      try
      {
        var files = open(candidate);
        if ((await files.ListEntriesAsync(WorldsFolder, cancellationToken)).Any(e => WorldName(e.Name, e.IsDirectory) is not null))
        {
          return candidate;
        }
      }
      catch (IOException)
      {
        // Not there, or not readable for this login: the next place.
      }
    }

    return null;
  }

  /// <summary>The world a file or folder in <c>worlds_local</c> belongs to, or null for anything else.</summary>
  internal static string? WorldName(string name, bool isDirectory)
  {
    if (isDirectory)
    {
      return name;
    }

    // Midgard.fwl, Midgard.db, Midgard.db.old, Midgard.fwl.old; not the minimap cache or anything else.
    var match = WorldFilePattern().Match(name);
    return match.Success ? match.Groups["world"].Value : null;
  }

  internal static async Task<IReadOnlyList<string>> ListRecursiveAsync(IServerFiles files, string directory, CancellationToken cancellationToken)
  {
    var result = new List<string>();
    foreach (var entry in await files.ListEntriesAsync(directory, cancellationToken))
    {
      var path = $"{directory}/{entry.Name}";
      if (entry.IsDirectory)
      {
        result.AddRange(await ListRecursiveAsync(files, path, cancellationToken));
      }
      else
      {
        result.Add(path);
      }
    }

    return result;
  }

  [GeneratedRegex(@"^(?<world>.+?)\.(fwl|db)(\.old)?$")]
  private static partial Regex WorldFilePattern();

  [GeneratedRegex(@"^(?<server>.+)-(?<time>\d{8}-\d{6})(-(?<label>[a-z-]+))?\.zip$")]
  private static partial Regex NamePattern();

  /// <summary>Counts what goes through, for the manifest's size.</summary>
  private sealed class CountingStream(Stream inner) : Stream
  {
    public long Count { get; private set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Count;
    public override long Position { get => Count; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count)
    {
      inner.Write(buffer, offset, count);
      Count += count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
      await inner.WriteAsync(buffer, cancellationToken);
      Count += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
      WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
  }
}

public sealed class BackupException(string message, Exception? inner = null) : IOException(message, inner);
