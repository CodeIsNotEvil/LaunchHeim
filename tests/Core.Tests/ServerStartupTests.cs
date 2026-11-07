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

public class ServerHeartbeatTests
{
  private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void A_recent_beat_means_up_with_its_players()
  {
    var beat = ServerHeartbeat.Parse("""{"time":"2026-11-07T11:59:40.123Z","players":3,"world":"Midgard","version":"l-1.0.21","stopped":false}""");

    Assert.NotNull(beat);
    Assert.True(beat.IsAlive(Now));
    Assert.Equal((3, "Midgard", "l-1.0.21"), (beat.Players, beat.World, beat.Version));
  }

  [Fact]
  public void An_old_or_last_beat_means_down()
  {
    Assert.False(ServerHeartbeat.Parse("""{"time":"2026-11-07T11:55:00Z","players":0}""")!.IsAlive(Now));
    Assert.False(ServerHeartbeat.Parse("""{"time":"2026-11-07T11:59:59Z","players":0,"stopped":true}""")!.IsAlive(Now));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("{\"players\":1}")]
  [InlineData("not json")]
  public void Anything_else_is_no_beat(string? json)
  {
    Assert.Null(ServerHeartbeat.Parse(json));
  }
}
