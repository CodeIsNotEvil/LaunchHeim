namespace CINE.LaunchHeim.Core.Servers;

/// <summary>A server's files, wherever they are: on this machine, or at a host over FTP, FTPS or SFTP.</summary>
/// <remarks>
/// <para>
/// Paths are relative to the server's root and use forward slashes, as FTP and SFTP do; each
/// implementation turns them into its own. Everything that only needs a server's files goes through
/// this (decision 0019): the plugin's file channel (<see cref="ServerCommandChannel"/>), following the
/// log (<see cref="ServerFilesLogSource"/>), deploying mods and the server program (<see cref="ServerDeploy"/>),
/// and backups next. So a host that offers nothing but FTP gets all of them.
/// </para>
/// <para>
/// Remote implementations hold one connection and are not safe for concurrent use by themselves; they
/// serialize calls internally, since FTP can only do one transfer per connection anyway.
/// </para>
/// </remarks>
public interface IServerFiles : IAsyncDisposable
{
  /// <summary>Where these files are, for messages: a folder, or <c>sftp://user@host/path</c>.</summary>
  string Describe { get; }

  /// <summary>The file's text, or null when it doesn't exist.</summary>
  Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken);

  /// <summary>Writes the file so that a reader sees the old text or the new one, never half of it.</summary>
  Task WriteTextAsync(string path, string text, CancellationToken cancellationToken);

  /// <summary>Copies a local file there, creating folders as needed, through a temporary name like <see cref="WriteTextAsync"/>.</summary>
  /// <param name="executable">Mark it executable (a server binary or start script); ignored where files have no such bit.</param>
  Task UploadAsync(string localFile, string path, bool executable, CancellationToken cancellationToken);

  /// <summary>The names (not paths) of the files in a folder ending in <paramref name="extension"/>; empty when the folder doesn't exist.</summary>
  Task<IReadOnlyList<string>> ListAsync(string directory, string extension, CancellationToken cancellationToken);

  /// <summary>The file's size, or null when it doesn't exist.</summary>
  Task<long?> SizeAsync(string path, CancellationToken cancellationToken);

  /// <summary>Up to <paramref name="count"/> bytes from <paramref name="offset"/> on; fewer at the end of the file.</summary>
  Task<byte[]> ReadRangeAsync(string path, long offset, int count, CancellationToken cancellationToken);

  /// <summary>Deletes the file if it is there.</summary>
  Task DeleteAsync(string path, CancellationToken cancellationToken);

  /// <summary>Files and folders in a folder; empty when it doesn't exist. Not recursive, and without <c>.</c> and <c>..</c>.</summary>
  Task<IReadOnlyList<ServerFileEntry>> ListEntriesAsync(string directory, CancellationToken cancellationToken);

  /// <summary>Copies the file into <paramref name="destination"/>, for backups of worlds that may be hundreds of MB.</summary>
  Task DownloadAsync(string path, Stream destination, CancellationToken cancellationToken);
}

/// <summary>One entry of <see cref="IServerFiles.ListEntriesAsync"/>.</summary>
public sealed record ServerFileEntry(string Name, bool IsDirectory, long Size);

/// <summary>A server folder on this machine.</summary>
public sealed class LocalServerFiles(string root) : IServerFiles
{
  public string Root => root;

  public string Describe => root;

