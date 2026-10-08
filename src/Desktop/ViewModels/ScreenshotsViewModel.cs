using System.Globalization;
using CINE.LaunchHeim.Core.Game;
using CINE.LaunchHeim.Core.Logging;
using CINE.LaunchHeim.Core.Saves;
using CINE.LaunchHeim.Desktop.Hosting;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>The Screenshots page: what Steam's overlay saved while playing Valheim (<see cref="SteamScreenshots"/>).</summary>
/// <remarks>
/// Read again whenever the page opens and after the game exits, rather than watched: screenshots are
/// taken in the game, so they're new exactly when someone comes back from it. The folder is looked up
/// each time too, because Steam only creates it with the first screenshot.
/// </remarks>
public sealed class ScreenshotsViewModel(AppViewModel app) : ViewModel
{
  private List<ScreenshotViewModel> _items = [];
  private IReadOnlyList<string> _folders = [];
  private bool _isLoading;
  private bool _loaded;

  [NotifySignal]
  public List<ScreenshotViewModel> Items { get => _items; private set => Set(ref _items, value); }

  [NotifySignal]
  public int Count => _items.Count;

  [NotifySignal]
  public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

  /// <summary>Where Steam keeps them, for the subtitle and "Open folder"; empty before the first one.</summary>
  [NotifySignal]
  public string Folder => _folders.Count > 0 ? _folders[0] : "";

  public void Refresh() => _ = RefreshAsync();

  /// <summary>Only when the page was opened before; otherwise opening it reads them anyway.</summary>
  internal void RefreshIfLoaded()
  {
    if (_loaded)
    {
      Refresh();
    }
  }

  private async Task RefreshAsync()
  {
    if (IsLoading)
    {
      return;
    }

    IsLoading = true;
    try
    {
      var (folders, screenshots) = await Task.Run(() =>
      {
        var steam = SteamScreenshots.ForCurrentUser(SteamLibraryLocator.ForCurrentUser().SteamRoots);
        return (steam.Directories, steam.List());
      });
      _folders = folders;
      Items = screenshots.Select(s => new ScreenshotViewModel(s)).ToList();
      _loaded = true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      Log.Error("Reading Steam's screenshots failed", ex);
      app.Toast("error", "Could not read the screenshots", ex.Message);
    }
    finally
    {
      IsLoading = false;
      Raise(nameof(Count));
      Raise(nameof(Folder));
    }
  }

  public void OpenFolder()
  {
    if (Folder.Length > 0)
    {
      DesktopShell.Open(Folder);
    }
  }
}

/// <summary>One screenshot in the grid and the preview.</summary>
public sealed class ScreenshotViewModel(SteamScreenshot screenshot) : ViewModel
{
  /// <summary>A file:// URL for QML's Image; <see cref="Uri"/> escapes what a URL can't hold, such as '#'.</summary>
  [NotifySignal]
  public string Url => new Uri(screenshot.File).AbsoluteUri;

  /// <summary>
  /// Steam's thumbnail for the grid while the full picture loads. At 200 pixels wide it is too small to
  /// fill a card sharply on its own.
  /// </summary>
  [NotifySignal]
  public string ThumbnailUrl => screenshot.Thumbnail is { } thumbnail ? new Uri(thumbnail).AbsoluteUri : "";

  [NotifySignal]
  public string FileName => Path.GetFileName(screenshot.File);

  [NotifySignal]
  public string Taken => screenshot.TakenAt.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

  [NotifySignal]
  public string Ago => Format.Ago(new DateTimeOffset(screenshot.TakenAt));

  /// <summary>In the user's picture viewer.</summary>
  public void Open() => DesktopShell.Open(screenshot.File);

  public void ShowInFolder() => _ = DesktopShell.ShowInFolder(screenshot.File);
}
