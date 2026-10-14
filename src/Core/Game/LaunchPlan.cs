using System.Diagnostics;

namespace CINE.LaunchHeim.Core.Game;

public sealed record LaunchPlan(
  string FileName,
  string WorkingDirectory,
  IReadOnlyList<string> Arguments,
  IReadOnlyDictionary<string, string?> Environment)
{
  /// <summary>
  /// Set when <see cref="FileName"/> is Steam, which then starts the game: the game's executable, whose
  /// process LaunchHeim waits for (see <see cref="GameProcess"/>). Null when the game itself is started.
  /// </summary>
  public string? SteamStartsGame { get; init; }

  public ProcessStartInfo ToStartInfo()
  {
    var info = new ProcessStartInfo(FileName) { WorkingDirectory = WorkingDirectory, UseShellExecute = false };
    foreach (var argument in Arguments)
    {
      info.ArgumentList.Add(argument);
    }

    foreach (var (key, value) in Environment)
    {
      if (value is null)
      {
        info.Environment.Remove(key);
      }
      else
      {
        info.Environment[key] = value;
      }
    }

    return info;
  }
}

public sealed class LaunchException(string message) : Exception(message);

/// <summary>Builds the process for starting Valheim, either vanilla or with an instance's BepInEx.</summary>
/// <remarks>
/// <para>
/// This does what BepInExPack's <c>start_game_bepinex.sh</c> does, minus the script: preload
/// Unity Doorstop and point its target assembly at the <em>instance's</em> preloader. BepInEx derives
/// its root folder from that path, so plugins, configs and logs all come from the instance and the
/// game folder stays untouched. That is what makes several instances possible.
/// </para>
/// <para>
/// On Linux the game is started directly rather than through <c>steam -applaunch</c>: Steam starts it
/// with its own environment, and Doorstop only gets in through <c>LD_PRELOAD</c>. <c>SteamAppId</c> lets
/// the Steam API attach to the running client anyway, which is how the BepInEx script does it as well.
/// Steam's overlay (Shift+Tab, F12 screenshots) is what Steam injects into the games it starts, so
/// LaunchHeim loads it the same way Steam does (<see cref="AddSteamOverlay"/>).
/// </para>
/// <para>
/// On Windows Doorstop is not preloaded but found by the game: it is a <c>winhttp.dll</c> proxy that
/// Windows only loads from the game folder. <see cref="PrepareGameFolder"/> puts it there once, with a
/// <c>doorstop_config.ini</c> that keeps it disabled, and each launch switches it on and points it at
/// the instance with Doorstop 4's command-line options. r2modman does the same. The game folder then
/// holds two extra files, but still no mods, and a launch from Steam stays vanilla.
/// </para>
/// <para>
/// Because those options are plain arguments, Windows can start the game through Steam:
/// <c>steam.exe -applaunch 892970 &lt;arguments&gt;</c>, as r2modman does. Steam injects its overlay into
/// what it starts and nothing else; a Valheim started directly on Windows had no overlay and no F12
/// screenshots (found 2026-10-08). The game then isn't LaunchHeim's child, so LaunchHeim finds its
/// process by name. Valheim's launch options in Steam's game properties apply too, as with any Steam start.
/// </para>
/// <para>
/// The other way round, Steam can start LaunchHeim: <c>launchheim steam %command%</c> in Valheim's launch
/// options hands Steam's command to <see cref="WrapSteamCommand"/>, which adds the instance to it. Steam
/// then starts every launch, LaunchHeim's own on Windows included, so those are recognised and left as
/// they are (<see cref="IsLaunchHeimCommand"/>).
/// </para>
/// </remarks>
public static class GameLauncher
{
  public const string PreloaderPath = "BepInEx/core/BepInEx.Preloader.dll";
  public const string DoorstopLibrary = "doorstop_libs/libdoorstop_x64.so";
  public const string WindowsDoorstopProxy = "winhttp.dll";
  public const string WindowsDoorstopConfig = "doorstop_config.ini";
  public const string DoorstopEnabledArgument = "--doorstop-enabled";

  /// <summary>Valheim's argument for joining a server, which Steam's "Join game" passes as well.</summary>
  public const string ConnectArgument = "+connect";

  /// <summary>First line of the config LaunchHeim writes, so it can tell its own proxy from a manual install.</summary>
  public const string WindowsConfigMarker = "# Written by LaunchHeim.";

