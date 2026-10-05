using System.Diagnostics;
using CINE.LaunchHeim.Core.Catalogs;
using CINE.LaunchHeim.Core.Catalogs.Nexus;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Core.Packs;
using CINE.LaunchHeim.Core.Saves;
using CINE.LaunchHeim.Core.Storage;
using CINE.LaunchHeim.Desktop.Hosting;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>The root object QML sees as <c>app</c>.</summary>
[Signal("activateRequested")]
public sealed class AppViewModel : ViewModel
{
  private readonly SettingsStore _settingsStore;
  private readonly GameFolderImporter _importer;
  private readonly SteamClient _steam = SteamClient.ForCurrentUser();
  private readonly Dictionary<string, string> _pendingNexus = new();
  private List<InstanceViewModel> _instances = [];
  private List<ActivityItem> _activities = [];
  private List<ToastItem> _toasts = [];
  private InstanceViewModel? _selected;
  private string _page = "library";
  private string _runningInstanceId = "";
  private string _runningDestination = "";
  private bool _isGameRunning;
  private string _steamStatus = "";

  public AppViewModel(
    AppPaths paths,
    SettingsStore settingsStore,
    InstanceStore instances,
    ModService mods,
    CatalogRegistry catalogs,
    PackService packs,
    GameFolderImporter importer,
    ImageCache images)
  {
    Paths = paths;
    _settingsStore = settingsStore;
    SettingsModel = settingsStore.Load();
    Instances = instances;
    Mods = mods;
    Catalogs = catalogs;
    Packs = packs;
    Images = images;
    _importer = importer;

    Theme = new ThemeViewModel();
    Browse = new BrowseViewModel(this);
    Settings = new SettingsViewModel(this);
    BrowserPrompt = new BrowserPromptViewModel();
    DebugConsole = new ConsoleViewModel(this);
    Play = new PlayViewModel(this);
    PhoneSync = new PhoneSyncViewModel(this);

    _instances = instances.LoadAll().Select(i => new InstanceViewModel(this, i)).ToList();
    _selected = _instances.FirstOrDefault(i => i.Id == SettingsModel.LastInstanceId) ?? _instances.FirstOrDefault();
    Browse.SetTarget(_selected);
  }

  internal AppPaths Paths { get; }
  internal AppSettings SettingsModel { get; }
  internal InstanceStore Instances { get; }
  internal ModService Mods { get; }
  internal CatalogRegistry Catalogs { get; }
  internal PackService Packs { get; }
  internal ImageCache Images { get; }
  internal List<InstanceViewModel> InstanceList => _instances;

  [NotifySignal]
  public ThemeViewModel Theme { get; }

  [NotifySignal]
  public BrowseViewModel Browse { get; }

  [NotifySignal]
  public SettingsViewModel Settings { get; }

  [NotifySignal]
  public BrowserPromptViewModel BrowserPrompt { get; }

  [NotifySignal]
  public PlayViewModel Play { get; }

  [NotifySignal]
  public PhoneSyncViewModel PhoneSync { get; }

  /// <summary>Not called Console: QML would read <c>console</c> as its logging object.</summary>
  [NotifySignal]
  public ConsoleViewModel DebugConsole { get; }

  /// <summary>Some options only mean something on Windows, such as BepInEx's own console window.</summary>
  [NotifySignal]
  public bool IsWindows => OperatingSystem.IsWindows();

  [NotifySignal]
  public string Version => Core.AppInfo.Version;

  /// <summary>library, play, instance, browse or settings.</summary>
  [NotifySignal]
  public string CurrentPage { get => _page; private set => Set(ref _page, value); }

  [NotifySignal]
  public List<InstanceViewModel> InstanceItems { get => _instances; private set => Set(ref _instances, value); }

  [NotifySignal]
  public int InstanceCount => _instances.Count;

  [NotifySignal]
  public InstanceViewModel? SelectedInstance { get => _selected; private set => Set(ref _selected, value); }

  /// <summary>The most recently played instance, for the "continue" banner on the library page.</summary>
  [NotifySignal]
  public InstanceViewModel? RecentInstance => _instances.Where(i => i.Model.LastPlayedAt is not null).MaxBy(i => i.Model.LastPlayedAt) ?? _instances.FirstOrDefault();

  [NotifySignal]
  public List<ActivityItem> Activities { get => _activities; private set => Set(ref _activities, value); }

  [NotifySignal]
  public List<ToastItem> Toasts { get => _toasts; private set => Set(ref _toasts, value); }

