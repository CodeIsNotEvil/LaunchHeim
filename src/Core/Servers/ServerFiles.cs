namespace CINE.LaunchHeim.Core.Servers;

/// <summary>A server's files, wherever they are: on this machine now, over FTP or SFTP later.</summary>
/// <remarks>
/// Paths are relative to the server's root and use forward slashes, as FTP and SFTP do; each
/// implementation turns them into its own. Everything that only needs a server's files goes through
/// this: the plugin's file channel (<see cref="ServerCommandChannel"/>) now, and backups and remote mod
/// deployment next (decision 0019), so a host that only offers FTP gets them too.
/// </remarks>
public interface IServerFiles
{
  /// <summary>The file's text, or null when it doesn't exist.</summary>
  Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken);

  /// <summary>Writes the file so that a reader sees the old text or the new one, never half of it.</summary>
  Task WriteTextAsync(string path, string text, CancellationToken cancellationToken);

  /// <summary>The names (not paths) of the files in a folder ending in <paramref name="extension"/>; empty when the folder doesn't exist.</summary>
  Task<IReadOnlyList<string>> ListAsync(string directory, string extension, CancellationToken cancellationToken);

  /// <summary>Deletes the file if it is there.</summary>
  Task DeleteAsync(string path, CancellationToken cancellationToken);
}

/// <summary>A server folder on this machine.</summary>
public sealed class LocalServerFiles(string root) : IServerFiles
{
  public string Root => root;

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

  public Task<IReadOnlyList<string>> ListAsync(string directory, string extension, CancellationToken cancellationToken)
  {
    var full = Full(directory);
    IReadOnlyList<string> names = Directory.Exists(full)
      ? Directory.EnumerateFiles(full, "*" + extension).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToList()
      : [];
    return Task.FromResult(names);
  }

  public Task DeleteAsync(string path, CancellationToken cancellationToken)
  {
    File.Delete(Full(path));
    return Task.CompletedTask;
  }

  private string Full(string path) =>
    Path.Combine([root, .. path.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
}
