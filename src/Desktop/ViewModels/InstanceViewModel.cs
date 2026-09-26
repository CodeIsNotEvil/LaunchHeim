using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Desktop.Hosting;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

public sealed class InstanceViewModel : ViewModel
{
  // Muted versions of the Breeze accent palette, so cards are told apart without shouting.
  private static readonly string[] Palette = ["#3daee9", "#1abc9c", "#9b59b6", "#f67400", "#da4453", "#27ae60", "#fdbc4b", "#2980b9", "#e93d8f"];

  private readonly AppViewModel _app;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private List<InstalledModViewModel> _mods = [];
  private List<ConfigFileViewModel> _configFiles = [];
  private bool _isBusy;
  private string _filter = "";

  public InstanceViewModel(AppViewModel app, Instance instance)
  {
    _app = app;
    Model = instance;
    Refresh();
  }

  internal Instance Model { get; }

  [NotifySignal]
  public string Id => Model.Id;

  [NotifySignal]
  public string Name => Model.Name;

  public string Initials
  {
    get
    {
      var words = Model.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      return words.Length switch
      {
        0 => "?",
        1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
        _ => $"{words[0][0]}{words[1][0]}".ToUpperInvariant(),
      };
    }
  }

  [NotifySignal]
  public string Color => Palette[(int)((uint)StableHash(Model.Id) % Palette.Length)];

  [NotifySignal]
  public string Directory => _app.Instances.DirectoryOf(Model);

  [NotifySignal]
  public int ModCount => Model.Mods.Count(m => !m.IsLoader);

  [NotifySignal]
  public int EnabledModCount => Model.Mods.Count(m => !m.IsLoader && m.Enabled);

  [NotifySignal]
  public bool HasLoader => Model.Loader is not null;

  [NotifySignal]
  public string LoaderVersion => Model.Loader?.Version ?? "";

  [NotifySignal]
  public string LastPlayedText => Model.LastPlayedAt is null ? "Never played" : "Played " + Format.Ago(Model.LastPlayedAt);

  [NotifySignal]
  public string Summary => ModCount switch
  {
    0 => HasLoader ? "BepInEx only" : "Empty",
    1 => "1 mod",
    var n => $"{n} mods",
  };

  [NotifySignal]
  public string LaunchArguments => Model.LaunchArguments;

  [NotifySignal]
  public List<InstalledModViewModel> Mods { get => _mods; private set => Set(ref _mods, value); }

