using CINE.LaunchHeim.Desktop.Theming;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>The Plasma color scheme as QML color strings, updated live when kdeglobals changes.</summary>
public sealed class ThemeViewModel : ViewModel, IDisposable
{
  private readonly FileSystemWatcher? _watcher;
  private KdeColorScheme _scheme;
  private CancellationTokenSource? _debounce;

  public ThemeViewModel()
  {
    _scheme = KdeColorScheme.Load(KdeColorScheme.ConfigPath);

    var directory = Path.GetDirectoryName(KdeColorScheme.ConfigPath)!;
    if (Directory.Exists(directory))
    {
      // KDE rewrites kdeglobals by replacing the file, so watch the folder, not the file.
      _watcher = new FileSystemWatcher(directory, "kdeglobals")
      {
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
        EnableRaisingEvents = true,
      };
      _watcher.Changed += (_, _) => ScheduleReload();
      _watcher.Created += (_, _) => ScheduleReload();
      _watcher.Renamed += (_, _) => ScheduleReload();
    }
  }

  [NotifySignal]
  public string Window => _scheme.Window;

  [NotifySignal]
  public string WindowAlternate => _scheme.WindowAlternate;

  [NotifySignal]
  public string View => _scheme.View;

  [NotifySignal]
  public string ViewAlternate => _scheme.ViewAlternate;

  [NotifySignal]
  public string Button => _scheme.Button;

  [NotifySignal]
  public string Header => _scheme.Header;

  [NotifySignal]
  public string Text => _scheme.Text;

  [NotifySignal]
  public string TextMuted => _scheme.TextInactive;

  [NotifySignal]
  public string Accent => _scheme.Accent;

  [NotifySignal]
  public string AccentText => _scheme.AccentText;

  [NotifySignal]
  public string Positive => _scheme.Positive;

  [NotifySignal]
  public string Negative => _scheme.Negative;

  [NotifySignal]
  public string Neutral => _scheme.Neutral;

  [NotifySignal]
  public string Link => _scheme.Link;

  [NotifySignal]
  public string FontFamily => _scheme.FontFamily;

  [NotifySignal]
  public double FontPointSize => _scheme.FontPointSize;

  [NotifySignal]
  public bool IsDark => _scheme.IsDark;

  private static readonly string[] ColorProperties =
  [
    nameof(Window), nameof(WindowAlternate), nameof(View), nameof(ViewAlternate), nameof(Button), nameof(Header),
    nameof(Text), nameof(TextMuted), nameof(Accent), nameof(AccentText), nameof(Positive), nameof(Negative),
    nameof(Neutral), nameof(Link), nameof(FontFamily), nameof(FontPointSize), nameof(IsDark),
  ];

  private void ScheduleReload()
  {
    // The watcher fires on a thread-pool thread and several times per save. Collapse the burst and
    // hop to the Qt thread, where property signals may be raised.
    _debounce?.Cancel();
    var cts = _debounce = new CancellationTokenSource();
    Program.Dispatch(async () =>
    {
      try
      {
        await Task.Delay(300, cts.Token);
      }
      catch (TaskCanceledException)
      {
        return;
      }

      _scheme = KdeColorScheme.Load(KdeColorScheme.ConfigPath);
      foreach (var property in ColorProperties)
      {
        Raise(property);
      }
    });
  }

  public void Dispose() => _watcher?.Dispose();
}
