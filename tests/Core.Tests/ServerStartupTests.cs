using CINE.LaunchHeim.Core.Servers;

namespace CINE.LaunchHeim.Core.Tests;

public class ServerStartupTests : IDisposable
{
  private readonly TempDirectory _temp = new();

  public void Dispose() => _temp.Dispose();

  // Lines from a real start of Valheim 1.0.21 with BepInEx 5.4.23 (2026-11-06).
  private static readonly string[] Preloader = ["[Message:   BepInEx] Preloader started", "[Message:   BepInEx] Preloader finished"];
  private const string CreateWorld = "11/06/2026 23:50:11: Get create world E2ETest";
  private const string Missing = "11/06/2026 23:50:14:   missing /srv/saves/worlds_local/E2ETest.db";
  private const string LoadWorld = "11/06/2026 23:50:14: Load world: E2ETest (E2ETest)";
  private const string Connected = "11/06/2026 23:50:18: Game server connected";

  [Fact]
  public void Phases_follow_the_log()
  {
    Assert.Equal(ServerStartPhase.Starting, ServerStartup.Detect(["Unity 6000.0.75f1", "Loading player data"]));
    Assert.Equal(ServerStartPhase.LoadingMods, ServerStartup.Detect(Preloader));
    Assert.Equal(ServerStartPhase.LoadingWorld, ServerStartup.Detect([.. Preloader, CreateWorld, LoadWorld]));
    Assert.Equal(ServerStartPhase.Online, ServerStartup.Detect([.. Preloader, CreateWorld, LoadWorld, Connected]));
  }

  [Fact]
  public void A_new_world_is_being_created_until_the_server_is_up()
  {
    Assert.Equal(ServerStartPhase.CreatingWorld, ServerStartup.Detect([CreateWorld, LoadWorld, Missing]));
    Assert.Equal(ServerStartPhase.CreatingWorld, ServerStartup.Detect([CreateWorld, Missing, LoadWorld]));
  }

  [Fact]
  public void Steps_climb_to_online()
  {
    Assert.Equal(
      [1, 2, 3, 3, ServerStartup.Steps],
      new[] { ServerStartPhase.Starting, ServerStartPhase.LoadingMods, ServerStartPhase.LoadingWorld, ServerStartPhase.CreatingWorld, ServerStartPhase.Online }.Select(ServerStartup.Step));
  }

  [Fact]
  public void The_log_file_is_read_and_a_missing_one_means_starting()
  {
    var log = _temp.Combine("server.log");
    Assert.Equal(ServerStartPhase.Starting, ServerStartup.Detect(log));

    File.WriteAllLines(log, [.. Preloader, CreateWorld, Connected]);
    Assert.Equal(ServerStartPhase.Online, ServerStartup.Detect(log));
  }
}