  public async Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken)
  {
    try
    {
      return await File.ReadAllTextAsync(Full(path), cancellationToken);
    }
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
    {
      return null;
    }
  }

  public async Task WriteTextAsync(string path, string text, CancellationToken cancellationToken)
  {
    var full = Full(path);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    var temp = full + ".tmp";
    await File.WriteAllTextAsync(temp, text, cancellationToken);
    File.Move(temp, full, overwrite: true);
  }

  public Task UploadAsync(string localFile, string path, bool executable, CancellationToken cancellationToken)
  {
    var full = Full(path);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    var temp = full + ".tmp";
    File.Copy(localFile, temp, overwrite: true);
    if (executable && !OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(temp, File.GetUnixFileMode(temp) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    File.Move(temp, full, overwrite: true);
    return Task.CompletedTask;
  }

  public Task<IReadOnlyList<string>> ListAsync(string directory, string extension, CancellationToken cancellationToken)
  {
    var full = Full(directory);
    IReadOnlyList<string> names = Directory.Exists(full)
      ? Directory.EnumerateFiles(full, "*" + extension).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToList()
      : [];
    return Task.FromResult(names);
  }

  public Task<long?> SizeAsync(string path, CancellationToken cancellationToken)
  {
    var info = new FileInfo(Full(path));
    return Task.FromResult<long?>(info.Exists ? info.Length : null);
  }

  public async Task<byte[]> ReadRangeAsync(string path, long offset, int count, CancellationToken cancellationToken)
  {
    await using var stream = new FileStream(Full(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    if (offset >= stream.Length)
    {
      return [];
    }

    stream.Position = offset;
    var buffer = new byte[(int)Math.Min(count, stream.Length - offset)];
    await stream.ReadExactlyAsync(buffer, cancellationToken);
    return buffer;
  }

  public Task DeleteAsync(string path, CancellationToken cancellationToken)
  {
    File.Delete(Full(path));
    return Task.CompletedTask;
  }

  public Task<IReadOnlyList<ServerFileEntry>> ListEntriesAsync(string directory, CancellationToken cancellationToken)
  {
    var full = Full(directory);
    IReadOnlyList<ServerFileEntry> entries = Directory.Exists(full)
      ? new DirectoryInfo(full).EnumerateFileSystemInfos()
        .Select(i => new ServerFileEntry(i.Name, i is DirectoryInfo, i is FileInfo f ? f.Length : 0))
        .OrderBy(e => e.Name, StringComparer.Ordinal)
        .ToList()
      : [];
    return Task.FromResult(entries);
  }

  public async Task DownloadAsync(string path, Stream destination, CancellationToken cancellationToken)
  {
    await using var source = new FileStream(Full(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    await source.CopyToAsync(destination, cancellationToken);
  }

  public ValueTask DisposeAsync() => ValueTask.CompletedTask;

  private string Full(string path) =>
    Path.Combine([root, .. path.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
}

/// <summary>A folder inside another <see cref="IServerFiles"/>, sharing its connection (which it never closes).</summary>
internal sealed class SubFolderFiles(IServerFiles inner, string folder) : IServerFiles
{
  public string Describe => $"{inner.Describe.TrimEnd('/')}/{folder}";

  private string In(string path) => folder.Length == 0 ? path : path.Length == 0 ? folder : $"{folder.TrimEnd('/')}/{path}";

  public Task<string?> ReadTextAsync(string path, CancellationToken c) => inner.ReadTextAsync(In(path), c);
  public Task WriteTextAsync(string path, string text, CancellationToken c) => inner.WriteTextAsync(In(path), text, c);
  public Task UploadAsync(string localFile, string path, bool executable, CancellationToken c) => inner.UploadAsync(localFile, In(path), executable, c);
  public Task<IReadOnlyList<string>> ListAsync(string directory, string extension, CancellationToken c) => inner.ListAsync(In(directory), extension, c);
  public Task<long?> SizeAsync(string path, CancellationToken c) => inner.SizeAsync(In(path), c);
  public Task<byte[]> ReadRangeAsync(string path, long offset, int count, CancellationToken c) => inner.ReadRangeAsync(In(path), offset, count, c);
  public Task DeleteAsync(string path, CancellationToken c) => inner.DeleteAsync(In(path), c);
  public Task<IReadOnlyList<ServerFileEntry>> ListEntriesAsync(string directory, CancellationToken c) => inner.ListEntriesAsync(In(directory), c);
  public Task DownloadAsync(string path, Stream destination, CancellationToken c) => inner.DownloadAsync(In(path), destination, c);
  public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Joins a host's server folder and a path inside it the way FTP and SFTP want them.</summary>
internal static class RemotePath
{
  /// <summary><paramref name="root"/> empty means the folder the login starts in, so the path stays relative.</summary>
  public static string Combine(string root, string path)
  {
    path = path.Replace('\\', '/').TrimStart('/');
    root = root.Replace('\\', '/').TrimEnd('/');
    return root.Length == 0 ? path : $"{root}/{path}";
  }

  public static string? Parent(string path)
  {
    var slash = path.TrimEnd('/').LastIndexOf('/');
    return slash <= 0 ? (slash == 0 ? "/" : null) : path[..slash];
  }
}
