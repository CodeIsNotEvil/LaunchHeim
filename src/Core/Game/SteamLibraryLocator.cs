namespace CINE.LaunchHeim.Core.Game;

/// <summary>Finds the Valheim install through Steam's own library list instead of guessing paths.</summary>
/// <remarks>
/// Games are often on a second drive (the owner's is <c>/mnt/games/SteamLibrary</c>), so looking in
/// <c>~/.local/share/Steam/steamapps/common</c> alone is not enough. <c>libraryfolders.vdf</c> lists
/// every library, and the app manifest there says which folder the game was installed into. Windows
/// uses the same files; only the Steam folder itself is found differently (the registry).
/// </remarks>
public sealed class SteamLibraryLocator(IEnumerable<string> steamRoots, GamePlatform platform)
{
  public const string ValheimAppId = "892970";

  public SteamLibraryLocator(IEnumerable<string> steamRoots)
    : this(steamRoots, GamePlatforms.Current)
  {
  }

  /// <summary>The game binary on this machine: the native Linux build, or valheim.exe on Windows.</summary>
  public static string ValheimExecutable => ExecutableFor(GamePlatforms.Current);

  public static string ExecutableFor(GamePlatform platform) =>
    platform == GamePlatform.Windows ? "valheim.exe" : "valheim.x86_64";

  public static SteamLibraryLocator ForCurrentUser()
  {
    if (OperatingSystem.IsWindows())
    {
      return new SteamLibraryLocator(WindowsSteamRoots(), GamePlatform.Windows);
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return new SteamLibraryLocator(
    [
      Path.Combine(home, ".local/share/Steam"),
      Path.Combine(home, ".steam/steam"),
      Path.Combine(home, ".steam/root"),
      // Flatpak Steam keeps its whole tree inside the sandbox's home.
      Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
    ], GamePlatform.Linux);
  }

  // Steam records where it lives on every start. The default folder covers a registry that was cleaned.
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static IEnumerable<string> WindowsSteamRoots()
  {
    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
    {
      if (key?.GetValue("SteamPath") is string steamPath && steamPath.Length > 0)
      {
        yield return Path.GetFullPath(steamPath);
      }
    }

    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
  }

  public IEnumerable<string> LibraryFolders()
  {
    // Windows paths differ only in case (the registry says c:/program files (x86)/steam, the vdf C:\\...).
    var seen = new HashSet<string>(platform == GamePlatform.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
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
      if (IsValheimDirectory(gameDir, platform))
      {
        return gameDir;
      }
    }

    return null;
  }

  public static bool IsValheimDirectory(string? directory) => IsValheimDirectory(directory, GamePlatforms.Current);

  public static bool IsValheimDirectory(string? directory, GamePlatform platform) =>
    !string.IsNullOrEmpty(directory) && File.Exists(Path.Combine(directory, ExecutableFor(platform)));

  // ~/.steam/steam is usually a symlink to ~/.local/share/Steam, and both list the same libraries.
  private static string Canonical(string path)
  {
    try
    {
      var info = new DirectoryInfo(path);
      return (info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName).TrimEnd('/', '\\');
    }
    catch (IOException)
    {
      return path.TrimEnd('/', '\\');
    }
  }
}
