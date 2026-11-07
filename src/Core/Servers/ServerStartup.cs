namespace CINE.LaunchHeim.Core.Servers;

/// <summary>How far a starting server has got, as its log tells.</summary>
public enum ServerStartPhase
{
  /// <summary>The process runs but hasn't logged anything that says more.</summary>
  Starting,

  /// <summary>BepInEx's preloader has started: mods are loading.</summary>
  LoadingMods,

  /// <summary>Valheim is loading the world.</summary>
  LoadingWorld,

  /// <summary>The world didn't exist yet and is being created, which takes longest.</summary>
  CreatingWorld,

  /// <summary>Connected to Steam's network: players can join.</summary>
  Online,
}

/// <summary>Reads a starting server's phase from its log, so players see progress without the console.</summary>
/// <remarks>
/// The markers are the lines Valheim 1.0.21 and BepInEx 5.4 write, from a real start (2026-11-06):
/// "Preloader started", then "Get create world &lt;name&gt;", "missing …/&lt;world&gt;.db" for a new world,
/// "Load world:", and "Game server connected" once players can join. A line a future version words
/// differently only makes the phase lag behind; "online" also comes from the other checks in
/// <see cref="ServerManager.StateAsync"/>.
/// </remarks>
public static class ServerStartup
{
  /// <summary>The phases a start goes through, for a progress bar: the index of a phase over this.</summary>
  public const int Steps = 4;

  public static ServerStartPhase Detect(IEnumerable<string> lines)
  {
    var phase = ServerStartPhase.Starting;
    foreach (var line in lines)
    {
      if (line.Contains("Game server connected", StringComparison.Ordinal))
      {
        return ServerStartPhase.Online;
      }

      if (line.Contains("Get create world", StringComparison.Ordinal) || line.Contains("Load world:", StringComparison.Ordinal))
      {
        phase = phase == ServerStartPhase.CreatingWorld ? phase : ServerStartPhase.LoadingWorld;
      }
      else if (line.Contains("  missing ", StringComparison.Ordinal) && line.EndsWith(".db", StringComparison.Ordinal))
      {
        phase = ServerStartPhase.CreatingWorld;
      }
      else if (phase == ServerStartPhase.Starting && line.Contains("Preloader started", StringComparison.Ordinal))
      {
        phase = ServerStartPhase.LoadingMods;
      }
    }

    return phase;
  }

  /// <summary>The phase from the log file, read while the server may still write it.</summary>
  public static ServerStartPhase Detect(string logFile)
  {
    try
    {
      using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      using var reader = new StreamReader(stream);
      return Detect(ReadLines(reader));
    }
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
    {
      return ServerStartPhase.Starting;
    }
  }

  /// <summary>The step a phase is at, 1 to <see cref="Steps"/>, for a progress bar.</summary>
  public static int Step(ServerStartPhase phase) => phase switch
  {
    ServerStartPhase.Starting => 1,
    ServerStartPhase.LoadingMods => 2,
    ServerStartPhase.LoadingWorld or ServerStartPhase.CreatingWorld => 3,
    _ => Steps,
  };

  private static IEnumerable<string> ReadLines(StreamReader reader)
  {
    while (reader.ReadLine() is { } line)
    {
      yield return line;
    }
  }
}
