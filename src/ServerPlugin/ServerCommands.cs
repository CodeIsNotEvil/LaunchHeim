#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace CINE.LaunchHeim.ServerPlugin;

/// <summary>The console's commands, run on Unity's main thread.</summary>
/// <remarks>
/// <para>
/// Valheim's classes are reached by name through reflection, like the game plugin does, so this builds
/// without the game and a renamed member only breaks the command that uses it, with a message saying so.
/// The names come from the game's own code (<c>ZNet</c>, <c>ZNetPeer</c>, <c>ZRoutedRpc</c>, <c>MessageHud</c>,
/// <c>Terminal</c>), read with ilspycmd from <c>assembly_valheim.dll</c>.
/// </para>
/// <para>
/// Anything that isn't one of these goes to Valheim's own console commands (<c>Terminal.commands</c>),
/// the ones an admin can type in game, so the console isn't limited to this list.
/// </para>
/// </remarks>
internal sealed class ServerCommands(ManualLogSource logger, Action stopServer)
{
  private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

  /// <summary>The answer to <c>save</c> once the world is on disk. Core's BackupService looks for it.</summary>
  public const string SavedAnswer = "The world is saved.";

  private readonly DateTime _startedAt = DateTime.UtcNow;

  public string Run(string line)
  {
    line = line.Trim();
    if (line.Length == 0)
    {
      return "";
    }

    var space = line.IndexOf(' ');
    var name = (space < 0 ? line : line.Substring(0, space)).ToLowerInvariant();
    var argument = space < 0 ? "" : line.Substring(space + 1).Trim();
    try
    {
      switch (name)
      {
        case "help":
          return Help();
        case "status":
          return Status();
        case "players":
          return Players();
        case "say":
          return Say(argument);
        case "save":
          // Not SaveWorldAndPlayerProfiles, which Valheim's own console command calls: on a dedicated
          // server it checks HardSaveBlock, which reads PlatformManager.DistributionPlatform, and that is
          // null there. Every save within 60 s of the last one (or of the start) then threw a
          // NullReferenceException and saved nothing (Valheim 1.0.21, found 2026-11-07). ZNet.Save is
          // what it ends in anyway: the world and the players' profiles.
          // Synchronous, as Valheim saves on shutdown: the answer then means the world is on disk, which
          // backups wait for (BackupService). Valheim reports the finished save from its save thread, and
          // BepInEx doesn't copy other threads' Unity messages into its log, so a host's console never
          // showed it (found 2026-11-07). A save takes a fraction of a second on a typical world.
          Call(ZNet(), "Save", true, true, false);
          return SavedAnswer;
        case "stop":
          logger.LogInfo("Stopping the server on request from the console.");
          stopServer();
          return "Saving the world and stopping the server.";
        case "kick":
        case "ban":
        case "unban":
          if (argument.Length == 0)
          {
            return $"Usage: {name} <name, IP or player ID>";
          }

          Call(ZNet(), char.ToUpperInvariant(name[0]) + name.Substring(1), argument);
          return $"{name} {argument}: done.";
        case "banned":
          return List("m_bannedList", "Nobody is banned.");
        case "admins":
          return List("m_adminList", "No admins are listed.");
        case "permitted":
          return List("m_permittedList", "Everybody may join (the permitted list is empty).");
        default:
          return RunGameCommand(line, name);
      }
    }
    catch (Exception ex)
    {
      var inner = ex is TargetInvocationException { InnerException: { } cause } ? cause : ex;
      logger.LogWarning($"Console command \"{line}\" failed: {inner}");
      return "Error: " + inner.Message;
    }
  }

  private static string Help() => string.Join("\n", new[]
  {
    "status              server name, world, players, uptime",
    "players             who is online, with their player IDs",
    "say <text>          show a message in the middle of every player's screen",
    "save                save the world now",
    "stop                save the world and stop the server",
    "kick <who>          kick by name, IP or player ID",
    "ban <who>           ban by name, IP or player ID",
    "unban <who>         unban by IP or player ID",
    "banned / admins / permitted   the server's lists",
    "Anything else is passed to Valheim's own console commands.",
  });

