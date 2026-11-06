#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using CINE.LaunchHeim.Shared;
using UnityEngine;

namespace CINE.LaunchHeim.ServerPlugin;

/// <summary>
/// LaunchHeim's plugin on a dedicated server: a console over Source RCON and through files
/// (decision 0020). It has nothing to do with the players' game and isn't needed there.
/// </summary>
/// <remarks>
/// <para>
/// Commands may arrive on any thread (RCON runs on the thread pool), but Valheim's objects only work on
/// Unity's main thread. So every command is queued and run in <see cref="Update"/>, and the RCON
/// connection waits for its answer there.
/// </para>
/// <para>
/// On a game client (someone dropped the dll into their own plugins) it does nothing: a console that
/// opens a port on a player's PC would only be a risk.
/// </para>
/// </remarks>
[BepInPlugin(Guid, "LaunchHeim Server", "1.0.0")]
public sealed class LaunchHeimServerPlugin : BaseUnityPlugin
{
  /// <summary>Also the config file's name. Core's ServerPluginInstaller has the same constant.</summary>
  public const string Guid = "io.github.codeisnotevil.launchheim.server";

  private readonly ConcurrentQueue<Pending> _queue = new();
  private RconServer? _rcon;
  private FileChannel? _files;
  private ServerCommands? _commands;

  private void Awake()
  {
    if (!Application.isBatchMode)
    {
      Logger.LogInfo("Not a dedicated server; LaunchHeim's server plugin stays off.");
      return;
    }

    var rconEnabled = Config.Bind("RCON", "Enabled", true, "Source RCON, for LaunchHeim's console and any other RCON tool.");
    var address = Config.Bind("RCON", "Address", "127.0.0.1", "127.0.0.1 accepts only this machine, 0.0.0.0 every network.");
    var port = Config.Bind("RCON", "Port", 2458, "The TCP port to listen on.");
    var password = Config.Bind("RCON", "Password", "", "Required. RCON sends it unencrypted, so use a long random one.");
    var filesEnabled = Config.Bind("FileChannel", "Enabled", true, "Run commands written as files into BepInEx/config/LaunchHeim.Server/commands.");

    _commands = new ServerCommands(Logger, StopServer);

    if (rconEnabled.Value)
    {
      StartRcon(address.Value, port.Value, password.Value);
    }

    if (filesEnabled.Value)
    {
      _files = new FileChannel(Paths.ConfigPath, Logger);
      Logger.LogInfo($"Taking commands from files in {_files.Directory}");
    }
  }

  private void StartRcon(string address, int port, string password)
  {
    if (password.Length == 0)
    {
      // Without a password anyone who reaches the port runs the server.
      Logger.LogWarning("RCON has no password set, so it stays off.");
      return;
    }

    if (!IPAddress.TryParse(address, out var ip))
    {
      Logger.LogWarning($"RCON address \"{address}\" isn't an IP address; listening on 127.0.0.1 instead.");
      ip = IPAddress.Loopback;
    }

    try
    {
      _rcon = new RconServer(ip, port, password, Enqueue, message => Logger.LogInfo(message));
      _rcon.Start();
      Logger.LogInfo($"RCON listening on {ip}:{port}");
    }
    catch (Exception ex)
    {
      // A taken port must not keep the server from running; the file channel still works.
      Logger.LogError($"RCON could not listen on {ip}:{port}: {ex.Message}");
      _rcon = null;
    }
  }

  /// <summary>Hands a command to the main thread and waits for its answer.</summary>
  private Task<string> Enqueue(string line)
  {
    var pending = new Pending(line);
    _queue.Enqueue(pending);
    return pending.Answer.Task;
  }

  private void Update()
  {
    if (_commands is null)
    {
      return;
    }

    // A few per frame, so a flood of commands can't stall the server.
    for (var i = 0; i < 8 && _queue.TryDequeue(out var pending); i++)
    {
      pending.Answer.TrySetResult(_commands.Run(pending.Line));
    }

    _files?.Poll(Time.unscaledTime, _commands.Run);
  }

  private void StopServer()
  {
    // Application.Quit runs Game.OnApplicationQuit, which saves the world and the players before the
    // server goes. Delayed by a frame so the "stopping" answer still reaches the console.
    StartCoroutine(QuitNextFrame());
  }

  private static System.Collections.IEnumerator QuitNextFrame()
  {
    yield return null;
    yield return null;
    Application.Quit();
  }

  private void OnDestroy()
  {
    _rcon?.Dispose();
    while (_queue.TryDequeue(out var pending))
    {
      pending.Answer.TrySetResult("The server is shutting down.");
    }
  }

  private sealed class Pending(string line)
  {
    public string Line { get; } = line;

    // Continuations run on the RCON thread, never inline on Unity's main thread.
    public TaskCompletionSource<string> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
  }
}
