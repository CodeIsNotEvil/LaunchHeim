using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Mods;

namespace CINE.LaunchHeim.Core.Tests;

public class InstantStartTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly string _plugin;
  private readonly string _instance;

  public InstantStartTests()
  {
    _plugin = Path.Combine(_temp.Tree("build", InstantStart.PluginFileName), InstantStart.PluginFileName);
    _instance = _temp.Tree("instance", GameLauncher.PreloaderPath);
  }

  public void Dispose() => _temp.Dispose();

  private static readonly DirectPlay Instant = DirectPlay.World("world:midgard", "Midgard", "ragnar", instant: true);

  // Downloading BepInExPack is ModService's job; here it must not be reached unless a test says so.
  private InstantStart Create(string? plugin = null, Func<ModService>? mods = null) =>
    new(_temp.AppPaths, _ => mods?.Invoke() ?? throw new InvalidOperationException("No download expected."), plugin ?? _plugin);

  [Fact]
  public async Task A_launch_without_instant_start_is_left_alone()
  {
    var result = await Create().PrepareAsync(_instance, DirectPlay.World("world:midgard", "Midgard", "ragnar"), null, CancellationToken.None);

    Assert.Equal(new InstantStart.Result(_instance, null), result);
    Assert.False(File.Exists(Path.Combine(_instance, InstantStart.PluginPath)));
  }

  [Fact]
  public async Task An_instance_gets_the_plugin()
  {
    var result = await Create().PrepareAsync(_instance, Instant, null, CancellationToken.None);

    Assert.Equal(new InstantStart.Result(_instance, null), result);
    Assert.Equal(File.ReadAllText(_plugin), File.ReadAllText(Path.Combine(_instance, InstantStart.PluginPath)));
  }

  [Fact]
  public async Task An_older_plugin_in_the_instance_is_replaced()
  {
    var installed = Path.Combine(_instance, InstantStart.PluginPath);
    Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
    File.WriteAllText(installed, "from an older LaunchHeim");

    await Create().PrepareAsync(_instance, Instant, null, CancellationToken.None);

    Assert.Equal(File.ReadAllText(_plugin), File.ReadAllText(installed));
  }

  [Fact]
  public async Task Vanilla_runs_from_launchheims_own_setup_once_it_has_bepinex()
  {
    var instantStart = Create();
    var setup = instantStart.VanillaStore.Create("Vanilla instant start");
    var directory = instantStart.VanillaStore.DirectoryOf(setup);
    _temp.Tree(Path.GetRelativePath(_temp.Path, directory), GameLauncher.PreloaderPath);

    var result = await instantStart.PrepareAsync(null, Instant, null, CancellationToken.None);

    Assert.Equal(new InstantStart.Result(directory, null), result);
    Assert.True(File.Exists(Path.Combine(directory, InstantStart.PluginPath)));
    // Apart from the user's instances, so it never shows in the library.
    Assert.Empty(new Instances.InstanceStore(_temp.AppPaths).LoadAll());
  }

  [Fact]
  public async Task Vanilla_starts_plainly_when_bepinex_cant_be_downloaded()
  {
    var result = await Create(mods: () => throw new HttpRequestException("offline")).PrepareAsync(null, Instant, null, CancellationToken.None);

    Assert.Null(result.InstanceDirectory);
    Assert.Equal("offline", result.Problem);
  }

  [Fact]
  public async Task A_missing_plugin_starts_the_instance_without_instant_start()
  {
    var result = await Create(plugin: _temp.Combine("nowhere", InstantStart.PluginFileName)).PrepareAsync(_instance, Instant, null, CancellationToken.None);

    Assert.Equal(_instance, result.InstanceDirectory);
    Assert.Contains("missing", result.Problem);
  }

  [Fact]
  public void Instant_start_is_one_argument_after_the_join()
  {
    Assert.Equal(["+connect", "10.0.0.1:2456", InstantStart.Argument], DirectPlay.Server("server:x", "X", "10.0.0.1:2456", "ragnar", null, instant: true).Arguments);
    Assert.Equal([InstantStart.Argument], Instant.Arguments);
    Assert.Empty(DirectPlay.World("world:midgard", "Midgard", "ragnar").Arguments);
  }
}
