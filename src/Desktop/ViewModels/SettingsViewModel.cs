using CINE.LaunchHeim.Core.Catalogs.CurseForge;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Desktop.Hosting;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModel
{
  private readonly AppViewModel _app;
  private string _gameDirectory = "";
  private string _nexusStatus = "";
  private string _curseForgeStatus = "";
  private bool _nxmRegistered;
  private bool _validating;

  public SettingsViewModel(AppViewModel app)
  {
    _app = app;
    ResolveGameDirectory();
    _ = RefreshNxmAsync();
  }

  [NotifySignal]
  public string GameDirectory { get => _gameDirectory; private set => Set(ref _gameDirectory, value); }

  [NotifySignal]
  public bool GameFound => SteamLibraryLocator.IsValheimDirectory(_gameDirectory);

  [NotifySignal]
  public bool GameDirectoryIsCustom => !string.IsNullOrEmpty(_app.SettingsModel.GameDirectory);

  /// <summary>BepInEx installed straight into the game folder, which can be imported as an instance.</summary>
  [NotifySignal]
  public bool GameFolderHasBepInEx => GameFolderImporter.HasBepInEx(_gameDirectory);

  [NotifySignal]
  public string NexusApiKey => _app.SettingsModel.NexusApiKey ?? "";

  [NotifySignal]
  public string CurseForgeApiKey => _app.SettingsModel.CurseForgeApiKey ?? "";

  [NotifySignal]
  public string NexusStatus { get => _nexusStatus; private set => Set(ref _nexusStatus, value); }

  [NotifySignal]
  public string CurseForgeStatus { get => _curseForgeStatus; private set => Set(ref _curseForgeStatus, value); }

  [NotifySignal]
  public bool Validating { get => _validating; private set => Set(ref _validating, value); }

  [NotifySignal]
  public bool ShowNsfw => _app.SettingsModel.ShowNsfw;

  [NotifySignal]
  public bool NxmRegistered { get => _nxmRegistered; private set => Set(ref _nxmRegistered, value); }

  [NotifySignal]
  public string QtRuntime => Hosting.QtRuntime.Description;

  [NotifySignal]
  public string Version => Core.AppInfo.Version;

  /// <summary>Where the colors come from, for the page subtitle and About.</summary>
  [NotifySignal]
  public string ThemeSource => OperatingSystem.IsWindows() ? "the Windows app mode and accent color" : "your Plasma color scheme and accent color";

  [NotifySignal]
  public string NxmHint => OperatingSystem.IsWindows()
    ? "Registers LaunchHeim for nxm:// links for your Windows account."
    : DistroPackage.IsInstalled
      ? "Makes LaunchHeim the default app for nxm:// links."
      : "Registers LaunchHeim for nxm:// links and adds it to your application launcher.";

  [NotifySignal]
  public string DataDirectory => _app.Paths.DataDirectory;

  [NotifySignal]
  public string CacheDirectory => _app.Paths.CacheDirectory;

  public void SetGameDirectory(string folderUrl)
  {
    var path = folderUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(folderUrl).LocalPath : folderUrl;
    if (!SteamLibraryLocator.IsValheimDirectory(path))
    {
      _app.Toast("error", "Not a Valheim folder", $"{path} does not contain {SteamLibraryLocator.ValheimExecutable}.");
      return;
    }

    _app.SettingsModel.GameDirectory = path;
    _app.SaveSettings();
    ResolveGameDirectory();
  }

  public void DetectGame()
  {
    _app.SettingsModel.GameDirectory = null;
    _app.SaveSettings();
    ResolveGameDirectory();
    _app.Toast(GameFound ? "success" : "error", GameFound ? "Valheim found" : "Valheim not found",
      GameFound ? GameDirectory : "Install Valheim through Steam, or pick its folder by hand.");
  }

  public void OpenGameDirectory() => DesktopShell.Open(_gameDirectory);

  public void SaveNexusApiKey(string key) => _ = SaveNexusApiKeyAsync(key);

  private async Task SaveNexusApiKeyAsync(string key)
  {
    _app.SettingsModel.NexusApiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    _app.SaveSettings();
    Raise(nameof(NexusApiKey));
    await ValidateNexusAsync();
    _app.Browse.SettingsChanged();
  }

  public void SaveCurseForgeApiKey(string key) => _ = SaveCurseForgeApiKeyAsync(key);

  private async Task SaveCurseForgeApiKeyAsync(string key)
  {
    _app.SettingsModel.CurseForgeApiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    _app.SaveSettings();
    Raise(nameof(CurseForgeApiKey));
    await ValidateCurseForgeAsync();
    _app.Browse.SettingsChanged();
  }

  public void SetShowNsfw(bool value)
  {
    _app.SettingsModel.ShowNsfw = value;
    _app.SaveSettings();
    Raise(nameof(ShowNsfw));
    _app.Browse.SettingsChanged();
  }

  public void RegisterNxmHandler() => _ = RegisterNxmHandlerAsync();

  private async Task RegisterNxmHandlerAsync()
  {
    try
    {
      await NxmHandler.RegisterAsync();
      await RefreshNxmAsync();
      _app.Toast(NxmRegistered ? "success" : "error", "Nexus links",
        NxmRegistered ? "\"Mod Manager Download\" buttons on Nexus now open LaunchHeim." : "The system did not accept LaunchHeim as the handler.");
    }
    catch (Exception ex)
    {
      _app.Toast("error", "Nexus links", ex.Message);
    }
  }

  public void OpenDataDirectory() => DesktopShell.Open(_app.Paths.DataDirectory);

  public void ClearDownloadCache()
  {
    try
    {
      if (Directory.Exists(_app.Paths.DownloadsDirectory))
      {
        Directory.Delete(_app.Paths.DownloadsDirectory, recursive: true);
      }

      _app.Toast("success", "Cache cleared", "Downloaded mod archives were removed. Installed mods are not affected.");
    }
    catch (IOException ex)
    {
      _app.Toast("error", "Cache", ex.Message);
    }
  }

  internal async Task ValidateAllAsync()
  {
    await ValidateNexusAsync();
    await ValidateCurseForgeAsync();
  }

  private async Task ValidateNexusAsync()
  {
    if (string.IsNullOrEmpty(_app.SettingsModel.NexusApiKey))
    {
      NexusStatus = "";
      return;
    }

    Validating = true;
    try
    {
      var account = await Task.Run(() => _app.Catalogs.Nexus.ValidateAsync(CancellationToken.None));
      NexusStatus = account.IsPremium
        ? $"Signed in as {account.Name} (Premium): installs download directly."
        : $"Signed in as {account.Name}. Free accounts confirm each download on the Nexus page.";
    }
    catch (Exception ex)
    {
      NexusStatus = "Could not verify the key: " + ex.Message;
    }
    finally
    {
      Validating = false;
    }
  }

  private async Task ValidateCurseForgeAsync()
  {
    if (string.IsNullOrEmpty(_app.SettingsModel.CurseForgeApiKey))
    {
      CurseForgeStatus = "";
      return;
    }

    Validating = true;
    try
    {
      await Task.Run(() => ((CurseForgeCatalog)_app.Catalogs.CurseForge).ValidateAsync(CancellationToken.None));
      CurseForgeStatus = "The key works.";
    }
    catch (Exception ex)
    {
      CurseForgeStatus = "Could not verify the key: " + ex.Message;
    }
    finally
    {
      Validating = false;
    }
  }

  private async Task RefreshNxmAsync() => NxmRegistered = await NxmHandler.IsRegisteredAsync();

  internal void ResolveGameDirectory()
  {
    var configured = _app.SettingsModel.GameDirectory;
    GameDirectory = SteamLibraryLocator.IsValheimDirectory(configured)
      ? configured!
      : SteamLibraryLocator.ForCurrentUser().FindValheim() ?? configured ?? "";
    Raise(nameof(GameFound));
    Raise(nameof(GameDirectoryIsCustom));
    Raise(nameof(GameFolderHasBepInEx));
  }
}
