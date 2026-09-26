using System.Diagnostics;
using CINE.LaunchHeim.Core.Catalogs;
using CINE.LaunchHeim.Core.Catalogs.Nexus;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;
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
  private readonly Dictionary<string, string> _pendingNexus = new();
  private List<InstanceViewModel> _instances = [];
  private List<ActivityItem> _activities = [];
  private List<ToastItem> _toasts = [];
  private InstanceViewModel? _selected;
  private string _page = "library";
  private string _runningInstanceId = "";
  private bool _isGameRunning;

  public AppViewModel(
    AppPaths paths,
    SettingsStore settingsStore,
    InstanceStore instances,
    ModService mods,
    CatalogRegistry catalogs,
    GameFolderImporter importer,
    ImageCache images)
  {
    Paths = paths;
    _settingsStore = settingsStore;
    SettingsModel = settingsStore.Load();
    Instances = instances;
    Mods = mods;
    Catalogs = catalogs;
    Images = images;
    _importer = importer;

    Theme = new ThemeViewModel();
    Browse = new BrowseViewModel(this);
    Settings = new SettingsViewModel(this);
    BrowserPrompt = new BrowserPromptViewModel();

    _instances = instances.LoadAll().Select(i => new InstanceViewModel(this, i)).ToList();
    _selected = _instances.FirstOrDefault(i => i.Id == SettingsModel.LastInstanceId) ?? _instances.FirstOrDefault();
    Browse.SetTarget(_selected);
  }

  internal AppPaths Paths { get; }
  internal AppSettings SettingsModel { get; }
  internal InstanceStore Instances { get; }
  internal ModService Mods { get; }
  internal CatalogRegistry Catalogs { get; }
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
  public string Version => Core.AppInfo.Version;

  /// <summary>library, instance, browse or settings.</summary>
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

  [NotifySignal]
  public string RunningInstanceId { get => _runningInstanceId; private set => Set(ref _runningInstanceId, value); }

  [NotifySignal]
  public string RunningName => _instances.FirstOrDefault(i => i.Id == _runningInstanceId)?.Name ?? (IsGameRunning ? "Vanilla Valheim" : "");

  public void Navigate(string page)
  {
    CurrentPage = page;
    if (page == "browse")
    {
      Browse.EnsureLoaded();
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

  public void LaunchVanilla() => StartGame(null);

  internal void Launch(InstanceViewModel instance) => StartGame(instance);

  private async void StartGame(InstanceViewModel? instance)
  {
    if (IsGameRunning)
    {
      Toast("info", "Valheim is running", "Close the game before starting another instance.");
      return;
    }

    try
    {
      var plan = GameLauncher.Plan(
        Settings.GameDirectory,
        instance is null ? null : Instances.DirectoryOf(instance.Model),
        instance?.Model.LaunchArguments ?? SettingsModel.VanillaLaunchArguments,
        GameLauncher.CurrentEnvironment());

      using var process = Process.Start(plan.ToStartInfo()) ?? throw new LaunchException("The game did not start.");
      RunningInstanceId = instance?.Id ?? "";
      IsGameRunning = true;
      Raise(nameof(RunningName));

      if (instance is not null)
      {
        instance.Model.LastPlayedAt = DateTimeOffset.UtcNow;
        Instances.Save(instance.Model);
        Select(instance);
      }

      RefreshRunning();
      await process.WaitForExitAsync();
    }
    catch (Exception ex)
    {
      Toast("error", "Could not start Valheim", ex.Message);
    }
    finally
    {
      IsGameRunning = false;
      RunningInstanceId = "";
      Raise(nameof(RunningName));
      RefreshRunning();
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
    if (kind == "error")
    {
      // Errors also go to stderr, so they end up in the journal when started from the launcher.
      Console.Error.WriteLine($"LaunchHeim: {title}: {message}");
    }

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
    Browse.InstancesChanged();
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
