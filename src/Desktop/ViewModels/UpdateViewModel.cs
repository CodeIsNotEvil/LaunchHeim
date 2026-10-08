using System.ComponentModel;
using System.Diagnostics;
using CINE.LaunchHeim.Core;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Updates;
using CINE.LaunchHeim.Desktop.Hosting;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>
/// The reminder that a newer LaunchHeim is out: a line in the sidebar, and a dialog with the download
/// page, the commands that update this kind of install, "Skip this version" and "Don't remind me again".
/// A copy the Windows setup installed also gets "Update now", which runs the new version's setup.
/// </summary>
/// <remarks><c>quitRequested</c> asks QML to close LaunchHeim, so the setup can replace its files.</remarks>
[Signal("quitRequested")]
public sealed class UpdateViewModel : ViewModel
{
  // Twice a day while LaunchHeim stays open, which people do between sessions.
  private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

  private readonly AppViewModel _app;
  private readonly UpdateChecker _checker;
  private readonly InstallKind _kind;
  private AvailableUpdate? _update;
  private bool _dialogVisible;
  private bool _checking;
  private bool _installing;
  private double _installProgress;
  private string _installError = "";

  public UpdateViewModel(AppViewModel app, UpdateChecker checker)
  {
    _app = app;
    _checker = checker;
    _kind = InstallDetection.Detect(
      OperatingSystem.IsWindows(),
      DistroPackage.IsInstalled,
      OperatingSystem.IsLinux() ? ReadOsRelease() : null,
      AppContext.BaseDirectory,
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
      OperatingSystem.IsWindows() ? WindowsSetup.InstallFolders().ToList() : null);
    Log.Info($"Installed as {_kind}.");
  }

  /// <summary>Whether the sidebar shows the reminder: something newer, not skipped, reminders on.</summary>
  [NotifySignal]
  public bool Available => _update is { } update && RemindersEnabled && update.Version != _app.SettingsModel.SkippedUpdateVersion;

  [NotifySignal]
  public string Version => _update?.Version ?? "";

  [NotifySignal]
  public string Text => _update is null ? "" : $"LaunchHeim {_update.Version} is available";

  /// <summary>The sidebar's line, without the app's own name: the sidebar is narrow.</summary>
  [NotifySignal]
  public string ShortText => _update is null ? "" : $"Version {_update.Version} is available";

  [NotifySignal]
  public bool DialogVisible { get => _dialogVisible; private set => Set(ref _dialogVisible, value); }

  /// <summary>Empty when the system couldn't be told, and the dialog then only links the download page.</summary>
  [NotifySignal]
  public string Commands => _update is null ? "" : string.Join('\n', UpdateCommands.For(_kind, _update, AppContext.BaseDirectory));

  [NotifySignal]
  public bool HasCommands => Commands.Length > 0;

  [NotifySignal]
  public string SystemName => InstallDetection.DisplayName(_kind);

  [NotifySignal]
  public string DownloadUrl => UpdateChecker.DownloadPage;

  [NotifySignal]
  public string ReleaseUrl => _update?.ReleaseUrl ?? UpdateChecker.DownloadPage;

  [NotifySignal]
  public bool RemindersEnabled => !_app.SettingsModel.UpdateRemindersDisabled;

  [NotifySignal]
  public bool Checking { get => _checking; private set => Set(ref _checking, value); }

  /// <summary>Whether "Update now" is offered: installed by the setup, and the release has a setup.</summary>
  [NotifySignal]
  public bool CanInstall => _kind == InstallKind.WindowsSetup && _update?.WindowsSetup is not null;

  [NotifySignal]
  public bool Installing { get => _installing; private set => Set(ref _installing, value); }

  /// <summary>How much of the setup has been downloaded, 0 to 1.</summary>
  [NotifySignal]
  public double InstallProgress { get => _installProgress; private set => Set(ref _installProgress, value); }

  /// <summary>Why "Update now" failed, for the dialog; empty otherwise.</summary>
  [NotifySignal]
  public string InstallError { get => _installError; private set => Set(ref _installError, value); }

  /// <summary>Checks now, then every <see cref="CheckInterval"/> while LaunchHeim runs.</summary>
  internal async void StartChecking()
  {
    await CheckAsync();
    using var timer = new PeriodicTimer(CheckInterval);
    // Awaited on the Qt thread, which Qml.Net's SynchronizationContext brings each tick back to.
    while (await timer.WaitForNextTickAsync())
    {
      await CheckAsync();
    }
  }

