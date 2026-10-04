using CINE.LaunchHeim.Core.Saves;

namespace CINE.LaunchHeim.Core.Game;

/// <summary>Where Unity writes Valheim's <c>Player.log</c>: the game's own output, crashes included.</summary>
/// <remarks>
/// It matters when BepInEx never gets as far as writing its own log, for example when Doorstop fails to
/// load or the game dies on start. Unity derives the folder from the company and product name, which for
/// Valheim are IronGate and Valheim (the same folder that holds the local worlds on Linux).
/// </remarks>
public static class UnityPlayerLog
{
  public const string FileName = "Player.log";

  /// <summary>Every place the log may be, most likely first.</summary>
  /// <remarks>
  /// LaunchHeim starts the game outside any sandbox, so it writes to the normal config folder. Started
  /// from Flatpak Steam it writes inside the Flatpak's folder instead, so that one is checked as well.
  /// </remarks>
  public static IReadOnlyList<string> Candidates(GamePlatform platform, string home, string? xdgConfigHome) =>
    ValheimSaves.DataDirectories(platform, home, xdgConfigHome).Select(d => Path.Combine(d, FileName)).ToList();

  /// <summary>The candidate written last, or the usual location when the game has never run.</summary>
  public static string Find() => Find(Candidates(
    GamePlatforms.Current,
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")));

  internal static string Find(IReadOnlyList<string> candidates) =>
    candidates.Where(File.Exists).MaxBy(File.GetLastWriteTimeUtc) ?? candidates[0];
}
