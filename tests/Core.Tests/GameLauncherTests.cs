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
    var plan = GameLauncher.Plan(_game, _instance, "", NoEnvironment, GamePlatform.Linux);

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

    var plan = GameLauncher.Plan(_game, _instance, "", environment, GamePlatform.Linux);

    Assert.Equal("libdoorstop_x64.so:gameoverlayrenderer.so", plan.Environment["LD_PRELOAD"]);
    Assert.Equal($"{Path.Combine(_instance, "doorstop_libs")}:/usr/lib/steam", plan.Environment["LD_LIBRARY_PATH"]);
  }

  [Fact]
  public void Vanilla_launch_does_not_preload_doorstop()
  {
    var plan = GameLauncher.Plan(_game, null, "", NoEnvironment, GamePlatform.Linux);

    Assert.False(plan.Environment.ContainsKey("LD_PRELOAD"));
    Assert.Equal("TRUE", plan.Environment["DOORSTOP_DISABLE"]);
  }

  [Fact]
  public void The_launchers_qt_variables_are_removed_from_the_game_environment()
  {
    var plan = GameLauncher.Plan(_game, null, "", NoEnvironment, GamePlatform.Linux);
    var info = plan.ToStartInfo();

    Assert.All(GameLauncher.HostOnlyVariables, v => Assert.False(info.Environment.ContainsKey(v)));
  }

  [Fact]
  public void Refuses_to_launch_an_instance_without_bepinex()
  {
    var empty = _temp.Tree("empty");

    var error = Assert.Throws<LaunchException>(() => GameLauncher.Plan(_game, empty, "", NoEnvironment, GamePlatform.Linux));
    Assert.Contains("BepInEx", error.Message);
  }

  [Fact]
  public void Refuses_to_launch_without_the_game()
  {
    Assert.Throws<LaunchException>(() => GameLauncher.Plan(_temp.Tree("nogame"), null, "", NoEnvironment, GamePlatform.Linux));
  }

  [Fact]
  public void Windows_modded_launch_passes_the_instance_to_doorstop_on_the_command_line()
  {
    var (game, instance) = WindowsSetup();
    Directory.CreateDirectory(Path.Combine(instance, "unstripped_corlib"));
    GameLauncher.PrepareGameFolder(game, instance, GamePlatform.Windows);

    var plan = GameLauncher.Plan(game, instance, "-console", NoEnvironment, GamePlatform.Windows);

    Assert.Equal(Path.Combine(game, "valheim.exe"), plan.FileName);
    Assert.Equal(
      [
        "--doorstop-enabled", "true",
        "--doorstop-target-assembly", Path.GetFullPath(Path.Combine(instance, GameLauncher.PreloaderPath)),
        "--doorstop-mono-dll-search-path-override", Path.GetFullPath(Path.Combine(instance, "unstripped_corlib")),
        "-console",
      ],
      plan.Arguments);
    Assert.False(plan.Environment.ContainsKey("LD_PRELOAD"));
    Assert.Equal("892970", plan.Environment["SteamAppId"]);
  }

  [Fact]
  public void Windows_vanilla_launch_switches_doorstop_off_only_when_a_proxy_is_there()
  {
    var (game, instance) = WindowsSetup();

    Assert.Empty(GameLauncher.Plan(game, null, "", NoEnvironment, GamePlatform.Windows).Arguments);

    GameLauncher.PrepareGameFolder(game, instance, GamePlatform.Windows);
    Assert.Equal(["--doorstop-enabled", "false"], GameLauncher.Plan(game, null, "", NoEnvironment, GamePlatform.Windows).Arguments);
  }

  [Fact]
  public void Preparing_the_windows_game_folder_adds_the_proxy_with_doorstop_disabled()
  {
    var (game, instance) = WindowsSetup();

    GameLauncher.PrepareGameFolder(game, instance, GamePlatform.Windows);

    Assert.Equal("proxy from the instance", File.ReadAllText(Path.Combine(game, "winhttp.dll")));
    var config = File.ReadAllLines(Path.Combine(game, "doorstop_config.ini"));
    Assert.Equal(GameLauncher.WindowsConfigMarker, config[0]);
    Assert.Contains("enabled = false", config);
  }

  [Fact]
  public void Preparing_the_windows_game_folder_refreshes_launchheims_own_proxy()
  {
    var (game, instance) = WindowsSetup();
    GameLauncher.PrepareGameFolder(game, instance, GamePlatform.Windows);
    File.WriteAllText(Path.Combine(instance, "winhttp.dll"), "newer proxy");

    GameLauncher.PrepareGameFolder(game, instance, GamePlatform.Windows);

    Assert.Equal("newer proxy", File.ReadAllText(Path.Combine(game, "winhttp.dll")));
  }

  [Fact]
  public void Preparing_the_windows_game_folder_leaves_a_manual_bepinex_install_alone()
  {
    var (game, instance) = WindowsSetup();
    File.WriteAllText(Path.Combine(game, "winhttp.dll"), "manual proxy");
    File.WriteAllText(Path.Combine(game, "doorstop_config.ini"), "[General]\nenabled = true\n");

    GameLauncher.PrepareGameFolder(game, instance, GamePlatform.Windows);

    Assert.Equal("manual proxy", File.ReadAllText(Path.Combine(game, "winhttp.dll")));
    Assert.Equal("[General]\nenabled = true\n", File.ReadAllText(Path.Combine(game, "doorstop_config.ini")));
  }

  [Fact]
  public void Preparing_the_game_folder_does_nothing_on_linux()
  {
    var (game, instance) = WindowsSetup();

    GameLauncher.PrepareGameFolder(game, instance, GamePlatform.Linux);

    Assert.False(File.Exists(Path.Combine(game, "winhttp.dll")));
  }

  [Fact]
  public void Windows_refuses_an_instance_without_bepinex()
  {
    var (game, _) = WindowsSetup();
    var empty = _temp.Tree("empty-windows");

    Assert.Throws<LaunchException>(() => GameLauncher.PrepareGameFolder(game, empty, GamePlatform.Windows));
    Assert.Throws<LaunchException>(() => GameLauncher.Plan(game, empty, "", NoEnvironment, GamePlatform.Windows));
  }

  [Fact]
  public void Linux_launches_load_steams_overlay_like_steam_does()
  {
    var steam = _temp.Tree("Steam", "ubuntu12_32/steam", "ubuntu12_64/gameoverlayrenderer.so");
    var overlay = Path.Combine(steam, "ubuntu12_64", "gameoverlayrenderer.so");

    var vanilla = GameLauncher.Plan(_game, null, "", NoEnvironment, GamePlatform.Linux, steamDirectory: steam);
    var modded = GameLauncher.Plan(_game, _instance, "", NoEnvironment, GamePlatform.Linux, steamDirectory: steam);

    Assert.Equal(overlay, vanilla.Environment["LD_PRELOAD"]);
    Assert.Equal("1", vanilla.Environment["ENABLE_VK_LAYER_VALVE_steam_overlay_1"]);
    Assert.Equal("892970", vanilla.Environment["SteamOverlayGameId"]);
    // Doorstop first, as with BepInEx's own start script in Steam's launch options.
    Assert.Equal($"libdoorstop_x64.so:{overlay}", modded.Environment["LD_PRELOAD"]);
    Assert.Equal(Path.Combine(_game, "valheim.x86_64"), modded.FileName);
    Assert.Null(modded.SteamStartsGame);
  }

  [Fact]
  public void Steams_overlay_is_not_preloaded_twice_or_without_the_library()
  {
    var steam = _temp.Tree("Steam", "ubuntu12_32/steam", "ubuntu12_64/gameoverlayrenderer.so");
    var fromSteam = new Dictionary<string, string?> { ["LD_PRELOAD"] = "/steam/ubuntu12_64/gameoverlayrenderer.so" };

    var inherited = GameLauncher.Plan(_game, null, "", fromSteam, GamePlatform.Linux, steamDirectory: steam);
    var missing = GameLauncher.Plan(_game, null, "", NoEnvironment, GamePlatform.Linux, steamDirectory: _temp.Tree("NoOverlay"));

    Assert.False(inherited.Environment.ContainsKey("LD_PRELOAD"));
    Assert.False(missing.Environment.ContainsKey("LD_PRELOAD"));
    Assert.False(missing.Environment.ContainsKey("ENABLE_VK_LAYER_VALVE_steam_overlay_1"));
  }

  [Fact]
  public void Windows_launches_go_through_steam_with_the_same_arguments()
  {
    var (game, instance) = WindowsSetup();
    var steam = _temp.Tree("SteamWindows", "steam.exe");
    GameLauncher.PrepareGameFolder(game, instance, GamePlatform.Windows);
    var join = GameLauncher.JoinArguments("10.0.0.1:2456", null);

    var direct = GameLauncher.Plan(game, instance, "-console", NoEnvironment, GamePlatform.Windows, join);
    var plan = GameLauncher.Plan(game, instance, "-console", NoEnvironment, GamePlatform.Windows, join, steam);

    Assert.Equal(Path.Combine(steam, "steam.exe"), plan.FileName);
    Assert.Equal(["-applaunch", "892970", .. direct.Arguments], plan.Arguments);
    Assert.Equal(Path.Combine(game, "valheim.exe"), plan.SteamStartsGame);
    Assert.Null(direct.SteamStartsGame);
  }

  [Fact]
  public void Windows_starts_the_game_itself_when_steam_exe_is_missing()
  {
    var (game, _) = WindowsSetup();

    var plan = GameLauncher.Plan(game, null, "", NoEnvironment, GamePlatform.Windows, steamDirectory: _temp.Tree("NoSteam"));

    Assert.Equal(Path.Combine(game, "valheim.exe"), plan.FileName);
    Assert.Null(plan.SteamStartsGame);
  }

  [Fact]
  public void The_steam_client_folder_is_the_first_root_with_the_client_in_it()
  {
    var library = _temp.Tree("Library", "steamapps/libraryfolders.vdf");
    var linux = _temp.Tree("LinuxSteam", "ubuntu12_32/steam");
    var windows = _temp.Tree("WindowsSteam", "steam.exe");

    Assert.Equal(linux, new SteamLibraryLocator([library, linux, windows], GamePlatform.Linux).ClientDirectory());
    Assert.Equal(windows, new SteamLibraryLocator([library, linux, windows], GamePlatform.Windows).ClientDirectory());
    Assert.Null(new SteamLibraryLocator([library], GamePlatform.Linux).ClientDirectory());
  }

  [Fact]
  public void Joining_a_server_comes_after_the_launch_arguments()
  {
    var join = GameLauncher.JoinArguments("10.0.0.1:2456", "hunter22");

    var linux = GameLauncher.Plan(_game, _instance, "-console", NoEnvironment, GamePlatform.Linux, join);
    var (game, instance) = WindowsSetup();
    GameLauncher.PrepareGameFolder(game, instance, GamePlatform.Windows);
    var windows = GameLauncher.Plan(game, instance, "-console", NoEnvironment, GamePlatform.Windows, join);

    Assert.Equal(["-console", "+connect", "10.0.0.1:2456", "-password", "hunter22"], linux.Arguments);
    Assert.Equal(["-console", "+connect", "10.0.0.1:2456", "-password", "hunter22"], windows.Arguments.TakeLast(5));
  }

  [Fact]
  public void A_server_without_a_password_gets_no_password_argument() =>
    Assert.Equal(["+connect", "10.0.0.1:2456"], GameLauncher.JoinArguments("10.0.0.1:2456", ""));

  [Fact]
  public void Passwords_are_hidden_from_the_log() =>
    Assert.Equal(
      ["-console", "+connect", "10.0.0.1:2456", "-password", "***"],
      GameLauncher.Redact(["-console", "+connect", "10.0.0.1:2456", "-password", "hunter22"]));

  private (string Game, string Instance) WindowsSetup()
  {
    var game = _temp.Tree("ValheimWindows", "valheim.exe");
    var instance = _temp.Tree("windows-instance", GameLauncher.PreloaderPath);
    File.WriteAllText(Path.Combine(instance, "winhttp.dll"), "proxy from the instance");
    return (game, instance);
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
