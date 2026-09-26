using System.Diagnostics;

namespace CINE.LaunchHeim.Core.Game;

public sealed record LaunchPlan(
  string FileName,
  string WorkingDirectory,
  IReadOnlyList<string> Arguments,
  IReadOnlyDictionary<string, string?> Environment)
{
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
/// The game is started directly rather than through <c>steam -applaunch</c>, because Steam's launch
/// options cannot be set per launch. <c>SteamAppId</c> lets the Steam API attach to the running client
/// anyway, which is how the BepInEx script does it as well.
/// </para>
/// </remarks>
public static class GameLauncher
{
  public const string PreloaderPath = "BepInEx/core/BepInEx.Preloader.dll";
  public const string DoorstopLibrary = "doorstop_libs/libdoorstop_x64.so";

  /// <summary>
  /// Variables LaunchHeim's own Qt runtime sets on its process. The game must not inherit them, and
  /// neither should anything else we spawn (a Qt 6 file manager loading Qt 5 plugins crashes).
  /// </summary>
  public static readonly string[] HostOnlyVariables = ["QT_PLUGIN_PATH", "QML2_IMPORT_PATH", "QT_QPA_PLATFORM", "QT_QPA_PLATFORMTHEME", "QT_QUICK_CONTROLS_STYLE", "QT_QUICK_CONTROLS_MATERIAL_VARIANT"];

  public static LaunchPlan Plan(
    string gameDirectory,
    string? instanceDirectory,
    string extraArguments,
    IReadOnlyDictionary<string, string?> currentEnvironment)
  {
    var executable = Path.Combine(gameDirectory, SteamLibraryLocator.ValheimExecutable);
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

    return new LaunchPlan(executable, gameDirectory, SplitArguments(extraArguments), environment);
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
    environment["LD_PRELOAD"] = Prepend(Path.GetFileName(doorstop), currentEnvironment.GetValueOrDefault("LD_PRELOAD"));
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