  /// <summary>
  /// Variables LaunchHeim's own Qt runtime sets on its process. The game must not inherit them, and
  /// neither should anything else we spawn (a Qt 6 file manager loading Qt 5 plugins crashes).
  /// </summary>
  public static readonly string[] HostOnlyVariables = ["QT_PLUGIN_PATH", "QML2_IMPORT_PATH", "QT_QPA_PLATFORM", "QT_QPA_PLATFORMTHEME", "QT_QUICK_CONTROLS_STYLE", "QT_QUICK_CONTROLS_MATERIAL_VARIANT"];

  /// <param name="steamDirectory">
  /// The Steam client's folder (<see cref="SteamLibraryLocator.ClientDirectory"/>), for its overlay on
  /// Linux and <c>steam.exe</c> on Windows. Without it the game starts directly, with no overlay.
  /// </param>
  public static LaunchPlan Plan(
    string gameDirectory,
    string? instanceDirectory,
    string extraArguments,
    IReadOnlyDictionary<string, string?> currentEnvironment,
    GamePlatform? platform = null,
    IReadOnlyList<string>? joinArguments = null,
    string? steamDirectory = null)
  {
    var target = platform ?? GamePlatforms.Current;
    var executable = Path.Combine(gameDirectory, SteamLibraryLocator.ExecutableFor(target));
    if (!File.Exists(executable))
    {
      throw new LaunchException($"Valheim was not found in {gameDirectory}. Set the game folder in Settings.");
    }

    var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
      ["SteamAppId"] = SteamLibraryLocator.ValheimAppId,
      ["SteamGameId"] = SteamLibraryLocator.ValheimAppId,
    };

    foreach (var variable in HostOnlyVariables)
    {
      environment[variable] = null;
    }

    if (target == GamePlatform.Windows)
    {
      var arguments = WindowsDoorstopArguments(gameDirectory, instanceDirectory);
      arguments.AddRange(SplitArguments(extraArguments));
      arguments.AddRange(joinArguments ?? []);
      var steam = steamDirectory is null ? null : Path.Combine(steamDirectory, SteamLibraryLocator.WindowsClient);
      if (steam is not null && File.Exists(steam))
      {
        // Steam adds Valheim's launch options, which may be `LaunchHeim.exe steam %command%`
        // (WrapSteamCommand). A Doorstop argument tells that start it is LaunchHeim's own, so it runs
        // as planned here instead of as the last play. Valheim ignores it without a proxy.
        if (!arguments.Contains(DoorstopEnabledArgument))
        {
          arguments.InsertRange(0, [DoorstopEnabledArgument, "false"]);
        }

        // steam.exe hands the launch to the running client and exits; SteamClient made sure one runs.
        return new LaunchPlan(steam, steamDirectory!, ["-applaunch", SteamLibraryLocator.ValheimAppId, .. arguments], environment)
        {
          SteamStartsGame = executable,
        };
      }

      return new LaunchPlan(executable, gameDirectory, arguments, environment);
    }

    AddSteamOverlay(environment, steamDirectory, currentEnvironment);
    if (instanceDirectory is null)
    {
      // A preload left over in the user's session would otherwise mod the "vanilla" launch.
      environment["DOORSTOP_DISABLE"] = "TRUE";
      environment["DOORSTOP_ENABLED"] = "0";
    }
    else
    {
      AddDoorstop(environment, instanceDirectory, currentEnvironment);
    }

