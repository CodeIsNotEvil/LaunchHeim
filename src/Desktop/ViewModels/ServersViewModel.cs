using System.Text.Json;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Saves;
using CINE.LaunchHeim.Core.Servers;
using CINE.LaunchHeim.Core.Servers.Backups;
using CINE.LaunchHeim.Core.Storage;
using CINE.LaunchHeim.Desktop.Hosting;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>The Servers page: the dedicated servers LaunchHeim runs on this machine (<see cref="ServerManager"/>).</summary>
/// <remarks>
/// Status is asked for while the page is open (QML's timer calls <see cref="RefreshStatus"/>) and after
/// every start or stop, not watched: a server's process and its Steam query are cheap to look at, and a
/// server started or stopped elsewhere shows up on the next look.
/// </remarks>
[Signal("joinDialogRequested")]
public sealed class ServersViewModel(AppViewModel app, ServerManager manager, SteamCmd steamCmd, HttpClient http) : ViewModel
{
  /// <summary>Separates the names in the combo box models, as the Play page does.</summary>
  internal const string Separator = "\u001f";

  private List<ServerViewModel> _items = [];
  private ServerViewModel? _selected;
  private bool _loaded;
  private bool _isDownloading;
  private bool _refreshing;
  private IReadOnlyList<LocalAddress> _localAddresses = [];
  private string _publicAddress = "";
  private bool _isFindingPublic;

  internal AppViewModel App => app;
  internal ServerManager Manager => manager;

  /// <summary>Backs servers up (Core/Servers/Backups); one at a time for the whole app.</summary>
  internal BackupService Backups { get; } = new(manager, app.Paths);

  /// <summary>Held while any backup or restore runs, so the schedule and a click don't overlap.</summary>
  internal SemaphoreSlim BackupGate { get; } = new(1, 1);

  private bool _schedulerStarted;

  /// <summary>
  /// Checks every minute whether a server's scheduled backup is due, while LaunchHeim runs. Only a
  /// server that runs is backed up on schedule: a stopped one hasn't changed since its last backup
  /// (its stop made one, <see cref="BackupSettings.AfterStop"/>). With the window closed, the command
  /// line does it (<c>launchheim backup --all</c> in a systemd timer or a scheduled task).
  /// </summary>
  internal void StartBackupScheduler()
  {
    if (_schedulerStarted)
    {
      return;
    }

    _schedulerStarted = true;
    if (!_loaded)
    {
      Reload();
    }

    app.Guard("Scheduled backups stopped", async () =>
    {
      using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
      while (await timer.WaitForNextTickAsync())
      {
        foreach (var server in _items.ToList())
        {
          var settings = server.Model.Backup;
          if (!settings.Scheduled || settings.LastBackupAt is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(settings.IntervalHours))
          {
            continue;
          }

          try
          {
            if ((await manager.StateAsync(server.Model, CancellationToken.None)).Running)
            {
              await server.BackupAsync("scheduled", saveFirst: true, quiet: true);
            }
          }
          catch (Exception ex)
          {
            // One server's trouble mustn't stop the others' schedule.
            Log.Error($"Scheduled backup of {server.Name} failed", ex);
          }
        }
      }
    });
  }

  [NotifySignal]
  public List<ServerViewModel> Items { get => _items; private set => Set(ref _items, value); }

  [NotifySignal]
  public int Count => _items.Count;

  [NotifySignal]
  public int RunningCount => _items.Count(s => s.IsRunning);

  /// <summary>Servers running but not joinable yet; the pages poll faster while there are any.</summary>
  [NotifySignal]
  public int StartingCount => _items.Count(s => s.IsStarting);

  /// <summary>Asks the open server's page for its Join dialog, for a Join in the list before a character was picked.</summary>
  public void RequestJoinDialog() => this.ActivateSignal("joinDialogRequested");

  [NotifySignal]
  public ServerViewModel? Selected { get => _selected; private set => Set(ref _selected, value); }

  /// <summary>Steam has "Valheim Dedicated Server" installed.</summary>
  [NotifySignal]
  public bool SteamServerFound => manager.Dedicated.FindInSteam() is not null;

  /// <summary>LaunchHeim's own SteamCMD download is there.</summary>
  [NotifySignal]
  public bool DownloadedServerFound => manager.Dedicated.FindDownloaded() is not null;

  [NotifySignal]
  public bool IsDownloading { get => _isDownloading; private set => Set(ref _isDownloading, value); }

  /// <summary>"Vanilla" and the instances' names, for the "Mods from" combo box.</summary>
  [NotifySignal]
  public string InstanceChoices => string.Join(Separator, [PlayViewModel.VanillaName, .. app.InstanceList.Select(i => i.Name)]);

  /// <summary>The ids in the order of <see cref="InstanceChoices"/>, empty for vanilla.</summary>
  [NotifySignal]
  public string InstanceChoiceIds => string.Join(Separator, ["", .. app.InstanceList.Select(i => i.Id)]);

  internal void EnsureLoaded()
  {
    if (!_loaded)
    {
      Reload();
    }

    RefreshAddresses();
    RefreshStatus();
  }

  /// <summary>This PC's addresses on its networks, read when the pages open (a laptop may have moved).</summary>
  internal IReadOnlyList<LocalAddress> LocalAddresses => _localAddresses;

  /// <summary>This network's internet address once looked up, else empty.</summary>
  [NotifySignal]
  public string PublicAddress { get => _publicAddress; private set => Set(ref _publicAddress, value); }

  [NotifySignal]
  public bool IsFindingPublic { get => _isFindingPublic; private set => Set(ref _isFindingPublic, value); }

  private void RefreshAddresses()
  {
    _localAddresses = ServerAddresses.Local();
    foreach (var server in _items)
    {
      server.RefreshAddresses();
    }
  }

  /// <summary>Asks an outside service for the internet address; only on the player's click (<see cref="ServerAddresses"/>).</summary>
  public void FindPublicAddress() => app.Guard("Couldn't find your internet address", FindPublicAddressAsync);

  private async Task FindPublicAddressAsync()
  {
    if (IsFindingPublic)
    {
      return;
    }

    IsFindingPublic = true;
    try
    {
      PublicAddress = (await ServerAddresses.FindPublicAsync(http, CancellationToken.None)).ToString();
      foreach (var server in _items)
      {
        server.RefreshAddresses();
      }
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
      app.Toast("error", "Couldn't find your internet address", ex.Message);
    }
    finally
    {
      IsFindingPublic = false;
    }
  }

  private void Reload()
  {
    _loaded = true;
    Items = manager.Store.LoadAll().Select(s => new ServerViewModel(this, s)).ToList();
    if (_selected is not null)
    {
      Selected = _items.FirstOrDefault(s => s.Id == _selected.Id);
    }

    RaiseAll();
  }

  private void RaiseAll()
  {
    Raise(nameof(Count));
    Raise(nameof(RunningCount));
    Raise(nameof(SteamServerFound));
    Raise(nameof(DownloadedServerFound));
    Raise(nameof(InstanceChoices));
    Raise(nameof(InstanceChoiceIds));
  }

  internal void InstancesChanged()
  {
    Raise(nameof(InstanceChoices));
    Raise(nameof(InstanceChoiceIds));
    foreach (var server in _items)
    {
      server.RefreshMods();
    }
  }

  public void RefreshStatus() => app.Guard("Couldn't check the servers", RefreshStatusAsync);

  private async Task RefreshStatusAsync()
  {
    if (_refreshing)
    {
      return;
    }

    _refreshing = true;
    try
    {
      foreach (var server in _items.ToList())
      {
        await server.RefreshStateAsync();
      }

      Raise(nameof(RunningCount));
      Raise(nameof(StartingCount));
      Raise(nameof(SteamServerFound));
      Raise(nameof(DownloadedServerFound));
    }
    finally
    {
      _refreshing = false;
    }

    // The server program may have appeared since (Steam finished installing it), which enables Start.
    foreach (var server in _items)
    {
      server.RaiseBinary();
    }
  }

