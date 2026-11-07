using System.Text.Json;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Saves;
using CINE.LaunchHeim.Core.Servers;
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
public sealed class ServersViewModel(AppViewModel app, ServerManager manager, SteamCmd steamCmd) : ViewModel
{
  /// <summary>Separates the names in the combo box models, as the Play page does.</summary>
  internal const string Separator = "\u001f";

  private List<ServerViewModel> _items = [];
  private ServerViewModel? _selected;
  private bool _loaded;
  private bool _isDownloading;
  private bool _refreshing;

  internal AppViewModel App => app;
  internal ServerManager Manager => manager;

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

    RefreshStatus();
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

  public async void RefreshStatus()
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
    }
  }

  /// <param name="instanceId">The instance whose mods it runs, or empty for vanilla.</param>
  public void Create(string? name, string? instanceId)
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
    if (server.IsRunning)
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
  public async void DownloadServer()
  {
    if (IsDownloading)
    {
      return;
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
      var platform = GamePlatforms.Current;
      await Task.Run(() => steamCmd.InstallServerAsync(manager.Dedicated.DownloadDirectory(platform), platform, progress, CancellationToken.None));
      app.Toast("success", "Dedicated server ready", "Servers set to LaunchHeim's download can start now.");
    }
    catch (Exception ex) when (ex is SteamCmdException or HttpRequestException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidDataException)
    {
      Log.Error("Downloading the dedicated server failed", ex);
      app.Toast("error", "Could not download the server", ex.Message);
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

  internal static string ChoiceKey(ServerBinary binary) => binary switch
  {
    ServerBinary.LaunchHeim => "launchheim",
    ServerBinary.Custom => "custom",
    _ => "steam",
  };

  internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
}

/// <summary>One server: its card on the Servers page and its own page.</summary>
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
    : !_isRunning ? "Stopped"
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

  /// <summary>The server's log, which the console's Server tab follows.</summary>
  internal string LogFile => Manager.Store.LogFileOf(Model);

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
  /// <returns>Empty when saved, otherwise the problems (one per line), and nothing was changed.</returns>
  public string Apply(string? json)
  {
    if (string.IsNullOrEmpty(json))
    {
      return "";
    }

    ServerForm? form;
    try
    {
      form = JsonSerializer.Deserialize<ServerForm>(json, ServersViewModel.Json);
    }
    catch (JsonException ex)
    {
      return ex.Message;
    }

    if (form is null)
    {
      return "";
    }

    // Checked on a copy, so a rejected form leaves the profile as it was.
    var candidate = new ServerProfile
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
    };

    if (candidate.Problems() is { Count: > 0 } problems)
    {
      return string.Join("\n", problems);
    }

    Model.Name = candidate.Name;
    Model.World = candidate.World;
    Model.Port = candidate.Port;
    Model.Password = candidate.Password;
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
      nameof(InstanceMissing), nameof(JoinAddress), nameof(Problems), nameof(ModSummary)])
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

  public void Start() => _ = StartAsync(replaceConfigs: false);

  /// <summary>Starts with the instance's configs copied over the server's, for when the server's drifted.</summary>
  public void StartWithInstanceConfigs() => _ = StartAsync(replaceConfigs: true);

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
    catch (Exception ex) when (ex is LaunchException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or Core.Catalogs.CatalogException or HttpRequestException)
    {
      Log.Error($"Starting server {Model.Name} failed", ex);
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

  public async void Stop()
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
      else
      {
        App.Toast("success", $"{Model.Name} stopped", "The world was saved.");
      }
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

  public async void Restart()
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
  public async void Kill()
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
    catch (RconException ex)
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
