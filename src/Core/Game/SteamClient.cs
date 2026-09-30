using System.Diagnostics;

namespace CINE.LaunchHeim.Core.Game;

/// <summary>How far the Steam client is from being usable by the game.</summary>
public enum SteamState
{
  NotRunning,

  /// <summary>The client runs but nobody is logged in yet (starting up, updating, or at the login window).</summary>
  Starting,

  Ready,
}

/// <summary>Makes sure the Steam client is running and logged in before Valheim starts.</summary>
/// <remarks>
/// <para>
/// The game is started directly (see <see cref="GameLauncher"/>), so nothing starts Steam for it. Without
/// a client, Valheim's Steam API init does not fail loudly: the game window stays black and never shows
/// an error. So LaunchHeim checks first, starts Steam when it is not running, and waits until a user is
/// logged in.
/// </para>
/// <para>
/// Where "logged in" comes from differs per platform. Windows keeps Steam's <c>ActiveProcess/ActiveUser</c>
/// in the registry: 0 until a user is logged in, reset on a clean exit. Linux Steam used to mirror that
/// key into <c>registry.vdf</c> but no longer does (checked 2026-09-30, the file only holds
/// <c>SteamPID</c>), so there the last state in <c>logs/connection_log.txt</c> is used: <c>Logged On</c>
/// after login, <c>Logging Off</c>/<c>Logged Off</c> on exit. A crash can leave either signal set, so it
/// only counts while a <c>steam</c> process runs, and on Linux only when it is newer than that process.
/// The pid Steam records is not used, because Flatpak Steam writes the pid from inside its sandbox.
/// </para>
/// </remarks>
public sealed partial class SteamClient(Func<SteamState> probe, Func<bool> start)
{
  public const string FlatpakId = "com.valvesoftware.Steam";
  public const string ConnectionLog = "connection_log.txt";

