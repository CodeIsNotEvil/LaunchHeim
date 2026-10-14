using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Mods;

namespace CINE.LaunchHeim.Core.Tests;

public class GamePluginTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly string _plugin;
  private readonly string _instance;

  public GamePluginTests()
  {
    _plugin = Path.Combine(_temp.Tree("build", GamePlugin.PluginFileName), GamePlugin.PluginFileName);
    _instance = _temp.Tree("instance", GameLauncher.PreloaderPath);
  }

  public void Dispose() => _temp.Dispose();

  private static readonly DirectPlay Instant = DirectPlay.World("world:midgard", "Midgard", "ragnar", instant: true);
  private static readonly DirectPlay NotInstant = DirectPlay.World("world:midgard", "Midgard", "ragnar");

  // Downloading BepInExPack is ModService's job; here it must not be reached unless a test says so.
  private GamePlugin Create(string? plugin = null, Func<ModService>? mods = null) =>
    new(_temp.AppPaths, _ => mods?.Invoke() ?? throw new InvalidOperationException("No download expected."), plugin ?? _plugin);

  private Task<GamePlugin.Result> Prepare(string? instance, DirectPlay? direct, bool keepAchievements = false, GamePlugin? plugin = null) =>
    (plugin ?? Create()).PrepareAsync(instance, direct, keepAchievements, null, CancellationToken.None);

  [Fact]
  public async Task Plain_vanilla_gets_no_plugin()
  {
    var result = await Prepare(null, NotInstant, keepAchievements: true);

    Assert.Null(result.InstanceDirectory);
    Assert.Empty(result.Arguments);
    Assert.Null(result.Problem);
  }

  [Fact]
  public async Task Every_instance_launch_gets_the_plugin_and_the_achievements_setting()
  {
    var kept = await Prepare(_instance, null, keepAchievements: true);
    var notKept = await Prepare(_instance, NotInstant);

    Assert.Equal(_instance, kept.InstanceDirectory);
    Assert.Equal([GamePlugin.AchievementsArgument, "1"], kept.Arguments);
    // 0 rather than nothing, so the plugin takes back what an earlier launch set.
    Assert.Equal([GamePlugin.AchievementsArgument, "0"], notKept.Arguments);
    Assert.Equal(File.ReadAllText(_plugin), File.ReadAllText(Path.Combine(_instance, GamePlugin.PluginPath)));
  }

  [Fact]
  public async Task An_older_plugin_in_the_instance_is_replaced_and_its_old_name_removed()
  {
    var folder = Path.Combine(_instance, GamePlugin.PluginDirectory);
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(_instance, GamePlugin.PluginPath), "from an older LaunchHeim");
    File.WriteAllText(Path.Combine(folder, "LaunchHeim.InstantStart.dll"), "the plugin's old name");

    await Prepare(_instance, Instant);

    Assert.Equal([GamePlugin.PluginFileName], Directory.GetFiles(folder).Select(Path.GetFileName));
    Assert.Equal(File.ReadAllText(_plugin), File.ReadAllText(Path.Combine(_instance, GamePlugin.PluginPath)));
  }

  [Fact]
  public async Task Vanilla_with_instant_start_runs_from_launchheims_own_setup_once_it_has_bepinex()
  {
    var plugin = Create();
    var setup = plugin.VanillaStore.Create("Vanilla instant start");
    var directory = plugin.VanillaStore.DirectoryOf(setup);
    _temp.Tree(Path.GetRelativePath(_temp.Path, directory), GameLauncher.PreloaderPath);

    var result = await Prepare(null, Instant, plugin: plugin);

    Assert.Equal(directory, result.InstanceDirectory);
    Assert.True(File.Exists(Path.Combine(directory, GamePlugin.PluginPath)));
    // Apart from the user's instances, so it never shows in the library.
    Assert.Empty(new Instances.InstanceStore(_temp.AppPaths).LoadAll());
  }

  [Fact]
  public async Task Vanilla_starts_plainly_when_bepinex_cant_be_downloaded()
  {
    var result = await Prepare(null, Instant, plugin: Create(mods: () => throw new HttpRequestException("offline")));

    Assert.Null(result.InstanceDirectory);
    Assert.Empty(result.Arguments);
    Assert.Equal("offline", result.Problem);
  }

  [Fact]
  public async Task A_missing_plugin_is_only_reported_when_the_player_asked_for_what_it_does()
  {
    var missing = Create(plugin: _temp.Combine("nowhere", GamePlugin.PluginFileName));

    var asked = await Prepare(_instance, Instant, plugin: missing);
    var notAsked = await Prepare(_instance, NotInstant, plugin: missing);

    Assert.Equal(_instance, asked.InstanceDirectory);
    Assert.Contains("missing", asked.Problem);
    Assert.Empty(asked.Arguments);
    Assert.Null(notAsked.Problem);
  }

  [Fact]
  public void Instant_start_is_one_argument_after_the_join()
  {
    Assert.Equal(["+connect", "10.0.0.1:2456", GamePlugin.InstantArgument], DirectPlay.Server("server:x", "X", "10.0.0.1:2456", "ragnar", null, instant: true).Arguments);
    Assert.Equal([GamePlugin.InstantArgument], Instant.Arguments);
    Assert.Empty(NotInstant.Arguments);
  }
}