  /// <summary>The mods list narrowed by the search box on the instance page.</summary>
  [NotifySignal]
  public List<InstalledModViewModel> FilteredMods => _filter.Length == 0
    ? _mods
    : _mods.Where(m => m.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || m.Author.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();

  [NotifySignal]
  public string Filter
  {
    get => _filter;
    set
    {
      if (Set(ref _filter, value ?? ""))
      {
        Raise(nameof(FilteredMods));
      }
    }
  }

  [NotifySignal]
  public List<ConfigFileViewModel> ConfigFiles { get => _configFiles; private set => Set(ref _configFiles, value); }

  [NotifySignal]
  public int UpdateCount => _mods.Count(m => m.HasUpdate);

  [NotifySignal]
  public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

  [NotifySignal]
  public bool IsRunning => _app.RunningInstanceId == Model.Id;

  public void Play() => _app.Launch(this);

  public void OpenFolder() => DesktopShell.Open(Directory);

  public void OpenConfigFolder()
  {
    var config = Path.Combine(Directory, "BepInEx", "config");
    System.IO.Directory.CreateDirectory(config);
    DesktopShell.Open(config);
  }

  public void OpenLog()
  {
    var log = Path.Combine(Directory, "BepInEx", "LogOutput.log");
    if (File.Exists(log))
    {
      DesktopShell.Open(log);
    }
    else
    {
      _app.Toast("info", "No log yet", "BepInEx writes LogOutput.log the first time the instance is played.");
    }
  }

  public void BrowseMods() => _app.BrowseFor(this);

  public void Rename(string name)
  {
    if (string.IsNullOrWhiteSpace(name) || name.Trim() == Model.Name)
    {
      return;
    }

    Model.Name = name.Trim();
    _app.Instances.Save(Model);
    Raise(nameof(Name));
    _app.InstancesChanged();
  }

  public void SaveLaunchArguments(string arguments)
  {
    Model.LaunchArguments = arguments?.Trim() ?? "";
    _app.Instances.Save(Model);
    Raise(nameof(LaunchArguments));
    _app.Toast("success", "Saved", "Launch arguments updated.");
  }

  public void Duplicate() => _app.Duplicate(this);

  public void Delete() => _app.Delete(this);

  public void InstallLoader() => RunExclusive("Installing BepInEx", async progress =>
  {
    var report = await Task.Run(() => _app.Mods.InstallLoaderAsync(Model, progress, CancellationToken.None));
    _app.ReportInstall(report, Model.Name);
  });

  /// <param name="fileUrl">A file:// URL from the QML file dialog.</param>
  public void InstallFile(string fileUrl)
  {
    var path = fileUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(fileUrl).LocalPath : fileUrl;
    RunExclusive("Installing " + Path.GetFileName(path), async progress =>
    {
      var report = await Task.Run(() => _app.Mods.InstallFileAsync(Model, path, progress, CancellationToken.None));
      _app.ReportInstall(report, Model.Name);
    });
  }

  public void CheckUpdates() => RunExclusive("Checking for updates", async _ =>
  {
    await Task.Run(() => _app.Catalogs.Thunderstore.Index.EnsureLoadedAsync(forceRefresh: true, CancellationToken.None));
    ApplyUpdates();
    _app.Toast("info", "Updates", UpdateCount == 0 ? "Everything is up to date." : $"{UpdateCount} update(s) available.");
  });

  public void UpdateAll() => RunExclusive("Updating mods", async progress =>
  {
    var keys = _mods.Where(m => m.HasUpdate).Select(m => m.Key).ToList();
    foreach (var key in keys)
    {
      var report = await Task.Run(() => _app.Mods.UpdateAsync(Model, key, progress, CancellationToken.None));
      _app.ReportInstall(report, Model.Name, quiet: true);
    }

    _app.Toast("success", "Updated", $"{keys.Count} mod(s) updated in {Model.Name}.");
  });

  internal void SetModEnabled(InstalledModViewModel mod, bool enabled)
  {
    if (!enabled && Model.Dependents(mod.Key).Where(d => d.Enabled).Select(d => d.Name).ToList() is { Count: > 0 } dependents)
    {
      _app.Toast("info", $"{mod.Name} is needed", $"{string.Join(", ", dependents)} will not work while it is off.");
    }

    RunExclusive(null, async _ =>
    {
      var changed = await Task.Run(() => _app.Mods.SetEnabledAsync(Model, mod.Key, enabled));
      if (changed.Count > 1)
      {
        _app.Toast("info", "Dependencies enabled", string.Join(", ", changed.Where(m => m.Key != mod.Key).Select(m => m.Name)));
      }
    });
  }

  internal void RemoveMod(InstalledModViewModel mod) => RunExclusive("Removing " + mod.Name, async _ =>
  {
    var report = await Task.Run(() => _app.Mods.RemoveAsync(Model, mod.Key, CancellationToken.None));
    var extra = report.Removed.Where(m => m.Key != mod.Key).Select(m => m.Name).ToList();
    _app.Toast("success", $"Removed {mod.Name}", extra.Count == 0 ? "" : "Also removed unused " + string.Join(", ", extra) + ".");
  });

  internal void UpdateMod(InstalledModViewModel mod) => RunExclusive("Updating " + mod.Name, async progress =>
  {
    var report = await Task.Run(() => _app.Mods.UpdateAsync(Model, mod.Key, progress, CancellationToken.None));
    _app.ReportInstall(report, Model.Name);
  });

  /// <summary>Installs from the browser; used by the Browse page and nxm links.</summary>
  internal Task<InstallReport?> InstallAsync(string title, Func<IProgress<InstallProgress>, Task<InstallReport>> install)
  {
    var completion = new TaskCompletionSource<InstallReport?>();
    RunExclusive(title, async progress =>
    {
      try
      {
        completion.SetResult(await Task.Run(() => install(progress)));
      }
      catch
      {
        // RunExclusive reports the error; the caller only needs to know nothing was installed.
        completion.TrySetResult(null);
        throw;
      }
    });
    return completion.Task;
  }

  /// <summary>
  /// Runs one change at a time per instance and rebuilds the view models afterwards on the Qt thread,
  /// so the list QML shows is never read while a background install is changing it.
  /// </summary>
  private async void RunExclusive(string? activityTitle, Func<IProgress<InstallProgress>, Task> work)
  {
    var activity = activityTitle is null ? null : _app.BeginActivity(activityTitle);
    await _gate.WaitAsync();
    IsBusy = true;
    try
    {
      await work(activity?.CreateProgress() ?? new Progress<InstallProgress>());
    }
    catch (Exception ex)
    {
      _app.Toast("error", activityTitle ?? "Something went wrong", ex.Message);
    }
    finally
    {
      Refresh();
      IsBusy = false;
      _gate.Release();
      if (activity is not null)
      {
        _app.EndActivity(activity);
      }

      _app.InstallStateChanged(this);
    }
  }

  internal void Refresh()
  {
    var mods = Model.Mods
      .OrderByDescending(m => m.IsLoader)
      .ThenBy(m => m.InstalledAsDependency)
      .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
      .Select(m => new InstalledModViewModel(this, m, Model.Dependents(m.Key).Select(d => d.Name).ToList(), _app.Images))
      .ToList();
    Mods = mods;
    ApplyUpdates();
    RefreshConfigFiles();

    Raise(nameof(FilteredMods));
    Raise(nameof(ModCount));
    Raise(nameof(EnabledModCount));
    Raise(nameof(HasLoader));
    Raise(nameof(LoaderVersion));
    Raise(nameof(Summary));
    Raise(nameof(LastPlayedText));
  }

  internal void RaiseRunning()
  {
    Raise(nameof(IsRunning));
    Raise(nameof(LastPlayedText));
  }

  private void ApplyUpdates()
  {
    var updates = _app.Mods.FindUpdates(Model);
    foreach (var mod in _mods)
    {
      mod.UpdateVersion = updates.GetValueOrDefault(mod.Key) ?? "";
    }

    Raise(nameof(UpdateCount));
  }

  private void RefreshConfigFiles()
  {
    var config = Path.Combine(Directory, "BepInEx", "config");
    ConfigFiles = System.IO.Directory.Exists(config)
      ? System.IO.Directory.EnumerateFiles(config, "*.cfg", SearchOption.AllDirectories)
          .Order(StringComparer.OrdinalIgnoreCase)
          .Select(f => new ConfigFileViewModel(f, Directory))
          .ToList()
      : [];
  }

  // string.GetHashCode is randomized per process; the card color must stay the same across starts.
  private static int StableHash(string value)
  {
    unchecked
    {
      var hash = 23;
      foreach (var c in value)
      {
        hash = hash * 31 + c;
      }

      return hash;
    }
  }
}
