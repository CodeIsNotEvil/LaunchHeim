using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Tests;

public class SteamClientTests
{
  /// <summary>A fake Steam that goes through the given states, one per check, and stays in the last.</summary>
  private sealed class FakeSteam(params SteamState[] states)
  {
    private int _checks;

    public int Starts { get; private set; }

    public bool CanStart { get; init; } = true;

    public SteamClient Client(TimeSpan? timeout = null) => new(
      () => states[Math.Min(_checks++, states.Length - 1)],
      () =>
      {
        Starts++;
        return CanStart;
      })
    {
      Timeout = timeout ?? TimeSpan.FromSeconds(5),
      PollInterval = TimeSpan.FromMilliseconds(1),
      SettleDelay = TimeSpan.Zero,
    };
  }

  [Fact]
  public async Task A_logged_in_client_is_used_as_it_is()
  {
    var steam = new FakeSteam(SteamState.Ready);
    var seen = new List<SteamState>();

    await steam.Client().EnsureReadyAsync(seen.Add, CancellationToken.None);

    Assert.Equal(0, steam.Starts);
    Assert.Empty(seen);
  }

  [Fact]
  public async Task Steam_is_started_when_it_is_not_running_and_waited_for()
  {
    var steam = new FakeSteam(SteamState.NotRunning, SteamState.NotRunning, SteamState.Starting, SteamState.Ready);
    var seen = new List<SteamState>();
    var client = steam.Client();

    await client.EnsureReadyAsync(seen.Add, CancellationToken.None);

    Assert.Equal(1, steam.Starts);
    Assert.Equal([SteamState.NotRunning, SteamState.Starting], seen);
  }

  [Fact]
  public async Task A_client_that_is_still_starting_is_waited_for_but_not_started_again()
  {
    var steam = new FakeSteam(SteamState.Starting, SteamState.Starting, SteamState.Ready);

    await steam.Client().EnsureReadyAsync(null, CancellationToken.None);

    Assert.Equal(0, steam.Starts);
  }

  [Fact]
  public async Task A_steam_that_cannot_be_started_is_reported()
  {
    var steam = new FakeSteam(SteamState.NotRunning) { CanStart = false };

    var error = await Assert.ThrowsAsync<LaunchException>(() => steam.Client().EnsureReadyAsync(null, CancellationToken.None));

    Assert.Contains("could not start", error.Message);
  }

  [Fact]
  public async Task Waiting_for_a_login_gives_up_after_the_timeout()
  {
    var steam = new FakeSteam(SteamState.NotRunning, SteamState.Starting);

    var error = await Assert.ThrowsAsync<LaunchException>(
      () => steam.Client(TimeSpan.FromMilliseconds(50)).EnsureReadyAsync(null, CancellationToken.None));

    Assert.Contains("not logged in", error.Message);
  }

  // Trimmed from a real connection_log.txt (2026-09-30): one exit, then a new start and login.
  private const string Log = """
    [2026-09-30 20:22:33] [Logged On, 4, 7] [U:1:105751808] RecvMsgClientLogOnResponse() : processing complete
    [2026-09-30 20:23:41] [Logging Off, 4, 7] [U:1:105751808] LogOff()
    [2026-09-30 20:23:41] [Logged Off, 0, 0] [U:1:105751808] ~CCMInterface()
    [2026-09-30 20:25:00] Client version: 1788652215
    [2026-09-30 20:25:00] [Connected, 4, 7] [U:1:105751808] Logging on [U:1:105751808]
    [2026-09-30 20:25:00] [Logged On, 4, 7] [U:1:105751808] RecvMsgClientLogOnResponse() : processing complete
    [2026-09-30 20:25:00] CClientJobGetClientUpdateHosts: cached version not expired

    """;

  [Fact]
  public void A_login_after_steam_started_counts()
  {
    Assert.True(SteamClient.IsLoggedOn(Log, new DateTime(2026, 9, 30, 20, 24, 58)));
  }

  [Fact]
  public void A_login_older_than_the_running_steam_is_left_over_from_a_crash()
  {
    Assert.False(SteamClient.IsLoggedOn(Log, new DateTime(2026, 9, 30, 20, 30, 0)));
  }

  [Fact]
  public void A_log_that_ends_logged_off_means_not_logged_in()
  {
    var log = Log[..Log.IndexOf("[2026-09-30 20:25:00] Client", StringComparison.Ordinal)];

    Assert.False(SteamClient.IsLoggedOn(log, new DateTime(2026, 9, 30, 20, 0, 0)));
  }

  [Fact]
  public void A_steam_still_connecting_is_not_logged_in()
  {
    var log = Log[..Log.LastIndexOf("[2026-09-30 20:25:00] [Logged On", StringComparison.Ordinal)];

    Assert.False(SteamClient.IsLoggedOn(log, new DateTime(2026, 9, 30, 20, 24, 58)));
    Assert.False(SteamClient.IsLoggedOn("", DateTime.MinValue));
  }
}
