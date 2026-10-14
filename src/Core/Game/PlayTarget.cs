using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Saves;
using CINE.LaunchHeim.Core.Storage;

namespace CINE.LaunchHeim.Core.Game;

/// <summary>What a launch should open in the game: a server to join, or a character and world to select.</summary>
/// <param name="Key">The server or world's key in <see cref="AppSettings.PlayChoices"/>, to play it again later.</param>
/// <param name="Name">The server or world, for the log and the sidebar.</param>
/// <param name="Arguments">Command-line arguments for the game, see <see cref="GameLauncher.JoinArguments"/>.</param>
/// <param name="Prefs">PlayerPrefs to set first, see <see cref="ValheimPrefs"/>.</param>
public sealed record DirectPlay(string Key, string Name, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Prefs)
{
  /// <summary>Joins a server. The character is set too: <c>+connect</c> opens the character selection, which starts on it.</summary>
  public static DirectPlay Server(string key, string name, string address, string character, string? password) =>
    new(key, name, GameLauncher.JoinArguments(address, password), new Dictionary<string, string> { [ValheimPrefs.Character] = character });

  /// <summary>Selects a character and world in the menus. The game has no option to load the world itself.</summary>
  public static DirectPlay World(string key, string name, string character) =>
    new(key, name, [], new Dictionary<string, string> { [ValheimPrefs.Character] = character, [ValheimPrefs.World] = name });

  /// <summary>Sets the character and world Valheim's menus start on, in the game's PlayerPrefs.</summary>
  /// <remarks>
  /// Right before the start, because Unity reads PlayerPrefs once when the game starts and writes all of
  /// them back when it quits.
  /// </remarks>
  /// <param name="dataDirectory">The game's data folder, <see cref="ValheimSaves.DataDirectory"/>.</param>
  public void SelectInGame(string dataDirectory)
  {
    var prefs = UnityPrefs.ForValheim(dataDirectory);
    foreach (var (key, value) in Prefs)
    {
      prefs.SetString(key, value);
    }
  }
}

/// <summary>What to play, as the command line asks for it. Nothing set means what was played last.</summary>
public sealed record PlayRequest
{
  /// <summary>An instance's name or id.</summary>
  public string? Instance { get; init; }

  public bool Vanilla { get; init; }

  /// <summary>A server's name in Valheim's lists, or any <c>host:port</c>.</summary>
  public string? Server { get; init; }

  public string? World { get; init; }

  /// <summary>A character's name as the game shows it, or its file name.</summary>
  public string? Character { get; init; }

  public string? Password { get; init; }
}

/// <summary>A resolved launch: the setup, and the server or world with the choice to remember for it.</summary>
/// <param name="Instance">The instance to play, or null for vanilla.</param>
/// <param name="Direct">The server or world, or null to start at the main menu.</param>
/// <param name="Choice">What to store in <see cref="AppSettings.PlayChoices"/> under the destination's key once the game runs.</param>
public sealed record PlayTarget(Instance? Instance, DirectPlay? Direct, PlayChoice? Choice)
{
  public static readonly PlayTarget VanillaGame = new(null, null, null);

  /// <summary>"SoloHard · Vanilla Valheim", like the sidebar names it.</summary>
  public string Describe() => Direct is null ? SetupName : $"{Direct.Name} · {SetupName}";

  private string SetupName => Instance?.Name ?? PlayTargets.VanillaName;

  /// <summary>The same launch without its server or world, for a start Steam already gave a server to join.</summary>
  public PlayTarget WithoutDestination() => this with { Direct = null, Choice = null };

  /// <summary>
  /// What <see cref="AppSettings.LastPlay"/> becomes once this launch is running, so the sidebar and the
  /// next start from Steam play it again.
  /// </summary>
  public LastPlay ToLastPlay() => new()
  {
    DestinationKey = Direct?.Key,
    DestinationName = Direct?.Name,
    InstanceId = Direct is null ? Instance?.Id : null,
  };
}

