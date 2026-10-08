using System.ComponentModel;
using System.Diagnostics;

namespace CINE.LaunchHeim.Core.Game;

/// <summary>Finds and follows a game process LaunchHeim didn't start itself: one Steam started for it.</summary>
/// <remarks>
/// <para>
/// With <c>steam.exe -applaunch</c> (Windows, see <see cref="GameLauncher"/>) the process LaunchHeim
/// starts is Steam's, which hands the launch to the running client and exits. The game appears a moment
/// later, or much later when Steam first syncs the cloud, updates the game or asks something. It is
/// found by name: a Valheim already running is refused before the launch, so the next one to appear is
/// the one Steam started.
/// </para>
/// <para>
/// A game started by a Steam running as administrator runs elevated too, and Windows may then refuse
/// LaunchHeim a handle to wait on. Its exit is then noticed by polling for its id.
/// </para>
/// </remarks>
public static class GameProcess
{
  /// <param name="executable">The game's file, such as valheim.exe; the process name is its name without the extension.</param>
  public static bool IsRunning(string executable)
  {
    var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
    foreach (var process in processes)
    {
      process.Dispose();
    }

    return processes.Length > 0;
  }

  /// <summary>The game's process once it is running, or null when it didn't appear in <paramref name="timeout"/>.</summary>
  public static async Task<Process?> WaitForStartAsync(string executable, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)
  {
    var name = Path.GetFileNameWithoutExtension(executable);
    var deadline = Stopwatch.StartNew();
    while (deadline.Elapsed < timeout)
    {
      var processes = Process.GetProcessesByName(name);
      // The oldest, should Valheim ever start a second process of the same name.
      var found = processes.OrderBy(p => p.Id).FirstOrDefault();
      foreach (var process in processes.Where(p => p != found))
      {
        process.Dispose();
      }

      if (found is not null)
      {
        return found;
      }

      await Task.Delay(pollInterval, cancellationToken);
    }

    return null;
  }

  /// <summary>Returns once the process has exited, with its exit code when Windows tells it.</summary>
  public static async Task<int?> WaitForExitAsync(Process process, CancellationToken cancellationToken)
  {
    try
    {
      await process.WaitForExitAsync(cancellationToken);
      return process.ExitCode;
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
    {
      // No handle with the access waiting needs (an elevated game). Watching its id works without one.
    }

    var id = process.Id;
    while (true)
    {
      try
      {
        using var current = Process.GetProcessById(id);
        if (current.HasExited)
        {
          return null;
        }
      }
      catch (ArgumentException)
      {
        // No process with this id any more.
        return null;
      }
      catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
      {
        // Still there, but HasExited needs the access that was refused above.
      }

      await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }
  }
}