  [NotifySignal]
  public bool IsGameRunning { get => _isGameRunning; private set => Set(ref _isGameRunning, value); }

  /// <summary>Set while a launch waits for Steam, and shown in the sidebar instead of the game status.</summary>
  [NotifySignal]
  public string SteamStatus { get => _steamStatus; private set => Set(ref _steamStatus, value); }

  [NotifySignal]
  public string RunningInstanceId { get => _runningInstanceId; private set => Set(ref _runningInstanceId, value); }

  /// <summary>
  /// The server or world the game was started for, and the setup: "Walheim · Survival". The destination
  /// comes first because the sidebar is narrow and elides the end.
  /// </summary>
  [NotifySignal]
  public string RunningName
  {
    get
    {
      var setup = _instances.FirstOrDefault(i => i.Id == _runningInstanceId)?.Name ?? (IsGameRunning ? PlayViewModel.VanillaName : "");
      return _runningDestination.Length > 0 && setup.Length > 0 ? $"{_runningDestination} · {setup}" : setup;
    }
  }

  /// <summary>
  /// What the sidebar's Play button starts: the server or world played last ("Shitbox · Survival", in the
  /// order of <see cref="RunningName"/>), else the instance played last, else vanilla.
  /// </summary>
  [NotifySignal]
  public string LastPlayName
  {
    get
    {
      if (SettingsModel.LastPlay is not { } last)
      {
        return PlayViewModel.VanillaName;
      }

      if (last.DestinationKey is { } key && SettingsModel.PlayChoices.GetValueOrDefault(key) is { } choice)
      {
        var setup = choice.InstanceId is null ? PlayViewModel.VanillaName : _instances.FirstOrDefault(i => i.Id == choice.InstanceId)?.Name;
        return setup is null ? last.DestinationName ?? "" : $"{last.DestinationName} · {setup}";
      }

      return LastInstance?.Name ?? PlayViewModel.VanillaName;
    }
  }

  // An instance deleted since is forgotten rather than an error: the button falls back to vanilla, and says so.
  private InstanceViewModel? LastInstance =>
    SettingsModel.LastPlay?.InstanceId is { } id ? _instances.FirstOrDefault(i => i.Id == id) : null;

  /// <summary>Starts what was played last again, see <see cref="LastPlayName"/>.</summary>
  public void PlayLast()
  {
    if (SettingsModel.LastPlay?.DestinationKey is { } key)
    {
      _ = Play.PlayAgainAsync(key);
    }
    else
    {
      StartGame(LastInstance);
    }
  }

  public void Navigate(string page)
  {
    CurrentPage = page;
    if (page == "browse")
    {
      Browse.EnsureLoaded();
    }
    else if (page == "play")
    {
      Play.Refresh();
    }
  }

  public void OpenInstance(string id)
  {
    if (_instances.FirstOrDefault(i => i.Id == id) is { } instance)
    {
      Select(instance);
      CurrentPage = "instance";
    }
  }

  public void OpenUrl(string url) => DesktopShell.Open(url);

  /// <summary>Opens the console on the running instance's log, or the selected one's.</summary>
  public void OpenConsole()
  {
    if ((_instances.FirstOrDefault(i => i.Id == _runningInstanceId) ?? _selected) is { } instance)
    {
      DebugConsole.ShowFor(instance);
    }
    else
    {
      DebugConsole.Show(IsGameRunning ? "unity" : "app");
    }
  }

  /// <param name="fileUrl">A file:// URL from the QML file dialog.</param>
  public void ImportPack(string fileUrl) => _ = ImportPackAsync(LocalPath(fileUrl));

  /// <summary>Imports a pack that arrived from the companion app or another LocalSend device.</summary>
  internal Task ImportPackFile(string file) => ImportPackAsync(file);

  private async Task ImportPackAsync(string file)
  {
    var activity = BeginActivity("Importing " + Path.GetFileName(file));
    try
    {
      var progress = activity.CreateProgress();
      var report = await Task.Run(() => Packs.ImportAsync(file, progress, CancellationToken.None));
      var instance = new InstanceViewModel(this, report.Instance);
      AddInstance(instance);
      OpenInstance(instance.Id);

      ReportInstall(report.Install, instance.Name, quiet: true);
      var from = report.FromR2modman ? " from r2modman" : "";
      Log.Info($"Imported {file}{from} as {instance.Name} ({instance.Directory}): {instance.ModCount} mod(s), {report.ConfigFiles} config file(s).");
      Toast("success", $"Imported {instance.Name}{from}", $"{instance.ModCount} mod(s) and {report.ConfigFiles} config file(s).");
    }
    catch (Exception ex)
    {
      Log.Error($"Importing {file} failed", ex);
      Toast("error", "Import failed", ex.Message);
    }
    finally
    {
      EndActivity(activity);
    }
  }

