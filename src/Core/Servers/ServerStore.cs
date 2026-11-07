using System.Text.RegularExpressions;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>Servers on disk: one folder each under <c>servers/</c> in the data folder.</summary>
/// <remarks>
/// <para>
/// A server's folder holds <c>server.json</c> and everything the server owns: its BepInEx tree (mods
/// synced from the instance, <see cref="ServerMods"/>), its worlds in <c>saves/</c> unless the profile
/// points elsewhere, its log and <c>run.json</c> while it runs. Like an instance, copying the folder copies
/// the server.
/// </para>
/// <para>
/// <c>server.json</c> carries the server and RCON passwords, so it is written readable by the user only.
/// A keyring would be better; it is noted as open in the second brain.
/// </para>
/// </remarks>
public sealed partial class ServerStore(AppPaths paths)
{
  public const string ProfileFile = "server.json";

  public string Root => Path.Combine(paths.DataDirectory, "servers");

  public string DirectoryOf(ServerProfile server) => DirectoryOf(server.Id);

  public string DirectoryOf(string id) => Path.Combine(Root, id);

  /// <summary>The worlds and admin lists: the profile's own choice, else <c>saves/</c> in the server's folder.</summary>
  public string SaveDirectoryOf(ServerProfile server) =>
    string.IsNullOrWhiteSpace(server.SaveDirectory) ? Path.Combine(DirectoryOf(server), "saves") : server.SaveDirectory;

  /// <summary>Valheim's log, written with <c>-logFile</c>. BepInEx's own log is under <c>BepInEx/</c>.</summary>
  public string LogFileOf(ServerProfile server) => Path.Combine(DirectoryOf(server), "server.log");

  public IReadOnlyList<ServerProfile> LoadAll()
  {
    if (!Directory.Exists(Root))
    {
      return [];
    }

    var servers = new List<ServerProfile>();
    foreach (var directory in Directory.EnumerateDirectories(Root))
    {
      try
      {
        if (JsonFile.Read<ServerProfile>(Path.Combine(directory, ProfileFile)) is { } server)
        {
          // The folder name wins, as for instances.
          server.Id = Path.GetFileName(directory);
          servers.Add(server);
        }
      }
      catch (System.Text.Json.JsonException)
      {
        // A damaged profile hides that server, not the others.
      }
    }

    return servers.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
  }

  public ServerProfile Create(string name)
  {
    var server = new ServerProfile { Id = NewId(name), Name = name.Trim() };
    Directory.CreateDirectory(DirectoryOf(server));
    Save(server);
    return server;
  }

  public void Save(ServerProfile server) =>
    JsonFile.Write(Path.Combine(DirectoryOf(server), ProfileFile), server, UnixFileMode.UserRead | UnixFileMode.UserWrite);

  /// <summary>Deletes the server's folder, and with it the worlds in its own <c>saves/</c>. A save folder elsewhere stays.</summary>
  public void Delete(ServerProfile server)
  {
    var directory = DirectoryOf(server);
    if (Directory.Exists(directory))
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  private string NewId(string name)
  {
    var slug = SlugPattern().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
    if (slug.Length == 0)
    {
      slug = "server";
    }

    if (slug.Length > 40)
    {
      slug = slug[..40].TrimEnd('-');
    }

    var id = slug;
    for (var n = 2; Directory.Exists(DirectoryOf(id)); n++)
    {
      id = $"{slug}-{n}";
    }

    return id;
  }

  [GeneratedRegex("[^a-z0-9]+")]
  private static partial Regex SlugPattern();
}