  private string Status()
  {
    var znet = ZNet();
    var world = Call(znet, "GetWorld");
    var worldName = world?.GetType().GetField("m_name", Any)?.GetValue(world) as string ?? "?";
    var serverName = znet.GetType().GetField("m_ServerName", Any)?.GetValue(null) as string ?? "?";
    var peers = Peers().Count;
    var uptime = DateTime.UtcNow - _startedAt;
    // Application.version is Unity's project version ("1.0"); the game's own is Version.GetVersionString.
    // Its one parameter (includeMercurialHash) is optional, which reflection doesn't fill in.
    var versionMethod = Type("Version")?.GetMethods(Any).FirstOrDefault(m => m.Name == "GetVersionString" && m.GetParameters().All(p => p.IsOptional));
    var version = versionMethod?.Invoke(null, versionMethod.GetParameters().Select(p => p.DefaultValue).ToArray()) as string ?? Application.version;
    return $"Server: {serverName}\nWorld: {worldName}\nPlayers: {peers}\nUp: {(int)uptime.TotalHours}h {uptime.Minutes}m\nValheim: {version}";
  }

  /// <summary>
  /// The heartbeat's JSON (FileChannel.Heartbeat), or null while the world isn't loaded. Core's
  /// ServerManager reads it: <c>time</c> (UTC), <c>players</c>, <c>world</c>, <c>version</c>, <c>stopped</c>.
  /// </summary>
  public string? StatusJson(bool stopped)
  {
    var znet = Type("ZNet")?.GetProperty("instance", Any)?.GetValue(null);
    var zones = Type("ZoneSystem")?.GetProperty("instance", Any)?.GetValue(null);
    if (!stopped && (znet is null || zones is null))
    {
      return null;
    }

    var world = "";
    var players = 0;
    if (znet is not null)
    {
      var worldObject = Call(znet, "GetWorld");
      world = worldObject?.GetType().GetField("m_name", Any)?.GetValue(worldObject) as string ?? "";
      players = Peers().Count;
    }

    var versionMethod = Type("Version")?.GetMethods(Any).FirstOrDefault(m => m.Name == "GetVersionString" && m.GetParameters().All(p => p.IsOptional));
    var version = versionMethod?.Invoke(null, versionMethod.GetParameters().Select(p => p.DefaultValue).ToArray()) as string ?? Application.version;
    return "{\"time\":\"" + DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
      + "\",\"players\":" + players
      + ",\"world\":\"" + Escape(world)
      + "\",\"version\":\"" + Escape(version)
      + "\",\"stopped\":" + (stopped ? "true" : "false") + "}";
  }

  private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

  private string Players()
  {
    var peers = Peers();
    if (peers.Count == 0)
    {
      return "Nobody is online.";
    }

    var text = new StringBuilder();
    foreach (var peer in peers)
    {
      var playerName = peer.GetType().GetField("m_playerName", Any)?.GetValue(peer) as string;
      var socket = peer.GetType().GetField("m_socket", Any)?.GetValue(peer);
      var id = socket is null ? "" : Call(socket, "GetHostName") as string ?? "";
      text.Append(string.IsNullOrEmpty(playerName) ? "(joining)" : playerName).Append('\t').Append(id).Append('\n');
    }

    return text.ToString().TrimEnd('\n');
  }