  internal static string LocalPath(string fileUrl) =>
    fileUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(fileUrl).LocalPath : fileUrl;

  public void CreateInstance(string name, bool installLoader)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return;
    }

    var instance = new InstanceViewModel(this, Instances.Create(name));
    AddInstance(instance);
    OpenInstance(instance.Id);
    if (installLoader)
    {
      instance.InstallLoader();
    }
  }

  public void ImportGameFolder(string name) => _ = ImportGameFolderAsync(name);

  private async Task ImportGameFolderAsync(string name)
  {
    var activity = BeginActivity("Importing the game folder");
    try
    {
      var directory = Settings.GameDirectory;
      var model = await Task.Run(() => _importer.Import(directory, string.IsNullOrWhiteSpace(name) ? "Imported" : name));
      var instance = new InstanceViewModel(this, model);
      AddInstance(instance);
      OpenInstance(instance.Id);
      Toast("success", "Imported", $"{instance.ModCount} mod(s) and your configs were copied. The game folder was not changed.");
    }
    catch (Exception ex)
    {
      Toast("error", "Import failed", ex.Message);
    }
    finally
    {
      EndActivity(activity);
    }
  }

  internal void Launch(InstanceViewModel instance) => StartGame(instance);

  /// <param name="instance">The instance to play, or null for vanilla.</param>
  internal void Launch(InstanceViewModel? instance, DirectPlay direct) => StartGame(instance, direct);

  private async void StartGame(InstanceViewModel? instance, DirectPlay? direct = null)
  {
    if (IsGameRunning)
    {
      Toast("info", "Valheim is running", "Close the game before starting another instance.");
      return;
    }

    if (SteamStatus.Length > 0)
    {
      Toast("info", "Waiting for Steam", "Valheim starts as soon as Steam is ready.");
      return;
    }

    try
    {
      var instanceDirectory = instance is null ? null : Instances.DirectoryOf(instance.Model);
      // Windows only: Doorstop's proxy has to sit in the game folder (see GameLauncher).
      GameLauncher.PrepareGameFolder(Settings.GameDirectory, instanceDirectory);
      var plan = GameLauncher.Plan(
        Settings.GameDirectory,
        instanceDirectory,
        instance?.Model.LaunchArguments ?? SettingsModel.VanillaLaunchArguments,
        GameLauncher.CurrentEnvironment(),
        joinArguments: direct?.Arguments);

      // Valheim without a logged-in Steam client shows a black window and no error (see SteamClient).
      SteamStatus = "Checking Steam";
      await _steam.EnsureReadyAsync(
        state =>
        {
          SteamStatus = state == SteamState.NotRunning ? "Starting Steam" : "Waiting for Steam";
          Log.Info(SteamStatus);
        },
        CancellationToken.None);
      SteamStatus = "";

      if (direct is not null)
      {
        SelectInGame(direct);
      }

      LogLaunch(instance, plan, direct);
      var started = DateTimeOffset.Now;
      using var process = Process.Start(plan.ToStartInfo()) ?? throw new LaunchException("The game did not start.");
      Log.Info($"Valheim started, process {process.Id}.");
      DebugConsole.GameStarted(instance);
      RunningInstanceId = instance?.Id ?? "";
      _runningDestination = direct?.Name ?? "";
      IsGameRunning = true;
      Raise(nameof(RunningName));

      // Only once the game is up, so a launch that failed isn't what the sidebar offers next.
      SettingsModel.LastPlay = new LastPlay
      {
        DestinationKey = direct?.Key,
        DestinationName = direct?.Name,
        InstanceId = direct is null ? instance?.Id : null,
      };
      SaveSettings();
      Raise(nameof(LastPlayName));

      if (instance is not null)
      {
        instance.Model.LastPlayedAt = DateTimeOffset.UtcNow;
        Instances.Save(instance.Model);
        Select(instance);
      }

      RefreshRunning();
      await process.WaitForExitAsync();
      Log.Info($"Valheim exited with code {process.ExitCode} after {DateTimeOffset.Now - started:h\\:mm\\:ss}.");
    }
    catch (Exception ex)
    {
      Log.Error("Starting Valheim failed", ex is LaunchException ? null : ex);
      Toast("error", "Could not start Valheim", ex.Message);
    }
    finally
    {
      SteamStatus = "";
      IsGameRunning = false;
      RunningInstanceId = "";
      _runningDestination = "";
      Raise(nameof(RunningName));
      RefreshRunning();
      Play.RefreshIfLoaded();
    }
  }

  /// <summary>Sets the character and world Valheim's menus start on (see <see cref="PlayViewModel"/>).</summary>
  /// <remarks>
  /// Written right before the start, because Unity reads PlayerPrefs once when the game starts and
  /// writes all of them back when it quits. A failure only costs the preselection, so the game still starts.
  /// </remarks>
  private void SelectInGame(DirectPlay direct)
  {
    try
    {
      var prefs = UnityPrefs.ForValheim(Play.Saves.DataDirectory);
      foreach (var (key, value) in direct.Prefs)
      {
        prefs.SetString(key, value);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      Log.Error("Setting Valheim's selected character failed", ex);
      Toast("error", "Could not preselect the character", ex.Message);
    }
  }

  /// <summary>What the game was started with, so a launch that goes wrong can be reproduced by hand.</summary>
  private void LogLaunch(InstanceViewModel? instance, LaunchPlan plan, DirectPlay? direct)
  {
    Log.Info($"Starting {(instance is null ? "vanilla Valheim" : $"{instance.Name} ({instance.Directory})")}{(direct is null ? "" : $" for {direct.Name}")}");
    foreach (var (key, value) in direct?.Prefs ?? new Dictionary<string, string>())
    {
      Log.Info($"  PlayerPrefs {key} = {value}");
    }

    Log.Info($"  {plan.FileName} {string.Join(' ', GameLauncher.Redact(plan.Arguments))}".TrimEnd());
    foreach (var (key, value) in plan.Environment.Where(e => e.Value is not null).OrderBy(e => e.Key, StringComparer.Ordinal))
    {
      Log.Info($"  {key}={value}");
    }
  }

  private void RefreshRunning()
  {
    foreach (var instance in _instances)
    {
      instance.RaiseRunning();
    }

    Raise(nameof(RecentInstance));
  }

  internal void Duplicate(InstanceViewModel source) => _ = DuplicateAsync(source);

  private async Task DuplicateAsync(InstanceViewModel source)
  {
    var activity = BeginActivity("Copying " + source.Name);
    try
    {
      var copy = await Task.Run(() => Instances.Duplicate(source.Model, source.Name + " (copy)"));
      AddInstance(new InstanceViewModel(this, copy));
      Toast("success", "Duplicated", $"Created {copy.Name}.");
    }
    catch (Exception ex)
    {
      Toast("error", "Could not duplicate", ex.Message);
    }
    finally
    {
      EndActivity(activity);
    }
  }

  internal void Delete(InstanceViewModel instance)
  {
    if (instance.IsRunning)
    {
      Toast("error", "Still running", "Close Valheim before deleting this instance.");
      return;
    }

    try
    {
      Instances.Delete(instance.Model);
    }
    catch (IOException ex)
    {
      Toast("error", "Could not delete", ex.Message);
      return;
    }

    InstanceItems = _instances.Where(i => i != instance).ToList();
    if (_selected == instance)
    {
      Select(_instances.FirstOrDefault());
    }

    DebugConsole.InstanceRemoved(instance);

    if (CurrentPage == "instance")
    {
      CurrentPage = "library";
    }

    InstancesChanged();
    Toast("success", "Deleted", $"{instance.Name} and its files were removed.");
  }

  internal void BrowseFor(InstanceViewModel instance)
  {
    Select(instance);
    Browse.SetTarget(instance);
    Navigate("browse");
  }

  /// <summary>Starts an install from the browser, or explains why the site wants the user to click through.</summary>
  internal async void InstallFromCatalog(InstanceViewModel? target, InstallRequest request, string label)
  {
    if (target is null)
    {
      Toast("info", "No instance yet", "Create an instance first, then install mods into it.");
      return;
    }

    var report = await target.InstallAsync("Installing " + label, progress => Mods.InstallAsync(target.Model, request, progress, CancellationToken.None));
    if (report?.Browser is { } browser)
    {
      if (request.Source == ModSource.Nexus)
      {
        _pendingNexus[request.ModId] = target.Id;
      }

      BrowserPrompt.Title = $"Download {label} on the website";
      BrowserPrompt.Reason = browser.Reason;
      BrowserPrompt.Url = browser.PageUrl.AbsoluteUri;
      BrowserPrompt.WaitsForNxm = request.Source == ModSource.Nexus && Catalogs.Nexus.HasApiKey;
      BrowserPrompt.Visible = true;
      return;
    }

    if (report is not null)
    {
      ReportInstall(report, target.Name);
    }
  }

  /// <summary>Brings the window to the front, for something that needs an answer now.</summary>
  internal void Activate() => this.ActivateSignal("activateRequested");

  /// <summary>Handles a command line forwarded by a second start, usually an nxm:// link.</summary>
  public void HandleCommandLine(string line)
  {
    this.ActivateSignal("activateRequested");
    if (NxmLink.TryParse(line.Trim(), out var link))
    {
      _ = InstallNxmAsync(link);
    }
  }

  private async Task InstallNxmAsync(NxmLink link)
  {
    var target = (_pendingNexus.Remove(link.ModId, out var id) ? _instances.FirstOrDefault(i => i.Id == id) : null)
      ?? Browse.Target ?? _selected;
    if (target is null)
    {
      Toast("error", "No instance", "Create an instance first, then click the download button on Nexus again.");
      return;
    }

    var report = await target.InstallAsync($"Nexus mod {link.ModId}", progress => Mods.InstallNxmAsync(target.Model, link, progress, CancellationToken.None));
    if (report is not null)
    {
      ReportInstall(report, target.Name);
    }
  }

  internal void ReportInstall(InstallReport report, string instanceName, bool quiet = false)
  {
    foreach (var warning in report.Warnings)
    {
      Toast("error", "Heads up", warning);
    }

    var main = report.Installed.LastOrDefault(m => !m.InstalledAsDependency) ?? report.Installed.LastOrDefault();
    if (quiet || main is null)
    {
      return;
    }

    var extra = report.Installed.Count - 1;
    Toast("success", $"Installed {main.Name} {main.Version}",
      extra > 0 ? $"Into {instanceName}, with {extra} dependenc{(extra == 1 ? "y" : "ies")}." : $"Into {instanceName}.");
  }

  internal ActivityItem BeginActivity(string title)
  {
    var item = new ActivityItem(title);
    Activities = [.. _activities, item];
    return item;
  }

  internal void EndActivity(ActivityItem item) => Activities = _activities.Where(a => a != item).ToList();

  internal async void Toast(string kind, string title, string message, string actionLabel = "", string actionUrl = "")
  {
    // Errors also go to stderr, so they end up in the journal when started from the launcher.
    Log.Write(kind == "error" ? LogLevel.Error : LogLevel.Info, $"{title}: {message}".TrimEnd(' ', ':'));

    var toast = new ToastItem(kind, title, message, actionLabel, actionUrl, DismissToast);
    Toasts = [.. _toasts.TakeLast(3), toast];
    await Task.Delay(kind == "error" ? 9000 : 5000);
    DismissToast(toast);
  }

  private void DismissToast(ToastItem toast)
  {
    if (_toasts.Contains(toast))
    {
      Toasts = _toasts.Where(t => t != toast).ToList();
    }
  }

  internal void SaveSettings() => _settingsStore.Save(SettingsModel);

  internal void InstancesChanged()
  {
    Raise(nameof(InstanceCount));
    Raise(nameof(RecentInstance));
    Raise(nameof(LastPlayName));
    Browse.InstancesChanged();
    Play.InstancesChanged();
  }

  internal void InstallStateChanged(InstanceViewModel instance)
  {
    if (Browse.Target == instance)
    {
      Browse.RefreshInstalledState();
    }
  }

  internal void RefreshUpdates()
  {
    foreach (var instance in _instances)
    {
      instance.Refresh();
    }
  }

  /// <summary>Loads the Thunderstore list in the background so update badges and search are ready.</summary>
  internal async void WarmUp()
  {
    try
    {
      await Task.Run(() => Catalogs.Thunderstore.Index.EnsureLoadedAsync(forceRefresh: false, CancellationToken.None));
      RefreshUpdates();
    }
    catch (Exception)
    {
      // Offline on first start; the browser shows the error when it is opened.
    }

    await Settings.ValidateAllAsync();
  }

  private void AddInstance(InstanceViewModel instance)
  {
    InstanceItems = [instance, .. _instances];
    InstancesChanged();
  }

  private void Select(InstanceViewModel? instance)
  {
    SelectedInstance = instance;
    SettingsModel.LastInstanceId = instance?.Id;
    SaveSettings();
  }
}