  public void Open(string? id)
  {
    if (_items.FirstOrDefault(s => s.Id == id) is { } server)
    {
      Selected = server;
      server.RefreshMods();
      app.Navigate("server");
      // The characters for Join.
      _ = app.Play.EnsureLoadedAsync();
      server.RefreshBackups();
    }
  }

  /// <param name="instanceId">The instance whose mods it runs, or empty for vanilla.</param>
  /// <param name="hosted">At a game host or VPS reached over FTP/SFTP, rather than on this PC.</param>
  public void Create(string? name, string? instanceId, bool hosted)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return;
    }

    try
    {
      var profile = manager.Store.Create(name);
      profile.World = ServerProfile.DefaultWorld;
      profile.InstanceId = string.IsNullOrEmpty(instanceId) ? null : instanceId;
      if (hosted)
      {
        profile.Kind = ServerKind.Hosted;
        profile.Host = new HostAccess();
      }

      // A server that LaunchHeim downloaded wins once there is one, since it's there for this purpose.
      profile.Binary = manager.Dedicated.FindInSteam() is null && manager.Dedicated.FindDownloaded() is not null ? ServerBinary.LaunchHeim : ServerBinary.Steam;
      manager.Store.Save(profile);
      Reload();
      Open(profile.Id);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      app.Toast("error", "Could not create the server", ex.Message);
    }
  }

  internal void Delete(ServerViewModel server)
  {
    // A hosted server only leaves LaunchHeim; nothing at the host is touched.
    if (server.IsRunning && !server.IsHosted)
    {
      app.Toast("error", "Still running", "Stop the server before deleting it.");
      return;
    }

    try
    {
      manager.Store.Delete(server.Model);
      if (Selected == server)
      {
        Selected = null;
        app.Navigate("servers");
      }

      Reload();
      app.Toast("success", "Deleted", $"{server.Name} and its folder were removed.");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      app.Toast("error", "Could not delete", ex.Message);
    }
  }

  /// <summary>Opens Steam's install dialog for Valheim Dedicated Server.</summary>
  /// <remarks>
  /// The page notices the finished install on its next status refresh, since the Steam copy is looked
  /// up each time (<see cref="DedicatedServer.FindInSteam"/>).
  /// </remarks>
  public void InstallWithSteam()
  {
    if (DesktopShell.OpenSteamLink(DedicatedServer.SteamInstallUrl))
    {
      app.Toast("info", "Steam's install dialog should open", "Install Valheim Dedicated Server there. Servers using Steam's copy can start once Steam is done.");
    }
    else
    {
      app.Toast("error", "Couldn't reach Steam", "Install Valheim Dedicated Server from Steam yourself (Library → Tools), or let LaunchHeim download it.");
    }
  }

  /// <summary>Downloads or updates LaunchHeim's own copy with SteamCMD.</summary>
  public void DownloadServer() => app.Guard("Could not download the server", () => DownloadAsync(GamePlatforms.Current));

  /// <summary>
  /// LaunchHeim's download of the server for a platform, downloaded first when it isn't there: what gets
  /// uploaded to a VPS. Null when the download failed (a toast said why).
  /// </summary>
  internal async Task<string?> EnsureProgramAsync(GamePlatform platform)
  {
    var directory = manager.Dedicated.DownloadDirectory(platform);
    if (DedicatedServer.IsServerDirectory(directory, platform) || await DownloadAsync(platform))
    {
      return directory;
    }

    return null;
  }

  private async Task<bool> DownloadAsync(GamePlatform platform)
  {
    if (IsDownloading)
    {
      return false;
    }

    IsDownloading = true;
    var activity = app.BeginActivity("Valheim dedicated server");
    var progress = new Progress<SteamCmdProgress>(p =>
    {
      activity.Detail = p.Stage;
      activity.Progress = p.Fraction ?? -1;
    });

    try
    {
      await Task.Run(() => steamCmd.InstallServerAsync(manager.Dedicated.DownloadDirectory(platform), platform, progress, CancellationToken.None));
      app.Toast("success", "Dedicated server ready", platform == GamePlatforms.Current
        ? "Servers set to LaunchHeim's download can start now."
        : $"The {platform} build is downloaded for uploading.");
      return true;
    }
    catch (Exception ex) when (ex is SteamCmdException or HttpRequestException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidDataException)
    {
      Log.Error("Downloading the dedicated server failed", ex);
      app.Toast("error", "Could not download the server", ex.Message);
      return false;
    }
    finally
    {
      app.EndActivity(activity);
      IsDownloading = false;
      RaiseAll();
      foreach (var server in _items)
      {
        server.RaiseBinary();
      }
    }
  }

  /// <summary>The server a Play-page key (<c>server:127.0.0.1:2456</c>) belongs to, for the sidebar's Play again.</summary>
  internal ServerViewModel? FindByJoinKey(string key)
  {
    if (!_loaded)
    {
      Reload();
    }

    return _items.FirstOrDefault(s => s.JoinKey == key);
  }

  /// <summary>Valheim's characters were read (again); the Join buttons depend on them.</summary>
  internal void CharactersChanged()
  {
    foreach (var server in _items)
    {
      server.RaiseJoin();
    }
  }

  /// <summary>Problems as QML reads them: <c>{"field": "message"}</c>, the first problem per field.</summary>
  internal static string ErrorsJson(IEnumerable<FieldProblem> problems) =>
    JsonSerializer.Serialize(problems.GroupBy(p => p.Field).ToDictionary(g => g.Key, g => g.First().Message));

  internal static string ChoiceKey(ServerBinary binary) => binary switch
  {
    ServerBinary.LaunchHeim => "launchheim",
    ServerBinary.Custom => "custom",
    _ => "steam",
  };

  internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
}

/// <summary>One server: its card on the Servers page and its own page.</summary>
/// <remarks><c>settingsIncomplete</c>: an action needed settings that are missing; the page outlines them.</remarks>
[Signal("settingsIncomplete")]
public sealed class ServerViewModel : ViewModel
{
  private readonly ServersViewModel _servers;
  private List<ServerModViewModel> _mods = [];
  private bool _isRunning;
  private bool _isOnline;
  private int _players;
  private int _maxPlayers;
  private bool _isBusy;
  private string _busyText = "";
  private ServerStartPhase _phase = ServerStartPhase.Starting;
  private List<ServerAddressViewModel> _addresses = [];
  private string _joinCode = "";
  private DateTimeOffset? _startingSince;

  public ServerViewModel(ServersViewModel servers, ServerProfile model)
  {
    _servers = servers;
    Model = model;
    RefreshMods();
  }

  internal ServerProfile Model { get; }

  private ServerManager Manager => _servers.Manager;

  private AppViewModel App => _servers.App;

  [NotifySignal]
  public string Id => Model.Id;

  [NotifySignal]
  public string Name => Model.Name;

  [NotifySignal]
  public string World => Model.World;

  [NotifySignal]
  public int Port => Model.Port;

  [NotifySignal]
  public string Password => Model.Password;

  /// <summary>Listed in the server browser. Not called Public: QML's JavaScript reserves the word.</summary>
  [NotifySignal]
  public bool IsPublic => Model.Public;

  [NotifySignal]
  public bool Crossplay => Model.Crossplay;

  [NotifySignal]
  public string Preset => Model.Preset;

  [NotifySignal]
  public int SaveInterval => Model.SaveInterval;

  [NotifySignal]
  public int Backups => Model.Backups;

  [NotifySignal]
  public string ExtraArguments => Model.ExtraArguments;

  [NotifySignal]
  public bool ConsoleEnabled => Model.ConsoleEnabled;

  [NotifySignal]
  public int RconPort => Model.EffectiveRconPort;

  /// <summary>steam, launchheim or custom.</summary>
  [NotifySignal]
  public string Binary => ServersViewModel.ChoiceKey(Model.Binary);