    return new LaunchPlan(executable, gameDirectory, [.. SplitArguments(extraArguments), .. joinArguments ?? []], environment);
  }

  /// <summary>Makes Valheim join a dedicated server as soon as its menu is up.</summary>
  /// <remarks>
  /// <para>
  /// <c>+connect</c> is what Steam's "Join game" passes (<c>FejdStartup.HandleStartupJoin</c>). The game
  /// skips the main menu and opens the character selection, and its Start button joins the server.
  /// <c>-password</c> answers the server's password prompt (<c>ZNet.RPC_ClientHandshake</c>).
  /// </para>
  /// <para>
  /// Valheim also knows <c>-joinserverwithcharacter</c>, which would skip the character selection too.
  /// It runs in <c>Awake</c> before Steam is initialised and only finds characters kept off the cloud,
  /// so it is not used.
  /// </para>
  /// </remarks>
  public static IReadOnlyList<string> JoinArguments(string address, string? password) =>
    string.IsNullOrEmpty(password) ? [ConnectArgument, address] : [ConnectArgument, address, "-password", password];

  /// <summary>Where the game is in a command Steam passed with <c>%command%</c>, or -1 when it isn't there.</summary>
  /// <remarks>
  /// Steam may put wrappers in front of the game (its reaper, a compatibility tool, or what the user's
  /// launch options add before LaunchHeim), so the game is found by its file name.
  /// </remarks>
  public static int FindGame(IReadOnlyList<string> command)
  {
    for (var i = 0; i < command.Count; i++)
    {
      var name = Path.GetFileName(command[i]);
      if (name.Equals(SteamLibraryLocator.ExecutableFor(GamePlatform.Linux), StringComparison.OrdinalIgnoreCase)
        || name.Equals(SteamLibraryLocator.ExecutableFor(GamePlatform.Windows), StringComparison.OrdinalIgnoreCase))
      {
        return i;
      }
    }

    return -1;
  }

  /// <summary>
  /// Whether a command from Steam is a launch LaunchHeim planned itself: on Windows every launch goes
  /// through <c>steam.exe -applaunch</c>, and Steam then runs Valheim's launch options with LaunchHeim's
  /// arguments in the command. Those always carry a Doorstop argument (see <see cref="Plan"/>).
  /// </summary>
  public static bool IsLaunchHeimCommand(IReadOnlyList<string> command) => command.Contains(DoorstopEnabledArgument);

  /// <summary>Whether Steam passed a server to join, which a remembered destination must not replace.</summary>
  public static bool JoinsServer(IReadOnlyList<string> command) => command.Contains(ConnectArgument);

  /// <summary>
  /// Turns the command Steam starts Valheim with into a modded one: what <c>launchheim steam %command%</c>
  /// in Valheim's launch options runs.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Steam's own start is kept, with the environment Steam gives it, so its overlay, the play time and the
  /// "Running" state work as for any game. Only what LaunchHeim adds to its own launches is added:
  /// Doorstop's variables (Linux) or arguments (Windows) for the instance, the instance's launch arguments
  /// and the server to join. Steam's overlay preload is already in <c>LD_PRELOAD</c> and stays behind
  /// Doorstop, the order <c>start_game_bepinex.sh %command%</c> gives.
  /// </para>
  /// <para>
  /// The instance's arguments and the join arguments go at the end, after what Steam passed (such as
  /// <c>-force-glcore</c> for the OpenGL entry), and the game's folder is the working directory, as Steam
  /// sets it.
  /// </para>
  /// </remarks>
  /// <param name="command">Steam's command, from the wrapper's arguments. It has to contain the game (<see cref="FindGame"/>).</param>
  public static LaunchPlan WrapSteamCommand(
    IReadOnlyList<string> command,
    string? instanceDirectory,
    string extraArguments,
    IReadOnlyDictionary<string, string?> currentEnvironment,
    GamePlatform? platform = null,
    IReadOnlyList<string>? joinArguments = null)
  {
    var gameIndex = FindGame(command);
    if (gameIndex < 0)
    {
      throw new LaunchException("Steam's command doesn't start Valheim. Put `%command%` after LaunchHeim's arguments in Valheim's launch options.");
    }

    var gameDirectory = Path.GetDirectoryName(Path.GetFullPath(command[gameIndex]))!;
    var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
    foreach (var variable in HostOnlyVariables)
    {
      environment[variable] = null;
    }

    List<string> added = [];
    if ((platform ?? GamePlatforms.Current) == GamePlatform.Windows)
    {
      added.AddRange(WindowsDoorstopArguments(gameDirectory, instanceDirectory));
    }
    else if (instanceDirectory is null)
    {
      environment["DOORSTOP_DISABLE"] = "TRUE";
      environment["DOORSTOP_ENABLED"] = "0";
    }
    else
    {
      AddDoorstop(environment, instanceDirectory, currentEnvironment);
    }

    added.AddRange(SplitArguments(extraArguments));
    added.AddRange(joinArguments ?? []);
    return new LaunchPlan(command[0], gameDirectory, [.. command.Skip(1), .. added], environment);
  }

  /// <summary>The arguments as they may be logged: a server password is replaced with stars.</summary>
  public static IEnumerable<string> Redact(IEnumerable<string> arguments)
  {
    var hideNext = false;
    foreach (var argument in arguments)
    {
      yield return hideNext ? "***" : argument;
      hideNext = argument == "-password";
    }
  }

  // Doorstop 4 reads these before the game does, and Valheim ignores them.
  private static List<string> WindowsDoorstopArguments(string gameDirectory, string? instanceDirectory)
  {
    if (instanceDirectory is null)
    {
      // Only matters when a manual BepInEx install in the game folder has Doorstop switched on.
      return File.Exists(Path.Combine(gameDirectory, WindowsDoorstopProxy)) ? [DoorstopEnabledArgument, "false"] : [];
    }

    var preloader = Path.Combine(instanceDirectory, PreloaderPath);
    if (!File.Exists(preloader) || !File.Exists(Path.Combine(gameDirectory, WindowsDoorstopProxy)))
    {
      throw new LaunchException("BepInEx is not installed in this instance. Install BepInExPack_Valheim from Thunderstore first.");
    }

    List<string> arguments = [DoorstopEnabledArgument, "true", "--doorstop-target-assembly", Path.GetFullPath(preloader)];
    var corlib = Path.Combine(instanceDirectory, "unstripped_corlib");
    if (Directory.Exists(corlib))
    {
      arguments.AddRange(["--doorstop-mono-dll-search-path-override", Path.GetFullPath(corlib)]);
    }

    return arguments;
  }

  /// <summary>
  /// Puts the instance's Doorstop proxy into the game folder before a modded launch on Windows. Nothing
  /// happens on Linux or for a vanilla launch.
  /// </summary>
  /// <remarks>
  /// A proxy that is not LaunchHeim's (a manual BepInEx install, recognised by a config without the
  /// marker) is left alone: replacing it could break that setup, and the launch arguments steer it anyway.
  /// LaunchHeim's own proxy is refreshed when the instance brings a different one, so it always matches
  /// the instance's BepInEx.
  /// </remarks>
  public static void PrepareGameFolder(string gameDirectory, string? instanceDirectory, GamePlatform? platform = null)
  {
    if ((platform ?? GamePlatforms.Current) != GamePlatform.Windows || instanceDirectory is null)
    {
      return;
    }

    var source = Path.Combine(instanceDirectory, WindowsDoorstopProxy);
    if (!File.Exists(source))
    {
      throw new LaunchException("BepInEx is not installed in this instance. Install BepInExPack_Valheim from Thunderstore first.");
    }

    var proxy = Path.Combine(gameDirectory, WindowsDoorstopProxy);
    var config = Path.Combine(gameDirectory, WindowsDoorstopConfig);
    var ours = File.Exists(config) && File.ReadLines(config).FirstOrDefault() == WindowsConfigMarker;
    if (File.Exists(proxy) && !ours)
    {
      return;
    }

    if (!File.Exists(proxy) || !File.ReadAllBytes(proxy).AsSpan().SequenceEqual(File.ReadAllBytes(source)))
    {
      File.Copy(source, proxy, overwrite: true);
    }

    if (!File.Exists(config) || ours)
    {
      File.WriteAllText(config, $"""
        {WindowsConfigMarker}
        # Keeps Doorstop off, so starting Valheim from Steam stays vanilla. LaunchHeim turns it on per
        # launch with --doorstop-enabled and points it at the instance. Delete this file and winhttp.dll
        # to remove LaunchHeim from the game folder completely.
        [General]
        enabled = false
        target_assembly = BepInEx\core\BepInEx.Preloader.dll

        """);
    }
  }

  private static void AddDoorstop(
    Dictionary<string, string?> environment,
    string instanceDirectory,
    IReadOnlyDictionary<string, string?> currentEnvironment)
  {
    var preloader = Path.Combine(instanceDirectory, PreloaderPath);
    var doorstop = Path.Combine(instanceDirectory, DoorstopLibrary);
    if (!File.Exists(preloader) || !File.Exists(doorstop))
    {
      throw new LaunchException("BepInEx is not installed in this instance. Install BepInExPack_Valheim from Thunderstore first.");
    }

    var corlib = Path.Combine(instanceDirectory, "unstripped_corlib");
    var corlibOverride = Directory.Exists(corlib) ? corlib : "";

    // Doorstop 4, used by BepInExPack_Valheim 5.4.22 and newer.
    environment["DOORSTOP_ENABLED"] = "1";
    environment["DOORSTOP_TARGET_ASSEMBLY"] = preloader;
    environment["DOORSTOP_IGNORE_DISABLED_ENV"] = "0";
    environment["DOORSTOP_BOOT_CONFIG_OVERRIDE"] = "";
    environment["DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE"] = corlibOverride;
    environment["DOORSTOP_MONO_DEBUG_ENABLED"] = "0";
    environment["DOORSTOP_MONO_DEBUG_ADDRESS"] = "127.0.0.1:10000";
    environment["DOORSTOP_MONO_DEBUG_SUSPEND"] = "0";

    // Doorstop 3, used by older packs that people pin for old mods. Doorstop 4 ignores these.
    environment["DOORSTOP_ENABLE"] = "TRUE";
    environment["DOORSTOP_INVOKE_DLL_PATH"] = preloader;
    environment["DOORSTOP_CORLIB_OVERRIDE_PATH"] = corlibOverride;

    var doorstopDirectory = Path.GetDirectoryName(doorstop)!;
    environment["LD_LIBRARY_PATH"] = Prepend(doorstopDirectory, currentEnvironment.GetValueOrDefault("LD_LIBRARY_PATH"));
    // In front of Steam's overlay, if AddSteamOverlay added it: the order a start_game_bepinex.sh in
    // Steam's launch options gives, which is what BepInEx is tested with.
    environment["LD_PRELOAD"] = Prepend(Path.GetFileName(doorstop), environment.GetValueOrDefault("LD_PRELOAD") ?? currentEnvironment.GetValueOrDefault("LD_PRELOAD"));
  }

  /// <summary>Loads Steam's overlay into the game the way Steam does for the games it starts on Linux.</summary>
  /// <remarks>
  /// <para>
  /// Steam preloads <c>gameoverlayrenderer.so</c>, which draws the overlay into OpenGL games and talks to
  /// the client, and switches on its Vulkan layer (<c>VK_LAYER_VALVE_steam_overlay</c>, installed as an
  /// implicit layer in <c>~/.local/share/vulkan/implicit_layer.d</c>) for Vulkan, which Valheim uses on
  /// Linux. Both find the game by <c>SteamGameId</c>/<c>SteamOverlayGameId</c>. Steam also preloads the
  /// 32-bit build, which a 64-bit game only refuses with a warning, so it is left out.
  /// </para>
  /// <para>
  /// Skipped when the overlay is already preloaded: LaunchHeim itself started from Steam (as a
  /// non-Steam game) passes Steam's environment on. A Steam without the library (another layout, or none
  /// found) only costs the overlay.
  /// </para>
  /// </remarks>
  private static void AddSteamOverlay(
    Dictionary<string, string?> environment,
    string? steamDirectory,
    IReadOnlyDictionary<string, string?> currentEnvironment)
  {
    var preload = currentEnvironment.GetValueOrDefault("LD_PRELOAD");
    var overlay = steamDirectory is null ? null : Path.Combine(steamDirectory, "ubuntu12_64", "gameoverlayrenderer.so");
    if (overlay is null || !File.Exists(overlay) || (preload?.Contains("gameoverlayrenderer.so", StringComparison.Ordinal) ?? false))
    {
      return;
    }

    environment["LD_PRELOAD"] = Prepend(overlay, preload);
    environment["ENABLE_VK_LAYER_VALVE_steam_overlay_1"] = "1";
    environment["SteamOverlayGameId"] = SteamLibraryLocator.ValheimAppId;
  }

  private static string Prepend(string value, string? existing) =>
    string.IsNullOrEmpty(existing) ? value : $"{value}:{existing}";

  /// <summary>Splits launch arguments like a shell would for the simple cases: spaces and quotes.</summary>
  public static IReadOnlyList<string> SplitArguments(string? arguments)
  {
    var result = new List<string>();
    if (string.IsNullOrWhiteSpace(arguments))
    {
      return result;
    }

    var current = new System.Text.StringBuilder();
    var inToken = false;
    char? quote = null;
    foreach (var c in arguments)
    {
      if (quote is { } q)
      {
        if (c == q)
        {
          quote = null;
        }
        else
        {
          current.Append(c);
        }
      }
      else if (c is '"' or '\'')
      {
        quote = c;
        inToken = true;
      }
      else if (char.IsWhiteSpace(c))
      {
        if (inToken)
        {
          result.Add(current.ToString());
          current.Clear();
          inToken = false;
        }
      }
      else
      {
        current.Append(c);
        inToken = true;
      }
    }

    if (inToken)
    {
      result.Add(current.ToString());
    }

    return result;
  }

  public static IReadOnlyDictionary<string, string?> CurrentEnvironment() =>
    System.Environment.GetEnvironmentVariables()
      .Cast<System.Collections.DictionaryEntry>()
      .ToDictionary(e => (string)e.Key, e => (string?)e.Value, StringComparer.Ordinal);
}
