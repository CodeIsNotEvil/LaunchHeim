using System.Globalization;
using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Saves;

/// <summary>Where a save lives, in Valheim's own terms (<c>FileHelpers.FileSource</c>).</summary>
public enum SaveSource
{
  /// <summary>Steam Cloud: <c>userdata/&lt;account&gt;/892970/remote</c> in the Steam folder.</summary>
  Cloud,

  /// <summary>Saves kept off the cloud: <c>characters_local</c> and <c>worlds_local</c>.</summary>
  Local,

  /// <summary>From before Steam Cloud support: <c>characters</c> and <c>worlds</c> next to the prefs.</summary>
  Legacy,
}

public sealed record ValheimCharacter(string FileName, SaveSource Source, DateTime LastWriteUtc)
{
  /// <summary>
  /// The name shown in LaunchHeim. Valheim names the file after the character in lower case, and the
  /// name as typed is buried behind version-dependent stats inside the file, so the file name in title
  /// case stands in for it.
  /// </summary>
  public string DisplayName => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(FileName);
}

public sealed record ValheimWorld(string Name, SaveSource Source, DateTime LastWriteUtc)
{
  /// <summary>Identifies the world in LaunchHeim's settings. Valheim matches world names ignoring case.</summary>
  public string Key => "world:" + Name.ToLowerInvariant();
}

/// <param name="Address"><c>host:port</c>, the form <c>+connect</c> takes.</param>
public sealed record ValheimServer(string Name, string Address, bool IsFavorite, bool IsRecent)
{
  public string Key => "server:" + Address.ToLowerInvariant();
}

/// <summary>Reads the characters, worlds and servers Valheim itself manages, without changing them.</summary>
/// <remarks>
/// <para>
/// The rules are the game's own (<c>SaveSystem</c>, <c>SaveCollection</c> and <c>ServerListGui</c> in
/// <c>assembly_valheim.dll</c>). Every save type exists in up to three places, see
/// <see cref="SaveSource"/>, and Valheim lists them together, so this does too. Backups carry
/// <c>_backup_</c> in their name and are left out, like the game's menus leave them out.
/// </para>
/// <para>
/// A world is either the older pair <c>&lt;Name&gt;.fwl</c> plus <c>.db</c>, or in current versions a folder
/// <c>&lt;Name&gt;/</c> holding <c>_main.&lt;n&gt;.fwl2</c> and the chunk files. A world folder without a
/// <c>_main.*</c> file only holds minimap caches and is not a world.
/// </para>
/// </remarks>
public sealed class ValheimSaves(string dataDirectory, IReadOnlyList<string> cloudDirectories)
{
  private const string BackupMarker = "_backup_";

  /// <summary>The game's own folder (Unity's persistent data path), which also holds the Linux prefs.</summary>
  public string DataDirectory => dataDirectory;

  public IReadOnlyList<string> CloudDirectories => cloudDirectories;