  [NotifySignal]
  public string CustomDirectory => Model.CustomDirectory ?? "";

  /// <summary>Where it saves its worlds, for the settings and "Open saves".</summary>
  [NotifySignal]
  public string SaveDirectory => Manager.Store.SaveDirectoryOf(Model);

  [NotifySignal]
  public bool SaveDirectoryIsCustom => !string.IsNullOrWhiteSpace(Model.SaveDirectory);

  /// <summary>The dedicated server's folder it runs from, or empty when that copy isn't there.</summary>
  [NotifySignal]
  public string ServerDirectory => Manager.Dedicated.DirectoryFor(Model) ?? "";

  [NotifySignal]
  public string ServerMissingText => DedicatedServer.MissingText(Model.Binary);

  [NotifySignal]
  public string InstanceId => Model.InstanceId ?? "";

  [NotifySignal]
  public string InstanceName => Model.InstanceId is null ? PlayViewModel.VanillaName
    : App.InstanceList.FirstOrDefault(i => i.Id == Model.InstanceId)?.Name ?? "a deleted instance";

  [NotifySignal]
  public bool InstanceMissing => Model.InstanceId is not null && App.InstanceList.All(i => i.Id != Model.InstanceId);

  /// <summary>What players type into "Join IP" on this machine's network; another PC uses this PC's address.</summary>
  [NotifySignal]
  public string JoinAddress => Model.JoinAddress();

  /// <summary>Why it can't start as set up, one problem per line; empty when it can.</summary>
  [NotifySignal]
  public string Problems => string.Join("\n", Model.Problems());

  [NotifySignal]
  public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

  [NotifySignal]
  public bool IsOnline { get => _isOnline; private set => Set(ref _isOnline, value); }

  [NotifySignal]
  public int Players { get => _players; private set => Set(ref _players, value); }

  [NotifySignal]
  public int MaxPlayers { get => _maxPlayers; private set => Set(ref _maxPlayers, value); }

  [NotifySignal]
  public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

