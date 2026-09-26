using CINE.LaunchHeim.Core.Catalogs;
using CINE.LaunchHeim.Core.Catalogs.Thunderstore;
using CINE.LaunchHeim.Core.Instances;
using CINE.LaunchHeim.Core.Mods;
using CINE.LaunchHeim.Desktop.Hosting;
using Markdig;
using Qml.Net;

namespace CINE.LaunchHeim.Desktop.ViewModels;

/// <summary>The mod browser: one search box over Thunderstore, Nexus Mods and CurseForge.</summary>
public sealed class BrowseViewModel : ViewModel
{
  private static readonly ModSort[] Sorts = [ModSort.Relevance, ModSort.Popular, ModSort.Downloads, ModSort.Updated, ModSort.Newest, ModSort.Name];
  private static readonly string[] SortNames = ["Best match", "Top rated", "Most downloaded", "Recently updated", "Newest", "Name"];

  private readonly AppViewModel _app;
  private CancellationTokenSource? _search;
  private ModSource _source = ModSource.Thunderstore;
  private string _query = "";
  private int _sortIndex;
  private int _page;
  private int _pageCount = 1;
  private int _totalCount;
  private bool _isLoading;
  private string _error = "";
  private List<ModCardViewModel> _results = [];
  private ModDetailsViewModel? _details;
  private string _targetInstanceId = "";
  private bool _hasSearched;

  public BrowseViewModel(AppViewModel app) => _app = app;

  [NotifySignal]
  public string Source => _source.ToString().ToLowerInvariant();

  [NotifySignal]
  public string SourceTitle => _source switch
  {
    ModSource.Nexus => "Nexus Mods",
    ModSource.CurseForge => "CurseForge",
    _ => "Thunderstore",
  };

  [NotifySignal]
  public string SortLabels => string.Join('\u001f', SortNames);

  [NotifySignal]
  public int SortIndex
  {
    get => _sortIndex;
    set
    {
      if (Set(ref _sortIndex, Math.Clamp(value, 0, Sorts.Length - 1)))
      {
        _page = 0;
        Search();
      }
    }
  }

  [NotifySignal]
  public string Query { get => _query; set => Set(ref _query, value ?? ""); }

  [NotifySignal]
  public List<ModCardViewModel> Results { get => _results; private set => Set(ref _results, value); }

  [NotifySignal]
  public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

  [NotifySignal]
  public string Error { get => _error; private set => Set(ref _error, value); }

  [NotifySignal]
  public int Page => _page;

  [NotifySignal]
  public int PageCount => _pageCount;

  [NotifySignal]
  public string ResultSummary => _totalCount switch
  {
    0 => IsLoading || SetupHint.Length > 0 ? "" : "No mods found",
    1 => "1 mod",
    _ => $"{_totalCount:N0} mods",
  };

  /// <summary>What the user has to set up before this source works, or empty.</summary>
  [NotifySignal]
  public string SetupHint => _app.Catalogs[_source].SetupHint ?? "";

  [NotifySignal]
  public string Notice => _source == ModSource.Nexus && !_app.Catalogs.Nexus.HasApiKey
    ? "Browsing Nexus needs no account. To install, add your personal API key in Settings."
    : "";

  [NotifySignal]
  public string IndexStatus => _source == ModSource.Thunderstore && _app.Catalogs.Thunderstore.Index.LastUpdated is { } updated
    ? $"{_app.Catalogs.Thunderstore.Index.Count:N0} packages · list updated {Format.Ago(updated)}"
    : "";

  [NotifySignal]
  public ModDetailsViewModel? Details { get => _details; private set => Set(ref _details, value); }

  [NotifySignal]
  public bool DetailsOpen => _details is not null;

  /// <summary>Instance names for the "Install into" box, separated by U+001F since names may hold anything.</summary>
  [NotifySignal]
  public string TargetNames => string.Join('\u001f', _app.InstanceList.Select(i => i.Name));

  [NotifySignal]
  public int TargetIndex => _app.InstanceList.FindIndex(i => i.Id == _targetInstanceId);

  [NotifySignal]
  public string TargetName => Target?.Name ?? "";

  internal InstanceViewModel? Target => _app.InstanceList.FirstOrDefault(i => i.Id == _targetInstanceId);

