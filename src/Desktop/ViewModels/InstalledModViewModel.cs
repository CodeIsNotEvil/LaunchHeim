using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Desktop.Hosting;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

public sealed class InstalledModViewModel : ViewModel
{
  private readonly InstanceViewModel _owner;
  private bool _enabled;
  private string _iconSource = "";
  private string _updateVersion = "";

  public InstalledModViewModel(InstanceViewModel owner, InstalledMod mod, IReadOnlyList<string> requiredBy, ImageCache images)
  {
    _owner = owner;
    Key = mod.Key;
    Name = mod.Name;
    Author = mod.Author;
    Version = mod.Version;
    Source = mod.Source.ToString().ToLowerInvariant();
    SourceLabel = mod.Source == ModSource.CurseForge ? "CurseForge" : mod.Source.ToString();
    IsLoader = mod.IsLoader;
    IsDependency = mod.InstalledAsDependency;
    WebsiteUrl = mod.WebsiteUrl ?? "";
    RequiredByText = requiredBy.Count == 0 ? "" : "Needed by " + string.Join(", ", requiredBy);
    FileCount = mod.Files.Count;
    _enabled = mod.Enabled;
    LoadIcon(mod.IconUrl, images);
  }

  [NotifySignal]
  public string Key { get; }

  [NotifySignal]
  public string Name { get; }

  [NotifySignal]
  public string Author { get; }

  [NotifySignal]
  public string Version { get; }

  [NotifySignal]
  public string Source { get; }

  [NotifySignal]
  public string SourceLabel { get; }

  [NotifySignal]
  public bool IsLoader { get; }

  [NotifySignal]
  public bool IsDependency { get; }

  [NotifySignal]
  public string WebsiteUrl { get; }

  [NotifySignal]
  public string RequiredByText { get; }

  [NotifySignal]
  public int FileCount { get; }

  [NotifySignal]
  public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

  [NotifySignal]
  public string IconSource { get => _iconSource; private set => Set(ref _iconSource, value); }

  [NotifySignal]
  public bool Enabled { get => _enabled; internal set => Set(ref _enabled, value); }

  [NotifySignal]
  public string UpdateVersion
  {
    get => _updateVersion;
    internal set
    {
      if (Set(ref _updateVersion, value))
      {
        Raise(nameof(HasUpdate));
      }
    }
  }

  [NotifySignal]
  public bool HasUpdate => _updateVersion.Length > 0;

  public void SetEnabled(bool enabled) => _owner.SetModEnabled(this, enabled);

  public void Remove() => _owner.RemoveMod(this);

  public void Update() => _owner.UpdateMod(this);

  public void OpenWebsite() => DesktopShell.Open(WebsiteUrl);

  private async void LoadIcon(string? url, ImageCache images)
  {
    if (await images.GetAsync(url) is { } local)
    {
      IconSource = local;
    }
  }
}

/// <remarks>
/// The properties never change, but QML warns about bindings on properties without a notify signal
/// when they are combined in an expression, so they declare one that is simply never raised.
/// </remarks>
public sealed class ConfigFileViewModel(string path, string instanceDirectory)
{
  [NotifySignal]
  public string Name { get; } = Path.GetRelativePath(Path.Combine(instanceDirectory, "BepInEx", "config"), path);

  [NotifySignal]
  public string SizeText { get; } = Format.Bytes(new FileInfo(path).Length);

  [NotifySignal]
  public string ModifiedText { get; } = Format.Ago(File.GetLastWriteTimeUtc(path));

  public void Open() => DesktopShell.Open(path);
}
