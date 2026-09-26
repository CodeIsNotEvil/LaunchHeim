namespace CINE.LaunchHeim.Core.Game;

/// <summary>The operating systems Valheim and LaunchHeim run on.</summary>
/// <remarks>
/// Passed explicitly instead of asking the OS everywhere, so the tests cover the Windows launch on
/// Linux and the other way round.
/// </remarks>
public enum GamePlatform
{
  Linux,
  Windows,
}

public static class GamePlatforms
{
  public static GamePlatform Current => OperatingSystem.IsWindows() ? GamePlatform.Windows : GamePlatform.Linux;
}