  public void SetSource(string source)
  {
    var parsed = Enum.TryParse<ModSource>(source, ignoreCase: true, out var value) ? value : ModSource.Thunderstore;
    if (parsed == _source && _hasSearched)
    {
      return;
    }

    _source = parsed;
    _page = 0;
    Raise(nameof(Source));
    Raise(nameof(SourceTitle));
    Raise(nameof(SetupHint));
    Raise(nameof(Notice));
    Raise(nameof(IndexStatus));
    Search();
  }

  public void SetTargetIndex(int index)
  {
    if (index >= 0 && index < _app.InstanceList.Count)
    {
      SetTarget(_app.InstanceList[index]);
    }
  }

  internal void SetTarget(InstanceViewModel? instance)
  {
    _targetInstanceId = instance?.Id ?? "";
    Raise(nameof(TargetIndex));
    Raise(nameof(TargetName));
    RefreshInstalledState();
  }

  internal void InstancesChanged()
  {
    if (Target is null)
    {
      _targetInstanceId = _app.InstanceList.FirstOrDefault()?.Id ?? "";
    }

    Raise(nameof(TargetNames));
    Raise(nameof(TargetIndex));
    Raise(nameof(TargetName));
    RefreshInstalledState();
  }

  /// <summary>Called when the page is first shown, so the list is not empty on arrival.</summary>
  public void EnsureLoaded()
  {
    if (!_hasSearched)
    {
      Search();
    }
  }

  public void SubmitQuery(string query)
  {
    Query = query;
    _page = 0;
    Search();
  }

  public void NextPage()
  {
    if (_page + 1 < _pageCount)
    {
      _page++;
      Search();
    }
  }

  public void PreviousPage()
  {
    if (_page > 0)
    {
      _page--;
      Search();
    }
  }

  public void RefreshIndex() => _ = RefreshIndexAsync();

  private async Task RefreshIndexAsync()
  {
    var activity = _app.BeginActivity("Updating the Thunderstore list");
    try
    {
      await Task.Run(() => _app.Catalogs.Thunderstore.Index.EnsureLoadedAsync(forceRefresh: true, CancellationToken.None));
      Raise(nameof(IndexStatus));
      _app.RefreshUpdates();
      Search();
    }
    catch (Exception ex)
    {
      _app.Toast("error", "Thunderstore", ex.Message);
    }
    finally
    {
      _app.EndActivity(activity);
    }
  }

  public void Search() => _ = SearchAsync();

  private async Task SearchAsync()
  {
    _hasSearched = true;
    _search?.Cancel();
    var cts = _search = new CancellationTokenSource();
    var catalog = _app.Catalogs[_source];

    Error = "";
    if (catalog.SetupHint is not null)
    {
      Results = [];
      SetPaging(0, 1, 0);
      return;
    }

    IsLoading = true;
    Raise(nameof(ResultSummary));
    try
    {
      var query = new ModSearchQuery(_query.Trim(), Sorts[_sortIndex], _page, 24, _app.Settings.ShowNsfw);
      var page = await Task.Run(() => catalog.SearchAsync(query, cts.Token), cts.Token);
      if (cts.IsCancellationRequested)
      {
        return;
      }

      Results = page.Items.Select(i => new ModCardViewModel(this, i, _app.Images)).ToList();
      SetPaging(page.Page, page.PageCount, page.TotalCount);
      RefreshInstalledState();
      Raise(nameof(IndexStatus));
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
    }
    catch (Exception ex)
    {
      if (!cts.IsCancellationRequested)
      {
        Results = [];
        SetPaging(0, 1, 0);
        Error = ex is HttpRequestException ? $"{SourceTitle} could not be reached. Check your connection." : ex.Message;
      }
    }
    finally
    {
      if (_search == cts)
      {
        IsLoading = false;
        Raise(nameof(ResultSummary));
      }
    }
  }

  private void SetPaging(int page, int pageCount, int total)
  {
    _page = page;
    _pageCount = Math.Max(1, pageCount);
    _totalCount = total;
    Raise(nameof(Page));
    Raise(nameof(PageCount));
    Raise(nameof(ResultSummary));
  }

  internal void OpenDetails(ModCardViewModel card)
  {
    var details = new ModDetailsViewModel(this, card.Summary, _app.Images);
    Details = details;
    Raise(nameof(DetailsOpen));
    details.Load(_app.Catalogs[card.Summary.Source]);
  }

  public void CloseDetails()
  {
    Details = null;
    Raise(nameof(DetailsOpen));
  }

  internal void Install(ModSummary mod, string? fileId, string label) =>
    _app.InstallFromCatalog(Target, new InstallRequest(mod.Source, mod.Id, fileId), label);