/// <summary>Everything a launch can be picked from: LaunchHeim's instances and Valheim's own lists.</summary>
public sealed record PlayLibrary(
  IReadOnlyList<Instance> Instances,
  IReadOnlyList<ValheimCharacter> Characters,
  IReadOnlyList<ValheimServer> Servers,
  IReadOnlyList<ValheimWorld> Worlds)
{
  public static PlayLibrary Read(IReadOnlyList<Instance> instances, ValheimSaves saves) =>
    new(instances, saves.Characters(), saves.Servers(), saves.Worlds());
}

/// <summary>
/// Turns a <see cref="PlayRequest"/> into a launch with the window's rules, so a start from the command
/// line or from Steam plays exactly what the window would.
/// </summary>
/// <remarks>
/// <para>
/// An empty request is the sidebar's Play button: <see cref="AppSettings.LastPlay"/>, a server or world
/// with its remembered character, setup and password, else the instance played last, else vanilla. An
/// instance deleted since falls back to vanilla, as in the window. A server that dropped off Valheim's
/// Recent list is still joined by the address in its key; a world has to exist.
/// </para>
/// <para>
/// A server or world on its own uses its remembered choice. Each part of it can be given instead, and a
/// server or world played the first time needs both a character and a setup (<c>--instance</c> or
/// <c>--vanilla</c>): guessing could open a modded world in vanilla, which drops the modded items when it
/// saves.
/// </para>
/// </remarks>
public static class PlayTargets
{
  public const string VanillaName = "Vanilla Valheim";

  public static PlayTarget Resolve(PlayRequest request, AppSettings settings, PlayLibrary library)
  {
    if (request.Vanilla && request.Instance is not null)
    {
      throw new LaunchException("Pick either --instance or --vanilla, not both.");
    }

    if (request.Server is not null && request.World is not null)
    {
      throw new LaunchException("Pick either --server or --world, not both.");
    }

    if (request.Server is null && request.World is null)
    {
      if (request.Character is not null || request.Password is not null)
      {
        throw new LaunchException("--character and --password need a --server or --world.");
      }

      if (request.Vanilla)
      {
        return PlayTarget.VanillaGame;
      }

      return request.Instance is { } name ? new PlayTarget(FindInstance(library, name), null, null) : LastPlay(settings, library);
    }

    var (key, destinationName, address) = request.Server is { } server ? FindServer(library, server) : FindWorld(library, request.World!);
    return ForDestination(key, destinationName, address, settings.PlayChoices.GetValueOrDefault(key), request, library);
  }

  private static PlayTarget LastPlay(AppSettings settings, PlayLibrary library)
  {
    if (settings.LastPlay is not { } last)
    {
      return PlayTarget.VanillaGame;
    }

    if (last.DestinationKey is not { } key)
    {
      return new PlayTarget(library.Instances.FirstOrDefault(i => i.Id == last.InstanceId), null, null);
    }

    string name, address;
    if (key.StartsWith(ServerPrefix, StringComparison.Ordinal))
    {
      var server = library.Servers.FirstOrDefault(s => s.Key == key);
      address = server?.Address ?? key[ServerPrefix.Length..];
      name = server?.Name ?? last.DestinationName ?? address;
    }
    else
    {
      name = library.Worlds.FirstOrDefault(w => w.Key == key)?.Name
        ?? throw new LaunchException($"{last.DestinationName ?? "The world played last"} is gone from Valheim's worlds. Pick another in LaunchHeim.");
      address = "";
    }

    var choice = settings.PlayChoices.GetValueOrDefault(key)
      ?? throw new LaunchException($"Nothing is remembered for {name}. Play it once from LaunchHeim's Play page.");
    return ForDestination(key, name, address, choice, new PlayRequest(), library);
  }

