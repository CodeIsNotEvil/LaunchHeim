using CINE.LaunchHeim.Core.Mods;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>A running download or install, shown in the sidebar with its progress.</summary>
public sealed class ActivityItem : ViewModel
{
  private string _title;
  private string _detail = "";
  private double _progress = -1;

  public ActivityItem(string title) => _title = title;

  [NotifySignal]
  public string Title { get => _title; set => Set(ref _title, value); }

  [NotifySignal]
  public string Detail { get => _detail; set => Set(ref _detail, value); }

  /// <summary>0 to 1, or -1 while the total is unknown (QML shows an indeterminate bar).</summary>
  [NotifySignal]
  public double Progress { get => _progress; set => Set(ref _progress, value); }

  /// <summary>
  /// Creates the progress sink handed to background work. Progress&lt;T&gt; captures the Qt thread's
  /// SynchronizationContext here, so reports from the thread pool arrive back on the Qt thread,
  /// the only place Qml.Net allows property signals to be raised.
  /// </summary>
  public IProgress<InstallProgress> CreateProgress() => new Progress<InstallProgress>(Apply);

  private void Apply(InstallProgress value)
  {
    Title = value.ModName;
    Detail = value.Stage;
    Progress = value.Fraction ?? -1;
  }
}

public sealed class ToastItem(string kind, string title, string message, string actionLabel, string actionUrl, Action<ToastItem> dismiss) : ViewModel
{
  /// <summary>info, success or error.</summary>
  [NotifySignal]
  public string Kind { get; } = kind;

  [NotifySignal]
  public string Title { get; } = title;

  [NotifySignal]
  public string Message { get; } = message;

  [NotifySignal]
  public string ActionLabel { get; } = actionLabel;

  [NotifySignal]
  public string ActionUrl { get; } = actionUrl;

  public void Dismiss() => dismiss(this);

  public void RunAction()
  {
    Hosting.DesktopShell.Open(ActionUrl);
    Dismiss();
  }
}

/// <summary>Shown when a site will not let LaunchHeim download a file by itself.</summary>
public sealed class BrowserPromptViewModel : ViewModel
{
  private bool _visible;
  private string _title = "";
  private string _reason = "";
  private string _url = "";
  private bool _waitsForNxm;

  [NotifySignal]
  public bool Visible { get => _visible; set => Set(ref _visible, value); }

  [NotifySignal]
  public string Title { get => _title; set => Set(ref _title, value); }

  [NotifySignal]
  public string Reason { get => _reason; set => Set(ref _reason, value); }

  [NotifySignal]
  public string Url { get => _url; set => Set(ref _url, value); }

  /// <summary>True for Nexus, where the page hands the download back to LaunchHeim via nxm://.</summary>
  [NotifySignal]
  public bool WaitsForNxm { get => _waitsForNxm; set => Set(ref _waitsForNxm, value); }

  public void Open()
  {
    Hosting.DesktopShell.Open(Url);
    Visible = false;
  }

  public void Close() => Visible = false;
}