  internal InstalledMod? InstalledInTarget(ModSummary mod) =>
    Target?.Model.FindMod(InstalledMod.MakeKey(mod.Source, mod.Id));

  internal static bool IsOutdated(InstalledMod? installed, ModSummary mod) =>
    installed is { Version.Length: > 0 } && mod.LatestVersion is { Length: > 0 } latest && ModVersion.Compare(latest, installed.Version) > 0;

  internal void RefreshInstalledState()
  {
    foreach (var card in _results)
    {
      card.RefreshInstalled();
    }

    _details?.RefreshInstalled();
  }

  internal void SettingsChanged()
  {
    Raise(nameof(SetupHint));
    Raise(nameof(Notice));
    if (_hasSearched)
    {
      Search();
    }
  }
}

public sealed class ModCardViewModel : ViewModel
{
  private readonly BrowseViewModel _browse;
  private string _iconSource = "";
  private string _installedVersion = "";

  public ModCardViewModel(BrowseViewModel browse, ModSummary summary, ImageCache images)
  {
    _browse = browse;
    Summary = summary;
    LoadIcon(summary.IconUrl, images);
  }

  internal ModSummary Summary { get; }

  [NotifySignal]
  public string Name => Summary.Name;

  [NotifySignal]
  public string Author => Summary.Author;

  [NotifySignal]
  public string Description => Summary.Summary;

  [NotifySignal]
  public string Version => Summary.LatestVersion ?? "";

  [NotifySignal]
  public string DownloadsText => Format.Count(Summary.Downloads);

  [NotifySignal]
  public string LikesText => Format.Count(Summary.Likes);

  [NotifySignal]
  public string UpdatedText => Format.Ago(Summary.Updated);

  [NotifySignal]
  public string Category => Summary.Categories.FirstOrDefault() ?? "";

  [NotifySignal]
  public bool IsDeprecated => Summary.IsDeprecated;

  [NotifySignal]
  public string Initial => Summary.Name.Length > 0 ? Summary.Name[..1].ToUpperInvariant() : "?";

  [NotifySignal]
  public string IconSource { get => _iconSource; private set => Set(ref _iconSource, value); }

  [NotifySignal]
  public string InstalledVersion { get => _installedVersion; private set => Set(ref _installedVersion, value); }

  [NotifySignal]
  public bool IsInstalled => _installedVersion.Length > 0;

  /// <summary>Installed, but the site has a newer version than the one in the target instance.</summary>
  [NotifySignal]
  public bool HasUpdate { get; private set; }

  public void OpenDetails() => _browse.OpenDetails(this);

  public void Install() => _browse.Install(Summary, null, Summary.Name);

  public void OpenWebsite() => DesktopShell.Open(Summary.WebsiteUrl);

  internal void RefreshInstalled()
  {
    var installed = _browse.InstalledInTarget(Summary);
    InstalledVersion = installed is null ? "" : installed.Version.Length > 0 ? installed.Version : "installed";
    HasUpdate = BrowseViewModel.IsOutdated(installed, Summary);
    Raise(nameof(IsInstalled));
    Raise(nameof(HasUpdate));
  }

  private async void LoadIcon(string? url, ImageCache images)
  {
    if (await images.GetAsync(url) is { } local)
    {
      IconSource = local;
    }
  }
}

public sealed class ModDetailsViewModel : ViewModel
{
  private const int DescriptionWidth = 560;
  private static readonly MarkdownPipeline Markdown = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

  private readonly BrowseViewModel _browse;
  private readonly ImageCache _images;
  private string _iconSource = "";
  private string _descriptionHtml = "";
  private bool _isLoading = true;
  private string _error = "";
  private List<ModFileViewModel> _files = [];
  private string _installedVersion = "";

  public ModDetailsViewModel(BrowseViewModel browse, ModSummary summary, ImageCache images)
  {
    _browse = browse;
    _images = images;
    Summary = summary;
    _ = LoadIconAsync();
  }

  internal ModSummary Summary { get; }

  [NotifySignal]
  public string Name => Summary.Name;

  [NotifySignal]
  public string Author => Summary.Author;

  [NotifySignal]
  public string ShortDescription => Summary.Summary;

  [NotifySignal]
  public string Version => Summary.LatestVersion ?? "";

  [NotifySignal]
  public string DownloadsText => Format.Count(Summary.Downloads);

