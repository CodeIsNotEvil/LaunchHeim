using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>Finds Valheim's dedicated server: Steam's copy, LaunchHeim's own download, or a folder the user chose.</summary>
/// <remarks>
/// The dedicated server is its own Steam app, free and downloadable without an account, for Linux and
/// Windows alike. Steam shows it under Library → Tools as "Valheim Dedicated Server". LaunchHeim can also
/// download it itself with SteamCMD (<see cref="SteamCmd"/>), which is the only way on a machine without
/// Steam and the way to get the other platform's build for a remote server.
/// </remarks>
public sealed class DedicatedServer(SteamLibraryLocator steam, AppPaths paths)
{
  public const string SteamAppId = "896660";

  /// <summary>The folder name Steam uses when the manifest doesn't say.</summary>
  public const string SteamInstallDir = "Valheim dedicated server";

  public static string ExecutableFor(GamePlatform platform) =>
    platform == GamePlatform.Windows ? "valheim_server.exe" : "valheim_server.x86_64";

  public static bool IsServerDirectory(string? directory, GamePlatform platform) =>
    !string.IsNullOrEmpty(directory) && File.Exists(Path.Combine(directory, ExecutableFor(platform)));

  /// <summary>Where LaunchHeim's own downloads go, one folder per platform so a Windows build for a remote server never replaces the local one.</summary>
  public string DownloadDirectory(GamePlatform platform) =>
    Path.Combine(paths.DataDirectory, "dedicated-server", platform == GamePlatform.Windows ? "windows" : "linux");

  /// <summary>Steam's copy, or null when Steam doesn't have it installed.</summary>
  public string? FindInSteam() =>
    steam.FindApp(SteamAppId, SteamInstallDir, directory => IsServerDirectory(directory, steam.Platform));

  /// <summary>LaunchHeim's download for this machine, or null before it was downloaded.</summary>
  public string? FindDownloaded()
  {
    var directory = DownloadDirectory(steam.Platform);
    return IsServerDirectory(directory, steam.Platform) ? directory : null;
  }

  /// <summary>The folder a profile runs from, or null when that copy isn't there.</summary>
  public string? DirectoryFor(ServerProfile server) => server.Binary switch
  {
    ServerBinary.Steam => FindInSteam(),
    ServerBinary.LaunchHeim => FindDownloaded(),
    _ => IsServerDirectory(server.CustomDirectory, steam.Platform) ? server.CustomDirectory : null,
  };

  /// <summary>What to tell the user when <see cref="DirectoryFor"/> found nothing.</summary>
  public static string MissingText(ServerBinary binary) => binary switch
  {
    ServerBinary.Steam => "Steam doesn't have Valheim Dedicated Server installed. Install it from Steam (Library → Tools), or let LaunchHeim download it.",
    ServerBinary.LaunchHeim => "The dedicated server hasn't been downloaded yet.",
    _ => "The chosen folder doesn't contain Valheim's dedicated server.",
  };

  /// <summary>
  /// Asks Steam to install the dedicated server: <c>steam://install/896660</c> opens Steam's own install
  /// dialog, which picks the library and keeps it updated like any app.
  /// </summary>
  public const string SteamInstallUrl = "steam://install/" + SteamAppId;
}
