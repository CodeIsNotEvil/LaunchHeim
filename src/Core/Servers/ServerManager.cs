using System.Collections.Concurrent;
using System.Diagnostics;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Saves;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>How a server is doing right now.</summary>
/// <param name="Running">Its process is alive.</param>
/// <param name="Online">Players can join.</param>
/// <param name="Players">How many are on, or -1 when nothing said.</param>
/// <param name="Phase">How far a start has got (<see cref="ServerStartup"/>); Online once it's up.</param>
public sealed record ServerState(bool Running, bool Online, int Players, int MaxPlayers, ServerStartPhase Phase = ServerStartPhase.Starting);

/// <summary>How a stop went.</summary>
public enum StopResult
{
  /// <summary>It saved and quit, or wasn't running.</summary>
  Stopped,

  /// <summary>It ignored every request and still runs; only killing it is left.</summary>
  StillRunning,
}

/// <summary>Everything LaunchHeim does with local dedicated servers, for the window and (later) the command line.</summary>
/// <remarks>
/// <para>
/// Starting: the profile is checked against Valheim's rules, the server's folder gets its mods
/// (<see cref="ServerMods"/>, from the linked instance) and LaunchHeim's server plugin
/// (<see cref="ServerPluginInstaller"/>), and the process starts on its own (<see cref="ServerProcess"/>).
/// A vanilla server with the console on gets BepInEx from the vanilla setup instant start uses
/// (<see cref="GamePlugin"/>), with no gameplay mods: clients don't need anything for it.
/// </para>
/// <para>
/// Commands go over RCON while the plugin listens, and through the file channel when RCON can't be
/// reached (a port taken by something else), which is slower but needs nothing but the files.
/// </para>
/// </remarks>
/// <param name="categories">Thunderstore's categories for an installed mod, or null when unknown.</param>
public sealed class ServerManager(
  ServerStore store,
  DedicatedServer dedicated,
  InstanceStore instances,
  GamePlugin gamePlugin,
  Func<InstalledMod, IReadOnlyList<string>?> categories,
  string? bundledPlugin = null)
{
  private readonly ConcurrentDictionary<string, RconClient> _rcon = new();

  public ServerStore Store => store;

  public DedicatedServer Dedicated => dedicated;

  public Instance? InstanceOf(ServerProfile server) =>
    server.InstanceId is { } id ? instances.LoadAll().FirstOrDefault(i => i.Id == id) : null;

  public IReadOnlyList<ServerModEntry> ModEntries(ServerProfile server) =>
    InstanceOf(server) is { } instance ? ServerMods.Entries(instance, server, categories) : [];

  public Process? FindProcess(ServerProfile server) => ServerProcess.Find(store.DirectoryOf(server));

  /// <summary>Valheim's player limit; the server creates its Steam lobby with room for ten.</summary>
  public const int MaxPlayers = 10;

  /// <summary>Whether the server runs, accepts players, and how many are on.</summary>
  /// <remarks>
  /// Steam's server query (A2S) is the usual way, but a private local server didn't answer it at all
  /// (2026-11-06: its query port's receive queue sat full, nothing in the server read it). So when A2S
  /// is silent the server's own log says how far it got (<see cref="ServerStartup"/>; the log is new for
  /// every start), and the plugin's <c>status</c> over RCON gives the player count. Without the plugin
  /// the count is unknown (-1).
  /// </remarks>
  public async Task<ServerState> StateAsync(ServerProfile server, CancellationToken cancellationToken)
  {
    using var process = FindProcess(server);
    if (process is null)
    {
      return new ServerState(false, false, 0, 0);
    }

    if (await ServerQuery.QueryAsync(server.JoinAddress(), TimeSpan.FromSeconds(1), cancellationToken) is { } status)
    {
      return new ServerState(true, true, status.Players, status.MaxPlayers, ServerStartPhase.Online);
    }

    // The plugin answers while the world is still loading, so only the log says when players can join.
    var phase = ServerStartup.Detect(store.LogFileOf(server));
    if (phase != ServerStartPhase.Online)
    {
      return new ServerState(true, false, 0, MaxPlayers, phase);
    }

    var players = -1;
    if (server.ConsoleEnabled)
    {
      try
      {
        using var quick = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        quick.CancelAfter(TimeSpan.FromSeconds(2));
        var client = await RconAsync(server, quick.Token);
        players = ParsePlayers(await client.SendAsync("status", quick.Token)) ?? -1;
      }
      catch (Exception ex) when (ex is RconException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
      {
        await DropConsoleAsync(server);
      }
    }

    return new ServerState(true, true, players, MaxPlayers, ServerStartPhase.Online);
  }

  /// <summary>The count from the plugin's <c>status</c> ("Players: 2"), or null when it isn't up yet.</summary>
  internal static int? ParsePlayers(string status)
  {
    foreach (var line in status.Split('\n'))
    {
      if (line.StartsWith("Players: ", StringComparison.Ordinal) && int.TryParse(line["Players: ".Length..].Trim(), out var players))
      {
        return players;
      }
    }

    return null;
  }


  public async Task StartAsync(ServerProfile server, bool replaceConfigs, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
  {
    if (server.Problems() is { Count: > 0 } problems)
    {
      throw new LaunchException(string.Join("\n", problems));
    }

    using (var running = FindProcess(server))
    {
      if (running is not null)
      {
        throw new LaunchException($"{server.Name} is already running.");
      }
    }

    var serverDirectory = dedicated.DirectoryFor(server) ?? throw new LaunchException(DedicatedServer.MissingText(server.Binary));
    var folder = store.DirectoryOf(server);
    var modsDirectory = await PrepareModsAsync(server, folder, replaceConfigs, progress, cancellationToken);

    GameLauncher.PrepareGameFolder(serverDirectory, modsDirectory);
    var saveDirectory = store.SaveDirectoryOf(server);
    Directory.CreateDirectory(saveDirectory);
    var logFile = store.LogFileOf(server);
    var plan = ServerLauncher.Plan(server, serverDirectory, modsDirectory, saveDirectory, logFile, GameLauncher.CurrentEnvironment());
    Log.Info($"Starting server {server.Name}: {plan.FileName} {string.Join(' ', GameLauncher.Redact(plan.Arguments))}");

    // A fresh log, so the console doesn't show the last session as if it were this one.
    TryDelete(logFile);
    await DropConsoleAsync(server);
    using var process = ServerProcess.Start(plan, folder);
    server.LastStartedAt = DateTimeOffset.UtcNow;
    store.Save(server);
  }

  /// <summary>Saves the world and stops the server, through the plugin if it answers and the system otherwise.</summary>
  public async Task<StopResult> StopAsync(ServerProfile server, CancellationToken cancellationToken)
  {
    using var process = FindProcess(server);
    if (process is null)
    {
      return StopResult.Stopped;
    }

    var asked = false;
    if (server.ConsoleEnabled)
    {
      try
      {
        // Not the file channel's usual 30 s: a plugin that isn't answering shouldn't hold up SIGINT.
        await SendAsync(server, "stop", cancellationToken, fileChannelTimeout: TimeSpan.FromSeconds(10));
        asked = true;
      }
      catch (RconException ex)
      {
        Log.Warning($"The server plugin didn't take the stop command: {ex.Message}");
      }
    }

    if (!asked && !ServerProcess.RequestStop(process))
    {
      return StopResult.StillRunning;
    }

    if (!await ServerProcess.WaitForExitAsync(process, ServerProcess.StopTimeout, cancellationToken))
    {
      return StopResult.StillRunning;
    }

    await DropConsoleAsync(server);
    ServerProcess.Forget(store.DirectoryOf(server));
    return StopResult.Stopped;
  }

  /// <summary>Ends the process without saving, for a server that won't stop.</summary>
  public async Task KillAsync(ServerProfile server)
  {
    using var process = FindProcess(server);
    if (process is not null)
    {
      Log.Warning($"Killing server {server.Name}; what happened since its last save is lost.");
      process.Kill(entireProcessTree: true);
      await process.WaitForExitAsync();
    }

    await DropConsoleAsync(server);
    ServerProcess.Forget(store.DirectoryOf(server));
  }

  /// <summary>Runs a console command on the server and returns its answer.</summary>
  /// <exception cref="RconException">Neither RCON nor the file channel got an answer.</exception>
  public async Task<string> SendAsync(ServerProfile server, string command, CancellationToken cancellationToken, TimeSpan? fileChannelTimeout = null)
  {
    if (!server.ConsoleEnabled)
    {
      throw new RconException("The console is switched off for this server.");
    }

    try
    {
      var client = await RconAsync(server, cancellationToken);
      return await client.SendAsync(command, cancellationToken);
    }
    catch (RconException ex)
    {
      await DropConsoleAsync(server);
      Log.Info($"RCON to {server.Name} failed ({ex.Message}), trying the file channel.");
    }

    return await new ServerCommandChannel(new LocalServerFiles(store.DirectoryOf(server))).SendAsync(command, fileChannelTimeout, cancellationToken);
  }

  /// <summary>Copies the server's mods in, or returns null for a plain vanilla start.</summary>
  private async Task<string?> PrepareModsAsync(ServerProfile server, string folder, bool replaceConfigs, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
  {
    var instance = InstanceOf(server);
    if (server.InstanceId is not null && instance is null)
    {
      throw new LaunchException("The instance this server takes its mods from no longer exists. Choose another one in the server's settings.");
    }

    var modded = instance is not null && instance.Mods.Any(m => !m.IsLoader && m.Enabled);
    if (!modded && !server.ConsoleEnabled)
    {
      return null;
    }

    Instance source;
    string sourceDirectory;
    if (instance?.Loader is not null)
    {
      source = instance;
      sourceDirectory = instances.DirectoryOf(instance);
    }
    else
    {
      // Vanilla with the console: BepInEx alone, from the setup instant start uses.
      (source, sourceDirectory) = await gamePlugin.EnsureVanillaSetupAsync(progress, cancellationToken);
    }

    var mods = new List<InstalledMod>();
    if (source.Loader is { } loader)
    {
      mods.Add(loader);
    }

    if (instance is not null && ReferenceEquals(source, instance))
    {
      mods.AddRange(ServerMods.Entries(instance, server, categories).Where(e => e.Included).Select(e => e.Mod));
    }

    progress?.Report(new InstallProgress(server.Name, "Copying mods to the server", null));
    var sync = await Task.Run(() => ServerMods.Sync(sourceDirectory, mods, folder, replaceConfigs), cancellationToken);
    Log.Info($"Server {server.Name}: {mods.Count} mods from {sourceDirectory}, {sync.Copied} files copied, {sync.Removed} removed, {sync.ConfigsCopied} configs copied");

    if (server.ConsoleEnabled)
    {
      ServerPluginInstaller.Install(folder, server, bundledPlugin: bundledPlugin);
    }
    else
    {
      ServerPluginInstaller.Remove(folder);
    }

    return folder;
  }

  private async Task<RconClient> RconAsync(ServerProfile server, CancellationToken cancellationToken)
  {
    if (_rcon.TryGetValue(server.Id, out var existing) && existing.IsConnected)
    {
      return existing;
    }

    var client = await RconClient.ConnectAsync("127.0.0.1", server.EffectiveRconPort, server.RconPassword, cancellationToken);
    if (_rcon.TryRemove(server.Id, out var old))
    {
      await old.DisposeAsync();
    }

    _rcon[server.Id] = client;
    return client;
  }

  private async Task DropConsoleAsync(ServerProfile server)
  {
    if (_rcon.TryRemove(server.Id, out var client))
    {
      await client.DisposeAsync();
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
}