  private static string Say(string text)
  {
    if (text.Length == 0)
    {
      return "Usage: say <text>";
    }

    // What MessageHud.MessageAll does: the routed RPC every client registers, to everybody (0),
    // MessageType.Center (2). MessageHud itself doesn't exist on a server.
    var rpcType = Type("ZRoutedRpc") ?? throw new InvalidOperationException("Valheim's ZRoutedRpc wasn't found.");
    var rpc = rpcType.GetProperty("instance", Any)?.GetValue(null) ?? throw new InvalidOperationException("The server isn't up yet.");
    var invoke = rpcType.GetMethods(Any).FirstOrDefault(m =>
      m.Name == "InvokeRoutedRPC" && m.GetParameters() is { Length: 3 } p && p[0].ParameterType == typeof(long) && p[1].ParameterType == typeof(string))
      ?? throw new InvalidOperationException("Valheim's InvokeRoutedRPC wasn't found.");
    invoke.Invoke(rpc, new object[] { 0L, "ShowMessage", new object[] { 2, text } });
    return "Sent: " + text;
  }

  private string List(string field, string empty)
  {
    var list = ZNet().GetType().GetField(field, Any)?.GetValue(ZNet()) ?? throw new InvalidOperationException("The list wasn't found.");
    var entries = (Call(list, "GetList") as IEnumerable)?.Cast<object>().Select(o => o.ToString()).ToList() ?? new List<string>();
    return entries.Count == 0 ? empty : string.Join("\n", entries);
  }

  /// <summary>Valheim's own console command, as in game: <c>Terminal.commands[name].RunAction(args)</c>.</summary>
  private static string RunGameCommand(string line, string name)
  {
    var terminal = Type("Terminal");
    var commands = terminal?.GetField("commands", Any)?.GetValue(null) as IDictionary;
    if (commands is null || !commands.Contains(name))
    {
      return $"Unknown command \"{name}\". Type help for the list.";
    }

    var command = commands[name]!;
    // Cheats would first ask for "confirmcheats" in a terminal the server doesn't have, and turning
    // them on for the whole server isn't something to do by accident from a console.
    if (command.GetType().GetField("IsCheat", Any)?.GetValue(command) is true)
    {
      return $"{name} is a cheat command; run it in game as an admin.";
    }

    var argsType = Type("Terminal+ConsoleEventArgs");
    var constructor = argsType?.GetConstructors(Any).FirstOrDefault(c => c.GetParameters() is { Length: 3 } p && p[0].ParameterType == typeof(string));
    var run = command.GetType().GetMethod("RunAction", Any);
    if (constructor is null || run is null)
    {
      return "Valheim's console commands can't be run from here in this game version.";
    }

    // There is no terminal to print into on a server (Context is null): commands that only print fail
    // with an error, the rest do their work.
    try
    {
      run.Invoke(command, new[] { constructor.Invoke(new object?[] { line, null, command }) });
    }
    catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException)
    {
      // It wanted to print into the in-game console, which a server doesn't have.
      return $"{name} only works in the game's own console.";
    }

    return $"Ran {name}.";
  }

  private static object ZNet()
  {
    var type = Type("ZNet") ?? throw new InvalidOperationException("Valheim's ZNet wasn't found.");
    return type.GetProperty("instance", Any)?.GetValue(null) ?? throw new InvalidOperationException("The server isn't up yet.");
  }

  private static IList<object> Peers() =>
    (Call(ZNet(), "GetPeers") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();

  private static object? Call(object target, string method, params object[] arguments)
  {
    // Matched on the argument types too, so an overload added by a game update (Kick(long)) isn't picked.
    var info = target.GetType().GetMethods(Any).FirstOrDefault(m =>
        m.Name == method
        && m.GetParameters() is var parameters
        && parameters.Length == arguments.Length
        && parameters.Zip(arguments, (p, a) => p.ParameterType.IsInstanceOfType(a)).All(ok => ok))
      ?? throw new InvalidOperationException($"Valheim's {target.GetType().Name}.{method} wasn't found.");
    return info.Invoke(target, arguments);
  }

  private static Type? Type(string name) =>
    AppDomain.CurrentDomain.GetAssemblies()
      .Where(a => a.GetName().Name is "assembly_valheim" or "assembly_utils")
      .Select(a => a.GetType(name, throwOnError: false))
      .FirstOrDefault(t => t is not null);
}
