using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Servers;

namespace CINE.LaunchHeim.Core.Tests;

public class ServerLauncherTests : IDisposable
{
  private static readonly IReadOnlyDictionary<string, string?> NoEnvironment = new Dictionary<string, string?>();

  private readonly TempDirectory _temp = new();
  private readonly string _linuxServer;
  private readonly string _windowsServer;
  private readonly string _mods;
  private readonly ServerProfile _server = new() { Name = "Walheim", World = "Midgard", Port = 2456, Password = "secret1" };

  public ServerLauncherTests()
  {
    _linuxServer = _temp.Tree("server-linux", "valheim_server.x86_64");
    _windowsServer = _temp.Tree("server-windows", "valheim_server.exe", GameLauncher.WindowsDoorstopProxy);
    _mods = _temp.Tree("mods", GameLauncher.PreloaderPath, GameLauncher.DoorstopLibrary, GameLauncher.WindowsDoorstopProxy);
  }

  public void Dispose() => _temp.Dispose();

  private string Saves => _temp.Combine("saves");

  private string LogFile => _temp.Combine("server.log");

  [Fact]
  public void Arguments_carry_the_profile_and_always_say_whether_the_server_is_public()
  {
    var arguments = ServerLauncher.Arguments(_server, Saves, LogFile);

    Assert.Equal(
      ["-nographics", "-batchmode", "-name", "Walheim", "-port", "2456", "-world", "Midgard", "-public", "0",
       "-savedir", Path.GetFullPath(Saves), "-logFile", Path.GetFullPath(LogFile), "-saveinterval", "1800", "-backups", "4",
       "-password", "secret1"],
      arguments);
  }

  [Fact]
  public void Crossplay_preset_and_extra_arguments_are_added()
  {
    var server = new ServerProfile { Name = "S", World = "W", Public = true, Password = "hunter2", Crossplay = true, Preset = "Hard", ExtraArguments = "-modifier raids none" };

    var arguments = ServerLauncher.Arguments(server, Saves, LogFile);

    Assert.Contains("-crossplay", arguments);
    Assert.Equal("hard", arguments[arguments.ToList().IndexOf("-preset") + 1]);
    Assert.Equal(["-modifier", "raids", "none"], arguments.TakeLast(3));
    Assert.Equal("1", arguments[arguments.ToList().IndexOf("-public") + 1]);
  }

  [Fact]
  public void An_empty_password_is_left_out()
  {
    var server = new ServerProfile { Name = "S", World = "W" };

    Assert.DoesNotContain("-password", ServerLauncher.Arguments(server, Saves, LogFile));
  }

  [Fact]
  public void Linux_vanilla_server_gets_its_libraries_and_no_doorstop()
  {
    var environment = new Dictionary<string, string?> { ["LD_LIBRARY_PATH"] = "/usr/lib" };

    var plan = ServerLauncher.Plan(_server, _linuxServer, null, Saves, LogFile, environment, GamePlatform.Linux);

    Assert.Equal(Path.Combine(_linuxServer, "valheim_server.x86_64"), plan.FileName);
    Assert.Equal(_linuxServer, plan.WorkingDirectory);
    Assert.Equal("892970", plan.Environment["SteamAppId"]);
    Assert.Equal($"{Path.Combine(_linuxServer, "linux64")}:/usr/lib", plan.Environment["LD_LIBRARY_PATH"]);
    Assert.Equal("TRUE", plan.Environment["DOORSTOP_DISABLE"]);
    Assert.False(plan.Environment.ContainsKey("LD_PRELOAD"));
  }

  [Fact]
  public void Linux_modded_server_preloads_doorstop_from_the_servers_folder()
  {
    var plan = ServerLauncher.Plan(_server, _linuxServer, _mods, Saves, LogFile, NoEnvironment, GamePlatform.Linux);

    Assert.Equal("libdoorstop_x64.so", plan.Environment["LD_PRELOAD"]);
    Assert.Equal(Path.Combine(_mods, GameLauncher.PreloaderPath), plan.Environment["DOORSTOP_TARGET_ASSEMBLY"]);
    // Doorstop's folder first, then the server's own libraries.
    Assert.Equal($"{Path.Combine(_mods, "doorstop_libs")}:{Path.Combine(_linuxServer, "linux64")}", plan.Environment["LD_LIBRARY_PATH"]);
  }

  [Fact]
  public void Windows_modded_server_switches_doorstop_on_with_arguments()
  {
    var plan = ServerLauncher.Plan(_server, _windowsServer, _mods, Saves, LogFile, NoEnvironment, GamePlatform.Windows);

    Assert.Equal(Path.Combine(_windowsServer, "valheim_server.exe"), plan.FileName);
    Assert.Equal(
      [GameLauncher.DoorstopEnabledArgument, "true", "--doorstop-target-assembly", Path.GetFullPath(Path.Combine(_mods, GameLauncher.PreloaderPath))],
      plan.Arguments.Take(4));
    Assert.Contains("-nographics", plan.Arguments);
    Assert.False(plan.Environment.ContainsKey("LD_LIBRARY_PATH"));
  }

  [Fact]
  public void Windows_vanilla_server_keeps_a_leftover_proxy_off()
  {
    var plan = ServerLauncher.Plan(_server, _windowsServer, null, Saves, LogFile, NoEnvironment, GamePlatform.Windows);

    Assert.Equal([GameLauncher.DoorstopEnabledArgument, "false"], plan.Arguments.Take(2));
  }

  [Fact]
  public void A_missing_server_or_bepinex_is_reported()
  {
    Assert.Throws<LaunchException>(() => ServerLauncher.Plan(_server, _temp.Combine("nowhere"), null, Saves, LogFile, NoEnvironment, GamePlatform.Linux));
    var empty = _temp.Tree("empty-mods");
    Assert.Throws<LaunchException>(() => ServerLauncher.Plan(_server, _linuxServer, empty, Saves, LogFile, NoEnvironment, GamePlatform.Linux));
  }

  [Fact]
  public void The_password_is_hidden_in_logged_arguments()
  {
    var arguments = ServerLauncher.Arguments(_server, Saves, LogFile);

    Assert.DoesNotContain("secret1", GameLauncher.Redact(arguments));
  }
}