  /// <summary>Valheim's folders as the game started by LaunchHeim sees them.</summary>
  /// <remarks>
  /// LaunchHeim starts the game outside any Flatpak sandbox, so its data folder is the normal one even
  /// with Flatpak Steam (see <see cref="DataDirectories"/>). The cloud folder is per Steam account.
  /// </remarks>
  public static ValheimSaves ForCurrentUser(IEnumerable<string> steamRoots) => new(
    DataDirectories(
      GamePlatforms.Current,
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
      Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"))[0],
    CloudDirectoriesFor(steamRoots));

  /// <summary>Unity's persistent data path for Valheim (company IronGate, product Valheim), most likely first.</summary>
  /// <remarks>Started from Flatpak Steam, the game writes inside the Flatpak's folder instead, so that one comes second.</remarks>
  public static IReadOnlyList<string> DataDirectories(GamePlatform platform, string home, string? xdgConfigHome)
  {
    if (platform == GamePlatform.Windows)
    {
      return [Path.Combine(home, "AppData", "LocalLow", "IronGate", "Valheim")];
    }

    var config = !string.IsNullOrEmpty(xdgConfigHome) && Path.IsPathRooted(xdgConfigHome) ? xdgConfigHome : Path.Combine(home, ".config");
    return
    [
      Path.Combine(config, "unity3d", "IronGate", "Valheim"),
      Path.Combine(home, ".var", "app", SteamClient.FlatpakId, "config", "unity3d", "IronGate", "Valheim"),
    ];
  }

  /// <summary>Valheim's Steam Cloud folder for the Steam account that logged in last.</summary>
  /// <remarks>
  /// <c>config/loginusers.vdf</c> names the accounts by SteamID64; <c>userdata</c> is keyed by the 32-bit
  /// account id inside it. Older Steam marks the last account <c>MostRecent</c>, current Steam only
  /// keeps a login <c>Timestamp</c>. Without that file every account's folder is used.
  /// </remarks>
  internal static IReadOnlyList<string> CloudDirectoriesFor(IEnumerable<string> steamRoots)
  {
    var result = new List<string>();
    foreach (var root in steamRoots.Where(Directory.Exists))
    {
      var userdata = Path.Combine(root, "userdata");
      if (MostRecentAccountId(Path.Combine(root, "config", "loginusers.vdf")) is { } account)
      {
        result.Add(Path.Combine(userdata, account.ToString(CultureInfo.InvariantCulture), SteamLibraryLocator.ValheimAppId, "remote"));
      }
      else if (Directory.Exists(userdata))
      {
        result.AddRange(Directory.EnumerateDirectories(userdata).Select(d => Path.Combine(d, SteamLibraryLocator.ValheimAppId, "remote")));
      }
    }

    // ~/.steam/steam is usually a link to ~/.local/share/Steam: one folder, two paths.
    return result.Where(Directory.Exists).DistinctBy(Canonical).ToList();
  }

  internal static ulong? MostRecentAccountId(string loginUsersVdf)
  {
    if (!File.Exists(loginUsersVdf))
    {
      return null;
    }

    var users = VdfNode.Parse(File.ReadAllText(loginUsersVdf))["users"];
    var latest = users?.Children
      .Where(u => ulong.TryParse(u.Key, out _))
      .OrderByDescending(u => u.Value.Value("MostRecent") == "1")
      .ThenByDescending(u => long.TryParse(u.Value.Value("Timestamp"), out var t) ? t : 0)
      .Select(u => ulong.Parse(u.Key, CultureInfo.InvariantCulture))
      .FirstOrDefault();

    // SteamID64 = 76561197960265728 + account id for individual accounts.
    return latest is > 76561197960265728UL ? latest - 76561197960265728UL : null;
  }

  /// <summary>Every character, most recently played first. A name that exists twice is listed once, like in the game.</summary>
  public IReadOnlyList<ValheimCharacter> Characters() =>
    Locations("characters", "characters_local", "characters")
      .SelectMany(l => Files(l.Directory)
        .Where(f => Path.GetExtension(f) == ".fch" && !Path.GetFileName(f).Contains(BackupMarker, StringComparison.Ordinal))
        .Select(f => new ValheimCharacter(Path.GetFileNameWithoutExtension(f), l.Source, File.GetLastWriteTimeUtc(f))))
      .OrderByDescending(c => c.LastWriteUtc)
      // The game picks the character by file name only (FejdStartup.SetSelectedProfile).
      .DistinctBy(c => c.FileName, StringComparer.Ordinal)
      .ToList();

  /// <summary>Every world, most recently saved first.</summary>
  public IReadOnlyList<ValheimWorld> Worlds() =>
    Locations("worlds", "worlds_local", "worlds")
      .SelectMany(l => WorldsIn(l.Directory, l.Source))
      .OrderByDescending(w => w.LastWriteUtc)
      .DistinctBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();

  private static IEnumerable<ValheimWorld> WorldsIn(string directory, SaveSource source)
  {
    foreach (var file in Files(directory))
    {
      var name = Path.GetFileNameWithoutExtension(file);
      if (Path.GetExtension(file) == ".fwl" && !name.Contains(BackupMarker, StringComparison.Ordinal))
      {
        var db = Path.ChangeExtension(file, ".db");
        var written = File.Exists(db) ? Max(File.GetLastWriteTimeUtc(file), File.GetLastWriteTimeUtc(db)) : File.GetLastWriteTimeUtc(file);
        yield return new ValheimWorld(name, source, written);
      }
    }

    if (!Directory.Exists(directory))
    {
      yield break;
    }

    foreach (var folder in Directory.EnumerateDirectories(directory))
    {
      var name = Path.GetFileName(folder);
      if (name.Contains(BackupMarker, StringComparison.Ordinal))
      {
        continue;
      }

      var main = Directory.EnumerateFiles(folder, "_main.*").ToList();
      if (main.Count > 0)
      {
        yield return new ValheimWorld(name, source, main.Max(File.GetLastWriteTimeUtc));
      }
    }
  }

  /// <summary>
  /// The dedicated servers from Valheim's Favorites and Recent tabs: favorites first, each list in the
  /// game's order.
  /// </summary>
  /// <remarks>
  /// Entries for a Steam friend's or a PlayFab (crossplay) game have no address and can't be joined
  /// from the command line, so they are left out. Each list is kept locally and in the cloud; like the
  /// game, the newer file wins and the other only adds servers it lacks.
  /// </remarks>
  public IReadOnlyList<ValheimServer> Servers()
  {
    var favorites = ServerList("favorite");
    var recent = ServerList("recent");
    var recentAddresses = recent.Select(s => s.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var favoriteAddresses = favorites.Select(s => s.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);

    return favorites.Select(s => new ValheimServer(s.Name, s.Address, IsFavorite: true, recentAddresses.Contains(s.Address)))
      .Concat(recent.Where(s => !favoriteAddresses.Contains(s.Address)).Select(s => new ValheimServer(s.Name, s.Address, IsFavorite: false, IsRecent: true)))
      .ToList();
  }

  private List<ServerListEntry> ServerList(string name) =>
    new[] { Path.Combine(dataDirectory, "serverlist_local", name) }
      .Concat(cloudDirectories.Select(c => Path.Combine(c, "serverlist", name)))
      .Where(File.Exists)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .SelectMany(f => ServerListFile.ReadDedicated(File.ReadAllBytes(f)))
      .DistinctBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
      .ToList();

  private IEnumerable<(string Directory, SaveSource Source)> Locations(string cloud, string local, string legacy) =>
    cloudDirectories.Select(c => (Path.Combine(c, cloud), SaveSource.Cloud))
      .Append((Path.Combine(dataDirectory, local), SaveSource.Local))
      .Append((Path.Combine(dataDirectory, legacy), SaveSource.Legacy));

  private static IEnumerable<string> Files(string directory) =>
    Directory.Exists(directory) ? Directory.EnumerateFiles(directory) : [];

  private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

  private static string Canonical(string path)
  {
    try
    {
      var info = new DirectoryInfo(path);
      var resolved = info.FullName;
      // Resolve every parent too: the link is usually ~/.steam/steam, far above the remote folder.
      for (var current = info; current is not null; current = current.Parent)
      {
        if (current.ResolveLinkTarget(returnFinalTarget: true) is { } target)
        {
          resolved = Path.Combine(target.FullName, Path.GetRelativePath(current.FullName, info.FullName));
          break;
        }
      }

      return Path.GetFullPath(resolved).TrimEnd('/', '\\');
    }
    catch (IOException)
    {
      return path.TrimEnd('/', '\\');
    }
  }
}
