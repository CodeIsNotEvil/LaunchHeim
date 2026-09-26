using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Tests;

public class GameLauncherTests : IDisposable
{
  private readonly TempDirectory _temp = new();
  private readonly string _game;
  private readonly string _instance;

  public GameLauncherTests()
  {
    _game = _temp.Tree("Valheim", "valheim.x86_64");
    _instance = _temp.Tree("instance", GameLauncher.PreloaderPath, GameLauncher.DoorstopLibrary);
  }

  public void Dispose() => _temp.Dispose();

  private static readonly IReadOnlyDictionary<string, string?> NoEnvironment = new Dictionary<string, string?>();

  [Fact]
  public void Modded_launch_points_doorstop_at_the_instance_preloader()
  {
    var plan = GameLauncher.Plan(_game, _instance, "", NoEnvironment);

    Assert.Equal(Path.Combine(_game, "valheim.x86_64"), plan.FileName);
    Assert.Equal(_game, plan.WorkingDirectory);
    Assert.Equal("1", plan.Environment["DOORSTOP_ENABLED"]);
    Assert.Equal(Path.Combine(_instance, GameLauncher.PreloaderPath), plan.Environment["DOORSTOP_TARGET_ASSEMBLY"]);
    Assert.Equal(Path.Combine(_instance, GameLauncher.PreloaderPath), plan.Environment["DOORSTOP_INVOKE_DLL_PATH"]);
    Assert.Equal("libdoorstop_x64.so", plan.Environment["LD_PRELOAD"]);
    Assert.Equal(Path.Combine(_instance, "doorstop_libs"), plan.Environment["LD_LIBRARY_PATH"]);
    Assert.Equal("892970", plan.Environment["SteamAppId"]);
  }

  [Fact]
  public void Existing_preloads_and_library_paths_are_kept_after_doorstop()
  {
    var environment = new Dictionary<string, string?> { ["LD_PRELOAD"] = "gameoverlayrenderer.so", ["LD_LIBRARY_PATH"] = "/usr/lib/steam" };

    var plan = GameLauncher.Plan(_game, _instance, "", environment);

    Assert.Equal("libdoorstop_x64.so:gameoverlayrenderer.so", plan.Environment["LD_PRELOAD"]);
    Assert.Equal($"{Path.Combine(_instance, "doorstop_libs")}:/usr/lib/steam", plan.Environment["LD_LIBRARY_PATH"]);
  }

  [Fact]
  public void Vanilla_launch_does_not_preload_doorstop()
  {
    var plan = GameLauncher.Plan(_game, null, "", NoEnvironment);

    Assert.False(plan.Environment.ContainsKey("LD_PRELOAD"));
    Assert.Equal("TRUE", plan.Environment["DOORSTOP_DISABLE"]);
  }

  [Fact]
  public void The_launchers_qt_variables_are_removed_from_the_game_environment()
  {
    var plan = GameLauncher.Plan(_game, null, "", NoEnvironment);
    var info = plan.ToStartInfo();

    Assert.All(GameLauncher.HostOnlyVariables, v => Assert.False(info.Environment.ContainsKey(v)));
  }

  [Fact]
  public void Refuses_to_launch_an_instance_without_bepinex()
  {
    var empty = _temp.Tree("empty");

    var error = Assert.Throws<LaunchException>(() => GameLauncher.Plan(_game, empty, "", NoEnvironment));
    Assert.Contains("BepInEx", error.Message);
  }

  [Fact]
  public void Refuses_to_launch_without_the_game()
  {
    Assert.Throws<LaunchException>(() => GameLauncher.Plan(_temp.Tree("nogame"), null, "", NoEnvironment));
  }

  [Theory]
  [InlineData("", new string[0])]
  [InlineData("-console", new[] { "-console" })]
  [InlineData("-console  -windowed", new[] { "-console", "-windowed" })]
  [InlineData("+connect \"my server:2456\" -x", new[] { "+connect", "my server:2456", "-x" })]
  [InlineData("-name ''", new[] { "-name", "" })]
  public void Splits_arguments_like_a_shell(string input, string[] expected)
  {
    Assert.Equal(expected, GameLauncher.SplitArguments(input));
  }
}
