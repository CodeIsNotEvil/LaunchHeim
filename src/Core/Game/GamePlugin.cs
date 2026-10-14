using CINE.LaunchHeim.Core.Catalogs;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Game;

/// <summary>
/// Gets a launch ready for "Start instantly": LaunchHeim's BepInEx plugin (<c>src/InstantStart</c>) in the
/// instance, which presses Start in Valheim's menus once the game passes <see cref="Argument"/>.
/// </summary>
/// <remarks>
/// <para>
/// Valheim has no option to load a world, and <c>+connect</c> still stops at the character selection
/// (decision 0010). Only something inside the game can press Start, which needs BepInEx. An instance has
/// it; the plugin is copied into its <c>BepInEx/plugins/LaunchHeim/</c> before each such launch, so it
/// always matches this LaunchHeim. It stays there afterwards and does nothing without the argument.
/// It isn't a mod of the instance: packs export configs and the mod list, never this folder.
/// </para>
/// <para>
/// A vanilla launch has no BepInEx, so one with "Start instantly" on runs from a setup LaunchHeim manages
/// for it: BepInExPack and this plugin, no gameplay mods, in <c>instant-start/</c> in the data folder.
/// It is laid out like an instance (made with <see cref="InstanceStore"/> and <see cref="ModService"/>
/// on that folder) because <see cref="GameLauncher"/> starts it like one. BepInExPack is downloaded the
/// first time it's needed. Dedicated servers don't check what clients load, so vanilla servers accept it.
/// </para>
/// <para>
/// Nothing here stops a launch. When the plugin or the vanilla setup can't be got ready, the game starts
/// as it would without instant start (the argument means nothing without the plugin) and the problem is
/// reported, because the player still gets the right game with the right setup, only one or two clicks
/// short.
/// </para>
/// </remarks>
/// <param name="modsFor">Makes a <see cref="ModService"/> on the vanilla setup's store; called only when BepInExPack has to be installed there.</param>
/// <param name="bundledPlugin">The plugin LaunchHeim ships, <see cref="BundledPlugin"/> outside tests.</param>
public sealed class InstantStart(AppPaths paths, Func<InstanceStore, ModService> modsFor, string? bundledPlugin = null)
{
  /// <summary>What the plugin waits for. The same constant is in <c>InstantStartPlugin</c>.</summary>
  public const string Argument = "-launchheim-instant";

  public const string PluginFileName = "LaunchHeim.InstantStart.dll";

  /// <summary>Where the plugin goes in an instance, relative to it.</summary>
  public static readonly string PluginPath = Path.Combine("BepInEx", "plugins", "LaunchHeim", PluginFileName);

  /// <summary>The copy built with LaunchHeim, next to its executable (see the Desktop csproj).</summary>
  public static string BundledPlugin => Path.Combine(AppContext.BaseDirectory, "bepinex", PluginFileName);

  private readonly string _plugin = bundledPlugin ?? BundledPlugin;

  /// <summary>The vanilla setup's own store, apart from the user's instances so it never shows in the library.</summary>
  public InstanceStore VanillaStore { get; } = new(paths with { DataDirectory = Path.Combine(paths.DataDirectory, "instant-start") });

  /// <summary>What to launch with, and why instant start won't happen this time if it won't.</summary>
  /// <param name="InstanceDirectory">The instance to start, or the vanilla setup; null for plain vanilla.</param>
  /// <param name="Problem">Why the launch goes ahead without instant start, or null.</param>
  public sealed record Result(string? InstanceDirectory, string? Problem);

  /// <param name="instanceDirectory">The instance being played, or null for vanilla.</param>
  /// <param name="direct">The server or world; instant start only applies when it asks for it.</param>
  public async Task<Result> PrepareAsync(string? instanceDirectory, DirectPlay? direct, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
  {
    if (direct is not { Instant: true })
    {
      return new Result(instanceDirectory, null);
    }

    try
    {
      var target = instanceDirectory ?? await EnsureVanillaSetupAsync(progress, cancellationToken);
      InstallPlugin(target);
      return new Result(target, null);
    }
    catch (Exception ex) when (ex is LaunchException or CatalogException or HttpRequestException or IOException or UnauthorizedAccessException)
    {
      Log.Error($"Instant start isn't possible this time: {ex.Message}", ex is LaunchException ? null : ex);
      return new Result(instanceDirectory, ex.Message);
    }
  }

  /// <summary>Copies the plugin into the instance when it isn't there or differs.</summary>
  public void InstallPlugin(string instanceDirectory)
  {
    if (!File.Exists(_plugin))
    {
      throw new LaunchException($"LaunchHeim's instant-start plugin is missing ({_plugin}). Reinstall LaunchHeim.");
    }

    var target = Path.Combine(instanceDirectory, PluginPath);
    if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(_plugin)))
    {
      return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
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
