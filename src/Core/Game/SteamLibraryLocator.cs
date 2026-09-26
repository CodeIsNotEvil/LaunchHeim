namespace CINE.LaunchHeim.Core.Game;

/// <summary>Finds the Valheim install through Steam's own library list instead of guessing paths.</summary>
/// <remarks>
/// Games are often on a second drive (the owner's is <c>/mnt/games/SteamLibrary</c>), so looking in
/// <c>~/.local/share/Steam/steamapps/common</c> alone is not enough. <c>libraryfolders.vdf</c> lists
/// every library, and the app manifest there says which folder the game was installed into.
/// </remarks>
public sealed class SteamLibraryLocator(IEnumerable<string> steamRoots)
{
  public const string ValheimAppId = "892970";
  public const string ValheimExecutable = "valheim.x86_64";

  public static SteamLibraryLocator ForCurrentUser()
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return new SteamLibraryLocator(
    [
      Path.Combine(home, ".local/share/Steam"),
      Path.Combine(home, ".steam/steam"),
      Path.Combine(home, ".steam/root"),
      // Flatpak Steam keeps its whole tree inside the sandbox's home.
      Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
    ]);
  }

  public IEnumerable<string> LibraryFolders()
  {
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var root in steamRoots)
    {
      var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
      if (!File.Exists(vdf))
      {
        continue;
      }

      if (seen.Add(Canonical(root)))
      {
        yield return root;
      }

      var folders = VdfNode.Parse(File.ReadAllText(vdf))["libraryfolders"];
      if (folders is null)
      {
        continue;
      }

      foreach (var folder in folders.Children.Values)
      {
        if (folder.Value("path") is { Length: > 0 } path && seen.Add(Canonical(path)))
        {
          yield return path;
        }
      }
    }
  }

  public string? FindValheim()
  {
    foreach (var library in LibraryFolders())
    {
      var manifest = Path.Combine(library, "steamapps", $"appmanifest_{ValheimAppId}.acf");
      if (!File.Exists(manifest))
      {
        continue;
      }

      var installDir = VdfNode.Parse(File.ReadAllText(manifest))["AppState"]?.Value("installdir") ?? "Valheim";
      var gameDir = Path.Combine(library, "steamapps", "common", installDir);
      if (IsValheimDirectory(gameDir))
      {
        return gameDir;
      }
    }

    return null;
  }

  public static bool IsValheimDirectory(string? directory) =>
    !string.IsNullOrEmpty(directory) && File.Exists(Path.Combine(directory, ValheimExecutable));

  // ~/.steam/steam is usually a symlink to ~/.local/share/Steam, and both list the same libraries.
  private static string Canonical(string path)
  {
    try
    {
      var info = new DirectoryInfo(path);
      return (info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName).TrimEnd('/');
    }
    catch (IOException)
    {
      return path.TrimEnd('/');
    }
  }
}
