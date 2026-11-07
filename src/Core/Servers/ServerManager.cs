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
  private readonly ConcurrentDictionary<string, IServerFiles> _files = new();

  /// <summary>Commands through files at a host take a poll of the plugin (2 s) plus two round trips each way.</summary>
  private static readonly TimeSpan HostedFileChannelTimeout = TimeSpan.FromSeconds(60);

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
    if (server.IsHosted)
    {
      // Its process isn't ours to see. Up is what answers Steam's query, or the plugin's heartbeat in its files.
      if (server.Host is { GameAddress.Length: > 0 } host
        && await ServerQuery.QueryAsync(host.GameAddress, TimeSpan.FromSeconds(3), cancellationToken) is { } answer)
      {
        return new ServerState(true, true, answer.Players, answer.MaxPlayers, ServerStartPhase.Online);
      }

      if (server.ConsoleEnabled && server.Host is { Address.Length: > 0 })
      {
        try
        {
          var beat = ServerHeartbeat.Parse(await WithFilesAsync(server, files => files.ReadTextAsync(ServerHeartbeat.FileName, cancellationToken)));
          if (beat is not null && beat.IsAlive(DateTimeOffset.UtcNow))
          {
            return new ServerState(true, true, beat.Players, MaxPlayers, ServerStartPhase.Online);
          }
        }
        catch (IOException ex)
        {
          Log.Warning($"Couldn't read {server.Name}'s heartbeat: {ex.Message}");
        }
      }

      return new ServerState(false, false, 0, 0);
    }

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
    if (server.IsHosted)
    {
      // The plugin saves and quits; what happens then (a restart by the host, or nothing) is the host's.
      await SendAsync(server, "stop", cancellationToken);
      return StopResult.Stopped;
    }

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

    if (!server.IsHosted || server.Host is { RconPort: > 0 })
    {
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
    }

    if (server.IsHosted)
    {
      try
      {
        return await new ServerCommandChannel(FilesOf(server), TimeSpan.FromSeconds(2)).SendAsync(command, fileChannelTimeout ?? HostedFileChannelTimeout, cancellationToken);
      }
      catch (IOException ex)
      {
        await ForgetConnectionAsync(server);
        throw new RconException(ex.Message, ex);
      }
    }

    return await new ServerCommandChannel(new LocalServerFiles(store.DirectoryOf(server))).SendAsync(command, fileChannelTimeout, cancellationToken);
  }

  /// <summary>Copies the server's mods in, or returns null for a plain vanilla start.</summary>
  private async Task<string?> PrepareModsAsync(ServerProfile server, string folder, bool replaceConfigs, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
  {
    if (await ResolveModsAsync(server, progress, cancellationToken) is not var (sourceDirectory, mods))
    {
      return null;
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

  /// <summary>
  /// Where the server's BepInEx and mods come from, and which: the instance's loader and the mods the
  /// server runs, or the vanilla setup's loader for a vanilla server with the console. Null when the
  /// server runs plain vanilla.
  /// </summary>
  private async Task<(string SourceDirectory, List<InstalledMod> Mods)?> ResolveModsAsync(ServerProfile server, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
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

    return (sourceDirectory, mods);
  }

  /// <summary>
  /// The files of a hosted server's BepInEx, mods and plugin, as <see cref="ServerDeploy"/> takes them.
  /// The plugin's config is written into the server's local folder first and uploaded from there.
  /// </summary>
  public async Task<IReadOnlyList<DeployFile>> HostedModFilesAsync(ServerProfile server, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
  {
    var files = new List<DeployFile>();
    if (await ResolveModsAsync(server, progress, cancellationToken) is var (sourceDirectory, mods))
    {
      foreach (var path in mods.SelectMany(m => m.Files).Select(ModInstaller.NormalizePath).Distinct(StringComparer.Ordinal))
      {
        var isConfig = path.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase);
        files.Add(new DeployFile(path, Path.Combine([sourceDirectory, .. path.Split('/')]), Executable: path.EndsWith(".sh", StringComparison.Ordinal), IsConfig: isConfig));
      }

      // Configs the player made in the instance without a mod installing them, as for local servers.
      var configDirectory = Path.Combine(sourceDirectory, "BepInEx", "config");
      if (Directory.Exists(configDirectory))
      {
        foreach (var config in Directory.EnumerateFiles(configDirectory, "*", SearchOption.AllDirectories))
        {
          var path = "BepInEx/config/" + ModInstaller.NormalizePath(Path.GetRelativePath(configDirectory, config));
          if (files.All(f => f.Path != path))
          {
            files.Add(new DeployFile(path, config, IsConfig: true));
          }
        }
      }
    }

    if (server.ConsoleEnabled)
    {
      var plugin = bundledPlugin ?? ServerPluginInstaller.BundledPlugin;
      if (!File.Exists(plugin))
      {
        throw new LaunchException($"LaunchHeim's server plugin is missing ({plugin}). Reinstall LaunchHeim.");
      }

      var folder = store.DirectoryOf(server);
      Directory.CreateDirectory(folder);
      var config = Path.Combine(folder, "hosted-plugin.cfg");
      await File.WriteAllTextAsync(config, ServerPluginInstaller.HostedConfig(server), cancellationToken);
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(config, UnixFileMode.UserRead | UnixFileMode.UserWrite);
      }

      files.Add(new DeployFile(ModInstaller.NormalizePath(Path.Combine(ServerPluginInstaller.PluginDirectory, ServerPluginInstaller.PluginFileName)), plugin));
      // Always LaunchHeim's own, so never treated as a config the server may keep.
      files.Add(new DeployFile(ModInstaller.NormalizePath(ServerPluginInstaller.ConfigPath), config));
    }

    return files;
  }

  /// <summary>Uploads a hosted server's BepInEx, mods and plugin (<see cref="ServerDeploy"/>).</summary>
  public async Task<DeployReport> DeployModsAsync(ServerProfile server, bool replaceConfigs, bool force, IProgress<DeployProgress>? progress, CancellationToken cancellationToken)
  {
    var wanted = await HostedModFilesAsync(server, null, cancellationToken);
    return await WithFilesAsync(server, files => ServerDeploy.RunAsync(files, "mods", wanted, replaceConfigs, force, progress, cancellationToken));
  }

  /// <summary>Uploads the whole dedicated server from a local folder, for a VPS that doesn't have it yet.</summary>
  public Task<DeployReport> DeployServerProgramAsync(ServerProfile server, string programDirectory, bool force, IProgress<DeployProgress>? progress, CancellationToken cancellationToken) =>
    WithFilesAsync(server, files => ServerDeploy.RunAsync(files, "server-program", ServerDeploy.Folder(programDirectory), replaceConfigs: false, force, progress, cancellationToken));

  /// <summary>What's in the server's folder at the host, in words, for "Test connection".</summary>
  public Task<string> DescribeHostAsync(ServerProfile server, CancellationToken cancellationToken) => WithFilesAsync(server, async files =>
  {
    var linux = await files.SizeAsync("valheim_server.x86_64", cancellationToken) is not null;
    var windows = await files.SizeAsync("valheim_server.exe", cancellationToken) is not null;
    var bepinex = await files.SizeAsync("BepInEx/core/BepInEx.Preloader.dll", cancellationToken) is not null;
    var plugin = await files.SizeAsync(ModInstaller.NormalizePath(Path.Combine(ServerPluginInstaller.PluginDirectory, ServerPluginInstaller.PluginFileName)), cancellationToken) is not null;
    var program = linux ? "Valheim's server (Linux)" : windows ? "Valheim's server (Windows)" : "no Valheim server in this folder yet";
    return $"Connected to {files.Describe}: {program}, {(bepinex ? "BepInEx" : "no BepInEx")}, {(plugin ? "LaunchHeim's plugin" : "no LaunchHeim plugin")}.";
  });

  /// <summary>The log the console follows: the local server's own, or the host's through its files.</summary>
  public ILogSource LogSourceOf(ServerProfile server) =>
    server.IsHosted && server.Host is { } host
      ? new ServerFilesLogSource(FilesOf(server), host.LogFile)
      : new FileLogSource(store.LogFileOf(server));

  /// <summary>Drops the connection to a host, after its settings changed or it broke.</summary>
  public async Task ForgetConnectionAsync(ServerProfile server)
  {
    if (_files.TryRemove(server.Id, out var files))
    {
      await files.DisposeAsync();
    }

    await DropConsoleAsync(server);
  }

  /// <summary>
  /// A hosted server's files from a folder in <see cref="HostAccess.SaveDirectory"/>'s form: inside the
  /// server folder (sharing its connection), absolute, or <c>~/…</c> from the login's home (a connection
  /// of its own). Always dispose it; the shared kind ignores that.
  /// </summary>
  internal IServerFiles HostFilesAt(ServerProfile server, string folder)
  {
    var host = server.Host ?? throw new InvalidOperationException("Only a hosted server has a host.");
    if (folder.StartsWith('/') || folder == "~" || folder.StartsWith("~/", StringComparison.Ordinal))
    {
      return ServerConnections.Open(host.WithDirectory(folder.StartsWith('~') ? folder.TrimStart('~').TrimStart('/') : folder));
    }

    return new SubFolderFiles(FilesOf(server), folder.Trim('/'));
  }

  /// <summary>The hosted server's own files (its folder, for configs).</summary>
  internal IServerFiles HostFiles(ServerProfile server) => new SubFolderFiles(FilesOf(server), "");

  private IServerFiles FilesOf(ServerProfile server) =>
    _files.GetOrAdd(server.Id, _ => ServerConnections.Open(
      server.Host ?? throw new InvalidOperationException("Only a hosted server has a host."),
      // A host key or certificate pinned on first use is saved right away (HostAccess.Fingerprint).
      () => store.Save(server)));

  /// <summary>Runs work on the host's files; a broken connection is dropped so the next call reconnects.</summary>
  private async Task<T> WithFilesAsync<T>(ServerProfile server, Func<IServerFiles, Task<T>> work)
  {
    try
    {
      return await work(FilesOf(server));
    }
    catch (IOException)
    {
      await ForgetConnectionAsync(server);
      throw;
    }
  }

  private async Task<RconClient> RconAsync(ServerProfile server, CancellationToken cancellationToken)
  {
    if (_rcon.TryGetValue(server.Id, out var existing) && existing.IsConnected)
    {
      return existing;
    }

    var client = server.Host is { RconPort: > 0 } host && server.IsHosted
      ? await RconClient.ConnectAsync(host.Address, host.RconPort, server.RconPassword, cancellationToken)
      : await RconClient.ConnectAsync("127.0.0.1", server.EffectiveRconPort, server.RconPassword, cancellationToken);
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
