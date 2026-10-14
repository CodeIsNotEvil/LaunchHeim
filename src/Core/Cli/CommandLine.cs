using CINE.LaunchHeim.Core.Game;

namespace CINE.LaunchHeim.Core.Cli;

public enum CliVerb
{
  Help,
  Version,
  List,
  Play,

  /// <summary>Valheim's launch options in Steam: <c>launchheim steam %command%</c>.</summary>
  Steam,
}

/// <param name="SteamCommand">What Steam put in place of <c>%command%</c>. Empty for the other verbs.</param>
public sealed record CliCommand(CliVerb Verb, PlayRequest Request, IReadOnlyList<string> SteamCommand);

public sealed class CliUsageException(string message) : Exception(message);

/// <summary>LaunchHeim's command line. Anything it doesn't claim opens the window, as before.</summary>
/// <remarks>
/// Only a known verb as the first argument makes a command, so an <c>nxm://</c> link from the browser and
/// a start without arguments still reach the window. The verbs are words rather than options because
/// <c>--register-desktop</c> (install.sh) is the only option the window had, and a dash-less verb can't
/// be mistaken for one of Valheim's arguments in Steam's launch options.
/// </remarks>
public static class CommandLine
{
  public const string Usage = """
    Usage: launchheim [command] [options]

    Without a command LaunchHeim opens its window.

    Commands:
      play [options]             Start Valheim and wait until it closes. Without options it plays
                                 what was played last, like the Play button in the sidebar.
      steam [options] %command%  For Valheim's launch options in Steam. Steam's Play button then
                                 starts the last play (or what the options pick) with its instance.
      list                       Show the instances, characters, servers and worlds to pick from.
      help                       Show this text.
      --version                  Show LaunchHeim's version.

    Options for play and steam:
      --instance <name|id>       Play this instance.
      --vanilla                  Play without mods.
      --server <name|host:port>  Join a server. Uses the character, setup and password it was last
                                 played with, unless given here.
      --world <name>             Select a world in the menus. Uses what it was last played with.
      --character <name>         The character for --server or --world.
      --password <password>      The server's password. --password= forgets a remembered one.
      --instant, --no-instant    Start instantly: straight into the world, or onto the server
                                 without the character selection. A vanilla setup then runs
                                 with LaunchHeim's own BepInEx, which loads no gameplay mods.

    Examples:
      launchheim play --server "Our Server" --instance "Modded"
      launchheim play --world Midgard --character Ragnar --vanilla --instant
      Steam → Valheim → Properties → Launch options:  launchheim steam %command%
    """;

  /// <summary>The command, or null when the arguments are for the window (none, or an nxm:// link).</summary>
  /// <exception cref="CliUsageException">A known verb with arguments that don't fit it.</exception>
  public static CliCommand? Parse(IReadOnlyList<string> args)
  {
    if (args.Count == 0)
    {
      return null;
    }

    var rest = args.Skip(1).ToList();
    switch (args[0])
    {
      case "help" or "--help" or "-h":
        return new CliCommand(CliVerb.Help, new PlayRequest(), []);
      case "version" or "--version":
        return new CliCommand(CliVerb.Version, new PlayRequest(), []);
      case "list":
        if (rest.Count > 0)
        {
          throw new CliUsageException($"list takes no arguments, got \"{rest[0]}\".");
        }

        return new CliCommand(CliVerb.List, new PlayRequest(), []);
      case "play":
        var (request, extra) = ParseRequest(rest, stopAtCommand: false);
        return new CliCommand(CliVerb.Play, request, extra);
      case "steam":
        var (steamRequest, command) = ParseRequest(rest, stopAtCommand: true);
        if (command.Count == 0)
        {
          throw new CliUsageException("steam needs Steam's command: put %command% at the end of Valheim's launch options.");
        }

        return new CliCommand(CliVerb.Steam, steamRequest, command);
      default:
        return null;
    }
  }

  /// <param name="stopAtCommand">
  /// For <c>steam</c>: the first argument that isn't an option starts Steam's command, and everything from
  /// there is passed on untouched, Valheim's own options included. <c>--</c> may mark it as well.
  /// </param>
  private static (PlayRequest Request, IReadOnlyList<string> Command) ParseRequest(IReadOnlyList<string> args, bool stopAtCommand)
  {
    var request = new PlayRequest();
    for (var i = 0; i < args.Count; i++)
    {
      var argument = args[i];
      if (stopAtCommand && (argument == "--" || !argument.StartsWith('-')))
      {
        return (request, args.Skip(argument == "--" ? i + 1 : i).ToList());
      }

      // --password=secret as well as --password secret; only the first '=' splits, a password may hold more.
      var (name, inlineValue) = argument.IndexOf('=') is var equals and > 0
        ? (argument[..equals], argument[(equals + 1)..])
        : (argument, null);

      string Value()
      {
        if (inlineValue is not null)
        {
          return inlineValue;
        }

        if (i + 1 >= args.Count)
        {
          throw new CliUsageException($"{name} needs a value.");
        }

        return args[++i];
      }

      request = name switch
      {
        "--instance" => request with { Instance = Value() },
        "--vanilla" when inlineValue is null => request with { Vanilla = true },
        "--server" => request with { Server = Value() },
        "--world" => request with { World = Value() },
        "--character" => request with { Character = Value() },
        "--password" => request with { Password = Value() },
        "--instant" when inlineValue is null => request with { Instant = true },
        "--no-instant" when inlineValue is null => request with { Instant = false },
        _ => throw new CliUsageException($"Unknown option \"{argument}\". `launchheim help` lists them."),
      };
    }

    return (request, []);
  }
}
