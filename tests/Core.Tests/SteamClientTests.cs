using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Tests;

public class SteamClientTests
{
  /// <summary>A fake Steam that goes through the given states, one per check, and stays in the last.</summary>
  private sealed class FakeSteam(params SteamState[] states)
  {
    private int _checks;
    private SteamState _current;

    public int Starts { get; private set; }

    public bool CanStart { get; init; } = true;

    public SteamClient Client(TimeSpan? timeout = null) => new(
      () =>
      {
        // SteamClient asks for the process first, then for the user, so one check is one state.
        _current = states[Math.Min(_checks++, states.Length - 1)];
        return _current != SteamState.NotRunning;
      },
      () => _current == SteamState.Ready ? 12345 : 0,
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

  [Fact]
  public void The_active_user_is_read_from_registry_vdf()
  {
    const string vdf = """
      "Registry"
      {
        "HKCU"
        {
          "Software"
          {
            "Valve"
            {
              "Steam"
              {
                "AutoLoginUser"		"someone"
                "ActiveProcess"
                {
                  "pid"		"4242"
                  "SteamClientDll"		"/home/someone/.local/share/Steam/ubuntu12_32/steamclient.so"
                  "ActiveUser"		"123456789"
                }
              }
            }
          }
        }
      }
      """;

    Assert.Equal(123456789, SteamClient.ActiveUserFromRegistry(vdf));
  }

  [Fact]
  public void A_registry_without_an_active_process_means_nobody_is_logged_in()
  {
    const string vdf = """
      "Registry"
      {
        "HKCU" { "Software" { "Valve" { "Steam" { "AutoLoginUser" "someone" } } } }
      }
      """;

    Assert.Equal(0, SteamClient.ActiveUserFromRegistry(vdf));
  }
}
