using CINE.LaunchHeim.Core.Catalogs;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Game;

/// <summary>
/// Puts LaunchHeim's BepInEx plugin (<c>src/GamePlugin</c>) into the launch and tells it what to do:
/// "Start instantly" (<see cref="InstantArgument"/>) and keeping achievements with mods
/// (<see cref="AchievementsArgument"/>).
/// </summary>
/// <remarks>
/// <para>
/// Valheim has no option to load a world, and <c>+connect</c> still stops at the character selection
/// (decision 0010); with mods it gives no achievements unless a character carries the dev command's
/// switch. Only something inside the game can do either, which needs BepInEx. Every instance has it,
/// so every instance launch gets the plugin, copied into <c>BepInEx/plugins/LaunchHeim/</c> so it always
/// matches this LaunchHeim. It does only what the arguments say. It isn't a mod of the instance: packs
/// export configs and the mod list, never this folder.
/// </para>
/// <para>
/// A vanilla launch has no BepInEx, and with no mods nothing takes achievements away. Only "Start
/// instantly" needs the plugin there, so such a launch runs from a setup LaunchHeim manages for it:
/// BepInExPack and this plugin, no gameplay mods, in <c>instant-start/</c> in the data folder. It is laid
/// out like an instance (made with <see cref="InstanceStore"/> and <see cref="ModService"/> on that
/// folder) because <see cref="GameLauncher"/> starts it like one. BepInExPack is downloaded the first time
/// it's needed. Dedicated servers don't check what clients load, so vanilla servers accept it.
/// </para>
/// <para>
/// Nothing here stops a launch. When the plugin or the vanilla setup can't be got ready, the game starts
/// as it would without them (the arguments mean nothing without the plugin) and the problem is reported
/// when the player asked for something the plugin does.
/// </para>
/// </remarks>
/// <param name="modsFor">Makes a <see cref="ModService"/> on the vanilla setup's store; called only when BepInExPack has to be installed there.</param>
/// <param name="bundledPlugin">The plugin LaunchHeim ships, <see cref="BundledPlugin"/> outside tests.</param>
public sealed class GamePlugin(AppPaths paths, Func<InstanceStore, ModService> modsFor, string? bundledPlugin = null)
{
  /// <summary>Start instantly. The same constant is in the plugin's <c>InstantStart</c>.</summary>
  public const string InstantArgument = "-launchheim-instant";

  /// <summary>Followed by 1 or 0: keep achievements with mods or not. The same constant is in the plugin's <c>Achievements</c>.</summary>
  public const string AchievementsArgument = "-launchheim-achievements";

  public const string PluginFileName = "LaunchHeim.GamePlugin.dll";

  /// <summary>LaunchHeim's folder among an instance's plugins. Nothing else belongs in it.</summary>
  public static readonly string PluginDirectory = Path.Combine("BepInEx", "plugins", "LaunchHeim");

  /// <summary>Where the plugin goes in an instance, relative to it.</summary>
  public static readonly string PluginPath = Path.Combine(PluginDirectory, PluginFileName);

  /// <summary>The copy built with LaunchHeim, next to its executable (see the Desktop csproj).</summary>
  public static string BundledPlugin => Path.Combine(AppContext.BaseDirectory, "bepinex", PluginFileName);

  private readonly string _plugin = bundledPlugin ?? BundledPlugin;

  /// <summary>The vanilla setup's own store, apart from the user's instances so it never shows in the library.</summary>
  public InstanceStore VanillaStore { get; } = new(paths with { DataDirectory = Path.Combine(paths.DataDirectory, "instant-start") });

  /// <summary>What to launch with.</summary>
  /// <param name="InstanceDirectory">The instance to start, or the vanilla setup; null for plain vanilla.</param>
  /// <param name="Arguments">For the plugin, after the game's other arguments. Empty when it isn't there.</param>
  /// <param name="Problem">Why the plugin won't do what the player asked this time, or null.</param>
  public sealed record Result(string? InstanceDirectory, IReadOnlyList<string> Arguments, string? Problem);

  /// <param name="instanceDirectory">The instance being played, or null for vanilla.</param>
  /// <param name="direct">The server or world, whose <see cref="DirectPlay.Instant"/> asks for instant start.</param>
  /// <param name="keepAchievements">Settings' "Keep achievements with mods" (<see cref="AppSettings.KeepAchievements"/>).</param>
  public async Task<Result> PrepareAsync(
    string? instanceDirectory,
    DirectPlay? direct,
    bool keepAchievements,
    IProgress<InstallProgress>? progress,
    CancellationToken cancellationToken)
  {
    var instant = direct is { Instant: true };
    if (instanceDirectory is null && !instant)
    {
      return new Result(null, [], null);
    }

    try
    {
      var target = instanceDirectory ?? await EnsureVanillaSetupAsync(progress, cancellationToken);
      InstallPlugin(target);
      // Always 1 or 0: with 0 the plugin takes back what an earlier "keep" set (see its Achievements).
      return new Result(target, [AchievementsArgument, keepAchievements ? "1" : "0"], null);
    }
    catch (Exception ex) when (ex is LaunchException or CatalogException or HttpRequestException or IOException or UnauthorizedAccessException)
    {
      var asked = instant || keepAchievements;
      Log.Write(asked ? LogLevel.Error : LogLevel.Warning, $"LaunchHeim's game plugin isn't in this launch: {ex.Message}");
      return new Result(instanceDirectory, [], asked ? ex.Message : null);
    }
  }

  /// <summary>
  /// Copies the plugin into the instance when it isn't there or differs, and clears anything else from
  /// LaunchHeim's plugin folder, such as <c>LaunchHeim.InstantStart.dll</c> from before the plugin had
  /// its current name, which would otherwise press Start a second time.
  /// </summary>
  public void InstallPlugin(string instanceDirectory)
  {
    if (!File.Exists(_plugin))
    {
      throw new LaunchException($"LaunchHeim's game plugin is missing ({_plugin}). Reinstall LaunchHeim.");
    }

    var directory = Path.Combine(instanceDirectory, PluginDirectory);
    var target = Path.Combine(instanceDirectory, PluginPath);
    if (Directory.Exists(directory))
    {
      foreach (var file in Directory.EnumerateFiles(directory).Where(f => !Path.GetFileName(f).Equals(PluginFileName, StringComparison.Ordinal)))
      {
        File.Delete(file);
      }
    }

    if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(_plugin)))
    {
      return;
    }

    Directory.CreateDirectory(directory);
    File.Copy(_plugin, target, overwrite: true);
  }

  /// <summary>The vanilla setup's folder, with BepInExPack installed the first time.</summary>
  private async Task<string> EnsureVanillaSetupAsync(IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
  {
    var setup = VanillaStore.LoadAll().FirstOrDefault() ?? VanillaStore.Create("Vanilla instant start");
    var directory = VanillaStore.DirectoryOf(setup);
    if (File.Exists(Path.Combine(directory, GameLauncher.PreloaderPath)))
    {
      return directory;
    }

    Log.Info($"Setting up BepInEx for instant start in {directory}");
    var report = await modsFor(VanillaStore).InstallLoaderAsync(setup, progress, cancellationToken);
    if (!File.Exists(Path.Combine(directory, GameLauncher.PreloaderPath)))
    {
      throw new LaunchException(report.Warnings.FirstOrDefault() ?? "BepInEx could not be set up for instant start.");
    }

    return directory;
  }
}
