using System.Security.Cryptography;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>Where a server runs and how LaunchHeim reaches it.</summary>
/// <remarks>
/// Only <see cref="Local"/> exists so far. The plan (decision 0019): hosts reached over FTP/SFTP for files
/// and the plugin's file channel, then G-Portal, then SSH and the hosting panels' APIs.
/// </remarks>
public enum ServerKind
{
  Local,
}

/// <summary>Which copy of Valheim's dedicated server a local server runs.</summary>
public enum ServerBinary
{
  /// <summary>"Valheim Dedicated Server" from the Steam library's tools (<see cref="DedicatedServer.SteamAppId"/>).</summary>
  Steam,

  /// <summary>The copy LaunchHeim downloaded with SteamCMD into its data folder.</summary>
  LaunchHeim,

  /// <summary>A folder the user picked, such as one copied from another machine.</summary>
  Custom,
}

/// <summary>One dedicated server LaunchHeim manages: <c>server.json</c> in the server's folder.</summary>
public sealed class ServerProfile
{
  public const int DefaultPort = 2456;

  /// <summary>Valheim's own default when no <c>-world</c> is given.</summary>
  public const string DefaultWorld = "Dedicated";

  /// <summary>Valheim refuses shorter passwords on public servers (<c>FejdStartup.m_minimumPasswordLength</c>).</summary>
  public const int MinimumPasswordLength = 5;

  /// <summary>The presets <c>-preset</c> takes (<c>WorldPresets</c> in the game), lower case as typed.</summary>
  public static readonly string[] Presets = ["normal", "casual", "easy", "hard", "hardcore", "immersive", "hammer"];

  /// <summary>The folder name under the servers directory. Never changes, even on rename.</summary>
  public string Id { get; set; } = "";

  /// <summary>The name in LaunchHeim and in the server browser (<c>-name</c>).</summary>
  public string Name { get; set; } = "";

  public ServerKind Kind { get; set; } = ServerKind.Local;

  public ServerBinary Binary { get; set; } = ServerBinary.Steam;

  /// <summary>The server's folder when <see cref="Binary"/> is <see cref="ServerBinary.Custom"/>.</summary>
  public string? CustomDirectory { get; set; }

  /// <summary>
  /// The instance whose mods and configs the server runs, so players and server stay in step: synced
  /// into the server's own folder before each start (<see cref="ServerMods"/>). Null for vanilla.
  /// </summary>
  public string? InstanceId { get; set; }

  /// <summary>
  /// Mods of the instance switched on or off for the server, by key, overriding the default: everything
  /// except mods Thunderstore only lists as Client-side (<see cref="ServerMods.IsClientOnly"/>).
  /// </summary>
  public Dictionary<string, bool> ModChoices
  {
    get => _modChoices;
    // The JSON reader builds its own dictionary, with case-sensitive keys; mod keys aren't.
    set => _modChoices = new Dictionary<string, bool>(value, StringComparer.OrdinalIgnoreCase);
  }

  private Dictionary<string, bool> _modChoices = new(StringComparer.OrdinalIgnoreCase);

  public string World { get; set; } = DefaultWorld;

  public int Port { get; set; } = DefaultPort;

  public string Password { get; set; } = "";

  /// <summary>
  /// Listed in the game's community server browser (<c>-public 1</c>). Off by default: friends join by
  /// address, and a private server accepts any password, an empty one included. Valheim's own default
  /// is on, so the argument is always passed.
  /// </summary>
  public bool Public { get; set; }

  /// <summary>Joinable from the Xbox and Game Pass versions too (<c>-crossplay</c>, PlayFab instead of Steam's network).</summary>
  public bool Crossplay { get; set; }

  /// <summary>One of <see cref="Presets"/>, or empty for the world's own settings.</summary>
  public string Preset { get; set; } = "";

  /// <summary>Seconds between world saves (<c>-saveinterval</c>, Valheim's default 1800).</summary>
  public int SaveInterval { get; set; } = 1800;

  /// <summary>How many automatic backups of the world Valheim keeps (<c>-backups</c>, its default 4).</summary>
  public int Backups { get; set; } = 4;

  /// <summary>
  /// Where the server keeps its worlds and admin lists (<c>-savedir</c>). Null for the server's own
  /// <c>saves</c> folder, so it never shares a world with the game or another server by accident.
  /// </summary>
  public string? SaveDirectory { get; set; }

  public string ExtraArguments { get; set; } = "";

  /// <summary>Whether the server gets LaunchHeim's server plugin, which gives it a console (decision 0020).</summary>
  public bool ConsoleEnabled { get; set; } = true;

  /// <summary>The plugin's RCON port (TCP), or 0 for the game port + 2, the first one Valheim doesn't use.</summary>
  public int RconPort { get; set; }

  /// <summary>
  /// The RCON password, made up by LaunchHeim. The plugin only listens on localhost for a local server,
  /// so it guards against other users on the machine rather than the internet.
  /// </summary>
  public string RconPassword { get; set; } = NewSecret();

  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

  public DateTimeOffset? LastStartedAt { get; set; }

  /// <summary>The RCON port in use: <see cref="RconPort"/>, or the default next to the game ports.</summary>
  public int EffectiveRconPort => RconPort > 0 ? RconPort : Port + 2;

  /// <summary>The address players join, for the Play page and the copy button.</summary>
  public string JoinAddress(string host = "127.0.0.1") => Port == DefaultPort ? host : $"{host}:{Port}";

  /// <summary>What stops Valheim from starting with these settings, or what it would refuse, in plain words.</summary>
  /// <remarks>
  /// The password rules are the game's (<c>FejdStartup.IsPublicPasswordValid</c>): only checked for public
  /// servers, at least five characters, and not part of the <em>world's</em> name. A server that breaks
  /// them logs "Error bad password" and quits right away.
  /// </remarks>
  public IReadOnlyList<string> Problems()
  {
    var problems = new List<string>();
    if (string.IsNullOrWhiteSpace(Name))
    {
      problems.Add("The server needs a name.");
    }

    if (string.IsNullOrWhiteSpace(World))
    {
      problems.Add("The server needs a world name.");
    }
    else if (World.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\']) >= 0)
    {
      problems.Add("The world name can't contain slashes or other characters files can't have.");
    }

    if (Port is < 1 or > 65534)
    {
      problems.Add("The port has to be between 1 and 65534 (the server also uses the port after it).");
    }

    if (Public)
    {
      if (Password.Length < MinimumPasswordLength)
      {
        problems.Add($"A public server needs a password of at least {MinimumPasswordLength} characters.");
      }
      else if (World.Contains(Password, StringComparison.Ordinal))
      {
        problems.Add("Valheim refuses a password that is part of the world's name.");
      }
    }

    if (Preset.Length > 0 && !Presets.Contains(Preset, StringComparer.OrdinalIgnoreCase))
    {
      problems.Add($"Unknown world preset \"{Preset}\".");
    }

    if (ConsoleEnabled && EffectiveRconPort is < 1 or > 65535)
    {
      problems.Add("The console port has to be between 1 and 65535.");
    }

    if (Binary == ServerBinary.Custom && string.IsNullOrWhiteSpace(CustomDirectory))
    {
      problems.Add("Choose the folder of the dedicated server.");
    }

    return problems;
  }

  /// <summary>A random password for RCON: 24 characters nobody has to type.</summary>
  public static string NewSecret() =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');
}