  /// <summary>Long enough to log in or for Steam to update itself first.</summary>
  public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);

  public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

  /// <summary>
  /// Extra wait after a fresh login. The client reports the user a moment before the Steam API accepts
  /// games, and a game that connects too early ends up with the same black screen.
  /// </summary>
  public TimeSpan SettleDelay { get; init; } = TimeSpan.FromSeconds(5);

  public SteamState State() => probe();

  /// <summary>Returns once Steam is ready, starting it first when needed.</summary>
  /// <param name="progress">Told about every state change while waiting, never about <see cref="SteamState.Ready"/>.</param>
  /// <exception cref="LaunchException">Steam could not be started or was not ready in time.</exception>
  public async Task EnsureReadyAsync(Action<SteamState>? progress, CancellationToken cancellationToken)
  {
    var state = await Task.Run(State, cancellationToken);
    if (state == SteamState.Ready)
    {
      return;
    }

    if (state == SteamState.NotRunning && !start())
    {
      throw new LaunchException("Steam is not running and LaunchHeim could not start it. Start Steam, then click Play again.");
    }

    progress?.Invoke(state);
    var deadline = Stopwatch.StartNew();
    while (true)
    {
      await Task.Delay(PollInterval, cancellationToken);
      var current = await Task.Run(State, cancellationToken);
      if (current == SteamState.Ready)
      {
        break;
      }

      if (deadline.Elapsed >= Timeout)
      {
        throw new LaunchException(current == SteamState.NotRunning
          ? "Steam did not start. Start it yourself, then click Play again."
          : "Steam is still not logged in. Log in to Steam, then click Play again.");
      }

      if (current != state)
      {
        state = current;
        progress?.Invoke(state);
      }
    }

    await Task.Delay(SettleDelay, cancellationToken);
  }

  public static SteamClient ForCurrentUser()
  {
    if (OperatingSystem.IsWindows())
    {
      return new SteamClient(ProbeWindows, StartWindowsSteam);
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string[] logs =
    [
      Path.Combine(home, ".steam", "steam", "logs", ConnectionLog),
      Path.Combine(home, ".var", "app", FlatpakId, ".local", "share", "Steam", "logs", ConnectionLog),
    ];
    return new SteamClient(() => ProbeLinux(logs), () => StartLinuxSteam(home));
  }

  /// <summary>
  /// Whether the last connection state in Steam's <c>connection_log.txt</c> is <c>Logged On</c>, written at
  /// or after <paramref name="since"/> (local time, like the log).
  /// </summary>
  /// <remarks>State lines look like <c>[2026-09-30 20:25:00] [Logged On, 4, 7] [U:1:…] …</c>. Other lines are skipped.</remarks>
  public static bool IsLoggedOn(string connectionLog, DateTime since)
  {
    var lines = connectionLog.Split('\n');
    for (var i = lines.Length - 1; i >= 0; i--)
    {
      var match = StateLine().Match(lines[i]);
      if (!match.Success)
      {
        continue;
      }

      var time = DateTime.ParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
      // The log has whole seconds, the process start time does not.
      return match.Groups["state"].Value == "Logged On" && time >= since.AddSeconds(-1);
    }

    return false;
  }

  [System.Text.RegularExpressions.GeneratedRegex(@"^\[(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\] \[(?<state>[A-Za-z ]+), ")]
  private static partial System.Text.RegularExpressions.Regex StateLine();

  private static SteamState ProbeLinux(string[] logs)
  {
    if (SteamStartedAt() is not { } startedAt)
    {
      return SteamState.NotRunning;
    }

    return logs.Any(log => IsLoggedOn(ReadTail(log), startedAt)) ? SteamState.Ready : SteamState.Starting;
  }

  // The log grows to megabytes; the current session's last lines are all that matter.
  private static string ReadTail(string path)
  {
    try
    {
      using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      stream.Seek(Math.Max(0, stream.Length - 64 * 1024), SeekOrigin.Begin);
      using var reader = new StreamReader(stream);
      return reader.ReadToEnd();
    }
    catch (IOException)
    {
      return "";
    }
    catch (UnauthorizedAccessException)
    {
      return "";
    }
  }

  /// <summary>
  /// When the oldest running <c>steam</c> process started, or null when none runs. The client is
  /// <c>steam</c> on both systems (steam.exe, ubuntu12_32/steam), and a login is always newer than it.
  /// </summary>
  private static DateTime? SteamStartedAt()
  {
    DateTime? oldest = null;
    foreach (var process in Process.GetProcessesByName("steam"))
    {
      using (process)
      {
        try
        {
          var started = process.StartTime;
          oldest = oldest is { } o && o < started ? o : started;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
          // Exited between listing and asking; counts as not running.
        }
      }
    }

    return oldest;
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static SteamState ProbeWindows()
  {
    if (SteamStartedAt() is null)
    {
      return SteamState.NotRunning;
    }

    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
    return key?.GetValue("ActiveUser") is int user && user != 0 ? SteamState.Ready : SteamState.Starting;
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static bool StartWindowsSteam()
  {
    string? executable;
    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
    {
      executable = key?.GetValue("SteamExe") as string;
    }

    if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
    {
      executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe");
    }

    return File.Exists(executable) && Run(executable, "-silent");
  }

  // Native Steam first: it is what the packages install, and the one whose libraries are usually found.
  private static bool StartLinuxSteam(string home)
  {
    if (FindOnPath("steam") is { } steam)
    {
      return Run(steam, "-silent");
    }

    return Directory.Exists(Path.Combine(home, ".var", "app", FlatpakId))
      && FindOnPath("flatpak") is { } flatpak
      && Run(flatpak, "run", FlatpakId, "-silent");
  }

  private static string? FindOnPath(string name) =>
    (Environment.GetEnvironmentVariable("PATH") ?? "")
      .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
      .Select(directory => Path.Combine(directory, name))
      .FirstOrDefault(File.Exists);

  // -silent keeps the client in the tray; the login window still shows when a login is needed.
  private static bool Run(string fileName, params string[] arguments)
  {
    var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
    foreach (var argument in arguments)
    {
      info.ArgumentList.Add(argument);
    }

    // Steam outlives LaunchHeim and must not load our Qt 5 plugins either.
    foreach (var variable in GameLauncher.HostOnlyVariables)
    {
      info.Environment.Remove(variable);
    }

    try
    {
      Process.Start(info)?.Dispose();
      return true;
    }
    catch (System.ComponentModel.Win32Exception)
    {
      return false;
    }
  }
}
