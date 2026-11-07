using System.Globalization;
using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>Builds the process for a local dedicated server, vanilla or with BepInEx from the server's folder.</summary>
/// <remarks>
/// <para>
/// This does what the server's <c>start_headless_server.sh</c> / <c>.bat</c> and BepInExPack's
/// <c>start_server_bepinex.sh</c> do, without the scripts: Valheim's arguments, <c>SteamAppId</c> (the
/// server talks to Steam as the game, 892970) and on Linux the server's own <c>linux64</c> libraries.
/// BepInEx gets in the same way as for the game (<see cref="GameLauncher"/>): Doorstop pointed at the
/// preloader in the <em>server's</em> folder, so Steam's copy of the server stays vanilla on Linux and only
/// gains Doorstop's disabled proxy on Windows, exactly like the game folder.
/// </para>
/// <para>
/// <c>-public</c> is always passed. Valheim treats a missing one as public (<c>FejdStartup.ParseServerArguments</c>),
/// which would also switch on its password rules.
/// </para>
/// </remarks>
public static class ServerLauncher
{
  /// <summary>Valheim's Steam app id, which the server reports itself as (the start scripts export it).</summary>
  public const string GameAppId = SteamLibraryLocator.ValheimAppId;

  /// <param name="serverDirectory">The dedicated server's folder (<see cref="DedicatedServer.DirectoryFor"/>).</param>
  /// <param name="modsDirectory">The server's own folder with BepInEx in it, or null for vanilla.</param>
  /// <param name="saveDirectory">Where its worlds go (<see cref="ServerStore.SaveDirectoryOf"/>).</param>
  /// <param name="logFile">Valheim's log (<see cref="ServerStore.LogFileOf"/>), which the console follows.</param>
  public static LaunchPlan Plan(
    ServerProfile server,
    string serverDirectory,
    string? modsDirectory,
    string saveDirectory,
    string logFile,
    IReadOnlyDictionary<string, string?> currentEnvironment,
    GamePlatform? platform = null)
  {
    var target = platform ?? GamePlatforms.Current;
    var executable = Path.Combine(serverDirectory, DedicatedServer.ExecutableFor(target));
    if (!File.Exists(executable))
    {
      throw new LaunchException($"Valheim's dedicated server was not found in {serverDirectory}.");
    }

    if (modsDirectory is not null && !File.Exists(Path.Combine(modsDirectory, GameLauncher.PreloaderPath)))
    {
      throw new LaunchException("BepInEx is missing from the server's folder. Start the server again so LaunchHeim can set it up.");
    }

    var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
      ["SteamAppId"] = GameAppId,
    };

    foreach (var variable in GameLauncher.HostOnlyVariables)
    {
      environment[variable] = null;
    }

    var arguments = Arguments(server, saveDirectory, logFile);
    if (target == GamePlatform.Windows)
    {
      return new LaunchPlan(executable, serverDirectory, [.. GameLauncher.WindowsDoorstopArguments(serverDirectory, modsDirectory), .. arguments], environment);
    }

    // The server's own Steam API and PlayFab libraries.
    var libraries = GameLauncher.Prepend(Path.Combine(serverDirectory, "linux64"), currentEnvironment.GetValueOrDefault("LD_LIBRARY_PATH"));
    environment["LD_LIBRARY_PATH"] = libraries;
    if (modsDirectory is null)
    {
      environment["DOORSTOP_DISABLE"] = "TRUE";
      environment["DOORSTOP_ENABLED"] = "0";
    }
    else
    {
      var withLibraries = new Dictionary<string, string?>(currentEnvironment, StringComparer.Ordinal) { ["LD_LIBRARY_PATH"] = libraries };
      GameLauncher.AddDoorstop(environment, modsDirectory, withLibraries);
    }

    return new LaunchPlan(executable, serverDirectory, arguments, environment);
  }

  /// <summary>Valheim's own arguments for the profile, the same on both platforms.</summary>
  public static IReadOnlyList<string> Arguments(ServerProfile server, string saveDirectory, string logFile)
  {
    List<string> arguments =
    [
      "-nographics",
      "-batchmode",
      "-name", server.Name.Trim(),
      "-port", server.Port.ToString(CultureInfo.InvariantCulture),
      "-world", server.World.Trim(),
      "-public", server.Public ? "1" : "0",
      "-savedir", Path.GetFullPath(saveDirectory),
      "-logFile", Path.GetFullPath(logFile),
      "-saveinterval", server.SaveInterval.ToString(CultureInfo.InvariantCulture),
      "-backups", server.Backups.ToString(CultureInfo.InvariantCulture),
    ];

    if (server.Password.Length > 0)
    {
      arguments.AddRange(["-password", server.Password]);
    }

    if (server.Crossplay)
    {
      arguments.Add("-crossplay");
    }

    if (server.Preset.Length > 0)
    {
      arguments.AddRange(["-preset", server.Preset.ToLowerInvariant()]);
    }

    arguments.AddRange(GameLauncher.SplitArguments(server.ExtraArguments));
    return arguments;
  }
}