  public void CheckNow() => _ = CheckAsync();

  private async Task CheckAsync()
  {
    if (!RemindersEnabled || Checking)
    {
      return;
    }

    Checking = true;
    try
    {
      var found = await Task.Run(() => _checker.CheckAsync(AppInfo.Version, CancellationToken.None));
      if (found is not null && found.Version != _update?.Version)
      {
        Log.Info($"LaunchHeim {found.Version} is available ({found.ReleaseUrl}); this is {AppInfo.Version}, installed as {_kind}.");
      }

      _update = found;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
    {
      // Offline, rate-limited or a changed API: no reminder this time, and nothing to bother anyone with.
      Log.Info($"Checking for a new LaunchHeim failed: {ex.Message}");
    }
    finally
    {
      Checking = false;
    }

    RaiseAll();
  }

  public void OpenDialog()
  {
    if (_update is not null)
    {
      DialogVisible = true;
    }
  }

  public void CloseDialog() => DialogVisible = false;

  /// <summary>Downloads the new version's setup, checks it, starts it and closes LaunchHeim.</summary>
  /// <remarks>
  /// The setup runs with /SILENT: it shows its progress but asks nothing, and reuses the folder, the
  /// shortcuts and the install mode (per user or all users, the latter with a UAC prompt) chosen the
  /// first time. /CLOSEAPPLICATIONS covers LaunchHeim still shutting down when the setup starts copying.
  /// <c>/relaunch=yes</c> is the setup's own switch (launchheim.iss) that starts LaunchHeim again at the
  /// end; a silent setup run by hand doesn't.
  /// </remarks>
  public async void InstallNow()
  {
    if (!CanInstall || Installing || _update?.WindowsSetup is not { } asset)
    {
      return;
    }

    var version = _update.Version;
    Installing = true;
    InstallError = "";
    InstallProgress = 0;
    try
    {
      // Created here, on the Qt thread, so the reports come back to it from the download's thread.
      var progress = new Progress<double>(value => InstallProgress = value);
      // In the cache's tmp folder, which the next start empties again.
      var folder = Path.Combine(_app.Paths.CacheDirectory, "tmp", "update");
      var setup = await Task.Run(() => _checker.DownloadAsync(asset, folder, progress, CancellationToken.None));

      Log.Info($"Starting {setup} to update to {version}.");
      Process.Start(new ProcessStartInfo(setup, ["/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/relaunch=yes"])
      {
        UseShellExecute = false,
      });
      this.ActivateSignal("quitRequested");
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception)
    {
      Log.Error($"Updating to {version} failed", ex);
      InstallError = $"The update couldn't be installed: {ex.Message}";
    }
    finally
    {
      Installing = false;
    }
  }

  public void OpenDownloadPage()
  {
    DesktopShell.Open(UpdateChecker.DownloadPage);
    DialogVisible = false;
  }

  public void OpenReleaseNotes() => DesktopShell.Open(ReleaseUrl);

  /// <summary>No reminder for this version; the next one is announced again.</summary>
  public void Skip()
  {
    _app.SettingsModel.SkippedUpdateVersion = _update?.Version;
    _app.SaveSettings();
    DialogVisible = false;
    RaiseAll();
  }

  /// <summary>No reminders at all, for this or any later version, until Settings switches them on.</summary>
  public void Never()
  {
    _app.SettingsModel.UpdateRemindersDisabled = true;
    _app.SaveSettings();
    DialogVisible = false;
    RaiseAll();
  }

  public void SetRemindersEnabled(bool enabled)
  {
    _app.SettingsModel.UpdateRemindersDisabled = !enabled;
    _app.SaveSettings();
    RaiseAll();
    if (enabled)
    {
      CheckNow();
    }
  }

  private void RaiseAll()
  {
    foreach (var property in new[] { nameof(Available), nameof(Version), nameof(Text), nameof(ShortText), nameof(Commands), nameof(HasCommands), nameof(ReleaseUrl), nameof(RemindersEnabled), nameof(CanInstall) })
    {
      Raise(property);
    }
  }

  /// <summary>os-release, from either place the spec allows, or null where a sandbox hides both.</summary>
  private static string? ReadOsRelease()
  {
    foreach (var path in new[] { Path.Combine("/etc", "os-release"), Path.Combine("/usr", "lib", "os-release") })
    {
      try
      {
        return File.ReadAllText(path);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
      }
    }

    return null;
  }
}