  [NotifySignal]
  public string LikesText => Format.Count(Summary.Likes);

  [NotifySignal]
  public string UpdatedText => Format.Ago(Summary.Updated);

  [NotifySignal]
  public string Categories => string.Join(" · ", Summary.Categories.Take(4));

  [NotifySignal]
  public string WebsiteUrl => Summary.WebsiteUrl;

  [NotifySignal]
  public string Source => Summary.Source.ToString().ToLowerInvariant();

  [NotifySignal]
  public string Initial => Summary.Name.Length > 0 ? Summary.Name[..1].ToUpperInvariant() : "?";

  [NotifySignal]
  public string IconSource { get => _iconSource; private set => Set(ref _iconSource, value); }

  [NotifySignal]
  public string DescriptionHtml { get => _descriptionHtml; private set => Set(ref _descriptionHtml, value); }

  [NotifySignal]
  public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

  [NotifySignal]
  public string Error { get => _error; private set => Set(ref _error, value); }

  [NotifySignal]
  public List<ModFileViewModel> Files { get => _files; private set => Set(ref _files, value); }

  [NotifySignal]
  public string InstalledVersion { get => _installedVersion; private set => Set(ref _installedVersion, value); }

  [NotifySignal]
  public bool IsInstalled => _installedVersion.Length > 0;

  [NotifySignal]
  public bool HasUpdate { get; private set; }

  public void Install() => _browse.Install(Summary, null, Summary.Name);

  public void OpenWebsite() => DesktopShell.Open(Summary.WebsiteUrl);

  public void OpenLink(string url) => DesktopShell.Open(url);

  internal void InstallFile(ModFileViewModel file) => _browse.Install(Summary, file.Id, $"{Summary.Name} {file.Version}");

  internal async void Load(IModCatalog catalog)
  {
    try
    {
      var details = await Task.Run(() => catalog.GetDetailsAsync(Summary.Id, CancellationToken.None));
      var html = details.Format switch
      {
        DescriptionFormat.Markdown => Markdig.Markdown.ToHtml(details.Description, Markdown),
        DescriptionFormat.PlainText => System.Net.WebUtility.HtmlEncode(details.Description).Replace("\n", "<br/>"),
        _ => details.Description,
      };

      // Show the text right away; images follow once they are downloaded.
      Files = details.Files.Take(60).Select(f => new ModFileViewModel(this, f)).ToList();
      DescriptionHtml = WithoutImages(html);
      IsLoading = false;
      RefreshInstalled();
      DescriptionHtml = await Task.Run(() => _images.LocalizeImagesAsync(html, DescriptionWidth));
    }
    catch (Exception ex)
    {
      Error = ex.Message;
      IsLoading = false;
    }
  }

  internal void RefreshInstalled()
  {
    var installed = _browse.InstalledInTarget(Summary);
    InstalledVersion = installed is null ? "" : installed.Version.Length > 0 ? installed.Version : "installed";
    HasUpdate = BrowseViewModel.IsOutdated(installed, Summary);
    Raise(nameof(IsInstalled));
    Raise(nameof(HasUpdate));
    foreach (var file in _files)
    {
      file.IsInstalled = installed is not null && (installed.FileId == file.Id || (installed.FileId is null && installed.Version == file.Version));
    }
  }

  private static string WithoutImages(string html) =>
    System.Text.RegularExpressions.Regex.Replace(html, "<img\\b[^>]*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

  private async Task LoadIconAsync()
  {
    if (await _images.GetAsync(Summary.IconUrl) is { } local)
    {
      IconSource = local;
    }
  }
}

public sealed class ModFileViewModel(ModDetailsViewModel details, ModFileInfo file) : ViewModel
{
  private bool _isInstalled;

  [NotifySignal]
  public string Id => file.Id;

  [NotifySignal]
  public string Name => file.Name;

  [NotifySignal]
  public string Version => file.Version;

  [NotifySignal]
  public string DateText => Format.Date(file.Date);

  [NotifySignal]
  public string SizeText => Format.Bytes(file.SizeBytes);

  [NotifySignal]
  public string Category => file.Category;

  [NotifySignal]
  public string DescriptionHtml => file.Description ?? "";

  [NotifySignal]
  public bool IsRecommended => file.IsRecommended;

  [NotifySignal]
  public bool IsInstalled { get => _isInstalled; set => Set(ref _isInstalled, value); }

  public void Install() => details.InstallFile(this);
}
