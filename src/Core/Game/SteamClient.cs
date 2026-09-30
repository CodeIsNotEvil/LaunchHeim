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
/// "Logged in" is Steam's own <c>ActiveProcess/ActiveUser</c>: the Windows registry key, and on Linux the
/// same key in <c>registry.vdf</c>. It is 0 while the client starts or shows the login window, and Steam
/// resets it on a clean exit. A crash can leave it set, so it only counts while a <c>steam</c> process
/// runs. The pid Steam records next to it is not used, because Flatpak Steam writes the pid from inside
/// its sandbox.
/// </para>
/// </remarks>
public sealed class SteamClient(Func<bool> isProcessRunning, Func<long> activeUser, Func<bool> start)
{
  public const string FlatpakId = "com.valvesoftware.Steam";

  /// <summary>Long enough to log in or for Steam to update itself first.</summary>
  public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);

  public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

  /// <summary>
  /// Extra wait after a fresh login. The client reports the user a moment before the Steam API accepts
  /// games, and a game that connects too early ends up with the same black screen.
  /// </summary>
  public TimeSpan SettleDelay { get; init; } = TimeSpan.FromSeconds(5);

  public SteamState State()
  {
    if (!isProcessRunning())
    {
      return SteamState.NotRunning;
    }

    return activeUser() != 0 ? SteamState.Ready : SteamState.Starting;
  }

  /// <summary>Returns once Steam is ready, starting it first when needed.</summary>
  /// <param name="progress">Told about every state change while waiting, never about <see cref="SteamState.Ready"/>.</param>
  /// <exception cref="LaunchException">Steam could not be started or was not ready in time.</exception>
  public async Task EnsureReadyAsync(Action<SteamState>? progress, CancellationToken cancellationToken)
  {
    var state = State();
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
      var current = State();
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
      return new SteamClient(IsSteamProcessRunning, WindowsActiveUser, StartWindowsSteam);
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string[] registries =
    [
      Path.Combine(home, ".steam", "registry.vdf"),
      Path.Combine(home, ".var", "app", FlatpakId, ".steam", "registry.vdf"),
    ];
    return new SteamClient(IsSteamProcessRunning, () => registries.Max(LinuxActiveUser), () => StartLinuxSteam(home));
  }

  /// <summary>Reads <c>HKCU/Software/Valve/Steam/ActiveProcess/ActiveUser</c> from a Linux <c>registry.vdf</c>.</summary>
  public static long ActiveUserFromRegistry(string vdf)
  {
    var value = VdfNode.Parse(vdf)["Registry"]?["HKCU"]?["Software"]?["Valve"]?["Steam"]?["ActiveProcess"]?.Value("ActiveUser");
    return long.TryParse(value, out var user) ? user : 0;
  }

  private static long LinuxActiveUser(string registry)
  {
    try
    {
      return File.Exists(registry) ? ActiveUserFromRegistry(File.ReadAllText(registry)) : 0;
    }
    catch (IOException)
    {
      // Steam rewrites the file while we read it. The next poll sees the finished one.
      return 0;
    }
  }

  // The client is "steam" on both systems (steam.exe, ubuntu12_32/steam). On Linux the bootstrap script
  // has the same name while it unpacks or updates the client, which is fine: ActiveUser is still 0 then.
  private static bool IsSteamProcessRunning()
  {
    var processes = Process.GetProcessesByName("steam");
    foreach (var process in processes)
    {
      process.Dispose();
    }

    return processes.Length > 0;
  }

  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
  private static long WindowsActiveUser()
  {
    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
    return key?.GetValue("ActiveUser") is int user ? (uint)user : 0;
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
