using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>One mod of the server's instance, and whether the server runs it.</summary>
/// <param name="ClientOnly">Thunderstore lists it as Client-side and not Server-side.</param>
public sealed record ServerModEntry(InstalledMod Mod, bool ClientOnly, bool Included);

/// <summary>What a sync changed, for the log and the status line.</summary>
public sealed record ServerModSync(int Copied, int Removed, int ConfigsCopied);

/// <summary>Brings a server's BepInEx folder in line with its instance before each start.</summary>
/// <remarks>
/// <para>
/// A server links an instance instead of keeping its own mod list (decision 0019): players and server
/// need the same mods, and with one list in one place they can't drift apart. Mods are installed,
/// updated and removed on the instance as always, and the next start copies the server's share of them.
/// Not every mod belongs on a server. Thunderstore's Client-side category (minimaps, UI, graphics) is
/// left out unless the player switches it on; Nexus and local mods carry no such tag and go along.
/// </para>
/// <para>
/// The server's folder is its own copy, not a link: the server must keep running while the instance is
/// being changed for the next session, and Windows can't replace a dll a running server has loaded.
/// <c>synced.json</c> lists what the last sync copied, so a mod removed from the instance (or switched off
/// for the server) is removed from the server too, and nothing else is ever deleted.
/// </para>
/// <para>
/// Configs are different: with ServerSync-style mods the server's config is the one that counts and is
/// pushed to the players, and admins tune it on the server. So a config is copied only while the server
/// has none of that name, unless the player asks to replace them all.
/// </para>
/// </remarks>
public static class ServerMods
{
  public const string SyncedFile = "synced.json";

  public const string ClientSide = "Client-side";
  public const string ServerSide = "Server-side";

  private const string ConfigPrefix = "BepInEx/config/";

  public static bool IsClientOnly(IReadOnlyList<string>? categories) =>
    categories is not null
    && categories.Contains(ClientSide, StringComparer.OrdinalIgnoreCase)
    && !categories.Contains(ServerSide, StringComparer.OrdinalIgnoreCase);

  /// <summary>The instance's enabled mods, BepInEx left out, with the server's choice for each.</summary>
  /// <param name="categories">Thunderstore's categories for a mod, or null when unknown.</param>
  public static IReadOnlyList<ServerModEntry> Entries(Instance instance, ServerProfile server, Func<InstalledMod, IReadOnlyList<string>?> categories) =>
    instance.Mods
      .Where(m => m.Enabled && !m.IsLoader)
      .Select(m =>
      {
        var clientOnly = IsClientOnly(categories(m));
        var included = server.ModChoices.TryGetValue(m.Key, out var choice) ? choice : !clientOnly;
        return new ServerModEntry(m, clientOnly, included);
      })
      .OrderBy(e => e.Mod.Name, StringComparer.CurrentCultureIgnoreCase)
      .ToList();

  /// <summary>Copies the mods' files from <paramref name="sourceDirectory"/> into the server's folder and removes what no longer belongs.</summary>
  /// <param name="mods">BepInEx and the mods the server runs, as installed in the source.</param>
  /// <param name="replaceConfigs">Overwrite the server's configs with the source's, instead of only adding missing ones.</param>
  public static ServerModSync Sync(string sourceDirectory, IEnumerable<InstalledMod> mods, string serverDirectory, bool replaceConfigs)
  {
    var syncedPath = Path.Combine(serverDirectory, SyncedFile);
    var previous = JsonFile.Read<List<string>>(syncedPath) ?? [];
    var wanted = new List<string>();
    int copied = 0, configs = 0;

    foreach (var file in mods.SelectMany(m => m.Files).Select(ModInstaller.NormalizePath).Distinct(StringComparer.Ordinal))
    {
      var source = Full(sourceDirectory, file);
      if (!File.Exists(source))
      {
        continue;
      }

      var target = Full(serverDirectory, file);
      if (IsConfig(file))
      {
        // Never tracked: a config stays when its mod goes, as BepInEx itself leaves them.
        if (replaceConfigs || !File.Exists(target))
        {
          configs += Copy(source, target) ? 1 : 0;
        }

        continue;
      }

      wanted.Add(file);
      copied += Copy(source, target) ? 1 : 0;
    }

    // Configs the player made in the instance without a mod installing them (most are written by the
    // mod on its first start, in the instance).
    var sourceConfig = Path.Combine(sourceDirectory, "BepInEx", "config");
    if (Directory.Exists(sourceConfig))
    {
      foreach (var source in Directory.EnumerateFiles(sourceConfig, "*", SearchOption.AllDirectories))
      {
        var relative = ConfigPrefix + ModInstaller.NormalizePath(Path.GetRelativePath(sourceConfig, source));
        var target = Full(serverDirectory, relative);
        if (replaceConfigs || !File.Exists(target))
        {
          configs += Copy(source, target) ? 1 : 0;
        }
      }
    }

    var keep = new HashSet<string>(wanted, StringComparer.Ordinal);
    var removed = 0;
    foreach (var stale in previous.Where(f => !keep.Contains(f)))
    {
      var path = Full(serverDirectory, stale);
      if (File.Exists(path))
      {
        File.Delete(path);
        removed++;
      }
    }

    JsonFile.Write(syncedPath, wanted);
    return new ServerModSync(copied, removed, configs);
  }

  private static bool IsConfig(string file) => file.StartsWith(ConfigPrefix, StringComparison.OrdinalIgnoreCase);

  /// <summary>Copies when the target is missing or differs in size or time, which a mod update always changes.</summary>
  private static bool Copy(string source, string target)
  {
    var from = new FileInfo(source);
    var to = new FileInfo(target);
    if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc)
    {
      return false;
    }

    Directory.CreateDirectory(to.DirectoryName!);
    File.Copy(source, target, overwrite: true);
    File.SetLastWriteTimeUtc(target, from.LastWriteTimeUtc);
    return true;
  }

  private static string Full(string root, string relative) =>
    Path.Combine([root, .. relative.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
}