  private static PlayTarget ForDestination(string key, string name, string address, PlayChoice? remembered, PlayRequest request, PlayLibrary library)
  {
    var character = request.Character is { } characterName
      ? FindCharacter(library, characterName)
      : remembered is null
        ? throw new LaunchException($"{name} wasn't played from LaunchHeim yet. Give a character with --character.")
        : library.Characters.FirstOrDefault(c => c.FileName == remembered.Character)
          ?? throw new LaunchException($"The character played last on {name} is gone. Give one with --character.");

    Instance? instance;
    if (request.Vanilla)
    {
      instance = null;
    }
    else if (request.Instance is { } instanceName)
    {
      instance = FindInstance(library, instanceName);
    }
    else if (remembered is null)
    {
      throw new LaunchException($"{name} wasn't played from LaunchHeim yet. Give a setup with --instance or --vanilla.");
    }
    else
    {
      // A deleted instance is no choice, rather than silently going vanilla (see PlayTargets).
      instance = remembered.InstanceId is null
        ? null
        : library.Instances.FirstOrDefault(i => i.Id == remembered.InstanceId)
          ?? throw new LaunchException($"The instance played last on {name} is gone. Give a setup with --instance or --vanilla.");
    }

    var isServer = key.StartsWith(ServerPrefix, StringComparison.Ordinal);
    // An empty password from the command line clears the remembered one, as an empty field does in the window.
    var password = isServer ? (request.Password ?? remembered?.Password) : null;
    if (string.IsNullOrEmpty(password))
    {
      password = null;
    }

    var direct = isServer
      ? DirectPlay.Server(key, name, address, character.FileName, password)
      : DirectPlay.World(key, name, character.FileName);
    var choice = new PlayChoice
    {
      Character = character.FileName,
      InstanceId = instance?.Id,
      Password = password,
      LastPlayedAt = DateTimeOffset.UtcNow,
    };
    return new PlayTarget(instance, direct, choice);
  }

  private const string ServerPrefix = "server:";

  private static Instance FindInstance(PlayLibrary library, string name)
  {
    if (library.Instances.FirstOrDefault(i => i.Id == name) is { } byId)
    {
      return byId;
    }

    return Single(library.Instances.Where(i => Same(i.Name, name)).ToList(), "instance", name, "Use its id from `launchheim list`.");
  }

  private static (string Key, string Name, string Address) FindServer(PlayLibrary library, string name)
  {
    var matches = library.Servers.Where(s => Same(s.Name, name) || Same(s.Address, name)).DistinctBy(s => s.Key).ToList();
    if (matches.Count == 0 && name.Contains(':'))
    {
      // Any address can be joined, not only the ones in Valheim's lists.
      var server = new ValheimServer(name.Trim(), name.Trim(), false, false);
      return (server.Key, server.Name, server.Address);
    }

    var found = Single(matches, "server", name, "Use its address.");
    return (found.Key, found.Name, found.Address);
  }

  private static (string Key, string Name, string Address) FindWorld(PlayLibrary library, string name)
  {
    // The same world may be saved in the cloud and locally; they share a key, which is what counts.
    var found = Single(library.Worlds.Where(w => Same(w.Name, name)).DistinctBy(w => w.Key).ToList(), "world", name, "");
    return (found.Key, found.Name, "");
  }

  private static ValheimCharacter FindCharacter(PlayLibrary library, string name) =>
    Single(library.Characters.Where(c => Same(c.FileName, name) || Same(c.DisplayName, name)).DistinctBy(c => c.FileName).ToList(), "character", name, "");

  private static T Single<T>(IReadOnlyList<T> matches, string kind, string name, string hint) => matches.Count switch
  {
    1 => matches[0],
    0 => throw new LaunchException($"No {kind} called \"{name}\". `launchheim list` shows them all."),
    _ => throw new LaunchException($"Several {kind}s are called \"{name}\". {hint}".TrimEnd()),
  };

  private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
