using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>What LaunchHeim remembers about a server it started: <c>run.json</c> in the server's folder.</summary>
/// <param name="StartedAt">The process's own start time, so a recycled process id isn't taken for the server.</param>
public sealed record ServerRun(int ProcessId, DateTimeOffset StartedAt, string Executable);

/// <summary>Starts, finds and stops the process of a local dedicated server.</summary>
/// <remarks>
/// <para>
/// The server runs on its own, never as part of LaunchHeim: closing LaunchHeim, or the terminal it was
/// started from, must not take the server and its players down. On Linux it is started in a session of
/// its own (<c>setsid</c>) with its output dropped, because Valheim logs to the file given with
/// <c>-logFile</c>, which the console follows (the same reasoning as for the game, <see cref="LogFollower"/>).
/// <c>run.json</c> lets a LaunchHeim started later find the process again.
/// </para>
/// <para>
/// Stopping saves the world first. The plugin's <c>stop</c> command does that on every platform and is
/// tried by the caller first (<see cref="ServerManager"/>). Without the plugin: on Linux SIGINT, which
/// Valheim treats as Ctrl+C, quitting through <c>Game.OnApplicationQuit</c> and saving; on Windows a
/// Ctrl+C sent to the server's console. Only a server that ignores all of that is killed, which loses
/// what happened since the last save.
/// </para>
/// </remarks>
public static class ServerProcess
{
  public const string RunFile = "run.json";

  /// <summary>How long a server gets to save and quit after being asked to.</summary>
  public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(60);

  public static Process Start(LaunchPlan plan, string serverFolder)
  {
    var info = plan.ToStartInfo();
    info.CreateNoWindow = true;
    if (!OperatingSystem.IsWindows() && File.Exists("/usr/bin/setsid"))
    {
      // setsid execs the shell, and the shell execs the server, so the process id stays the server's.
      var target = info.FileName;
      var arguments = info.ArgumentList.ToList();
      info.FileName = "/usr/bin/setsid";
      info.ArgumentList.Clear();
      foreach (var argument in (string[])["/bin/sh", "-c", "exec \"$0\" \"$@\" </dev/null >/dev/null 2>&1", target, .. arguments])
      {
        info.ArgumentList.Add(argument);
      }
    }

    var process = Process.Start(info) ?? throw new LaunchException("The server did not start.");
    var executable = plan.FileName;
    DateTimeOffset started;
    try
    {
      started = process.StartTime;
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
    {
      started = DateTimeOffset.Now;
    }

    JsonFile.Write(Path.Combine(serverFolder, RunFile), new ServerRun(process.Id, started, executable));
    return process;
  }

  /// <summary>The server's process when it still runs, found through <c>run.json</c>; null otherwise.</summary>
  public static Process? Find(string serverFolder)
  {
    var file = Path.Combine(serverFolder, RunFile);
    ServerRun? run;
    try
    {
      run = JsonFile.Read<ServerRun>(file);
    }
    catch (System.Text.Json.JsonException)
    {
      run = null;
    }

    if (run is null)
    {
      return null;
    }

    try
    {
      var process = Process.GetProcessById(run.ProcessId);
      if (!process.HasExited && IsSameProcess(process, run))
      {
        return process;
      }

      process.Dispose();
    }
    catch (ArgumentException)
    {
      // Gone.
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
    {
      // Not ours to look at, so not ours.
    }

    TryDelete(file);
    return null;
  }

  /// <summary>Asks the process to save and quit without the plugin. False when that isn't possible here.</summary>
  public static bool RequestStop(Process process)
  {
    try
    {
      if (OperatingSystem.IsWindows())
      {
        return SendCtrlC(process.Id);
      }

      return Kill(process.Id, SigInt) == 0;
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
    {
      Log.Warning($"Could not ask the server to stop: {ex.Message}");
      return false;
    }
  }

  public static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
  {
    using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    limit.CancelAfter(timeout);
    try
    {
      await process.WaitForExitAsync(limit.Token);
      return true;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      return false;
    }
  }

  public static void Forget(string serverFolder) => TryDelete(Path.Combine(serverFolder, RunFile));

  private static bool IsSameProcess(Process process, ServerRun run)
  {
    try
    {
      // Start times are recorded to the millisecond on Linux and to 100 ns on Windows; seconds are enough
      // to tell a recycled id from the server.
      return Math.Abs((process.StartTime - run.StartedAt).TotalSeconds) < 2;
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
    {
      // An elevated or foreign process refuses its start time; trust the name instead.
      return Path.GetFileNameWithoutExtension(run.Executable).Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase);
    }
  }

  private static void TryDelete(string file)
  {
    try
    {
      File.Delete(file);
    }
    catch (IOException)
    {
    }
  }

  private const int SigInt = 2;

  [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
  private static extern int Kill(int pid, int signal);

  /// <summary>
  /// Ctrl+C for a console program on Windows: attach to its console, raise the event there, detach. While
  /// attached LaunchHeim ignores Ctrl+C itself, or it would be the one closing. A GUI process like
  /// LaunchHeim has no console of its own, so attaching to the server's is allowed.
  /// </summary>
  private static bool SendCtrlC(int processId)
  {
    if (!AttachConsole((uint)processId))
    {
      return false;
    }

    try
    {
      SetConsoleCtrlHandler(IntPtr.Zero, true);
      return GenerateConsoleCtrlEvent(CtrlCEvent, 0);
    }
    finally
    {
      FreeConsole();
      // The event is delivered asynchronously; ignoring it a moment longer keeps it from reaching us.
      Thread.Sleep(200);
      SetConsoleCtrlHandler(IntPtr.Zero, false);
    }
  }

  private const uint CtrlCEvent = 0;

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool AttachConsole(uint processId);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool FreeConsole();

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
}
