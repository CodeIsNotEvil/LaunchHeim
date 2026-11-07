using System.Security.Cryptography;

namespace CINE.LaunchHeim.Core.Servers;

/// <summary>Where a server runs and how LaunchHeim reaches it.</summary>
/// <remarks>
/// The plan (decision 0019): local servers, then hosts reached over FTP/SFTP for files and the plugin's
/// file channel, then G-Portal, then SSH and the hosting panels' APIs.
/// </remarks>
public enum ServerKind
{
  /// <summary>Runs on this PC; LaunchHeim starts and stops it.</summary>
  Local,

  /// <summary>
  /// Runs somewhere LaunchHeim reaches only through its files (<see cref="HostAccess"/>): a game host or a
  /// VPS. LaunchHeim deploys mods and the plugin, follows the log and sends commands through the plugin,
  /// but starting is up to the host's panel (or a shell LaunchHeim doesn't have).
  /// </summary>
  Hosted,
}

public enum FileProtocol
{
  Ftp,

  /// <summary>FTP over TLS (explicit, <c>AUTH TLS</c>), which most hosts that encrypt FTP offer.</summary>
  Ftps,

  Sftp,
}

/// <summary>How LaunchHeim reaches a hosted server's files, and where players and the console reach it.</summary>
public sealed class HostAccess
{
  public FileProtocol Protocol { get; set; } = FileProtocol.Sftp;

  /// <summary>The host name or address for FTP/SFTP, without a port.</summary>
  public string Address { get; set; } = "";

  /// <summary>The FTP/SFTP port, or 0 for the protocol's own (21, 22).</summary>
  public int Port { get; set; }

  public string User { get; set; } = "";

  public string Password { get; set; } = "";

  /// <summary>An SSH private key for SFTP instead of (or with) the password; null for none.</summary>
  public string? KeyFile { get; set; }

  /// <summary>
  /// The server's folder on the host, the one with <c>valheim_server</c> and <c>BepInEx/</c> in it.
  /// Empty for the folder the login starts in, which is where game hosts usually put it.
  /// </summary>
  public string Directory { get; set; } = "";

  /// <summary>The log the console follows, inside <see cref="Directory"/>. BepInEx's own log holds the game's lines too.</summary>
  public string LogFile { get; set; } = "BepInEx/LogOutput.log";

  /// <summary><c>host:port</c> players join, for the status (Steam's server query) and Join. Empty when not known.</summary>
  public string GameAddress { get; set; } = "";

  /// <summary>The host's web panel, where the server is started; empty for none.</summary>
  public string PanelUrl { get; set; } = "";

  /// <summary>
  /// A TCP port the host forwards to the server for RCON, or 0 to use the file channel only. Most game
  /// hosts don't give one; a VPS can.
  /// </summary>
  public int RconPort { get; set; }

  /// <summary>The host's operating system, for uploading the matching build of the server program.</summary>
  public Game.GamePlatform Platform { get; set; } = Game.GamePlatform.Linux;

  /// <summary>
  /// What the host proved itself with the first time: an SSH host key or a TLS certificate fingerprint.
  /// A different one later means another machine answers (or the host changed its key), and LaunchHeim
  /// refuses until the player clears it. Trust on first use, as SSH clients do.
  /// </summary>
  public string? Fingerprint { get; set; }

  public int EffectivePort => Port > 0 ? Port : Protocol == FileProtocol.Sftp ? 22 : 21;

  /// <summary><c>sftp://user@host:22/folder</c>, for messages and the log; never with the password.</summary>
  public string Describe() =>
    $"{Protocol.ToString().ToLowerInvariant()}://{(User.Length > 0 ? User + "@" : "")}{Address}:{EffectivePort}/{Directory.TrimStart('/')}";
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

  /// <summary>How a <see cref="ServerKind.Hosted"/> server is reached; null for a local one.</summary>
  public HostAccess? Host { get; set; }

  public bool IsHosted => Kind == ServerKind.Hosted;

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

  /// <summary>When a hosted server last got its mods and plugin uploaded.</summary>
  public DateTimeOffset? LastDeployedAt { get; set; }

  /// <summary>The RCON port in use: <see cref="RconPort"/>, or the default next to the game ports.</summary>
  public int EffectiveRconPort => RconPort > 0 ? RconPort : Port + 2;

  /// <summary>
  /// What LaunchHeim's own Join connects to, always with the port, so it matches the key Valheim's server
  /// list and the Play page use (<c>server:127.0.0.1:2456</c>) and the remembered choice is shared.
  /// </summary>
  public string ConnectAddress => IsHosted ? Host?.GameAddress ?? "" : $"127.0.0.1:{Port}";

  /// <summary>The address players join, for the Play page and the copy button.</summary>
  public string JoinAddress(string host = "127.0.0.1") =>
    IsHosted ? Host?.GameAddress ?? "" : Port == DefaultPort ? host : $"{host}:{Port}";

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

    // A hosted server's world, port and password are set at the host; only the way in matters here.
    if (IsHosted)
    {
      if (Host is null || string.IsNullOrWhiteSpace(Host.Address))
      {
        problems.Add("Enter the host's address for FTP or SFTP.");
      }
      else if (Host.Port is < 0 or > 65535 || Host.RconPort is < 0 or > 65535)
      {
        problems.Add("Ports have to be between 1 and 65535.");
      }

      return problems;
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
