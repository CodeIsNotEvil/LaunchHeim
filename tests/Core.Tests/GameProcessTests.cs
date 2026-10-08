using System.Diagnostics;
using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Tests;

public class GameProcessTests
{
  // The test host stands in for a game Steam started: a process LaunchHeim finds by name.
  private static string ThisExecutable => Process.GetCurrentProcess().ProcessName + ".exe";

  [Fact]
  public async Task A_running_game_is_found_by_its_executables_name()
  {
    Assert.True(GameProcess.IsRunning(ThisExecutable));

    using var found = await GameProcess.WaitForStartAsync(ThisExecutable, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), CancellationToken.None);

    Assert.NotNull(found);
  }

  [Fact]
  public async Task A_game_that_never_starts_gives_up_after_the_timeout()
  {
    var name = "launchheim-no-such-game-" + Guid.NewGuid().ToString("N") + ".exe";

    Assert.False(GameProcess.IsRunning(name));
    Assert.Null(await GameProcess.WaitForStartAsync(name, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(50), CancellationToken.None));
  }

  [Fact]
  public async Task Waiting_for_the_exit_returns_the_exit_code()
  {
    using var process = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true })!;

    Assert.Equal(0, await GameProcess.WaitForExitAsync(process, CancellationToken.None));
  }
}