  [NotifySignal]
  public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }

  /// <summary>One line for the card and the header.</summary>
  [NotifySignal]
  public string StatusText => _isBusy ? _busyText
    // A hosted server that doesn't answer may be down, starting, or unreachable from here.
    : !_isRunning ? (Model.IsHosted ? "Offline or not answering" : "Stopped")
    : !_isOnline ? StartPhaseText
    : _players < 0 ? "Online"
    : $"Online · {_players} of {_maxPlayers} players";

  /// <summary>Running but not joinable yet: the page shows the start's progress instead of Join.</summary>
  [NotifySignal]
  public bool IsStarting => _isRunning && !_isOnline;

  /// <summary>What the start is doing, read from the server's log (<see cref="ServerStartup"/>).</summary>
  [NotifySignal]
  public string StartPhaseText => _phase switch
  {
    ServerStartPhase.LoadingMods => "Starting: loading mods",
    ServerStartPhase.LoadingWorld => $"Starting: loading the world {Model.World}",
    ServerStartPhase.CreatingWorld => $"Starting: creating the world {Model.World} (the first start takes longest)",
    ServerStartPhase.Online => "Starting: connecting to Steam",
    _ => "Starting the server",
  };

  /// <summary>1 to <see cref="StartSteps"/>, for the progress bar.</summary>
  [NotifySignal]
  public int StartStep => ServerStartup.Step(_phase);

  [NotifySignal]
  public int StartSteps => ServerStartup.Steps;

  /// <summary>How long the start has taken so far ("0:42"), counted from when LaunchHeim saw it start.</summary>
  [NotifySignal]
  public string StartElapsed => _startingSince is { } since
    ? (DateTimeOffset.UtcNow - since).ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture)
    : "";

  [NotifySignal]
  public List<ServerModViewModel> Mods { get => _mods; private set => Set(ref _mods, value); }

  [NotifySignal]
  public int IncludedModCount => _mods.Count(m => m.Included);

  [NotifySignal]
  public string ModSummary => Model.InstanceId is null ? "Vanilla"
    : $"{InstanceName} · {IncludedModCount} of {_mods.Count} mods";

  /// <summary>Where other players join from: this PC's address on each of its networks, with the port.</summary>
  [NotifySignal]
  public List<ServerAddressViewModel> Addresses { get => _addresses; private set => Set(ref _addresses, value); }

  /// <summary>
  /// The address for players from the internet, once looked up: the router's, with the port that has to
  /// be forwarded to this PC. Empty before.
  /// </summary>
  [NotifySignal]
  public string InternetAddress => _servers.PublicAddress.Length > 0 ? $"{_servers.PublicAddress}:{Model.Port}" : "";

  /// <summary>The PC's address on its home network, for the port-forwarding hint.</summary>
  [NotifySignal]
  public string ForwardTo => _servers.LocalAddresses.FirstOrDefault(a => a.Scope == AddressScope.LocalNetwork)?.Address.ToString() ?? "this PC";

  /// <summary>A crossplay server's code for Xbox and Game Pass players, from its log; empty otherwise.</summary>
  [NotifySignal]
  public string JoinCode { get => _joinCode; private set => Set(ref _joinCode, value); }

  internal void RefreshAddresses()
  {
    Addresses = _servers.LocalAddresses.Select(a => new ServerAddressViewModel(a, Model.Port)).ToList();
    Raise(nameof(ForwardTo));
    Raise(nameof(InternetAddress));
  }

  /// <summary>The local server's log file (the crossplay join code is read from it).</summary>
  internal string LogFile => Manager.Store.LogFileOf(Model);

  /// <summary>The log the console's Server tab follows: the local file, or the host's through its files.</summary>
  internal Core.Logging.ILogSource LogSource => Manager.LogSourceOf(Model);

  // A hosted server: how it's reached, and what's there.
  private HostAccess Host => Model.Host ??= new HostAccess();
  private string _hostStatus = "";
  private string _deployText = "";
  private bool _isDeploying;
  private double _deployProgress = -1;

  [NotifySignal]
  public bool IsHosted => Model.IsHosted;

  /// <summary>ftp, ftps or sftp.</summary>
  [NotifySignal]
  public string HostProtocol => Host.Protocol.ToString().ToLowerInvariant();

  [NotifySignal]
  public string HostAddress => Host.Address;

  [NotifySignal]
  public int HostPort => Host.Port;

  [NotifySignal]
  public string HostUser => Host.User;

  [NotifySignal]
  public string HostPassword => Host.Password;

  [NotifySignal]
  public string HostKeyFile => Host.KeyFile ?? "";

  [NotifySignal]
  public string HostDirectory => Host.Directory;

  [NotifySignal]
  public string HostLogFile => Host.LogFile;

  [NotifySignal]
  public string GameAddress => Host.GameAddress;

  [NotifySignal]
  public string PanelUrl => Host.PanelUrl;

  [NotifySignal]
  public int HostRconPort => Host.RconPort;

  /// <summary>linux or windows: which build of the server program to upload.</summary>
  [NotifySignal]
  public string HostPlatform => Host.Platform == GamePlatform.Windows ? "windows" : "linux";

  /// <summary>The host key or certificate pinned on first contact; empty before.</summary>
  [NotifySignal]
  public string HostFingerprint => Host.Fingerprint ?? "";

  /// <summary>The last connection test's result, in words.</summary>
  [NotifySignal]
  public string HostStatus { get => _hostStatus; private set => Set(ref _hostStatus, value); }

  [NotifySignal]
  public bool IsDeploying { get => _isDeploying; private set => Set(ref _isDeploying, value); }

  /// <summary>0 to 1 while uploading, -1 while preparing.</summary>
  [NotifySignal]
  public double DeployProgress { get => _deployProgress; private set => Set(ref _deployProgress, value); }

  /// <summary>What the upload is doing, or when the mods were last uploaded.</summary>
  [NotifySignal]
  public string DeployText => _deployText.Length > 0 ? _deployText
    : Model.LastDeployedAt is { } at ? $"Mods last uploaded {Format.Ago(at)}." : "The mods haven't been uploaded yet.";

  /// <summary>What's missing or wrong in the connection form as it stands, without saving: <c>{"field": "message"}</c>.</summary>
  /// <remarks>The page asks after every change once the player tried to save, so the outlines follow the typing.</remarks>
  public string CheckHosted(string? json) =>
    ReadHosted(json) is { } read ? ServersViewModel.ErrorsJson(read.Candidate.Validate()) : "{}";

  /// <summary>Takes the connection form as JSON, like <see cref="Apply"/> does the local settings.</summary>
  /// <returns>Empty when saved; otherwise <c>{"field": "message"}</c> for the fields to outline, and nothing was changed.</returns>
  public string ApplyHosted(string? json)
  {
    if (ReadHosted(json) is not var (form, candidate))
    {
      return "";
    }

    if (candidate.Validate() is { Count: > 0 } problems)
    {
      return ServersViewModel.ErrorsJson(problems);
    }

    var host = candidate.Host!;
    Model.Name = candidate.Name;
    Model.Host = host;
    Model.Password = form.ServerPassword ?? "";
    Model.ConsoleEnabled = form.ConsoleEnabled;
    var instanceChanged = (Model.InstanceId ?? "") != (form.InstanceId ?? "");
    Model.InstanceId = string.IsNullOrEmpty(form.InstanceId) ? null : form.InstanceId;
    Save();
    _ = Manager.ForgetConnectionAsync(Model);
    RaiseSettings();
    RaiseHosted();
    if (instanceChanged)
    {
      RefreshMods();
    }

    return "";
  }

  /// <summary>The form and the profile it would make, or null for no or unreadable JSON.</summary>
  private (HostedForm Form, ServerProfile Candidate)? ReadHosted(string? json)
  {
    if (string.IsNullOrEmpty(json))
    {
      return null;
    }

    HostedForm? form;
    try
    {
      form = JsonSerializer.Deserialize<HostedForm>(json, ServersViewModel.Json);
    }
    catch (JsonException)
    {
      return null;
    }

    if (form is null)
    {
      return null;
    }

    var host = new HostAccess
    {
      Protocol = form.Protocol switch { "ftp" => FileProtocol.Ftp, "ftps" => FileProtocol.Ftps, _ => FileProtocol.Sftp },
      Address = (form.Address ?? "").Trim(),
      Port = form.Port,
      User = (form.User ?? "").Trim(),
      Password = form.Password ?? "",
      KeyFile = string.IsNullOrWhiteSpace(form.KeyFile) ? null : AppViewModel.LocalPath(form.KeyFile),
      Directory = (form.Directory ?? "").Trim(),
      LogFile = string.IsNullOrWhiteSpace(form.LogFile) ? new HostAccess().LogFile : form.LogFile.Trim(),
      GameAddress = (form.GameAddress ?? "").Trim(),
      PanelUrl = (form.PanelUrl ?? "").Trim(),
      RconPort = form.RconPort,
      Platform = form.Platform == "windows" ? GamePlatform.Windows : GamePlatform.Linux,
    };

    // The pinned key belongs to the address it came from; another address starts over.
    host.Fingerprint = host.Address == Host.Address && host.EffectivePort == Host.EffectivePort && host.Protocol == Host.Protocol ? Host.Fingerprint : null;
    return (form, new ServerProfile { Name = form.Name?.Trim() ?? "", Kind = ServerKind.Hosted, Host = host });
  }

  /// <summary>Saves the connection form and, when it's complete, says so.</summary>
  /// <returns>See <see cref="ApplyHosted"/>.</returns>
  public string SaveHosted(string? json)
  {
    var errors = ApplyHosted(json);
    if (errors.Length == 0)
    {
      App.Toast("success", "Saved", $"Upload the mods to bring {Model.Name} up to date.");
    }

    return errors;
  }

  /// <summary>The saved settings' problems by field, for outlining them when an action needs them.</summary>
  [NotifySignal]
  public string FieldErrors => ServersViewModel.ErrorsJson(Model.Validate());

  /// <summary>Forgets the pinned host key, for a host that really changed it.</summary>
  public void ClearFingerprint()
  {
    Host.Fingerprint = null;
    Save();
    _ = Manager.ForgetConnectionAsync(Model);
    Raise(nameof(HostFingerprint));
  }

  /// <summary>Saves the form as it stands and tries it: what the player just typed is what gets tested.</summary>
  /// <returns>The fields to outline, as <see cref="ApplyHosted"/>; when there are any, nothing is tried.</returns>
  public string TestConnection(string? json)
  {
    var errors = ApplyHosted(json);
    if (errors.Length > 0)
    {
      HostStatus = "";
      return errors;
    }

    HostStatus = "Connecting…";
    App.Guard("Couldn't test the connection", async () =>
    {
      try
      {
        HostStatus = await Task.Run(() => Manager.DescribeHostAsync(Model, CancellationToken.None));
      }
      catch (Exception ex)
      {
        HostStatus = "Couldn't connect: " + ex.Message;
      }

      Raise(nameof(HostFingerprint));
    });
    return "";
  }

  /// <summary>Uploads BepInEx, the server's mods and LaunchHeim's plugin to the host.</summary>
  public void DeployMods() => GuardedDeploy("mods", replaceConfigs: false, force: false);

  /// <summary>Uploads with the instance's configs over the server's.</summary>
  public void DeployModsWithConfigs() => GuardedDeploy("mods", replaceConfigs: true, force: false);

  /// <summary>Uploads every file again, for files changed at the host behind LaunchHeim's back.</summary>
  public void DeployModsAgain() => GuardedDeploy("mods", replaceConfigs: false, force: true);

  /// <summary>Uploads Valheim's dedicated server itself, for a VPS that doesn't have it: about 2 GB the first time.</summary>
  public void UploadServerProgram() => GuardedDeploy("program", replaceConfigs: false, force: false);

  /// <summary>Uploads only with complete settings; otherwise the page outlines what's missing (<see cref="FieldErrors"/>).</summary>
  private void GuardedDeploy(string what, bool replaceConfigs, bool force)
  {
    if (Model.Validate().Count > 0)
    {
      Raise(nameof(FieldErrors));
      this.ActivateSignal("settingsIncomplete");
      return;
    }

    App.Guard($"Couldn't upload to {Model.Name}", () => DeployAsync(what, replaceConfigs, force));
  }

  private async Task DeployAsync(string what, bool replaceConfigs, bool force)
  {
    if (IsDeploying)
    {
      return;
    }

    IsDeploying = true;
    DeployProgress = -1;
    var activity = App.BeginActivity((what == "program" ? "Uploading the server to " : "Uploading mods to ") + Model.Name);
    // Made here, on the Qt thread: Progress<T> reports where it was created (see the 2026-11-07 crash).
    var progress = new Progress<DeployProgress>(p =>
    {
      DeployProgress = p.Total == 0 ? 1 : (double)p.Done / p.Total;
      activity.Progress = DeployProgress;
      activity.Detail = p.File;
      SetDeployText($"Uploading {p.Done} of {p.Total} files…");
    });

    try
    {
      DeployReport report;
      if (what == "program")
      {
        var directory = await App.Servers.EnsureProgramAsync(Host.Platform);
        if (directory is null)
        {
          return;
        }

        SetDeployText("Comparing with what's at the host…");
        report = await Task.Run(() => Manager.DeployServerProgramAsync(Model, directory, force, progress, CancellationToken.None));
      }
      else
      {
        SetDeployText("Getting the mods ready…");
        report = await Task.Run(() => Manager.DeployModsAsync(Model, replaceConfigs, force, progress, CancellationToken.None));
        Model.LastDeployedAt = DateTimeOffset.UtcNow;
        Save();
      }

      App.Toast("success", $"{Model.Name} is up to date",
        $"{report.Uploaded} file(s) uploaded, {report.Unchanged} already there, {report.Removed} removed."
        + (what == "program" ? "" : IsOnline ? " Restart the server at the host for the mods to load." : ""));
    }
    catch (Exception ex)
    {
      Core.Logging.Log.Error($"Uploading to {Model.Name} failed", ex);
      App.Toast("error", $"Couldn't upload to {Model.Name}", ex.Message);
    }
    finally
    {
      App.EndActivity(activity);
      SetDeployText("");
      DeployProgress = -1;
      IsDeploying = false;
    }
  }

  private void SetDeployText(string text)
  {
    _deployText = text;
    Raise(nameof(DeployText));
  }

  /// <summary>Opens the host's web panel, where a hosted server is started.</summary>
  public void OpenPanel()
  {
    if (Host.PanelUrl.Length > 0)
    {
      DesktopShell.Open(Host.PanelUrl.Contains("://", StringComparison.Ordinal) ? Host.PanelUrl : "https://" + Host.PanelUrl);
    }
  }

  private void RaiseHosted()
  {
    foreach (var property in (string[])[nameof(IsHosted), nameof(HostProtocol), nameof(HostAddress), nameof(HostPort), nameof(HostUser), nameof(HostPassword),
      nameof(HostKeyFile), nameof(HostDirectory), nameof(HostLogFile), nameof(GameAddress), nameof(PanelUrl), nameof(HostRconPort), nameof(HostPlatform),
      nameof(HostFingerprint), nameof(DeployText)])
    {
      Raise(property);
    }
  }

  /// <summary>The connection form as QML sends it.</summary>
  private sealed record HostedForm(
    string? Name,
    string? Protocol,
    string? Address,
    int Port,
    string? User,
    string? Password,
    string? KeyFile,
    string? Directory,
    string? LogFile,
    string? GameAddress,
    string? PanelUrl,
    int RconPort,
    string? Platform,
    string? ServerPassword,
    bool ConsoleEnabled,
    string? InstanceId);

  internal async Task RefreshStateAsync()
  {
    try
    {
      var state = await Manager.StateAsync(Model, CancellationToken.None);
      var wasStarting = _startingSince is not null && !_isOnline;
      IsRunning = state.Running;
      IsOnline = state.Online;
      Players = state.Players;
      MaxPlayers = state.MaxPlayers;
      _phase = state.Phase;
      _startingSince = !state.Running ? null : _startingSince ?? DateTimeOffset.UtcNow;
      JoinCode = state.Online && Model.Crossplay ? ServerAddresses.JoinCode(LogFile) ?? "" : "";
      if (wasStarting && state.Online)
      {
        App.Toast("success", $"{Model.Name} is online", $"Ready after {StartElapsed}. Join it from the server's page; friends join at this PC's address.");
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      Log.Warning($"Could not check server {Model.Name}: {ex.Message}");
    }

    Raise(nameof(StatusText));
    Raise(nameof(IsStarting));
    Raise(nameof(StartPhaseText));
    Raise(nameof(StartStep));
    Raise(nameof(StartElapsed));
    Raise(nameof(InternetAddress));
  }

  internal void RefreshMods()
  {
    Mods = Manager.ModEntries(Model).Select(e => new ServerModViewModel(this, e)).ToList();
    Raise(nameof(IncludedModCount));
    Raise(nameof(ModSummary));
    Raise(nameof(InstanceName));
    Raise(nameof(InstanceMissing));
  }

  internal void RaiseBinary()
  {
    Raise(nameof(ServerDirectory));
    Raise(nameof(ServerMissingText));
  }

  internal void SetModIncluded(string key, bool included)
  {
    Model.ModChoices[key] = included;
    Save();
    RefreshMods();
  }

  /// <summary>Takes the settings form as JSON, so one call saves them all and the checks see them together.</summary>
  /// <returns>Empty when saved; otherwise <c>{"field": "message"}</c> for the fields to outline, and nothing was changed.</returns>
  public string Apply(string? json)
  {
    if (ReadLocal(json) is not var (form, candidate))
    {
      return "";
    }

    if (candidate.Validate() is { Count: > 0 } problems)
    {
      return ServersViewModel.ErrorsJson(problems);
    }

    Model.Name = candidate.Name;
    Model.World = candidate.World;
    Model.Port = candidate.Port;
    Model.Password = candidate.Password;
    return ApplyRest(form, candidate);
  }

  /// <summary>What's missing or wrong in the settings form as it stands, without saving: <c>{"field": "message"}</c>.</summary>
  public string Check(string? json) =>
    ReadLocal(json) is { } read ? ServersViewModel.ErrorsJson(read.Candidate.Validate()) : "{}";

  /// <summary>The form and the profile it would make (checked on a copy, so a rejected form changes nothing).</summary>
  private (ServerForm Form, ServerProfile Candidate)? ReadLocal(string? json)
  {
    if (string.IsNullOrEmpty(json))
    {
      return null;
    }

    ServerForm? form;
    try
    {
      form = JsonSerializer.Deserialize<ServerForm>(json, ServersViewModel.Json);
    }
    catch (JsonException)
    {
      return null;
    }

    if (form is null)
    {
      return null;
    }

    return (form, new ServerProfile
    {
      Name = form.Name?.Trim() ?? "",
      World = form.World?.Trim() ?? "",
      Port = form.Port,
      Password = form.Password ?? "",
      Public = form.IsPublic,
      Crossplay = form.Crossplay,
      Preset = form.Preset ?? "",
      SaveInterval = Math.Max(60, form.SaveInterval),
      Backups = Math.Max(0, form.Backups),
      ExtraArguments = form.ExtraArguments ?? "",
      ConsoleEnabled = form.ConsoleEnabled,
      RconPort = Model.RconPort,
      Binary = form.Binary switch { "launchheim" => ServerBinary.LaunchHeim, "custom" => ServerBinary.Custom, _ => ServerBinary.Steam },
      CustomDirectory = string.IsNullOrWhiteSpace(form.CustomDirectory) ? null : AppViewModel.LocalPath(form.CustomDirectory),
      SaveDirectory = string.IsNullOrWhiteSpace(form.SaveDirectory) ? null : AppViewModel.LocalPath(form.SaveDirectory),
    });
  }

  private string ApplyRest(ServerForm form, ServerProfile candidate)
  {
    Model.Public = candidate.Public;
    Model.Crossplay = candidate.Crossplay;
    Model.Preset = candidate.Preset;
    Model.SaveInterval = candidate.SaveInterval;
    Model.Backups = candidate.Backups;
    Model.ExtraArguments = candidate.ExtraArguments;
    Model.ConsoleEnabled = candidate.ConsoleEnabled;
    Model.Binary = candidate.Binary;
    Model.CustomDirectory = candidate.CustomDirectory;
    Model.SaveDirectory = candidate.SaveDirectory;
    var instanceChanged = (Model.InstanceId ?? "") != (form.InstanceId ?? "");
    Model.InstanceId = string.IsNullOrEmpty(form.InstanceId) ? null : form.InstanceId;
    Save();
    RaiseSettings();
    if (instanceChanged)
    {
      RefreshMods();
    }

    App.Toast("success", "Saved", IsRunning ? $"{Model.Name} uses the new settings after a restart." : $"{Model.Name} is set up.");
    return "";
  }

  private void RaiseSettings()
  {
    foreach (var property in (string[])[nameof(Name), nameof(World), nameof(Port), nameof(Password), nameof(IsPublic), nameof(Crossplay), nameof(Preset),
      nameof(SaveInterval), nameof(Backups), nameof(ExtraArguments), nameof(ConsoleEnabled), nameof(RconPort), nameof(Binary), nameof(CustomDirectory),
      nameof(SaveDirectory), nameof(SaveDirectoryIsCustom), nameof(ServerDirectory), nameof(ServerMissingText), nameof(InstanceId), nameof(InstanceName),
      nameof(InstanceMissing), nameof(JoinAddress), nameof(Problems), nameof(ModSummary), nameof(FieldErrors)])
    {
      Raise(property);
    }
  }

  private void Save()
  {
    try
    {
      Manager.Store.Save(Model);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      App.Toast("error", "Could not save the server", ex.Message);
    }
  }

  /// <summary>Starts a local server; a hosted one starts in its host's panel, which this opens.</summary>
  public void Start()
  {
    if (Model.IsHosted)
    {
      if (Model.Host is { PanelUrl.Length: > 0 })
      {
        OpenPanel();
      }
      else
      {
        App.Toast("info", "Start it at your host", "LaunchHeim reaches this server only through its files. Start it in your host's panel, or add the panel's address in the connection settings.");
      }

      return;
    }

    if (Model.Validate().Count > 0)
    {
      Raise(nameof(FieldErrors));
      this.ActivateSignal("settingsIncomplete");
      return;
    }

    App.Guard($"Could not start {Model.Name}", () => StartAsync(replaceConfigs: false));
  }

  /// <summary>Starts with the instance's configs copied over the server's, for when the server's drifted.</summary>
  public void StartWithInstanceConfigs() => App.Guard($"Could not start {Model.Name}", () => StartAsync(replaceConfigs: true));

  private async Task StartAsync(bool replaceConfigs)
  {
    if (IsBusy)
    {
      return;
    }

    SetBusy("Getting the server ready");
    var activity = App.BeginActivity("Starting " + Model.Name);
    try
    {
      // Created here, on the Qt thread, not inside Task.Run: Progress<T> reports on the thread it was
      // made on, and a report from the thread pool crashed LaunchHeim (Qml.Net signals need the UI thread).
      var progress = activity.CreateProgress();
      await Task.Run(() => Manager.StartAsync(Model, replaceConfigs, progress, CancellationToken.None));
      App.Toast("success", $"{Model.Name} is starting", $"Players join at {JoinAddress} once the world is loaded. The console shows the progress.");
      App.DebugConsole.ShowForServer(this, onlyIfOpen: true);
    }
    catch (Exception ex)
    {
      Log.Error($"Starting server {Model.Name} failed", ex is LaunchException ? null : ex);
      App.Toast("error", $"Could not start {Model.Name}", ex.Message);
    }
    finally
    {
      App.EndActivity(activity);
      SetBusy(null);
      await RefreshStateAsync();
      _servers.RefreshStatus();
    }
  }

  public void Stop() => App.Guard($"Could not stop {Model.Name}", StopAsync);

  private async Task StopAsync()
  {
    if (IsBusy)
    {
      return;
    }

    SetBusy("Saving the world and stopping");
    try
    {
      var result = await Task.Run(() => Manager.StopAsync(Model, CancellationToken.None));
      if (result == StopResult.StillRunning)
      {
        App.Toast("error", $"{Model.Name} didn't stop", "It ignored the request to save and quit. Kill it from the server's page if it's stuck; that loses what happened since its last save.");
      }
      else if (Model.IsHosted)
      {
        App.Toast("success", $"{Model.Name} is saving and stopping", "Many hosts start a stopped server again on their own; their panel has the final say.");
      }
      else
      {
        App.Toast("success", $"{Model.Name} stopped", "The world was saved.");
        if (Model.Backup.AfterStop)
        {
          // Just saved and quiet: the best moment for a backup. Not awaited, so the page is free again.
          App.Guard($"Couldn't back up {Model.Name}", () => BackupAsync("after stopping", saveFirst: false, quiet: true));
        }
      }
    }
    catch (RconException ex)
    {
      App.Toast("error", $"Could not reach {Model.Name}", ex.Message);
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
      App.Toast("error", $"Could not stop {Model.Name}", ex.Message);
    }
    finally
    {
      SetBusy(null);
      await RefreshStateAsync();
      _servers.RefreshStatus();
    }
  }

  public void Restart() => App.Guard($"Could not restart {Model.Name}", RestartAsync);

  private async Task RestartAsync()
  {
    if (IsBusy)
    {
      return;
    }

    SetBusy("Saving the world and stopping");
    StopResult result;
    try
    {
      result = await Task.Run(() => Manager.StopAsync(Model, CancellationToken.None));
    }
    finally
    {
      SetBusy(null);
    }

    if (result == StopResult.StillRunning)
    {
      App.Toast("error", $"{Model.Name} didn't stop", "So it wasn't started again.");
      await RefreshStateAsync();
      return;
    }

    await StartAsync(replaceConfigs: false);
  }

  /// <summary>Ends a server that won't stop. QML asks first; this loses what wasn't saved.</summary>
  public void Kill() => App.Guard($"Could not kill {Model.Name}", KillAsync);

  private async Task KillAsync()
  {
    try
    {
      await Task.Run(() => Manager.KillAsync(Model));
      App.Toast("info", $"{Model.Name} was killed", "Valheim's last autosave is what's left of the world.");
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
      App.Toast("error", $"Could not kill {Model.Name}", ex.Message);
    }

    await RefreshStateAsync();
    _servers.RefreshStatus();
  }

  /// <summary>Runs a console command and returns its answer, or the reason there is none.</summary>
  internal async Task<string> SendAsync(string command)
  {
    try
    {
      return await Task.Run(() => Manager.SendAsync(Model, command, CancellationToken.None));
    }
    catch (Exception ex) when (ex is RconException or IOException or InvalidOperationException)
    {
      return "Error: " + ex.Message;
    }
  }

  /// <summary>The key Valheim's server list and the Play page use for this server, where its Join choice is remembered.</summary>
  internal string JoinKey => new ValheimServer(Model.Name, Model.ConnectAddress, IsFavorite: false, IsRecent: false).Key;

  private PlayChoice? JoinChoice => App.SettingsModel.PlayChoices.GetValueOrDefault(JoinKey);

  private int RememberedCharacterIndex => JoinChoice is { } choice ? IndexOfCharacter(choice.Character) : -1;

  /// <summary>A character was picked before and still exists, so Join needs no dialog.</summary>
  [NotifySignal]
  public bool HasJoinChoice => RememberedCharacterIndex >= 0;

  /// <summary>The character the Join dialog starts on: the remembered one, else the one Valheim has selected.</summary>
  [NotifySignal]
  public int JoinCharacterIndex => RememberedCharacterIndex >= 0 ? RememberedCharacterIndex : Math.Max(0, IndexOfCharacter(App.Play.GameCharacter));

  /// <summary>"Lukaz", or "Lukaz · instant", for the line under Join.</summary>
  [NotifySignal]
  public string JoinChoiceText => HasJoinChoice
    ? App.Play.CharacterList[RememberedCharacterIndex].DisplayName + (JoinInstant ? " · instant" : "")
    : "";

  /// <summary>Start instantly, as last time; on the first time, since Join is meant to be one click.</summary>
  [NotifySignal]
  public bool JoinInstant => JoinChoice?.InstantStart ?? true;

  /// <summary>Joins with the remembered character. QML opens the Join dialog instead when there is none.</summary>
  public void Join()
  {
    if (HasJoinChoice)
    {
      JoinWith(RememberedCharacterIndex, JoinInstant);
    }
  }

  /// <summary>
  /// Plays this server with the character chosen and always with the server's own instance and password,
  /// so the player has exactly the mods the server runs. Remembered like a Play-page server (same key),
  /// which also makes the sidebar's Play button and the Steam start join it again.
  /// </summary>
  public void JoinWith(int characterIndex, bool instant)
  {
    var characters = App.Play.CharacterList;
    if (characterIndex < 0 || characterIndex >= characters.Count)
    {
      App.Toast("info", "No character", "Create a character in Valheim first, then join.");
      return;
    }

    InstanceViewModel? instance = null;
    if (Model.InstanceId is { } instanceId)
    {
      instance = App.InstanceList.FirstOrDefault(i => i.Id == instanceId);
      if (instance is null)
      {
        App.Toast("error", "The server's instance is gone", "Choose the instance with the server's mods in its settings first.");
        return;
      }
    }

    var character = characters[characterIndex];
    App.SettingsModel.PlayChoices[JoinKey] = new PlayChoice
    {
      Character = character.FileName,
      InstanceId = instance?.Id,
      Password = Model.Password.Length > 0 ? Model.Password : null,
      InstantStart = instant,
      LastPlayedAt = DateTimeOffset.UtcNow,
    };
    App.SaveSettings();
    RaiseJoin();
    App.Launch(instance, DirectPlay.Server(JoinKey, Model.Name, Model.ConnectAddress, character.FileName, Model.Password, instant));
  }

  internal void RaiseJoin()
  {
    Raise(nameof(HasJoinChoice));
    Raise(nameof(JoinCharacterIndex));
    Raise(nameof(JoinChoiceText));
    Raise(nameof(JoinInstant));
  }

  private int IndexOfCharacter(string fileName)
  {
    var characters = App.Play.CharacterList;
    for (var i = 0; i < characters.Count; i++)
    {
      if (characters[i].FileName == fileName)
      {
        return i;
      }
    }

    return -1;
  }

  // Backups (Core/Servers/Backups): settings, the list, back up now and restore.
  private List<BackupEntryViewModel> _backups = [];
  private bool _isBackingUp;
  private double _backupProgress = -1;
  private string _backupStatus = "";

  private BackupSettings BackupSettings => Model.Backup;

  [NotifySignal]
  public bool BackupScheduled => BackupSettings.Scheduled;

  [NotifySignal]
  public int BackupInterval => BackupSettings.IntervalHours;

  [NotifySignal]
  public bool BackupAfterStop => BackupSettings.AfterStop;

  /// <summary>Where backups are written: the chosen folder, or LaunchHeim's own.</summary>
  [NotifySignal]
  public string BackupFolder => _servers.Backups.FolderOf(Model);

  [NotifySignal]
  public bool BackupFolderIsCustom => !string.IsNullOrWhiteSpace(BackupSettings.Folder);

  /// <summary>none, rclone or proton.</summary>
  [NotifySignal]
  public string BackupCloud => BackupSettings.Cloud switch { Core.Servers.Backups.BackupCloud.Rclone => "rclone", Core.Servers.Backups.BackupCloud.ProtonDrive => "proton", _ => "none" };

  [NotifySignal]
  public string BackupCloudPath => BackupSettings.CloudPath;

  [NotifySignal]
  public string BackupToolPath => BackupSettings.ToolPath ?? "";

  [NotifySignal]
  public int BackupKeepLast => BackupSettings.KeepLast;

  [NotifySignal]
  public int BackupKeepDays => BackupSettings.KeepDays;

  [NotifySignal]
  public bool BackupIncludeConfigs => BackupSettings.IncludeConfigs;

  /// <summary>Where a hosted server's worlds are, in <see cref="HostAccess.SaveDirectory"/>'s form.</summary>
  [NotifySignal]
  public string HostSaveDirectory => Host.SaveDirectory;

  [NotifySignal]
  public string LastBackupText => BackupSettings.LastBackupAt is { } at ? $"Last backup {Format.Ago(at)}." : "No backup yet.";

  /// <summary>The backups LaunchHeim made, newest first. Not called Backups: that's Valheim's own count (<c>-backups</c>).</summary>
  [NotifySignal]
  public List<BackupEntryViewModel> BackupList { get => _backups; private set => Set(ref _backups, value); }

  [NotifySignal]
  public bool IsBackingUp { get => _isBackingUp; private set => Set(ref _isBackingUp, value); }

  /// <summary>0 to 1 while copying, -1 while saving or uploading.</summary>
  [NotifySignal]
  public double BackupProgress { get => _backupProgress; private set => Set(ref _backupProgress, value); }

  /// <summary>What a backup or restore is doing, or what the last look at the cloud said.</summary>
  [NotifySignal]
  public string BackupStatus { get => _backupStatus; private set => Set(ref _backupStatus, value); }

  /// <summary>What's missing in the backup form as it stands: <c>{"field": "message"}</c>.</summary>
  public string CheckBackup(string? json) =>
    ReadBackup(json) is { } settings ? ServersViewModel.ErrorsJson(settings.Validate()) : "{}";

  /// <summary>Saves the backup form; empty when saved, otherwise the fields to outline.</summary>
  public string ApplyBackup(string? json)
  {
    if (ReadBackup(json) is not { } settings)
    {
      return "";
    }

    if (settings.Validate() is { Count: > 0 } problems)
    {
      return ServersViewModel.ErrorsJson(problems);
    }

    settings.LastBackupAt = BackupSettings.LastBackupAt;
    Model.Backup = settings;
    if (Model.Host is { } host && json is not null && JsonSerializer.Deserialize<BackupForm>(json, ServersViewModel.Json) is { SaveDirectory: { } saves })
    {
      host.SaveDirectory = saves.Trim();
    }

    Save();
    RaiseBackup();
    App.Toast("success", "Saved", settings.Scheduled ? $"{Model.Name} is backed up every {settings.IntervalHours} h while it runs." : "Backups happen when you click Back up now.");
    RefreshBackups();
    return "";
  }

  private BackupSettings? ReadBackup(string? json)
  {
    if (string.IsNullOrEmpty(json))
    {
      return null;
    }

    try
    {
      return JsonSerializer.Deserialize<BackupForm>(json, ServersViewModel.Json) is { } form
        ? new BackupSettings
        {
          Scheduled = form.Scheduled,
          IntervalHours = form.IntervalHours,
          AfterStop = form.AfterStop,
          Folder = string.IsNullOrWhiteSpace(form.Folder) ? null : AppViewModel.LocalPath(form.Folder),
          Cloud = form.Cloud switch { "rclone" => Core.Servers.Backups.BackupCloud.Rclone, "proton" => Core.Servers.Backups.BackupCloud.ProtonDrive, _ => Core.Servers.Backups.BackupCloud.None },
          CloudPath = form.CloudPath?.Trim() ?? "",
          ToolPath = string.IsNullOrWhiteSpace(form.ToolPath) ? null : AppViewModel.LocalPath(form.ToolPath),
          KeepLast = form.KeepLast,
          KeepDays = form.KeepDays,
          IncludeConfigs = form.IncludeConfigs,
        }
        : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  /// <summary>Reads the backups again: the folder at once, then the cloud, which takes a moment.</summary>
  public void RefreshBackups() => App.Guard("Couldn't list the backups", async () =>
  {
    BackupList = (await Task.Run(() => _servers.Backups.ListAsync(Model, includeCloud: false, CancellationToken.None))).Select(e => new BackupEntryViewModel(this, e)).ToList();
    if (BackupSettings.Cloud == Core.Servers.Backups.BackupCloud.None)
    {
      return;
    }

    try
    {
      BackupList = (await Task.Run(() => _servers.Backups.ListAsync(Model, includeCloud: true, CancellationToken.None))).Select(e => new BackupEntryViewModel(this, e)).ToList();
      BackupStatus = "";
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException)
    {
      BackupStatus = "The cloud couldn't be read: " + ex.Message;
    }
  });

  public void BackupNow()
  {
    if (Model.Validate().Concat(BackupSettings.Validate()).Any())
    {
      Raise(nameof(FieldErrors));
      this.ActivateSignal("settingsIncomplete");
      return;
    }

    App.Guard($"Couldn't back up {Model.Name}", () => BackupAsync("by hand", saveFirst: true, quiet: false));
  }

  internal async Task BackupAsync(string reason, bool saveFirst, bool quiet)
  {
    if (!await _servers.BackupGate.WaitAsync(0))
    {
      if (!quiet)
      {
        App.Toast("info", "A backup is running", "Try again once it's done.");
      }

      return;
    }

    IsBackingUp = true;
    BackupProgress = -1;
    BackupStatus = saveFirst ? "Asking the server to save…" : "Backing up…";
    var activity = App.BeginActivity("Backing up " + Model.Name);
    // On the Qt thread, before Task.Run (see the 2026-11-07 crash).
    var progress = new Progress<DeployProgress>(p =>
    {
      BackupProgress = p.Total == 0 ? -1 : (double)p.Done / p.Total;
      activity.Progress = BackupProgress;
      BackupStatus = p.Done < p.Total ? $"Copying {p.Done} of {p.Total} files…" : BackupSettings.Cloud == Core.Servers.Backups.BackupCloud.None ? "Finishing…" : "Copying to the cloud…";
    });

    try
    {
      var entry = await Task.Run(() => _servers.Backups.BackupAsync(Model, reason, progress, CancellationToken.None, saveFirst: saveFirst));
      if (!quiet)
      {
        App.Toast("success", $"{Model.Name} is backed up", $"{entry.Name} ({Format.Bytes(entry.Size)}){(entry.InCloud ? ", also in the cloud" : "")}.");
      }
    }
    catch (Exception ex)
    {
      Log.Error($"Backing up {Model.Name} failed", ex is IOException ? null : ex);
      App.Toast("error", $"Couldn't back up {Model.Name}", ex.Message);
    }
    finally
    {
      App.EndActivity(activity);
      BackupStatus = "";
      IsBackingUp = false;
      _servers.BackupGate.Release();
      Raise(nameof(LastBackupText));
      Raise(nameof(HostSaveDirectory));
      RefreshBackups();
    }
  }

  /// <summary>Puts a backup back. QML asks first; the current state is backed up before.</summary>
  internal void Restore(string name) => App.Guard($"Couldn't restore {Model.Name}", async () =>
  {
    if (!await _servers.BackupGate.WaitAsync(0))
    {
      App.Toast("info", "A backup is running", "Try again once it's done.");
      return;
    }

    IsBackingUp = true;
    BackupProgress = -1;
    BackupStatus = "Restoring " + name + "…";
    var progress = new Progress<DeployProgress>(p => BackupProgress = p.Total == 0 ? -1 : (double)p.Done / p.Total);
    try
    {
      var report = await Task.Run(() => _servers.Backups.RestoreAsync(Model, name, progress, CancellationToken.None));
      App.Toast("success", $"{Model.Name} is restored", $"{string.Join(", ", report.Worlds)} from {name}. The state before is kept as a backup marked “before restore”.");
    }
    finally
    {
      BackupStatus = "";
      IsBackingUp = false;
      _servers.BackupGate.Release();
      RefreshBackups();
    }
  });

  public void OpenBackupFolder()
  {
    Directory.CreateDirectory(BackupFolder);
    DesktopShell.Open(BackupFolder);
  }

  /// <summary>Looks for a hosted server's worlds at the usual places, and fills in the field when found.</summary>
  public void FindSaveDirectory() => App.Guard("Couldn't look for the worlds", async () =>
  {
    BackupStatus = "Looking for the worlds at the host…";
    try
    {
      var found = await Task.Run(() => _servers.Backups.FindSaveDirectoryAsync(Model, CancellationToken.None));
      BackupStatus = found is null
        ? "No worlds_local folder in the usual places. Enter the folder your host's panel names as the save folder."
        : $"Found the worlds in {(found.Length == 0 ? "the server folder" : found)}.";
      Raise(nameof(HostSaveDirectory));
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException)
    {
      BackupStatus = "Couldn't look: " + ex.Message;
    }
  });

  private void RaiseBackup()
  {
    foreach (var property in (string[])[nameof(BackupScheduled), nameof(BackupInterval), nameof(BackupAfterStop), nameof(BackupFolder), nameof(BackupFolderIsCustom),
      nameof(BackupCloud), nameof(BackupCloudPath), nameof(BackupToolPath), nameof(BackupKeepLast), nameof(BackupKeepDays), nameof(BackupIncludeConfigs),
      nameof(HostSaveDirectory), nameof(LastBackupText)])
    {
      Raise(property);
    }
  }

  /// <summary>The backup form as QML sends it.</summary>
  private sealed record BackupForm(
    bool Scheduled,
    int IntervalHours,
    bool AfterStop,
    string? Folder,
    string? Cloud,
    string? CloudPath,
    string? ToolPath,
    int KeepLast,
    int KeepDays,
    bool IncludeConfigs,
    string? SaveDirectory);

  public void OpenConsole() => App.DebugConsole.ShowForServer(this);

  public void OpenFolder() => DesktopShell.Open(Manager.Store.DirectoryOf(Model));

  public void OpenSaves()
  {
    Directory.CreateDirectory(SaveDirectory);
    DesktopShell.Open(SaveDirectory);
  }

  public void OpenInstance()
  {
    if (Model.InstanceId is { } id)
    {
      App.OpenInstance(id);
    }
  }

  public void Delete() => _servers.Delete(this);

  private void SetBusy(string? text)
  {
    BusyText = text ?? "";
    IsBusy = text is not null;
    Raise(nameof(StatusText));
  }

  /// <summary>The settings form as QML sends it.</summary>
  private sealed record ServerForm(
    string? Name,
    string? World,
    int Port,
    string? Password,
    bool IsPublic,
    bool Crossplay,
    string? Preset,
    int SaveInterval,
    int Backups,
    string? ExtraArguments,
    bool ConsoleEnabled,
    string? Binary,
    string? CustomDirectory,
    string? SaveDirectory,
    string? InstanceId);
}

/// <summary>One backup in the list on a server's page.</summary>
public sealed class BackupEntryViewModel(ServerViewModel server, BackupEntry entry) : ViewModel
{
  [NotifySignal]
  public string Name => entry.Name;

  /// <summary>"7 Nov 2026, 14:05 · 2 hours ago", in local time.</summary>
  [NotifySignal]
  public string When => $"{entry.CreatedAt.ToLocalTime():d MMM yyyy, HH:mm} · {Format.Ago(entry.CreatedAt)}";

  [NotifySignal]
  public string Size => Format.Bytes(entry.Size);

  /// <summary>"Folder and cloud", "Folder" or "Cloud".</summary>
  [NotifySignal]
  public string Where => entry is { InFolder: true, InCloud: true } ? "Folder and cloud" : entry.InFolder ? "Folder" : "Cloud";

  /// <summary>A safety copy made before a restore, which is never removed automatically.</summary>
  [NotifySignal]
  public bool IsSafetyCopy => entry.Label is not null;

  public void Restore() => server.Restore(entry.Name);
}

/// <summary>One address other players can join at, with what it's for.</summary>
public sealed class ServerAddressViewModel(LocalAddress address, int port) : ViewModel
{
  /// <summary>What goes into Valheim's "Join IP": the address and the port.</summary>
  [NotifySignal]
  public string Address => $"{address.Address}:{port}";

  /// <summary>Who it's for, in words.</summary>
  [NotifySignal]
  public string Label => address.Scope switch
  {
    AddressScope.LocalNetwork => "Same network (home Wi-Fi or LAN)",
    AddressScope.Vpn => "Same VPN (Tailscale, ZeroTier)",
    _ => "Internet (this PC is directly reachable)",
  };

  [NotifySignal]
  public string Interface => address.Interface;
}

/// <summary>A mod of the server's instance and whether the server runs it.</summary>
public sealed class ServerModViewModel(ServerViewModel server, ServerModEntry entry) : ViewModel
{
  [NotifySignal]
  public string Key => entry.Mod.Key;

  [NotifySignal]
  public string Name => entry.Mod.Name;

  [NotifySignal]
  public string Version => entry.Mod.Version;

  [NotifySignal]
  public string Author => entry.Mod.Author;

  /// <summary>Thunderstore lists it as Client-side only, which is why it's off unless switched on.</summary>
  [NotifySignal]
  public bool ClientOnly => entry.ClientOnly;

  [NotifySignal]
  public bool Included => entry.Included;

  public void SetIncluded(bool included) => server.SetModIncluded(entry.Mod.Key, included);
}
